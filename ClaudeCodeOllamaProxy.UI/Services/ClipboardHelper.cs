using Windows.ApplicationModel.DataTransfer;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>Small wrapper around the Windows clipboard for copying text (the listening URL).</summary>
public static class ClipboardHelper
{
    public static void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
