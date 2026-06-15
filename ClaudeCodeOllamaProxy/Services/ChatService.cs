using System.Text;
using Claude.AgentSdk;
using Claude.AgentSdk.Extensions;
using Claude.AgentSdk.Messages;
using ClaudeCodeOllamaProxy.Endpoints;
using ClaudeCodeOllamaProxy.Infrastructure;
using ClaudeCodeOllamaProxy.Models;

namespace ClaudeCodeOllamaProxy.Services;

/// <summary>
/// Handles the read-only (Ask / Plan) chat path: flatten the conversation, run a stateless
/// one-shot Claude query in Plan permission mode, and stream the assistant text back as OpenAI SSE
/// (or a single JSON object when streaming is disabled).
/// </summary>
public sealed class ChatService
{
    private readonly ClaudeSessionFactory _sessionFactory;
    private readonly ImageMaterializer _imageMaterializer;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        ClaudeSessionFactory sessionFactory,
        ImageMaterializer imageMaterializer,
        ILogger<ChatService> logger)
    {
        _sessionFactory = sessionFactory;
        _imageMaterializer = imageMaterializer;
        _logger = logger;
    }

    public async Task RunAsync(
        HttpContext ctx,
        OpenAiChatRequest request,
        string baseModel,
        string? effort,
        string echoModel,
        string workingDirectory,
        CancellationToken ct)
    {
        var flat = OpenAiMessageMapper.Flatten(request.Messages);
        var prompt = flat.Prompt + _imageMaterializer.Materialize(flat.Images);
        var fullPrompt = OpenAiMessageMapper.CombineSystemAndPrompt(flat.SystemPrompt, prompt);

        _logger.LogDebug("Ask system prompt: {SystemPrompt}", Truncate(flat.SystemPrompt));
        _logger.LogDebug("Ask prompt: {Prompt}", Truncate(prompt));

        var options = _sessionFactory.BuildOptions(
            model: baseModel,
            workingDirectory: workingDirectory,
            permissionMode: PermissionMode.Plan,
            effort: effort);

        var streaming = request.Stream ?? true;
        if (streaming)
            await RunStreamingAsync(ctx, request, echoModel, fullPrompt, options, ct);
        else
            await RunNonStreamingAsync(ctx, request, echoModel, fullPrompt, options, ct);
    }

    private async Task RunStreamingAsync(
        HttpContext ctx, OpenAiChatRequest request, string model, string prompt, ClaudeAgentOptions options, CancellationToken ct)
    {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        var writer = new SseWriter(ctx.Response, NewId(), model, Now());

        try
        {
            // Use a bidirectional session so the prompt goes over stdin, not as a --print CLI arg
            // (which would overflow the command-line length limit on large conversations).
            await using var client = _sessionFactory.CreateClient(options);
            await using var session = await client.CreateSessionAsync(ct);
            await session.SendAsync(prompt, null!, ct);
            await foreach (var msg in session.ReceiveResponseAsync(ct))
            {
                if (msg is AssistantMessage am)
                {
                    var text = am.GetText();
                    if (!string.IsNullOrEmpty(text))
                        await writer.WriteContentAsync(text, ct);
                }
                else if (msg is ResultMessage)
                {
                    break;
                }
            }

            await writer.FinishAsync("stop", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read-only streaming chat failed; emitting error to client and finishing.");
            await writer.WriteContentAsync($"\n\n[proxy error: {ex.Message}]", ct);
            await writer.FinishAsync("stop", ct);
        }
    }

    private async Task RunNonStreamingAsync(
        HttpContext ctx, OpenAiChatRequest request, string model, string prompt, ClaudeAgentOptions options, CancellationToken ct)
    {
        var sb = new StringBuilder();
        try
        {
            await using var client = _sessionFactory.CreateClient(options);
            await using var session = await client.CreateSessionAsync(ct);
            await session.SendAsync(prompt, null!, ct);
            await foreach (var msg in session.ReceiveResponseAsync(ct))
            {
                if (msg is AssistantMessage am)
                    sb.Append(am.GetText());
                else if (msg is ResultMessage)
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read-only non-streaming chat failed.");
            sb.Append($"[proxy error: {ex.Message}]");
        }

        var response = new OpenAiChatResponse
        {
            Id = NewId(),
            Created = Now(),
            Model = model,
            Choices = new List<OpenAiResponseChoice>
            {
                new()
                {
                    Index = 0,
                    Message = new OpenAiResponseMessage { Role = "assistant", Content = sb.ToString() },
                    FinishReason = "stop",
                },
            },
        };

        await ctx.Response.WriteAsJsonAsync(response, JsonDefaults.Options, ct);
    }

    internal static string NewId() => "chatcmpl-" + Guid.NewGuid().ToString("N");
    internal static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string Truncate(string? s, int max = 4000)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + $"… (+{s.Length - max} chars)";
    }
}
