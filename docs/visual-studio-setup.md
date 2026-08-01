# Configure Visual Studio

How to point Visual Studio's GitHub Copilot Chat at this proxy. Applies to **Visual Studio 2022**
and **Visual Studio 2026** — the flow is the same in both.

For VS Code, see [VS Code setup](vscode-setup.md).

## Requirements

- **Visual Studio 2022 17.14 or later**, or **Visual Studio 2026**. Bring Your Own Model arrived in
  Copilot Chat in 17.14. Microsoft's
  [BYOM documentation](https://learn.microsoft.com/en-us/visualstudio/ide/copilot-select-add-models?view=visualstudio#bring-your-own-model-byom)
  still lists only OpenAI / Anthropic / Google and never documented the servicing build that added
  **Ollama**, so the reliable check is the UI itself: if **Ollama** is missing from the **Choose an
  available provider** list in step 4, update to the latest servicing build.
- A GitHub account whose Copilot plan allows Bring Your Own Model — see
  [If you don't see "Manage models"](#if-you-dont-see-manage-models) below.
- The proxy running. Copy the listening URL from the tray app's **Home** page (default
  `http://localhost:11434`).

> These steps were walked through on **Visual Studio Professional 2026 18.8**.

## If you don't see "Manage models"

The **Manage models** entry is hidden for Copilot **Business** and **Enterprise** accounts. Microsoft
states it plainly: *"Support for custom models is **not** available for Copilot Business or
Enterprise users."*
([Limitations and considerations](https://learn.microsoft.com/en-us/visualstudio/ide/copilot-select-add-models?view=visualstudio#limitations-and-considerations))

A GitHub account tied to an Organization usually carries one of those plans, so **Manage models**
won't appear. To work around it, add a **personal** GitHub account, make it the **active** account,
then reopen the model dropdown:
[Switch the active account](https://learn.microsoft.com/en-us/visualstudio/ide/work-with-github-accounts?view=visualstudio#switch-the-active-account).

This is a Visual Studio limitation. It does **not** apply to VS Code, where BYOK models work without
a Copilot plan at all.

## Steps

1. Open **Copilot Chat** — press **Ctrl+\\**, or select the Copilot badge in the top-right corner of
   the IDE and choose **Open Chat Window**.
2. At the bottom of the chat window, open the **model dropdown** (it reads **Auto** by default).
3. Select **⚙ Manage models**. The **Bring Your Own Model** dialog opens.
4. Under **Choose an available provider**, select **Ollama**.
5. Set **Endpoint URL** to `http://localhost:11434` — or whichever port the tray app's Home page
   shows, if you changed it.
6. Select **Add**.
7. The dialog now lists every model the proxy advertises — one row per model **and effort level**.
   Tick the ones you want to show up in the picker.
8. Select **Save**.
9. Back in the chat window, reopen the model dropdown and pick your model under **Other models**
   (provider **Ollama**). Set the chat mode to **Agent** so Claude can edit files — changes appear in
   Copilot's normal changed-files / diff / Accept-Reject UI.

## Notes

- Each row reads like
  `Opus 4.8 (1M context) · high effort (claude, api) (Opus 4.8 (1M context) · high effort)`. Visual
  Studio renders the model label twice — once as the title and once as the id in parentheses — and
  ignores the friendlier display name the proxy sends. There is no proxy-side fix.
- There is no effort setting in Copilot. Effort is chosen by picking the corresponding **model
  variant** — see [Effort levels](effort-levels.md).
- Changing the proxy's port means re-adding the provider with the new endpoint URL.
- An empty model list in step 7 means the proxy isn't running, or the endpoint port doesn't match.
  Check with `curl http://localhost:11434/api/tags`, and see [Troubleshooting](troubleshooting.md).
