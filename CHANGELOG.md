# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions use the
`vYYYY.MM.DD.<build>` scheme produced by the release workflow, and each released version links to its
GitHub release.

## [v2026.08.01.10] - 2026-08-01

### Added

- **All current Anthropic model versions are selectable.** `claude-fable-5`, `claude-mythos-5`,
  `claude-opus-5`, `claude-opus-4-8`, `claude-opus-4-7`, `claude-opus-4-6`, `claude-sonnet-5`,
  `claude-sonnet-4-6`, and `claude-haiku-4-5` are advertised alongside whatever the `claude` CLI
  reports, so a specific version can be picked from Copilot's model picker instead of relying on
  the CLI's family aliases. Each appears once per supported effort level; Opus 4.6 and Sonnet 4.6
  omit `xhigh` (they predate it) and Haiku 4.5 appears once with no effort. Configurable via the new
  `Claude.KnownModels` setting.
- **Logging verbosity is configurable from the Logs tab**, with a "Default" button to restore it.
  Changes apply immediately — no restart — and cover both the Logs tab and the log files on disk.
- **Multi-line selection and copy in the Logs tab.** The log output is now one selectable text surface,
  so a selection can be dragged vertically across any number of lines (and start or end mid-line),
  with Shift+Click, Ctrl+A, Ctrl+C, and a right-click Copy / Select all menu. A Copy button is also in
  the header. Log updates and auto-scroll pause while text is selected, so an incoming line can't wipe
  a selection mid-drag, and the view only follows the tail when already scrolled to the bottom.
- **"Claude CLI debug output" setting** on the Logs tab, passing `--debug` to the `claude` CLI.
  Previously this was inferred from the log level, so raising verbosity to Trace turned it on as a
  side effect; it is now an independent toggle (`Claude.CliDebug`).
- `ModelDefaults` entries accept `SupportsEffort` and `SupportedEfforts`, so a model's effort levels
  can be described per model rather than assumed uniform.
- **Clicking the tray icon now toggles the window.** If the main window is already open and focused, a
  left-click minimizes it; otherwise it opens (or is brought to the front) as before. The tray menu's
  **Open** item always shows the window.
- **[VS Code setup guide](docs/vscode-setup.md)** — step-by-step instructions for VS Code's Ollama
  Bring Your Own Key provider, including the `github.copilot.chat.byok.ollamaEndpoint` setting for
  non-default ports.
- **Update checking.** The About tab has a **Check for updates** button next to the version number; when
  a newer GitHub release exists it shows a message box and a highlighted "Update available" notice, both
  linking to the release page. Nothing is downloaded or installed automatically. Two new settings on the
  Settings tab: **Automatically check for updates when the app starts** (on by default) and **Include
  pre-release versions** (off by default). When a startup check finds an update while the app is hidden
  in the tray, no dialog appears — instead the **tray and taskbar icon gain a blue download marker**
  (opposite the proxy status dot), the tray tooltip names the new version, and the **About** navigation
  item shows a badge.
- **Released builds now report their real version.** The release workflow stamps the `vYYYY.MM.DD.<build>`
  version into the published assemblies, so the About tab shows the release you're actually running
  instead of `1.0.0.0` — which is also what makes the update comparison meaningful. Builds from source
  are still `1.0.0.0`, so they always report an update as available.

### Changed

- **[Visual Studio setup](docs/visual-studio-setup.md) is now a full walkthrough** and covers
  **Visual Studio 2022 17.14+** as well as 2026. It documents the required IDE versions and why
  **Manage models** can be missing: Bring Your Own Model is unavailable to Copilot Business and
  Enterprise accounts, so an Organization-tied GitHub account has to be swapped for a personal one.
  The same points are in [Troubleshooting](docs/troubleshooting.md).
- **Settings and About moved to the top of the navigation pane**, after Home and Logs, instead of
  being pinned to the bottom.
- **The Port setting moved to the Home tab**, next to the listening URL it controls (with its
  restart-needed notice), replacing the "The port can be changed in Settings" link.
- **The proxy status dot on the Home navigation icon moved to the top-right corner** (was bottom-right).
- Model family aliases are now labelled without a version — "Opus", "Sonnet", "Haiku", "Fable" — since
  the version an alias resolves to is the CLI's choice and changes over time. Explicit versions keep
  their version in the label ("Opus 5", "Opus 4.8"), so the two never collide in the picker.
- **The model list is sorted for Copilot's picker** — alphabetically by name, then by effort from
  lightest to heaviest, so each model's variants stay together (Opus ×5, Opus 4.6 ×5, Opus 4.7 ×5,
  Opus 4.8 ×5, Opus 5 ×5, …) instead of following CLI discovery order.

### Fixed

- **The Logs tab no longer goes silent after stopping and restarting the proxy.** The log provider is
  owned by the app but was being disposed with the host, which dropped its subscriber on the first
  stop/restart.
- **Minimizing no longer loses the saved window position.** Quitting while the window was minimized
  persisted its off-screen placement, so the next launch reopened it somewhere else.

## [v2026.06.28.9] — 2026-06-28

### Added

- Tray and taskbar icons show live proxy status, with Start/Stop/Restart actions available from the
  tray menu (#4).
- "Start minimized to tray" setting, plus assorted UI polish (#5).

### Fixed

- Unhandled exception in `Microsoft.ui.xaml.dll` (#3).
- .NET restore corrected for x64; unused usings removed (#6).

## [v2026.06.28.2] — 2026-06-28

### Fixed

- `WindowActivationHelper` reliably brings the app window to the foreground.

## [v2026.06.28.1] — 2026-06-28

### Added

- WinUI 3 system-tray app hosting the proxy in-process, with Home, Logs, Settings, and About pages,
  single-instance activation, theme support, and persisted user settings (#1).

## [v2026.06.15.0] — 2026-06-15

### Added

- Initial release: a local HTTP server exposing the Ollama API and driving the Claude Code CLI, so
  Visual Studio / VS Code GitHub Copilot Chat's Bring Your Own Model Ollama provider can use Claude Code — including
  Copilot's native in-chat diff/accept-reject UI via the tool-call bridge.

[v2026.08.01.10]: https://github.com/icnocop/ClaudeCodeOllamaProxy/compare/v2026.06.28.9...v2026.08.01.10
[v2026.06.28.9]: https://github.com/icnocop/ClaudeCodeOllamaProxy/releases/tag/v2026.06.28.9
[v2026.06.28.2]: https://github.com/icnocop/ClaudeCodeOllamaProxy/releases/tag/v2026.06.28.2
[v2026.06.28.1]: https://github.com/icnocop/ClaudeCodeOllamaProxy/releases/tag/v2026.06.28.1
[v2026.06.15.0]: https://github.com/icnocop/ClaudeCodeOllamaProxy/releases/tag/v2026.06.15.0
