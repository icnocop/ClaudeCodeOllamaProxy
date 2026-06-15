# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A local HTTP server that exposes the **Ollama API** and drives the local **Claude Code CLI** (via the `AJGit.Claude.AgentSdk` NuGet package), so Visual Studio 2026 / VS Code GitHub Copilot Chat's BYOK **Ollama provider** can use Claude Code — including Copilot's native in-chat diff/accept-reject UI. It launches the `claude` CLI and nothing else (no cloud provider forwarding). See `README.md` for end-user setup.

## Commands

```bash
# Build (project lives in the ClaudeCodeOllamaProxy/ subfolder; solution is ClaudeCodeOllamaProxy.slnx)
dotnet build

# Run on the default Ollama port 11434 (uses Properties/launchSettings.json)
dotnet run --project ClaudeCodeOllamaProxy

# Run on a different port — REQUIRED if a real Ollama already owns 11434.
# Note: --urls only wins with --no-launch-profile (the launch profile otherwise forces 11434).
dotnet run --project ClaudeCodeOllamaProxy --no-launch-profile --urls "http://127.0.0.1:11435"
```

There is **no test project**. Verification is done by smoke-testing endpoints against the real `claude` CLI (must be installed, on PATH, and authenticated via `claude login`). Examples are in `ClaudeCodeOllamaProxy/ClaudeCodeOllamaProxy.http`. The two-step bridge test:

```bash
H=http://127.0.0.1:11435
# Discovery
curl -s $H/api/version          # -> {"version":"0.6.4"}
curl -s $H/api/tags
curl -s -X POST $H/api/show -H "Content-Type: application/json" -d '{"model":"sonnet"}'
# Ask (read-only, streamed SSE)
curl -sN -X POST $H/v1/chat/completions -H "Content-Type: application/json" \
  -d '{"model":"sonnet","stream":true,"messages":[{"role":"user","content":"say hi"}]}'
# Agent bridge: request 1 returns a tool_call (finish_reason "tool_calls"); a follow-up request
# containing a role:"tool" message with the same tool_call_id resumes the SAME parked session.
```

