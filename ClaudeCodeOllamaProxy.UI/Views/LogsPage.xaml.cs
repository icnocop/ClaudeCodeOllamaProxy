using System.Collections.Specialized;
using ClaudeCodeOllamaProxy.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ClaudeCodeOllamaProxy.UI.Views;

public sealed partial class LogsPage : Page
{
    private ProxyHostController Controller => App.ProxyController;

    public LogsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        LogList.ItemsSource = Controller.Logs;
        Controller.Logs.CollectionChanged += OnLogsChanged;
        ScrollToEnd();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Controller.Logs.CollectionChanged -= OnLogsChanged;
        LogList.ItemsSource = null;
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (Controller.Logs.Count > 0)
            DispatcherQueue.TryEnqueue(() => LogList.ScrollIntoView(Controller.Logs[^1]));
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) => Controller.Logs.Clear();
}
