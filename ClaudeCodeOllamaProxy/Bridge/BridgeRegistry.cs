using System.Collections.Concurrent;

namespace ClaudeCodeOllamaProxy.Bridge;

/// <summary>
/// Process-wide registry correlating in-flight bridged tool calls to their live <see cref="ToolBridge"/>,
/// and tracking one bridge per conversation so a single chat never spawns duplicate Claude sessions.
/// A tool call's globally-unique <c>tool_call_id</c> is the correlation key: Copilot returns it on the
/// follow-up request, which lets us find the parked session and resolve the blocked MCP handler.
/// </summary>
public sealed class BridgeRegistry
{
    private sealed record PendingEntry(ToolBridge Bridge, TaskCompletionSource<string> Completion);

    private readonly ConcurrentDictionary<string, PendingEntry> _pending = new();
    private readonly ConcurrentDictionary<string, ToolBridge> _byConversation = new();
    private readonly ILogger<BridgeRegistry> _logger;
    private int _callSeq;

    public BridgeRegistry(ILogger<BridgeRegistry> logger) => _logger = logger;

    /// <summary>A process-wide unique tool_call id. Must not be per-bridge — concurrent bridges would
    /// otherwise collide and deliver tool results to the wrong session.</summary>
    public string NextCallId() => "call_" + Interlocked.Increment(ref _callSeq).ToString("D8");

    public void RegisterPending(string callId, ToolBridge bridge, TaskCompletionSource<string> completion)
    {
        _pending[callId] = new PendingEntry(bridge, completion);
        _logger.LogInformation("Bridged tool call {CallId} registered as pending.", callId);
    }

    /// <summary>Find the live bridge waiting on the given tool_call_id, if any.</summary>
    public ToolBridge? FindBridge(string callId)
        => _pending.TryGetValue(callId, out var entry) ? entry.Bridge : null;

    /// <summary>Complete the MCP handler blocked on this tool_call_id with Copilot's tool result.</summary>
    public bool TryResolve(string callId, string result)
    {
        if (_pending.TryRemove(callId, out var entry))
        {
            _logger.LogInformation("Resolving bridged tool call {CallId} with Copilot's result ({Len} chars).",
                callId, result.Length);
            _logger.LogDebug("Tool result {CallId}: {Preview}", callId, Preview(result));
            entry.Completion.TrySetResult(result);
            return true;
        }

        _logger.LogWarning("No pending bridge found for tool_call_id {CallId} (the session may have ended or been superseded).", callId);
        return false;
    }

    /// <summary>The live bridge for a conversation, if one exists.</summary>
    public ToolBridge? GetBridgeByConversation(string conversationKey)
        => _byConversation.TryGetValue(conversationKey, out var b) ? b : null;

    /// <summary>Register a bridge as the active one for its conversation.</summary>
    public void RegisterBridge(string conversationKey, ToolBridge bridge)
        => _byConversation[conversationKey] = bridge;

    /// <summary>Cancel and drop any pending calls owned by a bridge that is being torn down, and
    /// remove it from the conversation map.</summary>
    public void DropBridge(ToolBridge bridge)
    {
        foreach (var (callId, entry) in _pending)
        {
            if (ReferenceEquals(entry.Bridge, bridge) && _pending.TryRemove(callId, out var removed))
                removed.Completion.TrySetCanceled();
        }

        foreach (var (key, b) in _byConversation)
        {
            if (ReferenceEquals(b, bridge))
                _byConversation.TryRemove(KeyValuePair.Create(key, b));
        }
    }

    private static string Preview(string s)
    {
        const int max = 2000;
        s = s.ReplaceLineEndings(" ");
        return s.Length <= max ? s : s[..max] + $"… (+{s.Length - max} chars)";
    }
}
