using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The Settings page (port, theme, toggles, reset).</summary>
public sealed class SettingsPage(Window window) : PageObject(window)
{
    public ComboBox ThemeCombo => Element("ThemeCombo").AsComboBox();
    public TextBox PortBox => Element("PortBox").AsTextBox();
    public Button PortDefaultButton => Element("PortDefaultButton").AsButton();
    public Button ResetButton => Element("ResetButton").AsButton();

    /// <summary>The "Run at startup" toggle (WinUI ToggleSwitch → UIA Toggle pattern).</summary>
    public AutomationElement StartupToggle => Element("StartupToggle");

    /// <summary>The "Minimize to system tray on close" toggle.</summary>
    public AutomationElement MinimizeToTrayToggle => Element("MinimizeToTrayToggle");

    /// <summary>Select a theme: "System default", "Light", or "Dark".</summary>
    public void SetTheme(string itemText) => ThemeCombo.Select(itemText);
}
