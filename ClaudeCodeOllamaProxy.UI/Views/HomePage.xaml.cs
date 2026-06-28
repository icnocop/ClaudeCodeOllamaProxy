using ClaudeCodeOllamaProxy.UI.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ClaudeCodeOllamaProxy.UI.Views;

public sealed partial class HomePage : Page
{
    private ProxyHostController Controller => App.ProxyController;

    public HomePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Controller.StateChanged += OnStateChanged;
        UpdateState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Controller.StateChanged -= OnStateChanged;
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
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) =>
        ClipboardHelper.SetText(Controller.ListeningUrl);

    private void SettingsLink_Click(object sender, RoutedEventArgs e) =>
        (App.MainWindow as MainWindow)?.NavigateToSettings();

    private async void StartButton_Click(object sender, RoutedEventArgs e) =>
        await Controller.StartAsync();

    private async void StopButton_Click(object sender, RoutedEventArgs e) =>
        await Controller.StopAsync();

    private async void RestartButton_Click(object sender, RoutedEventArgs e) =>
        await Controller.RestartAsync();
}
