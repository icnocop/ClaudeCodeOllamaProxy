using System.Text;
using System.Text.Json;
using Claude.AgentSdk;
using Claude.AgentSdk.Extensions;
using Claude.AgentSdk.Messages;
using ClaudeCodeOllamaProxy.Infrastructure;
using ClaudeCodeOllamaProxy.Models;
using ClaudeCodeOllamaProxy.Services;

namespace ClaudeCodeOllamaProxy.Endpoints;

/// <summary>
/// The native Ollama <c>/api/chat</c> endpoint (NDJSON), provided for non-Copilot Ollama clients and
/// smoke testing. This is a read-only, text-only path — it does not run the Copilot tool-call bridge.
/// </summary>
public static class OllamaChatEndpoint
{
    public static void MapOllamaChatEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", HandleAsync);
    }

    private static async Task HandleAsync(
        HttpContext ctx,
        OllamaChatRequest? request,
        ClaudeSessionFactory sessionFactory,
        ImageMaterializer imageMaterializer,
        WorkingDirectoryResolver workingDirectoryResolver,
        ModelCatalog catalog,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ClaudeCodeOllamaProxy.OllamaChat");

        if (request is null || request.Messages.Count == 0)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { error = "messages are required" }, JsonDefaults.Options, ct);
            return;
        }

        var requestedId = string.IsNullOrWhiteSpace(request.Model)
            ? await catalog.DefaultModelIdAsync(ct)
            : request.Model!;
        var selection = await catalog.ResolveSelectionAsync(requestedId, ct);
        var model = requestedId; // echoed back to the client in NDJSON chunks

        var (systemPrompt, transcript, images) = Flatten(request.Messages);
        var prompt = transcript + imageMaterializer.Materialize(images);
        var fullPrompt = OpenAiMessageMapper.CombineSystemAndPrompt(systemPrompt, prompt);
        var workingDir = workingDirectoryResolver.Resolve(new[] { systemPrompt, transcript });

        var options = sessionFactory.BuildOptions(
            model: selection.BaseModel,
            workingDirectory: workingDir,
            permissionMode: PermissionMode.Plan,
            effort: selection.Effort);

        var streaming = request.Stream ?? true;
        var createdAt = DateTimeOffset.UtcNow.ToString("o");

        try
        {
            // Bidirectional session: prompt over stdin, not a --print CLI arg (avoids the
            // command-line length limit on large conversations / system prompts).
            await using var client = sessionFactory.CreateClient(options);
            await using var session = await client.CreateSessionAsync(ct);
            await session.SendAsync(fullPrompt, null!, ct);

            if (streaming)
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.ContentType = "application/x-ndjson";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                await foreach (var msg in session.ReceiveResponseAsync(ct))
                {
                    if (msg is AssistantMessage am)
                    {
                        var text = am.GetText();
                        if (!string.IsNullOrEmpty(text))
                            await WriteChunkAsync(ctx.Response, new OllamaChatResponseChunk
                            {
                                Model = model!,
                                CreatedAt = createdAt,
                                Message = new OllamaChatMessage { Role = "assistant", Content = text },
                                Done = false,
                            }, ct);
                    }
                    else if (msg is ResultMessage)
                    {
                        break;
                    }
                }

                await WriteChunkAsync(ctx.Response, new OllamaChatResponseChunk
                {
                    Model = model!,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                    Message = new OllamaChatMessage { Role = "assistant", Content = "" },
                    Done = true,
                    DoneReason = "stop",
                }, ct);
            }
            else
            {
                var sb = new StringBuilder();
                await foreach (var msg in session.ReceiveResponseAsync(ct))
                {
                    if (msg is AssistantMessage am) sb.Append(am.GetText());
                    else if (msg is ResultMessage) break;
                }

                await ctx.Response.WriteAsJsonAsync(new OllamaChatResponseChunk
                {
                    Model = model!,
                    CreatedAt = createdAt,
                    Message = new OllamaChatMessage { Role = "assistant", Content = sb.ToString() },
                    Done = true,
                    DoneReason = "stop",
                }, JsonDefaults.Options, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ollama /api/chat failed.");
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, JsonDefaults.Options, ct);
            }
        }
    }

    private static (string System, string Transcript, IReadOnlyList<ImageReference> Images) Flatten(
        IReadOnlyList<OllamaChatMessage> messages)
    {
        var system = new StringBuilder();
        var transcript = new StringBuilder();
        var images = new List<ImageReference>();

        foreach (var msg in messages)
        {
            if (msg.Images is { Count: > 0 })
                images.AddRange(msg.Images.Select(i => new ImageReference(i)));

            switch (msg.Role?.ToLowerInvariant())
            {
                case "system":
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        if (system.Length > 0) system.Append("\n\n");
                        system.Append(msg.Content);
                    }
                    break;
                case "assistant":
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                        transcript.Append("Assistant: ").AppendLine(msg.Content).AppendLine();
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                        transcript.Append("User: ").AppendLine(msg.Content).AppendLine();
                    break;
            }
        }

        return (system.ToString().Trim(), transcript.ToString().Trim(), images);
    }

    private static async Task WriteChunkAsync(HttpResponse response, OllamaChatResponseChunk chunk, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(chunk, JsonDefaults.Options);
        await response.WriteAsync(json + "\n", Encoding.UTF8, ct);
        await response.Body.FlushAsync(ct);
    }
}
