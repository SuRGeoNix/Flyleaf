using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FlyleafLib.Controls;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    sealed class ComposedHost(FlyleafME owner) : FlyleafHost
    {
        // Direct host swaps bypass the owner's player, queue and UI lifecycle.
        protected override bool SupportsPlayerSwap => false;

        public override void SetPlayer(Player oldPlayer)
        {
            base.SetPlayer(oldPlayer);
            if (Player != null) Player.Host = owner;
        }
    }

    public FlyleafHost Host { get; private set; } = null!;
    readonly Grid overlayRoot = new();
    readonly FlyleafDebugOverlay debugOverlay;
    readonly FlyleafStatisticsOverlay statisticsOverlay = new();
    readonly Canvas statisticsLayer = new() { ClipToBounds = true, IsHitTestVisible = false };
    readonly Grid statisticsViewport = new() { ClipToBounds = true };
    public FlyleafMediaInformation Information { get; } = new();
    public FlyleafQueue Queue { get; } = new();
    readonly ContentPresenter overlayHeader = new();
    readonly Grid overlayPanels = new();
    readonly Grid dockLayout = new();
    readonly ContentControl leftDock = new(), rightDock = new();
    readonly GridSplitter leftSplitter = new() { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly GridSplitter rightSplitter = new() { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Dictionary<string, FlyleafPanel> panels = new();
    FlyleafPanel? overlayPanel;
    Window? inputSurface, inputOverlay;
    bool disposed;
    public double DockWidth => dockLayout.ColumnDefinitions.Count == 5
        ? dockLayout.ColumnDefinitions[0].ActualWidth + dockLayout.ColumnDefinitions[1].ActualWidth +
          dockLayout.ColumnDefinitions[3].ActualWidth + dockLayout.ColumnDefinitions[4].ActualWidth : 0;
    public double MinimumComposedWidth => 400 +
        (leftDock.Content is FlyleafPanel left ? left.Definition.MinimumWidth + 5 : 0) +
        (rightDock.Content is FlyleafPanel right ? right.Definition.MinimumWidth + 5 : 0);
    /// <summary>Dock width to reserve when the application explicitly resizes the window. Excludes temporarily hidden docks.</summary>
    public double RequestedDockWidth => overlayPanel?.Definition.FillOverlayArea == true ? 0 :
        RequestedDockWidthAt(0) + RequestedDockWidthAt(4) + (leftDock.Content != null ? 5 : 0) + (rightDock.Content != null ? 5 : 0);
    public double RequestedLeftDockWidth => HasFullAreaPanel || leftDock.Content == null ? 0 : RequestedDockWidthAt(0) + 5;
    public double RequestedRightDockWidth => HasFullAreaPanel || rightDock.Content == null ? 0 : RequestedDockWidthAt(4) + 5;
    /// <summary>Requested visible dock and splitter widths in device pixels for window sizing.</summary>
    public (int Left, int Right) GetRequestedDockPixelInsets(double dpiScaleX)
    {
        if (HasFullAreaPanel) return (0, 0);
        // WPF rounds each fixed grid column independently.
        int Pixels(double width) => (int)Math.Round(width * dpiScaleX);
        return (leftDock.Content == null ? 0 : Pixels(RequestedDockWidthAt(0)) + Pixels(leftSplitter.Width),
            rightDock.Content == null ? 0 : Pixels(RequestedDockWidthAt(4)) + Pixels(rightSplitter.Width));
    }

    public bool HasFullAreaPanel => overlayPanel?.Definition.FillOverlayArea == true;
    double RequestedDockWidthAt(int index)
    {
        if ((index == 0 ? leftDock.Content : rightDock.Content) is not FlyleafPanel panel) return 0;
        var width = dockLayout.ColumnDefinitions[index].Width;
        return Math.Clamp(width.IsAbsolute ? width.Value : panel.PreferredWidth, panel.Definition.MinimumWidth, panel.Definition.MaximumWidth);
    }
    /// <summary>The shared header overlays playback, so it reserves no layout height.</summary>
    public double HeaderHeight => 0;
    public bool HasDockedPanels => leftDock.Content != null || rightDock.Content != null;
    public event EventHandler? ViewportLayoutChanged;
    /// <summary>Capture viewport geometry before a panel presentation or splitter change.</summary>
    public event EventHandler? ViewportLayoutChanging;
    public event EventHandler? FullScreenChanged;
    public event EventHandler? RatioChanged;
    public double CurResizeRatio => Host.CurResizeRatio;

    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent), typeof(object), typeof(FlyleafME), new PropertyMetadata(null, (d, _) => ((FlyleafME)d).PlaceHeader()));
    public object? HeaderContent { get => GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }

    public static readonly DependencyProperty PlayerProperty = DependencyProperty.Register(
        nameof(Player), typeof(Player), typeof(FlyleafME), new PropertyMetadata(null, (d, e) =>
        {
            var view = (FlyleafME)d;
            Interlocked.Increment(ref view.menuActionGeneration);
            if (e.OldValue is Player oldPlayer)
            {
                oldPlayer.PropertyChanged -= view.ComposedPlayer_PropertyChanged;
                oldPlayer.Activity.ForceFullActive();
            }
            view.CloseQueueSearch();
            view.CloseAllPanels();
            view.CloseGeneralMenu();
            view.Host.SetCurrentValue(FlyleafHost.PlayerProperty, e.NewValue);
            view.Information.SetPlayer(e.NewValue as Player);
            view.Queue.SetPlayer(e.NewValue as Player);
            if (e.NewValue is Player newPlayer)
            {
                view.RegisterControlKeybindings(newPlayer);
                newPlayer.PropertyChanged += view.ComposedPlayer_PropertyChanged;
            }
            view.RefreshPanelContexts();
            if (view.IsLoaded) view.ObserveTextPlayer(e.NewValue as Player);
        }));
    public Player? Player { get => (Player?)GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }

    void ComposedPlayer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Player.CanPlay) or nameof(Player.Status))) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(sender, Player) && Player?.CanPlay == false && Player.Status != Status.Opening)
                Player.Activity.ForceFullActive();
        });
    }

    static DependencyProperty Forward<T>(string name, T defaultValue, DependencyProperty target) =>
        DependencyProperty.Register(name, typeof(T), typeof(FlyleafME), new PropertyMetadata(defaultValue,
            (d, e) => ((FlyleafME)d).Host.SetCurrentValue(target, e.NewValue)));
    public static readonly DependencyProperty ActivityTimeoutProperty = Forward(nameof(ActivityTimeout), 2800, FlyleafHost.ActivityTimeoutProperty);
    public int ActivityTimeout { get => (int)GetValue(ActivityTimeoutProperty); set => SetValue(ActivityTimeoutProperty, value); }
    public static readonly DependencyProperty AttachedDragMoveProperty = Forward(nameof(AttachedDragMove), AttachedDragMoveOptions.BothOwner, FlyleafHost.AttachedDragMoveProperty);
    public AttachedDragMoveOptions AttachedDragMove { get => (AttachedDragMoveOptions)GetValue(AttachedDragMoveProperty); set => SetValue(AttachedDragMoveProperty, value); }
    public static readonly DependencyProperty AttachedResizeProperty = Forward(nameof(AttachedResize), AvailableWindows.None, FlyleafHost.AttachedResizeProperty);
    public AvailableWindows AttachedResize { get => (AvailableWindows)GetValue(AttachedResizeProperty); set => SetValue(AttachedResizeProperty, value); }
    public static readonly DependencyProperty KeyBindingsProperty = DependencyProperty.Register(
        nameof(KeyBindings), typeof(AvailableWindows), typeof(FlyleafME),
        new PropertyMetadata(AvailableWindows.Both, (d, _) => ((FlyleafME)d).UpdateHostKeyBindings()));
    public AvailableWindows KeyBindings { get => (AvailableWindows)GetValue(KeyBindingsProperty); set => SetValue(KeyBindingsProperty, value); }
    // Compatibility for the existing attached-mode sample. Detached hosting is not part of this prototype.
    public bool IsAttached { get => true; set { if (!value) throw new NotSupportedException("The composed prototype supports attached hosting only."); } }
    public bool KeepRatioOnResize { get => false; set { if (value) throw new NotSupportedException("Use the application's window ratio policy for the composed control."); } }
    public AvailableWindows ToggleFullScreenOnDoubleClick { get; set; } = AvailableWindows.Both;
    public static readonly DependencyProperty IsFullScreenProperty = DependencyProperty.Register(
        nameof(IsFullScreen), typeof(bool), typeof(FlyleafME), new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) =>
            {
                var view = (FlyleafME)d;
                view.Host.SetCurrentValue(FlyleafHost.AttachedDragMoveProperty,
                    view.IsFullScreen ? AttachedDragMoveOptions.None : view.AttachedDragMove);
                view.Player?.Activity.RefreshFullActive();
                view.FullScreenChanged?.Invoke(view, EventArgs.Empty);
                view.ShowOSD(view.IsFullScreen ? "Full screen" : "Windowed");
            }));
    public bool IsFullScreen { get => (bool)GetValue(IsFullScreenProperty); set => SetValue(IsFullScreenProperty, value); }

    void InitializeComposition()
    {
        Host = new ComposedHost(this)
        {
            IsAttached = true, KeepRatioOnResize = false, AttachedDragMove = AttachedDragMove,
            AttachedResize = AttachedResize, KeyBindings = KeyBindings, ActivityTimeout = ActivityTimeout,
            ToggleFullScreenOnDoubleClick = AvailableWindows.None, SwapOnDrop = AvailableWindows.None
        };
        Host.Resources = Resources;
        Host.Loaded += (_, _) => AttachHostInput();
        Host.RatioChanged += (_, _) => RatioChanged?.Invoke(this, EventArgs.Empty);
        Host.Content = overlayRoot;
        Children.Add(dockLayout);
        foreach (var width in new[] { new GridLength(0), new GridLength(0), new GridLength(1, GridUnitType.Star), new GridLength(0), new GridLength(0) })
            dockLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        dockLayout.Children.Add(leftDock);
        SetColumn(leftSplitter, 1); dockLayout.Children.Add(leftSplitter);
        SetColumn(Host, 2); dockLayout.Children.Add(Host);
        SetColumn(rightSplitter, 3); dockLayout.Children.Add(rightSplitter);
        SetColumn(rightDock, 4); dockLayout.Children.Add(rightDock);
        dockLayout.ColumnDefinitions[2].MinWidth = 400;
        foreach (var splitter in new[] { leftSplitter, rightSplitter })
        {
            splitter.SetResourceReference(StyleProperty, "FlyleafME.DockSplitterStyle");
            splitter.ToolTip = "Drag to resize panel";
            System.Windows.Automation.AutomationProperties.SetName(splitter, ReferenceEquals(splitter, leftSplitter) ? "Resize left panel" : "Resize right panel");
        }
        leftSplitter.DragCompleted += (_, _) => ViewportLayoutChanged?.Invoke(this, EventArgs.Empty);
        rightSplitter.DragCompleted += (_, _) => ViewportLayoutChanged?.Invoke(this, EventArgs.Empty);
        leftSplitter.DragStarted += (_, _) => ViewportLayoutChanging?.Invoke(this, EventArgs.Empty);
        rightSplitter.DragStarted += (_, _) => ViewportLayoutChanging?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Resolve dock constraints before Grid measures/arranges the video host.
        // SizeChanged is too late: changing columns there causes another layout
        // pass and resizes the native surface against intermediate dimensions.
        UpdateDockLimits(double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth);
        return base.MeasureOverride(availableSize);
    }

    void FinishOverlayComposition()
    {
        statisticsOverlay.Source = Information;
        InitializeTextOverlays();
        statisticsOverlay.VerticalAlignment = VerticalAlignment.Center;
        statisticsOverlay.Margin = new Thickness(8);
        statisticsViewport.Children.Add(statisticsOverlay);
        statisticsLayer.Children.Add(statisticsViewport);
        SetZIndex(statisticsLayer, -1); overlayRoot.Children.Add(statisticsLayer);
        SetZIndex(debugOverlay, 5); overlayRoot.Children.Add(debugOverlay);
        overlayHeader.SizeChanged += (_, _) =>
        {
            UpdateFullAreaPanelBounds();
            UpdateOSDMargin();
        };
        overlayHeader.VerticalAlignment = VerticalAlignment.Top;
        SetZIndex(overlayHeader, 10); overlayRoot.Children.Add(overlayHeader);
        SetZIndex(overlayPanels, 20); overlayRoot.Children.Add(overlayPanels);
        overlayRoot.PreviewKeyDown += PanelEscape;
        PreviewKeyDown += PanelEscape;
        PlaceHeader();
    }

    void PanelEscape(object sender, KeyEventArgs e)
    {
        if (IsQueueSearchOpen) return;
        if (e.OriginalSource is TextBox { Tag: "FlyleafKeyCapture" }) return;
        if (e.OriginalSource is TextBox { Tag: FlyleafLanguageEntry { IsSuggestionOpen: true } }) return;
        if (e.Key == Key.Escape && overlayPanel != null) { ClosePanel(overlayPanel); e.Handled = true; }
    }

    bool HandleDebugOverlayKey(object sender, KeyEventArgs e, ModifierKeys modifiers)
    {
        if (e.Handled || e.Key != Key.Tab || IsQueueSearchOpen || overlayPanel?.Definition.FillOverlayArea == true ||
            KeyBindings == AvailableWindows.None ||
            KeyBindings == AvailableWindows.Surface && !ReferenceEquals(sender, inputSurface) ||
            KeyBindings == AvailableWindows.Overlay && !ReferenceEquals(sender, inputOverlay) ||
            !IsDebugShortcutTarget(sender, e.OriginalSource))
            return false;

        bool cycle = modifiers == ModifierKeys.Control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        if (cycle ? !Information.ShowDebugOverlay : modifiers != ModifierKeys.None)
            return false;

        if (!e.IsRepeat)
        {
            if (cycle) debugOverlay.CycleTab((modifiers & ModifierKeys.Shift) != 0);
            else
            {
                bool hadFocus = debugOverlay.IsKeyboardFocusWithin;
                Information.ShowDebugOverlay = !Information.ShowDebugOverlay;
                if (hadFocus && !Information.ShowDebugOverlay) FocusPlayback();
            }
        }
        return e.Handled = true;
    }

    bool IsDebugShortcutTarget(object sender, object original)
    {
        if (ReferenceEquals(original, sender) || ReferenceEquals(original, overlayRoot)) return true;
        for (var item = original as DependencyObject; item != null; item = item is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
        {
            if (item is TextBoxBase or PasswordBox or ComboBox or MenuBase or FlyleafPanel) return false;
            if (item is FlyleafPlaybackBar || ReferenceEquals(item, debugOverlay)) return true;
        }
        return false;
    }

    internal void UpdateHostKeyBindings()
    {
        // Like v1's settings dialog: suppress host shortcuts while an editor owns focus.
        // The configured preference stays intact, including bindings supplied by the app.
        bool editing = IsQueueSearchOpen || panels.Values.Any(panel => panel.IsOpen && panel.IsKeyboardFocusWithin);
        Host.SetCurrentValue(FlyleafHost.KeyBindingsProperty, editing ? AvailableWindows.None : KeyBindings);
    }

    void PlaceHeader()
    {
        if (ReferenceEquals(overlayHeader.Content, HeaderContent)) return;
        overlayHeader.Content = HeaderContent;
        UpdateFullAreaPanelBounds();
        UpdateOSDMargin();
    }

    void AttachHostInput()
    {
        DetachHostInput();
        inputSurface = Host.Surface; inputOverlay = Host.Overlay;
        if (inputSurface != null) { inputSurface.MouseDoubleClick += HostDoubleClick; inputSurface.MouseRightButtonUp += HostRightClick; inputSurface.PreviewKeyDown += HostMenuKeyDown; }
        if (inputOverlay != null) { inputOverlay.MouseDoubleClick += HostDoubleClick; inputOverlay.MouseRightButtonUp += HostRightClick; inputOverlay.PreviewKeyDown += HostMenuKeyDown; }
    }
    void DetachHostInput()
    {
        CloseQueueSearch();
        CloseGeneralMenu();
        if (inputSurface != null) { inputSurface.MouseDoubleClick -= HostDoubleClick; inputSurface.MouseRightButtonUp -= HostRightClick; inputSurface.PreviewKeyDown -= HostMenuKeyDown; }
        if (inputOverlay != null) { inputOverlay.MouseDoubleClick -= HostDoubleClick; inputOverlay.MouseRightButtonUp -= HostRightClick; inputOverlay.PreviewKeyDown -= HostMenuKeyDown; }
        inputSurface = inputOverlay = null;
    }
    void HostDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsQueueSearchOpen || e.ChangedButton != MouseButton.Left || e.Handled) return;
        // Interactive controls, title bar, and tool panels handle their own input.
        if (e.OriginalSource is DependencyObject source)
            for (DependencyObject? item = source; item != null; item = item is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
                if (item is System.Windows.Controls.Primitives.ButtonBase or Slider or FlyleafPanel ||
                    ReferenceEquals(item, overlayHeader) || ReferenceEquals(item, debugOverlay)) return;
        bool enabled = ToggleFullScreenOnDoubleClick == AvailableWindows.Both ||
            (ReferenceEquals(sender, inputSurface) && ToggleFullScreenOnDoubleClick == AvailableWindows.Surface) ||
            (ReferenceEquals(sender, inputOverlay) && ToggleFullScreenOnDoubleClick == AvailableWindows.Overlay);
        if (enabled) { SetCurrentValue(IsFullScreenProperty, !IsFullScreen); e.Handled = true; }
    }

    public void Player_SetFullScreen(bool value) => Dispatcher.Invoke(() => SetCurrentValue(IsFullScreenProperty, value));
    public bool Player_GetFullScreen() => IsFullScreen;
    // In composed fullscreen the main owner can be active while its native playback children are not.
    public bool Player_CanHideCursor() => Player?.CanPlay == true && (Host.Player_CanHideCursor() || Window.GetWindow(this)?.IsActive == true);
    public bool Player_HandlesRatioResize(int width, int height) => false;
    public void Player_RatioChanged(double ratio) => Host.Player_RatioChanged(ratio);
    public void Player_Disposed() => Dispatcher.Invoke(() => SetCurrentValue(PlayerProperty, null));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ObserveTextPlayer(null);
        Interlocked.Increment(ref menuActionGeneration);
        if (Player != null) Player.PropertyChanged -= ComposedPlayer_PropertyChanged;
        CloseAllPanels();
        DetachHostInput();
        ObserveConfig(null);
        foreach (var id in panelDefinitions.Keys.ToArray()) UnregisterPanel(id);
        panels.Clear();
        Information.Dispose();
        Queue.Dispose();
        Host.Dispose();
    }
}
