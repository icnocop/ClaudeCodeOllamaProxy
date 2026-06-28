namespace ClaudeCodeOllamaProxy.Infrastructure;

/// <summary>
/// Wraps an <see cref="ILoggerFactory"/> so that logging can never crash the process. The Claude SDK
/// runs a long-lived background message-reader loop (<c>QueryHandler.ReadMessagesLoopAsync</c>) that
/// logs through the factory it's given. When the in-process host is stopped/restarted, its logger
/// providers (Karambolo file sink, the default Windows EventLog) are disposed by <c>DisposeAsync</c>;
/// a still-running SDK loop that then logs hits <see cref="ObjectDisposedException"/> — which the
/// aggregating <see cref="Logger"/> rethrows as an <see cref="AggregateException"/> on a background
/// thread, surfacing as an unobserved <c>TaskScheduler</c> exception that crashes the app. Swallowing
/// logging failures here keeps that race harmless. Logging is best-effort by nature, so dropping a log
/// line during shutdown is acceptable.
/// </summary>
internal sealed class FaultTolerantLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public ILogger CreateLogger(string categoryName) => new FaultTolerantLogger(inner.CreateLogger(categoryName));

    // No-op: the wrapped factory is the host's shared factory and is owned (and disposed) by the host.
    public void Dispose() { }

    private sealed class FaultTolerantLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            try
            {
                return inner.IsEnabled(logLevel);
            }
            catch
            {
                return false;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
            catch
            {
                // Logging must never crash the host. Providers may already be disposed during a
                // stop/restart while an SDK background loop is still draining.
            }
        }
    }
}
