namespace ClaudeCodeOllamaProxy.Bridge;

/// <summary>An event produced by a live Claude session and consumed by the SSE writer.</summary>
public abstract record BridgeEvent;

/// <summary>Assistant text to stream to Copilot as a content delta.</summary>
public sealed record TextDeltaEvent(string Text) : BridgeEvent;

/// <summary>Claude invoked a bridged Copilot tool; emit an OpenAI tool_call and end the response.</summary>
public sealed record ToolCallEvent(string CallId, string ToolName, string ArgumentsJson) : BridgeEvent;

/// <summary>The agent turn finished; emit finish_reason "stop" and dispose the session.</summary>
public sealed record DoneEvent(bool IsError, string? ErrorMessage) : BridgeEvent;

/// <summary>The result of draining the event channel into one HTTP response.</summary>
public enum DrainOutcome
{
    /// <summary>A tool_call was emitted; the session is parked awaiting Copilot's tool result.</summary>
    ParkedForToolCall,

    /// <summary>The turn completed; the session should be disposed.</summary>
    Completed,
}
