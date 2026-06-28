using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Centers the main window on first launch and restores its last position/size on subsequent launches.
/// WinUI has no automatic window-placement persistence for unpackaged apps, so this saves to
/// <see cref="SettingsStore"/> and restores via <see cref="AppWindow"/>. The saved placement also records
/// which monitor the window was on: if that monitor is gone, we fall back to the primary monitor; if it's
/// still there but changed resolution, the window is shrunk to fit its work area.
/// </summary>
public static class WindowPlacementHelper
{
    /// <summary>Apply the saved placement if its monitor is still usable; otherwise center on primary.</summary>
    public static void ApplyInitialPlacement(AppWindow appWindow, SettingsStore settings)
    {
        if (settings.Window is { } saved && TryResolveSavedPlacement(saved, out var rect))
            appWindow.MoveAndResize(rect);
        else
            ResizeToDefault(appWindow);
    }

    /// <summary>Resize the window to the default size (clamped to the screen) and re-center it.</summary>
    public static void ResizeToDefault(AppWindow appWindow) =>
        CenterOnScreen(appWindow, SettingsStore.DefaultWindowWidth, SettingsStore.DefaultWindowHeight);

    /// <summary>Persist the window's current position, size, and the monitor it's on.</summary>
    public static void Save(AppWindow appWindow, SettingsStore settings)
    {
        var pos = appWindow.Position;
        var size = appWindow.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var monitor = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest)?.OuterBounds
                      ?? default;
        settings.Window = new SettingsStore.WindowPlacement(
            pos.X, pos.Y, size.Width, size.Height,
            monitor.X, monitor.Y, monitor.Width, monitor.Height);
    }

    private static void CenterOnScreen(AppWindow appWindow, int width, int height)
    {
        var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        // The default size must never exceed the monitor (and never drop below the minimum), so clamp first.
        var size = ClampToWorkArea(new RectInt32(work.X, work.Y, width, height), work);
        var x = work.X + ((work.Width - size.Width) / 2);
        var y = work.Y + ((work.Height - size.Height) / 2);
        appWindow.MoveAndResize(new RectInt32(x, y, size.Width, size.Height));
    }

    /// <summary>
    /// Resolve a saved placement to a concrete on-screen rectangle, or return false to fall back to the
    /// primary monitor. A placement that recorded its monitor requires that monitor to still exist (matched
    /// by its outer-bounds origin); the rectangle is then clamped to the monitor's current work area so a
    /// resolution change shrinks the window to fit. Legacy placements (no monitor) use a center-visibility test.
    /// </summary>
    private static bool TryResolveSavedPlacement(SettingsStore.WindowPlacement p, out RectInt32 rect)
    {
        rect = default;
        if (p.Width <= 0 || p.Height <= 0)
            return false;

        if (p.MonitorWidth > 0 && p.MonitorHeight > 0)
        {
            // Match the saved monitor by its outer-bounds origin (stable across a resolution change).
            // NB: index into FindAll() rather than enumerating it — the WinRT IReadOnlyList projection
            // throws InvalidCastException from its LINQ/foreach enumerator.
            var displays = DisplayArea.FindAll();
            for (var i = 0; i < displays.Count; i++)
            {
                var bounds = displays[i].OuterBounds;
                if (bounds.X != p.MonitorX || bounds.Y != p.MonitorY)
                    continue;

                rect = ClampToWorkArea(new RectInt32(p.X, p.Y, p.Width, p.Height), displays[i].WorkArea);
                return true;
            }

            return false; // the saved monitor is gone — fall back to primary
        }

        if (!IsOnScreen(p))
            return false;

        rect = new RectInt32(p.X, p.Y, p.Width, p.Height);
        return true;
    }

    /// <summary>Clamp a rectangle's size between the minimum and the work area, then keep it fully on-screen.</summary>
    private static RectInt32 ClampToWorkArea(RectInt32 desired, RectInt32 work)
    {
        var width = Math.Clamp(desired.Width, SettingsStore.MinWindowWidth, Math.Max(SettingsStore.MinWindowWidth, work.Width));
        var height = Math.Clamp(desired.Height, SettingsStore.MinWindowHeight, Math.Max(SettingsStore.MinWindowHeight, work.Height));
        var x = Math.Clamp(desired.X, work.X, Math.Max(work.X, work.X + work.Width - width));
        var y = Math.Clamp(desired.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - height));
        return new RectInt32(x, y, width, height);
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
