using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The Home page (run state, listening URL, Start/Stop/Restart).</summary>
public sealed class HomePage(Window window) : PageObject(window)
{
    public Label StatusText => Element("StatusText").AsLabel();
    public TextBox UrlBox => Element("UrlBox").AsTextBox();
    public Button CopyButton => Element("CopyButton").AsButton();
    public Button StartButton => Element("StartButton").AsButton();
    public Button StopButton => Element("StopButton").AsButton();
    public Button RestartButton => Element("RestartButton").AsButton();
}
