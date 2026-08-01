using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The Home page (run state, listening URL, port, Start/Stop/Restart).</summary>
public sealed class HomePage(Window window) : PageObject(window)
{
    public Label StatusText => Element("StatusText").AsLabel();
    public TextBox UrlBox => Element("UrlBox").AsTextBox();
    public Button CopyButton => Element("CopyButton").AsButton();
    public Button StartButton => Element("StartButton").AsButton();
    public Button StopButton => Element("StopButton").AsButton();
    public Button RestartButton => Element("RestartButton").AsButton();

    /// <summary>The port the proxy listens on (WinUI NumberBox → UIA Edit).</summary>
    public TextBox PortBox => Element("PortBox").AsTextBox();

    /// <summary>Resets the port to the default Ollama port (11434).</summary>
    public Button PortDefaultButton => Element("PortDefaultButton").AsButton();
}
