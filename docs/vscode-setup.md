# Configure VS Code

How to point VS Code's GitHub Copilot Chat at this proxy using its Ollama **Bring Your Own Key**
(BYOK) provider.

For Visual Studio, see [Visual Studio setup](visual-studio-setup.md).

> These steps are written from the
> [VS Code language-models documentation](https://code.visualstudio.com/docs/copilot/customization/language-models)
> and the provider source
> ([`ollamaProvider.ts`](https://github.com/microsoft/vscode-copilot-chat/blob/main/src/extension/byok/vscode-node/ollamaProvider.ts)),
> not from a hands-on run. The Visual Studio steps were verified directly; these were not.

## Requirements

- **VS Code 1.99** (March 2025) or later — the release that added BYOK with an Ollama provider.
- **VS Code 1.117** (April 2026) or later on **Copilot Business / Enterprise** — BYOK wasn't
  available to those plans before it.
- The proxy running on the endpoint you configure below.

Unlike Visual Studio, no Copilot plan is required: BYOK models work without a Copilot subscription,
and as of **1.122** (May 2026) without signing into GitHub at all.

## Steps

1. Open the **Chat** view (**Ctrl+Alt+I**).
2. Open the model picker and select **Manage Models…** — or run **Chat: Manage Language Models**
   from the Command Palette (**Ctrl+Shift+P**).
3. Choose **Ollama** from the provider list.
4. Set the endpoint. The provider defaults to `http://localhost:11434`. For any other port, set
   `github.copilot.chat.byok.ollamaEndpoint` in your **Settings (JSON)**:

   ```json
   "github.copilot.chat.byok.ollamaEndpoint": "http://localhost:11435"
   ```

   Note this setting is *application*-scoped, so it can't be overridden per workspace or through
   `devcontainer.json` — see [microsoft/vscode#279356](https://github.com/microsoft/vscode/issues/279356).
5. Tick the models you want in the picker and confirm. The proxy advertises one entry per model
   **and effort level** — see [Effort levels](effort-levels.md).
6. Pick the model in the chat model picker and switch the chat mode to **Agent** so Claude can edit
   files.

## Notes

- VS Code's documentation marks the **built-in** Ollama provider as deprecated in favour of Ollama's
  own marketplace extension. These steps use the built-in provider; the Ollama extension has not
  been tested against this proxy.
- An empty model list means the proxy isn't running, or the endpoint doesn't match. Check with
  `curl http://localhost:11434/api/tags`, and see [Troubleshooting](troubleshooting.md).
