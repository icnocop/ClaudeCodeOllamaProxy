using Claude.AgentSdk;
using ClaudeCodeOllamaProxy.Infrastructure;
using ClaudeCodeOllamaProxy.Models;
using Microsoft.Extensions.Options;

namespace ClaudeCodeOllamaProxy.Services;

/// <summary>
/// Builds <see cref="ClaudeAgentOptions"/> and <see cref="ClaudeAgentClient"/> instances with the
/// proxy's configuration and logging callbacks wired in (raw CLI stdin/stdout/stderr at Trace).
/// </summary>
public sealed class ClaudeSessionFactory
{
    // Monitored rather than snapshotted so a settings change (e.g. the tray app's CLI-debug toggle,
    // pushed in as configuration) applies to the next session without restarting the host.
    private readonly IOptionsMonitor<ProxyOptions> _options;
    private readonly ILoggerFactory _sdkLoggerFactory;
    private readonly ILogger _cliLogger;
    private readonly ILogger<ClaudeSessionFactory> _logger;

    public ClaudeSessionFactory(IOptionsMonitor<ProxyOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options;
        // The SDK runs a background message-reader loop that logs through this factory; wrap it so a
        // log emitted after the host's providers are disposed (stop/restart) can't crash the process.
        _sdkLoggerFactory = new FaultTolerantLoggerFactory(loggerFactory);
        _cliLogger = loggerFactory.CreateLogger("ClaudeCodeOllamaProxy.ClaudeCli");
        _logger = loggerFactory.CreateLogger<ClaudeSessionFactory>();
    }

    public ClaudeAgentClient CreateClient(ClaudeAgentOptions options) => new(options, _sdkLoggerFactory);

    // NOTE: the system prompt is intentionally NOT a parameter here — it must never become a CLI
    // argument (Copilot's Agent-mode system prompt is tens of KB and overflows the Windows
    // command-line limit). Callers fold it into the prompt sent over stdin instead.
    public ClaudeAgentOptions BuildOptions(
        string model,
        string workingDirectory,
        PermissionMode permissionMode,
        IReadOnlyList<string>? allowedTools = null,
        IReadOnlyList<string>? disallowedTools = null,
        IReadOnlyDictionary<string, McpServerConfig>? mcpServers = null,
        string? effort = null)
    {
        var claude = _options.CurrentValue.Claude;

        // The AJGit SDK has no effort option, so pass it to the CLI as --effort via ExtraArgs.
        // --debug comes from its own setting, NOT from the log level: raising verbosity to Trace must
        // not silently turn on CLI debug output.
        var extraArgs = new Dictionary<string, string?>();
        if (claude.CliDebug) extraArgs["debug"] = null;
        if (!string.IsNullOrWhiteSpace(effort)) extraArgs["effort"] = effort;

        // CLI behavior tuned via environment variables (the SDK passes these to the subprocess):
        // long tool timeouts for build/test/debug, and a high output cap so large file reads aren't truncated.
        var environment = new Dictionary<string, string>();
        if (claude.McpToolTimeoutMs > 0)
            environment["MCP_TOOL_TIMEOUT"] = claude.McpToolTimeoutMs.ToString();
        if (claude.MaxMcpOutputTokens > 0)
            environment["MAX_MCP_OUTPUT_TOKENS"] = claude.MaxMcpOutputTokens.ToString();

        var options = new ClaudeAgentOptions
        {
            Model = model,
            WorkingDirectory = workingDirectory,
            PermissionMode = permissionMode,
            MaxTurns = claude.MaxTurns,
            CliPath = string.IsNullOrWhiteSpace(claude.CliPath) ? null : claude.CliPath,
            AllowedTools = allowedTools ?? Array.Empty<string>(),
            DisallowedTools = disallowedTools ?? Array.Empty<string>(),
            McpServers = mcpServers,
            Environment = environment,
            OnStderr = line => _cliLogger.LogTrace("CLI stderr: {Line}", line),
            OnMessageSent = json => _cliLogger.LogTrace("=> CLI stdin: {Json}", JsonDefaults.Prettify(json)),
            OnMessageReceived = json => _cliLogger.LogTrace("<= CLI stdout: {Json}", JsonDefaults.Prettify(json)),
            ExtraArgs = extraArgs,
        };

        _logger.LogInformation(
            "Claude options: model={Model}, permission={Permission}, cwd={Cwd}, maxTurns={MaxTurns}, allowed=[{Allowed}], disallowed=[{Disallowed}], mcp=[{Mcp}], extraArgs=[{ExtraArgs}].",
            model, permissionMode, workingDirectory, options.MaxTurns,
            string.Join(",", options.AllowedTools), string.Join(",", options.DisallowedTools),
            mcpServers is null ? "" : string.Join(",", mcpServers.Keys),
            string.Join(" ", options.ExtraArgs.Select(kv => kv.Value is null ? $"--{kv.Key}" : $"--{kv.Key} {kv.Value}")));

        return options;
    }
}
