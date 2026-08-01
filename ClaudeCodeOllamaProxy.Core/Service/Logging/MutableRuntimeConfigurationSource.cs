using Microsoft.Extensions.Configuration;

namespace ClaudeCodeOllamaProxy.Service.Logging;

/// <summary>
/// An in-memory configuration source whose values can be changed after the host is built, raising a
/// reload so options bound through <c>IOptionsMonitor</c> pick the new value up live.
/// <para>
/// This is what makes the tray app's log-verbosity control take effect without restarting the host:
/// the minimum level is decided by the logging framework's global category filter, which is bound from
/// <c>Logging:LogLevel</c> and gates events <em>before</em> any provider sees them (including before the
/// file sink's own <c>MinLevel</c>). <c>LoggerFactory</c> subscribes to
/// <c>IOptionsMonitor&lt;LoggerFilterOptions&gt;</c>, so a reload rebuilds its filter rules in place.
/// </para>
/// <para>
/// Add it from the <c>configure</c> callback of <see cref="ProxyHost.Create"/> so it lands last in the
/// provider chain and wins over <c>appsettings.json</c>. Hold a single instance for the lifetime of the
/// app, not the host, so settings survive a stop/restart.
/// </para>
/// </summary>
public sealed class MutableRuntimeConfigurationSource : IConfigurationSource
{
    private readonly MutableRuntimeConfigurationProvider _provider = new();

    /// <summary>
    /// Set (or clear, when <paramref name="value"/> is null) a configuration key and reload.
    /// <para>
    /// For log level, set only <c>Logging:LogLevel:Default</c>. Do NOT add per-category keys for the
    /// proxy's own categories: a more-specific category rule overrides <c>Default</c> and would silently
    /// defeat the switch. <c>Microsoft.AspNetCore</c> stays pinned to Warning in appsettings.json so
    /// Kestrel remains quiet even at Trace.
    /// </para>
    /// </summary>
    public void Set(string key, string? value) => _provider.Set(key, value);

    public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;

    private sealed class MutableRuntimeConfigurationProvider : ConfigurationProvider
    {
        public override void Set(string key, string? value)
        {
            base.Set(key, value);

            // Signals the change token the logging/options binders are registered against.
            OnReload();
        }
    }
}
