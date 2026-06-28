using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Composes the app icon at runtime: the brand glyph (<c>Assets/AppIcon.ico</c>) with a small status
/// circle overlaid in the bottom-right corner. The circle color reflects the proxy host's
/// <see cref="ProxyState"/> — soft/muted greens, grays, and ambers rather than saturated primaries.
/// <para>
/// Two outputs are produced from the same composition: a single <c>HICON</c> for the system-tray icon
/// (<see cref="CreateIcon"/>), and a multi-resolution <c>.ico</c> file for the window/taskbar icon
/// (<see cref="EnsureIconFile"/>). The multi-resolution file is what keeps the title-bar and taskbar
/// icons crisp at every DPI — drawing each frame from the matching native-size base frame avoids the
/// blurry up/down-scaling you get from a single fixed-size icon.
/// </para>
/// </summary>
public static class StatusIconRenderer
{
    // The tray icon is small; a single 32px HICON is downscaled cleanly by the shell.
    private const int TrayIconSize = 32;

    // Frames baked into the window/taskbar .ico. Cover the title bar (16-24px), the taskbar across DPIs
    // (24-48px), and large views (256px). The base AppIcon.ico has exact frames for these sizes, so each
    // is drawn 1:1 with no rescaling of the glyph.
    private static readonly int[] FrameSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    private static readonly string IconDir = Path.Combine(Path.GetTempPath(), "ClaudeCodeOllamaProxy.UI");
    private static readonly HashSet<ProxyState> WrittenThisRun = [];
    private static readonly object FileLock = new();

    /// <summary>
    /// Builds the composed tray icon for <paramref name="state"/>. The returned <c>HICON</c> must be
    /// released with <c>DestroyIcon</c> once it is no longer the icon shown by the tray.
    /// </summary>
    public static IntPtr CreateIcon(ProxyState state)
    {
        using var bmp = RenderFrame(state, TrayIconSize);
        return bmp.GetHicon();
    }

    /// <summary>
    /// Builds the plain brand icon (no status circle) at <paramref name="size"/> pixels, for the window's
    /// title-bar icon. The returned <c>HICON</c> must be released with <c>DestroyIcon</c>. Render it large
    /// (e.g. 64px) so the title bar always downscales — and therefore stays crisp — at any DPI.
    /// </summary>
    public static IntPtr CreatePlainIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawBaseGlyph(g, size);
        }

        var handle = bmp.GetHicon();
        bmp.Dispose();
        return handle;
    }

    /// <summary>
    /// Ensures a multi-resolution <c>.ico</c> for <paramref name="state"/> exists on disk (in the temp
    /// folder) and returns its path, suitable for <c>AppWindow.SetIcon(path)</c>. Written once per state
    /// per process run; the file is not held open after <c>SetIcon</c> reads it.
    /// </summary>
    public static string EnsureIconFile(ProxyState state)
    {
        var path = Path.Combine(IconDir, $"status-{state}.ico");
        lock (FileLock)
        {
            if (WrittenThisRun.Add(state) || !File.Exists(path))
            {
                Directory.CreateDirectory(IconDir);
                File.WriteAllBytes(path, BuildIcoBytes(state));
            }
        }

        return path;
    }

    /// <summary>Composes a multi-resolution icon (one PNG frame per <see cref="FrameSizes"/> entry).</summary>
    private static byte[] BuildIcoBytes(ProxyState state)
    {
        var frames = new List<byte[]>(FrameSizes.Length);
        foreach (var size in FrameSizes)
        {
            using var bmp = RenderFrame(state, size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);   // Vista+ icons may store PNG frames directly.
            frames.Add(ms.ToArray());
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        // ICONDIR header.
        writer.Write((short)0);                 // reserved
        writer.Write((short)1);                 // type: 1 = icon
        writer.Write((short)FrameSizes.Length); // image count

        // ICONDIRENTRY for each frame (16 bytes each); image data follows all entries.
        var offset = 6 + (16 * FrameSizes.Length);
        for (var i = 0; i < FrameSizes.Length; i++)
        {
            var size = FrameSizes[i];
            var dimension = (byte)(size >= 256 ? 0 : size);   // 0 encodes 256
            writer.Write(dimension);            // width
            writer.Write(dimension);            // height
            writer.Write((byte)0);              // palette count (0 = no palette)
            writer.Write((byte)0);              // reserved
            writer.Write((short)1);             // color planes
            writer.Write((short)32);            // bits per pixel
            writer.Write(frames[i].Length);     // bytes in resource
            writer.Write(offset);               // offset to image data
            offset += frames[i].Length;
        }

        foreach (var frame in frames)
            writer.Write(frame);

        writer.Flush();
        return output.ToArray();
    }

    private static Bitmap RenderFrame(ProxyState state, int size)
    {
        var (fill, edge) = ColorsFor(state);

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            DrawBaseGlyph(g, size);
            DrawStatusCircle(g, size, fill, edge);
        }

        return bmp;
    }

    // Draw the glyph slightly larger than the frame so it fills the taskbar button more fully (closer to
    // how apps like Teams fill theirs). The small overflow only crops the base art's transparent margin.
    private const float GlyphOverscan = 1.10f;

    private static void DrawBaseGlyph(Graphics g, int size)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (!File.Exists(path))
            return;   // No brand icon available — fall back to just the status circle on transparent.

        try
        {
            var drawSize = size * GlyphOverscan;
            var offset = (size - drawSize) / 2f;   // negative: center the overscanned glyph

            // Pull a base frame at least as large as the draw size so the glyph is downscaled (crisp),
            // never upscaled. The base icon has native frames up to 256px.
            var source = (int)Math.Ceiling(drawSize);
            using var icon = new Icon(path, source, source);
            using var glyph = icon.ToBitmap();
            g.DrawImage(glyph, new RectangleF(offset, offset, drawSize, drawSize));
        }
        catch
        {
            // A malformed/locked icon file shouldn't crash icon rendering — show the status circle alone.
        }
    }

    private static void DrawStatusCircle(Graphics g, int size, Color fill, Color edge)
    {
        // Circle occupies the bottom-right ~55% of the icon, with a soft white ring so it reads on
        // both light and dark backgrounds.
        var diameter = size * 0.55f;
        var x = size - diameter;
        var y = size - diameter;

        using var ringBrush = new SolidBrush(StatusPalette.Ring.ToDrawing());
        g.FillEllipse(ringBrush, x, y, diameter, diameter);

        var inset = Math.Max(1f, size * 0.05f);
        var innerX = x + inset;
        var innerY = y + inset;
        var innerD = diameter - (inset * 2f);

        using var fillBrush = new SolidBrush(fill);
        g.FillEllipse(fillBrush, innerX, innerY, innerD, innerD);

        using var edgePen = new Pen(edge, Math.Max(1f, size * 0.04f));
        g.DrawEllipse(edgePen, innerX, innerY, innerD, innerD);
    }

    private static (Color Fill, Color Edge) ColorsFor(ProxyState state)
    {
        var (fill, edge) = StatusPalette.For(state);
        return (fill.ToDrawing(), edge.ToDrawing());
    }
}
