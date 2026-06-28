using System.IO;
using ClaudeCodeOllamaProxy.UI.Tests.Infrastructure;
using ClaudeCodeOllamaProxy.UI.Tests.PageObjects;
using Xunit;

namespace ClaudeCodeOllamaProxy.UI.Tests;

public sealed class ScreenshotTests
{
    // Each page is shown at a size that suits its content (window-rect pixels).
    private static readonly (string Name, Action<MainWindowShell> Navigate, int Width, int Height)[] Pages =
    [
        ("home", s => s.GoHome(), 740, 400),
        ("logs", s => s.GoLogs(), 800, 500),
        ("settings", s => s.GoSettings(), 700, 900),
    ];

    // (theme setting value shown in the ComboBox, file-name suffix)
    private static readonly (string ComboText, string Suffix)[] Themes =
    [
        ("Light", "light"),
        ("Dark", "dark"),
    ];

    [Fact]
    public void Capture_All_Pages_In_Light_And_Dark()
    {
        // Isolated settings: no UAC (RunAsAdmin=false), a non-default port to avoid clashing with a real
        // proxy, and MinimizeToTrayOnClose=false so closing the window quits the app cleanly on teardown
        // (which removes the tray icon instead of leaving a ghost icon behind).
        var dataDir = Path.Combine(Path.GetTempPath(), "ccop-ui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(
            Path.Combine(dataDir, "settings.json"),
            """{ "Port": 18434, "Theme": "Light", "RunAsAdmin": false, "MinimizeToTrayOnClose": false }""");

        var outDir = Path.Combine(AppDriver.RepoRoot(), "docs", "screenshots");
        Directory.CreateDirectory(outDir);

        var driver = AppDriver.Launch(dataDir);
        try
        {
            var window = driver.Window;
            var shell = new MainWindowShell(window);

            // Position + size to a known on-screen rectangle so all nav items (incl. the footer
            // Settings/About) are clickable — FlaUI clicks at screen coordinates, so off-screen items
            // can't be hit.
            Screenshotter.Resize(window, 700, 660);
            Thread.Sleep(400);

            foreach (var (comboText, suffix) in Themes)
            {
                shell.GoSettings();
                Thread.Sleep(400);
                shell.Settings.SetTheme(comboText);
                Thread.Sleep(700); // let the theme apply to the window + title bar

                foreach (var (name, navigate, width, height) in Pages)
                {
                    navigate(shell);
                    Screenshotter.Resize(window, width, height); // size to suit this page
                    window.Focus();
                    Thread.Sleep(700); // let the page re-layout/render before capturing

                    var file = Path.Combine(outDir, $"{name}-{suffix}.png");
                    Screenshotter.Capture(window, file); // trims invisible borders + rounds the corners
                }
            }
        }
        finally
        {
            driver.Dispose();
            try
            {
                Directory.Delete(dataDir, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }

        foreach (var (_, suffix) in Themes)
        {
            foreach (var page in Pages)
            {
                var file = Path.Combine(outDir, $"{page.Name}-{suffix}.png");
                Assert.True(File.Exists(file), $"Missing screenshot: {file}");
                Assert.True(new FileInfo(file).Length > 0, $"Empty screenshot: {file}");
            }
        }
    }
}
