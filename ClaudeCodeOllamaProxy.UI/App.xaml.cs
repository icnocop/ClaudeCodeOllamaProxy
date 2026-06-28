using ClaudeCodeOllamaProxy.UI.Services;
using ClaudeCodeOllamaProxy.UI.Views;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace ClaudeCodeOllamaProxy.UI;

public partial class App : Application
{
    /// <summary>The single main window. Hidden (not closed) when the user clicks the window's close button.</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>When true (default), closing the window hides it instead of exiting. Set false on Quit.</summary>
    public static bool HandleClosedEvents { get; set; } = true;

    /// <summary>Shared in-process proxy host controller, used by the tray menu and all pages.</summary>
    public static ProxyHostController ProxyController { get; private set; } = null!;

    /// <summary>Shared user settings (port, etc.).</summary>
    public static SettingsStore Settings { get; private set; } = null!;

    private TrayIconView? _trayIcon;
    private DispatcherQueue? _dispatcher;
    private UISettings? _uiSettings;

    public App()
    {
        // Capture UI-thread exceptions before WinUI swallows them into an opaque "stowed exception"
        // (0xC000027B). Process-wide handlers (AppDomain / TaskScheduler) are registered earlier in
        // Program.Main so they also cover failures before the XAML layer is up.
        UnhandledException += OnUnhandledException;

        InitializeComponent();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        // Leave e.Handled = false so the process still terminates as before — but now the real exception
        // (type, message, stack) is recorded to crash.log instead of vanishing into the stowed-exception code.
        CrashLog.Report("Application.UnhandledException", e.Exception, e.Message);

    /// <summary>Strongly-typed accessor for the running app instance.</summary>
    public static new App? Current => (App?)Application.Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        Settings = new SettingsStore();
        ProxyController = new ProxyHostController(Settings);

        // Start serving immediately so the proxy is live whether or not the window is shown.
        _ = ProxyController.StartAsync();

        MainWindow = new MainWindow();
        MainWindow.Closed += OnMainWindowClosed;

        // Create the tray icon and keep a reference so it isn't garbage-collected while the app runs.
        _trayIcon = new TrayIconView();
        _trayIcon.TrayIcon.ForceCreate();

        // Render the initial icon (brand glyph + status circle) now that the tray icon exists; it then
        // re-renders on every host state change via TrayIconView.OnStateChanged.
        _trayIcon.RefreshIcon();

        // Apply the saved theme to the window + tray icon, and react to live system theme changes.
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += (_, _) => _dispatcher?.TryEnqueue(ApplyTheme);
        ApplyTheme();

        // Launched at login (registry "--startup") → stay hidden in the tray; otherwise show the window.
        if (!IsStartupLaunch())
            ShowMainWindow();
    }

    /// <summary>Apply the configured theme (System/Light/Dark) to the main window and the tray icon.</summary>
    public void ApplyTheme()
    {
        var setting = Settings.Theme;

        if (MainWindow?.Content is FrameworkElement root)
            root.RequestedTheme = ThemeHelper.ToElementTheme(setting);

        // The title bar is non-client area and doesn't follow RequestedTheme — color it explicitly.
        if (MainWindow?.AppWindow is { } appWindow)
            TitleBarThemeHelper.Apply(appWindow, ThemeHelper.IsEffectivelyLight(setting));

        // The tray icon is theme-independent (brand glyph + status circle), so it isn't touched here —
        // it's composed by TrayIconView based on the proxy host state.
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs e)
    {
        // Remember window placement + nav-pane state, both when hiding (X button) and exiting (Quit).
        (sender as MainWindow)?.PersistState();

        // An orderly Quit is already in progress (ExitApplication cleared the flag) — allow the close.
        if (!HandleClosedEvents)
            return;

        e.Handled = true;

        if (Settings.MinimizeToTrayOnClose)
            MainWindow?.Hide();   // keep running in the tray
        else
            ExitApplication();    // closing quits the app
    }

    /// <summary>
    /// Show and foreground the main window (used by left-click, the tray "Open" item, and re-launch).
    /// Marshaled to the main UI thread because the tray context menu runs on a separate thread.
    /// </summary>
    public void ShowMainWindow() => RunOnUi(() =>
    {
        if (MainWindow is null)
            return;

        MainWindow.Show();
        MainWindow.Activate();

        // Activate() alone doesn't pull the window in front of other apps' windows (or un-minimize it),
        // so force it to the foreground.
        WindowActivationHelper.BringToFront(MainWindow);
    });

    /// <summary>Copy the current listening URL to the clipboard (on the UI thread).</summary>
    public void CopyUrlToClipboard() => RunOnUi(() => ClipboardHelper.SetText(ProxyController.ListeningUrl));

    /// <summary>Start the in-process proxy host (no-op if already running). Marshaled to the UI thread
    /// because the tray context menu runs on a separate thread.</summary>
    public void StartProxy() => RunOnUi(async () => await ProxyController.StartAsync());

    /// <summary>Stop the in-process proxy host (no-op if already stopped).</summary>
    public void StopProxy() => RunOnUi(async () => await ProxyController.StopAsync());

    /// <summary>Restart the in-process proxy host (picks up a changed port).</summary>
    public void RestartProxy() => RunOnUi(async () => await ProxyController.RestartAsync());

    /// <summary>Relaunch the app elevated and exit this instance so the elevated one takes over.</summary>
    public void RestartAsAdministrator()
    {
        if (ElevationHelper.RestartElevated())
            ExitApplication();
    }

    /// <summary>Called when a second instance is launched and redirects its activation to this one.</summary>
    public void OnRedirectedActivation() => ShowMainWindow();

    /// <summary>Tear everything down and exit the process (invoked by the tray "Quit" item).</summary>
    public void ExitApplication() => RunOnUi(async () =>
    {
        HandleClosedEvents = false;

        // Detach state-change handlers before stopping/disposing so the stop's StateChanged doesn't
        // try to update an already-disposed tray icon or closing window.
        _trayIcon?.DetachEvents();
        (MainWindow as MainWindow)?.DetachStatusIcon();

        try
        {
            await ProxyController.StopAsync();
        }
        catch
        {
            // Ignore shutdown errors on exit.
        }

        _trayIcon?.TrayIcon.Dispose();
        MainWindow?.Close();
        Exit();
    });

    private void RunOnUi(DispatcherQueueHandler action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(action);
    }

    private static bool IsStartupLaunch() =>
        Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, StartupManager.StartupArgument, StringComparison.OrdinalIgnoreCase));
}
