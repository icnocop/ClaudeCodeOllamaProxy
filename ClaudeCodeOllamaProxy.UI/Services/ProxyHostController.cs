using System.Collections.ObjectModel;
using ClaudeCodeOllamaProxy.Service.Logging;
using ClaudeCodeOllamaProxy.UI.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>Lifecycle state of the in-process proxy host, surfaced as the tray-icon status indicator.</summary>
public enum ProxyState
{
    Stopped,
    Starting,
    Running,
    Stopping,
}

/// <summary>
/// Owns the in-process ClaudeCodeOllamaProxy host lifecycle (start/stop/restart) for the tray app, and
/// captures its log output into <see cref="Logs"/> for the Logs page. The host binds to the port from
/// <see cref="SettingsStore"/> at start time; a changed port only takes effect on the next restart.
/// Log verbosity and the Claude CLI debug flag, by contrast, are pushed in through
/// <see cref="MutableRuntimeConfigurationSource"/> and apply live.
/// </summary>
public sealed class ProxyHostController
{
    private const int MaxLogLines = 2000;

    private readonly SettingsStore _settings;
    private readonly ObservableLoggerProvider _logProvider = new();

    // App-lifetime, not host-lifetime: re-added on every start so live settings survive a restart.
    private readonly MutableRuntimeConfigurationSource _runtimeConfig = new();

    private readonly DispatcherQueue _dispatcher;

    private WebApplication? _app;

    public ProxyHostController(SettingsStore settings)
    {
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        RunningPort = settings.Port;
        _logProvider.LineLogged += OnLineLogged;

        // Seed the persisted values so the very first host start already honors them.
        ApplyLogLevel();
        ApplyClaudeCliDebug();
    }

    /// <summary>
    /// Push the persisted log verbosity to the host's global category filter and to the Logs page's own
    /// floor. Applies live — no restart. Only <c>Logging:LogLevel:Default</c> is set: a more-specific
    /// category rule would override it and silently defeat the switch.
    /// </summary>
    public void ApplyLogLevel()
    {
        var level = Enum.TryParse<LogLevel>(_settings.LogLevel, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;

        _logProvider.MinLevel = level;
        _runtimeConfig.Set("Logging:LogLevel:Default", level.ToString());
    }

    /// <summary>
    /// Push the persisted Claude CLI debug flag to the host. Read per session by
    /// <c>ClaudeSessionFactory</c>, so it takes effect on the next request without a restart.
    /// </summary>
    public void ApplyClaudeCliDebug() =>
        _runtimeConfig.Set("Claude:CliDebug", _settings.ClaudeCliDebug ? "true" : "false");

    /// <summary>Live host log output, updated on the UI thread. Bind from the Logs page.</summary>
    public ObservableCollection<string> Logs { get; } = new();

    /// <summary>Current lifecycle state, including the transient Starting/Stopping phases.</summary>
    public ProxyState State { get; private set; } = ProxyState.Stopped;

    public bool IsRunning => State == ProxyState.Running;

    /// <summary>The port the host is actually bound to (captured at start time, not the pending setting).</summary>
    public int RunningPort { get; private set; }

    public string ListeningUrl => $"http://127.0.0.1:{RunningPort}";

    /// <summary>Raised (on the UI thread) whenever <see cref="State"/> / <see cref="ListeningUrl"/> change.</summary>
    public event Action? StateChanged;

    private void SetState(ProxyState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public async Task StartAsync()
    {
        if (State is ProxyState.Running or ProxyState.Starting)
            return;

        SetState(ProxyState.Starting);

        // Capture the configured port now; a later port change won't affect this run until the next restart.
        RunningPort = _settings.Port;
        var url = ListeningUrl;
        var app = ProxyHost.Create(
            ["--urls", url],
            builder =>
            {
                // Added last so it wins over appsettings.json, and re-registered so the logging binder
                // picks up the source that was appended after CreateBuilder wired the Logging section.
                // Cast to IConfigurationBuilder: ConfigurationManager's own Add<T> is the options-builder
                // overload and can't take a source instance.
                ((IConfigurationBuilder)builder.Configuration).Add(_runtimeConfig);
                builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
                builder.Logging.AddProvider(_logProvider);
            });

        try
        {
            await app.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Most likely the port is already in use (e.g. another instance). Report and stay stopped.
            await app.DisposeAsync().ConfigureAwait(true);
            AddLog($"Failed to start on {url}: {ex.Message}");
            SetState(ProxyState.Stopped);
            return;
        }

        _app = app;
        SetState(ProxyState.Running);
    }

    private void AddLog(string line) => _dispatcher.TryEnqueue(() => Logs.Add(line));

    public async Task StopAsync()
    {
        var app = _app;
        if (app is null)
        {
            if (State != ProxyState.Stopped)
                SetState(ProxyState.Stopped);
            return;
        }

        _app = null;
        SetState(ProxyState.Stopping);

        try
        {
            await app.StopAsync().ConfigureAwait(true);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(true);
            SetState(ProxyState.Stopped);
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
