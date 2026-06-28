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

    /// <summary>Indented variant used only for human-readable log output.</summary>
    private static readonly JsonSerializerOptions IndentedOptions = new(Options) { WriteIndented = true };

    /// <summary>
    /// Re-formats JSON embedded in a log payload with newlines and indentation for readability.
    /// Handles a whole JSON object/array (request bodies, single CLI messages), line-oriented payloads
    /// (SSE / NDJSON), and lines with a leading prefix followed by JSON that runs to the end of the
    /// line (e.g. <c>Received: {json}</c>, <c>body: {json}</c>). Any text that is not valid JSON (or
    /// only a partial streamed chunk) is returned unchanged.
    /// </summary>
    public static string Prettify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        // Fast path: the whole payload is one JSON document.
        var trimmed = text.TrimStart();
        if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) && TryFormat(text, out var whole))
            return whole;

        // Otherwise look for JSON on individual lines (a prefix is allowed before it).
        if (!text.Contains('{') && !text.Contains('[')) return text;

        var lines = text.Split('\n');
        var any = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (TryPrettifyLine(lines[i], out var pretty))
            {
                lines[i] = pretty;
                any = true;
            }
        }

        return any ? string.Join('\n', lines) : text;
    }

    /// <summary>Prettify a single line whose remainder, from the first <c>{</c>/<c>[</c>, is JSON.</summary>
    private static bool TryPrettifyLine(string line, out string result)
    {
        result = line;

        var start = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] is '{' or '[') { start = i; break; }
        }
        if (start < 0) return false;

        // Trim a trailing CR (from Split('\n')) so the JSON parse sees a clean payload.
        if (!TryFormat(line[start..].TrimEnd(), out var formatted)) return false;

        result = line[..start] + formatted;
        return true;
    }

    private static bool TryFormat(string json, out string formatted)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            formatted = JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
            return true;
        }
        catch (JsonException)
        {
            formatted = json;
            return false;
        }
    }
}
