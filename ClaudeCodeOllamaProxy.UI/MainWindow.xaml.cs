using ClaudeCodeOllamaProxy.UI.Services;
using ClaudeCodeOllamaProxy.UI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClaudeCodeOllamaProxy.UI;

public sealed partial class MainWindow : Window
{
    private const uint WM_SETICON = 0x0080;
    private const nuint ICON_SMALL = 0;   // the small (title-bar) icon slot

    // Rendered large so the title bar always downscales it — crisp at any DPI.
    private const int TitleBarIconSize = 64;

    private readonly DispatcherQueue _dispatcher;
    private readonly HWND _hwnd;
    private bool _detached;

    // Plain brand icon (no status circle) shown in the title bar. Created once, freed on exit.
    private IntPtr _titleBarIcon;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Claude Code Ollama Proxy";
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this));

        // Set the window/taskbar icon to the brand glyph + status circle, and keep it in sync with the host.
        App.ProxyController.StateChanged += OnStateChanged;
        RefreshStatusIcon();

        // Center on first launch; restore the saved position/size on later launches.
        WindowPlacementHelper.ApplyInitialPlacement(AppWindow, App.Settings);

        // Restore the saved navigation-pane width and open/collapsed state.
        ApplyNavSettings();
    }

    /// <summary>
    /// Update the window icons for the current host state. The taskbar / alt-tab icon (Win32 "big" icon,
    /// set via <see cref="AppWindow"/>) carries the status circle — a multi-resolution .ico so it stays
    /// crisp at every DPI. The title-bar icon (Win32 "small" icon) is then overridden with the plain brand
    /// glyph so the status circle shows only in the taskbar and tray, not in the title bar.
    /// </summary>
    public void RefreshStatusIcon()
    {
        if (_detached)
            return;

        try
        {
            AppWindow.SetIcon(StatusIconRenderer.EnsureIconFile(App.ProxyController.State));
        }
        catch
        {
            // The window may be closing/closed during shutdown, or the icon file couldn't be written —
            // a missing status overlay isn't worth crashing over.
        }

        ApplyPlainTitleBarIcon();
    }

    /// <summary>
    /// Override just the title-bar (small) icon with the plain brand glyph. Re-applied after every
    /// <see cref="AppWindow"/>.SetIcon, which otherwise sets both the small and big icons to the composite.
    /// </summary>
    private void ApplyPlainTitleBarIcon()
    {
        if (_hwnd.IsNull)
            return;

        if (_titleBarIcon == IntPtr.Zero)
            _titleBarIcon = StatusIconRenderer.CreatePlainIcon(TitleBarIconSize);

        PInvoke.SendMessage(_hwnd, WM_SETICON, ICON_SMALL, _titleBarIcon);
    }

    /// <summary>Stop reacting to host state changes and free the title-bar icon handle — call before exit.</summary>
    public void DetachStatusIcon()
    {
        _detached = true;
        App.ProxyController.StateChanged -= OnStateChanged;

        if (_titleBarIcon != IntPtr.Zero)
        {
            PInvoke.DestroyIcon(new HICON(_titleBarIcon));
            _titleBarIcon = IntPtr.Zero;
        }
    }

    private void OnStateChanged()
    {
        if (_detached)
            return;
        _dispatcher.TryEnqueue(() =>
        {
            RefreshStatusIcon();
            UpdateHomeStatusDot();
        });
    }

    // The status circle overlaid on the Home nav-item icon (mirrors the tray/taskbar indicator).
    private Microsoft.UI.Xaml.Shapes.Ellipse? _homeStatusDot;

    private void HomeNavItem_Loaded(object sender, RoutedEventArgs e)
    {
        InjectHomeStatusDot();
        UpdateHomeStatusDot();
    }

    /// <summary>
    /// Overlay a status circle on the Home nav-item icon, bottom-right. The icon is hosted in the item
    /// presenter's <c>Viewbox</c>; we wrap that Viewbox's content in a Grid and add the dot as an overlay,
    /// so it scales with the icon and shows in both the expanded and compact pane modes. Keeping the
    /// original vector <see cref="SymbolIcon"/> means it still themes (light/dark/selected) correctly.
    /// </summary>
    private void InjectHomeStatusDot()
    {
        if (_homeStatusDot is not null)
            return;   // already injected

        if (FindAncestor<Microsoft.UI.Xaml.Controls.Viewbox>(HomeSymbolIcon) is not { } iconBox)
        {
            // Template not ready yet — retry once on the next tick.
            _dispatcher.TryEnqueue(InjectHomeStatusDot);
            return;
        }

        var inner = iconBox.Child;
        iconBox.Child = null;

        var grid = new Grid();
        if (inner is not null)
            grid.Children.Add(inner);

        _homeStatusDot = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 11,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            // Soft white ring for separation from the glyph, matching the tray/taskbar icon.
            Stroke = new SolidColorBrush(StatusPalette.Ring.ToWindows()),
            StrokeThickness = 1,
        };
        grid.Children.Add(_homeStatusDot);

        iconBox.Child = grid;
    }

    private void UpdateHomeStatusDot()
    {
        if (_homeStatusDot is null)
            return;
        _homeStatusDot.Fill = new SolidColorBrush(StatusPalette.For(App.ProxyController.State).Fill.ToWindows());
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : class
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    /// <summary>Apply the saved navigation-pane width and open/collapsed state to the view.</summary>
    public void ApplyNavSettings()
    {
        Nav.OpenPaneLength = App.Settings.NavPaneLength;
        Nav.IsPaneOpen = App.Settings.NavPaneOpen;
    }

    /// <summary>Resize the window to the default size and re-center it, keeping the nav-pane state.</summary>
    public void ResetWindowSize() => WindowPlacementHelper.ResizeToDefault(AppWindow);

    /// <summary>Persist window placement and navigation-pane state. Called when the window hides or the app exits.</summary>
    public void PersistState()
    {
        WindowPlacementHelper.Save(AppWindow, App.Settings);
        App.Settings.NavPaneOpen = Nav.IsPaneOpen;
        App.Settings.NavPaneLength = Nav.OpenPaneLength;
    }

    private void Nav_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Start on Home.
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "home":
                ContentFrame.Navigate(typeof(HomePage));
                break;
            case "logs":
                ContentFrame.Navigate(typeof(LogsPage));
                break;
            case "settings":
                ContentFrame.Navigate(typeof(SettingsPage));
                break;
            case "about":
                ContentFrame.Navigate(typeof(AboutPage));
                break;
        }
    }

    /// <summary>Select the Settings item (used by the Home page's "Settings" link).</summary>
    public void NavigateToSettings() => Nav.SelectedItem = SettingsNavItem;
}
