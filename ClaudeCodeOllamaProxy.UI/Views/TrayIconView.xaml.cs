using ClaudeCodeOllamaProxy.UI.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    /// <summary>Stop reacting to host state changes — call before disposing the tray icon on exit.</summary>
    public void DetachEvents()
    {
        _detached = true;
        App.ProxyController.StateChanged -= OnStateChanged;
    }

    // Left-click runs on the icon's own (main) thread, so a command binding is fine here.
    [RelayCommand]
    private void ShowWindow() => App.Current?.ShowMainWindow();

    private void OpenItem_Click(object sender, RoutedEventArgs e) => App.Current?.ShowMainWindow();

    private void CopyUrlItem_Click(object sender, RoutedEventArgs e) => App.Current?.CopyUrlToClipboard();

    private void QuitItem_Click(object sender, RoutedEventArgs e) => App.Current?.ExitApplication();

    private void ContextMenu_Opening(object sender, object e)
    {
        // Show the current URL alongside "Copy URL".
        CopyUrlItem.Text = $"Copy URL  ({App.ProxyController.ListeningUrl})";
    }

    private void OnStateChanged()
    {
        if (_detached)
            return;
        _dispatcher.TryEnqueue(UpdateTooltip);
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
