using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeOllamaProxy.Infrastructure;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for the Ollama/OpenAI wire format.
/// Ollama and OpenAI both use snake_case property names.
/// </summary>
public static class JsonDefaults
{
    /// <summary>Options used for serializing responses and deserializing requests.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };
}
