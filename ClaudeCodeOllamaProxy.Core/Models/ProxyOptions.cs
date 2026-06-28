namespace ClaudeCodeOllamaProxy.Models;

/// <summary>
/// Root configuration bound from appsettings.json. The model list itself is retrieved from the
/// claude CLI at runtime (see <see cref="Services.ModelCatalog"/>); this only holds proxy behavior
/// overrides and a capability/context defaults table (the CLI does not report those per model).
/// </summary>
public sealed class ProxyOptions
{
    public ClaudeOptionsConfig Claude { get; set; } = new();

    /// <summary>
    /// Capability/context defaults applied to CLI-reported models, keyed by case-insensitive
    /// substring of the model id. The <c>"*"</c> key is the fallback for ids that match nothing else.
    /// </summary>
    public Dictionary<string, ModelCapabilities> ModelDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve the capability defaults for a model id: longest matching substring key, else "*".</summary>
    public ModelCapabilities ResolveDefaults(string modelId)
    {
        ModelCapabilities? best = null;
        var bestLen = -1;
        foreach (var (key, value) in ModelDefaults)
        {
            if (key == "*") continue;
            if (modelId.Contains(key, StringComparison.OrdinalIgnoreCase) && key.Length > bestLen)
            {
                best = value;
                bestLen = key.Length;
            }
        }

        if (best is not null) return best;
        return ModelDefaults.TryGetValue("*", out var fallback) ? fallback : ModelCapabilities.Default;
    }
}

public sealed class ClaudeOptionsConfig
{
    /// <summary>Optional explicit working directory; when empty it is inferred from the request.</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>Optional path to the claude CLI executable; when empty the SDK resolves it from PATH.</summary>
    public string CliPath { get; set; } = "";

    /// <summary>Upper bound on agent turns per request.</summary>
    public int MaxTurns { get; set; } = 50;

    /// <summary>When true, run_in_terminal is routed through Copilot instead of Claude's own Bash tool.</summary>
    public bool BridgeTerminalTool { get; set; } = true;

    /// <summary>How long the model catalog is cached before re-querying the CLI.</summary>
    public int ModelsCacheTtlMinutes { get; set; } = 30;

    /// <summary>Effort applied when a request selects a model without an effort suffix.</summary>
    public string DefaultEffort { get; set; } = "high";

    /// <summary>
    /// Effort levels exposed as selectable model variants. Empty falls back to the CLI's documented
    /// set (low, medium, high, xhigh, max). Values outside that set are dropped (the CLI ignores
    /// unknown values; ultracode/auto are interactive-only and not valid for the --effort flag).
    /// </summary>
    public List<string> SupportedEfforts { get; set; } = new();

    /// <summary>
    /// Sets the CLI's <c>MCP_TOOL_TIMEOUT</c> (ms) — the wall-clock cap on a bridged Copilot tool
    /// call (build/test/debug can run long). 0 leaves the CLI default (~28h). Note: Copilot's own
    /// tools may impose their own caps (e.g. debugger_launch_unit_test ~30s) which this cannot change.
    /// </summary>
    public int McpToolTimeoutMs { get; set; } = 600000;

    /// <summary>
    /// Sets the CLI's <c>MAX_MCP_OUTPUT_TOKENS</c> — the cap on a tool result before it is truncated
    /// (large read_file/search results otherwise truncate at ~10k tokens). 0 leaves the CLI default.
    /// </summary>
    public int MaxMcpOutputTokens { get; set; } = 100000;
}

public sealed class ModelCapabilities
{
    /// <summary>Friendly, correctly-cased version label for display, e.g. "Opus 4.8". Optional.</summary>
    public string DisplayName { get; set; } = "";

    public int ContextLength { get; set; } = 200000;

    // No initializer items: .NET config binding APPENDS to a non-empty list, which would duplicate
    // entries. The "*" entry in appsettings supplies the real defaults.
    public List<string> Capabilities { get; set; } = new();

    public static readonly ModelCapabilities Default = new() { Capabilities = { "completion", "tools" } };
}
