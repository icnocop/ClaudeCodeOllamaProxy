# Logging

Logs go to the **console** and to rolling **files** under `Logs/` via
[`Karambolo.Extensions.Logging.File`](https://github.com/adams85/filelogger). The listening URL is
printed to the console on startup by default.

- File naming: a **unique file per launch** (`<launch>` token), rolling **daily** (`<date>`) and at
  **10 MB** (`<counter>`): `ClaudeCodeOllamaProxy-<date>-<launch>-<counter>.log`. Newest 3 files are
  kept (swept at startup; Karambolo has no native retention).
- By default, high-level events log at `Information` (model, detected mode, resolved working
  directory, effective Claude options, bridged tool calls, exceptions + how they were handled).

## Most-verbose logging (CLI command line, stdout/stderr, HTTP requests/responses)

Set the **minimum log level to `Trace`**. The simplest switch (keeps ASP.NET/Kestrel quiet):

```bash
# environment variable
Logging__LogLevel__Default=Trace dotnet run --project ClaudeCodeOllamaProxy.Console
```

or in `appsettings.json` set `"Logging": { "LogLevel": { "Default": "Trace" } }`.

Levels:

- **Information** (default): per-request model/mode/effort/working-dir, effective Claude options,
  each bridged tool_call name + id, tool-result sizes, client disconnects (HTTP 499), exceptions.
- **Debug**: the **system prompt** and prompt sent to Claude, bridged **tool-call arguments**, and
  **tool-result content** (all truncated). Set `Logging__LogLevel__Default=Debug` for these.
- **Trace** (everything):
  - **HTTP requests and responses** (incl. streamed SSE/NDJSON chunks) — category `ClaudeCodeOllamaProxy.Wire`.
    Bodies/chunks are capped at `Wire:MaxBytes` (default **1 MB**, enough for Copilot's full
    system prompt; set `0` for unlimited, or a small value to truncate).
  - **The claude CLI command line** (`Starting CLI: …/claude.exe --output-format stream-json …`) and
    CLI **stdout** — category `Claude.AgentSdk` (its transport logs the spawn command at `Debug`).
  - **CLI stdin/stdout/stderr** as the proxy sees them — category `ClaudeCodeOllamaProxy.ClaudeCli`.
    (One-shot Ask/Plan requests use `--print`, so there is no stdin stream; the bridge session does.)

To raise just one area instead of everything, set that category, e.g.
`Logging__LogLevel__ClaudeCodeOllamaProxy=Trace` (proxy wire + CLI I/O + options) or
`Logging__LogLevel__Claude.AgentSdk=Debug` (CLI command line). `Microsoft.AspNetCore` stays at
`Warning` regardless, so Kestrel noise is suppressed.
