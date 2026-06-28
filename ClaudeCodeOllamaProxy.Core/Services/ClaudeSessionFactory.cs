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
    private readonly ProxyOptions _options;
    private readonly ILoggerFactory _sdkLoggerFactory;
    private readonly ILogger _cliLogger;
    private readonly ILogger<ClaudeSessionFactory> _logger;

    public ClaudeSessionFactory(IOptions<ProxyOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
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
        var traceEnabled = _cliLogger.IsEnabled(LogLevel.Trace);

        // The AJGit SDK has no effort option, so pass it to the CLI as --effort via ExtraArgs.
        var extraArgs = new Dictionary<string, string?>();
        if (traceEnabled) extraArgs["debug"] = null;
        if (!string.IsNullOrWhiteSpace(effort)) extraArgs["effort"] = effort;

        // CLI behavior tuned via environment variables (the SDK passes these to the subprocess):
        // long tool timeouts for build/test/debug, and a high output cap so large file reads aren't truncated.
        var environment = new Dictionary<string, string>();
        if (_options.Claude.McpToolTimeoutMs > 0)
            environment["MCP_TOOL_TIMEOUT"] = _options.Claude.McpToolTimeoutMs.ToString();
        if (_options.Claude.MaxMcpOutputTokens > 0)
            environment["MAX_MCP_OUTPUT_TOKENS"] = _options.Claude.MaxMcpOutputTokens.ToString();

        var options = new ClaudeAgentOptions
        {
            Model = model,
            WorkingDirectory = workingDirectory,
            PermissionMode = permissionMode,
            MaxTurns = _options.Claude.MaxTurns,
            CliPath = string.IsNullOrWhiteSpace(_options.Claude.CliPath) ? null : _options.Claude.CliPath,
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
