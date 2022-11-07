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

using Binding = System.Windows.Data.Binding;
using Brushes = System.Windows.Media.Brushes;
using Panel = System.Windows.Controls.Panel;

namespace FlyleafLib.Controls.WPF
{
    public class FlyleafHost : ContentControl, IDisposable
    {
        /* -= FlyleafHost Properties Notes =-

            Player							[Can be changed, can be null]

            Surface							[ReadOnly / Required]
            Overlay							[AutoCreated OnContentChanged | Provided directly | Provided in Stand Alone Constructor]

            Content							[Overlay's Content]
            DetachedContent					[Host's actual content]

            DataContext						[Set by the user or default inheritance]
            HostDataContext					[Will be set Sync with DataContext as helper to Overlay when we pass this as Overlay's DataContext]

            OpenOnDrop						[None, Surface, Overlay, Both]		| Requires AllowDrop and Player
            SwapOnDrop						[None, Surface, Overlay, Both]		| Requires AllowDrop and Player

            SwapDragEnterWithShift			[None, Surface, Overlay, Both]		| Requires SwapOnDrop and Player
            ToggleFullScreenOnDoubleClick	[None, Surface, Overlay, Both]

            PanMoveWithCtrl					[None, Surface, Overlay, Both]		| Requires Player and VideoStream Opened
            PanZoomWithCtrlWheel			[None, Surface, Overlay, Both]		| Requires Player and VideoStream Opened

            AttachedDragMove				[None, Surface, Overlay, Both, SurfaceOwner, OverlayOwner, BothOwner]
            DetachedDragMove				[None, Surface, Overlay, Both]

            AttachedResize					[None, Surface, Overlay, Both]
            DetachedResize					[None, Surface, Overlay, Both]
            KeepRatioOnResize				[False, True]
            CurResizeRatio                  [0 if not Keep Ratio or Player's aspect ratio]
            ResizeSensitivity               Pixels sensitivity from the window's edges

            DetachedPosition				[Custom, TopLeft, TopCenter, TopRight, CenterLeft, CenterCenter, CenterRight, BottomLeft, BottomCenter, BottomRight]
            DetachedPositionMargin			[X, Y, CX, CY]						| Does not affect the Size / Eg. No point to provide both X/CX 	
            DetachedFixedPosition			[X, Y]								| If not set same as attached, if remember only first time
            DetachedFixedSize				[CX, CY]							| If not set same as attached, if remember only first time
            DetachedRememberPosition		[False, True]
            DetachedRememberSize			[False, True]
            DetachedTopMost					[False, True] (Surfaces Only Required?)

            KeyBindings						[None, Surface, Overlay, Both]

            ActivityTimeout					[0: Disabled]						| Requires Player?
            ActivityRefresh?				[None, Surface, Overlay, Both]		| MouseMove / MouseDown / KeyUp

            PassWheelToOwner?				[None, Surface, Overlay, Both]		| When host belongs to ScrollViewer

            IsAttached
            IsFullScreen
            IsMinimized
            IsResizing						[ReadOnly]
            IsSwapping						[ReadOnly]
            IsStandAlone					[ReadOnly]
         */

