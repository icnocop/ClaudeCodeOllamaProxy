using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The Settings page (theme, toggles, reset). Port lives on Home; CLI debug on Logs.</summary>
public sealed class SettingsPage(Window window) : PageObject(window)
{
    public ComboBox ThemeCombo => Element("ThemeCombo").AsComboBox();
    public Button ResetButton => Element("ResetButton").AsButton();

    /// <summary>The "Run at startup" toggle (WinUI ToggleSwitch → UIA Toggle pattern).</summary>
    public AutomationElement StartupToggle => Element("StartupToggle");

    /// <summary>The "Minimize to system tray on close" toggle.</summary>
    public AutomationElement MinimizeToTrayToggle => Element("MinimizeToTrayToggle");

    /// <summary>The "Start minimized to tray" toggle.</summary>
    public AutomationElement StartMinimizedToggle => Element("StartMinimizedToggle");

    /// <summary>The "Automatically check for updates when the app starts" toggle.</summary>
    public AutomationElement UpdateCheckToggle => Element("UpdateCheckToggle");

    /// <summary>The "Include pre-release versions" toggle.</summary>
    public AutomationElement PrereleaseToggle => Element("PrereleaseToggle");

    /// <summary>Select a theme: "System default", "Light", or "Dark".</summary>
    public void SetTheme(string itemText) => ThemeCombo.Select(itemText);
}
