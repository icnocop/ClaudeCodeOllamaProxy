using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;

namespace ClaudeCodeOllamaProxy.UI.Tests.Infrastructure;

/// <summary>
/// Resizes the app window and captures clean PNG screenshots: it captures the DWM *visible* bounds
/// (excluding Windows 11's invisible resize borders, which otherwise leak the desktop behind the edges)
/// and masks the rounded corners to transparency so the corners don't show the background.
/// </summary>
public static class Screenshotter
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint SPI_GETWORKAREA = 0x0030;
    private const int DefaultCornerRadius = 8;
    // Drop the outermost ring (the 1px DWM border + the corner anti-aliasing blended against whatever
    // was behind the window) so screenshots don't show discolored edges.
    private const int DefaultInset = 3;

    /// <summary>Resize the window (window-rect pixels) and center it on screen, clamped to the work area.</summary>
    public static void Resize(Window window, int width, int height)
    {
        var x = 80;
        var y = 80;

        var work = WorkArea();
        if (!work.IsEmpty)
        {
            const int margin = 16;
            width = Math.Min(width, work.Width - margin);
            height = Math.Min(height, work.Height - margin);
            x = work.Left + ((work.Width - width) / 2);
            y = work.Top + ((work.Height - height) / 2);
        }

        MoveWindow(Handle(window), x, y, width, height, bRepaint: true);
    }

    /// <summary>Capture the window to a PNG: trim the invisible borders + discolored edge ring, round the corners.</summary>
    public static void Capture(Window window, string path, int cornerRadius = DefaultCornerRadius, int inset = DefaultInset)
    {
        var bounds = VisibleBounds(Handle(window));
        if (bounds.IsEmpty)
            bounds = window.BoundingRectangle;

        bounds.Inflate(-inset, -inset);

        using var capture = FlaUI.Core.Capturing.Capture.Rectangle(bounds);
        using var rounded = RoundCorners(capture.Bitmap, cornerRadius);
        rounded.Save(path, ImageFormat.Png);
    }

    private static IntPtr Handle(Window window) => window.Properties.NativeWindowHandle.Value;

    private static Rectangle VisibleBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0)
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return Rectangle.Empty;
    }

    private static Rectangle WorkArea()
    {
        var r = new RECT();
        return SystemParametersInfo(SPI_GETWORKAREA, 0, ref r, 0)
            ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
            : Rectangle.Empty;
    }

    private static Bitmap RoundCorners(Bitmap src, int radius)
    {
        var result = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var rect = new Rectangle(0, 0, src.Width, src.Height);
        using var path = new GraphicsPath();
        if (radius > 0)
        {
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
        }
        else
        {
            path.AddRectangle(rect);
        }

        g.SetClip(path);
        g.DrawImage(src, rect, rect, GraphicsUnit.Pixel);
        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