        /* TODO
         * 1) The surface / overlay events code is repeated
         * 2) PassWheelToOwner (Related with LayoutUpdate performance / ScrollViewer) / ActivityRefresh
         * 3) Review Content/DetachedContent/Template logic
         * 4) Attach to different Owner (Load/Unload) and change Overlay?
         * 5) WindowStates having issues with Owner Window, should prevent user to change states directly (we currently not forcing overlay -> surface states)
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

        static int idGenerator;
        bool isSurfaceClosing;
        static bool isDesginMode;
        Grid dMain, dHost, dSurface, dOverlay; // Design Mode Grids
        bool preventContentUpdate;
        int panPrevX, panPrevY;
        bool ownedRestoreToMaximize;
        WindowState prevSurfaceWindowState = WindowState.Normal;

        Matrix matrix;
        Point zeroPoint = new Point(0, 0);
        Point mouseLeftDownPoint = new Point(0, 0);
        Point mouseMoveLastPoint = new Point(0, 0);

        static Rect rectRandom = new Rect(1, 2, 3, 4);
        Rect rectDetachedLast = Rect.Empty;
        Rect rectIntersectLast = rectRandom;
        Rect rectInitLast = rectRandom;

        private class FlyleafHostDropWrap { public FlyleafHost FlyleafHost; } // To allow non FlyleafHosts to drag & drop
        protected readonly LogHandler Log;
        #endregion

        #region Dependency Properties
        public AvailableWindows OpenOnDrop
        {
            get { return (AvailableWindows)GetValue(OpenOnDropProperty); }
            set { SetValue(OpenOnDropProperty, value); }
        }
        public static readonly DependencyProperty OpenOnDropProperty =
            DependencyProperty.Register(nameof(OpenOnDrop), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface, new PropertyChangedCallback(DropChanged)));
        
        public AvailableWindows SwapOnDrop
        {
            get { return (AvailableWindows)GetValue(SwapOnDropProperty); }
            set { SetValue(SwapOnDropProperty, value); }
        }
        public static readonly DependencyProperty SwapOnDropProperty =
            DependencyProperty.Register(nameof(SwapOnDrop), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface, new PropertyChangedCallback(DropChanged)));

        public AvailableWindows SwapDragEnterWithShift
        {
            get { return (AvailableWindows)GetValue(SwapDragEnterWithShiftProperty); }
            set { SetValue(SwapDragEnterWithShiftProperty, value); }
        }
        public static readonly DependencyProperty SwapDragEnterWithShiftProperty =
            DependencyProperty.Register(nameof(SwapDragEnterWithShift), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

        public AvailableWindows ToggleFullScreenOnDoubleClick
        {
            get { return (AvailableWindows)GetValue(ToggleFullScreenOnDoubleClickProperty); }
            set { SetValue(ToggleFullScreenOnDoubleClickProperty, value); }
        }
        public static readonly DependencyProperty ToggleFullScreenOnDoubleClickProperty =
            DependencyProperty.Register(nameof(ToggleFullScreenOnDoubleClick), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

        public AvailableWindows PanMoveWithCtrl
        {
            get { return (AvailableWindows)GetValue(PanMoveWithCtrlProperty); }
            set { SetValue(PanMoveWithCtrlProperty, value); }
        }
        public static readonly DependencyProperty PanMoveWithCtrlProperty =
            DependencyProperty.Register(nameof(PanMoveWithCtrl), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

        public AvailableWindows PanZoomWithCtrlWheel
        {
            get { return (AvailableWindows)GetValue(PanZoomWithCtrlWheelProperty); }
            set { SetValue(PanZoomWithCtrlWheelProperty, value); }
        }
        public static readonly DependencyProperty PanZoomWithCtrlWheelProperty =
            DependencyProperty.Register(nameof(PanZoomWithCtrlWheel), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));


        public AttachedDragMoveOptions AttachedDragMove
        {
            get { return (AttachedDragMoveOptions)GetValue(AttachedDragMoveProperty); }
            set { SetValue(AttachedDragMoveProperty, value); }
        }
        public static readonly DependencyProperty AttachedDragMoveProperty =
            DependencyProperty.Register(nameof(AttachedDragMove), typeof(AttachedDragMoveOptions), typeof(FlyleafHost), new PropertyMetadata(AttachedDragMoveOptions.Surface));

        public AvailableWindows DetachedDragMove
        {
            get { return (AvailableWindows)GetValue(DetachedDragMoveProperty); }
            set { SetValue(DetachedDragMoveProperty, value); }
        }
        public static readonly DependencyProperty DetachedDragMoveProperty =
            DependencyProperty.Register(nameof(DetachedDragMove), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));


        public AvailableWindows AttachedResize
        {
            get { return (AvailableWindows)GetValue(AttachedResizeProperty); }
            set { SetValue(AttachedResizeProperty, value); }
        }
        public static readonly DependencyProperty AttachedResizeProperty =
            DependencyProperty.Register(nameof(AttachedResize), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

        public AvailableWindows DetachedResize
        {
            get { return (AvailableWindows)GetValue(DetachedResizeProperty); }
            set { SetValue(DetachedResizeProperty, value); }
        }
        public static readonly DependencyProperty DetachedResizeProperty =
            DependencyProperty.Register(nameof(DetachedResize), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

        public bool KeepRatioOnResize
        {
            get { return (bool)GetValue(KeepRatioOnResizeProperty); }
            set { SetValue(KeepRatioOnResizeProperty, value); }
        }
        public static readonly DependencyProperty KeepRatioOnResizeProperty =
            DependencyProperty.Register(nameof(KeepRatioOnResize), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnKeepRatioOnResizeChanged)));

        public float CurResizeRatio
        {
            get { return (float)GetValue(CurResizeRatioProperty); }
            private set { SetValue(CurResizeRatioProperty, value); }
        }
        public static readonly DependencyProperty CurResizeRatioProperty =
            DependencyProperty.Register(nameof(CurResizeRatio), typeof(float), typeof(FlyleafHost), new PropertyMetadata((float)0, new PropertyChangedCallback(OnCurResizeRatioChanged)));

        public int ResizeSensitivity
        {
            get { return (int)GetValue(ResizeSensitivityProperty); }
            set { SetValue(ResizeSensitivityProperty, value); }
        }
        public static readonly DependencyProperty ResizeSensitivityProperty =
            DependencyProperty.Register(nameof(ResizeSensitivity), typeof(int), typeof(FlyleafHost), new PropertyMetadata(6));


        public DetachedPositionOptions DetachedPosition
        {
            get { return (DetachedPositionOptions)GetValue(DetachedPositionProperty); }
            set { SetValue(DetachedPositionProperty, value); }
        }
        public static readonly DependencyProperty DetachedPositionProperty =
            DependencyProperty.Register(nameof(DetachedPosition), typeof(DetachedPositionOptions), typeof(FlyleafHost), new PropertyMetadata(DetachedPositionOptions.BottomRight));

        public Thickness DetachedPositionMargin
        {
            get { return (Thickness)GetValue(DetachedPositionMarginProperty); }
            set { SetValue(DetachedPositionMarginProperty, value); }
        }
        public static readonly DependencyProperty DetachedPositionMarginProperty =
            DependencyProperty.Register(nameof(DetachedPositionMargin), typeof(Thickness), typeof(FlyleafHost), new PropertyMetadata(new Thickness(0, 0, 40, 40)));

        public Point DetachedFixedPosition
        {
            get { return (Point)GetValue(DetachedFixedPositionProperty); }
            set { SetValue(DetachedFixedPositionProperty, value); }
        }
        public static readonly DependencyProperty DetachedFixedPositionProperty =
            DependencyProperty.Register(nameof(DetachedFixedPosition), typeof(Point), typeof(FlyleafHost), new PropertyMetadata(new Point()));

        public Size DetachedFixedSize
        {
            get { return (Size)GetValue(DetachedFixedSizeProperty); }
            set { SetValue(DetachedFixedSizeProperty, value); }
        }
        public static readonly DependencyProperty DetachedFixedSizeProperty =
            DependencyProperty.Register(nameof(DetachedFixedSize), typeof(Size), typeof(FlyleafHost), new PropertyMetadata(new Size(300, 200)));

        public bool DetachedRememberPosition
        {
            get { return (bool)GetValue(DetachedRememberPositionProperty); }
            set { SetValue(DetachedRememberPositionProperty, value); }
        }
        public static readonly DependencyProperty DetachedRememberPositionProperty =
            DependencyProperty.Register(nameof(DetachedRememberPosition), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

        public bool DetachedRememberSize
        {
            get { return (bool)GetValue(DetachedRememberSizeProperty); }
            set { SetValue(DetachedRememberSizeProperty, value); }
        }
        public static readonly DependencyProperty DetachedRememberSizeProperty =
            DependencyProperty.Register(nameof(DetachedRememberSize), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

        public bool DetachedTopMost
        {
            get { return (bool)GetValue(DetachedTopMostProperty); }
            set { SetValue(DetachedTopMostProperty, value); }
        }
        public static readonly DependencyProperty DetachedTopMostProperty =
            DependencyProperty.Register(nameof(DetachedTopMost), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnDetachedTopMostChanged)));

        public AvailableWindows KeyBindings
        {
            get { return (AvailableWindows)GetValue(KeyBindingsProperty); }
            set { SetValue(KeyBindingsProperty, value); }
        }
        public static readonly DependencyProperty KeyBindingsProperty =
            DependencyProperty.Register(nameof(KeyBindings), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));


        public int ActivityTimeout
        {
            get { return (int)GetValue(ActivityTimeoutProperty); }
            set { SetValue(ActivityTimeoutProperty, value); }
        }
        public static readonly DependencyProperty ActivityTimeoutProperty =
            DependencyProperty.Register(nameof(ActivityTimeout), typeof(int), typeof(FlyleafHost), new PropertyMetadata(0, new PropertyChangedCallback(OnActivityTimeoutChanged)));


        public bool IsAttached
        {
            get { return (bool)GetValue(IsAttachedProperty); }
            set { SetValue(IsAttachedProperty, value); }
        }
        public static readonly DependencyProperty IsAttachedProperty =
            DependencyProperty.Register(nameof(IsAttached), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true, new PropertyChangedCallback(OnIsAttachedChanged)));

        public bool IsMinimized
        {
            get { return (bool)GetValue(IsMinimizedProperty); }
            set { SetValue(IsMinimizedProperty, value); }
        }
        public static readonly DependencyProperty IsMinimizedProperty =
            DependencyProperty.Register(nameof(IsMinimized), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnIsMinimizedChanged)));

        public bool IsFullScreen
        {
            get { return (bool)GetValue(IsFullScreenProperty); }
            set { SetValue(IsFullScreenProperty, value); }
        }
        public static readonly DependencyProperty IsFullScreenProperty =
            DependencyProperty.Register(nameof(IsFullScreen), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnIsFullScreenChanged)));

        public bool IsResizing
        {
            get { return (bool)GetValue(IsResizingProperty); }
            private set { SetValue(IsResizingProperty, value); }
        }
        public static readonly DependencyProperty IsResizingProperty =
            DependencyProperty.Register(nameof(IsResizing), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));

        public bool IsStandAlone
        {
            get { return (bool)GetValue(IsStandAloneProperty); }
            private set { SetValue(IsStandAloneProperty, value); }
        }
        public static readonly DependencyProperty IsStandAloneProperty =
            DependencyProperty.Register(nameof(IsStandAlone), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));

        public bool IsSwapping
        {
            get { return (bool)GetValue(IsSwappingProperty); }
            private set { SetValue(IsSwappingProperty, value); }
        }
        public static readonly DependencyProperty IsSwappingProperty =
            DependencyProperty.Register(nameof(IsSwapping), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));


        public FrameworkElement MarginTarget
        {
            get { return (FrameworkElement)GetValue(MarginTargetProperty); }
            set { SetValue(MarginTargetProperty, value); }
        }
        public static readonly DependencyProperty MarginTargetProperty =
            DependencyProperty.Register(nameof(MarginTarget), typeof(FrameworkElement), typeof(FlyleafHost), new PropertyMetadata(null));

        public object HostDataContext
        {
            get { return (object)GetValue(HostDataContextProperty); }
            set { SetValue(HostDataContextProperty, value); }
        }
        public static readonly DependencyProperty HostDataContextProperty =
            DependencyProperty.Register(nameof(HostDataContext), typeof(object), typeof(FlyleafHost), new PropertyMetadata(null));

        public object DetachedContent
        {
            get { return GetValue(DetachedContentProperty); }
            set { SetValue(DetachedContentProperty, value); }
        }
        public static readonly DependencyProperty DetachedContentProperty =
            DependencyProperty.Register(nameof(DetachedContent), typeof(object), typeof(FlyleafHost), new PropertyMetadata(null, new PropertyChangedCallback(OnDetachedContentChanged)));

        public Player Player
        {
            get { return (Player)GetValue(PlayerProperty); }
            set { SetValue(PlayerProperty, value); }
        }
        public static readonly DependencyProperty PlayerProperty =
            DependencyProperty.Register(nameof(Player), typeof(Player), typeof(FlyleafHost), new PropertyMetadata(null, OnPlayerChanged));

        public Window Overlay
        {
            get { return (Window)GetValue(OverlayProperty); }
            set { SetValue(OverlayProperty, value); }
        }
        public static readonly DependencyProperty OverlayProperty =
            DependencyProperty.Register(nameof(Overlay), typeof(Window), typeof(FlyleafHost), new PropertyMetadata(null, new PropertyChangedCallback(OnOverlayChanged)));
        #endregion

        #region Events
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
            if (host.KeepRatioOnResize)
            {
                if (host.IsAttached)
                    host.Height = host.Width / host.CurResizeRatio;
                else
                    host.Surface.Height = host.Surface.Width / host.CurResizeRatio;
            }
        }
        private static void OnKeepRatioOnResizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (isDesginMode)
                return;

            FlyleafHost host = d as FlyleafHost;
            if (host.KeepRatioOnResize)
                host.CurResizeRatio = host.Player != null && host.Player.Video.AspectRatio.Value > 0 ? host.Player.Video.AspectRatio.Value : (float)(16.0/9.0);
            else
                host.CurResizeRatio = 0;
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

            if (host.Player != null)
                host.Player.IsFullScreen = host.IsFullScreen;
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

            if (host.IsAttached)
                host.Attach();
            else
                host.Detach();
        }
        private static void OnDetachedContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            FlyleafHost host = d as FlyleafHost;

            if (isDesginMode)
            {
                host.preventContentUpdate = true;
                host.Content = null;
                host.dHost.Children.Clear();
                if (e.NewValue != null)
                    host.dHost.Children.Add((UIElement)e.NewValue);
                host.Content = host.dMain;
                host.preventContentUpdate = false;
            }
            else
            {
                host.preventContentUpdate = true;
                host.Content = e.NewValue;
                host.preventContentUpdate = false;
            }
        }
        private static void OnActivityTimeoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            FlyleafHost host = d as FlyleafHost;

            if (host.Player == null)
                return;

            host.Player.Activity.Timeout = host.ActivityTimeout;
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
        protected override void OnContentChanged(object oldContent, object newContent) // Can be called before OnApplyTemplate!
        {
            //base.OnContentChanged(oldContent, newContent);
            //return;

            if (isDesginMode)
            {
                if (preventContentUpdate)
                {
                    base.OnContentChanged(oldContent, newContent);
                    return;
                }
                    

                preventContentUpdate = true;
                Content = null;
                dOverlay.Children.Clear();
                if (newContent != null)
                    dOverlay.Children.Add((UIElement)newContent);
                Content = dMain;
                preventContentUpdate = false;

                if (oldContent != dMain)
                    base.OnContentChanged(oldContent, dMain);

                return;
            }

            if (preventContentUpdate)
            {
                if (oldContent != DetachedContent)
                    base.OnContentChanged(oldContent, DetachedContent);

                return;
            }

            preventContentUpdate = true;
            Content = DetachedContent;
            preventContentUpdate = false;

            if (Overlay == null)
                Overlay = new Window();

            Overlay.Content = newContent;

            base.OnContentChanged(oldContent, DetachedContent);
        }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (isDesginMode)
                return;

            var overlayGrid = GetTemplateChild("PART_OverlayContent") as Grid;
            if (overlayGrid != null)
            {
                var overlayContent = overlayGrid.Children.Count > 0 ? overlayGrid.Children[0] : null;
                overlayGrid.Children.Clear();

                if (overlayContent != null)
                {
                    if (Overlay == null)
                        Overlay = new Window();

                    Overlay.Content = overlayContent;
                }
            }
        }

        private void Host_Unloaded(object sender, RoutedEventArgs e)
        {
            LayoutUpdated   -= Host_LayoutUpdated;
            IsVisibleChanged-= Host_IsVisibleChanged;
        }
        private void Host_Loaded(object sender, RoutedEventArgs e)
        {
            // TODO: Handle owner changed
            var owner = Window.GetWindow(this);
            //if (WPFOwner == owner) return;

            Owner =  owner;
            OwnerHandle = new WindowInteropHelper(Owner).EnsureHandle();
            matrix = PresentationSource.FromVisual(Owner).CompositionTarget.TransformFromDevice;
            HostDataContext = DataContext;
            Owner.LocationChanged += Owner_LocationChanged;

            ZOrderHandler.Register(Owner);

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
            if (IsAttached)
                Attach();
            else
            {
                Surface.Width = Surface.Height = 200;
                Surface.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Detach();
            }
            rectDetachedLast = Rect.Empty;

            LayoutUpdated   += Host_LayoutUpdated;
            IsVisibleChanged+= Host_IsVisibleChanged;

            //if (ControlRequiresPlayer == null && Overlay != null)
            //    FindIFlyleafHost((Visual)Overlay.Content);

            //if (ControlRequiresPlayer != null && Player != null)
            //    ControlRequiresPlayer.Player = Player;
        }
        private void Host_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) 
        {
            // TBR
            // 1. this.DataContext: FlyleafHost's DataContext will not be affected (Inheritance)
            // 2. Overlay.DataContext: Overlay's DataContext will be FlyleafHost itself
            // 3. Overlay.DataContext.HostDataContext: FlyleafHost's DataContext includes HostDataContext to access FlyleafHost's DataContext
            // 4. In case of Stand Alone will let the user to decide

            HostDataContext = DataContext;
        }
        private void Host_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsAttached)
                return;

            if (IsVisible)
            {
                Surface?.Show();
                Overlay?.Show();
            }
            else
            {
                Surface?.Hide();
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

            Rect rectInit = new Rect(matrix.Transform(PointToScreen(zeroPoint)), RenderSize);
            Rect rectIntersect = rectInit;

            FrameworkElement parent = this;
            while ((parent = VisualTreeHelper.GetParent(parent) as FrameworkElement) != null)
                rectIntersect.Intersect(new Rect(matrix.Transform(parent.PointToScreen(zeroPoint)), parent.RenderSize));

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

        private void Owner_LocationChanged(object sender, EventArgs e)
        {
            // This is no required from VS for some reason but we need it for release (Layout Update will not be called on release)
            var pos = matrix.Transform(PointToScreen(zeroPoint));
            if (IsAttached)
                SetWindowPos(SurfaceHandle, IntPtr.Zero, (int)(pos.X * DpiX), (int)(pos.Y * DpiY), 0, 0, (UInt32)(SetWindowPosFlags.SWP_NOZORDER | (SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE)));
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
            Overlay.WindowState = Surface.WindowState;
         
            switch (Surface.WindowState)
            {
                case WindowState.Maximized:

                    if (prevSurfaceWindowState == WindowState.Minimized)
                        ownedRestoreToMaximize = true;

                    IsFullScreen = true;
                    IsMinimized = false;

                    break;

                case WindowState.Normal:

                    IsFullScreen = false;
                    IsMinimized = false;

                    break;

                case WindowState.Minimized:

                    IsMinimized = true;
                    break;
            }

            prevSurfaceWindowState = Surface.WindowState;
        }
        private void Overlay_StateChanged(object sender, EventArgs e)
        {       
            if (Overlay.WindowState == WindowState.Normal && ownedRestoreToMaximize)
            {
                ownedRestoreToMaximize = false;
                Overlay.WindowState = WindowState.Maximized;
            }
        }

        private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            mouseLeftDownPoint = e.GetPosition(Surface);
            Player?.Activity.RefreshFullActive();

            if ((SwapOnDrop == AvailableWindows.Surface || SwapOnDrop == AvailableWindows.Both) && 
                (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
            {
                IsSwapping = true;
                DragDrop.DoDragDrop(this, new FlyleafHostDropWrap() { FlyleafHost = this }, DragDropEffects.Move);
                
                return;
            }

            if (ResizingSide != 0)
            {
                ReSetVisibleRect();
                IsResizing = true;
            }
            else
            {
                if (Player != null)
                {
                    panPrevX = Player.PanXOffset;
                    panPrevY = Player.PanYOffset;
                }
            }

            Surface.CaptureMouse();
        }
        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            mouseLeftDownPoint = e.GetPosition(Overlay);
            Player?.Activity.RefreshFullActive();
            
            if ((SwapOnDrop == AvailableWindows.Overlay || SwapOnDrop == AvailableWindows.Both) && 
                (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
            {
                IsSwapping = true;
                DragDrop.DoDragDrop(this, new FlyleafHostDropWrap() { FlyleafHost = this }, DragDropEffects.Move);
                
                return;
            }

            if (ResizingSide != 0)
            {
                ReSetVisibleRect();
                IsResizing = true;
            }
            else
            {
                if (Player != null)
                {
                    panPrevX = Player.PanXOffset;
                    panPrevY = Player.PanYOffset;
                }
            }

            Overlay.CaptureMouse();
        }

        private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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
            Surface.ReleaseMouseCapture();
        }
        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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
            Overlay.ReleaseMouseCapture();
        }

        private void Surface_MouseMove(object sender, MouseEventArgs e)
        {
            Point cur = e.GetPosition(Overlay);
             
            if (Player != null && cur != mouseMoveLastPoint)
            {
                Player.Activity.RefreshFullActive();
                mouseMoveLastPoint = cur;
            }

            // Resize Sides (CanResize + !MouseDown + !FullScreen)
            if (e.MouseDevice.LeftButton != MouseButtonState.Pressed)
            {
                if ( Surface.WindowState != WindowState.Maximized && 
                    ((IsAttached && (AttachedResize == AvailableWindows.Surface || AttachedResize == AvailableWindows.Both)) ||
                    (!IsAttached && (DetachedResize == AvailableWindows.Surface || DetachedResize == AvailableWindows.Both))))
                {
                    ResizingSide = ResizeSides(Surface, cur, ResizeSensitivity);
                }
            }
            else if (IsSwapping)
                return;

            // Resize (MouseDown + ResizeSide != 0)
            else if (IsResizing)
            {
                Point x1 = new Point(Surface.Left, Surface.Top);

                Resize(Surface, SurfaceHandle, cur, ResizingSide, CurResizeRatio);

                if (IsAttached)
                {
                    Point x2 = new Point(Surface.Left, Surface.Top);
                    
                    MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + x2.X - x1.X, MarginTarget.Margin.Top + x2.Y - x1.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                    Width = Surface.Width;
                    Height = Surface.Height;
                }
            }

            // Bug? happens on double click
            else if (mouseLeftDownPoint.X == -1)
                return;

            // Player's Pan Move (Ctrl + Drag Move)
            else if (Player != null && 
                (PanMoveWithCtrl == AvailableWindows.Surface || PanMoveWithCtrl == AvailableWindows.Both) &&
                (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                Player.PanXOffset = panPrevX + (int) (cur.X - mouseLeftDownPoint.X);
                Player.PanYOffset = panPrevY + (int) (cur.Y - mouseLeftDownPoint.Y);
            }

            // Drag Move Self (Detached) / Self (Attached) / Owner (Attached)
            else if (Surface.IsMouseCaptured && Surface.WindowState != WindowState.Maximized)
            {
                if (IsAttached)
                {
                    if (AttachedDragMove == AttachedDragMoveOptions.SurfaceOwner || AttachedDragMove == AttachedDragMoveOptions.BothOwner)
                    {
                        if (Owner != null)
                        {
                            Owner.Left += cur.X - mouseLeftDownPoint.X;
                            Owner.Top += cur.Y - mouseLeftDownPoint.Y;
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
            Point cur = e.GetPosition(Overlay);
             
            if (Player != null && cur != mouseMoveLastPoint)
            {
                Player.Activity.RefreshFullActive();
                mouseMoveLastPoint = cur;
            }

            // Resize Sides (CanResize + !MouseDown + !FullScreen)
            if (e.MouseDevice.LeftButton != MouseButtonState.Pressed)
            {
                if ( Overlay.WindowState != WindowState.Maximized && 
                    ((IsAttached && (AttachedResize == AvailableWindows.Overlay || AttachedResize == AvailableWindows.Both)) ||
                    (!IsAttached && (DetachedResize == AvailableWindows.Overlay || DetachedResize == AvailableWindows.Both))))
                {
                    ResizingSide = ResizeSides(Overlay, cur, ResizeSensitivity);
                }
            }
            else if (IsSwapping)
                return;

            // Resize (MouseDown + ResizeSide != 0)
            else if (IsResizing)
            {
                Point x1 = new Point(Overlay.Left, Overlay.Top);

                Resize(Overlay, OverlayHandle, cur, ResizingSide, CurResizeRatio);

                if (IsAttached)
                {
                    Point x2 = new Point(Overlay.Left, Overlay.Top);
                    
                    MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + x2.X - x1.X, MarginTarget.Margin.Top + x2.Y - x1.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                    Width = Overlay.Width;
                    Height = Overlay.Height;
                }
            }

            // Bug? happens on double click
            else if (mouseLeftDownPoint.X == -1)
                return;

            // Player's Pan Move (Ctrl + Drag Move)
            else if (Player != null && 
                (PanMoveWithCtrl == AvailableWindows.Overlay || PanMoveWithCtrl == AvailableWindows.Both) &&
                (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                Player.PanXOffset = panPrevX + (int) (cur.X - mouseLeftDownPoint.X);
                Player.PanYOffset = panPrevY + (int) (cur.Y - mouseLeftDownPoint.Y);
            }

            // Drag Move Self (Detached) / Self (Attached) / Owner (Attached)
            else if (Overlay.IsMouseCaptured && Overlay.WindowState != WindowState.Maximized)
            {
                if (IsAttached)
                {
                    if (AttachedDragMove == AttachedDragMoveOptions.OverlayOwner || AttachedDragMove == AttachedDragMoveOptions.BothOwner)
                    {
                        if (Owner != null)
                        {
                            Owner.Left += cur.X - mouseLeftDownPoint.X;
                            Owner.Top += cur.Y - mouseLeftDownPoint.Y;
                        }
                    }
                    else if (AttachedDragMove == AttachedDragMoveOptions.Overlay || AttachedDragMove == AttachedDragMoveOptions.Both)
                    {
                        // TBR: Bug with right click (popup menu) and then left click drag
                        MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + cur.X - mouseLeftDownPoint.X, MarginTarget.Margin.Top + cur.Y - mouseLeftDownPoint.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                    }
                } else
                {
                    if (DetachedDragMove == AvailableWindows.Overlay || DetachedDragMove == AvailableWindows.Both)
                    {
                        Overlay.Left  += cur.X - mouseLeftDownPoint.X;
                        Overlay.Top   += cur.Y - mouseLeftDownPoint.Y;
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
            if (Player != null && e.Delta != 0 && 
                (PanZoomWithCtrlWheel == AvailableWindows.Surface || PanZoomWithCtrlWheel == AvailableWindows.Both) &&
                (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                Player.Zoom += e.Delta > 0 ? Player.Config.Player.ZoomOffset : -Player.Config.Player.ZoomOffset;
            }
            //else if (IsAttached) // TBR ScrollViewer
            //{
            //    RaiseEvent(e);
            //}
        }
        private void Overlay_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Player != null && e.Delta != 0 && 
                (PanZoomWithCtrlWheel == AvailableWindows.Overlay || PanZoomWithCtrlWheel == AvailableWindows.Both) &&
                (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                Player.Zoom += e.Delta > 0 ? Player.Config.Player.ZoomOffset : -Player.Config.Player.ZoomOffset;
            }
        }

        private void Surface_Closing(object sender, CancelEventArgs e) { isSurfaceClosing = true; Dispose(); }
        private void Overlay_Closing(object sender, CancelEventArgs e) { if (isSurfaceClosing) return; e.Cancel = true; Surface.Close(); }

        private void Overlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        { // Stand Alone Only (Main control has the overlay)
            if (Overlay.IsVisible)
            {
                Surface?.Show();
            }
            else
            {
                Surface?.Hide();
            }
        }
        private void Overlay_Loaded(object sender, RoutedEventArgs e)
        {
            if (Overlay.IsVisible)
                Surface.Show();
        }

        public static void Resize(Window Window, IntPtr WindowHandle, Point p, int resizingSide, double ratio = 0.0)
        {
            double WindowWidth = Window.ActualWidth, WindowHeight = Window.ActualHeight, WindowLeft = Window.Left, WindowTop = Window.Top;

            if (resizingSide == 2 || resizingSide == 3 || resizingSide == 6)
            {
                p.X += 5;

                if (p.X > Window.MinWidth)
                {
                    WindowWidth = p.X < Window.MaxWidth ? p.X : Window.MaxWidth;
                }
                else
                    WindowWidth = Window.MinWidth;
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
                    p.Y = Window.MinHeight;
            }
            else if (resizingSide == 1 || resizingSide == 3 || resizingSide == 7)
            {
                if (ratio != 0 && resizingSide != 7)
                {
                    WindowTop += (Window.ActualWidth - WindowWidth) / ratio;
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
                    {
                        WindowHeight = temp > Window.MinHeight ? Window.MaxHeight : Window.MinHeight;
                    }
                }
            }

            // TBR: should also change position on some cases
            if (ratio == 0)
                SetWindowPos(WindowHandle, IntPtr.Zero, (int)(WindowLeft * DpiX), (int)(WindowTop * DpiY), (int)(WindowWidth * DpiX), (int)(WindowHeight * DpiY), (UInt32)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
            else if (resizingSide == 7 || resizingSide == 8)
                SetWindowPos(WindowHandle, IntPtr.Zero, (int)(WindowLeft * DpiX), (int)(WindowTop * DpiY), (int)((WindowHeight * ratio) * DpiX), (int)(WindowHeight * DpiY), (UInt32)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
            else
                SetWindowPos(WindowHandle, IntPtr.Zero, (int)(WindowLeft * DpiX), (int)(WindowTop * DpiY), (int)(WindowWidth * DpiX), (int)((WindowWidth / ratio) * DpiY), (UInt32)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
        }
        public static int ResizeSides(Window Window, Point p, int ResizeSensitivity)
        {
            if (p.X <= ResizeSensitivity && p.Y <= ResizeSensitivity)
            {
                Window.Cursor = Cursors.SizeNWSE;
                return 1;
            }
            else if (p.X + ResizeSensitivity >= Window.ActualWidth && p.Y + ResizeSensitivity >= Window.ActualHeight)
            {
                Window.Cursor = Cursors.SizeNWSE;
                return 2;
            }
            else if (p.X + ResizeSensitivity >= Window.ActualWidth && p.Y <= ResizeSensitivity)
            {
                Window.Cursor = Cursors.SizeNESW;
                return 3;
            }
            else if (p.X <= ResizeSensitivity && p.Y + ResizeSensitivity >= Window.ActualHeight)
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
        }
        public FlyleafHost()
        {
            UniqueId = idGenerator++;
            isDesginMode = DesignerProperties.GetIsInDesignMode(this);
            //isDesginMode = true;
            if (isDesginMode)
            {
                dMain   = new Grid();
                dHost   = new Grid();
                dSurface= new Grid();
                dOverlay= new Grid();

                Panel.SetZIndex(dHost, 0);
                Panel.SetZIndex(dSurface, 1);
                Panel.SetZIndex(dOverlay, 2);

                dMain.Children.Add(dHost);
                dMain.Children.Add(dSurface);
                dMain.Children.Add(dOverlay);

                return;
            }

            MarginTarget = this;

            Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost NP] ");
            Loaded      += Host_Loaded; // Initialized event ??
            Unloaded    += Host_Unloaded;
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
            Overlay = standAloneOverlay;
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

                if (oldPlayer.renderer != null)
                    oldPlayer.renderer.SetControl(null);
                
                oldPlayer.WPFHost = null;
                oldPlayer.IsFullScreen = false;
            }

            if (Player == null)
                return;

            // Set UniqueId (First Player's Id)
            Log.Prefix  = ("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost #{Player.PlayerId}] ";

            // De-assign new Player's Handle/FlyleafHost
            if (Player.WPFHost != null)
                Player.WPFHost.Player = null;

            if (Player == null) // We might just de-assign our Player
                return;
            
            // Assign new Player's (Handle/FlyleafHost)
            Log.Debug($"Assign Player #{Player.PlayerId}");

            Player.WPFHost = this;
            Player.Activity.Timeout = ActivityTimeout;
            Player.IsFullScreen = IsFullScreen;
            Player.VideoDecoder.CreateRenderer(Surface);

            Player.Video.PropertyChanged += Player_Video_PropertyChanged;
            if (KeepRatioOnResize && Player.Video.AspectRatio.Value > 0)
                CurResizeRatio = Player.Video.AspectRatio.Value;
        }
        public virtual void SetSurface()
        {
            Surface = new Window();
            Surface.ShowInTaskbar = IsStandAlone;
            Surface.Background  = Brushes.Black;
            Surface.WindowStyle = WindowStyle.None;
            Surface.ResizeMode  = ResizeMode.NoResize;
            Surface.Width       = Surface.Height = 1; // Will be set on loaded

            SurfaceHandle       = new WindowInteropHelper(Surface).EnsureHandle();

            Surface.Closing     += Surface_Closing;
            Surface.KeyDown     += Surface_KeyDown;
            Surface.KeyUp       += Surface_KeyUp;
            Surface.MouseLeftButtonDown
                                += Surface_MouseLeftButtonDown;
            Surface.MouseLeftButtonUp
                                += Surface_MouseLeftButtonUp;
            Surface.MouseMove   += Surface_MouseMove;
            Surface.MouseDoubleClick
                                += Surface_MouseDoubleClick;
            Surface.DragEnter   += Surface_DragEnter;
            Surface.Drop        += Surface_Drop;
            Surface.MouseWheel  += Surface_MouseWheel;
            Surface.StateChanged+= Surface_StateChanged;
            Surface.MouseLeave  += Surface_MouseLeave;

            Surface.AllowDrop =
                OpenOnDrop == AvailableWindows.Surface || OpenOnDrop == AvailableWindows.Both ||
                SwapOnDrop == AvailableWindows.Surface || SwapOnDrop == AvailableWindows.Both;
        }
        public virtual void SetOverlay()
        {
            //if (OverlayHandle == new WindowInteropHelper(overlay).Handle) // Don't create it yet
                //DisposeOverlay();

            if (Overlay == null)
                return;

            Overlay.Background = Brushes.Transparent;
            Overlay.WindowStyle = WindowStyle.None;
            Overlay.ResizeMode = ResizeMode.NoResize;
            Overlay.AllowsTransparency = true;

            OverlayHandle = new WindowInteropHelper(Overlay).EnsureHandle();

            bool wasTopmost = false;
            if (Surface.Topmost)
            {
                wasTopmost = true;
                Surface.Topmost = false;
            }

            Overlay.Owner = Surface;
            Overlay.ShowInTaskbar = false;
            Surface.Topmost = wasTopmost;

            if (IsStandAlone)
            {
                Overlay.IsVisibleChanged 
                                += Overlay_IsVisibleChanged;
                Overlay.Loaded  += Overlay_Loaded;
                Surface.Width   = Overlay.Width;
                Surface.Height  = Overlay.Height;
                Surface.Icon    = Overlay.Icon;
            }
            else
            {
                Overlay.Resources = Resources;
                Overlay.DataContext = this; // TBR: or this.DataContext?
                Overlay.Width = Overlay.Height = 1; // Will be set on loaded
            }

            Overlay.SetBinding(Window.MinWidthProperty,     new Binding(nameof(Surface.MinWidth))   { Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });
            Overlay.SetBinding(Window.MaxWidthProperty,     new Binding(nameof(Surface.MaxWidth))   { Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });
            Overlay.SetBinding(Window.MinHeightProperty,    new Binding(nameof(Surface.MinHeight))  { Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });
            Overlay.SetBinding(Window.MaxHeightProperty,    new Binding(nameof(Surface.MaxHeight))  { Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });
            Overlay.SetBinding(Window.WidthProperty,        new Binding(nameof(Surface.Width))      { Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });
            Overlay.SetBinding(Window.HeightProperty,       new Binding(nameof(Surface.Height))     { Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });
            Overlay.SetBinding(Window.LeftProperty,         new Binding(nameof(Surface.Left))       { Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });
            Overlay.SetBinding(Window.TopProperty,          new Binding(nameof(Surface.Top))        { Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });

            Overlay.KeyUp       += Overlay_KeyUp;
            Overlay.KeyDown     += Overlay_KeyDown;
            Overlay.Closing     += Overlay_Closing;
            Overlay.MouseLeftButtonDown
                                += Overlay_MouseLeftButtonDown;
            Overlay.MouseLeftButtonUp
                                += Overlay_MouseLeftButtonUp;
            Overlay.MouseWheel  += Overlay_MouseWheel;
            Overlay.MouseMove   += Overlay_MouseMove;
            Overlay.MouseLeave  += Overlay_MouseLeave;
            Overlay.MouseDoubleClick
                                += Overlay_MouseDoubleClick;
            Overlay.Drop        += Overlay_Drop;
            Overlay.DragEnter   += Overlay_DragEnter;
            Overlay.StateChanged+= Overlay_StateChanged;

            // Owner will close the overlay
            Overlay.KeyDown += (o, e) => { if (e.Key == Key.System && e.SystemKey == Key.F4) Surface?.Focus(); };

            Overlay.AllowDrop =
                OpenOnDrop == AvailableWindows.Overlay || OpenOnDrop == AvailableWindows.Both ||
                SwapOnDrop == AvailableWindows.Overlay || SwapOnDrop == AvailableWindows.Both;

            //??
            if (Surface.IsVisible)
                Overlay.Visibility = Visibility.Visible;
        }

        public virtual void Attach()
        {
            rectDetachedLast = new Rect(Surface.Left, Surface.Top, Surface.Width, Surface.Height);

            if (DetachedTopMost)
                Surface.Topmost = false;

            Surface.MinWidth = MinWidth;
            Surface.MinHeight = MinHeight;
                
            Surface.Owner = Owner;

            if (Surface.IsVisible)
                Surface.Activate();

            rectInitLast = rectIntersectLast = rectRandom;
            Host_LayoutUpdated(null, null);
        }
        public virtual void Detach()
        {
            if (IsFullScreen)
                IsFullScreen = false;

            // Calculate Size
            Size newSize;
            if (DetachedRememberSize && rectDetachedLast != Rect.Empty)
                newSize = new Size(rectDetachedLast.Width, rectDetachedLast.Height);
            else if (!DetachedFixedSize.IsEmpty)
                newSize = DetachedFixedSize;
            else
                newSize = new Size(Surface.Width, Surface.Height);

            // Calculate Position
            Point newPos;
            if (DetachedRememberPosition && rectDetachedLast != Rect.Empty)
                newPos = new Point(rectDetachedLast.X, rectDetachedLast.Y);
            else
            {
                switch (DetachedPosition)
                {
                    case DetachedPositionOptions.TopLeft:
                        newPos = new Point(0, 0);
                        break;
                    case DetachedPositionOptions.TopCenter:
                        newPos = new Point((SystemParameters.PrimaryScreenWidth / 2) - (newSize.Width / 2), 0);
                        break;

                    case DetachedPositionOptions.TopRight:
                        newPos = new Point(SystemParameters.PrimaryScreenWidth - newSize.Width, 0);
                        break;

                    case DetachedPositionOptions.CenterLeft:
                        newPos = new Point(0, (SystemParameters.PrimaryScreenHeight / 2) - (newSize.Height / 2));
                        break;

                    case DetachedPositionOptions.CenterCenter:
                        newPos = new Point((SystemParameters.PrimaryScreenWidth / 2) - (newSize.Width / 2), (SystemParameters.PrimaryScreenHeight / 2) - (newSize.Height / 2));
                        break;

                    case DetachedPositionOptions.CenterRight:
                        newPos = new Point(SystemParameters.PrimaryScreenWidth - newSize.Width, (SystemParameters.PrimaryScreenHeight / 2) - (newSize.Height / 2));
                        break;

                    case DetachedPositionOptions.BottomLeft:
                        newPos = new Point(0, SystemParameters.PrimaryScreenHeight - newSize.Height);
                        break;

                    case DetachedPositionOptions.BottomCenter:
                        newPos = new Point((SystemParameters.PrimaryScreenWidth / 2) - (newSize.Width / 2), SystemParameters.PrimaryScreenHeight - newSize.Height);
                        break;

                    case DetachedPositionOptions.BottomRight:
                        newPos = new Point(SystemParameters.PrimaryScreenWidth - newSize.Width, SystemParameters.PrimaryScreenHeight - newSize.Height);
                        break;

                    case DetachedPositionOptions.Custom:
                        newPos = DetachedFixedPosition;
                        break;

                    default:
                        newPos = new Point(Surface.Left, Surface.Top);
                        break;
                }

                newPos.X += (DetachedPositionMargin.Left - DetachedPositionMargin.Right);
                newPos.Y += (DetachedPositionMargin.Top - DetachedPositionMargin.Bottom);
            }
            Rect final = new Rect(newPos.X, newPos.Y, newSize.Width, newSize.Height);

            SetRect(final);
            ReSetVisibleRect();

            if (DetachedTopMost)
                Surface.Topmost = true;

            Surface.Owner = null;

            if (Surface.IsVisible)
                Surface.Activate();
        }

        public void RefreshNormalFullScreen()
        {
            if (Surface.WindowState == WindowState.Minimized)
                return;

            if (IsFullScreen)
            {
                if (IsAttached)
                    ReSetVisibleRect();

                Surface.WindowState = WindowState.Maximized;
            }
            else
            {
                Surface.WindowState = WindowState.Normal;

                if (IsAttached)
                {
                    rectInitLast = rectIntersectLast = Rect.Empty;
                    Host_LayoutUpdated(null, null);
                }
            }
        }
        public void SetRect(Rect rect)
        {
            SetWindowPos(SurfaceHandle, IntPtr.Zero, (int)(rect.X * DpiX), (int)(rect.Y * DpiY), (int)(rect.Width * DpiX), (int)(rect.Height * DpiY), (UInt32)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
        }
        public void ReSetVisibleRect()
        {
            SetWindowRgn(SurfaceHandle, IntPtr.Zero, true);
            if (OverlayHandle != IntPtr.Zero)
                SetWindowRgn(OverlayHandle, IntPtr.Zero, true);
        }
        public void SetVisibleRect(Rect rect)
        {
            SetWindowRgn(SurfaceHandle, CreateRectRgn((int)(rect.X * DpiX), (int)(rect.Y * DpiY), (int)(rect.Right * DpiX), (int)(rect.Bottom * DpiY)), true);

            if (OverlayHandle != IntPtr.Zero)
                SetWindowRgn(OverlayHandle, CreateRectRgn((int)(rect.X * DpiX), (int)(rect.Y * DpiY), (int)(rect.Right * DpiX), (int)(rect.Bottom * DpiY)), true);
        }

        public void Dispose()
        {
            lock (this)
            {
                if (Disposed)
                    return;

                Disposed = true;

                if (Surface != null)
                {
                    Surface.MouseMove -= Surface_MouseMove;
                    Surface.MouseLeave -= Surface_MouseLeave;
                }

                if (Overlay != null)
                {
                    Overlay.IsVisibleChanged -= Overlay_IsVisibleChanged;
                    Overlay.MouseLeave -= Overlay_MouseLeave;
                }

                if (Player != null)
                {
                    Player.WPFHost = null;
                    Player.Dispose();
                }

                SurfaceHandle   = IntPtr.Zero;
                OverlayHandle   = IntPtr.Zero;
                OwnerHandle     = IntPtr.Zero;

                Surface = null;
                Overlay = null;
                Owner   = null;
                Player  = null;
            }
        }
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
}