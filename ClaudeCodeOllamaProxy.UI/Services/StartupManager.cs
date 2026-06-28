using Microsoft.Win32;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Enables/disables launching the tray app at Windows login via the per-user
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> registry key. Unpackaged-app equivalent of
/// the modern StartupTask API. The registered command adds <c>--startup</c> so the app starts hidden in
/// the tray (see <see cref="App"/>).
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeCodeOllamaProxy";
    public const string StartupArgument = "--startup";

    private static string LaunchCommand
    {
        get
        {
            var exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
            return $"\"{exe}\" {StartupArgument}";
        }
    }

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.SetValue(ValueName, LaunchCommand, RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
