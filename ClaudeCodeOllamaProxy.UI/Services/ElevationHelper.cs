using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Detects whether the process is elevated and relaunches it elevated ("runas"). When the app runs
/// elevated, the <c>claude</c> CLI it spawns inherits the elevated token, so it also runs as administrator.
/// </summary>
public static class ElevationHelper
{
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Relaunch the current executable elevated, forwarding the original arguments. Returns true if an
    /// elevated process was started (caller should exit), false if elevation failed or the user declined UAC.
    /// </summary>
    public static bool RestartElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        var args = Environment.GetCommandLineArgs().Skip(1)
            .Select(a => a.Contains(' ') ? $"\"{a}\"" : a);

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(' ', args),
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            // ERROR_CANCELLED (1223) when the user declines the UAC prompt, or elevation failed.
            return false;
        }
    }
}
