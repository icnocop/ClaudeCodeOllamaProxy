using ClaudeCodeOllamaProxy.UI.Tests.Infrastructure;
using ClaudeCodeOllamaProxy.UI.Tests.PageObjects;
using Xunit;

namespace ClaudeCodeOllamaProxy.UI.Tests;

/// <summary>
/// Captures the README screenshots for each page in light and dark themes. There's one test per page; each
/// launches the app once per theme with the theme already configured in settings.json (so it's applied at
/// startup) and captures that page. The app is single-instance, so the tests run sequentially.
/// </summary>
public sealed class ScreenshotTests
{
    // (theme setting value written to settings.json, file-name suffix)
    private static readonly (string Theme, string Suffix)[] Themes =
    [
        ("Light", "light"),
        ("Dark", "dark"),
    ];

    // Wide enough for the port card's spinner + Default button to sit beside its caption.
    [Fact]
    public void Capture_Home() => CapturePage("home", s => s.GoHome(), 820, 540);

    // Extra height over Home for the verbosity and CLI-debug cards above the log list.
    [Fact]
    public void Capture_Logs() => CapturePage("logs", s => s.GoLogs(), 820, 700);

    // Taller than the other pages so every card fits in one shot. Height is capped by the monitor work
    // area (~1020px on 1080p — CenteredRect clamps to it), so the width does the work too: the wider the
    // window, the fewer lines each caption wraps to, and the shorter the page gets. Don't shrink this
    // without re-checking that the last card ("Window size and position") is still visible.
    [Fact]
    public void Capture_Settings() => CapturePage("settings", s => s.GoSettings(), 820, 920);

    // The shortest page — one card — so this sits at SettingsStore.MinWindowHeight (420), the tightest
    // crop the app allows; the leftover space below the card can't be trimmed by shrinking the window.
    // The update check is suppressed for test runs (CLAUDECODEOLLAMAPROXY_UI_SKIP_UPDATE_CHECK, see
    // AppDriver), so this captures the resting state: no "Update available" bar below the card.
    [Fact]
    public void Capture_About() => CapturePage("about", s => s.GoAbout(), 820, 420);

    private static void CapturePage(string name, Action<MainWindowShell> navigate, int width, int height)
    {
        var outDir = Path.Combine(AppDriver.RepoRoot(), "docs", "screenshots");
        Directory.CreateDirectory(outDir);

        foreach (var (theme, suffix) in Themes)
        {
            // The window launches at this page's size (configured in settings.json) — no post-launch resize.
            using var app = AppSession.Launch(theme, width, height);

            navigate(app.Shell);
            app.Window.Focus();
            Thread.Sleep(700); // let the page re-layout/render before capturing

            var file = Path.Combine(outDir, $"{name}-{suffix}.png");
            Screenshotter.Capture(app.Window, file); // trims invisible borders + rounds the corners

            Assert.True(File.Exists(file), $"Missing screenshot: {file}");
            Assert.True(new FileInfo(file).Length > 0, $"Empty screenshot: {file}");
        }
    }
}
