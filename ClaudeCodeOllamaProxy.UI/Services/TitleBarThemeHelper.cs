using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Colors the standard window title bar to match the chosen app theme — the title bar is non-client
/// area and does not follow <c>RequestedTheme</c> on its own, so a Dark app theme on a Light system
/// (or vice versa) would otherwise leave a mismatched title bar.
/// </summary>
public static class TitleBarThemeHelper
{
    public static void Apply(AppWindow appWindow, bool isLight)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
            return;

        var bar = appWindow.TitleBar;

        if (isLight)
        {
            var bg = ColorHelper.FromArgb(255, 243, 243, 243);
            bar.BackgroundColor = bg;
            bar.InactiveBackgroundColor = bg;
            bar.ForegroundColor = ColorHelper.FromArgb(255, 0, 0, 0);
            bar.InactiveForegroundColor = ColorHelper.FromArgb(255, 153, 153, 153);

            // Match the caption-button strip to the bar background (transparent can reveal a lighter default).
            bar.ButtonBackgroundColor = bg;
            bar.ButtonInactiveBackgroundColor = bg;
            bar.ButtonForegroundColor = ColorHelper.FromArgb(255, 0, 0, 0);
            bar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 153, 153, 153);
            bar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 229, 229, 229);
            bar.ButtonHoverForegroundColor = ColorHelper.FromArgb(255, 0, 0, 0);
            bar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 218, 218, 218);
            bar.ButtonPressedForegroundColor = ColorHelper.FromArgb(255, 0, 0, 0);
        }
        else
        {
            var bg = ColorHelper.FromArgb(255, 32, 32, 32);
            bar.BackgroundColor = bg;
            bar.InactiveBackgroundColor = bg;
            bar.ForegroundColor = ColorHelper.FromArgb(255, 255, 255, 255);
            bar.InactiveForegroundColor = ColorHelper.FromArgb(255, 153, 153, 153);

            bar.ButtonBackgroundColor = bg;
            bar.ButtonInactiveBackgroundColor = bg;
            bar.ButtonForegroundColor = ColorHelper.FromArgb(255, 255, 255, 255);
            bar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 153, 153, 153);
            bar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 55, 55, 55);
            bar.ButtonHoverForegroundColor = ColorHelper.FromArgb(255, 255, 255, 255);
            bar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 66, 66, 66);
            bar.ButtonPressedForegroundColor = ColorHelper.FromArgb(255, 255, 255, 255);
        }
    }
}
