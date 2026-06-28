# Effort levels

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
