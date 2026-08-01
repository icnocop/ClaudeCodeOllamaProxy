# Development

How to build, run, and test Claude Code Ollama Proxy from source. End-user instructions live in the
[README](README.md).

## Prerequisites

- **.NET 10 SDK**.
- **Claude Code CLI** installed and on `PATH`, and authenticated:
  - `npm install -g @anthropic-ai/claude-code`
  - `claude login` (or set `ANTHROPIC_API_KEY` / `ANTHROPIC_AUTH_TOKEN` in the proxy's environment).
- **Visual Studio 2022 17.14+** / **Visual Studio 2026**, or **VS Code 1.99+** — a build whose
  Copilot Chat supports the Ollama Bring Your Own Model provider (and Agent mode for it). See
  [Visual Studio setup](docs/visual-studio-setup.md) / [VS Code setup](docs/vscode-setup.md).
- For building/running the tray app (`ClaudeCodeOllamaProxy.UI`): Windows 10 1809+; build for
  **x64 / x86 / ARM64** (the WinUI app has no "Any CPU" platform — pick one in the Configuration
  Manager).

## Projects

| Project | What it is |
|---|---|
| `ClaudeCodeOllamaProxy.Core` | Class library with all proxy logic + the `ProxyHost` factory. |
| `ClaudeCodeOllamaProxy.Console` | Console host (`Microsoft.NET.Sdk.Web`) — a one-line `Program.cs` that runs `ProxyHost`. |
| `ClaudeCodeOllamaProxy.UI` | WinUI 3 (unpackaged) system-tray app that hosts `ProxyHost` in-process. |
| `ClaudeCodeOllamaProxy.UI.Tests` | xUnit + FlaUI UI tests that screenshot each page (see below). |

## Build & run

```bash
# Build the whole solution
dotnet build

# Console host on the default Ollama port 11434 (uses Properties/launchSettings.json)
dotnet run --project ClaudeCodeOllamaProxy.Console

# Console host on a different port (REQUIRED if a real Ollama already owns 11434).
# --urls only wins with --no-launch-profile (the launch profile otherwise forces 11434).
dotnet run --project ClaudeCodeOllamaProxy.Console --no-launch-profile --urls "http://127.0.0.1:11435"

# System-tray app (hosts the proxy in-process; port is set in its Settings tab, default 11434).
# Don't run this and the console host at the same time — they bind the same port.
dotnet run --project ClaudeCodeOllamaProxy.UI
```

There is no unit-test suite for the proxy itself; verify it by smoke-testing the endpoints against the
real `claude` CLI. Examples are in `ClaudeCodeOllamaProxy.Console/ClaudeCodeOllamaProxy.http`:

```bash
H=http://127.0.0.1:11435
curl -s $H/api/version          # -> {"version":"0.6.4"}
curl -s $H/api/tags
curl -s -X POST $H/api/show -H "Content-Type: application/json" -d '{"model":"sonnet"}'
```

See [docs/logging.md](docs/logging.md) for verbose tracing of the wire protocol and CLI command line.

### Versioning

The release workflow stamps the version into the published assemblies
(`dotnet publish -p:Version=YYYY.MM.DD.<run>`, matching the `vYYYY.MM.DD.<run>` release tag). A local
build sets no `<Version>` and so reports **1.0.0.0** on the About tab, which is older than every
release — the update check therefore always reports an update available when running from source. To
see what a released build looks like, pass the version yourself:

```bash
dotnet build ClaudeCodeOllamaProxy.UI/ClaudeCodeOllamaProxy.UI.csproj -c Release -p:Platform=x64 -p:Version=2026.06.28.9
```

## UI screenshot tests (`ClaudeCodeOllamaProxy.UI.Tests`)

[FlaUI](https://github.com/FlaUI/FlaUI)-based tests that launch the tray app, navigate each page, and
save screenshots to `docs/screenshots/` (used by the README) in both light and dark themes.

```bash
# 1. Build the tray app first (RID-specific output):
dotnet build ClaudeCodeOllamaProxy.UI/ClaudeCodeOllamaProxy.UI.csproj -p:Platform=x64

# 2. Run the tests (regenerates the screenshots):
dotnet test ClaudeCodeOllamaProxy.UI.Tests
```

Notes:
- Requires **Developer Mode** (Settings ▸ Privacy & security ▸ For developers) and an interactive
  Windows session — FlaUI automates the real UI.
- The test launches the app with an **isolated settings directory** (env var
  `CLAUDECODEOLLAMAPROXY_UI_DATADIR`) so it never touches your real settings, runs **non-elevated**
  (no UAC prompt), and binds an isolated port (`18434`).
- The app is **single-instance**, so the test closes any already-running tray instance first.
- The startup **update check** is suppressed via `CLAUDECODEOLLAMAPROXY_UI_SKIP_UPDATE_CHECK`, so a run
  never calls the GitHub API and no "Update available" dialog can land in a screenshot. The *setting* is
  left at its shipped default, so the Settings screenshot still shows the toggle on.
- Page Objects live in `PageObjects/`; each element is a property that resolves by AutomationId in its
  getter (the idiomatic .NET Page Object Model — no `[FindBy]`/PageFactory attributes).

## Git policy

Commits and pushes are done manually by the maintainer. Read-only git commands are fine.