The listening URL prints to the console on startup. For the most-verbose logs (HTTP requests/responses incl. SSE chunks, the claude CLI command line, and CLI stdout/stderr), set the minimum level to `Trace` — `Logging__LogLevel__Default=Trace` is the single switch (Kestrel stays quiet because `Microsoft.AspNetCore` is pinned to `Warning`). **Do not** re-add per-category `Information` entries under `Logging:LogLevel` for `ClaudeCodeOllamaProxy*`/`Claude.AgentSdk` — a more-specific category rule overrides `Default`, which silently defeats the `Default=Trace` switch (the global category filter gates before any provider, including the file sink's `MinLevel`).

## Architecture (the big picture)

Minimal-API server. `Program.cs` wires DI singletons, file logging, snake_case JSON, the loopback `11434` default, warms the model catalog, and maps three endpoint groups.

**Two protocols, by design (verified against `microsoft/vscode-copilot-chat`):** Copilot's Ollama BYOK provider does *discovery* over the native Ollama API but sends *chat* to the OpenAI `/v1/chat/completions` endpoint as SSE.

- `Endpoints/OllamaDiscoveryEndpoints.cs` — `/api/version` (must report ≥ 0.6.4), `/api/tags`, `/api/show`, `/v1/models`. **Load-bearing detail in `/api/show`:** emit both `model_info["general.architecture"]="claude"` and `model_info["claude.context_length"]=N` (Copilot builds that key dynamically and silently clamps context to 32768 if missing), and include `"tools"` in `capabilities` or Copilot disables Agent mode for the model.
- `Endpoints/OpenAiChatEndpoint.cs` — the chat entry point Copilot actually uses. Dispatches by mode.
- `Endpoints/OllamaChatEndpoint.cs` — native `/api/chat` NDJSON, **text-only** (no bridge); for non-Copilot clients and smoke tests.

**Mode is implicit.** `Services/ModeDetector.cs` infers Ask / Plan / Agent from the request's `tools` array (edit tools like `apply_patch`/`create_file` ⇒ Agent). There is no `mode` field and no permission knob in config — Ask/Plan map to Claude `PermissionMode.Plan` (read-only); Agent maps to `AcceptEdits` + the bridge.

**The tool-call bridge (`Bridge/`) is the core mechanism and the trickiest code.** Copilot's native diff/accept UI only works if the model returns `tool_calls` to Copilot's *own* edit tools (Copilot applies them via `WorkspaceEdit`). The Claude CLI instead uses its own tools, and the SDK's `canUseTool` callback cannot inject a synthetic result — so the bridge runs through **in-process MCP tools**:
- `CopilotToolServerBuilder` turns Copilot's `tools[]` into an `McpToolServer` (`mcp__copilot__<tool>`), passing each tool's JSON schema through verbatim. Claude's own mutating tools (Write/Edit/MultiEdit, and Bash when `BridgeTerminalTool`) are disallowed so edits *must* flow through the bridge; read-only built-ins stay enabled.
- `ToolBridge` holds one long-lived `ClaudeAgentSession`. A background pump reads `ReceiveResponseAsync` and pushes text/done events into a `Channel<BridgeEvent>`. When Claude calls a bridged tool, the MCP handler emits a `ToolCallEvent`, registers a `TaskCompletionSource` in `BridgeRegistry` keyed by `tool_call_id`, and **blocks indefinitely**. `DrainAsync` writes one HTTP response until it hits a tool call (emit `finish_reason:"tool_calls"`, end response, keep session parked) or completion (`stop`, dispose).
- A follow-up Copilot request carrying a `role:"tool"` result with a registered `tool_call_id` is a **continuation**: `BridgeRegistry.TryResolve` unblocks the handler, and `DrainAsync` resumes streaming the *same* session. Correlation is purely by `tool_call_id`; bridge state is in-memory only (a restart drops an active tool loop).
- **tool_call ids MUST be globally unique** — generated by `BridgeRegistry.NextCallId()` (a process-wide counter), never per-bridge. Per-bridge ids collide across concurrent bridges and cross-deliver results to the wrong session (a real bug that was fixed). **One bridge per conversation:** the endpoint keys bridges by the first user message (`ConversationKey`, deliberately not the system prompt — VS injects volatile dates there) and disposes any existing bridge for that conversation before starting a new turn, so a resend never runs two Claude sessions on the same workspace. Bridges are also disposed on client disconnect (HTTP 499) / errors via `DrainAndDisposeAsync`, so abandoned sessions don't linger.
- **Never pass large content as a CLI argument (Windows ~32 KB command-line limit).** Copilot's Agent-mode system prompt is tens of KB; passing it as `--system-prompt` (or a long conversation as `--print`) throws `Win32Exception (206): The filename or extension is too long`. So `BuildOptions` deliberately has **no system-prompt parameter**, all chat paths use a **bidirectional session** (prompt over stdin via `SendAsync`, never `--print`/`QueryAsync`), and `OpenAiMessageMapper.CombineSystemAndPrompt` folds the system prompt into the stdin prompt as an `<operator_instructions>` preamble (Claude Code keeps its own default system prompt). Keep it this way — reintroducing `SystemPrompt`/`--print` for big inputs re-breaks Agent mode.
- **MCP env tuning** (`ClaudeSessionFactory` → SDK `Environment`): `MCP_TOOL_TIMEOUT` (from `Claude.McpToolTimeoutMs`) and `MAX_MCP_OUTPUT_TOKENS` (from `Claude.MaxMcpOutputTokens`) — the latter prevents large `read_file`/search results from truncating at the ~10k-token default. Copilot's own per-tool caps (e.g. `debugger_launch_unit_test` ~30s) are VS-side and not controllable here.

**Claude CLI integration:**
- `Services/ClaudeSessionFactory.cs` is the single place that builds `ClaudeAgentOptions` and wires the SDK's `OnStderr`/`OnMessageSent`/`OnMessageReceived` to Trace logging. Auth flows through the CLI's own `claude login` (no ApiKey/BaseUrl option exists).
- `Services/ChatService.cs` handles the read-only path with the SDK's stateless one-shot `client.QueryAsync`.
- `Services/ModelCatalog.cs` queries the CLI for the base model list and caches it, then **expands each base model across the supported effort levels** into selectable variants (id `"<base>:<effort>"`, label `"Opus 4.8 (1M context) · high effort"`). The CLI reports only id/displayName, so context/capabilities/friendly-name come from the `ModelDefaults` table (matched by id substring, `"*"` fallback). `ResolveSelection(id)` parses the `:<effort>` suffix back into `(baseModel, effort)`; `ResolveAsync` builds the variant for `/api/show`. Some CLI versions don't implement `supported_models` — it then falls back to the `sonnet`/`opus`/`haiku`/`fable` aliases.
- **Effort:** the AJGit SDK has no effort option, so `ClaudeSessionFactory.BuildOptions(effort:)` passes `--effort <level>` to the CLI via `ExtraArgs` (alongside `--debug` when Trace). Valid flag values are `low|medium|high|xhigh|max` only (the CLI warns-and-defaults on unknown values; `ultracode`/`auto` are interactive-`/effort`-only). Copilot has no effort UI, so effort is selected by picking a model variant; chat endpoints resolve the id via `ResolveSelectionAsync`, run the CLI with the **base model** + `--effort`, and echo the requested id back to the client.
- **VS 2026 Copilot uses the `/api/tags` `name` field as the model identifier** (not `model`, contrary to the VS Code source) — it sends the friendly label (`Opus 4.8 (1M context) · high effort`) back as the chat `model`. So `ModelCatalog.ResolveSelectionAsync`/`ResolveAsync` match the catalog by **both** the clean id (`opus:high`) and the display label, then map to the base model. **Never pass the requested id straight to `--model`** — always resolve it first, or the CLI gets an invalid model name and exits 1 (the original Agent-mode failure).
- `Services/OpenAiMessageMapper.cs` flattens OpenAI messages (incl. multi-part content) into a Claude prompt; `Services/ImageMaterializer.cs` writes image parts to temp files referenced by path (the SDK has no native image input); `Services/WorkingDirectoryResolver.cs` infers the solution directory from absolute paths in the request (Copilot does not pass the workspace path) with an `appsettings.json` override.

## Gotchas

- **Models are NOT configured in `appsettings.json`** — only capability/context defaults are. The list comes from the CLI (or fallback). Don't hardcode model lists.
- **`.NET config binding APPENDS to non-empty `List<T>` initializers.** `ModelCapabilities.Capabilities` is intentionally initialized empty to avoid duplicate entries; keep it that way.
- The SDK's published `0.6.1` API matches its `main`-branch source; namespaces split across `Claude.AgentSdk` (client/options/`PermissionMode`/`McpSdkServerConfig`), `Claude.AgentSdk.Messages`, `Claude.AgentSdk.Tools` (`SdkMcpTool`/`McpToolServer`/`ToolResult`), `Claude.AgentSdk.Protocol`, and `Claude.AgentSdk.Exceptions` (`CliNotFoundException`).
- File logging (`Service/Logging/`, namespace `ClaudeCodeOllamaProxy.Service.Logging`) uses `Karambolo.Extensions.Logging.File`, which has **no native `MaxFiles`** (a startup sweep in `LaunchPlaceholderResolver` keeps newest 3) and no `<appname>`/`<launch>` placeholders (provided by the custom `PathPlaceholderResolver`). The `Logging:File` section must be nested under `Logging`, and per-file level uses `MinLevel`.
