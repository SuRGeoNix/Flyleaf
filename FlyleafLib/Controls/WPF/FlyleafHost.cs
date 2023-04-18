using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;

using FlyleafLib.MediaPlayer;

using static FlyleafLib.Utils;
using static FlyleafLib.Utils.NativeMethods;

using Brushes = System.Windows.Media.Brushes;

namespace FlyleafLib.Controls.WPF;

public class FlyleafHost : ContentControl, IHostPlayer, IDisposable
{
    /* -= FlyleafHost Properties Notes =-

        Player							[Can be changed, can be null]

        Surface							[ReadOnly / Required]
        Overlay							[AutoCreated OnContentChanged | Provided directly | Provided in Stand Alone Constructor]

        Content							[Overlay's Content]
        DetachedContent					[Host's actual content]

        DataContext						[Set by the user or default inheritance]
        HostDataContext					[Will be set Sync with DataContext as helper to Overlay when we pass this as Overlay's DataContext]

        OpenOnDrop						[None, Surface, Overlay, Both]		| Requires Player and AllowDrop
        SwapOnDrop						[None, Surface, Overlay, Both]		| Requires Player and AllowDrop

        SwapDragEnterOnShift			[None, Surface, Overlay, Both]		| Requires Player and SwapOnDrop
        ToggleFullScreenOnDoubleClick	[None, Surface, Overlay, Both]

        PanMoveOnCtrl					[None, Surface, Overlay, Both]		| Requires Player and VideoStream Opened
        PanZoomOnCtrlWheel			    [None, Surface, Overlay, Both]		| Requires Player and VideoStream Opened
        PanRotateOnShiftWheel           [None, Surface, Overlay, Both]      | Requires Player and VideoStream Opened

        AttachedDragMove				[None, Surface, Overlay, Both, SurfaceOwner, OverlayOwner, BothOwner]
        DetachedDragMove				[None, Surface, Overlay, Both]

        AttachedResize					[None, Surface, Overlay, Both]
        DetachedResize					[None, Surface, Overlay, Both]
        KeepRatioOnResize				[False, True]
        CurResizeRatio                  [0 if not Keep Ratio or Player's aspect ratio]
        ResizeSensitivity               Pixels sensitivity from the window's edges

        DetachedPosition				[Custom, TopLeft, TopCenter, TopRight, CenterLeft, CenterCenter, CenterRight, BottomLeft, BottomCenter, BottomRight]
        DetachedPositionMargin			[X, Y, CX, CY]						| Does not affect the Size / Eg. No point to provide both X/CX 	
        DetachedFixedPosition			[X, Y]								| if remember only first time
        DetachedFixedSize				[CX, CY]							| if remember only first time
        DetachedRememberPosition		[False, True]
        DetachedRememberSize			[False, True]
        DetachedTopMost					[False, True] (Surfaces Only Required?)
        DetachedShowInTaskbar           [False, True]                       | When Detached or Fullscreen will be in Switch Apps
        DetachedShowInTaskbarNoOwner    [False, True]                       | When Detached or Fullscreen will be in Switch Apps and will be minimized/maximized separate from Owner

        KeyBindings						[None, Surface, Overlay, Both]
        MouseBindings                   [None, Surface, Overlay, Both]      | Required for all other mouse events

        ActivityTimeout					[0: Disabled]						| Requires Player?
        ActivityRefresh?				[None, Surface, Overlay, Both]		| MouseMove / MouseDown / KeyUp

        PassWheelToOwner?				[None, Surface, Overlay, Both]		| When host belongs to ScrollViewer

        IsAttached                      [False, True]
        IsFullScreen                    [False, True]                       | Should be used instead of WindowStates
        IsMinimized                     [False, True]                       | Should be used instead of WindowStates
        IsResizing						[ReadOnly]
        IsSwapping						[ReadOnly]
        IsStandAlone					[ReadOnly]
     */

    /* TODO
     * 1) The surface / overlay events code is repeated
     * 2) PassWheelToOwner (Related with LayoutUpdate performance / ScrollViewer) / ActivityRefresh
     * 3) Attach to different Owner (Load/Unload) and change Overlay?
     * 4) WindowStates should not be used by user directly. Use IsMinimized and IsFullScreen instead.
     */

    #region Properties / Variables
    public Window       Owner           { get; private set; }
    public Window       Surface         { get; private set; }
    public IntPtr       SurfaceHandle   { get; private set; }
    public IntPtr       OverlayHandle   { get; private set; }
    public IntPtr       OwnerHandle     { get; private set; }
    public int          ResizingSide    { get; private set; }

    public int          UniqueId        { get; private set; }
    public bool         Disposed        { get; private set; }

    static bool isDesginMode;
    static int  idGenerator;
    static nint NONE_STYLE = (nint) (WindowStyles.WS_MINIMIZEBOX | WindowStyles.WS_CLIPSIBLINGS | WindowStyles.WS_CLIPCHILDREN | WindowStyles.WS_VISIBLE); // WS_MINIMIZEBOX required for swapchain
    static Rect rectRandom = new(1, 2, 3, 4);

    bool surfaceClosed, surfaceClosing, overlayClosed;
    int panPrevX, panPrevY;
    bool isMouseBindingsSubscribedSurface;
    bool isMouseBindingsSubscribedOverlay;
    MouseButtonEventHandler surfaceMouseUp, overlayMouseUp;
    Window standAloneOverlay;

    CornerRadius zeroCornerRadius = new(0);
    Point zeroPoint = new(0, 0);
    Point mouseLeftDownPoint = new(0, 0);
    Point mouseMoveLastPoint = new(0, 0);

    Rect rectDetachedLast = Rect.Empty;
    Rect rectIntersectLast = rectRandom;
    Rect rectInitLast = rectRandom;

    private class FlyleafHostDropWrap { public FlyleafHost FlyleafHost; } // To allow non FlyleafHosts to drag & drop
    protected readonly LogHandler Log;
    #endregion

    #region Dependency Properties
    public void BringToFront() => SetWindowPos(SurfaceHandle, IntPtr.Zero, 0, 0, 0, 0, (UInt32)(SetWindowPosFlags.SWP_SHOWWINDOW | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOMOVE));
    public bool BringToFrontOnClick
    {
        get { return (bool)GetValue(BringToFrontOnClickProperty); }
        set { SetValue(BringToFrontOnClickProperty, value); }
    }
    public static readonly DependencyProperty BringToFrontOnClickProperty =
    DependencyProperty.Register(nameof(BringToFrontOnClick), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

    public AvailableWindows OpenOnDrop
    {
        get => (AvailableWindows)GetValue(OpenOnDropProperty);
        set => SetValue(OpenOnDropProperty, value);
    }
    public static readonly DependencyProperty OpenOnDropProperty =
        DependencyProperty.Register(nameof(OpenOnDrop), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface, new PropertyChangedCallback(DropChanged)));
    
