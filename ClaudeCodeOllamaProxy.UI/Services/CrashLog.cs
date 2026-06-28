using System.IO;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Last-resort crash logging. WinUI funnels any unhandled managed exception through the XAML layer as an
/// opaque "stowed exception" (0xC000027B) with no usable detail, and an unpackaged release build has no
/// console — so the global handlers in <see cref="App"/> route here to (1) append the real exception
/// (type, message, stack) to %LOCALAPPDATA%\ClaudeCodeOllamaProxy.UI\crash.log and (2) show a native
/// message box pointing the user at that file. Everything here is best-effort and never throws.
/// </summary>
internal static class CrashLog
{
    private static readonly string LogPath = Path.Combine(SettingsStore.DataDirectory, "crash.log");
    private static readonly object Gate = new();

    /// <summary>Full path of the crash log file (shown to the user in the dialog).</summary>
    public static string FilePath => LogPath;

    /// <summary>
    /// Record an unhandled exception: append it to the crash log and notify the user. <paramref name="message"/>
    /// carries the WinUI <c>UnhandledExceptionEventArgs.Message</c>, which often holds the real error text even
    /// when the reconstructed <paramref name="exception"/> has no stack trace.
    /// </summary>
    public static void Report(string source, Exception? exception, string? message = null)
    {
        AppendToFile(source, exception, message);
        ShowDialog(exception, message);
    }

    private static void AppendToFile(string source, Exception? exception, string? message)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDirectory);
            var entry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}" + Environment.NewLine +
                (string.IsNullOrEmpty(message) ? "" : $"Message: {message}" + Environment.NewLine) +
                Describe(exception) + Environment.NewLine +
                new string('-', 80) + Environment.NewLine;

            lock (Gate)
                File.AppendAllText(LogPath, entry);
        }
        catch
        {
            // Crash logging is best-effort; never throw from a crash handler.
        }
    }

    /// <summary>Render an exception and its inner chain, including the HRESULT (the useful part of a
    /// WinUI/COM "stowed" exception whose .ToString() is otherwise bare).</summary>
    private static string Describe(Exception? exception)
    {
        if (exception is null)
            return "(no exception object)";

        var sb = new System.Text.StringBuilder();
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            sb.AppendLine($"{ex.GetType().FullName} (HRESULT 0x{ex.HResult:X8}): {ex.Message}");
            if (!string.IsNullOrEmpty(ex.StackTrace))
                sb.AppendLine(ex.StackTrace);
            if (ex.InnerException is not null)
                sb.AppendLine("--- inner exception ---");
        }
        return sb.ToString().TrimEnd();
    }

    private static void ShowDialog(Exception? exception, string? message)
    {
        try
        {
            var detail = exception is not null ? $"{exception.GetType().Name}: {exception.Message}" : message;
            var text =
                "Claude Code Ollama Proxy hit an unexpected error." + Environment.NewLine + Environment.NewLine +
                detail + Environment.NewLine + Environment.NewLine +
                "Details were written to:" + Environment.NewLine + LogPath;

            PInvoke.MessageBox(HWND.Null, text, "Claude Code Ollama Proxy", MESSAGEBOX_STYLE.MB_ICONERROR);
        }
        catch
        {
            // A best-effort notification; ignore if the message box can't be shown (e.g. session 0).
        }
    }
}
