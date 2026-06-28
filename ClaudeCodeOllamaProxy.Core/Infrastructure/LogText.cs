namespace ClaudeCodeOllamaProxy.Infrastructure;

/// <summary>Helpers for shaping text destined for log output.</summary>
public static class LogText
{
    /// <summary>
    /// Truncate <paramref name="s"/> to <paramref name="max"/> characters for logging, appending a
    /// <c>… (+N chars)</c> suffix when content was dropped. Returns <c>""</c> for null/empty input.
    /// </summary>
    public static string Truncate(string? s, int max = 4000)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + $"… (+{s.Length - max} chars)";
    }
}
