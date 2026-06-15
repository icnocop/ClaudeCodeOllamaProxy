using System.Text.Json;
using System.Threading.Channels;
using Claude.AgentSdk;
using Claude.AgentSdk.Extensions;
using Claude.AgentSdk.Messages;
using Claude.AgentSdk.Tools;
using ClaudeCodeOllamaProxy.Endpoints;
using ClaudeCodeOllamaProxy.Models;
using ClaudeCodeOllamaProxy.Services;

namespace ClaudeCodeOllamaProxy.Bridge;

/// <summary>
/// A live Claude session for one Copilot agent task. Copilot's edit tools are exposed to Claude as
/// in-process MCP tools; when Claude calls one, the handler emits an OpenAI tool_call to Copilot,
/// blocks until Copilot's follow-up request delivers the result, then returns it to Claude. The
/// session is held in memory across the tool loop and torn down when the task completes.
/// </summary>
public sealed class ToolBridge : IAsyncDisposable
{
    // Claude's own read-only tools remain available; its built-in mutating tools are disabled so all
    // edits/terminal must flow through the bridged Copilot tools (which Copilot applies via its UI).
    private static readonly string[] ReadOnlyBuiltins = { "Read", "Grep", "Glob", "LS", "WebFetch", "WebSearch" };
    private static readonly string[] EditBuiltins = { "Write", "Edit", "MultiEdit", "NotebookEdit" };

    private readonly BridgeRegistry _registry;
    private readonly ILogger _logger;
    private readonly Channel<BridgeEvent> _events = Channel.CreateUnbounded<BridgeEvent>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _drainLock = new(1, 1);
    private readonly CancellationTokenSource _sessionCts = new();

    private ClaudeAgentClient _client = null!;
    private ClaudeAgentSession _session = null!;
    private Task _pumpTask = Task.CompletedTask;
    private bool _disposed;

    public string Model { get; }
    public string ConversationKey { get; }

    private ToolBridge(BridgeRegistry registry, ILogger logger, string model, string conversationKey)
    {
        _registry = registry;
        _logger = logger;
        Model = model;
        ConversationKey = conversationKey;
    }

    public static async Task<ToolBridge> StartAsync(
        ClaudeSessionFactory sessionFactory,
        BridgeRegistry registry,
        ILoggerFactory loggerFactory,
        string model,
        string workingDirectory,
        string? systemPrompt,
        string prompt,
        IReadOnlyList<OpenAiTool> tools,
        bool bridgeTerminal,
        string? effort,
        string conversationKey,
        CancellationToken ct)
    {
        var bridge = new ToolBridge(registry, loggerFactory.CreateLogger<ToolBridge>(), model, conversationKey);
        registry.RegisterBridge(conversationKey, bridge);

        var toolServer = CopilotToolServerBuilder.Build(tools, bridge.HandleToolAsync);

        var allowed = new List<string>();
        allowed.AddRange(toolServer.AllowedToolNames);
        allowed.AddRange(ReadOnlyBuiltins);

        var disallowed = new List<string>(EditBuiltins);
        if (bridgeTerminal) disallowed.Add("Bash");

        var options = sessionFactory.BuildOptions(
            model: model,
            workingDirectory: workingDirectory,
            permissionMode: PermissionMode.AcceptEdits,
            allowedTools: allowed,
            disallowedTools: disallowed,
            mcpServers: new Dictionary<string, McpServerConfig>
            {
                [CopilotToolServerBuilder.ServerName] = new McpSdkServerConfig
                {
                    Name = CopilotToolServerBuilder.ServerName,
                    Instance = toolServer.Server,
                },
            },
            effort: effort);

        // Fold the (potentially huge) system prompt into the stdin prompt — never a CLI arg.
        var fullPrompt = OpenAiMessageMapper.CombineSystemAndPrompt(systemPrompt, prompt);

        bridge._logger.LogDebug("Bridge starting: model={Model}, effort={Effort}, tools={ToolCount}, conv={Conv}.",
            model, effort, tools.Count, conversationKey);
        bridge._logger.LogDebug("Bridge system prompt: {SystemPrompt}", Truncate(systemPrompt));
        bridge._logger.LogDebug("Bridge prompt: {Prompt}", Truncate(prompt));

        bridge._client = sessionFactory.CreateClient(options);
        bridge._session = await bridge._client.CreateSessionAsync(ct);
        bridge._pumpTask = bridge.PumpAsync(fullPrompt);
        return bridge;
    }

