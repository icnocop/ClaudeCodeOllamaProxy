using System.Reflection;
using Karambolo.Extensions.Logging.File;

namespace ClaudeCodeOllamaProxy.Service.Logging;

/// <summary>
/// Supplies extra log-file path placeholders that Karambolo doesn't provide natively:
/// <c>&lt;appname&gt;</c> (assembly name) and <c>&lt;launch&gt;</c> (a per-process timestamp token,
/// giving a unique file for each launch). Built-in <c>&lt;date&gt;</c>/<c>&lt;counter&gt;</c> are
/// delegated back to the context.
/// </summary>
public static class LaunchPlaceholderResolver
{
    /// <summary>Captured once per process so every file from a single launch shares the token.</summary>
    public static readonly string LaunchToken = DateTime.Now.ToString("yyyyMMdd-HHmmss");

    public static readonly string AppName =
        Assembly.GetEntryAssembly()?.GetName().Name ?? "ClaudeCodeOllamaProxy";

    public static string? Resolve(string placeholderName, string? inlineFormat, ILogFilePathFormatContext context)
        => placeholderName switch
        {
            "date" => context.FormatDate(inlineFormat!),
            "counter" => context.FormatCounter(inlineFormat!),
            "appname" => AppName,
            "launch" => LaunchToken,
            _ => null,
        };

    /// <summary>
    /// Best-effort retention: keep only the newest <paramref name="keep"/> log files matching the
    /// app's pattern (Karambolo has no native MaxFiles). Run once at startup.
    /// </summary>
    public static void SweepOldLogs(string logsDirectory, int keep)
    {
        try
        {
            if (!Directory.Exists(logsDirectory)) return;

            var files = new DirectoryInfo(logsDirectory)
                .EnumerateFiles($"{AppName}-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Math.Max(0, keep))
                .ToList();

            foreach (var file in files)
            {
                try { file.Delete(); }
                catch { /* file may be in use; ignore */ }
            }
        }
        catch { /* never fail startup over log cleanup */ }
    }
}
