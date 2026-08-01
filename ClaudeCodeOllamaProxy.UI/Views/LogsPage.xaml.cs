using System.Collections.Specialized;
using ClaudeCodeOllamaProxy.UI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ClaudeCodeOllamaProxy.UI.Views;

public sealed partial class LogsPage : Page
{
    // Log lines can arrive in bursts (especially at Trace), and the whole text is re-rendered per
    // update, so collapse a burst into one render instead of one per line.
    private static readonly TimeSpan RenderDelay = TimeSpan.FromMilliseconds(150);

    // Guards the control event handlers while the page pushes persisted settings into them.
    private bool _loading;

    // A render was requested but deferred (the user had text selected); re-render once that clears.
    private bool _renderPending;

    private DispatcherQueueTimer? _renderTimer;
    private ScrollViewer? _logScroller;

    private ProxyHostController Controller => App.ProxyController;

    public LogsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        LoadFromSettings();

        _renderTimer = DispatcherQueue.CreateTimer();
        _renderTimer.Interval = RenderDelay;
        _renderTimer.IsRepeating = false;
        _renderTimer.Tick += (_, _) => Render();

        Controller.Logs.CollectionChanged += OnLogsChanged;
        Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Controller.Logs.CollectionChanged -= OnLogsChanged;

        if (_renderTimer is not null)
        {
            _renderTimer.Stop();
            _renderTimer = null;
        }

        _logScroller = null;
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            var current = App.Settings.LogLevel;
            foreach (var item in LogLevelCombo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, current, StringComparison.OrdinalIgnoreCase))
                {
                    LogLevelCombo.SelectedItem = item;
                    break;
                }
            }

            ClaudeCliDebugToggle.IsOn = App.Settings.ClaudeCliDebug;
        }
        finally
        {
            _loading = false;
        }
    }

    private void LogLevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;

        if ((LogLevelCombo.SelectedItem as ComboBoxItem)?.Tag is string level)
        {
            App.Settings.LogLevel = level;
            Controller.ApplyLogLevel();
        }
    }

    // Set the control and let SelectionChanged persist it (same shape as the port's Default button).
    private void LogLevelDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in LogLevelCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, SettingsStore.DefaultLogLevel, StringComparison.OrdinalIgnoreCase))
            {
                LogLevelCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void ClaudeCliDebugToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        App.Settings.ClaudeCliDebug = ClaudeCliDebugToggle.IsOn;
        Controller.ApplyClaudeCliDebug();
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRender();

    private void ScheduleRender() => _renderTimer?.Start(); // restarts the window on each burst item

    /// <summary>
    /// Replace the whole log text and stick to the bottom. Assigning <c>Text</c> drops any selection,
    /// so a render is deferred while the user has one — otherwise a line arriving mid-drag would wipe
    /// what they were selecting. The deferred render runs as soon as the selection clears.
    /// </summary>
    private void Render()
    {
        if (LogBox.SelectionLength > 0)
        {
            _renderPending = true;
            return;
        }

        _renderPending = false;

        var atBottom = IsScrolledToBottom();
        LogBox.Text = string.Join(Environment.NewLine, Controller.Logs);

        // Only follow the tail if the user hadn't scrolled up to read something.
        if (atBottom)
            ScrollToEnd();
    }

    private void LogBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var hasSelection = LogBox.SelectionLength > 0;
        CopyButton.IsEnabled = hasSelection;

        if (!hasSelection && _renderPending)
            Render();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (LogBox.SelectionLength > 0)
            ClipboardHelper.SetText(LogBox.SelectedText);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) => Controller.Logs.Clear();

    private bool IsScrolledToBottom()
    {
        var scroller = LogScroller();
        if (scroller is null)
            return true; // not laid out yet — treat as "following the tail"

        // A pixel of slack: the offset rarely lands exactly on ScrollableHeight.
        return scroller.VerticalOffset >= scroller.ScrollableHeight - 1;
    }

    private void ScrollToEnd() => DispatcherQueue.TryEnqueue(() =>
    {
        var scroller = LogScroller();
        scroller?.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true);
    });

    /// <summary>The TextBox's own ScrollViewer (template part "ContentElement"), cached once found.</summary>
    private ScrollViewer? LogScroller() => _logScroller ??= FindDescendant<ScrollViewer>(LogBox);

    private static T? FindDescendant<T>(DependencyObject root) where T : class
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } nested)
                return nested;
        }

        return null;
    }
}
