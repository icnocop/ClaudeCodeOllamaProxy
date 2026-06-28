using ClaudeCodeOllamaProxy.UI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ClaudeCodeOllamaProxy.UI;

/// <summary>
/// Custom entry point (XAML-generated Main is disabled via DISABLE_XAML_GENERATED_MAIN) so the app can
/// enforce single-instance: a second launch is redirected to the already-running instance, which then
/// surfaces its window.
/// </summary>
public static class Program
{
    // Elevated and non-elevated instances use distinct keys so a "Restart as administrator" doesn't
    // get redirected back to the (exiting) non-elevated instance.
    private static string InstanceKey =>
        "ClaudeCodeOllamaProxy.UI" + (ElevationHelper.IsElevated() ? ".Admin" : string.Empty);

    [STAThread]
    private static void Main(string[] args)
    {
        // Record any unhandled exception (incl. before the XAML layer is up, e.g. during the elevated
        // relaunch) instead of losing it to WinUI's opaque stowed-exception crash (0xC000027B).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Report("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Report("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // If configured to run as administrator and we're not elevated yet, relaunch elevated and exit.
        if (new SettingsStore().RunAsAdmin && !ElevationHelper.IsElevated() && ElevationHelper.RestartElevated())
            return;

        if (DecideRedirection())
            return; // Another instance is primary; we've forwarded the activation and should exit.

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    private static bool DecideRedirection()
    {
        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += OnActivated;
            return false;
        }

        RedirectActivationTo(activationArgs, keyInstance);
        return true;
    }

    private static void OnActivated(object? sender, AppActivationArguments e)
    {
        // Fired on a background thread in the primary instance when a second launch is redirected here.
        App.Current?.OnRedirectedActivation();
    }

    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        using var redirectComplete = new ManualResetEvent(false);
        _ = Task.Run(async () =>
        {
            await keyInstance.RedirectActivationToAsync(args);
            redirectComplete.Set();
        });
        redirectComplete.WaitOne();
    }
}
