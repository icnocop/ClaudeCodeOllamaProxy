using System.Collections.ObjectModel;
using ClaudeCodeOllamaProxy.UI.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Owns the in-process ClaudeCodeOllamaProxy host lifecycle (start/stop/restart) for the tray app, and
/// captures its log output into <see cref="Logs"/> for the Logs page. The host binds to the port from
/// <see cref="SettingsStore"/> at start time; a changed port only takes effect on the next restart.
/// </summary>
public sealed class ProxyHostController
{
    private const int MaxLogLines = 2000;

    private readonly SettingsStore _settings;
    private readonly ObservableLoggerProvider _logProvider = new();
    private readonly DispatcherQueue _dispatcher;

    private WebApplication? _app;

    public ProxyHostController(SettingsStore settings)
    {
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        RunningPort = settings.Port;
        _logProvider.LineLogged += OnLineLogged;
    }

    /// <summary>Live host log output, updated on the UI thread. Bind from the Logs page.</summary>
    public ObservableCollection<string> Logs { get; } = new();

    public bool IsRunning { get; private set; }

    /// <summary>The port the host is actually bound to (captured at start time, not the pending setting).</summary>
    public int RunningPort { get; private set; }

    public string ListeningUrl => $"http://127.0.0.1:{RunningPort}";

    /// <summary>Raised (on the UI thread) whenever <see cref="IsRunning"/> / <see cref="ListeningUrl"/> change.</summary>
    public event Action? StateChanged;

    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        // Capture the configured port now; a later port change won't affect this run until the next restart.
        RunningPort = _settings.Port;
        var url = ListeningUrl;
        var app = ProxyHost.Create(
            ["--urls", url],
            builder => builder.Logging.AddProvider(_logProvider));

        try
        {
            await app.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Most likely the port is already in use (e.g. another instance). Report and stay stopped.
            await app.DisposeAsync().ConfigureAwait(true);
            AddLog($"Failed to start on {url}: {ex.Message}");
            IsRunning = false;
            StateChanged?.Invoke();
            return;
        }

        _app = app;
        IsRunning = true;
        StateChanged?.Invoke();
    }

    private void AddLog(string line) => _dispatcher.TryEnqueue(() => Logs.Add(line));

    public async Task StopAsync()
    {
        var app = _app;
        if (app is null)
            return;

        _app = null;
        IsRunning = false;
        StateChanged?.Invoke();

        try
        {
            await app.StopAsync().ConfigureAwait(true);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(true);
        }
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(true);
        await StartAsync().ConfigureAwait(true);
    }

    private void OnLineLogged(string line)
    {
        // Log events arrive on Kestrel/background threads; marshal onto the UI thread for the bound collection.
        _dispatcher.TryEnqueue(() =>
        {
            Logs.Add(line);
            while (Logs.Count > MaxLogLines)
                Logs.RemoveAt(0);
        });
    }
}
