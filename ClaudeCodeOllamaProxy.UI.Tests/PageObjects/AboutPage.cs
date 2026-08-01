using FlaUI.Core.AutomationElements;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>The About page (app name, version, update check, GitHub link).</summary>
public sealed class AboutPage(Window window) : PageObject(window)
{
    public Label VersionText => Element("VersionText").AsLabel();

    /// <summary>Runs an on-demand update check against the project's GitHub releases.</summary>
    public Button CheckUpdatesButton => Element("CheckUpdatesButton").AsButton();

    /// <summary>"You're using the latest version." / the failure message. Hidden until a check runs.</summary>
    public Label UpdateStatusText => Element("UpdateStatusText").AsLabel();

    /// <summary>The highlighted "Update available" notice (a WinUI InfoBar). Hidden when up to date.</summary>
    public AutomationElement UpdateBar => Element("UpdateBar");
}
