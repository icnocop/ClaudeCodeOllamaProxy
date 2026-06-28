# Configuration (`appsettings.json`)

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
