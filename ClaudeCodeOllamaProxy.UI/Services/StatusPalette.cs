namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Single source of truth for the status-indicator colors shared by every surface that shows the
/// proxy host's <see cref="ProxyState"/> — the tray/taskbar icon (<see cref="StatusIconRenderer"/>,
/// which draws with <see cref="System.Drawing"/>) and the Home nav-item dot (<c>MainWindow</c>, which
/// uses <see cref="Windows.UI.Color"/>). Colors are stored once as ARGB bytes and converted to the
/// type each surface needs, so the palette can't drift out of sync between them.
/// <para>Soft/pastel tones (not saturated primaries); each fill is paired with a darker edge of the
/// same hue for definition, and a near-white ring reads on both light and dark backgrounds.</para>
/// </summary>
internal static class StatusPalette
{
    /// <summary>An ARGB color stored type-agnostically, convertible to either color type used in the app.</summary>
    public readonly record struct Argb(byte A, byte R, byte G, byte B)
    {
        public System.Drawing.Color ToDrawing() => System.Drawing.Color.FromArgb(A, R, G, B);

        public Windows.UI.Color ToWindows() => Windows.UI.Color.FromArgb(A, R, G, B);
    }

    public static readonly Argb RunningFill = new(255, 129, 199, 132);   // muted green
    public static readonly Argb RunningEdge = new(255, 76, 142, 80);
    public static readonly Argb StoppedFill = new(255, 176, 176, 176);   // muted gray
    public static readonly Argb StoppedEdge = new(255, 120, 120, 120);
    public static readonly Argb TransitFill = new(255, 255, 213, 122);   // soft amber (Starting / Stopping)
    public static readonly Argb TransitEdge = new(255, 196, 152, 60);

    /// <summary>The soft near-white ring drawn behind the status fill so it separates from the glyph.</summary>
    public static readonly Argb Ring = new(235, 250, 250, 250);

    /// <summary>The fill + edge pair for a given host state.</summary>
    public static (Argb Fill, Argb Edge) For(ProxyState state) => state switch
    {
        ProxyState.Running => (RunningFill, RunningEdge),
        ProxyState.Stopped => (StoppedFill, StoppedEdge),
        _ => (TransitFill, TransitEdge),   // Starting / Stopping
    };
}
