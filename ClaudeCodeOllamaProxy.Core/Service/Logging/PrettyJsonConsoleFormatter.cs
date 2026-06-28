using ClaudeCodeOllamaProxy.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace ClaudeCodeOllamaProxy.Service.Logging;

/// <summary>
/// Console formatter that renders the same single-line layout as the built-in <c>simple</c> formatter
/// (<c>[timestamp] level: Category[eventId] message</c>) but pretty-prints any JSON in the message via
/// <see cref="JsonDefaults.Prettify"/>. Applies to <em>all</em> log sources, including the SDK's raw
/// <c>Sent:</c>/<c>Received:</c> transport lines that bypass the proxy's own call-site formatting.
/// </summary>
public sealed class PrettyJsonConsoleFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "pretty-json";

    private readonly IDisposable? _reload;
    private ConsoleFormatterOptions _options;

    public PrettyJsonConsoleFormatter(IOptionsMonitor<ConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.CurrentValue;
        _reload = options.OnChange(o => _options = o);
    }

    public void Dispose() => _reload?.Dispose();

    public override void Write<TState>(
        in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null) return;

        if (!string.IsNullOrEmpty(_options.TimestampFormat))
        {
            var now = _options.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
            textWriter.Write(now.ToString(_options.TimestampFormat));
        }

        textWriter.Write(LevelString(logEntry.LogLevel));
        textWriter.Write(": ");
        textWriter.Write(logEntry.Category);
        textWriter.Write('[');
        textWriter.Write(logEntry.EventId.Id);
        textWriter.Write("] ");

        if (!string.IsNullOrEmpty(message))
            textWriter.Write(JsonDefaults.Prettify(message));

        if (logEntry.Exception is not null)
        {
            textWriter.Write(Environment.NewLine);
            textWriter.Write(logEntry.Exception.ToString());
        }

        textWriter.Write(Environment.NewLine);
    }

    private static string LevelString(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "----",
    };
}
