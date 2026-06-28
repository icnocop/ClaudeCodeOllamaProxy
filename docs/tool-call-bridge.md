# The tool-call bridge (Agent mode)

Copilot's native diff/accept UI only lights up when the model emits `tool_calls` to **Copilot's**
edit tools, which Copilot applies via `WorkspaceEdit`. So in Agent mode the proxy:

1. Registers the tools Copilot sent as in-process MCP tools on a live Claude session
   (`mcp__copilot__<tool>`), and disables Claude's own file-mutating tools so edits **must** flow
   through them.
2. When Claude calls one, the MCP handler emits an OpenAI `tool_call` to Copilot, ends the response
   with `finish_reason: "tool_calls"`, and **blocks** — keeping the Claude session alive in memory.
3. Copilot applies the edit (its native diff/accept UI), then sends a follow-up request with the
   `role:"tool"` result. The proxy correlates it by `tool_call_id`, unblocks the handler, and the
   same Claude session continues — streaming more tool calls or the final answer.

Claude's own read-only tools (Read/Grep/Glob/LS) stay enabled for efficiency.
