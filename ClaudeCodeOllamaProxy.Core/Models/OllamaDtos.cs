using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeOllamaProxy.Models;

// ---- /api/version ----

public sealed class OllamaVersionResponse
{
    public string Version { get; set; } = "0.6.4";
}

// ---- /api/tags ----

public sealed class OllamaTagsResponse
{
    public List<OllamaTagModel> Models { get; set; } = new();
}

public sealed class OllamaTagModel
{
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";

    [JsonPropertyName("modified_at")]
    public string ModifiedAt { get; set; } = "";

    public long Size { get; set; }
    public string Digest { get; set; } = "";
    public OllamaModelDetails Details { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("context_length")]
    public int ContextLength { get; set; }
}

public sealed class OllamaModelDetails
{
    [JsonPropertyName("parent_model")]
    public string ParentModel { get; set; } = "";

    public string Format { get; set; } = "api";
    public string Family { get; set; } = "claude";
    public List<string> Families { get; set; } = new() { "claude" };

    [JsonPropertyName("parameter_size")]
    public string ParameterSize { get; set; } = "api";

    [JsonPropertyName("quantization_level")]
    public string QuantizationLevel { get; set; } = "none";
}

// ---- /api/show ----

public sealed class OllamaShowResponse
{
    public string License { get; set; } = "proprietary";
    public string Modelfile { get; set; } = "";
    public string Parameters { get; set; } = "";
    public string Template { get; set; } = "{{ .Prompt }}";
    public OllamaModelDetails Details { get; set; } = new();

    [JsonPropertyName("model_info")]
    public Dictionary<string, object> ModelInfo { get; set; } = new();

    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("context_length")]
    public int ContextLength { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    /// <summary>The effort level this model entry applies.</summary>
    public string Effort { get; set; } = "";

    /// <summary>All effort levels the model supports (selectable as separate model variants).</summary>
    [JsonPropertyName("supported_efforts")]
    public List<string> SupportedEfforts { get; set; } = new();

    [JsonPropertyName("recommended_parameters")]
    public Dictionary<string, object> RecommendedParameters { get; set; } = new();
}

public sealed class OllamaShowRequest
{
    public string? Model { get; set; }
    public string? Name { get; set; }
}

// ---- /api/chat (NDJSON) ----

public sealed class OllamaChatRequest
{
    public string? Model { get; set; }
    public List<OllamaChatMessage> Messages { get; set; } = new();
    public bool? Stream { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class OllamaChatMessage
{
    public string Role { get; set; } = "user";
    public string? Content { get; set; }
    public List<string>? Images { get; set; }
}

public sealed class OllamaChatResponseChunk
{
    public string Model { get; set; } = "";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    public OllamaChatMessage Message { get; set; } = new();
    public bool Done { get; set; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; set; }
}
