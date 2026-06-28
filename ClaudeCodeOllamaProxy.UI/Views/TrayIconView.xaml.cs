using ClaudeCodeOllamaProxy.UI.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClaudeCodeOllamaProxy.UI.Views;

/// <summary>
/// Hosts the system-tray icon and its right-click context menu (Open / Copy URL / Quit). The menu items
/// use Click handlers (not command bindings) because the menu renders in a separate window where
/// <c>{x:Bind}</c> commands don't resolve; the actions are marshaled onto the main UI thread by <see cref="App"/>.
/// </summary>
public sealed partial class TrayIconView : UserControl
{
    private readonly DispatcherQueue _dispatcher;
    private bool _detached;

    // The HICON currently shown by the tray. Kept alive until replaced (the shell references it), then
    // freed so the per-state recomposition doesn't leak GDI handles.
    private IntPtr _currentIcon;

    public TrayIconView()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // Assign the left-click command in code: {x:Bind} on the detached tray UserControl doesn't
        // reliably wire it (same reason the menu uses Click handlers). Single left-click opens the window.
        TrayIcon.LeftClickCommand = ShowWindowCommand;

        App.ProxyController.StateChanged += OnStateChanged;
        UpdateTooltip();
    }

    /// <summary>
    /// Compose the tray icon for the current host state (status circle overlay) and push it to the tray.
    /// Must be called after <c>ForceCreate()</c>, and on every subsequent state change.
    /// </summary>
    public void RefreshIcon()
    {
        if (_detached)
            return;

        var newIcon = StatusIconRenderer.CreateIcon(App.ProxyController.State);
        try
        {
            TrayIcon.TrayIcon.UpdateIcon(newIcon);
        }
        catch (ObjectDisposedException)
        {
            PInvoke.DestroyIcon(new HICON(newIcon));   // Tray is gone (app exiting) — don't leak the handle we just made.
            return;
        }

        var previous = _currentIcon;
        _currentIcon = newIcon;
        if (previous != IntPtr.Zero)
            PInvoke.DestroyIcon(new HICON(previous));
    }

    /// <summary>Stop reacting to host state changes — call before disposing the tray icon on exit.</summary>
    public void DetachEvents()
    {
        _detached = true;
        App.ProxyController.StateChanged -= OnStateChanged;

        if (_currentIcon != IntPtr.Zero)
        {
            PInvoke.DestroyIcon(new HICON(_currentIcon));
            _currentIcon = IntPtr.Zero;
        }
    }

    // Left-click runs on the icon's own (main) thread, so a command binding is fine here.
    [RelayCommand]
    private void ShowWindow() => App.Current?.ShowMainWindow();

    private void OpenItem_Click(object sender, RoutedEventArgs e) => App.Current?.ShowMainWindow();

    private void CopyUrlItem_Click(object sender, RoutedEventArgs e) => App.Current?.CopyUrlToClipboard();

    private void StartItem_Click(object sender, RoutedEventArgs e) => App.Current?.StartProxy();

    private void StopItem_Click(object sender, RoutedEventArgs e) => App.Current?.StopProxy();

    private void RestartItem_Click(object sender, RoutedEventArgs e) => App.Current?.RestartProxy();

    private void QuitItem_Click(object sender, RoutedEventArgs e) => App.Current?.ExitApplication();

    private void ContextMenu_Opening(object sender, object e)
    {
        // Show the current URL alongside "Copy URL".
        CopyUrlItem.Text = $"Copy URL  ({App.ProxyController.ListeningUrl})";

        // Enable only the actions that make sense for the current host state; everything is disabled
        // mid-transition (Starting/Stopping) to avoid overlapping start/stop calls.
        var state = App.ProxyController.State;
        StartItem.IsEnabled = state == ProxyState.Stopped;
        StopItem.IsEnabled = state == ProxyState.Running;
        RestartItem.IsEnabled = state == ProxyState.Running;
    }

    private void OnStateChanged()
    {
        if (_detached)
            return;
        _dispatcher.TryEnqueue(() =>
        {
            UpdateTooltip();
            RefreshIcon();
        });
    }

    private void UpdateTooltip()
    {
        if (_detached)
            return;

        var controller = App.ProxyController;
        var status = controller.IsRunning ? "Running" : "Stopped";
        try
        {
            TrayIcon.ToolTipText = $"Claude Code Ollama Proxy\n{status} — {controller.ListeningUrl}";
        }
        catch (ObjectDisposedException)
        {
            // The tray icon was disposed (app is exiting) — nothing to update.
        }
    }
}
