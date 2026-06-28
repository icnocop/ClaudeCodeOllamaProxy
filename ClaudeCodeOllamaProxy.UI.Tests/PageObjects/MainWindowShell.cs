using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The main window shell: the NavigationView and access to each page's Page Object.</summary>
public sealed class MainWindowShell(Window window) : PageObject(window)
{
    public void GoHome() => Element("NavHome").Click();
    public void GoLogs() => Element("NavLogs").Click();
    public void GoSettings() => Element("NavSettings").Click();
    public void GoAbout() => Element("NavAbout").Click();

    public HomePage Home => new(Window);
    public LogsPage Logs => new(Window);
    public SettingsPage Settings => new(Window);
    public AboutPage About => new(Window);
}
