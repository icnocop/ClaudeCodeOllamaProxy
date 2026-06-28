using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The Logs page (live host output).</summary>
public sealed class LogsPage(Window window) : PageObject(window)
{
    public ListBox LogList => Element("LogList").AsListBox();
    public Button ClearButton => Element("ClearButton").AsButton();
}
