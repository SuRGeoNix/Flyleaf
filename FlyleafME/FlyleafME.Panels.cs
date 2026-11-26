using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    public void ShowPanel(FlyleafPanel panel, FlyleafPanelMode mode = FlyleafPanelMode.Overlay,
        FlyleafPanelSide side = FlyleafPanelSide.Right) => ShowPanelCore(panel, mode, side, restoreDockWidth: false);

    void ShowPanelCore(FlyleafPanel panel, FlyleafPanelMode mode, FlyleafPanelSide side, bool restoreDockWidth)
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(panel);
        if (!IsLoaded) throw new InvalidOperationException("Show panels after the media element is loaded.");
        if (panel.OwnerElement != null && !ReferenceEquals(panel.OwnerElement, this))
            throw new InvalidOperationException("A panel belongs to one media element.");
        if (panels.TryGetValue(panel.Id, out var existing) && !ReferenceEquals(existing, panel))
            throw new InvalidOperationException("A panel ID must identify one editor instance.");
        if (!panel.Definition.Supports(mode, side))
            throw new InvalidOperationException("This panel does not support that presentation or side.");
        var ownerWindow = Window.GetWindow(this);
        if (mode == FlyleafPanelMode.Floating && ownerWindow == null)
            throw new InvalidOperationException("A floating panel requires an owner window.");
        double otherWidth = side == FlyleafPanelSide.Left
            ? (OccupiesDock(rightDock) && rightDock.Content != panel ? rightDock.ActualWidth : 0)
            : (OccupiesDock(leftDock) && leftDock.Content != panel ? leftDock.ActualWidth : 0);
        if (panelDefinitions.TryGetValue(panel.Id, out var registered) && !ReferenceEquals(registered, panel.Definition))
            throw new InvalidOperationException("Panel ID is registered with a different definition.");
        ViewportLayoutChanging?.Invoke(this, EventArgs.Empty);
        panelDefinitions.TryAdd(panel.Id, panel.Definition);
        panels[panel.Id] = panel;
        if (panel.Context == null || panel.Context.MediaElement == null)
            panel.AttachContext(this, new FlyleafPanelContext(this, panel.Id));
        ClosePanelCore(panel, false, updateLayout: false);
        panel.Mode = mode; panel.Side = side;
        panel.RefreshPresentation();
        if (mode == FlyleafPanelMode.Overlay && overlayPanel != null) ClosePanelCore(overlayPanel, false, updateLayout: false);
        var dock = side == FlyleafPanelSide.Left ? leftDock : rightDock;
        if (mode == FlyleafPanelMode.Docked && dock.Content is FlyleafPanel occupied) ClosePanelCore(occupied, false, updateLayout: false);
        panel.IsOpen = true;
        panel.Width = double.NaN;
        panel.Height = double.NaN;
        panel.Margin = new Thickness(0);
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.VerticalAlignment = VerticalAlignment.Stretch;
        if (mode == FlyleafPanelMode.Docked)
        {
            dock.Content = panel;
            var column = dockLayout.ColumnDefinitions[side == FlyleafPanelSide.Left ? 0 : 4];
            double maxWidth = restoreDockWidth || IsFullScreen && !panel.PinnedInFullscreen ? panel.Definition.MaximumWidth
                : Math.Min(panel.Definition.MaximumWidth, Math.Max(panel.Definition.MinimumWidth, ActualWidth - otherWidth - 405));
            column.Width = new GridLength(Math.Clamp(panel.PreferredWidth, panel.Definition.MinimumWidth, maxWidth));
        }
        else if (mode == FlyleafPanelMode.Overlay)
        {
            overlayPanel = panel;
            if (panel.Definition.FillOverlayArea)
                UpdateFullAreaPanelBounds();
            else
            {
                panel.Width = Math.Min(panel.PreferredWidth, Math.Max(180, Host.ActualWidth - 24));
                panel.Margin = new Thickness(12, 54, 12, 120);
                panel.HorizontalAlignment = side == FlyleafPanelSide.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }
            overlayPanels.Children.Add(panel);
            panel.ActivityHold = FlyleafPopupActivity.Hold(Player);
        }
        else
        {
            var window = new Window
            {
                Title = panel.Title, Owner = ownerWindow, ShowInTaskbar = false,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResize,
                Width = panel.PreferredWidth, Height = panel.Definition.PreferredFloatingHeight,
                MinWidth = panel.Definition.MinimumWidth, MaxWidth = panel.Definition.MaximumWidth,
                MinHeight = panel.Definition.MinimumFloatingHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel
            };
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            window.SetResourceReference(Window.BackgroundProperty, "FlyleafME.SurfaceBrush");
            window.SetResourceReference(Window.ForegroundProperty, "FlyleafME.TextBrush");
            window.Resources.MergedDictionaries.Add(Resources);
            if (panel.FloatingBounds is Rect bounds && ValidFloatingBounds(bounds, window))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = bounds.Left; window.Top = bounds.Top; window.Width = bounds.Width; window.Height = bounds.Height;
            }
            panel.FloatingWindow = window;
            window.Closing += (_, _) =>
            {
                // Capture normal bounds before WPF tears down the HWND.
                RememberFloatingBounds(panel, window);
            };
            window.Closed += (_, _) =>
            {
                window.Content = null;
                if (ReferenceEquals(panel.FloatingWindow, window))
                {
                    panel.FloatingWindow = null; panel.IsOpen = false;
                    panel.ActivityHold?.Dispose(); panel.ActivityHold = null;
                    FocusPlayback();
                }
                UpdateHostKeyBindings();
            };
            window.Loaded += (_, _) => FloatingPanelBounds.KeepOnScreen(window);
            window.Activated += (_, _) => panel.ActivityHold ??= FlyleafPopupActivity.Hold(Player);
            window.Deactivated += (_, _) => { panel.ActivityHold?.Dispose(); panel.ActivityHold = null; };
            try { window.Show(); }
            catch
            {
                // Never leave the editor parented to an unshown or failed window.
                window.Content = null;
                panel.FloatingWindow = null;
                panel.IsOpen = false;
                panel.ActivityHold?.Dispose(); panel.ActivityHold = null;
                window.Close();
                UpdateHostKeyBindings();
                UpdateDockLayout();
                throw;
            }
        }
        UpdateDockLayout();
        if (mode == FlyleafPanelMode.Docked) FullscreenDockAt(side == FlyleafPanelSide.Left ? 0 : 4).Reveal();
        RestorePanelFocus(panel);
        UpdateHostKeyBindings();
    }

    public void ClosePanel(FlyleafPanel panel) => ClosePanelCore(panel, true);

    void UpdateFullAreaPanelBounds()
    {
        if (overlayPanel is not { Definition.FillOverlayArea: true } panel) return;
        panel.Width = double.NaN;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Margin = new Thickness(0, overlayHeader.Content != null ? overlayHeader.ActualHeight : 0, 0, 0);
    }

    void ClosePanelCore(FlyleafPanel panel, bool restoreFocus, bool updateLayout = true)
    {
        Dispatcher.VerifyAccess();
        if (panel.OwnerElement != this) return;
        if (updateLayout && panel.IsOpen) ViewportLayoutChanging?.Invoke(this, EventArgs.Empty);
        if (ReferenceEquals(overlayPanel, panel))
        {
            overlayPanels.Children.Remove(panel); overlayPanel = null;
        }
        RememberDockWidth(leftDock, 0, panel);
        RememberDockWidth(rightDock, 4, panel);
        if (panel.FloatingWindow is Window window)
        {
            RememberFloatingBounds(panel, window);
            panel.FloatingWindow = null;
            window.Content = null;
            window.Close();
        }
        panel.ActivityHold?.Dispose(); panel.ActivityHold = null;
        panel.IsOpen = false;
        if (restoreFocus) FocusPlayback();
        UpdateHostKeyBindings();
        if (updateLayout) UpdateDockLayout();
    }

    void RememberDockWidth(ContentControl dock, int columnIndex, FlyleafPanel panel)
    {
        if (!ReferenceEquals(dock.Content, panel)) return;
        // Layout can temporarily hide the dock or constrain the overlaid panel.
        // Preserve the requested dock width in either case.
        var fullscreenDock = FullscreenDockAt(columnIndex);
        double width = fullscreenDock.HostsDock || dock.Visibility == Visibility.Collapsed || dock.ActualWidth <= 0
            ? RequestedDockWidthAt(columnIndex) : dock.ActualWidth;
        panel.PreferredWidth = Math.Clamp(width, panel.Definition.MinimumWidth, panel.Definition.MaximumWidth);
        fullscreenDock.Hide();
        dock.Content = null;
    }

    static bool ValidFloatingBounds(Rect bounds, Window window) => !bounds.IsEmpty &&
        FloatingPanelBounds.IsValid(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.MinWidth, window.MinHeight);

    static void RememberFloatingBounds(FlyleafPanel panel, Window window)
    {
        if (window.IsLoaded && ValidFloatingBounds(window.RestoreBounds, window))
            panel.FloatingBounds = window.RestoreBounds;
    }

    void CloseAllPanels()
    {
        if (panels.Values.Any(panel => panel.IsOpen)) ViewportLayoutChanging?.Invoke(this, EventArgs.Empty);
        foreach (var panel in panels.Values.ToArray()) ClosePanelCore(panel, false, updateLayout: false);
        UpdateDockLayout();
    }

    bool viewportNotificationQueued;

    void UpdateDockLayout()
    {
        if (dockLayout.ColumnDefinitions.Count != 5) return;
        var columns = dockLayout.ColumnDefinitions;
        bool fullArea = overlayPanel?.Definition.FillOverlayArea == true;
        playbackDisplay.SetObscured(overlayPanel != null);
        leftFullscreenDock.UpdateHost(IsFullScreen, fullArea);
        rightFullscreenDock.UpdateHost(IsFullScreen, fullArea);
        if (leftDock.Content == null) { columns[0].MinWidth = 0; columns[0].Width = new GridLength(0); }
        if (rightDock.Content == null) { columns[4].MinWidth = 0; columns[4].Width = new GridLength(0); }
        if (!leftFullscreenDock.HostsDock) leftDock.Visibility = fullArea ? Visibility.Collapsed : Visibility.Visible;
        if (!rightFullscreenDock.HostsDock) rightDock.Visibility = fullArea ? Visibility.Collapsed : Visibility.Visible;
        bool leftVisible = !fullArea && OccupiesDock(leftDock), rightVisible = !fullArea && OccupiesDock(rightDock);
        columns[1].Width = new GridLength(leftVisible ? leftSplitter.Width : 0);
        columns[3].Width = new GridLength(rightVisible ? rightSplitter.Width : 0);
        leftSplitter.Visibility = leftVisible ? Visibility.Visible : Visibility.Collapsed;
        rightSplitter.Visibility = rightVisible ? Visibility.Visible : Visibility.Collapsed;
        PlaceHeader();
        UpdateDockLimits();
        if (!viewportNotificationQueued)
        {
            viewportNotificationQueued = true;
            // Consumers remember viewport geometry after WPF layout, rather than
            // while those dimensions still describe the previous presentation.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                viewportNotificationQueued = false;
                if (!disposed && IsLoaded) ViewportLayoutChanged?.Invoke(this, EventArgs.Empty);
            }));
        }
    }

    void UpdateDockLimits(double? availableWidth = null)
    {
        if (dockLayout.ColumnDefinitions.Count != 5) return;
        // Keep the content usable at narrow sizes; do not silently relocate panels.
        var left = OccupiesDock(leftDock) ? (FlyleafPanel)leftDock.Content : null;
        var right = OccupiesDock(rightDock) ? (FlyleafPanel)rightDock.Content : null;
        double budget = Math.Max(0, (availableWidth ?? ActualWidth) - 400 - (left == null ? 0 : leftSplitter.Width) - (right == null ? 0 : rightSplitter.Width));
        var fitted = FlyleafDockWidths.Fit(budget, left == null ? 0 : RequestedDockWidthAt(0), left?.Definition.MinimumWidth ?? 0,
            right == null ? 0 : RequestedDockWidthAt(4), right?.Definition.MinimumWidth ?? 0);
        foreach (int index in new[] { 0, 4 })
        {
            var column = dockLayout.ColumnDefinitions[index];
            if (HasFullAreaPanel || FullscreenDockAt(index).HostsDock)
            {
                column.MinWidth = 0;
                column.MaxWidth = 0;
                continue;
            }
            if ((index == 0 ? left : right) is not FlyleafPanel panel)
            { if (!double.IsPositiveInfinity(column.MaxWidth)) column.MaxWidth = double.PositiveInfinity; continue; }
            double allocated = index == 0 ? fitted.Left : fitted.Right;
            double otherAllocated = index == 0 ? fitted.Right : fitted.Left;
            // Fit oversized requests without turning the current width into the
            // splitter's growth limit. Only one splitter changes a width at once.
            double maximum = Math.Min(panel.Definition.MaximumWidth, Math.Max(allocated, budget - otherAllocated));
            double minimum = Math.Min(panel.Definition.MinimumWidth, allocated);
            if (column.MinWidth != minimum) column.MinWidth = minimum;
            if (column.MaxWidth != maximum) column.MaxWidth = maximum;
        }
    }

    void RefreshPanelThemes()
    {
        // The floating window merges this element's resources, preserving per-player appearance.
        foreach (var panel in panels.Values) panel.InvalidateVisual();
    }
}
