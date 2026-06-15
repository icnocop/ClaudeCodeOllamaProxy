using Claude.AgentSdk;
using ClaudeCodeOllamaProxy.Models;

namespace ClaudeCodeOllamaProxy.Services;

/// <summary>Copilot Chat mode, inferred from the tool set sent on a request.</summary>
public enum ChatMode
{
    /// <summary>No edit/terminal tools — conversational, read-only.</summary>
    Ask,

    /// <summary>Planning tools present but no apply-edit tools — read-only research.</summary>
    Plan,

    /// <summary>Edit tools present — Claude performs work through Copilot's tools (the bridge).</summary>
    Agent,
}

/// <summary>
/// Copilot's BYOK Ollama provider does not send an explicit mode; it is conveyed by which tools are
/// included on the request. This infers the mode from the tool names.
/// </summary>
public sealed class ModeDetector
{
    private static readonly HashSet<string> EditTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "apply_patch",
        "replace_string_in_file",
        "multi_replace_string_in_file",
        "insert_edit_into_file",
        "create_file",
    };

    private static readonly HashSet<string> PlanSignalTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "manage_todo_list",
    };

    private readonly ILogger<ModeDetector> _logger;

    public ModeDetector(ILogger<ModeDetector> logger) => _logger = logger;

    public ChatMode Detect(IReadOnlyCollection<OpenAiTool>? tools)
    {
        if (tools is null || tools.Count == 0)
            return ChatMode.Ask;

        var names = tools.Select(t => t.Function.Name).ToList();

        if (names.Any(EditTools.Contains))
            return ChatMode.Agent;

        if (names.Any(PlanSignalTools.Contains))
            return ChatMode.Plan;

        return ChatMode.Ask;
    }

    /// <summary>Map the detected mode to a Claude permission mode. Ask/Plan are read-only.</summary>
    public PermissionMode ToPermissionMode(ChatMode mode) => mode switch
    {
        ChatMode.Agent => PermissionMode.AcceptEdits,
        _ => PermissionMode.Plan,
    };
}
