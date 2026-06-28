using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Centers the main window on first launch and restores its last position/size on subsequent launches.
/// WinUI has no automatic window-placement persistence for unpackaged apps, so this saves to
/// <see cref="SettingsStore"/> and restores via <see cref="AppWindow"/>.
/// </summary>
public static class WindowPlacementHelper
{
    /// <summary>Apply the saved placement if it's still visible on a connected display; otherwise center.</summary>
    public static void ApplyInitialPlacement(AppWindow appWindow, SettingsStore settings)
    {
        var saved = settings.Window;
        if (saved is not null && IsOnScreen(saved))
            appWindow.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
        else
            CenterOnScreen(appWindow, SettingsStore.DefaultWindowWidth, SettingsStore.DefaultWindowHeight);
    }

    /// <summary>Resize the window to the default size and re-center it on screen.</summary>
    public static void ResizeToDefault(AppWindow appWindow) =>
        CenterOnScreen(appWindow, SettingsStore.DefaultWindowWidth, SettingsStore.DefaultWindowHeight);

    /// <summary>Persist the window's current position and size.</summary>
    public static void Save(AppWindow appWindow, SettingsStore settings)
    {
        var pos = appWindow.Position;
        var size = appWindow.Size;
        if (size.Width > 0 && size.Height > 0)
            settings.Window = new SettingsStore.WindowPlacement(pos.X, pos.Y, size.Width, size.Height);
    }

    private static void CenterOnScreen(AppWindow appWindow, int width, int height)
    {
        var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var x = work.X + ((work.Width - width) / 2);
        var y = work.Y + ((work.Height - height) / 2);
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private static bool IsOnScreen(SettingsStore.WindowPlacement p)
    {
        if (p.Width <= 0 || p.Height <= 0)
            return false;

        // The window's center must lie within the nearest display's work area (handles a removed monitor).
        var center = new PointInt32(p.X + (p.Width / 2), p.Y + (p.Height / 2));
        var work = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest)?.WorkArea;
        return work is { } a
            && center.X >= a.X && center.X < a.X + a.Width
            && center.Y >= a.Y && center.Y < a.Y + a.Height;
    }
}
