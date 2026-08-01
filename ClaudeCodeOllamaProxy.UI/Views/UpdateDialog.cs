using ClaudeCodeOllamaProxy.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace ClaudeCodeOllamaProxy.UI.Views;

/// <summary>
/// The "a new version is available" message box, shared by the About page's manual check and the
/// check that runs at startup so both read identically.
/// </summary>
public static class UpdateDialog
{
    /// <summary>
    /// Show the update notice. <paramref name="root"/> and <paramref name="theme"/> both matter: an
    /// unpackaged app throws without an explicit <c>XamlRoot</c>, and a ContentDialog renders in a popup
    /// that doesn't inherit the page's theme.
    /// </summary>
    public static async Task ShowAsync(XamlRoot root, ElementTheme theme, UpdateInfo info)
    {
        var dialog = new ContentDialog
        {
            Title = "Update available",
            Content = $"Version {info.DisplayVersion} is available.\nYou're running {AppVersion.Display}.",
            PrimaryButtonText = "View release",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
            RequestedTheme = theme,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Launcher.LaunchUriAsync(new Uri(info.HtmlUrl));
    }
}
