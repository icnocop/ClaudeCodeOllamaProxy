using System.Text.Json;
using System.Text.Json.Nodes;
using Claude.AgentSdk.Tools;
using ClaudeCodeOllamaProxy.Models;

namespace ClaudeCodeOllamaProxy.Bridge;

/// <summary>The MCP tool server that bridges Copilot's tools, plus the namespaced names Claude uses.</summary>
public sealed record BridgeToolServer(McpToolServer Server, IReadOnlyList<string> AllowedToolNames);

/// <summary>
/// Builds an in-process MCP tool server named "copilot" from the tools Copilot sent on a request.
/// Each Copilot tool becomes an MCP tool with the same name and JSON schema; the supplied handler is
/// invoked (with the tool name and raw arguments) when Claude calls it. Claude refers to them as
/// <c>mcp__copilot__&lt;tool&gt;</c>.
/// </summary>
public static class CopilotToolServerBuilder
{
    public const string ServerName = "copilot";

    public static BridgeToolServer Build(
        IReadOnlyList<OpenAiTool> tools,
        Func<string, JsonElement, CancellationToken, Task<ToolResult>> handler)
    {
        var server = new McpToolServer(ServerName);
        var allowed = new List<string>();

        foreach (var tool in tools)
        {
            var name = tool.Function.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var schema = ToSchema(tool.Function.Parameters);
            var description = tool.Function.Description ?? name;

            // Capture the tool name so the handler knows which Copilot tool was called.
            var capturedName = name;
            server.RegisterTool(name, description, schema,
                (args, ct) => handler(capturedName, args, ct));

            allowed.Add($"mcp__{ServerName}__{name}");
        }

        return new BridgeToolServer(server, allowed);
    }

    private static JsonObject ToSchema(JsonElement? parameters)
    {
        if (parameters is { ValueKind: JsonValueKind.Object } el)
        {
            if (JsonNode.Parse(el.GetRawText()) is JsonObject obj)
                return obj;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
        };
    }
}
