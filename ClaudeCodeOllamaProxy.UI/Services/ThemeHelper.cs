using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>Allowed values for the app-theme setting (also the ComboBox tags on the Settings page).</summary>
public static class AppTheme
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";
}

/// <summary>
/// Resolves the configured theme setting into a window <see cref="ElementTheme"/> and the correct
/// tray-icon asset, honoring the system light/dark theme when set to <see cref="AppTheme.System"/>.
/// </summary>
public static class ThemeHelper
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Window content theme for the given setting (Default follows the OS).</summary>
    public static ElementTheme ToElementTheme(string setting) => setting switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// True when the effective theme is light. For <see cref="AppTheme.System"/> this reads the system
    /// theme (apps registry value), used to choose a contrasting tray icon.
    /// </summary>
    public static bool IsEffectivelyLight(string setting) => setting switch
    {
        AppTheme.Light => true,
        AppTheme.Dark => false,
        _ => IsSystemAppsLight(),
    };

    /// <summary>Tray-icon asset URI — the brand-orange app icon, the same in every theme.</summary>
    public static string TrayIconUri() => "ms-appx:///Assets/AppIcon.ico";

    private static bool IsSystemAppsLight() => ReadPersonalizeFlag("AppsUseLightTheme");

    private static bool ReadPersonalizeFlag(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            // Default to light when the value is missing (Windows' own default).
            return key?.GetValue(valueName) is not int v || v != 0;
        }
        catch
        {
            return true;
        }
    }
}
