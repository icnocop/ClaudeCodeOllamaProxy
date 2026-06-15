using System.Text.RegularExpressions;
using ClaudeCodeOllamaProxy.Models;
using Microsoft.Extensions.Options;

namespace ClaudeCodeOllamaProxy.Services;

/// <summary>
/// Determines the working directory Claude should run in. Copilot's Ollama API does not pass the
/// open solution path, so we (1) honor an explicit config override, else (2) infer the solution
/// directory from absolute paths mentioned in the request, else (3) fall back to the proxy's CWD.
/// </summary>
public sealed partial class WorkingDirectoryResolver
{
    private readonly ProxyOptions _options;
    private readonly ILogger<WorkingDirectoryResolver> _logger;

    public WorkingDirectoryResolver(IOptions<ProxyOptions> options, ILogger<WorkingDirectoryResolver> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    [GeneratedRegex(@"[A-Za-z]:[\\/][^\s""'<>|?*\r\n]+", RegexOptions.Compiled)]
    private static partial Regex WindowsPathRegex();

    /// <summary>Resolve the working directory from the candidate text fragments of a request.</summary>
    public string Resolve(IEnumerable<string?> textFragments)
    {
        if (!string.IsNullOrWhiteSpace(_options.Claude.WorkingDirectory))
        {
            var configured = _options.Claude.WorkingDirectory;
            _logger.LogInformation("Working directory (configured override): {Dir}", configured);
            return configured;
        }

        var candidateDirs = ExtractCandidateDirectories(textFragments);

        foreach (var dir in candidateDirs)
        {
            var solutionRoot = FindSolutionRoot(dir);
            if (solutionRoot is not null)
            {
                _logger.LogInformation("Working directory (inferred solution root from request): {Dir}", solutionRoot);
                return solutionRoot;
            }
        }

        if (candidateDirs.Count > 0)
        {
            var deepest = candidateDirs[0];
            _logger.LogInformation("Working directory (inferred deepest existing path from request): {Dir}", deepest);
            return deepest;
        }

        var cwd = Directory.GetCurrentDirectory();
        _logger.LogInformation("Working directory (fallback to proxy CWD): {Dir}", cwd);
        return cwd;
    }

    private static List<string> ExtractCandidateDirectories(IEnumerable<string?> textFragments)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fragment in textFragments)
        {
            if (string.IsNullOrEmpty(fragment)) continue;

            foreach (Match m in WindowsPathRegex().Matches(fragment))
            {
                var path = m.Value.Trim().TrimEnd('.', ',', ')', ']', '}', '"', '\'');
                try
                {
                    // If it looks like a file (has an extension), use its directory.
                    var dir = Path.HasExtension(path) ? Path.GetDirectoryName(path) : path;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        dirs.Add(Path.GetFullPath(dir));
                }
                catch
                {
                    // Ignore malformed paths.
                }
            }
        }

        // Prefer deeper directories first (more specific).
        return dirs.OrderByDescending(d => d.Length).ToList();
    }

    private static string? FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            try
            {
                if (dir.EnumerateFiles("*.sln").Any()
                    || dir.EnumerateFiles("*.slnx").Any()
                    || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }
            }
            catch
            {
                // Permission issues — stop walking up this branch.
                return null;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
