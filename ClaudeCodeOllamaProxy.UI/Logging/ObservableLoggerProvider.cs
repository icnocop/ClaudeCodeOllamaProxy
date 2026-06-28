using Microsoft.Extensions.Logging;

namespace ClaudeCodeOllamaProxy.UI.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> injected into the in-process proxy host so the tray app's Logs page
/// can show the host's output live. Each formatted line is raised via <see cref="LineLogged"/>.
/// </summary>
[ProviderAlias("Observable")]
public sealed class ObservableLoggerProvider : ILoggerProvider
{
    public event Action<string>? LineLogged;

    public ILogger CreateLogger(string categoryName) => new ObservableLogger(categoryName, this);

    internal void Emit(string line) => LineLogged?.Invoke(line);

    public void Dispose() => LineLogged = null;
}

internal sealed class ObservableLogger(string category, ObservableLoggerProvider provider) : ILogger
{
    private readonly string _shortCategory = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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
