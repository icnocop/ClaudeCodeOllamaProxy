# Claude Code Ollama Proxy

A local HTTP server that speaks the **Ollama API** and drives the **Claude Code CLI**, so Visual
Studio 2026's **GitHub Copilot Chat “Bring Your Own Key” (BYOK)** Ollama provider can use Claude
Code — including Copilot's native in-chat **diff / Accept-Reject / “Accept all” / rollback** UI —
**without installing any extra Visual Studio extension**.

Unlike proxies that forward to cloud AI providers, this one launches the local `claude` CLI (via
the [`AJGit.Claude.AgentSdk`](https://github.com/ajgit/Claude.AgentSdk) SDK) and nothing else, so it
uses your existing Claude Code login.

## How it works

Copilot's BYOK Ollama provider splits discovery from chat:

- **Discovery (Ollama-native):** `GET /api/version`, `GET /api/tags`, `POST /api/show`.
- **Chat:** `POST /v1/chat/completions` — OpenAI shape, streamed as SSE.

The proxy detects the Copilot **mode** from the tools on each request:

| Mode  | How it's detected                              | What Claude does                                  |
|-------|------------------------------------------------|---------------------------------------------------|
| Ask   | No edit/terminal tools                         | Read-only chat (Claude `plan` permission mode)    |
| Plan  | `manage_todo_list` etc. but no edit tools      | Read-only research                                |
| Agent | Edit tools present (`apply_patch`, …)          | Full work via the **tool-call bridge** (below)    |

### The tool-call bridge (Agent mode)

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

## Prerequisites

- **.NET 10 SDK**.
- **Claude Code CLI** installed and on `PATH`, and authenticated:
  - `npm install -g @anthropic-ai/claude-code`
  - `claude login` (or set `ANTHROPIC_API_KEY` / `ANTHROPIC_AUTH_TOKEN` in the proxy's environment).
- A Visual Studio 2026 / VS Code build whose Copilot Chat supports the Ollama BYOK provider
  (and Agent mode for it).

## Run

```bash
dotnet run --project ClaudeCodeOllamaProxy
```

The proxy listens on `http://127.0.0.1:11434` (the default Ollama port). Override with
`ASPNETCORE_URLS` if needed.

## Configure Visual Studio 2026

1. Copilot Chat → **Manage Models** → add an **Ollama** provider.
2. Set the endpoint to `http://localhost:11434`.
3. The `claude-*` models appear; pick one and chat. Switch to **Agent** mode to let Claude edit
   files — changes show up in Copilot's normal changed-files / diff / accept-reject UI.

## Effort levels

The claude CLI supports an effort level per session (`--effort low|medium|high|xhigh|max`), but
Copilot's Ollama provider has no effort control — so the proxy exposes **each model at each supported
effort as a separate selectable model**. In Copilot's model picker you'll see entries like:

```
Opus 4.8 (1M context) · high effort      (id: opus:high)
Sonnet 4.6 (1M context) · xhigh effort   (id: sonnet:xhigh)
```

Pick the one you want; the proxy parses the `:<effort>` suffix and passes `--effort` to the CLI.
`/api/show` reports each model's `effort`, full `supported_efforts` list, `display_name`, and
context size. Requests for a bare model id (no suffix) use `Claude.DefaultEffort`.

Configure under `Claude` in `appsettings.json`:

```json
"DefaultEffort": "high",
"SupportedEfforts": [ "low", "medium", "high", "xhigh", "max" ]
```

`SupportedEfforts` controls which variants are listed. Only the CLI's documented flag values are
valid — `ultracode` and `auto` are interactive-`/effort`-only and are dropped (the CLI ignores
unknown `--effort` values and falls back to its default).

## Configuration (`appsettings.json`)

Models are **retrieved from the claude CLI** at startup (not hardcoded). `appsettings.json` only
holds behavior overrides and capability defaults:

- `Claude.WorkingDirectory` — explicit working directory. When empty, the proxy infers the solution
  directory from absolute paths in the request (walking up to the nearest `*.sln`/`.git`). **If your
  edits land in the wrong place, set this to your solution folder.**
- `Claude.CliPath` — path to `claude` if not on `PATH`.
- `Claude.MaxTurns`, `Claude.BridgeTerminalTool` (route `run_in_terminal` through Copilot),
  `Claude.ModelsCacheTtlMinutes`.
- `Claude.McpToolTimeoutMs` — sets the CLI's `MCP_TOOL_TIMEOUT` (wall-clock cap on a bridged Copilot
  tool call, so long build/test/debug tools don't time out; default 600000 = 10 min; 0 = CLI default ~28h).
- `Claude.MaxMcpOutputTokens` — sets the CLI's `MAX_MCP_OUTPUT_TOKENS` so large `read_file`/search
  results aren't truncated at the ~10k-token default (default 100000; 0 = CLI default).
- `ModelDefaults` — per-model `DisplayName` (friendly version label, e.g. `"Opus 4.8"`),
  `ContextLength`, and `Capabilities`, keyed by case-insensitive substring of the model id (`"*"` is
  the fallback). The CLI doesn't report these, so they populate `/api/show`, `/api/tags`, and the
  friendly display labels (the `"tools"` capability enables Agent mode; `"vision"` enables image input).

## Logging

Logs go to the **console** and to rolling **files** under `Logs/` via
[`Karambolo.Extensions.Logging.File`](https://github.com/adams85/filelogger). The listening URL is
printed to the console on startup by default.

- File naming: a **unique file per launch** (`<launch>` token), rolling **daily** (`<date>`) and at
  **10 MB** (`<counter>`): `ClaudeCodeOllamaProxy-<date>-<launch>-<counter>.log`. Newest 3 files are
  kept (swept at startup; Karambolo has no native retention).
- By default, high-level events log at `Information` (model, detected mode, resolved working
  directory, effective Claude options, bridged tool calls, exceptions + how they were handled).

### Most-verbose logging (CLI command line, stdout/stderr, HTTP requests/responses)

Set the **minimum log level to `Trace`**. The simplest switch (keeps ASP.NET/Kestrel quiet):

```bash
# environment variable
Logging__LogLevel__Default=Trace dotnet run --project ClaudeCodeOllamaProxy
```

or in `appsettings.json` set `"Logging": { "LogLevel": { "Default": "Trace" } }`.

Levels:

- **Information** (default): per-request model/mode/effort/working-dir, effective Claude options,
  each bridged tool_call name + id, tool-result sizes, client disconnects (HTTP 499), exceptions.
- **Debug**: the **system prompt** and prompt sent to Claude, bridged **tool-call arguments**, and
  **tool-result content** (all truncated). Set `Logging__LogLevel__Default=Debug` for these.
- **Trace** (everything):
  - **HTTP requests and responses** (incl. streamed SSE/NDJSON chunks) — category `ClaudeCodeOllamaProxy.Wire`.
  - **The claude CLI command line** (`Starting CLI: …/claude.exe --output-format stream-json …`) and
    CLI **stdout** — category `Claude.AgentSdk` (its transport logs the spawn command at `Debug`).
  - **CLI stdin/stdout/stderr** as the proxy sees them — category `ClaudeCodeOllamaProxy.ClaudeCli`.
    (One-shot Ask/Plan requests use `--print`, so there is no stdin stream; the bridge session does.)

To raise just one area instead of everything, set that category, e.g.
`Logging__LogLevel__ClaudeCodeOllamaProxy=Trace` (proxy wire + CLI I/O + options) or
`Logging__LogLevel__Claude.AgentSdk=Debug` (CLI command line). `Microsoft.AspNetCore` stays at
`Warning` regardless, so Kestrel noise is suppressed.

## Image input

Best-effort: images Copilot sends (data: URLs / base64) are written to temp files and referenced by
path so Claude's `Read` tool can view them; `http(s)` image URLs are passed through as references.

## Troubleshooting

- **`debugger_launch_unit_test` (and similar) "caps execution at 30 seconds":** that cap is in
  **Copilot's own tool** (VS-side), not this proxy or the claude CLI — the proxy can't extend it.
  `Claude.McpToolTimeoutMs` only governs how long the proxy/CLI will wait for a bridged tool result
  (already generous); it does not change Copilot's per-tool caps.

## Limitations

- **In-flight bridge state is in memory only.** Restarting the proxy mid-tool-loop drops the active
  Claude session; the next Copilot request falls back to starting a fresh turn.
- The Claude CLI may impose its own timeout on a long-pending tool call (the SDK imposes none).
- Image input is best-effort (no native multimodal input in the CLI SDK).
- Streaming is message-granular (per assistant message), not token-by-token.
- Requires a Copilot build where the Ollama BYOK provider (and its Agent mode) is enabled.

## References

- SDK used: [AJGit.Claude.AgentSdk](https://github.com/ajgit/Claude.AgentSdk) ·
  [NuGet](https://www.nuget.org/packages/AJGit.Claude.AgentSdk)
- File logging: [Karambolo.Extensions.Logging.File](https://github.com/adams85/filelogger)
- Other C# Claude-CLI SDKs considered:
  [0xeb/claude-agent-sdk-dotnet](https://github.com/0xeb/claude-agent-sdk-dotnet),
  [zxyao145/claude-code-sdk-csharp](https://github.com/zxyao145/claude-code-sdk-csharp),
  [managedcode/ClaudeCodeSharpSDK](https://github.com/managedcode/ClaudeCodeSharpSDK),
  [gunpal5/claude-agent-sdk-dotnet](https://github.com/gunpal5/claude-agent-sdk-dotnet)
- Official Agent SDK (Python/TS only): <https://code.claude.com/docs/en/agent-sdk>
- Reference projects: `vs2026-copilot-deepseek-v4` (proxy structure),
  [zmy15/DeepSeek-v4-for-VisualStudio](https://github.com/zmy15/DeepSeek-v4-for-VisualStudio)
  (tool-loop patterns), [dliedke/ClaudeCodeExtension](https://github.com/dliedke/ClaudeCodeExtension)
  (the extension this obviates)
- Copilot / Ollama protocol: [microsoft/vscode-copilot-chat](https://github.com/microsoft/vscode-copilot-chat),
  [VS Code BYOK docs](https://code.visualstudio.com/docs/agent-customization/language-models),
  [Ollama API](https://github.com/ollama/ollama/blob/main/docs/api.md)
