using System.Text.Json;

namespace ClaudeCodeOllamaProxy.UI.Services;

/// <summary>
/// Persists the tray app's user settings (currently the proxy port) to a small JSON file under
/// %LOCALAPPDATA%\ClaudeCodeOllamaProxy.UI\settings.json. This is an unpackaged app, so it can't use
/// <c>Windows.Storage.ApplicationData</c>.
/// </summary>
public sealed class SettingsStore
{
    public const int DefaultPort = 11434;
    public const double DefaultNavPaneLength = 150;
    public const int DefaultWindowWidth = 690;
    public const int DefaultWindowHeight = 840;

    /// <summary>The smallest the main window may be sized to (enforced both on restore and live resizing).</summary>
    public const int MinWindowWidth = 420;
    public const int MinWindowHeight = 420;

    // Allow tests (or advanced users) to redirect the settings folder via an env var; defaults to
    // %LOCALAPPDATA%\ClaudeCodeOllamaProxy.UI. Lets a test run with isolated settings (no UAC, fixed
    // theme/port) without clobbering the real user settings.
    private static readonly string Dir =
        Environment.GetEnvironmentVariable("CLAUDECODEOLLAMAPROXY_UI_DATADIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeCodeOllamaProxy.UI");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    // Cached, reused across saves (CA1869: a fresh JsonSerializerOptions per call hurts performance).
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>The per-user data folder (%LOCALAPPDATA%\ClaudeCodeOllamaProxy.UI, or the test override).
    /// Shared with <see cref="CrashLog"/> so crash logs land next to settings.json.</summary>
    public static string DataDirectory => Dir;

    /// <summary>
    /// Persisted main-window position and size (screen coordinates), plus the outer bounds of the monitor
    /// it was on. The monitor bounds let us restore onto the same display and detect when that display is
    /// gone (fall back to primary) or has changed resolution (shrink the window to fit). The monitor fields
    /// are 0 for placements saved by older versions, which are treated as "monitor unknown".
    /// </summary>
    public sealed record WindowPlacement(
        int X, int Y, int Width, int Height,
        int MonitorX = 0, int MonitorY = 0, int MonitorWidth = 0, int MonitorHeight = 0);

    /// <summary>The on-disk shape of settings.json. Public so tests can serialize a real instance instead
    /// of hand-writing the JSON (keeping the test in lock-step with the schema).</summary>
    public sealed class Data
    {
        public int Port { get; set; } = DefaultPort;
        public string Theme { get; set; } = AppTheme.System;
        public WindowPlacement? Window { get; set; }
        public bool NavPaneOpen { get; set; } = true;
        public double NavPaneLength { get; set; } = DefaultNavPaneLength;
        public bool RunAsAdmin { get; set; }
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public bool StartMinimizedToTray { get; set; }
    }

    private Data _data = new();

    public SettingsStore() => Load();

    public int Port
    {
        get => _data.Port;
        set
        {
            if (_data.Port == value)
                return;
            _data.Port = value;
            Save();
        }
    }

    /// <summary>Whether the navigation pane was left open (true) or collapsed (false).</summary>
    public bool NavPaneOpen
    {
        get => _data.NavPaneOpen;
        set
        {
            if (_data.NavPaneOpen == value)
                return;
            _data.NavPaneOpen = value;
            Save();
        }
    }

    /// <summary>The navigation pane's open width.</summary>
    public double NavPaneLength
    {
        get => _data.NavPaneLength;
        set
        {
            if (_data.NavPaneLength == value)
                return;
            _data.NavPaneLength = value;
            Save();
        }
    }

    /// <summary>Last main-window placement, or null when never saved (first launch ⇒ center).</summary>
    public WindowPlacement? Window
    {
        get => _data.Window;
        set
        {
            _data.Window = value;
            Save();
        }
    }

    /// <summary>When true (default), closing the main window hides it to the tray instead of quitting.</summary>
    public bool MinimizeToTrayOnClose
    {
        get => _data.MinimizeToTrayOnClose;
        set
        {
            if (_data.MinimizeToTrayOnClose == value)
                return;
            _data.MinimizeToTrayOnClose = value;
            Save();
        }
    }

    /// <summary>When true, the app starts hidden in the tray (no window shown) on a normal launch.</summary>
    public bool StartMinimizedToTray
    {
        get => _data.StartMinimizedToTray;
        set
        {
            if (_data.StartMinimizedToTray == value)
                return;
            _data.StartMinimizedToTray = value;
            Save();
        }
    }

    /// <summary>Reset every persisted setting back to its default value.</summary>
    public void ResetToDefaults()
    {
        _data = new Data();
        Save();
    }

    /// <summary>Whether the app should relaunch elevated on startup (and run the Claude CLI elevated).</summary>
    public bool RunAsAdmin
    {
        get => _data.RunAsAdmin;
        set
        {
            if (_data.RunAsAdmin == value)
                return;
            _data.RunAsAdmin = value;
            Save();
        }
    }

    /// <summary>One of <see cref="AppTheme"/> (System / Light / Dark).</summary>
    public string Theme
    {
        get => _data.Theme;
        set
        {
            if (_data.Theme == value)
                return;
            _data.Theme = value;
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch
        {
            _data = new Data();
        }

        if (_data.Port is < 1 or > 65535)
            _data.Port = DefaultPort;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, SerializerOptions));
        }
        catch
        {
            // Best-effort persistence; ignore IO failures.
        }
    }
}
