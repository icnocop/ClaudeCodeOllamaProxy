using Octokit;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>A release that is newer than the running build.</summary>
/// <param name="Version">The tag parsed by <see cref="AppVersion.TryParseTag"/>, for comparisons.</param>
/// <param name="DisplayVersion">The tag as published (e.g. <c>v2026.06.28.9</c>), for display.</param>
/// <param name="HtmlUrl">The release page on github.com.</param>
public sealed record UpdateInfo(Version Version, string DisplayVersion, string HtmlUrl);

/// <summary>
/// Checks the project's GitHub releases for a build newer than this one. Read-only and anonymous — no
/// token, no download, no install; the UI only ever links to the release page.
/// </summary>
/// <remarks>
/// The result is cached in <see cref="Known"/> and broadcast via <see cref="UpdateStateChanged"/> so the
/// startup check (<c>App.OnLaunched</c>) can arm the About page's notice and the navigation badge before
/// either has been created.
/// </remarks>
public static class UpdateChecker
{
    public const string Owner = "icnocop";
    public const string Repo = "ClaudeCodeOllamaProxy";

    /// <summary>The repository's releases page — the fallback link when no specific release is known.</summary>
    public static string ReleasesUrl => $"https://github.com/{Owner}/{Repo}/releases";

    // One client for the process: it owns an HttpClient. The product header becomes the User-Agent,
    // which the GitHub API requires — requests without one are rejected.
    private static readonly GitHubClient Client = new(new ProductHeaderValue("ClaudeCodeOllamaProxy-UI"));

    /// <summary>The newest update found so far, or null if none was found (or nothing checked yet).</summary>
    public static UpdateInfo? Known { get; private set; }

    /// <summary>Raised on every successful check, with the result (null when up to date).</summary>
    public static event Action<UpdateInfo?>? UpdateStateChanged;

    /// <summary>
    /// The newest published release newer than the running build, or null when already up to date.
    /// Throws on network or API failure — callers decide how to surface that, because "up to date" and
    /// "couldn't check" are different messages.
    /// </summary>
    /// <param name="includePrereleases">When false (the default setting), prereleases are ignored.</param>
    public static async Task<UpdateInfo?> CheckAsync(bool includePrereleases)
    {
        // GetAll rather than GetLatest: GetLatest can't be told to consider prereleases. Drafts are
        // invisible either way (they need an authenticated token), which matters here because the release
        // workflow creates drafts — a release only becomes visible once it's published by hand.
        var releases = await Client.Repository.Release.GetAll(Owner, Repo).ConfigureAwait(false);

        var candidates = new List<UpdateInfo>();
        foreach (var release in releases)
        {
            if (release.Draft || (release.Prerelease && !includePrereleases))
                continue;

            // Tags that aren't in the vYYYY.MM.DD.<run> shape can't be compared; skip them.
            if (AppVersion.TryParseTag(release.TagName, out var version))
                candidates.Add(new UpdateInfo(version, release.TagName, release.HtmlUrl));
        }

        var newest = candidates.OrderByDescending(c => c.Version).FirstOrDefault();
        var result = newest is not null && newest.Version > AppVersion.Current ? newest : null;

        Known = result;
        UpdateStateChanged?.Invoke(result);
        return result;
    }
}
