using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The About page (app name, version, GitHub link).</summary>
public sealed class AboutPage(Window window) : PageObject(window)
{
    public Label VersionText => Element("VersionText").AsLabel();
}
