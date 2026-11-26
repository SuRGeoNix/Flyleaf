using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    readonly Grid fullscreenDockLayer = new() { ClipToBounds = true };
    FullscreenDock leftFullscreenDock = null!, rightFullscreenDock = null!;

    FullscreenDock FullscreenDockAt(int column) => column == 0 ? leftFullscreenDock : rightFullscreenDock;
    bool OccupiesDock(ContentControl dock) => dock.Content != null && ReferenceEquals(dock.Parent, dockLayout);

    internal void SetPanelFullscreenPin(FlyleafPanel panel, bool pinned)
    {
        if (panel is not { IsOpen: true, Mode: FlyleafPanelMode.Docked } || panel.PinnedInFullscreen == pinned) return;
        panel.PinnedInFullscreen = pinned;
        panel.RefreshPresentation();
        if (!IsFullScreen) return;

        UpdateDockLayout();
        // Let the user finish interacting with the panel after unpinning.
        if (!pinned) FullscreenDockAt(panel.Side == FlyleafPanelSide.Left ? 0 : 4).Reveal();
    }

    void InitializeFullscreenDocks()
    {
        leftFullscreenDock = new(this, leftDock, 0);
        rightFullscreenDock = new(this, rightDock, 4);
        fullscreenDockLayer.Children.Add(leftFullscreenDock);
        fullscreenDockLayer.Children.Add(rightFullscreenDock);
        fullscreenDockLayer.SizeChanged += (_, _) =>
        {
            leftFullscreenDock.UpdateWidth();
            rightFullscreenDock.UpdateWidth();
        };
        SetZIndex(fullscreenDockLayer, 15);
        overlayRoot.Children.Add(fullscreenDockLayer);
    }

    void UpdateFullscreenDockBounds() => fullscreenDockLayer.Margin =
        new Thickness(0, overlayHeader.Content != null ? overlayHeader.ActualHeight : 0, 0, 0);

    sealed class FullscreenDock : Border
    {
        const double EdgeWidth = 6;
        readonly FlyleafME owner;
        readonly ContentControl dock;
        readonly int column;
        readonly DispatcherTimer hideDelay;

        public bool HostsDock => ReferenceEquals(Child, dock);
        bool IsRevealed => HostsDock && dock.Visibility == Visibility.Visible;

        public FullscreenDock(FlyleafME owner, ContentControl dock, int column)
        {
            this.owner = owner;
            this.dock = dock;
            this.column = column;
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            // The layered playback window skips alpha-zero pixels in native hit testing.
            var edgeBackground = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            edgeBackground.Freeze();
            Background = edgeBackground;
            Width = EdgeWidth;
            Visibility = Visibility.Collapsed;

            hideDelay = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
            hideDelay.Tick += (_, _) =>
            {
                hideDelay.Stop();
                if (!IsMouseOver && !IsKeyboardFocusWithin && !Contains(Mouse.Captured as DependencyObject) &&
                    !Contains(Keyboard.FocusedElement as DependencyObject)) Hide();
            };
            MouseEnter += (_, _) => { hideDelay.Stop(); Reveal(); };
            MouseLeave += (_, _) => ScheduleHide();
            Unloaded += (_, _) => Hide();
        }

        public void UpdateHost(bool fullscreen, bool fullArea)
        {
            bool useOverlay = fullscreen && dock.Content is FlyleafPanel { PinnedInFullscreen: false };
            if (useOverlay != HostsDock)
            {
                if (useOverlay)
                {
                    dock.Visibility = Visibility.Collapsed;
                    owner.dockLayout.Children.Remove(dock);
                    Child = dock;
                }
                else
                {
                    Hide();
                    Child = null;
                    owner.dockLayout.Children.Add(dock);
                }
            }

            if (fullArea) Hide();
            Visibility = useOverlay && !fullArea ? Visibility.Visible : Visibility.Collapsed;
            UpdateWidth();
        }

        public void Reveal()
        {
            if (Visibility != Visibility.Visible || !HostsDock || IsRevealed) return;
            owner.FullscreenDockAt(column == 0 ? 4 : 0).Hide();
            dock.Visibility = Visibility.Visible;
            UpdateWidth();
            InputManager.Current.PostProcessInput += ObserveInteraction;
            ScheduleHide();
        }

        public bool Hide()
        {
            if (!IsRevealed) return false;
            hideDelay.Stop();
            InputManager.Current.PostProcessInput -= ObserveInteraction;
            dock.Visibility = Visibility.Collapsed;
            UpdateWidth();
            return true;
        }

        public void UpdateWidth() => Width = IsRevealed
            ? Math.Min(owner.RequestedDockWidthAt(column), Math.Max(EdgeWidth, owner.fullscreenDockLayer.ActualWidth - EdgeWidth))
            : EdgeWidth;

        void ScheduleHide()
        {
            hideDelay.Stop();
            if (IsRevealed && !IsMouseOver) hideDelay.Start();
        }

        void ObserveInteraction(object sender, ProcessInputEventArgs e)
        {
            // Menus can move focus and mouse capture into a separate WPF window.
            var input = e.StagingItem.Input;
            if (input.RoutedEvent == Mouse.PreviewMouseDownEvent && !IsMouseOver && !Contains(Mouse.DirectlyOver as DependencyObject))
                Hide();
            else if (input.RoutedEvent == Mouse.LostMouseCaptureEvent || input.RoutedEvent == Keyboard.LostKeyboardFocusEvent)
                ScheduleHide();
        }

        bool Contains(DependencyObject? element)
        {
            while (element != null)
            {
                if (element == this) return true;
                element = element switch
                {
                    ContextMenu menu => menu.PlacementTarget,
                    Popup popup => popup.PlacementTarget,
                    FrameworkElement { Parent: { } parent } => parent,
                    FrameworkContentElement content => content.Parent,
                    Visual or Visual3D => VisualTreeHelper.GetParent(element),
                    _ => null
                };
            }
            return false;
        }
    }
}
