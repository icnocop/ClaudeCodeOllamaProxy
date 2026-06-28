using System.Text;
using System.Text.Json;
using ClaudeCodeOllamaProxy.Models;

namespace ClaudeCodeOllamaProxy.Services;

/// <summary>A reference to an image found in a request (a data: URL, http(s) URL, or bare base64).</summary>
public sealed record ImageReference(string UrlOrData);

/// <summary>The result of flattening an OpenAI/Ollama conversation into a single Claude prompt.</summary>
public sealed record FlattenedConversation(string SystemPrompt, string Prompt, IReadOnlyList<ImageReference> Images);

/// <summary>
/// Flattens an OpenAI chat conversation (system/user/assistant/tool messages, possibly with
/// multi-part content) into a single system prompt + user/assistant transcript that can be sent to
/// the stateless Claude CLI. Image parts are collected separately for materialization.
/// </summary>
public static class OpenAiMessageMapper
{
    public static FlattenedConversation Flatten(IReadOnlyList<OpenAiMessage> messages)
    {
        var system = new StringBuilder();
        var transcript = new StringBuilder();
        var images = new List<ImageReference>();

        foreach (var msg in messages)
        {
            var (text, msgImages) = ExtractContent(msg.Content);
            images.AddRange(msgImages);

            switch (msg.Role?.ToLowerInvariant())
            {
                case "system":
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (system.Length > 0) system.Append("\n\n");
                        system.Append(text);
                    }
                    break;

                case "assistant":
                    if (!string.IsNullOrWhiteSpace(text))
                        transcript.Append("Assistant: ").AppendLine(text).AppendLine();
                    if (msg.ToolCalls is { Count: > 0 })
                        foreach (var tc in msg.ToolCalls)
                            transcript.Append("Assistant called tool ").Append(tc.Function.Name)
                                      .Append('(').Append(tc.Function.Arguments).AppendLine(")").AppendLine();
                    break;

                case "tool":
                    if (!string.IsNullOrWhiteSpace(text))
                        transcript.Append("Tool result").Append(msg.Name is { Length: > 0 } ? $" ({msg.Name})" : "")
                                  .Append(": ").AppendLine(text).AppendLine();
                    break;

                default: // user
                    if (!string.IsNullOrWhiteSpace(text))
                        transcript.Append("User: ").AppendLine(text).AppendLine();
                    break;
            }
        }

        return new FlattenedConversation(system.ToString().Trim(), transcript.ToString().Trim(), images);
    }

    /// <summary>Return only the latest user message's text (used as the turn prompt for live sessions).</summary>
    public static (string Text, IReadOnlyList<ImageReference> Images) LatestUserContent(IReadOnlyList<OpenAiMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (string.Equals(messages[i].Role, "user", StringComparison.OrdinalIgnoreCase))
                return ExtractContent(messages[i].Content);
        }
        return (string.Empty, Array.Empty<ImageReference>());
    }

    /// <summary>Extract just the plain text of a message content value (string or content-part array).</summary>
    public static string TextOf(JsonElement? content) => ExtractContent(content).Text;

    /// <summary>
    /// Combine the request's system prompt with the user prompt into one string sent over stdin.
    /// We deliberately do NOT pass the system prompt as a CLI argument: Copilot's Agent-mode system
    /// prompt is tens of KB and would blow the Windows command-line length limit (~32 KB). Claude
    /// Code keeps its own default system prompt; these are operator instructions layered on top.
    /// </summary>
    public static string CombineSystemAndPrompt(string? systemPrompt, string prompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt)) return prompt;
        return $"<operator_instructions>\n{systemPrompt}\n</operator_instructions>\n\n{prompt}";
    }

    private static (string Text, List<ImageReference> Images) ExtractContent(JsonElement? content)
    {
        var images = new List<ImageReference>();
        if (content is null) return (string.Empty, images);

        var el = content.Value;
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return (el.GetString() ?? string.Empty, images);

            case JsonValueKind.Array:
                var sb = new StringBuilder();
                foreach (var part in el.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object) continue;
                    var type = part.TryGetProperty("type", out var t) ? t.GetString() : null;

                    if (type == "text" && part.TryGetProperty("text", out var textEl))
                    {
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(textEl.GetString());
                    }
                    else if (type == "image_url" && part.TryGetProperty("image_url", out var imgEl))
                    {
                        var url = imgEl.ValueKind == JsonValueKind.Object && imgEl.TryGetProperty("url", out var u)
                            ? u.GetString()
                            : imgEl.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                            images.Add(new ImageReference(url!));
                    }
                }
                return (sb.ToString(), images);

            default:
                return (string.Empty, images);
        }
    }
}
