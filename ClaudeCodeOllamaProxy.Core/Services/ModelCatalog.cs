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

/// <summary>
/// The base model + effort a request resolved to (parsed from the requested model id). A null
/// <paramref name="Effort"/> means the model does not support effort (e.g. Haiku) and the CLI
/// must be invoked without <c>--effort</c>.
/// </summary>
public sealed record ModelSelection(string BaseModel, string? Effort);

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
    private readonly ClaudeSessionFactory _sessionFactory;
    private readonly ILogger<ModelCatalog> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<BaseModelInfo> _baseCache = Array.Empty<BaseModelInfo>();
    private DateTimeOffset _cachedAtUtc = DateTimeOffset.MinValue;
    // The base model the CLI marks as default (its initialize "default" entry); null ⇒ first model.
    private string? _defaultBaseId;

    public ModelCatalog(IOptions<ProxyOptions> options, ILoggerFactory loggerFactory, ClaudeSessionFactory sessionFactory)
    {
        _options = options.Value;
        _sessionFactory = sessionFactory;
        _logger = loggerFactory.CreateLogger<ModelCatalog>();
    }

    /// <summary>
    /// A base model discovered from the CLI. <paramref name="SupportedEfforts"/> is the model's own
    /// effort levels (from the initialize payload's <c>supportedEffortLevels</c>); empty when the
    /// model does not support effort.
    /// </summary>
    private sealed record BaseModelInfo(
        string Id,
        string? DisplayName,
        bool SupportsEffort,
        IReadOnlyList<string> SupportedEfforts,
        string? Description = null);

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

    /// <summary>
    /// All selectable models: each base model expanded across its own supported effort levels
    /// (intersected with the configured allow-list). Models that don't support effort (e.g. Haiku)
    /// surface as a single variant with no effort suffix.
    /// </summary>
    public async Task<IReadOnlyList<CatalogModel>> GetModelsAsync(CancellationToken ct = default)
    {
        var bases = await GetBaseModelsAsync(ct);
        var allow = SupportedEfforts();
        var result = new List<CatalogModel>();
        foreach (var b in bases)
        {
            var efforts = EffortsFor(b, allow);
            if (efforts.Count == 0)
                result.Add(BuildVariant(b, null, Array.Empty<string>()));
            else
                foreach (var e in efforts)
                    result.Add(BuildVariant(b, e, efforts));
        }
        return result;
    }

    /// <summary>A model's effort levels filtered to the configured allow-list (empty if no effort).</summary>
    private static List<string> EffortsFor(BaseModelInfo b, IReadOnlyList<string> allow)
        => b.SupportsEffort ? b.SupportedEfforts.Where(e => allow.Contains(e)).ToList() : new List<string>();

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
            return new ModelSelection(hit.BaseModel, string.IsNullOrEmpty(hit.Effort) ? null : hit.Effort);

        _logger.LogWarning(
            "Requested model '{RequestedId}' matched no catalog entry; passing it to the CLI as-is — " +
            "the CLI will choose its own default model (e.g. 'Auto' is not a real model id).", requestedId);
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
        var allow = SupportedEfforts();
        if (string.IsNullOrWhiteSpace(requestedId))
        {
            var first = await DefaultBaseAsync(ct);
            if (first is null) return null;
            var efforts = EffortsFor(first, allow);
            var eff = efforts.Count == 0 ? null
                : efforts.Contains(DefaultEffort()) ? DefaultEffort() : efforts[0];
            return BuildVariant(first, eff, efforts);
        }

        // Match the catalog by clean id or display label (VS sends the label back as the identifier).
        var models = await GetModelsAsync(ct);
        var hit = models.FirstOrDefault(m =>
            m.Id.Equals(requestedId, StringComparison.OrdinalIgnoreCase) ||
            m.DisplayLabel.Equals(requestedId, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;

        var bases = await GetBaseModelsAsync(ct);
        var sel = ResolveSelection(requestedId);
        var match = bases.FirstOrDefault(b => b.Id.Equals(sel.BaseModel, StringComparison.OrdinalIgnoreCase));
        var synthEfforts = sel.Effort is null ? Array.Empty<string>() : new[] { sel.Effort };
        var synth = match ?? new BaseModelInfo(sel.BaseModel, null, sel.Effort is not null, synthEfforts);
        return BuildVariant(synth, sel.Effort, match is null ? synthEfforts : EffortsFor(match, allow));
    }

    /// <summary>
    /// The default base model id for requests that omit the model — the model the CLI's initialize
    /// response marks as default (its <c>"default"</c> entry), else the first discovered model.
    /// </summary>
    public async Task<string> DefaultModelIdAsync(CancellationToken ct = default)
        => (await DefaultBaseAsync(ct))?.Id ?? "sonnet";

    /// <summary>Resolve the default base model: the CLI-marked default if present, else the first.</summary>
    private async Task<BaseModelInfo?> DefaultBaseAsync(CancellationToken ct)
    {
        var bases = await GetBaseModelsAsync(ct);
        if (bases.Count == 0) return null;
        if (_defaultBaseId is not null)
        {
            var hit = bases.FirstOrDefault(b => b.Id.Equals(_defaultBaseId, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return bases[0];
    }

    /// <summary>
    /// Build a catalog entry. A null/empty <paramref name="effort"/> yields a no-effort variant
    /// (no <c>:effort</c> id suffix and no "· effort" label segment).
    /// </summary>
    private CatalogModel BuildVariant(BaseModelInfo b, string? effort, IReadOnlyList<string> modelEfforts)
    {
        var defaults = _options.ResolveDefaults(b.Id);
        var friendly = Friendly(b.Id, b.DisplayName, defaults);
        var context = $"({FormatContext(defaults.ContextLength)} context)";
        var hasEffort = !string.IsNullOrEmpty(effort);
        return new CatalogModel(
            Id: hasEffort ? $"{b.Id}:{effort}" : b.Id,
            BaseModel: b.Id,
            Effort: effort ?? "",
            DisplayLabel: hasEffort ? $"{friendly} {context} · {effort} effort" : $"{friendly} {context}",
            ContextLength: defaults.ContextLength,
            Capabilities: defaults.Capabilities.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SupportedEfforts: modelEfforts);
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
        // The CLI's initialize handshake (always succeeds) carries a rich `models` array — including
        // per-model effort levels. The SDK discards that response, so capture it off the raw stdout
        // stream via OnMessageReceived (fires for every line, before parsing) during CreateSessionAsync.
        _defaultBaseId = null; // recomputed below from the init payload; null ⇒ fall back to first model
        var initModels = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientOptions = new ClaudeAgentOptions
        {
            CliPath = string.IsNullOrWhiteSpace(_options.Claude.CliPath) ? null : _options.Claude.CliPath,
            OnMessageReceived = line => TryCaptureInitModels(line, initModels),
        };

        try
        {
            // Build through the session factory so the SDK gets the fault-tolerant logger factory — its
            // background message-reader loop must not crash the host by logging after the providers are
            // disposed during a stop/restart (see ClaudeSessionFactory / FaultTolerantLoggerFactory).
            await using var client = _sessionFactory.CreateClient(clientOptions);
            await using var session = await client.CreateSessionAsync(ct);

            // Primary source: the initialize payload captured above (the handshake is complete by the
            // time CreateSessionAsync returns, so this is already resolved — read it without blocking).
            if (initModels.Task.IsCompletedSuccessfully)
            {
                var initArray = initModels.Task.Result;
                _logger.LogDebug("Init models: {Json}", initArray.GetRawText());
                var initResult = ParseModels(initArray, fromInit: true);
                if (initResult.Count > 0)
                {
                    _defaultBaseId = ResolveDefaultBaseId(initArray, initResult);
                    _logger.LogInformation(
                        "Loaded {Count} model(s) from the claude CLI initialize response (default={Default}): {Models}",
                        initResult.Count, _defaultBaseId ?? initResult[0].Id,
                        string.Join(", ", initResult.Select(m => m.Id)));
                    return initResult;
                }
            }

            // Secondary: the supported_models control request (older CLI versions don't implement it,
            // and its shape varies — so parse the raw JSON rather than the SDK's typed deserializer).
            var raw = await session.GetSupportedModelsAsync(ct);
            _logger.LogDebug("Raw supported_models response: {Json}", raw.GetRawText());

            var result = ParseModels(raw, fromInit: false);
            if (result.Count == 0)
            {
                _logger.LogWarning("claude CLI returned no parseable models; using fallback list.");
                return Fallback();
            }

            _logger.LogInformation("Loaded {Count} model(s) from the claude CLI supported_models: {Models}",
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

    /// <summary>
    /// Capture the initialize control_response's <c>models</c> array off the raw stdout stream.
    /// Shape: <c>{ "type":"control_response", "response":{ "subtype":"success",
    /// "response":{ "models":[...] } } }</c>. The element is cloned so it outlives the JsonDocument.
    /// </summary>
    private static void TryCaptureInitModels(string line, TaskCompletionSource<JsonElement> tcs)
    {
        if (tcs.Task.IsCompleted) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "control_response") return;
            if (!root.TryGetProperty("response", out var resp) || resp.ValueKind != JsonValueKind.Object) return;
            if (!resp.TryGetProperty("response", out var inner) || inner.ValueKind != JsonValueKind.Object) return;
            if (!inner.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return;
            tcs.TrySetResult(models.Clone());
        }
        catch (JsonException)
        {
            // Non-JSON or a partial line; ignore (the init response arrives as one complete line).
        }
    }

    /// <summary>
    /// Parse a model array from either the initialize payload (<paramref name="fromInit"/> true:
    /// effort comes from each entry's <c>supportsEffort</c>/<c>supportedEffortLevels</c>, absent ⇒ no
    /// effort) or the supported_models response (false: entries without effort metadata keep the
    /// legacy all-efforts behavior).
    /// </summary>
    private List<BaseModelInfo> ParseModels(JsonElement raw, bool fromInit)
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
            _logger.LogWarning("Unexpected model list shape: {Json}", raw.GetRawText());
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in array.EnumerateArray())
        {
            string? id;
            string? displayName = null;
            string? description = null;
            bool supportsEffort;
            IReadOnlyList<string> efforts;

            if (el.ValueKind == JsonValueKind.String)
            {
                id = el.GetString();
                supportsEffort = !fromInit;
                efforts = supportsEffort ? ValidEfforts : Array.Empty<string>();
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                id = Str(el, "value") ?? Str(el, "id") ?? Str(el, "model") ?? Str(el, "name");
                description = Str(el, "description");
                displayName = ParseFriendlyFromDescription(description)
                    ?? Str(el, "displayName") ?? Str(el, "display_name") ?? Str(el, "name");
                (supportsEffort, efforts) = ParseEffort(el, fromInit);
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(id)) continue;
            // "default" is a CLI alias for the recommended model, not a distinct selectable model —
            // skipped here, but used by ResolveDefaultBaseId to pick the default.
            if (string.Equals(id, "default", StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(id!))
                result.Add(new BaseModelInfo(id!, displayName, supportsEffort, efforts, description));
        }

        return result;

        static string? Str(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>
    /// Determine the default base model from the initialize payload. The CLI marks its default with a
    /// <c>"default"</c> entry (displayName "Default (recommended)"); match it to a real model by
    /// description, then by friendly label. Returns null if no match (caller falls back to the first).
    /// </summary>
    private static string? ResolveDefaultBaseId(JsonElement initArray, IReadOnlyList<BaseModelInfo> models)
    {
        string? defaultDescription = null;
        foreach (var el in initArray.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            if (el.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                && string.Equals(v.GetString(), "default", StringComparison.OrdinalIgnoreCase))
            {
                defaultDescription = el.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString() : null;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(defaultDescription)) return null;

        // The "default" entry's description is identical to the model it aliases (e.g. Opus).
        var byDescription = models.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.Description) &&
            string.Equals(m.Description, defaultDescription, StringComparison.OrdinalIgnoreCase));
        if (byDescription is not null) return byDescription.Id;

        var friendly = ParseFriendlyFromDescription(defaultDescription);
        var byFriendly = friendly is null ? null : models.FirstOrDefault(m =>
            string.Equals(m.DisplayName, friendly, StringComparison.OrdinalIgnoreCase));
        return byFriendly?.Id;
    }

    /// <summary>Determine a model's effort support from its JSON entry.</summary>
    private (bool SupportsEffort, IReadOnlyList<string> Efforts) ParseEffort(JsonElement el, bool fromInit)
    {
        var levels = ReadStringArray(el, "supportedEffortLevels");
        if (levels.Count > 0)
        {
            // Explicit levels: honored unless supportsEffort is explicitly false.
            var on = !el.TryGetProperty("supportsEffort", out var se) || se.ValueKind != JsonValueKind.False;
            return on ? (true, levels) : (false, Array.Empty<string>());
        }

        if (el.TryGetProperty("supportsEffort", out var flag))
        {
            var on = flag.ValueKind == JsonValueKind.True;
            return (on, on ? ValidEfforts : Array.Empty<string>());
        }

        // No effort metadata at all: init entries (e.g. Haiku) default to no effort; supported_models
        // entries keep the legacy behavior of expanding across every configured effort.
        return fromInit ? (false, Array.Empty<string>()) : (true, ValidEfforts);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                list.Add(s.Trim().ToLowerInvariant());
        return list;
    }

    /// <summary>Extract a friendly version label from a description like "Opus 4.8 with 1M context · …".</summary>
    private static string? ParseFriendlyFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var s = description!;
        var dot = s.IndexOf('·'); // "·" separator
        if (dot > 0) s = s[..dot];
        var with = s.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
        if (with > 0) s = s[..with];
        s = s.Trim();
        return s.Length > 0 ? s : null;
    }

    private List<BaseModelInfo> Fallback()
    {
        // CLI aliases that Claude Code accepts even when discovery fails. Effort metadata is unknown
        // here, so expand across every configured effort (the pre-existing fallback behavior).
        var models = new[] { "sonnet", "opus", "haiku", "fable" }
            .Select(id => new BaseModelInfo(id, null, true, ValidEfforts))
            .ToList();
        _logger.LogInformation("Using fallback model aliases: {Models}",
            string.Join(", ", models.Select(m => m.Id)));
        return models;
    }
}
