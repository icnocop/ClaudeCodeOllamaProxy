using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeOllamaProxy.Models;

// ---- Request shapes (Copilot -> proxy, /v1/chat/completions) ----

public sealed class OpenAiChatRequest
{
    public string? Model { get; set; }
    public List<OpenAiMessage> Messages { get; set; } = new();
    public bool? Stream { get; set; }
    public List<OpenAiTool>? Tools { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class OpenAiMessage
{
    public string Role { get; set; } = "user";

    /// <summary>Either a plain string or an array of content parts (text/image_url). Kept raw.</summary>
    public JsonElement? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    /// <summary>Tool name for role:"tool" messages (optional).</summary>
    public string? Name { get; set; }
}

public sealed class OpenAiTool
{
    public string Type { get; set; } = "function";
    public OpenAiFunction Function { get; set; } = new();
}

public sealed class OpenAiFunction
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Arbitrary JSON schema for the tool parameters.</summary>
    public JsonElement? Parameters { get; set; }
}

public sealed class OpenAiToolCall
{
    public string? Id { get; set; }
    public int? Index { get; set; }
    public string Type { get; set; } = "function";
    public OpenAiFunctionCall Function { get; set; } = new();
}

public sealed class OpenAiFunctionCall
{
    public string Name { get; set; } = "";

    /// <summary>JSON-encoded arguments string (OpenAI convention).</summary>
    public string Arguments { get; set; } = "";
}

// ---- Streaming response shapes (proxy -> Copilot) ----

public sealed class OpenAiChunk
{
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion.chunk";

    public long Created { get; set; }
    public string Model { get; set; } = "";
    public List<OpenAiChunkChoice> Choices { get; set; } = new();
}

public sealed class OpenAiChunkChoice
{
    public int Index { get; set; }
    public OpenAiDelta Delta { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class OpenAiDelta
{
    public string? Role { get; set; }
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }
}

// ---- Non-streaming response shapes (proxy -> Copilot) ----

public sealed class OpenAiChatResponse
{
    public string Id { get; set; } = "";

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion";

    public long Created { get; set; }
    public string Model { get; set; } = "";
    public List<OpenAiResponseChoice> Choices { get; set; } = new();
}

public sealed class OpenAiResponseChoice
{
    public int Index { get; set; }
    public OpenAiResponseMessage Message { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class OpenAiResponseMessage
{
    public string Role { get; set; } = "assistant";
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }
}