    public AvailableWindows SwapOnDrop
    {
        get => (AvailableWindows)GetValue(SwapOnDropProperty);
        set => SetValue(SwapOnDropProperty, value);
    }
    public static readonly DependencyProperty SwapOnDropProperty =
        DependencyProperty.Register(nameof(SwapOnDrop), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface, new PropertyChangedCallback(DropChanged)));

    public AvailableWindows SwapDragEnterOnShift
    {
        get => (AvailableWindows)GetValue(SwapDragEnterOnShiftProperty);
        set => SetValue(SwapDragEnterOnShiftProperty, value);
    }
    public static readonly DependencyProperty SwapDragEnterOnShiftProperty =
        DependencyProperty.Register(nameof(SwapDragEnterOnShift), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

    public AvailableWindows ToggleFullScreenOnDoubleClick
    {
        get => (AvailableWindows)GetValue(ToggleFullScreenOnDoubleClickProperty);
        set => SetValue(ToggleFullScreenOnDoubleClickProperty, value);
    }
    public static readonly DependencyProperty ToggleFullScreenOnDoubleClickProperty =
        DependencyProperty.Register(nameof(ToggleFullScreenOnDoubleClick), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

    public AvailableWindows PanMoveOnCtrl
    {
        get => (AvailableWindows)GetValue(PanMoveOnCtrlProperty);
        set => SetValue(PanMoveOnCtrlProperty, value);
    }
    public static readonly DependencyProperty PanMoveOnCtrlProperty =
        DependencyProperty.Register(nameof(PanMoveOnCtrl), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

    public AvailableWindows PanRotateOnShiftWheel
    {
        get => (AvailableWindows)GetValue(PanRotateOnShiftWheelProperty);
        set => SetValue(PanRotateOnShiftWheelProperty, value);
    }
    public static readonly DependencyProperty PanRotateOnShiftWheelProperty =
        DependencyProperty.Register(nameof(PanRotateOnShiftWheel), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

    public AvailableWindows PanZoomOnCtrlWheel
    {
        get => (AvailableWindows)GetValue(PanZoomOnCtrlWheelProperty);
        set => SetValue(PanZoomOnCtrlWheelProperty, value);
    }
    public static readonly DependencyProperty PanZoomOnCtrlWheelProperty =
        DependencyProperty.Register(nameof(PanZoomOnCtrlWheel), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));


    public AttachedDragMoveOptions AttachedDragMove
    {
        get => (AttachedDragMoveOptions)GetValue(AttachedDragMoveProperty);
        set => SetValue(AttachedDragMoveProperty, value);
    }
    public static readonly DependencyProperty AttachedDragMoveProperty =
        DependencyProperty.Register(nameof(AttachedDragMove), typeof(AttachedDragMoveOptions), typeof(FlyleafHost), new PropertyMetadata(AttachedDragMoveOptions.Surface));

    public AvailableWindows DetachedDragMove
    {
        get => (AvailableWindows)GetValue(DetachedDragMoveProperty);
        set => SetValue(DetachedDragMoveProperty, value);
    }
    public static readonly DependencyProperty DetachedDragMoveProperty =
        DependencyProperty.Register(nameof(DetachedDragMove), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));


    public AvailableWindows AttachedResize
    {
        get => (AvailableWindows)GetValue(AttachedResizeProperty);
        set => SetValue(AttachedResizeProperty, value);
    }
    public static readonly DependencyProperty AttachedResizeProperty =
        DependencyProperty.Register(nameof(AttachedResize), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

    public AvailableWindows DetachedResize
    {
        get => (AvailableWindows)GetValue(DetachedResizeProperty);
        set => SetValue(DetachedResizeProperty, value);
    }
    public static readonly DependencyProperty DetachedResizeProperty =
        DependencyProperty.Register(nameof(DetachedResize), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

    public bool KeepRatioOnResize
    {
        get => (bool)GetValue(KeepRatioOnResizeProperty);
        set => SetValue(KeepRatioOnResizeProperty, value);
    }
    public static readonly DependencyProperty KeepRatioOnResizeProperty =
        DependencyProperty.Register(nameof(KeepRatioOnResize), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnKeepRatioOnResizeChanged)));

    public float CurResizeRatio
    {
        get => (float)GetValue(CurResizeRatioProperty);
        private set => SetValue(CurResizeRatioProperty, value);
    }
    public static readonly DependencyProperty CurResizeRatioProperty =
        DependencyProperty.Register(nameof(CurResizeRatio), typeof(float), typeof(FlyleafHost), new PropertyMetadata((float)0, new PropertyChangedCallback(OnCurResizeRatioChanged)));

    public int ResizeSensitivity
    {
        get => (int)GetValue(ResizeSensitivityProperty);
        set => SetValue(ResizeSensitivityProperty, value);
    }
    public static readonly DependencyProperty ResizeSensitivityProperty =
        DependencyProperty.Register(nameof(ResizeSensitivity), typeof(int), typeof(FlyleafHost), new PropertyMetadata(6));


    public DetachedPositionOptions DetachedPosition
    {
        get => (DetachedPositionOptions)GetValue(DetachedPositionProperty);
        set => SetValue(DetachedPositionProperty, value);
    }
    public static readonly DependencyProperty DetachedPositionProperty =
        DependencyProperty.Register(nameof(DetachedPosition), typeof(DetachedPositionOptions), typeof(FlyleafHost), new PropertyMetadata(DetachedPositionOptions.BottomRight));

    public Thickness DetachedPositionMargin
    {
        get => (Thickness)GetValue(DetachedPositionMarginProperty);
        set => SetValue(DetachedPositionMarginProperty, value);
    }
    public static readonly DependencyProperty DetachedPositionMarginProperty =
        DependencyProperty.Register(nameof(DetachedPositionMargin), typeof(Thickness), typeof(FlyleafHost), new PropertyMetadata(new Thickness(0, 0, 0, 0)));

    public Point DetachedFixedPosition
    {
        get => (Point)GetValue(DetachedFixedPositionProperty);
        set => SetValue(DetachedFixedPositionProperty, value);
    }
    public static readonly DependencyProperty DetachedFixedPositionProperty =
        DependencyProperty.Register(nameof(DetachedFixedPosition), typeof(Point), typeof(FlyleafHost), new PropertyMetadata(new Point()));

    public Size DetachedFixedSize
    {
        get => (Size)GetValue(DetachedFixedSizeProperty);
        set => SetValue(DetachedFixedSizeProperty, value);
    }
    public static readonly DependencyProperty DetachedFixedSizeProperty =
        DependencyProperty.Register(nameof(DetachedFixedSize), typeof(Size), typeof(FlyleafHost), new PropertyMetadata(new Size(300, 200)));

    public bool DetachedRememberPosition
    {
        get => (bool)GetValue(DetachedRememberPositionProperty);
        set => SetValue(DetachedRememberPositionProperty, value);
    }
    public static readonly DependencyProperty DetachedRememberPositionProperty =
        DependencyProperty.Register(nameof(DetachedRememberPosition), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

    public bool DetachedRememberSize
    {
        get => (bool)GetValue(DetachedRememberSizeProperty);
        set => SetValue(DetachedRememberSizeProperty, value);
    }
    public static readonly DependencyProperty DetachedRememberSizeProperty =
        DependencyProperty.Register(nameof(DetachedRememberSize), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

    public bool DetachedTopMost
    {
        get => (bool)GetValue(DetachedTopMostProperty);
        set => SetValue(DetachedTopMostProperty, value);
    }
    public static readonly DependencyProperty DetachedTopMostProperty =
        DependencyProperty.Register(nameof(DetachedTopMost), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnDetachedTopMostChanged)));

    public bool DetachedShowInTaskbar
    {
        get { return (bool)GetValue(DetachedShowInTaskbarProperty); }
        set { SetValue(DetachedShowInTaskbarProperty, value); }
    }
    public static readonly DependencyProperty DetachedShowInTaskbarProperty =
    DependencyProperty.Register(nameof(DetachedShowInTaskbar), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));

    public bool DetachedShowInTaskbarNoOwner
    {
        get { return (bool)GetValue(DetachedShowInTaskbarNoOwnerProperty); }
        set { SetValue(DetachedShowInTaskbarNoOwnerProperty, value); }
    }
    public static readonly DependencyProperty DetachedShowInTaskbarNoOwnerProperty =
    DependencyProperty.Register(nameof(DetachedShowInTaskbarNoOwner), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));

    public int DetachedMinHeight
    {
        get { return (int)GetValue(DetachedMinHeightProperty); }
        set { SetValue(DetachedMinHeightProperty, value); }
    }
    public static readonly DependencyProperty DetachedMinHeightProperty =
    DependencyProperty.Register(nameof(DetachedMinHeight), typeof(int), typeof(FlyleafHost), new PropertyMetadata(0));

    public int DetachedMinWidth
    {
        get { return (int)GetValue(DetachedMinWidthProperty); }
        set { SetValue(DetachedMinWidthProperty, value); }
    }
    public static readonly DependencyProperty DetachedMinWidthProperty =
    DependencyProperty.Register(nameof(DetachedMinWidth), typeof(int), typeof(FlyleafHost), new PropertyMetadata(0));

    public double DetachedMaxHeight
    {
        get { return (double)GetValue(DetachedMaxHeightProperty); }
        set { SetValue(DetachedMaxHeightProperty, value); }
    }
    public static readonly DependencyProperty DetachedMaxHeightProperty =
    DependencyProperty.Register(nameof(DetachedMaxHeight), typeof(double), typeof(FlyleafHost), new PropertyMetadata(double.PositiveInfinity));

    public double DetachedMaxWidth
    {
        get { return (double)GetValue(DetachedMaxWidthProperty); }
        set { SetValue(DetachedMaxWidthProperty, value); }
    }
    public static readonly DependencyProperty DetachedMaxWidthProperty =
    DependencyProperty.Register(nameof(DetachedMaxWidth), typeof(double), typeof(FlyleafHost), new PropertyMetadata(double.PositiveInfinity));


    public AvailableWindows KeyBindings
    {
        get => (AvailableWindows)GetValue(KeyBindingsProperty);
        set => SetValue(KeyBindingsProperty, value);
    }
    public static readonly DependencyProperty KeyBindingsProperty =
        DependencyProperty.Register(nameof(KeyBindings), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

    public AvailableWindows MouseBindings
    {
        get => (AvailableWindows)GetValue(MouseBindingsProperty);
        set => SetValue(MouseBindingsProperty, value);
    }
    public static readonly DependencyProperty MouseBindingsProperty =
        DependencyProperty.Register(nameof(MouseBindings), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Both, new PropertyChangedCallback(OnMouseBindings)));

    public int ActivityTimeout
    {
        get => (int)GetValue(ActivityTimeoutProperty);
        set => SetValue(ActivityTimeoutProperty, value);
    }
    public static readonly DependencyProperty ActivityTimeoutProperty =
        DependencyProperty.Register(nameof(ActivityTimeout), typeof(int), typeof(FlyleafHost), new PropertyMetadata(0, new PropertyChangedCallback(OnActivityTimeoutChanged)));


    public bool IsAttached
    {
        get => (bool)GetValue(IsAttachedProperty);
        set => SetValue(IsAttachedProperty, value);
    }
    public static readonly DependencyProperty IsAttachedProperty =
        DependencyProperty.Register(nameof(IsAttached), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true, new PropertyChangedCallback(OnIsAttachedChanged)));

    public bool IsMinimized
    {
        get => (bool)GetValue(IsMinimizedProperty);
        set => SetValue(IsMinimizedProperty, value);
    }
    public static readonly DependencyProperty IsMinimizedProperty =
        DependencyProperty.Register(nameof(IsMinimized), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnIsMinimizedChanged)));

    public bool IsFullScreen
    {
        get => (bool)GetValue(IsFullScreenProperty);
        set => SetValue(IsFullScreenProperty, value);
    }
    public static readonly DependencyProperty IsFullScreenProperty =
        DependencyProperty.Register(nameof(IsFullScreen), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnIsFullScreenChanged)));

    public bool IsResizing
    {
        get => (bool)GetValue(IsResizingProperty);
        private set => SetValue(IsResizingProperty, value);
    }
    public static readonly DependencyProperty IsResizingProperty =
        DependencyProperty.Register(nameof(IsResizing), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));

    public bool IsStandAlone
    {
        get => (bool)GetValue(IsStandAloneProperty);
        private set => SetValue(IsStandAloneProperty, value);
    }
    public static readonly DependencyProperty IsStandAloneProperty =
        DependencyProperty.Register(nameof(IsStandAlone), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));

    public bool IsSwapping
    {
        get => (bool)GetValue(IsSwappingProperty);
        private set => SetValue(IsSwappingProperty, value);
    }
    public static readonly DependencyProperty IsSwappingProperty =
        DependencyProperty.Register(nameof(IsSwapping), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));


    public FrameworkElement MarginTarget
    {
        get => (FrameworkElement)GetValue(MarginTargetProperty);
        set => SetValue(MarginTargetProperty, value);
    }
    public static readonly DependencyProperty MarginTargetProperty =
        DependencyProperty.Register(nameof(MarginTarget), typeof(FrameworkElement), typeof(FlyleafHost), new PropertyMetadata(null));

    public object HostDataContext
    {
        get => GetValue(HostDataContextProperty);
        set => SetValue(HostDataContextProperty, value);
    }
    public static readonly DependencyProperty HostDataContextProperty =
        DependencyProperty.Register(nameof(HostDataContext), typeof(object), typeof(FlyleafHost), new PropertyMetadata(null));

    public object DetachedContent
    {
        get => GetValue(DetachedContentProperty);
        set => SetValue(DetachedContentProperty, value);
    }
    public static readonly DependencyProperty DetachedContentProperty =
        DependencyProperty.Register(nameof(DetachedContent), typeof(object), typeof(FlyleafHost), new PropertyMetadata(null));

    public Player Player
    {
        get => (Player)GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }
    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(nameof(Player), typeof(Player), typeof(FlyleafHost), new PropertyMetadata(null, OnPlayerChanged));

    public ControlTemplate OverlayTemplate
    {
        get => (ControlTemplate)GetValue(OverlayTemplateProperty);
        set => SetValue(OverlayTemplateProperty, value);
    }
    public static readonly DependencyProperty OverlayTemplateProperty =
        DependencyProperty.Register(nameof(OverlayTemplate), typeof(ControlTemplate), typeof(FlyleafHost), new PropertyMetadata(null, new PropertyChangedCallback(OnOverlayTemplateChanged)));

    public Window Overlay
    {
        get => (Window)GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }
    public static readonly DependencyProperty OverlayProperty =
        DependencyProperty.Register(nameof(Overlay), typeof(Window), typeof(FlyleafHost), new PropertyMetadata(null, new PropertyChangedCallback(OnOverlayChanged)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(FlyleafHost), new PropertyMetadata(new CornerRadius(0), new PropertyChangedCallback(OnCornerRadiusChanged)));
    #endregion

    #region Events
    private static void OnMouseBindings(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;

        host.SetMouseSurface();
        host.SetMouseOverlay();
    }
    private static void OnDetachedTopMostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;
        host.Surface.Topmost = !host.IsAttached && host.DetachedTopMost;
    }
    private static void DropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        FlyleafHost host = d as FlyleafHost;
        host.Surface.AllowDrop =
            host.OpenOnDrop == AvailableWindows.Surface || host.OpenOnDrop == AvailableWindows.Both ||
            host.SwapOnDrop == AvailableWindows.Surface || host.SwapOnDrop == AvailableWindows.Both;

        if (host.Overlay == null)
            return;

        host.Overlay.AllowDrop =
            host.OpenOnDrop == AvailableWindows.Overlay || host.OpenOnDrop == AvailableWindows.Both ||
            host.SwapOnDrop == AvailableWindows.Overlay || host.SwapOnDrop == AvailableWindows.Both;
    }
    private static void OnCurResizeRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;
        if (!host.KeepRatioOnResize || host.CurResizeRatio <= 0)
            return;

        if (host.IsAttached)
            host.Height = host.Width / host.CurResizeRatio;
        else
        {
            // TBR: CurResizeRatio < 1 should change the Width?
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)host.Surface.Top, (int)host.Surface.Left)).Bounds;

            if (host.Surface.Top > screen.Height / 2)
                host.Surface.Top += host.Surface.Height - (host.Surface.Width / host.CurResizeRatio);

            host.Surface.Height = host.Surface.Width / host.CurResizeRatio;
        }
    }
    private static void OnKeepRatioOnResizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;
        host.CurResizeRatio = host.KeepRatioOnResize
            ? host.Player != null && host.Player.Video.AspectRatio.Value > 0 ? host.Player.Video.AspectRatio.Value : (float)(16.0/9.0)
            : 0;
    }
    private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;
        
        FlyleafHost host = d as FlyleafHost;
        host.SetPlayer((Player)e.OldValue);
    }
    private static void OnIsFullScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;
        host.RefreshNormalFullScreen();
    }
    private static void OnIsMinimizedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;
        host.Surface.WindowState = host.IsMinimized ? WindowState.Minimized : (host.IsFullScreen ? WindowState.Maximized : WindowState.Normal);
    }
    private static void OnIsAttachedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;

        if (host.IsStandAlone)
        {
            host.IsAttached = false;
            return;
        }

        if (!host.IsLoaded)
            return;

        if (host.IsAttached)
            host.Attach();
        else
            host.Detach();
    }
    private static void OnActivityTimeoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        FlyleafHost host = d as FlyleafHost;

        if (host.Player == null)
            return;

        host.Player.Activity.Timeout = host.ActivityTimeout;
    }
    private static void OnOverlayTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;

        host.Overlay ??= new Window() { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true };

        host.Overlay.Template = host.OverlayTemplate;
    }
    private static void OnOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        FlyleafHost host = d as FlyleafHost;

        if (!isDesginMode)
            host.SetOverlay();
        else
        {
            // XSurface.Wpf.Window (can this work on designer?
        }
    }
    private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (isDesginMode)
            return;

        FlyleafHost host = d as FlyleafHost;

        if (host.Surface == null)
            return;

        if (host.CornerRadius == host.zeroCornerRadius)
            host.Surface.Background  = Brushes.Black;
        else
        {
            host.Surface.Background  = Brushes.Transparent;
            host.Surface.Content = new Border()
            {
                Background = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = host.CornerRadius,
            };
        }

        if (host?.Player == null)
            return;

        host.Player.renderer.CornerRadius = (CornerRadius)e.NewValue;

    }
    private static object OnContentChanging(DependencyObject d, object baseValue)
    {
        if (isDesginMode)
            return baseValue;

        FlyleafHost host = d as FlyleafHost;
        
        if (baseValue != null && host.Overlay == null)
            host.Overlay = new Window() { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true };

        if (host.Overlay != null)
            host.Overlay.Content = baseValue;

        return host.DetachedContent;
    }
    
    private void Host_Unloaded(object sender, RoutedEventArgs e)
    {
        LayoutUpdated   -= Host_LayoutUpdated;
        IsVisibleChanged-= Host_IsVisibleChanged;
    }
    private void Host_Loaded(object sender, RoutedEventArgs e)
    {
        if (Disposed)
            return;

        // TODO: Handle owner changed
        Window owner = Window.GetWindow(this);

        Owner           = owner;
        OwnerHandle     = new WindowInteropHelper(Owner).EnsureHandle();
        HostDataContext = DataContext;
        Surface.Owner   = Owner;
        Surface.Title   = Owner.Title;
        Surface.Icon    = Owner.Icon;

        LayoutUpdated   += Host_LayoutUpdated;
        IsVisibleChanged+= Host_IsVisibleChanged;

        SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_EXSTYLE,!DetachedShowInTaskbarNoOwner && DetachedShowInTaskbar ? (nint)WindowStylesEx.WS_EX_APPWINDOW : 0);

        if (IsAttached)
        {
            Attach();
            rectDetachedLast = Rect.Empty; // Attach will set it wrong first time
            Host_IsVisibleChanged(null, new());
        }
        else
        {
            Detach();
            Surface.Show();
            Overlay?.Show();
        }
    }
    private void Host_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        // TBR
        // 1. this.DataContext: FlyleafHost's DataContext will not be affected (Inheritance)
        // 2. Overlay.DataContext: Overlay's DataContext will be FlyleafHost itself
        // 3. Overlay.DataContext.HostDataContext: FlyleafHost's DataContext includes HostDataContext to access FlyleafHost's DataContext
        // 4. In case of Stand Alone will let the user to decide

        HostDataContext = DataContext;
    private void Host_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsAttached)
            return;

        if (IsVisible)
        {
            Surface.Show();
            Overlay?.Show();
        }
        else
        {
            Surface.Hide();
            Overlay?.Hide();
        }
    }
    private void Host_LayoutUpdated(object sender, EventArgs e)
    {
        // Finds Rect Intersect with FlyleafHost's parents and Clips Surface/Overlay (eg. within ScrollViewer)
        // TBR: Option not to clip rect or stop at first/second parent?
        // For performance should focus only on ScrollViewer if any and Owner Window (other sources that clip our host?)

        if (!IsVisible || !IsAttached || IsFullScreen || IsResizing)
            return;

        Rect rectInit = new(TransformToAncestor(Owner).Transform(zeroPoint), RenderSize);
        var rectIntersect = rectInit;

        FrameworkElement parent = this;
        while ((parent = VisualTreeHelper.GetParent(parent) as FrameworkElement) != null)
            rectIntersect.Intersect(new Rect(parent.TransformToAncestor(Owner).Transform(zeroPoint), parent.RenderSize));

        if (rectInit != rectInitLast)
        {
            SetRect(rectInit);
            rectInitLast = rectInit;
        }

        if (rectIntersect == Rect.Empty)
        {
            if (rectIntersect == rectIntersectLast)
                return;

            rectIntersectLast = rectIntersect;
            SetVisibleRect(new Rect(0, 0, 0, 0));
        }
        else
        {
            rectIntersect.X -= rectInit.X;
            rectIntersect.Y -= rectInit.Y;

            if (rectIntersect == rectIntersectLast)
                return;

            rectIntersectLast = rectIntersect;

            SetVisibleRect(rectIntersect);
        }
    }
    private void Player_Video_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (KeepRatioOnResize && e.PropertyName == nameof(Player.Video.AspectRatio) && Player.Video.AspectRatio.Value > 0)
            CurResizeRatio = Player.Video.AspectRatio.Value;
    }
    #endregion

    #region Events Surface / Overlay
    private void Surface_KeyDown(object sender, KeyEventArgs e) { if (KeyBindings == AvailableWindows.Surface || KeyBindings == AvailableWindows.Both) e.Handled = Player.KeyDown(Player, e); }
    private void Overlay_KeyDown(object sender, KeyEventArgs e) { if (KeyBindings == AvailableWindows.Overlay || KeyBindings == AvailableWindows.Both) e.Handled = Player.KeyDown(Player, e); }

    private void Surface_KeyUp(object sender, KeyEventArgs e) { if (KeyBindings == AvailableWindows.Surface || KeyBindings == AvailableWindows.Both) e.Handled = Player.KeyUp(Player, e); }
    private void Overlay_KeyUp(object sender, KeyEventArgs e) { if (KeyBindings == AvailableWindows.Overlay || KeyBindings == AvailableWindows.Both) e.Handled = Player.KeyUp(Player, e); }

    private void Surface_Drop(object sender, DragEventArgs e)
    {
        IsSwapping = false;
        Surface.ReleaseMouseCapture();
        FlyleafHostDropWrap hostWrap = (FlyleafHostDropWrap) e.Data.GetData(typeof(FlyleafHostDropWrap));
        
        // Swap FlyleafHosts
        if (hostWrap != null)
        {
            (hostWrap.FlyleafHost.Player, Player) = (Player, hostWrap.FlyleafHost.Player);
            Surface.Activate();
            return;
        }

        if (Player == null)
            return;

        // Player Open File
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string filename = ((string[])e.Data.GetData(DataFormats.FileDrop, false))[0];
            Player.OpenAsync(filename);
        }

        // Player Open Text
        else if (e.Data.GetDataPresent(DataFormats.Text))
        {
            string text = e.Data.GetData(DataFormats.Text, false).ToString();
            if (text.Length > 0)
                Player.OpenAsync(text);
        }
    }
    private void Overlay_Drop(object sender, DragEventArgs e)
    {
        IsSwapping = false;
        Overlay.ReleaseMouseCapture();
        FlyleafHostDropWrap hostWrap = (FlyleafHostDropWrap) e.Data.GetData(typeof(FlyleafHostDropWrap));
        
        // Swap FlyleafHosts
        if (hostWrap != null)
        {
            (hostWrap.FlyleafHost.Player, Player) = (Player, hostWrap.FlyleafHost.Player);
            Overlay.Activate();
            return;
        }

        if (Player == null)
            return;

        // Player Open File
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string filename = ((string[])e.Data.GetData(DataFormats.FileDrop, false))[0];
            Player.OpenAsync(filename);
        }

        // Player Open Text
        else if (e.Data.GetDataPresent(DataFormats.Text))
        {
            string text = e.Data.GetData(DataFormats.Text, false).ToString();
            if (text.Length > 0)
                Player.OpenAsync(text);
        }
    }
    private void Surface_DragEnter(object sender, DragEventArgs e) { if (Player != null) e.Effects = DragDropEffects.All; }
    private void Overlay_DragEnter(object sender, DragEventArgs e) { if (Player != null) e.Effects = DragDropEffects.All; }
    private void Surface_StateChanged(object sender, EventArgs e)
    {
        switch (Surface.WindowState)
        {
            case WindowState.Maximized:
                IsFullScreen = true;
                IsMinimized = false;
                Player?.Activity.RefreshFullActive();

                break;

            case WindowState.Normal:

                IsFullScreen = false;
                IsMinimized = false;
                Player?.Activity.RefreshFullActive();

                break;

            case WindowState.Minimized:

                IsMinimized = true;
                break;
        }
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Bring to front
        //SetWindowPos(OwnerHandle, IntPtr.Zero, 0, 0, 0, 0, (UInt32)(SetWindowPosFlags.SWP_SHOWWINDOW | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOMOVE)); 

        if (BringToFrontOnClick)
            BringToFront();

        mouseLeftDownPoint = e.GetPosition(Surface);

        if ((SwapOnDrop == AvailableWindows.Surface || SwapOnDrop == AvailableWindows.Both) && 
            (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
        {
            IsSwapping = true;
            DragDrop.DoDragDrop(this, new FlyleafHostDropWrap() { FlyleafHost = this }, DragDropEffects.Move);
            
            return;
        }

        if (ResizingSide != 0)
        {
            ResetVisibleRect();
            IsResizing = true;
        }
        else
        {
            if (Player != null)
            {
                Player.Activity.RefreshFullActive();

                panPrevX = Player.PanXOffset;
                panPrevY = Player.PanYOffset;
            }
        }

        Surface.CaptureMouse();
    }
    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Bring to front
        SetWindowPos(OwnerHandle, IntPtr.Zero, 0, 0, 0, 0, (UInt32)(SetWindowPosFlags.SWP_SHOWWINDOW | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOMOVE)); 
        if (BringToFrontOnClick)
            BringToFront();

        mouseLeftDownPoint = e.GetPosition(Overlay);
        
        if ((SwapOnDrop == AvailableWindows.Overlay || SwapOnDrop == AvailableWindows.Both) && 
            (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
        {
            IsSwapping = true;
            DragDrop.DoDragDrop(this, new FlyleafHostDropWrap() { FlyleafHost = this }, DragDropEffects.Move);
            
            return;
        }

        if (ResizingSide != 0)
        {
            ResetVisibleRect();
            IsResizing = true;
        }
        else
        {
            if (Player != null)
            {
                Player.Activity.RefreshFullActive();

                panPrevX = Player.PanXOffset;
                panPrevY = Player.PanYOffset;
            }
        }

        Overlay.CaptureMouse();
    }

    private void ReleaseMouseCapture12(object sender, MouseEventArgs e) => ReleaseMouseCapture1(sender, null);
    private void ReleaseMouseCapture1(object sender, MouseButtonEventArgs e)
    {
        if (IsResizing)
        {
            ResizingSide = 0;
            Surface.Cursor = Cursors.Arrow;
            IsResizing = false;
            Host_LayoutUpdated(null, null); // When attached to restore the clipped rect
        }
        mouseLeftDownPoint.X = -1;
        IsSwapping = false;
        Surface.Focus();
    }
    private void ReleaseMouseCapture2(object sender, MouseEventArgs e)
    {
        if (IsResizing)
        {
            ResizingSide = 0;
            Overlay.Cursor = Cursors.Arrow;
            IsResizing = false;
            Host_LayoutUpdated(null, null); // When attached to restore the clipped rect
        }
        mouseLeftDownPoint.X = -1;
        IsSwapping = false;
        Overlay.Focus();
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        var cur = e.GetPosition(Overlay);
         
        if (Player != null && cur != mouseMoveLastPoint)
        {
            Player.Activity.RefreshFullActive();
            mouseMoveLastPoint = cur;
        }

        // Resize Sides (CanResize + !MouseDown + !FullScreen)
        if (e.MouseDevice.LeftButton != MouseButtonState.Pressed)
        {
            if ( !IsFullScreen && 
                ((IsAttached && (AttachedResize == AvailableWindows.Surface || AttachedResize == AvailableWindows.Both)) ||
                (!IsAttached && (DetachedResize == AvailableWindows.Surface || DetachedResize == AvailableWindows.Both))))
            {
                ResizingSide = ResizeSides(Surface, cur, ResizeSensitivity, CornerRadius);
            }
        }
        else if (IsSwapping)
            return;

        // Resize (MouseDown + ResizeSide != 0)
        else if (IsResizing)
        {
            Point x1 = new(Surface.Left, Surface.Top);

            Resize(Surface, this, cur, ResizingSide, CurResizeRatio);

            if (IsAttached)
            {
                Point x2 = new(Surface.Left, Surface.Top);
                
                MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + x2.X - x1.X, MarginTarget.Margin.Top + x2.Y - x1.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                Width   = Surface.Width;
                Height  = Surface.Height;
            }
        }

        // Bug? happens on double click
        else if (mouseLeftDownPoint.X == -1)
            return;

        // Player's Pan Move (Ctrl + Drag Move)
        else if (Player != null && 
            (PanMoveOnCtrl == AvailableWindows.Surface || PanMoveOnCtrl == AvailableWindows.Both) &&
            (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
        {
            Player.PanXOffset = panPrevX + (int) (cur.X - mouseLeftDownPoint.X);
            Player.PanYOffset = panPrevY + (int) (cur.Y - mouseLeftDownPoint.Y);
        }

        // Drag Move Self (Detached) / Self (Attached) / Owner (Attached)
        else if (Surface.IsMouseCaptured && !IsFullScreen)
        {
            if (IsAttached)
            {
                if (AttachedDragMove == AttachedDragMoveOptions.SurfaceOwner || AttachedDragMove == AttachedDragMoveOptions.BothOwner)
                {
                    if (Owner != null)
                    {
                        Owner.Left  += cur.X - mouseLeftDownPoint.X;
                        Owner.Top   += cur.Y - mouseLeftDownPoint.Y;
                    }
                }
                else if (AttachedDragMove == AttachedDragMoveOptions.Surface || AttachedDragMove == AttachedDragMoveOptions.Both)
                {
                    // TBR: Bug with right click (popup menu) and then left click drag
                    MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + cur.X - mouseLeftDownPoint.X, MarginTarget.Margin.Top + cur.Y - mouseLeftDownPoint.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                }
            } else
            {
                if (DetachedDragMove == AvailableWindows.Surface || DetachedDragMove == AvailableWindows.Both)
                {
                    Surface.Left  += cur.X - mouseLeftDownPoint.X;
                    Surface.Top   += cur.Y - mouseLeftDownPoint.Y;
                }
            }
        }
    }
    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        var cur = e.GetPosition(Overlay);
         
        if (Player != null && cur != mouseMoveLastPoint)
        {
            Player.Activity.RefreshFullActive();
            mouseMoveLastPoint = cur;
        }

        // Resize Sides (CanResize + !MouseDown + !FullScreen)
        if (e.MouseDevice.LeftButton != MouseButtonState.Pressed && cur != zeroPoint)
        {
            if ( !IsFullScreen && 
                ((IsAttached && (AttachedResize == AvailableWindows.Overlay || AttachedResize == AvailableWindows.Both)) ||
                (!IsAttached && (DetachedResize == AvailableWindows.Overlay || DetachedResize == AvailableWindows.Both))))
            {
                ResizingSide = ResizeSides(Overlay, cur, ResizeSensitivity, CornerRadius);
            }
        }
        else if (IsSwapping)
            return;

        // Resize (MouseDown + ResizeSide != 0)
        else if (IsResizing)
        {
            Point x1 = new(Surface.Left, Surface.Top);

            Resize(Surface, this, cur, ResizingSide, CurResizeRatio);

            if (IsAttached)
            {
                Point x2 = new(Surface.Left, Surface.Top);
                
                MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + x2.X - x1.X, MarginTarget.Margin.Top + x2.Y - x1.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                Width   = Overlay.Width;
                Height  = Overlay.Height;
            }
        }

        // Bug? happens on double click
        else if (mouseLeftDownPoint.X == -1)
            return;

        // Player's Pan Move (Ctrl + Drag Move)
        else if (Player != null && 
            (PanMoveOnCtrl == AvailableWindows.Overlay || PanMoveOnCtrl == AvailableWindows.Both) &&
            (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
        {
            Player.PanXOffset = panPrevX + (int) (cur.X - mouseLeftDownPoint.X);
            Player.PanYOffset = panPrevY + (int) (cur.Y - mouseLeftDownPoint.Y);
        }

        // Drag Move Self (Detached) / Self (Attached) / Owner (Attached)
        else if (Overlay.IsMouseCaptured && !IsFullScreen)
        {
            if (IsAttached)
            {
                if (AttachedDragMove == AttachedDragMoveOptions.OverlayOwner || AttachedDragMove == AttachedDragMoveOptions.BothOwner)
                {
                    if (Owner != null)
                    {
                        Owner.Left  += cur.X - mouseLeftDownPoint.X;
                        Owner.Top   += cur.Y - mouseLeftDownPoint.Y;
                    }
                }
                else if (AttachedDragMove == AttachedDragMoveOptions.Overlay || AttachedDragMove == AttachedDragMoveOptions.Both)
                {
                    // TBR: Bug with right click (popup menu) and then left click drag
                    MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + cur.X - mouseLeftDownPoint.X, MarginTarget.Margin.Top + cur.Y - mouseLeftDownPoint.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                }
            }
            else
            {
                if (DetachedDragMove == AvailableWindows.Overlay || DetachedDragMove == AvailableWindows.Both)
                {
                    Surface.Left    += cur.X - mouseLeftDownPoint.X;
                    Surface.Top     += cur.Y - mouseLeftDownPoint.Y;
                }
            }
        }
    }

    private void Surface_MouseLeave(object sender, MouseEventArgs e) { ResizingSide = 0; Surface.Cursor = Cursors.Arrow; }
    private void Overlay_MouseLeave(object sender, MouseEventArgs e) { ResizingSide = 0; Overlay.Cursor = Cursors.Arrow; }

    private void Surface_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (ToggleFullScreenOnDoubleClick == AvailableWindows.Surface || ToggleFullScreenOnDoubleClick == AvailableWindows.Both) { IsFullScreen = !IsFullScreen; e.Handled = true; } }
    private void Overlay_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (ToggleFullScreenOnDoubleClick == AvailableWindows.Overlay || ToggleFullScreenOnDoubleClick == AvailableWindows.Both) { IsFullScreen = !IsFullScreen; e.Handled = true; } }

    private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Player == null || e.Delta == 0)
            return;

        if      ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) &&
            (PanZoomOnCtrlWheel == AvailableWindows.Surface || PanZoomOnCtrlWheel == AvailableWindows.Both))
        {
            var cur = e.GetPosition(Surface);
            Point curDpi = new(cur.X * DpiX, cur.Y * DpiY);
            if (e.Delta > 0)
                Player.ZoomIn(curDpi);
            else
                Player.ZoomOut(curDpi);
        }
        else if ((Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) &&
            (PanRotateOnShiftWheel == AvailableWindows.Surface || PanZoomOnCtrlWheel == AvailableWindows.Both))
        {
            if (e.Delta > 0)
                Player.RotateRight();
            else
                Player.RotateLeft();
        }

        //else if (IsAttached) // TBR ScrollViewer
        //{
        //    RaiseEvent(e);
        //}
    }
    private void Overlay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Player == null || e.Delta == 0)
            return;

        if      ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) &&
            (PanZoomOnCtrlWheel == AvailableWindows.Overlay || PanZoomOnCtrlWheel == AvailableWindows.Both))
        {
            var cur = e.GetPosition(Overlay);
            Point curDpi = new(cur.X * DpiX, cur.Y * DpiY);
            if (e.Delta > 0)
                Player.ZoomIn(curDpi);
            else
                Player.ZoomOut(curDpi);
        }
        else if ((Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) &&
            (PanRotateOnShiftWheel == AvailableWindows.Overlay || PanZoomOnCtrlWheel == AvailableWindows.Both))
        {
            if (e.Delta > 0)
                Player.RotateRight();
            else
                Player.RotateLeft();
        }
    }

    private void Surface_Closed(object sender, EventArgs e)
    {
        surfaceClosed = true;
        Dispose();
    }
    private void Surface_Closing(object sender, CancelEventArgs e) => surfaceClosing = true;
    private void Overlay_Closed(object sender, EventArgs e)
    {
        overlayClosed = true;
        if (!surfaceClosing)
            Surface?.Close();
    }

    private void OverlayAttached_ContentRendered(object sender, EventArgs e)
    {
        if (DetachedContent != null || Overlay.Content == null || ActualWidth != 0)
            return;

        try
        {
            var t = ((FrameworkElement)Overlay.Content).RenderSize;
            Width = t.Width; Height = t.Height;
        } catch { }
    }
    private void OverlayStandAlone_Loaded(object sender, RoutedEventArgs e)
    {
        if (Overlay != null)
            return;

        if (standAloneOverlay.WindowStyle != WindowStyle.None || standAloneOverlay.AllowsTransparency == false)
            throw new Exception("Stand-alone FlyleafHost requires WindowStyle = WindowStyle.None and AllowsTransparency = true");

        SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_EXSTYLE,!DetachedShowInTaskbarNoOwner && DetachedShowInTaskbar ? (nint)WindowStylesEx.WS_EX_APPWINDOW : 0);

        Overlay = standAloneOverlay;
        Overlay.IsVisibleChanged += OverlayStandAlone_IsVisibleChanged;
        OverlayStandAlone_IsVisibleChanged(null, new());
    }
    private void OverlayStandAlone_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Overlay.IsVisible)
            { Surface.Show(); ShowWindow(OverlayHandle, 2); ShowWindow(OverlayHandle, 3); }
        else
            Surface.Hide();
    }

    public static void Resize(Window Window, FlyleafHost fl, Point p, int resizingSide, double ratio = 0.0)
    {
        double WindowWidth  = Window.ActualWidth;
        double WindowHeight = Window.ActualHeight;
        double WindowLeft   = Window.Left;
        double WindowTop    = Window.Top;

        if (fl.IsAttached)
        {
            var t = fl.Owner.PointToScreen(new Point(0,0));
            WindowLeft -= t.X;
            WindowTop  -= t.Y;
        }

        if (resizingSide == 2 || resizingSide == 3 || resizingSide == 6)
        {
            p.X += 5;

            WindowWidth = p.X > Window.MinWidth ?
                p.X < Window.MaxWidth ? p.X : Window.MaxWidth :
                Window.MinWidth;
        }
        else if (resizingSide == 1 || resizingSide == 4 || resizingSide == 5)
        {
            p.X -= 5;
            double temp = Window.ActualWidth - p.X;
            if (temp > Window.MinWidth && temp < Window.MaxWidth)
            {
                WindowWidth = temp;
                WindowLeft += p.X;
            }
        }

        if (resizingSide == 2 || resizingSide == 4 || resizingSide == 8)
        {
            p.Y += 5;

            if (p.Y > Window.MinHeight)
            {
                WindowHeight = p.Y < Window.MaxHeight ? p.Y : Window.MaxHeight;
            }
            else
                return;
        }
        else if (resizingSide == 1 || resizingSide == 3 || resizingSide == 7)
        {
            if (ratio != 0 && resizingSide != 7)
            {
                double temp = WindowWidth / ratio;
                if (temp > Window.MinHeight && temp < Window.MaxHeight)
                    WindowTop += Window.ActualHeight - temp;
                else
                    return;
            }
            else
            {
                p.Y -= 5;
                double temp = Window.ActualHeight - p.Y;
                if (temp > Window.MinHeight && temp < Window.MaxHeight)
                {
                    WindowHeight = temp;
                    WindowTop += p.Y;                
                }
                else
                    return;
            }
        }

        if (ratio == 0)
            fl.SetRect(new(WindowLeft, WindowTop, WindowWidth, WindowHeight));
        else if (resizingSide == 7 || resizingSide == 8)
            fl.SetRect(new(WindowLeft, WindowTop, WindowHeight * ratio, WindowHeight));
        else
            fl.SetRect(new(WindowLeft, WindowTop, WindowWidth, WindowWidth / ratio));
    }
    public static int ResizeSides(Window Window, Point p, int ResizeSensitivity, CornerRadius cornerRadius)
    {
        if (p.X <= ResizeSensitivity + (cornerRadius.TopLeft / 2) && p.Y <= ResizeSensitivity + (cornerRadius.TopLeft / 2))
        {
            Window.Cursor = Cursors.SizeNWSE;
            return 1;
        }
        else if (p.X + ResizeSensitivity + (cornerRadius.BottomRight / 2) >= Window.ActualWidth && p.Y + ResizeSensitivity + (cornerRadius.BottomRight / 2) >= Window.ActualHeight)
        {
            Window.Cursor = Cursors.SizeNWSE;
            return 2;
        }
        else if (p.X + ResizeSensitivity + (cornerRadius.TopRight / 2) >= Window.ActualWidth && p.Y <= ResizeSensitivity + (cornerRadius.TopRight / 2))
        {
            Window.Cursor = Cursors.SizeNESW;
            return 3;
        }
        else if (p.X <= ResizeSensitivity + (cornerRadius.BottomLeft / 2) && p.Y + ResizeSensitivity + (cornerRadius.BottomLeft / 2)  >= Window.ActualHeight)
        {
            Window.Cursor = Cursors.SizeNESW;
            return 4;
        }
        else if (p.X <= ResizeSensitivity)
        {
            Window.Cursor = Cursors.SizeWE;
            return 5;
        }
        else if (p.X + ResizeSensitivity >= Window.ActualWidth)
        {
            Window.Cursor = Cursors.SizeWE;
            return 6;
        }
        else if (p.Y <= ResizeSensitivity)
        {
            Window.Cursor = Cursors.SizeNS;
            return 7;
        }
        else if (p.Y + ResizeSensitivity >= Window.ActualHeight)
        {
            Window.Cursor = Cursors.SizeNS;
            return 8;
        }
        else
        {
            Window.Cursor = Cursors.Arrow;
            return 0;
        }
    }
    #endregion

    #region Constructors
    static FlyleafHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlyleafHost), new FrameworkPropertyMetadata(typeof(FlyleafHost)));
        ContentProperty.OverrideMetadata(typeof(FlyleafHost), new FrameworkPropertyMetadata(null, new CoerceValueCallback(OnContentChanging)));
    }
    public FlyleafHost()
    {
        UniqueId = idGenerator++;
        isDesginMode = DesignerProperties.GetIsInDesignMode(this);
        if (isDesginMode)
            return;

        MarginTarget= this;
        Log         = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost NP] ");
        Loaded     += Host_Loaded; // Initialized event ??
        Unloaded   += Host_Unloaded;
        DataContextChanged 
                   += Host_DataContextChanged;

        SetSurface();
    }
    public FlyleafHost(Window standAloneOverlay)
    {
        UniqueId = idGenerator++;
        Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost NP] ");

        IsStandAlone = true;
        IsAttached = false;
        SetSurface();

        this.standAloneOverlay = standAloneOverlay;
        standAloneOverlay.Loaded += OverlayStandAlone_Loaded;
        if (standAloneOverlay.IsLoaded)
            OverlayStandAlone_Loaded(null, null);
    }
    #endregion

    #region Methods
    public virtual void SetPlayer(Player oldPlayer)
    {
        // De-assign old Player's Handle/FlyleafHost
        if (oldPlayer != null)
        {
            Log.Debug($"De-assign Player #{oldPlayer.PlayerId}");

            oldPlayer.Video.PropertyChanged -= Player_Video_PropertyChanged;
            oldPlayer.VideoDecoder.DestroySwapChain();
            oldPlayer.Host = null;
        }

        if (Player == null)
            return;

        Log.Prefix = ("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost #{Player.PlayerId}] ";

        // De-assign new Player's Handle/FlyleafHost
        Player.Host?.Player_Disposed();    

        if (Player == null) // We might just de-assign our Player
            return;
        
        // Assign new Player's (Handle/FlyleafHost)
        Log.Debug($"Assign Player #{Player.PlayerId}");

        Player.Host = this;
        Player.Activity.Timeout = ActivityTimeout;
        if (Player.renderer != null) // TBR: using as AudioOnly with a Control*
            Player.renderer.CornerRadius = IsFullScreen ? zeroCornerRadius : CornerRadius;

        if (CornerRadius == zeroCornerRadius)
            Surface.Background = new SolidColorBrush(Player.Config.Video.BackgroundColor);
        else
            ((Border)Surface.Content).Background = new SolidColorBrush(Player.Config.Video.BackgroundColor);

        Player.VideoDecoder.CreateSwapChain(SurfaceHandle);
        Player.Video.PropertyChanged += Player_Video_PropertyChanged;
        if (KeepRatioOnResize && Player.Video.AspectRatio.Value > 0)
            CurResizeRatio = Player.Video.AspectRatio.Value;
    }
    public virtual void SetSurface()
    {
        // Required for some reason (WindowStyle.None will not be updated with our style)
        Surface = new();
        Surface.Width = Surface.Height = 1; // Will be set on loaded
        Surface.WindowStyle = WindowStyle.None; 
        Surface.ResizeMode  = ResizeMode.NoResize;

        if (CornerRadius == zeroCornerRadius)
            Surface.Background  = Brushes.Black;
        else
        {
            Surface.AllowsTransparency  = true;
            Surface.Background          = Brushes.Transparent;
            Surface.Content             = new Border()
            {
                Background              = Brushes.Black,
                HorizontalAlignment     = HorizontalAlignment.Stretch,
                VerticalAlignment       = VerticalAlignment.Stretch,
                CornerRadius            = CornerRadius,
            };
        }

        SurfaceHandle   = new WindowInteropHelper(Surface).EnsureHandle();
        SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_STYLE, NONE_STYLE | (nint)WindowStyles.WS_CHILD); 
        SetWindowPos(SurfaceHandle, IntPtr.Zero, 0, 0, 0, 0, (uint)(SetWindowPosFlags.SWP_FRAMECHANGED | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOOWNERZORDER));

        Surface.Closed      += Surface_Closed;
        Surface.Closing     += Surface_Closing;
        Surface.KeyDown     += Surface_KeyDown;
        Surface.KeyUp       += Surface_KeyUp;
        Surface.Drop        += Surface_Drop;
        Surface.DragEnter   += Surface_DragEnter;
        Surface.StateChanged+= Surface_StateChanged;
        Surface.SizeChanged += SetRectOverlay;

        SetMouseSurface();

        Surface.AllowDrop =
            OpenOnDrop == AvailableWindows.Surface || OpenOnDrop == AvailableWindows.Both ||
            SwapOnDrop == AvailableWindows.Surface || SwapOnDrop == AvailableWindows.Both;
    }
    public virtual void SetOverlay()
    {
        if (Overlay == null)
            return;

        OverlayHandle   = new WindowInteropHelper(Overlay).EnsureHandle();

        if (IsStandAlone)
        {
            SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_STYLE, NONE_STYLE);
            SetWindowPos(SurfaceHandle, IntPtr.Zero, (int)Overlay.Left, (int)Overlay.Top, (int)Overlay.ActualWidth, (int)Overlay.ActualHeight,
                (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));

            Surface.Title       = Overlay.Title;
            Surface.Icon        = Overlay.Icon;
            Surface.MinHeight   = Overlay.MinHeight;
            Surface.MaxHeight   = Overlay.MaxHeight;
            Surface.MinWidth    = Overlay.MinWidth;
            Surface.MaxWidth    = Overlay.MaxWidth;
        }
        else
        {
            SetWindowPos(OverlayHandle, IntPtr.Zero, 0, 0, (int)Surface.ActualWidth, (int)Surface.ActualHeight,
                (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));

            Overlay.Resources   = Resources;
            Overlay.DataContext = this; // TBR: or this.DataContext?
            Overlay.ContentRendered += OverlayAttached_ContentRendered; // To set the size from overlay when this.RenderSize is not defined
        }

        Overlay.Background      = Brushes.Transparent;
        Overlay.ShowInTaskbar   = false;
        Overlay.Owner           = Surface;
        SetParent(OverlayHandle, SurfaceHandle);
        SetWindowLong(OverlayHandle, (int)WindowLongFlags.GWL_STYLE, NONE_STYLE | (nint)WindowStyles.WS_CHILD);
        SetWindowPos(OverlayHandle, IntPtr.Zero, 0, 0, 0, 0, (uint)(SetWindowPosFlags.SWP_FRAMECHANGED | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOOWNERZORDER));
        SetRectOverlay(null, null);

        Overlay.KeyUp       += Overlay_KeyUp;
        Overlay.KeyDown     += Overlay_KeyDown;
        Overlay.Closed      += Overlay_Closed;
        Overlay.Drop        += Overlay_Drop;
        Overlay.DragEnter   += Overlay_DragEnter;

        SetMouseOverlay();

        // Owner will close the overlay
        Overlay.KeyDown += (o, e) => { if (e.Key == Key.System && e.SystemKey == Key.F4) Surface?.Focus(); };

        Overlay.AllowDrop =
            OpenOnDrop == AvailableWindows.Overlay || OpenOnDrop == AvailableWindows.Both ||
            SwapOnDrop == AvailableWindows.Overlay || SwapOnDrop == AvailableWindows.Both;

        if (Surface.IsVisible)
            Overlay.Show();
    }
    private void SetMouseSurface()
    {
        if (Surface == null)
            return;

        if ((MouseBindings == AvailableWindows.Surface || MouseBindings == AvailableWindows.Both) && !isMouseBindingsSubscribedSurface)
        {
            surfaceMouseUp ??= new MouseButtonEventHandler((o, e) => Surface.ReleaseMouseCapture());
            Mouse.AddPreviewMouseUpOutsideCapturedElementHandler(Surface, surfaceMouseUp);
            Surface.LostMouseCapture    += ReleaseMouseCapture12;
            Surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
            Surface.MouseLeftButtonUp   += ReleaseMouseCapture1;
            Surface.MouseWheel          += Surface_MouseWheel;
            Surface.MouseMove           += Surface_MouseMove;
            Surface.MouseLeave          += Surface_MouseLeave;
            Surface.MouseDoubleClick    += Surface_MouseDoubleClick;
            isMouseBindingsSubscribedSurface = true;
        }
        else if (MouseBindings != AvailableWindows.Surface && MouseBindings != AvailableWindows.Both && isMouseBindingsSubscribedSurface)
        {
            Mouse.RemovePreviewMouseUpOutsideCapturedElementHandler(Surface, surfaceMouseUp);
            Surface.LostMouseCapture    -= ReleaseMouseCapture2;
            Surface.MouseLeftButtonDown -= Surface_MouseLeftButtonDown;
            Surface.MouseLeftButtonUp   -= ReleaseMouseCapture1;
            Surface.MouseWheel          -= Surface_MouseWheel;
            Surface.MouseMove           -= Surface_MouseMove;
            Surface.MouseLeave          -= Surface_MouseLeave;
            Surface.MouseDoubleClick    -= Surface_MouseDoubleClick;
            isMouseBindingsSubscribedSurface = false;
        }
    }
    private void SetMouseOverlay()
    {
        if (Overlay == null)
            return;

        if ((MouseBindings == AvailableWindows.Overlay || MouseBindings == AvailableWindows.Both) && !isMouseBindingsSubscribedOverlay)
        {
            overlayMouseUp ??= new MouseButtonEventHandler((o, e) => Overlay.ReleaseMouseCapture());
            Mouse.AddPreviewMouseUpOutsideCapturedElementHandler(Overlay, overlayMouseUp);
            Overlay.LostMouseCapture    += ReleaseMouseCapture2;

            Overlay.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;
            Overlay.MouseLeftButtonUp   += ReleaseMouseCapture1;
            Overlay.MouseWheel          += Overlay_MouseWheel;
            Overlay.MouseMove           += Overlay_MouseMove;
            Overlay.MouseLeave          += Overlay_MouseLeave;
            Overlay.MouseDoubleClick    += Overlay_MouseDoubleClick;
            isMouseBindingsSubscribedOverlay = true;
        }
        else if (MouseBindings != AvailableWindows.Overlay && MouseBindings != AvailableWindows.Both && isMouseBindingsSubscribedOverlay)
        {
            Mouse.RemovePreviewMouseUpOutsideCapturedElementHandler(Overlay, overlayMouseUp);
            Overlay.LostMouseCapture    -= ReleaseMouseCapture2;
            Overlay.MouseLeftButtonDown -= Overlay_MouseLeftButtonDown;
            Overlay.MouseLeftButtonUp   -= ReleaseMouseCapture1;
            Overlay.MouseWheel          -= Overlay_MouseWheel;
            Overlay.MouseMove           -= Overlay_MouseMove;
            Overlay.MouseLeave          -= Overlay_MouseLeave;
            Overlay.MouseDoubleClick    -= Overlay_MouseDoubleClick;
            isMouseBindingsSubscribedOverlay = false;
        }
    }

    public virtual void Attach()
    {
        if (IsFullScreen)
            IsFullScreen = false;
        else
            rectDetachedLast = new Rect(Surface.Left, Surface.Top, Surface.Width, Surface.Height);

        if (DetachedTopMost)
            Surface.Topmost = false;

        Surface.MinWidth    = MinWidth;
        Surface.MinHeight   = MinHeight;
        Surface.MaxWidth    = MaxWidth;
        Surface.MaxHeight   = MaxHeight;

        SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_STYLE, NONE_STYLE | (nint)WindowStyles.WS_CHILD);
        Surface.Owner = Owner;
        SetParent(SurfaceHandle, OwnerHandle);

        rectInitLast = rectIntersectLast = rectRandom;
        Host_LayoutUpdated(null, null);

        // Keep keyboard focus
        if (Overlay != null && Overlay.IsVisible && Overlay.IsKeyboardFocusWithin)
        {
            Overlay.Activate();
            Overlay.Focus();
        }
        else if (Surface.IsVisible)
        {
            Owner.Activate();
            Surface.Activate();
            Surface.Focus();
        }
    }
    public virtual void Detach()
    {
        if (IsFullScreen)
            IsFullScreen = false;

        Surface.MinWidth    = DetachedMinWidth;
        Surface.MinHeight   = DetachedMinHeight;
        Surface.MaxWidth    = DetachedMaxWidth;
        Surface.MaxHeight   = DetachedMaxHeight;

        // Calculate Size
        var newSize = DetachedRememberSize && rectDetachedLast != Rect.Empty
            ? new Size(rectDetachedLast.Width, rectDetachedLast.Height)
            : DetachedFixedSize;
        
        // Calculate Position
        Point newPos;
        if (DetachedRememberPosition && rectDetachedLast != Rect.Empty)
            newPos = new Point(rectDetachedLast.X, rectDetachedLast.Y);
        else
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)Surface.Top, (int)Surface.Left)).Bounds;

            switch (DetachedPosition)
            {
                case DetachedPositionOptions.TopLeft:
                    newPos = new Point(screen.Left, screen.Top);
                    break;
                case DetachedPositionOptions.TopCenter:
                    newPos = new Point(screen.Left + (screen.Width / 2) - (newSize.Width / 2), screen.Top);
                    break;

                case DetachedPositionOptions.TopRight:
                    newPos = new Point(screen.Left + screen.Width - newSize.Width, screen.Top);
                    break;

                case DetachedPositionOptions.CenterLeft:
                    newPos = new Point(screen.Left, screen.Top + (screen.Height / 2) - (newSize.Height / 2));
                    break;

                case DetachedPositionOptions.CenterCenter:
                    newPos = new Point(screen.Left + (screen.Width / 2) - (newSize.Width / 2), screen.Top + (screen.Height / 2) - (newSize.Height / 2));
                    break;

                case DetachedPositionOptions.CenterRight:
                    newPos = new Point(screen.Left + screen.Width - newSize.Width, screen.Top + (screen.Height / 2) - (newSize.Height / 2));
                    break;

                case DetachedPositionOptions.BottomLeft:
                    newPos = new Point(screen.Left, screen.Top + screen.Height - newSize.Height);
                    break;

                case DetachedPositionOptions.BottomCenter:
                    newPos = new Point(screen.Left + (screen.Width / 2) - (newSize.Width / 2), screen.Top + screen.Height - newSize.Height);
                    break;

                case DetachedPositionOptions.BottomRight:
                    newPos = new Point(screen.Left + screen.Width - newSize.Width, screen.Top + screen.Height - newSize.Height);
                    break;

                case DetachedPositionOptions.Custom:
                    newPos = DetachedFixedPosition;
                    break;

                default:
                    newPos = new Point(Surface.Left, Surface.Top);
                    break;
            }

            newPos.X += DetachedPositionMargin.Left - DetachedPositionMargin.Right;
            newPos.Y += DetachedPositionMargin.Top - DetachedPositionMargin.Bottom;
        }

        Rect final = new(newPos.X, newPos.Y, newSize.Width, newSize.Height);

        // Detach (Parent=Null, Owner=Null ?, ShowInTaskBar?, TopMost?)
        SetParent(SurfaceHandle, IntPtr.Zero);
        SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_STYLE, NONE_STYLE); // TBR (also in Attach/FullScren): Needs to be after SetParent. when detached and trying to close the owner will take two clicks (like mouse capture without release) //SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_STYLE, GetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_STYLE) & ~(nint)WindowStyles.WS_CHILD);

        if (DetachedShowInTaskbarNoOwner)
            Surface.Owner = null;
        
        if (DetachedTopMost)
            Surface.Topmost = true;

        SetRect(final);
        ResetVisibleRect();

        // Keep keyboard focus
        if (Overlay != null && Overlay.IsVisible && Overlay.IsKeyboardFocusWithin)
        {
            Overlay.Activate();
            Overlay.Focus();
        }
        else if (Surface.IsVisible)
        {
            Surface.Activate();
            Surface.Focus();
        }
    }

    public void RefreshNormalFullScreen()
    {
        if (IsFullScreen)
        {
            if (IsAttached)
            {
                ResetVisibleRect();
                SetParent(SurfaceHandle, IntPtr.Zero);
                SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_STYLE, NONE_STYLE);

                if (DetachedShowInTaskbarNoOwner)
                    Surface.Owner = null;
            }

            if (Player != null)
                Player.renderer.CornerRadius = zeroCornerRadius;
            
            if (CornerRadius != zeroCornerRadius)
                ((Border)Surface.Content).CornerRadius = zeroCornerRadius;

            Surface.WindowState = WindowState.Maximized;

            if (Overlay != null)
            {
                Overlay.WindowState = WindowState.Maximized; // possible not set this?
                SetWindowPos(OverlayHandle, IntPtr.Zero, 0, 0, (int)Surface.ActualWidth, (int)Surface.ActualHeight, 
                    (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE)); // changes the pos/size for some reason
            }
        }
        else
        {
            Surface.WindowState = WindowState.Normal;

            if (Overlay != null)
                Overlay.WindowState = WindowState.Normal;

            if (IsAttached)
            {
                SetWindowLong(SurfaceHandle, (int)WindowLongFlags.GWL_STYLE, NONE_STYLE | (nint)WindowStyles.WS_CHILD);
                Surface.Owner = Owner;
                SetParent(SurfaceHandle, OwnerHandle);
                SetWindowPos(SurfaceHandle, IntPtr.Zero, 0, 0, 0, 0, (uint)(SetWindowPosFlags.SWP_FRAMECHANGED | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOOWNERZORDER));

                rectInitLast = rectIntersectLast = Rect.Empty;
                Host_LayoutUpdated(null, null);
                Owner.Activate();
            }

            SetWindowPos(OverlayHandle, IntPtr.Zero, 0, 0, (int)Surface.ActualWidth, (int)Surface.ActualHeight, 
                (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE)); // changes the pos/size for some reason

            if (Surface.Topmost) // loses it
            {
                Surface.Topmost = false;
                Surface.Topmost = true;
            }
            
            if (Player != null)
                Player.renderer.CornerRadius = CornerRadius;

            if (CornerRadius != zeroCornerRadius)
                ((Border)Surface.Content).CornerRadius = CornerRadius;
        }
    }
    public void SetRect(Rect rect)
        => SetWindowPos(SurfaceHandle, IntPtr.Zero, (int)Math.Round(rect.X * DpiX), (int)Math.Round(rect.Y * DpiY), (int)Math.Round(rect.Width * DpiX), (int)Math.Round(rect.Height * DpiY), 
            (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));

    private void SetRectOverlay(object sender, SizeChangedEventArgs e)
    {
        if (Overlay != null)
            SetWindowPos(OverlayHandle, IntPtr.Zero, 0, 0, (int)Surface.ActualWidth, (int)Surface.ActualHeight, 
                (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOACTIVATE));
    }

    public void ResetVisibleRect()
    {
        SetWindowRgn(SurfaceHandle, IntPtr.Zero, true);
        if (Overlay != null)
            SetWindowRgn(OverlayHandle, IntPtr.Zero, true);
    }
    public void SetVisibleRect(Rect rect)
    {
        SetWindowRgn(SurfaceHandle, CreateRectRgn((int)(rect.X * DpiX), (int)(rect.Y * DpiY), (int)(rect.Right * DpiX), (int)(rect.Bottom * DpiY)), true);
        if (Overlay != null)
            SetWindowRgn(OverlayHandle, CreateRectRgn((int)(rect.X * DpiX), (int)(rect.Y * DpiY), (int)(rect.Right * DpiX), (int)(rect.Bottom * DpiY)), true);
    }

    /// <summary>
    /// Disposes the Surface and Overlay Windows and de-assigns the Player
    /// </summary>
    public void Dispose()
    {
        lock (this)
        {
            if (Disposed)
                return;

            Disposed = true;
            
            // Disposes SwapChain Only
            Player = null;

            if (Overlay != null)
            {
                Overlay.IsVisibleChanged -= OverlayStandAlone_IsVisibleChanged;
                Overlay.MouseLeave -= Overlay_MouseLeave;
            }

            if (Surface != null)
            {
                Surface.MouseMove -= Surface_MouseMove;
                Surface.MouseLeave -= Surface_MouseLeave;

                // If not shown yet app will not close properly
                if (!surfaceClosed)
                {
                    Surface.Width = Surface.Height = 1;
                    Surface.Show();
                    if (!overlayClosed)
                        Overlay?.Show();
                    Surface.Close();
                }
            }

            SurfaceHandle   = IntPtr.Zero;
            OverlayHandle   = IntPtr.Zero;
            OwnerHandle     = IntPtr.Zero;

            Surface = null;
            Overlay = null;
            //if (Owner != null)
                //Owner.Close();

            Owner   = null;
        }
    }

    public bool Player_CanHideCursor() => (Surface != null && Surface.IsActive) || (Overlay != null && Overlay.IsActive);
    public bool Player_GetFullScreen() => IsFullScreen;
    public void Player_SetFullScreen(bool value) => IsFullScreen = value;
    public void Player_Disposed() => UIInvokeIfRequired(() => Player = null);
    #endregion
}

public enum AvailableWindows
{
    None, Surface, Overlay, Both
}

public enum AttachedDragMoveOptions
{
    None, Surface, Overlay, Both, SurfaceOwner, OverlayOwner, BothOwner
}

public enum DetachedPositionOptions
{
    Custom, TopLeft, TopCenter, TopRight, CenterLeft, CenterCenter, CenterRight, BottomLeft, BottomCenter, BottomRight
}