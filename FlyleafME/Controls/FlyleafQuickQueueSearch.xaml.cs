using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafQuickQueueSearch : UserControl
{
    readonly DispatcherTimer refreshTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    FlyleafQueue? queue;
    bool resetSelection;
    internal Func<KeyEventArgs, bool>? IsSearchShortcut;
    internal event Action? CloseRequested;
    internal event Action<object>? PlayRequested;

    public FlyleafQuickQueueSearch()
    {
        InitializeComponent();
        refreshTimer.Tick += (_, _) => RefreshResults();
    }

    internal void Attach(FlyleafQueue value)
    {
        Detach(); queue = value; queue.PropertyChanged += QueueChanged;
        resetSelection = true; RefreshResults();
    }

    internal void Detach()
    {
        refreshTimer.Stop();
        if (queue != null) queue.PropertyChanged -= QueueChanged;
        queue = null; SearchBox.Clear(); Results.ItemsSource = null;
    }

    internal void FocusSearch() { SearchBox.Focus(); Keyboard.Focus(SearchBox); SearchBox.SelectAll(); }

    void QueueChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(FlyleafQueue.Revision) or nameof(FlyleafQueue.TotalItems))) return;
        if (queue?.TotalItems <= 1) { CloseRequested?.Invoke(); return; }
        if (!refreshTimer.IsEnabled) refreshTimer.Start();
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (queue == null) return;
        resetSelection = true; refreshTimer.Stop(); refreshTimer.Start();
    }

    void RefreshResults()
    {
        refreshTimer.Stop();
        if (queue == null) return;
        var selected = resetSelection ? null : Results.SelectedItem;
        var query = SearchBox.Text.Trim();
        var matches = FlyleafQueueSearch.FindPlayable(queue, query).ToArray();
        Results.ItemsSource = matches;
        if (selected != null && matches.Contains(selected)) Results.SelectedItem = selected;
        else Results.SelectedIndex = matches.Length == 0 ? -1 : 0;
        resetSelection = false;
        if (Results.SelectedItem != null) Results.ScrollIntoView(Results.SelectedItem);
        ResultCount.Text = query.Length == 0 ? $"{matches.Length} items" : $"{matches.Length} matching items";
        NoResults.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsSearchShortcut?.Invoke(e) == true) { FocusSearch(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { CloseRequested?.Invoke(); e.Handled = true; return; }
        if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Enter)) return;
        if (refreshTimer.IsEnabled) RefreshResults();
        if (e.Key == Key.Enter) Play(Results.SelectedItem);
        else if (Results.Items.Count > 0)
        {
            int page = Math.Max(1, (int)(Results.ActualHeight / 30) - 1);
            int delta = e.Key switch { Key.Up => -1, Key.Down => 1, Key.PageUp => -page, _ => page };
            Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, Results.Items.Count - 1);
            Results.ScrollIntoView(Results.SelectedItem);
        }
        e.Handled = true;
    }

    void Results_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) is not ListBoxItem row) return;
        // A just-typed query must not activate a result from the previous search.
        if (resetSelection && refreshTimer.IsEnabled) { RefreshResults(); e.Handled = true; return; }
        Play(row.Content); e.Handled = true;
    }

    void Play(object? item)
    {
        if (item != null) PlayRequested?.Invoke(item);
    }
    void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
