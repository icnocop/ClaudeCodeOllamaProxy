using System.IO;
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

    [Fact]
    public void Capture_Home() => CapturePage("home", s => s.GoHome(), 650, 442);

    [Fact]
    public void Capture_Logs() => CapturePage("logs", s => s.GoLogs(), 800, 500);

    // Taller than the other pages: the Settings page has grown enough cards (port, startup, two tray
    // toggles, admin, theme, window size) that it needs the extra height to show in one shot.
    [Fact]
    public void Capture_Settings() => CapturePage("settings", s => s.GoSettings(), 700, 960);

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
