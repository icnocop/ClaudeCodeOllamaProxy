# Configuration (`appsettings.json`)

Models are **retrieved from the claude CLI** at startup (not hardcoded). `appsettings.json` only
holds behavior overrides and capability defaults:

- `Claude.WorkingDirectory` — explicit working directory. When empty, the proxy infers the solution
  directory from absolute paths in the request (walking up to the nearest `*.sln`/`.git`). **If your
  edits land in the wrong place, set this to your solution folder.**
- `Claude.CliPath` — path to `claude` if not on `PATH`.
- `Claude.MaxTurns`, `Claude.BridgeTerminalTool` (route `run_in_terminal` through Copilot),
  `Claude.ModelsCacheTtlMinutes`.
- `Claude.CliDebug` — passes `--debug` to the `claude` CLI (its own diagnostics land at `Trace` under
  the `ClaudeCodeOllamaProxy.ClaudeCli` category). Independent of the log level: raising verbosity to
  `Trace` does **not** enable it. Read per session, so a change applies to the next request. The tray
  app exposes this as "Claude CLI debug output" on its Logs tab.
- `Claude.DefaultEffort` — effort used when a request selects a model without an `:effort` suffix
  (default `high`).
- `Claude.SupportedEfforts` — the effort levels exposed as selectable model variants. Empty falls back
  to the CLI's full set (`low`, `medium`, `high`, `xhigh`, `max`); values outside that set are dropped.
  Trim it to shorten Copilot's model picker.
- `Claude.McpToolTimeoutMs` — sets the CLI's `MCP_TOOL_TIMEOUT` (wall-clock cap on a bridged Copilot
  tool call, so long build/test/debug tools don't time out; default 600000 = 10 min; 0 = CLI default ~28h).
- `Claude.MaxMcpOutputTokens` — sets the CLI's `MAX_MCP_OUTPUT_TOKENS` so large `read_file`/search
  results aren't truncated at the ~10k-token default (default 100000; 0 = CLI default).
- `Claude.KnownModels` — model ids advertised **in addition** to whatever the CLI reports, so a specific
  Anthropic version can be picked from Copilot's model picker even when the CLI only reports family
  aliases (`opus`, `sonnet`, …). Each value is passed to `--model` verbatim and unioned into the catalog,
  deduped against CLI-reported ids (a CLI variant such as `opus[1m]` suppresses the plain `opus` entry,
  since the CLI's own value is known-good). Ships with the nine currently-active Anthropic models. Notes:
  - `claude-mythos-5` is available only to Project Glasswing participants and will fail for everyone else
    — remove it from the list if you don't have access.
  - An id the installed CLI doesn't recognize fails at **request** time (the CLI exits 1), not at pick
    time, so trim the list to what your CLI supports.
  - Both plain ids (`claude-opus-5`) and the CLI's 1M-context variant form (`claude-opus-5[1m]`) are
    accepted; use the latter if you want the larger window named explicitly.
- `ModelDefaults` — per-model metadata the CLI doesn't report, keyed by case-insensitive substring of
  the model id, longest match winning (`"*"` is the fallback). Populates `/api/show`, `/api/tags`, and
  the friendly display labels:
  - `DisplayName` — friendly label, e.g. `"Opus 5"`. Family aliases are deliberately version-neutral
    (`"Opus"`, `"Sonnet"`) because the version an alias resolves to is the CLI's choice and would
    otherwise collide with the explicit-version entries — VS Copilot uses the label as the model id.
  - `ContextLength`, `Capabilities` — `"tools"` enables Agent mode, `"vision"` enables image input.
  - `SupportsEffort` — set `false` for models with no effort control (e.g. Haiku 4.5), which then
    surface as a single variant with no `:effort` suffix.
  - `SupportedEfforts` — the model's own effort levels, intersected with `Claude.SupportedEfforts`.
    Empty means "all configured levels"; Opus 4.6 and Sonnet 4.6 list four because they predate `xhigh`.
  - `SupportsEffort`/`SupportedEfforts` apply only to `Claude.KnownModels` entries — a CLI-reported
    model uses the effort levels from the CLI's own `initialize` payload.
