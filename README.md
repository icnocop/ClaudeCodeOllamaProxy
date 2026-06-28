# Claude Code Ollama Proxy

Use **Claude Code** inside Visual Studio / VS Code **GitHub Copilot Chat** — including Copilot's native
diff / Accept-Reject UI — with no extra extension. The app runs a local server that speaks the
**Ollama API** and drives your existing **Claude Code CLI** login.

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/home-dark.png">
    <img alt="Claude Code Ollama Proxy — Home" src="docs/screenshots/home-light.png" width="650">
  </picture>
</p>

## How it works

```mermaid
flowchart LR
    A["Copilot Chat<br/>(Ollama provider)"] -->|HTTP :11434| B["Claude Code<br/>Ollama Proxy"]
    B -->|launches| C["claude CLI<br/>(your login)"]
```

## Getting started

1. Install the **Claude Code CLI** and sign in (`claude login`).
2. Run the console or tray app — it serves on `http://127.0.0.1:11434`.
3. In Copilot Chat, add an **Ollama** provider pointed at `http://127.0.0.1:11434`, then pick a
   `claude-*` model. Switch to **Agent** mode to let Claude edit files.

The tray app lets you start/stop the proxy, view live logs, change the port and theme, and run at
startup. Closing the window keeps it running in the tray; use **Quit** to stop.

<details>
<summary>📷 More screenshots</summary>

### Logs
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/logs-dark.png">
  <img alt="Logs" src="docs/screenshots/logs-light.png" width="650">
</picture>

### Settings
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/settings-dark.png">
  <img alt="Settings" src="docs/screenshots/settings-light.png" width="650">
</picture>

</details>

## Documentation

- [How it works](docs/how-it-works.md) · [The tool-call bridge](docs/tool-call-bridge.md)
- [Visual Studio setup](docs/visual-studio-setup.md) · [Effort levels](docs/effort-levels.md)
- [Configuration](docs/configuration.md) · [Logging](docs/logging.md) · [Image input](docs/image-input.md)
- [Troubleshooting](docs/troubleshooting.md) · [Limitations](docs/limitations.md) · [References](docs/references.md)
- [Development](DEVELOPMENT.md) — build, run, and test from source

## License

See [LICENSE.txt](LICENSE.txt).
