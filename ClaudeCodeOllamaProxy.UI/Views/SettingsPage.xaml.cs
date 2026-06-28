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
        PortBox.Value = App.Settings.Port;
        SelectThemeItem(App.Settings.Theme);
        MinimizeToTrayToggle.IsOn = App.Settings.MinimizeToTrayOnClose;

        AdminToggle.IsOn = App.Settings.RunAsAdmin;
        var elevated = ElevationHelper.IsElevated();
        RestartAdminButton.IsEnabled = !elevated;
        AdminStatusText.Visibility = elevated ? Visibility.Visible : Visibility.Collapsed;

        UpdatePortRestartBar();

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

        LoadFromSettings();                 // refresh all controls to the defaults
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

    private void PortBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading)
            return;

        // Clearing the box makes Value NaN before InvalidInputOverwritten reverts the text on focus loss;
        // (int)NaN is garbage (e.g. int.MinValue), which would persist and later bind a random port. Restore
        // the last valid port and skip persisting until the control overwrites the empty input itself.
        if (double.IsNaN(args.NewValue))
        {
            PortBox.Value = App.Settings.Port;
            return;
        }

        // InvalidInputOverwritten + Minimum/Maximum guarantees any other value is an in-range number, so just
        // persist it. The host stays on its current port until an explicit restart.
        var port = (int)args.NewValue;
        if (port != App.Settings.Port)
            App.Settings.Port = port;

        UpdatePortRestartBar();
    }

    private void PortDefaultButton_Click(object sender, RoutedEventArgs e) =>
        PortBox.Value = SettingsStore.DefaultPort; // raises ValueChanged → persists + updates the restart bar

    private void ResetWindowSizeButton_Click(object sender, RoutedEventArgs e) =>
        (App.MainWindow as MainWindow)?.ResetWindowSize();

    private async void RestartProxyButton_Click(object sender, RoutedEventArgs e)
    {
        await App.ProxyController.RestartAsync();
        UpdatePortRestartBar();
    }

    private void UpdatePortRestartBar() =>
        PortRestartBar.IsOpen = App.Settings.Port != App.ProxyController.RunningPort;
}