    private static string Truncate(string? s, int max = 4000)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + $"… (+{s.Length - max} chars)";
    }

    /// <summary>MCP tool handler: surface the call to Copilot and block until its result arrives.</summary>
    private async Task<ToolResult> HandleToolAsync(string toolName, JsonElement args, CancellationToken ct)
    {
        var callId = _registry.NextCallId();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.RegisterPending(callId, this, completion);

        _logger.LogInformation("Claude invoked bridged tool {Tool} -> emitting tool_call {CallId}.", toolName, callId);
        _logger.LogDebug("Tool {Tool} args {CallId}: {Args}", toolName, callId, Truncate(args.GetRawText()));
        await _events.Writer.WriteAsync(new ToolCallEvent(callId, toolName, args.GetRawText()), _sessionCts.Token);

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _sessionCts.Token);
            var result = await completion.Task.WaitAsync(linked.Token);
            return ToolResult.Text(result);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Error("Tool call was cancelled before Copilot returned a result.");
        }
    }

    private async Task PumpAsync(string prompt)
    {
        var ct = _sessionCts.Token;
        try
        {
            await _session.SendAsync(prompt, null!, ct);

            await foreach (var msg in _session.ReceiveResponseAsync(ct))
            {
                if (msg is AssistantMessage am)
                {
                    foreach (var block in am.GetTextBlocks())
                    {
                        if (!string.IsNullOrEmpty(block.Text))
                            await _events.Writer.WriteAsync(new TextDeltaEvent(block.Text), ct);
                    }
                }
            }

            await _events.Writer.WriteAsync(new DoneEvent(false, null), ct);
        }
        catch (OperationCanceledException)
        {
            // Session torn down; nothing more to emit.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bridge session pump failed.");
            try { await _events.Writer.WriteAsync(new DoneEvent(true, ex.Message), CancellationToken.None); }
            catch { /* channel may be completing */ }
        }
        finally
        {
            _events.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Drain queued events into one HTTP response until a tool_call is emitted (park) or the turn
    /// completes. Returns whether the session is parked for a tool result or finished.
    /// </summary>
    public async Task<DrainOutcome> DrainAsync(SseWriter writer, CancellationToken ct)
    {
        await _drainLock.WaitAsync(ct);
        try
        {
            while (true)
            {
                BridgeEvent ev;
                try
                {
                    ev = await _events.Reader.ReadAsync(ct);
                }
                catch (ChannelClosedException)
                {
                    await writer.FinishAsync("stop", ct);
                    return DrainOutcome.Completed;
                }

                switch (ev)
                {
                    case TextDeltaEvent t:
                        await writer.WriteContentAsync(t.Text, ct);
                        break;

                    case ToolCallEvent tc:
                        await writer.WriteToolCallAsync(tc.CallId, tc.ToolName, tc.ArgumentsJson, ct);
                        await writer.FinishAsync("tool_calls", ct);
                        return DrainOutcome.ParkedForToolCall;

                    case DoneEvent d:
                        if (d is { IsError: true, ErrorMessage: { } err })
                            await writer.WriteContentAsync($"\n\n[proxy error: {err}]", ct);
                        await writer.FinishAsync("stop", ct);
                        return DrainOutcome.Completed;
                }
            }
        }
        finally
        {
            _drainLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DropBridge(this);
        try { await _sessionCts.CancelAsync(); } catch { /* ignore */ }
        try { await _pumpTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
        try { await _session.DisposeAsync(); } catch { /* ignore */ }
        try { await _client.DisposeAsync(); } catch { /* ignore */ }
        _sessionCts.Dispose();
        _drainLock.Dispose();
    }
}
