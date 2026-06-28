using System.Text;
using System.Text.Json;
using ClaudeCodeOllamaProxy.Infrastructure;
using ClaudeCodeOllamaProxy.Models;

namespace ClaudeCodeOllamaProxy.Endpoints;

/// <summary>
/// Writes an OpenAI-style Server-Sent Events stream for <c>/v1/chat/completions</c>: content deltas,
/// tool-call deltas, the terminal finish chunk, and the <c>[DONE]</c> sentinel. Flushes per chunk so
/// Copilot sees output live.
/// </summary>
public sealed class SseWriter
{
    private readonly HttpResponse _response;
    private readonly string _id;
    private readonly string _model;
    private readonly long _created;
    private bool _roleSent;

    public SseWriter(HttpResponse response, string id, string model, long created)
    {
        _response = response;
        _id = id;
        _model = model;
        _created = created;
    }

    /// <summary>
    /// Set the status and headers for a Server-Sent Events response: <c>200</c>, the
    /// <c>text/event-stream</c> content type, no caching, and <c>X-Accel-Buffering: no</c> so any
    /// intermediary streams chunks live instead of buffering the whole response.
    /// </summary>
    public static void Start(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
    }

    public async Task WriteContentAsync(string content, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(content)) return;

        var delta = new OpenAiDelta { Content = content };
        if (!_roleSent) { delta.Role = "assistant"; _roleSent = true; }

        await WriteChunkAsync(delta, finishReason: null, ct);
    }

    public async Task WriteToolCallAsync(string callId, string name, string argumentsJson, CancellationToken ct)
    {
        var delta = new OpenAiDelta
        {
            ToolCalls = new List<OpenAiToolCall>
            {
                new()
                {
                    Index = 0,
                    Id = callId,
                    Type = "function",
                    Function = new OpenAiFunctionCall { Name = name, Arguments = argumentsJson },
                },
            },
        };
        if (!_roleSent) { delta.Role = "assistant"; _roleSent = true; }

        await WriteChunkAsync(delta, finishReason: null, ct);
    }

    /// <summary>Write the terminal chunk with the given finish reason, then the [DONE] sentinel.</summary>
    public async Task FinishAsync(string finishReason, CancellationToken ct)
    {
        await WriteChunkAsync(new OpenAiDelta(), finishReason, ct);
        await _response.WriteAsync("data: [DONE]\n\n", ct);
        await _response.Body.FlushAsync(ct);
    }

    private async Task WriteChunkAsync(OpenAiDelta delta, string? finishReason, CancellationToken ct)
    {
        var chunk = new OpenAiChunk
        {
            Id = _id,
            Created = _created,
            Model = _model,
            Choices = new List<OpenAiChunkChoice>
            {
                new() { Index = 0, Delta = delta, FinishReason = finishReason },
            },
        };

        var json = JsonSerializer.Serialize(chunk, JsonDefaults.Options);
        await _response.WriteAsync($"data: {json}\n\n", Encoding.UTF8, ct);
        await _response.Body.FlushAsync(ct);
    }
}
