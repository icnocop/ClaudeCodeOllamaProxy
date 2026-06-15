using System.Text.Json;
using ClaudeCodeOllamaProxy.Models;
using Claude.AgentSdk;
using Claude.AgentSdk.Exceptions;
using Microsoft.Extensions.Options;

namespace ClaudeCodeOllamaProxy.Services;

/// <summary>
/// A selectable model = a base claude model combined with an effort level. The proxy exposes one of
/// these per (base model × supported effort), so the effort can be chosen from Copilot's model picker
/// (which otherwise has no effort control).
/// </summary>
public sealed record CatalogModel(
    string Id,                                   // selectable id, e.g. "claude-opus-4-8:high"
    string BaseModel,                            // CLI model alias/id passed to --model, e.g. "opus"
    string Effort,                               // "high"
    string DisplayLabel,                         // "Opus 4.8 (1M context) · high effort"
    int ContextLength,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> SupportedEfforts);

/// <summary>The base model + effort a request resolved to (parsed from the requested model id).</summary>
public sealed record ModelSelection(string BaseModel, string Effort);

/// <summary>
/// Retrieves the base model list from the claude CLI (cached with a TTL), then expands it into
/// effort variants. The CLI reports only id/displayName, so context length, capabilities, friendly
/// names, and effort levels are layered on from configuration.
/// </summary>
public sealed class ModelCatalog
{
    // The claude CLI's --effort flag accepts exactly these (ultracode/auto are interactive-only).
    private static readonly string[] ValidEfforts = { "low", "medium", "high", "xhigh", "max" };

    private readonly ProxyOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ModelCatalog> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<BaseModelInfo> _baseCache = Array.Empty<BaseModelInfo>();
    private DateTimeOffset _cachedAtUtc = DateTimeOffset.MinValue;

    public ModelCatalog(IOptions<ProxyOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ModelCatalog>();
    }

    private sealed record BaseModelInfo(string Id, string? DisplayName);

