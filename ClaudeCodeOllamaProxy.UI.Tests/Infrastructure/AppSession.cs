using System.IO;
using System.Text.Json;
using ClaudeCodeOllamaProxy.UI.Services;
using ClaudeCodeOllamaProxy.UI.Tests.PageObjects;
using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.Infrastructure;

/// <summary>
/// Launches the tray app in an isolated settings dir with a given theme already written to settings.json,
/// so the theme is applied at startup (no in-app navigation needed), and tears it down on dispose. The
/// app is single-instance, so only one session can be alive at a time — each test launches and disposes
/// its own. Isolated settings: no UAC (RunAsAdmin=false), a non-default port to avoid clashing with a real
/// proxy, and MinimizeToTrayOnClose=false so closing the window quits the app cleanly on teardown (which
/// removes the tray icon instead of leaving a ghost behind).
/// </summary>
public sealed class AppSession : IDisposable
{
    private readonly string _dataDir;
    private readonly AppDriver _driver;

    public Window Window => _driver.Window;
    public MainWindowShell Shell { get; }

    private AppSession(string dataDir, AppDriver driver)
    {
        _dataDir = dataDir;
        _driver = driver;
        Shell = new MainWindowShell(driver.Window);
    }

    /// <summary>
    /// Launch the app with <paramref name="theme"/> ("Light"/"Dark"/"System") and the given window size
    /// (window-rect pixels) preconfigured in settings.json, so both are applied at startup. The size is
    /// written as a centered, work-area-clamped saved placement, which also keeps every nav item on-screen
    /// and clickable (FlaUI clicks at screen coordinates, so off-screen items can't be hit).
    /// </summary>
    public static AppSession Launch(string theme, int width, int height)
    {
        var (x, y, w, h) = Screenshotter.CenteredRect(width, height);

        // Build the real settings type the app deserializes, so the test can't drift from the schema.
        var settings = new SettingsStore.Data
        {
            Port = 18434,                    // non-default to avoid clashing with a real Ollama/proxy
            Theme = theme,
            RunAsAdmin = false,              // no UAC prompt
            MinimizeToTrayOnClose = false,   // closing the window quits cleanly on teardown
            Window = new SettingsStore.WindowPlacement(x, y, w, h),
        };

        var dataDir = Path.Combine(Path.GetTempPath(), "ccop-ui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(
            Path.Combine(dataDir, "settings.json"),
            JsonSerializer.Serialize(settings));

        var driver = AppDriver.Launch(dataDir);
        var session = new AppSession(dataDir, driver);
        Thread.Sleep(400);
        return session;
    }

    public void Dispose()
    {
        _driver.Dispose();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
