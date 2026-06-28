using System.Security.Cryptography;
using System.Text;
using Claude.AgentSdk.Exceptions;
using ClaudeCodeOllamaProxy.Bridge;
using ClaudeCodeOllamaProxy.Infrastructure;
using ClaudeCodeOllamaProxy.Models;
using ClaudeCodeOllamaProxy.Services;
using Microsoft.Extensions.Options;

namespace ClaudeCodeOllamaProxy.Endpoints;

/// <summary>
/// The OpenAI <c>/v1/chat/completions</c> endpoint Copilot uses for chat. Ask/Plan requests run the
/// read-only <see cref="ChatService"/>; Agent requests run the <see cref="ToolBridge"/>, either
/// starting a new agent task or continuing a parked one when the request carries tool results.
/// </summary>
public static class OpenAiChatEndpoint
{
    public static void MapOpenAiChatEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", HandleAsync);
    }

    private static async Task HandleAsync(
        HttpContext ctx,
        OpenAiChatRequest? request,
        ChatService chatService,
        ModeDetector modeDetector,
        WorkingDirectoryResolver workingDirectoryResolver,
        ImageMaterializer imageMaterializer,
        ModelCatalog catalog,
        BridgeRegistry registry,
        ClaudeSessionFactory sessionFactory,
        ILoggerFactory loggerFactory,
        IOptions<ProxyOptions> options,
        ILogger<ChatService> logger,
        CancellationToken ct)
    {
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

        try
        {
            // Continuation: the request carries tool results for a parked bridge session.
            if (TryGetContinuationBridge(request, registry, out var bridge))
            {
                await ContinueBridgeAsync(ctx, request, registry, loggerFactory, bridge!, requestedId, ct);
                return;
            }

            var mode = modeDetector.Detect(request.Tools);
            logger.LogInformation("Chat request: model={Model}, base={Base}, effort={Effort}, mode={Mode}, tools={ToolCount}.",
                requestedId, selection.BaseModel, selection.Effort, mode, request.Tools?.Count ?? 0);

            if (mode == ChatMode.Agent && request.Tools is { Count: > 0 })
            {
                await StartBridgeAsync(ctx, request, sessionFactory, registry, loggerFactory,
                    workingDirectoryResolver, imageMaterializer, options.Value,
                    selection.BaseModel, selection.Effort, requestedId, ct);
                return;
            }

            // Ask / Plan: read-only.
            var workingDir = workingDirectoryResolver.Resolve(TextFragments(request));
            await chatService.RunAsync(ctx, request, selection.BaseModel, selection.Effort, requestedId, workingDir, ct);
        }
        catch (CliNotFoundException ex)
        {
            logger.LogError(ex, "claude CLI not found while handling chat request.");
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    error = "The 'claude' CLI was not found. Install Claude Code and run 'claude login', then restart the proxy.",
                }, JsonDefaults.Options, ct);
            }
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            logger.LogError(ex, "Unhandled error before response started.");
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, JsonDefaults.Options, ct);
        }
    }

    private static async Task StartBridgeAsync(
        HttpContext ctx,
        OpenAiChatRequest request,
        ClaudeSessionFactory sessionFactory,
        BridgeRegistry registry,
        ILoggerFactory loggerFactory,
        WorkingDirectoryResolver workingDirectoryResolver,
        ImageMaterializer imageMaterializer,
        ProxyOptions options,
        string baseModel,
        string? effort,
        string echoModel,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ClaudeCodeOllamaProxy.Bridge");
        var flat = OpenAiMessageMapper.Flatten(request.Messages);
        var prompt = flat.Prompt + imageMaterializer.Materialize(flat.Images);
        var workingDir = workingDirectoryResolver.Resolve(new[] { flat.SystemPrompt, flat.Prompt });

        // One bridge (one Claude session) per conversation: supersede any earlier bridge for this
        // chat so a resend/new-turn never runs two concurrent sessions editing the same workspace.
        var conversationKey = ConversationKey(request);
        var existing = registry.GetBridgeByConversation(conversationKey);
        if (existing is not null)
        {
            logger.LogInformation("Superseding existing bridge for conversation {Conv}.", conversationKey[..8]);
            await existing.DisposeAsync();
        }

        StartSse(ctx);
        var writer = new SseWriter(ctx.Response, ChatService.NewId(), echoModel, ChatService.Now());

        var bridge = await ToolBridge.StartAsync(
            sessionFactory, registry, loggerFactory,
            model: baseModel,
            workingDirectory: workingDir,
            systemPrompt: flat.SystemPrompt,
            prompt: prompt,
            tools: request.Tools!,
            bridgeTerminal: options.Claude.BridgeTerminalTool,
            effort: effort,
            conversationKey: conversationKey,
            ct: ct);

        await DrainAndDisposeAsync(bridge, writer, logger, conversationKey, ct);
    }

    private static async Task ContinueBridgeAsync(
        HttpContext ctx,
        OpenAiChatRequest request,
        BridgeRegistry registry,
        ILoggerFactory loggerFactory,
        ToolBridge bridge,
        string echoModel,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ClaudeCodeOllamaProxy.Bridge");

        // Resolve every still-pending tool result carried by this request.
        foreach (var msg in request.Messages)
        {
            if (string.Equals(msg.Role, "tool", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(msg.ToolCallId))
            {
                var content = OpenAiMessageMapper.TextOf(msg.Content);
                registry.TryResolve(msg.ToolCallId!, content);
            }
        }

        StartSse(ctx);
        var writer = new SseWriter(ctx.Response, ChatService.NewId(), echoModel, ChatService.Now());

        await DrainAndDisposeAsync(bridge, writer, logger, bridge.ConversationKey, ct);
    }

    /// <summary>Drain the bridge to the response, disposing it when the turn completes, the client
    /// disconnects (HTTP 499), or an error occurs — so abandoned sessions never linger.</summary>
    private static async Task DrainAndDisposeAsync(ToolBridge bridge, SseWriter writer, ILogger logger, string conversationKey, CancellationToken ct)
    {
        try
        {
            var outcome = await bridge.DrainAsync(writer, ct);
            if (outcome == DrainOutcome.Completed)
                await bridge.DisposeAsync();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Client disconnected (request cancelled / HTTP 499) during agent turn; disposing bridge {Conv}.",
                conversationKey.Length >= 8 ? conversationKey[..8] : conversationKey);
            await bridge.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent bridge drain failed; disposing bridge {Conv}.",
                conversationKey.Length >= 8 ? conversationKey[..8] : conversationKey);
            await bridge.DisposeAsync();
        }
    }

    /// <summary>
    /// Stable per-conversation key: the first user message (the original ask). It stays constant across
    /// every turn of one chat and differs between chats. We deliberately avoid the system prompt — VS
    /// can inject volatile content (dates) there, which would change the key every request and defeat
    /// the one-bridge-per-conversation guarantee.
    /// </summary>
    private static string ConversationKey(OpenAiChatRequest request)
    {
        var firstUser = request.Messages.FirstOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        var anchor = firstUser is not null ? OpenAiMessageMapper.TextOf(firstUser.Content) : "";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(anchor)));
    }

    private static bool TryGetContinuationBridge(OpenAiChatRequest request, BridgeRegistry registry, out ToolBridge? bridge)
    {
        bridge = null;
        foreach (var msg in request.Messages)
        {
            if (string.Equals(msg.Role, "tool", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(msg.ToolCallId))
            {
                var found = registry.FindBridge(msg.ToolCallId!);
                if (found is not null)
                {
                    bridge = found;
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string?> TextFragments(OpenAiChatRequest request)
    {
        foreach (var msg in request.Messages)
            yield return OpenAiMessageMapper.TextOf(msg.Content);
    }

    private static void StartSse(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
    }
}
