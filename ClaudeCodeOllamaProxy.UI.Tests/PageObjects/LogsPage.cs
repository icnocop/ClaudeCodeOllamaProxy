using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The Logs page (live host output, verbosity, CLI debug, copy/clear).</summary>
public sealed class LogsPage(Window window) : PageObject(window)
{
    /// <summary>The log output — a read-only TextBox, so the whole buffer is one selectable string.</summary>
    public TextBox LogBox => Element("LogBox").AsTextBox();

    public Button ClearButton => Element("ClearButton").AsButton();
    public Button CopyButton => Element("CopyButton").AsButton();

    /// <summary>The logging-verbosity picker (Trace … Critical).</summary>
    public ComboBox LogLevelCombo => Element("LogLevelCombo").AsComboBox();

    /// <summary>Resets the verbosity to the default (Information).</summary>
    public Button LogLevelDefaultButton => Element("LogLevelDefaultButton").AsButton();

    /// <summary>The "Claude CLI debug output" toggle (passes --debug to the CLI).</summary>
    public AutomationElement ClaudeCliDebugToggle => Element("ClaudeCliDebugToggle");

    /// <summary>Select a verbosity by its display text, e.g. "Trace".</summary>
    public void SetLogLevel(string itemText) => LogLevelCombo.Select(itemText);
}
