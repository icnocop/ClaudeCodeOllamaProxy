using System.Text;
using ClaudeCodeOllamaProxy.Infrastructure;
using Karambolo.Extensions.Logging.File;

namespace ClaudeCodeOllamaProxy.Service.Logging;

/// <summary>
/// Custom file log line format: <c>yyyy-MM-dd HH:mm:ss.fffZ [Level] Category: message</c> with the
/// exception (if any) on following lines. Referenced from appsettings via <c>TextBuilderType</c>.
/// </summary>
public sealed class CustomLogFormatTextBuilder : FileLogEntryTextBuilder
{
    public override void BuildEntryText(
        StringBuilder sb,
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        string? message,
        Exception? exception,
        IExternalScopeProvider? scopeProvider,
        DateTimeOffset timestamp)
    {
        sb.Append(timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"))
          .Append("Z [").Append(Level(logLevel)).Append("] ")
          .Append(categoryName).Append(": ")
          .Append(JsonDefaults.Prettify(message));

        if (exception is not null)
            sb.AppendLine().Append(exception);

        sb.AppendLine();
    }

    private static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        _ => level.ToString(),
    };
}
