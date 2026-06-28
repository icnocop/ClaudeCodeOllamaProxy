# How it works

Copilot's BYOK Ollama provider splits discovery from chat:

- **Discovery (Ollama-native):** `GET /api/version`, `GET /api/tags`, `POST /api/show`.
- **Chat:** `POST /v1/chat/completions` — OpenAI shape, streamed as SSE.

The proxy detects the Copilot **mode** from the tools on each request:

| Mode  | How it's detected                              | What Claude does                                  |
|-------|------------------------------------------------|---------------------------------------------------|
| Ask   | No edit/terminal tools                         | Read-only chat (Claude `plan` permission mode)    |
| Plan  | `manage_todo_list` etc. but no edit tools      | Read-only research                                |
| Agent | Edit tools present (`apply_patch`, …)          | Full work via the **[tool-call bridge](tool-call-bridge.md)** |

In Agent mode, edits flow through the [tool-call bridge](tool-call-bridge.md) so they appear in
Copilot's native diff / Accept-Reject UI.
