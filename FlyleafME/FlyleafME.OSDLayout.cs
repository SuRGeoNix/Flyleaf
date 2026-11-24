using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    static readonly DependencyPropertyDescriptor osdOpacityProperty = DependencyPropertyDescriptor.FromProperty(OpacityProperty, typeof(FrameworkElement));
    FrameworkElement? osdHeader, osdPlaybackBar;
    bool osdHeaderVisible, osdBarVisible, osdBarsObserved, osdBarRefreshQueued;
    void InitializeOSDBarObservation()
    {
        // Resolve descriptors after all partial-class dependency properties have been initialized.
        var headerProperty = DependencyPropertyDescriptor.FromProperty(HeaderContentProperty, typeof(FlyleafME));
        var contentProperty = DependencyPropertyDescriptor.FromProperty(OverlayContentProperty, typeof(FlyleafME));
        overlayRoot.Loaded += (_, _) =>
        {
            if (osdBarsObserved) return;
            osdBarsObserved = true;
            headerProperty.AddValueChanged(this, OSDContentChanged);
            contentProperty.AddValueChanged(this, OSDContentChanged);
            ObserveOSDBars(); QueueOSDBarRefresh();
        };
        overlayRoot.Unloaded += (_, _) =>
        {
            if (!osdBarsObserved) return;
            osdBarsObserved = false;
            headerProperty.RemoveValueChanged(this, OSDContentChanged);
            contentProperty.RemoveValueChanged(this, OSDContentChanged);
            ObserveOSDBar(ref osdHeader, null); ObserveOSDBar(ref osdPlaybackBar, null);
            osdHeaderVisible = osdBarVisible = false;
        };
    }
    void OSDContentChanged(object? sender, EventArgs e) => QueueOSDBarRefresh();
    void QueueOSDBarRefresh()
    {
        if (!osdBarsObserved || osdBarRefreshQueued || disposed || Dispatcher.HasShutdownStarted) return;
        osdBarRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            osdBarRefreshQueued = false;
            if (osdBarsObserved && !disposed) ObserveOSDBars();
        }));
    }
    void ObserveOSDBars()
    {
        ObserveOSDBar(ref osdHeader, overlayHeader.Content == null ? null : overlayHeader.Content as FrameworkElement ?? overlayHeader);
        var bar = FindOSDPlaybackBar(OverlayContent as DependencyObject, new());
        if (bar == null)
        {
            // A data template may create the playback bar instead of supplying it directly.
            var presenter = overlayRoot.Children.OfType<ContentPresenter>().FirstOrDefault(p => !ReferenceEquals(p, overlayHeader));
            bar = FindOSDPlaybackBar(presenter, new());
        }
        ObserveOSDBar(ref osdPlaybackBar, bar);
        UpdateOSDMargin();
    }
    static FlyleafPlaybackBar? FindOSDPlaybackBar(DependencyObject? root, HashSet<DependencyObject> visited)
    {
        if (root == null || !visited.Add(root)) return null;
        if (root is FlyleafPlaybackBar bar) return bar;
        if (root is FlyleafOverlayLayout { BottomContent: DependencyObject bottom } && FindOSDPlaybackBar(bottom, visited) is { } bottomBar) return bottomBar;
        if (root is Visual)
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindOSDPlaybackBar(VisualTreeHelper.GetChild(root, i), visited) is { } visualBar) return visualBar;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject element && FindOSDPlaybackBar(element, visited) is { } logicalBar) return logicalBar;
        return null;
    }
    void ObserveOSDBar(ref FrameworkElement? current, FrameworkElement? next)
    {
        if (ReferenceEquals(current, next)) return;
        if (current != null)
        {
            current.SizeChanged -= OSDBarSizeChanged;
            current.IsVisibleChanged -= OSDBarVisibilityChanged;
            current.IsHitTestVisibleChanged -= OSDBarVisibilityChanged;
            current.Unloaded -= OSDBarUnloaded;
            osdOpacityProperty.RemoveValueChanged(current, OSDBarOpacityChanged);
        }
        current = next;
        if (current != null)
        {
            current.SizeChanged += OSDBarSizeChanged;
            current.IsVisibleChanged += OSDBarVisibilityChanged;
            current.IsHitTestVisibleChanged += OSDBarVisibilityChanged;
            current.Unloaded += OSDBarUnloaded;
            osdOpacityProperty.AddValueChanged(current, OSDBarOpacityChanged);
        }
    }
    void OSDBarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateOSDMargin();
    void OSDBarVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateOSDMargin();
    void OSDBarUnloaded(object sender, RoutedEventArgs e) => QueueOSDBarRefresh();
    void OSDBarOpacityChanged(object? sender, EventArgs e)
    {
        // Animated opacity changes frequently; only react to the visibility boundary.
        if (osdHeaderVisible != IsOSDBarVisible(osdHeader) || osdBarVisible != IsOSDBarVisible(osdPlaybackBar)) UpdateOSDMargin();
    }
    static bool IsOSDBarVisible(FrameworkElement? bar) => bar is { IsVisible: true, Opacity: > 0.001 };
    void UpdateOSDMargin()
    {
        bool header = IsOSDBarVisible(osdHeader), bar = IsOSDBarVisible(osdPlaybackBar);
        bool changed = osdBarVisible != bar;
        osdHeaderVisible = header; osdBarVisible = bar;
        playbackDisplay.SetInsets(new Thickness(12, (header ? overlayHeader.ActualHeight : 0) + 12, 12, (bar ? osdPlaybackBar!.ActualHeight : 0) + 12));
        if (IsQueueSearchOpen) PositionQueueSearch();
        if (changed && overlayPlayer is { IsDisposed: false }) UpdatePlaybackDisplay();
    }
}
