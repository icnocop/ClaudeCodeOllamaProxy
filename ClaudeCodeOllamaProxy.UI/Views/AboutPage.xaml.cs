using ClaudeCodeOllamaProxy.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace ClaudeCodeOllamaProxy.UI.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        VersionText.Text = $"Version {AppVersion.Display}";

        // A check may already have run at startup, before this page existed — show its result, then keep
        // following it (a startup check can still be in flight when the user opens this page).
        ApplyUpdateState(UpdateChecker.Known);
        UpdateChecker.UpdateStateChanged += OnUpdateStateChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) =>
        UpdateChecker.UpdateStateChanged -= OnUpdateStateChanged;

    private void OnUpdateStateChanged(UpdateInfo? info) =>
        DispatcherQueue.TryEnqueue(() => ApplyUpdateState(info));

    private void ApplyUpdateState(UpdateInfo? info)
    {
        if (info is null)
        {
            UpdateBar.IsOpen = false;
            return;
        }

        UpdateBar.Message = $"Version {info.DisplayVersion} is available. You're running {AppVersion.Display}.";
        UpdateBar.IsOpen = true;
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckProgress.IsActive = true;
        UpdateStatusText.Visibility = Visibility.Collapsed;

        try
        {
            var info = await UpdateChecker.CheckAsync(App.Settings.IncludePrereleaseUpdates);

            // The InfoBar is already updated (via UpdateStateChanged); this adds the "nothing to do"
            // wording, which a closed InfoBar can't convey.
            if (info is null)
                ShowStatus("You're using the latest version.");
            else
                await UpdateDialog.ShowAsync(XamlRoot, ActualTheme, info);
        }
        catch (Exception ex)
        {
            // Offline, rate-limited, or GitHub is down — say so rather than implying "up to date".
            ShowStatus($"Couldn't check for updates: {ex.Message}");
        }
        finally
        {
            CheckProgress.IsActive = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string text)
    {
        UpdateStatusText.Text = text;
        UpdateStatusText.Visibility = Visibility.Visible;
    }

    private async void ViewReleaseButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(UpdateChecker.Known?.HtmlUrl ?? UpdateChecker.ReleasesUrl));
}
