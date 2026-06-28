# Troubleshooting

- **`debugger_launch_unit_test` (and similar) "caps execution at 30 seconds":** that cap is in
  **Copilot's own tool** (VS-side), not this proxy or the claude CLI — the proxy can't extend it.
  `Claude.McpToolTimeoutMs` only governs how long the proxy/CLI will wait for a bridged tool result
  (already generous); it does not change Copilot's per-tool caps.
- **"There's no `debugger_continue`/`debugger_evaluate_expression` tool":** each Copilot mode
  (Ask/Plan/Agent, Debugger, Modernize, Profiler, Test) ships a **fixed set of tools chosen by
  Copilot**, and the proxy bridges exactly those — it can't add tools Copilot didn't send. So in
  Debugger mode Claude only has what VS exposes (e.g. launch test, set breakpoints, read the output
  window). This is a Copilot tool-set limitation, not a proxy bug.
- **"Read N files in <project>.csproj" → "Summarization completed" → "exceeded the maximum token
  limit":** this is **Copilot's own context-gathering**, not the proxy and not a bridged tool call
  (the proxy logs every tool Claude invokes, and none of these whole-project reads appear there). In
  Debugger mode especially, analyzing an exception pulls the *entire project that owns the faulting
  file* into context; thousands of source files exceed even a 1M-token window. It is **not** the
  32 KB clamp — if the model chip still shows the real size (e.g. "1M context"), `claude.context_length`
  was read correctly and the overflow is genuine volume. Mitigations are **VS-side**: scope the
  request with `#file`/method references instead of an open "analyze the exception"; avoid
  whole-project Debugger analysis on very large solutions; and start a **fresh chat** after an
  overflow (the summarized context persists, and summarization also rewrites the conversation, which
  supersedes the proxy's parked bridge — visible as `No pending bridge found … superseded`). The
  proxy bridges only the tools Copilot sends and can't stop Copilot from gathering context.
