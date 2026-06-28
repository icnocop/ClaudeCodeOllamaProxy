// Standalone console host: build the proxy and run it (blocks until shutdown).
// All wiring lives in ClaudeCodeOllamaProxy.Core/ProxyHost.cs, shared with the WinUI tray app.
ClaudeCodeOllamaProxy.ProxyHost.Create(args).Run();
