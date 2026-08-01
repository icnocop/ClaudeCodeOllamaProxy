using System.Reflection;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// The running build's version, and the parsing of GitHub release tags into the same shape so the two can
/// be compared by <see cref="UpdateChecker"/>.
/// </summary>
/// <remarks>
/// Released builds are stamped by CI (<c>dotnet publish -p:Version=YYYY.MM.DD.&lt;run&gt;</c> in
/// <c>.github/workflows/build.yml</c>), matching the <c>vYYYY.MM.DD.&lt;run&gt;</c> release tag. A local
/// developer build has no <c>&lt;Version&gt;</c> and so reports <see cref="DevelopmentVersion"/>
/// (1.0.0.0), which is older than every release — the update check therefore always reports an update
/// available when running from source. That's intentional; it keeps the code path identical in dev.
/// </remarks>
public static class AppVersion
{
    /// <summary>The version an unstamped (local developer) build reports.</summary>
    public static readonly Version DevelopmentVersion = new(1, 0, 0, 0);

    /// <summary>The running assembly's version, used for comparisons against release tags.</summary>
    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? DevelopmentVersion;

    /// <summary>
    /// The version as shown to the user. Prefers the informational version, which keeps the exact string
    /// CI passed (<c>2026.06.28.9</c>) rather than the normalized <see cref="Version"/> form
    /// (<c>2026.6.28.9</c>), so it reads the same as the release tag.
    /// </summary>
    public static string Display { get; } = ReadDisplay();

    /// <summary>True for an unstamped local build (see the remarks on this class).</summary>
    public static bool IsDevelopmentBuild => Current == DevelopmentVersion;

    /// <summary>
    /// Parse a GitHub release tag (<c>v2026.06.28.9</c>, possibly with a <c>-beta.1</c> style suffix) into
    /// a comparable <see cref="Version"/>. Returns false for tags that aren't in that shape.
    /// </summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = DevelopmentVersion;

        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim().TrimStart('v', 'V');

        // Drop any prerelease/build suffix — System.Version only understands the numeric part.
        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0)
            text = text[..cut];

        return Version.TryParse(text, out version!);
    }

    private static string ReadDisplay()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return Current.ToString();

        // The SDK appends "+<commit sha>" when SourceLink is in play; that's noise for the About page.
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
