using System.Security.Cryptography;
using System.Text;
using ClaudeCodeOllamaProxy.Infrastructure;
using ClaudeCodeOllamaProxy.Models;
using ClaudeCodeOllamaProxy.Services;

namespace ClaudeCodeOllamaProxy.Endpoints;

/// <summary>
/// Ollama-native discovery endpoints that VS Copilot Chat's BYOK Ollama provider calls before chat:
/// <c>/api/version</c>, <c>/api/tags</c>, and <c>/api/show</c>, plus the OpenAI <c>/v1/models</c> list.
/// All model data comes from <see cref="ModelCatalog"/>.
/// </summary>
public static class OllamaDiscoveryEndpoints
{
    // Copilot requires the reported version to be >= 0.6.4 or discovery aborts.
    private const string OllamaVersion = "0.6.4";
    private const string Architecture = "claude";

    public static void MapOllamaDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/version", () =>
            Results.Json(new OllamaVersionResponse { Version = OllamaVersion }, JsonDefaults.Options));

        app.MapGet("/api/tags", async (ModelCatalog catalog, CancellationToken ct) =>
        {
            var models = await catalog.GetModelsAsync(ct);
            var modifiedAt = DateTimeOffset.UtcNow.ToString("o");

            var response = new OllamaTagsResponse
            {
                Models = models.Select(m => new OllamaTagModel
                {
                    // VS uses `name` as both the picker label AND the identifier it echoes back as the
                    // chat model (it ignores `model`/`display_name`), so the friendly label goes here.
                    Name = m.DisplayLabel,
                    Model = m.Id,
                    ModifiedAt = modifiedAt,
                    Size = 0,
                    Digest = Digest(m.Id),
                    Details = new OllamaModelDetails(),
                    Capabilities = m.Capabilities.ToList(),
                    ContextLength = m.ContextLength,
                }).ToList(),
            };

            return Results.Json(response, JsonDefaults.Options);
        });

        app.MapMethods("/api/show", new[] { "GET", "POST" }, async (HttpContext ctx, ModelCatalog catalog, CancellationToken ct) =>
        {
            var modelId = await ReadModelIdAsync(ctx, ct);
            if (string.IsNullOrWhiteSpace(modelId))
                return Results.Json(new { error = "model is required" }, JsonDefaults.Options, statusCode: 400);

            var model = await catalog.ResolveAsync(modelId, ct)
                        ?? (await catalog.GetModelsAsync(ct)).FirstOrDefault();

            if (model is null)
                return Results.Json(new { error = $"model '{modelId}' not found" }, JsonDefaults.Options, statusCode: 404);

            var response = new OllamaShowResponse
            {
                Details = new OllamaModelDetails(),
                Capabilities = model.Capabilities.ToList(),
                ContextLength = model.ContextLength,
                DisplayName = model.DisplayLabel,
                Effort = model.Effort,
                SupportedEfforts = model.SupportedEfforts.ToList(),
                ModelInfo = new Dictionary<string, object>
                {
                    // The dynamic "<arch>.context_length" key is required or Copilot clamps to 32768.
                    ["general.architecture"] = Architecture,
                    [$"{Architecture}.context_length"] = model.ContextLength,
                },
                RecommendedParameters = new Dictionary<string, object>
                {
                    ["num_ctx"] = model.ContextLength,
                    ["effort"] = model.Effort,
                },
            };

            return Results.Json(response, JsonDefaults.Options);
        });

        app.MapGet("/v1/models", async (ModelCatalog catalog, CancellationToken ct) =>
        {
            var models = await catalog.GetModelsAsync(ct);
            var data = models.Select(m => new
            {
                id = m.Id,
                @object = "model",
                created = 0,
                owned_by = "anthropic",
            });

            return Results.Json(new { @object = "list", data }, JsonDefaults.Options);
        });
    }

    private static async Task<string?> ReadModelIdAsync(HttpContext ctx, CancellationToken ct)
    {
        if (ctx.Request.Query.TryGetValue("model", out var q) && !string.IsNullOrWhiteSpace(q))
            return q.ToString();

        if (HttpMethods.IsPost(ctx.Request.Method) && (ctx.Request.ContentLength ?? 0) > 0)
        {
            try
            {
                var body = await System.Text.Json.JsonSerializer.DeserializeAsync<OllamaShowRequest>(
                    ctx.Request.Body, JsonDefaults.Options, ct);
                return body?.Model ?? body?.Name;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string Digest(string id)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
