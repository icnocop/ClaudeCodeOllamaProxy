using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace ClaudeCodeOllamaProxy.UI.Tests.PageObjects;

/// <summary>
/// Base Page Object. Each derived page exposes its elements as properties whose getters resolve the
/// element by its UIA AutomationId on access — the idiomatic .NET POM style (no attribute/PageFactory).
/// In WinUI, a control's <c>x:Name</c> is its AutomationId when none is set explicitly.
/// </summary>
public abstract class PageObject(Window window)
{
    protected Window Window { get; } = window;

    protected AutomationElement Element(string automationId) =>
        Retry.WhileNull(
            () => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(200)).Result
        ?? throw new InvalidOperationException($"Element with AutomationId '{automationId}' was not found.");
}
