using ClaudeCodeOllamaProxy.UI.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ClaudeCodeOllamaProxy.UI.Views;

public sealed partial class HomePage : Page
{
    // Guards the control event handlers while the page pushes persisted settings into them.
    private bool _loading;

    private ProxyHostController Controller => App.ProxyController;

    public HomePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        LoadFromSettings();
        Controller.StateChanged += OnStateChanged;
        UpdateState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Controller.StateChanged -= OnStateChanged;
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            PortBox.Value = App.Settings.Port;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnStateChanged() => DispatcherQueue.TryEnqueue(UpdateState);

    private void UpdateState()
    {
        var running = Controller.IsRunning;
        StatusText.Text = running ? "Running" : "Stopped";
        StatusDot.Fill = new SolidColorBrush(running ? Colors.SeaGreen : Colors.Gray);
        UrlBox.Text = Controller.ListeningUrl;

        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        RestartButton.IsEnabled = running;

        // RunningPort only changes with the host's lifecycle, so re-evaluate the pending-port notice here.
        UpdatePortRestartBar();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) =>
        ClipboardHelper.SetText(Controller.ListeningUrl);

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

    private async void RestartProxyButton_Click(object sender, RoutedEventArgs e)
    {
        await Controller.RestartAsync();
        UpdatePortRestartBar();
    }

    private void UpdatePortRestartBar() =>
        PortRestartBar.IsOpen = App.Settings.Port != Controller.RunningPort;

    private async void StartButton_Click(object sender, RoutedEventArgs e) =>
        await Controller.StartAsync();

    private async void StopButton_Click(object sender, RoutedEventArgs e) =>
        await Controller.StopAsync();

    private async void RestartButton_Click(object sender, RoutedEventArgs e) =>
        await Controller.RestartAsync();
}
