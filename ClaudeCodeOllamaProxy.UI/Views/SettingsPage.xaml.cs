using ClaudeCodeOllamaProxy.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ClaudeCodeOllamaProxy.UI.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading;

    public SettingsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e) => LoadFromSettings();

    private void LoadFromSettings()
    {
        _loading = true;

        StartupToggle.IsOn = StartupManager.IsEnabled;
        SelectThemeItem(App.Settings.Theme);
        MinimizeToTrayToggle.IsOn = App.Settings.MinimizeToTrayOnClose;
        StartMinimizedToggle.IsOn = App.Settings.StartMinimizedToTray;
        UpdateCheckToggle.IsOn = App.Settings.CheckForUpdatesOnStartup;
        PrereleaseToggle.IsOn = App.Settings.IncludePrereleaseUpdates;

        AdminToggle.IsOn = App.Settings.RunAsAdmin;
        var elevated = ElevationHelper.IsElevated();
        RestartAdminButton.IsEnabled = !elevated;
        AdminStatusText.Visibility = elevated ? Visibility.Visible : Visibility.Collapsed;

        _loading = false;
    }

    private void SelectThemeItem(string theme)
    {
        foreach (var item in ThemeCombo.Items.OfType<ComboBoxItem>())
        {
            if ((item.Tag as string) == theme)
            {
                ThemeCombo.SelectedItem = item;
                return;
            }
        }

        ThemeCombo.SelectedIndex = 0; // System default
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;

        if ((ThemeCombo.SelectedItem as ComboBoxItem)?.Tag is string theme)
        {
            App.Settings.Theme = theme;
            App.Current?.ApplyTheme();
        }
    }

    private void AdminToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        App.Settings.RunAsAdmin = AdminToggle.IsOn;
    }

    private void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        App.Settings.MinimizeToTrayOnClose = MinimizeToTrayToggle.IsOn;
    }

    private void StartMinimizedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        App.Settings.StartMinimizedToTray = StartMinimizedToggle.IsOn;
    }

    private void UpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        App.Settings.CheckForUpdatesOnStartup = UpdateCheckToggle.IsOn;
    }

    private void PrereleaseToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        App.Settings.IncludePrereleaseUpdates = PrereleaseToggle.IsOn;
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Reset settings?",
            Content = "This restores all settings to their defaults.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            // ContentDialog renders in a popup that doesn't inherit the page theme — set it explicitly.
            RequestedTheme = ActualTheme,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        App.Settings.ResetToDefaults();
        StartupManager.Disable();          // run-at-startup lives in the registry, not the settings file
        App.Current?.ApplyTheme();          // re-apply default theme to window + tray
        (App.MainWindow as MainWindow)?.ApplyNavSettings();

        // Log level and CLI debug live on the Logs page but are pushed into the running host's
        // configuration, so re-apply the defaults there too (those pages reload on next navigation).
        App.ProxyController.ApplyLogLevel();
        App.ProxyController.ApplyClaudeCliDebug();

        LoadFromSettings();                 // refresh this page's controls to the defaults
    }

    private void RestartAdminButton_Click(object sender, RoutedEventArgs e) =>
        App.Current?.RestartAsAdministrator();

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        if (StartupToggle.IsOn)
            StartupManager.Enable();
        else
            StartupManager.Disable();
    }

    private void ResetWindowSizeButton_Click(object sender, RoutedEventArgs e) =>
        (App.MainWindow as MainWindow)?.ResetWindowSize();
}
