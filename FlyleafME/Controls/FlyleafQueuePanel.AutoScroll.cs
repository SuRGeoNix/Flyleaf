using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafQueuePanel
{
    FlyleafQueue? scrollQueue;
    object? lastScrolledItem;
    bool scrollScheduled;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        DataContextChanged += (_, _) => ObserveScrollQueue();
        Loaded += (_, _) => ObserveScrollQueue();
        Unloaded += (_, _) => UnobserveScrollQueue();
        IsVisibleChanged += (_, _) => ScheduleCurrentScroll(true);
    }

    void ObserveScrollQueue()
    {
        var target = DataContext as FlyleafQueue;
        if (ReferenceEquals(scrollQueue, target)) return;
        UnobserveScrollQueue();
        scrollQueue = target;
        if (scrollQueue != null) scrollQueue.PropertyChanged += ScrollQueueChanged;
        ScheduleCurrentScroll(true);
    }

    void UnobserveScrollQueue()
    {
        if (scrollQueue != null) scrollQueue.PropertyChanged -= ScrollQueueChanged;
        scrollQueue = null;
        lastScrolledItem = null;
    }

    void ScrollQueueChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FlyleafQueue.CurrentItemNumber)) ScheduleCurrentScroll();
    }

    void ScheduleCurrentScroll(bool force = false)
    {
        if (force) lastScrolledItem = null;
        if (IsSearching || !IsVisible || scrollScheduled) return;
        scrollScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            scrollScheduled = false;
            if (IsSearching || !IsVisible || scrollQueue == null) return;

            object? current = null;
            foreach (var source in scrollQueue.Sources)
            {
                var child = source.Children.FirstOrDefault(item => item.IsCurrent);
                if (child != null) current = Items.Items.Contains(child) ? child : source;
                else if (source.IsCurrent) current = source;
                if (current != null) break;
            }

            if (current == null || ReferenceEquals(current, lastScrolledItem) || !Items.Items.Contains(current)) return;
            Items.ScrollIntoView(current);
            lastScrolledItem = current;
        }));
    }
}
