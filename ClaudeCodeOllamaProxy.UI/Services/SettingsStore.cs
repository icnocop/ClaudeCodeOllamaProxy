using System.IO;
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

    // Allow tests (or advanced users) to redirect the settings folder via an env var; defaults to
    // %LOCALAPPDATA%\ClaudeCodeOllamaProxy.UI. Lets a test run with isolated settings (no UAC, fixed
    // theme/port) without clobbering the real user settings.
    private static readonly string Dir =
        Environment.GetEnvironmentVariable("CLAUDECODEOLLAMAPROXY_UI_DATADIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeCodeOllamaProxy.UI");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    /// <summary>Persisted main-window position and size (screen coordinates).</summary>
    public sealed record WindowPlacement(int X, int Y, int Width, int Height);

    private sealed class Data
    {
        public int Port { get; set; } = DefaultPort;
        public string Theme { get; set; } = AppTheme.System;
        public WindowPlacement? Window { get; set; }
        public bool NavPaneOpen { get; set; } = true;
        public double NavPaneLength { get; set; } = DefaultNavPaneLength;
        public bool RunAsAdmin { get; set; }
        public bool MinimizeToTrayOnClose { get; set; } = true;
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort persistence; ignore IO failures.
        }
    }
}