    /// <summary>The supported effort levels (configured, filtered to the CLI's valid set).</summary>
    public IReadOnlyList<string> SupportedEfforts()
    {
        var configured = _options.Claude.SupportedEfforts
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => ValidEfforts.Contains(e))
            .Distinct()
            .ToList();
        return configured.Count > 0 ? configured : ValidEfforts;
    }

    private string DefaultEffort()
    {
        var d = _options.Claude.DefaultEffort?.Trim().ToLowerInvariant();
        return !string.IsNullOrEmpty(d) && ValidEfforts.Contains(d) ? d : "high";
    }

    /// <summary>All selectable models: each base model expanded across every supported effort.</summary>
    public async Task<IReadOnlyList<CatalogModel>> GetModelsAsync(CancellationToken ct = default)
    {
        var bases = await GetBaseModelsAsync(ct);
        var efforts = SupportedEfforts();
        return bases
            .SelectMany(b => efforts.Select(e => BuildVariant(b.Id, b.DisplayName, e)))
            .ToList();
    }

    /// <summary>
    /// Resolve a requested model id to a base model + effort. Copilot may send back either the clean
    /// id (<c>opus:high</c>) or the display label (<c>Opus 4.8 (1M context) · high effort</c>) — which
    /// VS uses as the identifier — so match the catalog by both before falling back to suffix parsing.
    /// </summary>
    public async Task<ModelSelection> ResolveSelectionAsync(string? requestedId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestedId))
            return new ModelSelection(await DefaultModelIdAsync(ct), DefaultEffort());

        var models = await GetModelsAsync(ct);
        var hit = models.FirstOrDefault(m =>
            m.Id.Equals(requestedId, StringComparison.OrdinalIgnoreCase) ||
            m.DisplayLabel.Equals(requestedId, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
            return new ModelSelection(hit.BaseModel, hit.Effort);

        return ResolveSelection(requestedId);
    }

    /// <summary>Parse an <c>&lt;base&gt;:&lt;effort&gt;</c> id without consulting the catalog (sync fallback).</summary>
    public ModelSelection ResolveSelection(string requestedId)
    {
        var efforts = SupportedEfforts();
        var sep = requestedId.LastIndexOf(':');
        if (sep > 0 && sep < requestedId.Length - 1)
        {
            var suffix = requestedId[(sep + 1)..].ToLowerInvariant();
            if (efforts.Contains(suffix))
                return new ModelSelection(requestedId[..sep], suffix);
        }

        return new ModelSelection(requestedId, DefaultEffort());
    }

    /// <summary>Build the catalog entry for a specific requested id (used by /api/show).</summary>
    public async Task<CatalogModel?> ResolveAsync(string? requestedId, CancellationToken ct = default)
    {
        var bases = await GetBaseModelsAsync(ct);
        if (string.IsNullOrWhiteSpace(requestedId))
        {
            var first = bases.FirstOrDefault();
            return first is null ? null : BuildVariant(first.Id, first.DisplayName, DefaultEffort());
        }

        // Match the catalog by clean id or display label (VS sends the label back as the identifier).
        var models = await GetModelsAsync(ct);
        var hit = models.FirstOrDefault(m =>
            m.Id.Equals(requestedId, StringComparison.OrdinalIgnoreCase) ||
            m.DisplayLabel.Equals(requestedId, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;

        var sel = ResolveSelection(requestedId);
        var match = bases.FirstOrDefault(b => b.Id.Equals(sel.BaseModel, StringComparison.OrdinalIgnoreCase));
        return BuildVariant(sel.BaseModel, match?.DisplayName, sel.Effort);
    }

    /// <summary>First base model id, for requests that omit the model.</summary>
    public async Task<string> DefaultModelIdAsync(CancellationToken ct = default)
    {
        var bases = await GetBaseModelsAsync(ct);
        return bases.Count > 0 ? bases[0].Id : "sonnet";
    }

    private CatalogModel BuildVariant(string baseId, string? cliDisplayName, string effort)
    {
        var defaults = _options.ResolveDefaults(baseId);
        var friendly = Friendly(baseId, cliDisplayName, defaults);
        var label = $"{friendly} ({FormatContext(defaults.ContextLength)} context) · {effort} effort";
        return new CatalogModel(
            Id: $"{baseId}:{effort}",
            BaseModel: baseId,
            Effort: effort,
            DisplayLabel: label,
            ContextLength: defaults.ContextLength,
            Capabilities: defaults.Capabilities.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SupportedEfforts: SupportedEfforts());
    }

    private static string Friendly(string baseId, string? cliDisplayName, ModelCapabilities defaults)
    {
        if (!string.IsNullOrWhiteSpace(defaults.DisplayName)) return defaults.DisplayName;
        if (!string.IsNullOrWhiteSpace(cliDisplayName))
            return cliDisplayName!.StartsWith("Claude ", StringComparison.OrdinalIgnoreCase)
                ? cliDisplayName!["Claude ".Length..]
                : cliDisplayName!;
        return baseId;
    }

    internal static string FormatContext(int tokens)
    {
        if (tokens >= 1_000_000)
        {
            var m = tokens / 1_000_000.0;
            return (m == Math.Floor(m) ? ((int)m).ToString() : m.ToString("0.#")) + "M";
        }
        if (tokens >= 1000)
        {
            var k = tokens / 1000.0;
            return (k == Math.Floor(k) ? ((int)k).ToString() : k.ToString("0.#")) + "K";
        }
        return tokens.ToString();
    }

    private async Task<IReadOnlyList<BaseModelInfo>> GetBaseModelsAsync(CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _options.Claude.ModelsCacheTtlMinutes));
        if (_baseCache.Count > 0 && DateTimeOffset.UtcNow - _cachedAtUtc < ttl)
            return _baseCache;

        await _gate.WaitAsync(ct);
        try
        {
            if (_baseCache.Count > 0 && DateTimeOffset.UtcNow - _cachedAtUtc < ttl)
                return _baseCache;

            _baseCache = await RefreshAsync(ct);
            _cachedAtUtc = DateTimeOffset.UtcNow;
            return _baseCache;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<BaseModelInfo>> RefreshAsync(CancellationToken ct)
    {
        var clientOptions = new ClaudeAgentOptions
        {
            CliPath = string.IsNullOrWhiteSpace(_options.Claude.CliPath) ? null : _options.Claude.CliPath,
        };

        try
        {
            await using var client = new ClaudeAgentClient(clientOptions, _loggerFactory);
            await using var session = await client.CreateSessionAsync(ct);

            // Raw JSON variant: the CLI shape varies by version and the SDK's typed deserializer can
            // fail (e.g. an object wrapper rather than a bare array).
            var raw = await session.GetSupportedModelsAsync(ct);
            _logger.LogDebug("Raw supported_models response: {Json}", raw.GetRawText());

            var result = ParseModels(raw);
            if (result.Count == 0)
            {
                _logger.LogWarning("claude CLI returned no parseable models; using fallback list.");
                return Fallback();
            }

            _logger.LogInformation("Loaded {Count} model(s) from the claude CLI: {Models}",
                result.Count, string.Join(", ", result.Select(m => m.Id)));
            return result;
        }
        catch (CliNotFoundException ex)
        {
            _logger.LogError(ex,
                "The 'claude' CLI was not found. Install Claude Code and run 'claude login', then restart. Using fallback model list.");
            return Fallback();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query models from the claude CLI; using fallback list.");
            return Fallback();
        }
    }

    private List<BaseModelInfo> ParseModels(JsonElement raw)
    {
        var result = new List<BaseModelInfo>();

        JsonElement array = default;
        var found = false;
        if (raw.ValueKind == JsonValueKind.Array)
        {
            array = raw;
            found = true;
        }
        else if (raw.ValueKind == JsonValueKind.Object)
        {
            // Some claude CLI versions don't implement the supported_models control request.
            if (raw.TryGetProperty("subtype", out var st) && st.ValueKind == JsonValueKind.String
                && string.Equals(st.GetString(), "error", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "claude CLI does not support model listing ({Error}); using fallback model aliases.",
                    raw.TryGetProperty("error", out var e) ? e.GetString() : "unsupported");
                return result;
            }

            foreach (var key in new[] { "models", "data", "supportedModels", "supported_models" })
            {
                if (raw.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    array = arr;
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            _logger.LogWarning("Unexpected supported_models shape: {Json}", raw.GetRawText());
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in array.EnumerateArray())
        {
            string? id;
            string? displayName = null;

            if (el.ValueKind == JsonValueKind.String)
            {
                id = el.GetString();
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                id = Str(el, "value") ?? Str(el, "id") ?? Str(el, "model") ?? Str(el, "name");
                displayName = Str(el, "displayName") ?? Str(el, "display_name") ?? Str(el, "name");
            }
            else
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                result.Add(new BaseModelInfo(id!, displayName));
        }

        return result;

        static string? Str(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private List<BaseModelInfo> Fallback()
    {
        // CLI aliases that Claude Code accepts even when discovery fails.
        var models = new[] { "sonnet", "opus", "haiku", "fable" }
            .Select(id => new BaseModelInfo(id, null))
            .ToList();
        _logger.LogInformation("Using fallback model aliases: {Models}",
            string.Join(", ", models.Select(m => m.Id)));
        return models;
    }
}
