using System.IO;
using ClaudeCodeOllamaProxy.UI.Services;
using ClaudeCodeOllamaProxy.UI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClaudeCodeOllamaProxy.UI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Claude Code Ollama Proxy";

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        // Center on first launch; restore the saved position/size on later launches.
        WindowPlacementHelper.ApplyInitialPlacement(AppWindow, App.Settings);

        // Restore the saved navigation-pane width and open/collapsed state.
        ApplyNavSettings();
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
