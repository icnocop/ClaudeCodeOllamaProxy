using Microsoft.Extensions.Logging;

namespace ClaudeCodeOllamaProxy.UI.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> injected into the in-process proxy host so the tray app's Logs page
/// can show the host's output live. Each formatted line is raised via <see cref="LineLogged"/>.
/// </summary>
[ProviderAlias("Observable")]
public sealed class ObservableLoggerProvider : ILoggerProvider
{
    private volatile LogLevel _minLevel = LogLevel.Information;

    public event Action<string>? LineLogged;

    /// <summary>
    /// Floor for lines pushed to the Logs page, kept in sync with the user's verbosity setting. This is
    /// a second gate only — the host's global category filter (Logging:LogLevel) decides what reaches a
    /// provider at all, so lowering this alone cannot surface Debug/Trace.
    /// </summary>
    public LogLevel MinLevel
    {
        get => _minLevel;
        set => _minLevel = value;
    }

    public ILogger CreateLogger(string categoryName) => new ObservableLogger(categoryName, this);

    internal void Emit(string line) => LineLogged?.Invoke(line);

    /// <summary>
    /// Deliberately does nothing. This provider outlives the host: one instance is owned by
    /// <see cref="Services.ProxyHostController"/> for the app's lifetime and re-added on every start.
    /// Dropping the <see cref="LineLogged"/> subscribers here (the host disposes its providers on
    /// shutdown) would silence the Logs page after the first stop/restart.
    /// </summary>
    public void Dispose()
    {
    }
}

internal sealed class ObservableLogger(string category, ObservableLoggerProvider provider) : ILogger
{
    private readonly string _shortCategory = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= provider.MinLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
            return;

        var line = $"{Short(logLevel)}: {_shortCategory}: {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        provider.Emit(line);
    }

    private static string Short(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "info",
    };
}
