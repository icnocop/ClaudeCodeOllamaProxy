using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace ClaudeCodeOllamaProxy.UI.Tests.Infrastructure;

/// <summary>
/// Launches the built ClaudeCodeOllamaProxy.UI app under FlaUI for screenshot/UI tests, in an isolated
/// settings directory (no UAC, fixed theme/port), and tears it down. Single-instance means any existing
/// instance is closed first, otherwise our launch would be redirected and exit.
/// </summary>
public sealed class AppDriver : IDisposable
{
    private const string ProcessName = "ClaudeCodeOllamaProxy.UI";
    private const string WindowTitle = "Claude Code Ollama Proxy";

    private readonly Application _app;
    private readonly UIA3Automation _automation;

    public Window Window { get; }

    private AppDriver(Application app, UIA3Automation automation, Window window)
    {
        _app = app;
        _automation = automation;
        Window = window;
    }

    public static AppDriver Launch(string dataDir)
    {
        KillExisting();

        var exe = ResolveExePath();
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        startInfo.Environment["CLAUDECODEOLLAMAPROXY_UI_DATADIR"] = dataDir;

        var app = Application.Launch(startInfo);
        var automation = new UIA3Automation();

        var window = Retry.WhileNull(
            () => app.GetMainWindow(automation, TimeSpan.FromSeconds(2)) is { } w
                  && string.Equals(w.Title, WindowTitle, StringComparison.Ordinal) ? w : null,
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(500)).Result
            ?? throw new InvalidOperationException(
                "ClaudeCodeOllamaProxy.UI main window did not appear. Is Developer Mode enabled?");

        return new AppDriver(app, automation, window);
    }

    /// <summary>The repo root (the folder containing ClaudeCodeOllamaProxy.slnx).</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeCodeOllamaProxy.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate ClaudeCodeOllamaProxy.slnx above the test output.");
    }

    private static string ResolveExePath()
    {
        if (Environment.GetEnvironmentVariable("CCOP_UI_EXE") is { Length: > 0 } overridePath && File.Exists(overridePath))
            return overridePath;

        var uiBin = Path.Combine(RepoRoot(), "ClaudeCodeOllamaProxy.UI", "bin");
        if (!Directory.Exists(uiBin))
            throw new InvalidOperationException($"'{uiBin}' not found — build ClaudeCodeOllamaProxy.UI (x64) first.");

        var exe = Directory.EnumerateFiles(uiBin, $"{ProcessName}.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return exe ?? throw new InvalidOperationException(
            $"'{ProcessName}.exe' not found under '{uiBin}' — build ClaudeCodeOllamaProxy.UI (x64) first.");
    }

    private static void KillExisting()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
                // best effort
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public void Dispose()
    {
        try
        {
            // Close the main window so the app exits via its clean Quit path (disposing the tray icon),
            // rather than killing the process and leaving a ghost tray icon. Requires the test to have
            // turned off "minimize to tray on close" first. Close() kills as a fallback if that fails.
            if (!_app.HasExited)
                _app.Close();
        }
        catch
        {
            // ignore teardown errors
        }

        try
        {
            if (!_app.HasExited)
                _app.Kill();
        }
        catch
        {
            // ignore teardown errors
        }

        _automation.Dispose();
    }
}
