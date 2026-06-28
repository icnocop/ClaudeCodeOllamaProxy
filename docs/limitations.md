# Limitations

- **In-flight bridge state is in memory only.** Restarting the proxy mid-tool-loop drops the active
  Claude session; the next Copilot request falls back to starting a fresh turn.
- The Claude CLI may impose its own timeout on a long-pending tool call (the SDK imposes none).
- Image input is best-effort (no native multimodal input in the CLI SDK).
- Streaming is message-granular (per assistant message), not token-by-token.
- Requires a Copilot build where the Ollama BYOK provider (and its Agent mode) is enabled.
