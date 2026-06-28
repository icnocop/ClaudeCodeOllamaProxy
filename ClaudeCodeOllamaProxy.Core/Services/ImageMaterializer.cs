using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeCodeOllamaProxy.Services;

/// <summary>
/// The Claude SDK has no structured image input, so images sent by Copilot (data: URLs or bare
/// base64) are written to temp files and referenced by path in the prompt, letting Claude's own
/// Read tool view them. Best-effort: http(s) URLs are passed through as text references.
/// </summary>
public sealed partial class ImageMaterializer
{
    private readonly ILogger<ImageMaterializer> _logger;

    public ImageMaterializer(ILogger<ImageMaterializer> logger) => _logger = logger;

    [GeneratedRegex(@"^data:(?<mime>[^;,]+)?(;base64)?,(?<data>.*)$", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex DataUrlRegex();

    /// <summary>
    /// Materialize images and return a text snippet to append to the prompt that points Claude at them.
    /// Returns an empty string when there are no images.
    /// </summary>
    public string Materialize(IReadOnlyList<ImageReference> images)
    {
        if (images.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        var index = 0;
        foreach (var image in images)
        {
            index++;
            try
            {
                if (image.UrlOrData.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || image.UrlOrData.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"- Image {index}: {image.UrlOrData}");
                    continue;
                }

                var (bytes, ext) = Decode(image.UrlOrData);
                if (bytes is null) continue;

                var path = Path.Combine(Path.GetTempPath(), $"ccop-img-{Guid.NewGuid():N}{ext}");
                File.WriteAllBytes(path, bytes);
                sb.AppendLine($"- Image {index} (read this file): {path}");
                _logger.LogInformation("Materialized request image {Index} to {Path} ({Bytes} bytes).", index, path, bytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to materialize request image {Index}.", index);
            }
        }

        if (sb.Length == 0) return string.Empty;

        return "\n\nThe user attached the following image(s); use the Read tool to view them:\n" + sb;
    }

    private static (byte[]? Bytes, string Ext) Decode(string value)
    {
        var match = DataUrlRegex().Match(value);
        string base64;
        var ext = ".png";

        if (match.Success)
        {
            base64 = match.Groups["data"].Value;
            var mime = match.Groups["mime"].Value;
            ext = mime switch
            {
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".png",
            };
        }
        else
        {
            base64 = value;
        }

        try
        {
            return (Convert.FromBase64String(base64), ext);
        }
        catch
        {
            return (null, ext);
        }
    }
}
