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
        /* Properties
         * ----------
         * 
         * Player                   * Assigned Player (Can be changed or even be null)
         * 
         * KeyBindingsMode          * Disabled / Surface / Overlay / Both
         * 
         * AllowDrop                * File / Text Drop for Player
         *                          * Swap FlyleafHosts (Shift)
         *                  
         * DragMoveOnAttach         * Drag Move Owner (Main Window)
         *                          * Drag Move FlyleafHost
         *                          
         * DragMoveOnDetach         * Drag Move Surface (+Overlay)
         *                  
         * PanMove                  * Drag Move of Player's Pan & Zoom (wheel)
         * 
         * ResizeMode               * Allow Surface/Overlay to be resized
         * 
         * ResizeOnAttach           * Allow resize on attached
         * 
         * ResizeOnDetach           * Allow resize on detached
         * 
         * IsAttached               * Attach/Detach toggle
         * 
         * IsFullScreen             * Normal/Full toggle
         * 
         * IsResizing               * Whether Surface/Overlay is resizing
         * 
         * RememberLastDetachedRect * Restores detached surface to last position/size
         * 
         * DetachedRect             * Position and size of the detached surface
         * 
         * TopMost                  * Keeps surface on top
         * 
         * ToggleFullScreenOnDoubleClick
         *                          * Toggle full screen
         * 
         * Content (Overlay)        * Will be transfered to Overlay by default
         * 
         * Detached Content         * FlyleafHost's actual content
         */

        #region Properties / Variables
        public Window       Surface         { get; private set; }
        public Window       Owner           { get; private set; }

        public IntPtr       SurfaceHandle   { get; private set; }
        public IntPtr       OverlayHandle   { get; private set; }
        public IntPtr       OwnerHandle     { get; private set; }

        public bool         Disposed        { get; private set; }
        public int          UniqueId        { get; private set; } = -1;

        public int          ResizingSide    { get; private set; }
        public int          ResizingSideOverlay
                                            { get; private set; }

        public bool         IsSwaping       { get; private set; }

        bool isSurfaceClosing;
        bool isStandAlone;
        static bool isDesginMode;
        Grid dMain, dHost, dSurface, dOverlay; // Design Mode Grids
        bool preventContentUpdate;
        int panPrevX, panPrevY;
        bool mouseLeftDownDeactivated;
        bool preventFullScreenUpdate; // to handle a bug from fullscreen to minimize and back to normal

        Matrix matrix;
        Point mouseLeftDownPoint = new Point(0, 0);
        Point zeroPoint = new Point(0, 0);
        Point lastOverlayPosition = new Point(0, 0);

        static Rect rectRandom = new Rect(1, 2, 3, 4);
        Rect rectDetachedLast = Rect.Empty;
        Rect rectIntersectLast = rectRandom;
        Rect rectInitLast = rectRandom;

        private class FlyleafHostDropWrap { public FlyleafHost FlyleafHost; } // To allow non FlyleafHosts to drag & drop
        protected readonly LogHandler Log;
        #endregion

        #region Dependency Properties
        public int ActivityTimeout
        {
            get { return (int)GetValue(ActivityTimeoutProperty); }
            set { SetValue(ActivityTimeoutProperty, value); }
        }
        public static readonly DependencyProperty ActivityTimeoutProperty =
            DependencyProperty.Register(nameof(ActivityTimeout), typeof(int), typeof(FlyleafHost), new PropertyMetadata(0, new PropertyChangedCallback(OnActivityTimeoutChanged)));

        public AttachedDragMoveMode DragMoveOnAttach
        {
            get { return (AttachedDragMoveMode)GetValue(DragMoveOnAttachProperty); }
            set { SetValue(DragMoveOnAttachProperty, value); }
        }
        public static readonly DependencyProperty DragMoveOnAttachProperty =
            DependencyProperty.Register(nameof(DragMoveOnAttach), typeof(AttachedDragMoveMode), typeof(FlyleafHost), new PropertyMetadata(AttachedDragMoveMode.Owner));

        public bool DragMoveOnDetach
        {
            get { return (bool)GetValue(DragMoveOnDetachProperty); }
            set { SetValue(DragMoveOnDetachProperty, value); }
        }
        public static readonly DependencyProperty DragMoveOnDetachProperty =
            DependencyProperty.Register(nameof(DragMoveOnDetach), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

        public FrameworkElement MarginTarget
        {
            get { return (FrameworkElement)GetValue(MarginTargetProperty); }
            set { SetValue(MarginTargetProperty, value); }
        }
        public static readonly DependencyProperty MarginTargetProperty =
            DependencyProperty.Register(nameof(MarginTarget), typeof(FrameworkElement), typeof(FlyleafHost), new PropertyMetadata(null));

        public bool PanMove
        {
            get { return (bool)GetValue(PanMoveProperty); }
            set { SetValue(PanMoveProperty, value); }
        }
        public static readonly DependencyProperty PanMoveProperty =
            DependencyProperty.Register(nameof(PanMove), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

        public AvailableWindows ResizeMode
        {
            get { return (AvailableWindows)GetValue(ResizeModeProperty); }
            set { SetValue(ResizeModeProperty, value); }
        }
        public static readonly DependencyProperty ResizeModeProperty =
            DependencyProperty.Register(nameof(ResizeMode), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Both));

        public bool ResizeOnAttach
        {
            get { return (bool)GetValue(ResizeOnAttachProperty); }
            set { SetValue(ResizeOnAttachProperty, value); }
        }
        public static readonly DependencyProperty ResizeOnAttachProperty =
            DependencyProperty.Register(nameof(ResizeOnAttach), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));

        public bool ResizeOnDetach
        {
            get { return (bool)GetValue(ResizeOnDetachProperty); }
            set { SetValue(ResizeOnDetachProperty, value); }
        }
        public static readonly DependencyProperty ResizeOnDetachProperty =
            DependencyProperty.Register(nameof(ResizeOnDetach), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

        public bool ToggleFullScreenOnDoubleClick
        {
            get { return (bool)GetValue(ToggleFullScreenOnDoubleClickProperty); }
            set { SetValue(ToggleFullScreenOnDoubleClickProperty, value); }
        }
        public static readonly DependencyProperty ToggleFullScreenOnDoubleClickProperty =
            DependencyProperty.Register(nameof(ToggleFullScreenOnDoubleClick), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

        public Rect DetachedRect
        {
            get { return (Rect)GetValue(DetachedRectProperty); }
            set { SetValue(DetachedRectProperty, value); }
        }
        public static readonly DependencyProperty DetachedRectProperty =
            DependencyProperty.Register(nameof(DetachedRect), typeof(Rect), typeof(FlyleafHost), new PropertyMetadata(Rect.Empty));

        public AvailableWindows KeyBindingsMode
        {
            get { return (AvailableWindows)GetValue(KeyBindingsModeProperty); }
            set { SetValue(KeyBindingsModeProperty, value); }
        }
        public static readonly DependencyProperty KeyBindingsModeProperty =
            DependencyProperty.Register(nameof(KeyBindingsMode), typeof(AvailableWindows), typeof(FlyleafHost), new PropertyMetadata(AvailableWindows.Surface));

        public bool IsAttached
        {
            get { return (bool)GetValue(IsAttachedProperty); }
            set { SetValue(IsAttachedProperty, value); }
        }
        public static readonly DependencyProperty IsAttachedProperty =
            DependencyProperty.Register(nameof(IsAttached), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true, new PropertyChangedCallback(OnIsAttachedChanged)));

        public bool IsFullScreen
        {
            get { return (bool)GetValue(IsFullScreenProperty); }
            set { SetValue(IsFullScreenProperty, value); }
        }
        public static readonly DependencyProperty IsFullScreenProperty =
            DependencyProperty.Register(nameof(IsFullScreen), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false, new PropertyChangedCallback(OnFullScreenChanged)));

        public bool IsResizing
        {
            get { return (bool)GetValue(IsResizingProperty); }
            private set { SetValue(IsResizingProperty, value); }
        }
        public static readonly DependencyProperty IsResizingProperty =
            DependencyProperty.Register(nameof(IsResizing), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(false));

        public bool RememberLastDetachedRect
        {
            get { return (bool)GetValue(RememberLastDetachedRectProperty); }
            set { SetValue(RememberLastDetachedRectProperty, value); }
        }
        public static readonly DependencyProperty RememberLastDetachedRectProperty =
            DependencyProperty.Register(nameof(RememberLastDetachedRect), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

        public int ResizeSensitivity
        {
            get { return (int)GetValue(ResizeSensitivityProperty); }
            set { SetValue(ResizeSensitivityProperty, value); }
        }
        public static readonly DependencyProperty ResizeSensitivityProperty =
            DependencyProperty.Register(nameof(ResizeSensitivity), typeof(int), typeof(FlyleafHost), new PropertyMetadata(6));

        public bool TopMost
        {
            get { return (bool)GetValue(TopMostProperty); }
            set { SetValue(TopMostProperty, value); }
        }
        public static readonly DependencyProperty TopMostProperty =
            DependencyProperty.Register(nameof(TopMost), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

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
        private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (isDesginMode)
                return;
            
            FlyleafHost host = d as FlyleafHost;
            host.SetPlayer((Player)e.OldValue);
        }
        private static void OnFullScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (isDesginMode)
                return;

            FlyleafHost host = d as FlyleafHost;
            host.RefreshNormalFullScreen();

            if (host.Player != null)
                host.Player.IsFullScreen = host.IsFullScreen;
        }
        private static void OnIsAttachedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (isDesginMode)
                return;

            FlyleafHost host = d as FlyleafHost;

            if (host.isStandAlone)
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
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.Property.Name == nameof(AllowDrop) && Surface != null)
                Surface.AllowDrop = AllowDrop;
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

        private void Surface_Closing(object sender, CancelEventArgs e) { isSurfaceClosing = true; Dispose(); }
        private void Surface_KeyDown(object sender, KeyEventArgs e) { if (KeyBindingsMode == AvailableWindows.Surface || KeyBindingsMode == AvailableWindows.Both) e.Handled = Player.KeyDown(Player, e); }
        private void Surface_KeyUp(object sender, KeyEventArgs e) { if (KeyBindingsMode == AvailableWindows.Surface || KeyBindingsMode == AvailableWindows.Both) e.Handled = Player.KeyUp(Player, e); }
        private void Surface_Drop(object sender, DragEventArgs e)
        {
            IsSwaping = false;

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
        private void Surface_DragEnter(object sender, DragEventArgs e) { if (Player != null) e.Effects = DragDropEffects.All; }
        private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // TBR: Capture / Release of mouse is wrong here

            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                DragDrop.DoDragDrop(this, new FlyleafHostDropWrap() { FlyleafHost = this }, DragDropEffects.Move);;
                IsSwaping = true;
                return;
            }

            if (ResizingSide != 0)
            {
                ReSetVisibleRect();
                IsResizing = true;
            }
            else
            {
                mouseLeftDownPoint = e.GetPosition(Surface);
                mouseLeftDownDeactivated = false;
                if (Player != null)
                {
                    panPrevX = Player.PanXOffset;
                    panPrevY = Player.PanYOffset;
                }
            }

            Surface.CaptureMouse();
        }
        private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsResizing)
            {
                ResizingSide = 0;
                Surface.Cursor = Cursors.Arrow;
                //Overlay.Cursor = Cursors.Arrow;
                IsResizing = false;
            }
            mouseLeftDownPoint.X = -1;
            IsSwaping = false;
            Surface.ReleaseMouseCapture();
        }
        private void Surface_MouseMove(object sender, MouseEventArgs e)
        {
            Point cur = e.GetPosition(Surface);

            if (Player != null && ActivityTimeout > 0 && cur != lastOverlayPosition)
                Player.Activity.RefreshFullActive();

            // Resize Sides (CanResize + !MouseDown + !FullScreen)
            if (e.MouseDevice.LeftButton != MouseButtonState.Pressed)
            {
                if ( Surface.WindowState != WindowState.Maximized && 
                    (ResizeMode == AvailableWindows.Surface || ResizeMode == AvailableWindows.Both) &&
                    (IsAttached && ResizeOnAttach) ||
                    (!IsAttached && ResizeOnDetach) )
                {
                    ResizingSide = ResizeSides(Surface, cur, ResizeSensitivity);
                }
                        
            }
            else if (IsSwaping)
                return;

            // Resize (MouseDown + ResizeSide != 0)
            else if (IsResizing) //AllowResize &&  && !IsAttached)
            {
                Point x1 = new Point(Surface.Left, Surface.Top);

                Resize(Surface, SurfaceHandle, cur, ResizingSide);

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
            else if (PanMove && Player != null && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                Player.PanXOffset = panPrevX + (int) (cur.X - mouseLeftDownPoint.X);
                Player.PanYOffset = panPrevY + (int) (cur.Y - mouseLeftDownPoint.Y);
            }

            // Drag Move Self (Detached) / Self (Attached) / Owner (Attached)
            else if (!mouseLeftDownDeactivated && Surface.WindowState != WindowState.Maximized)
            {
                if (IsAttached)
                {
                    if (DragMoveOnAttach == AttachedDragMoveMode.Owner)
                    {
                        if (Owner != null)
                        {
                            Owner.Left += cur.X - mouseLeftDownPoint.X;
                            Owner.Top += cur.Y - mouseLeftDownPoint.Y;
                        }
                    }
                    else if (DragMoveOnAttach == AttachedDragMoveMode.Self)
                    {
                        // TBR: Bug with right click (popup menu) and then left click drag
                        MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + cur.X - mouseLeftDownPoint.X, MarginTarget.Margin.Top + cur.Y - mouseLeftDownPoint.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                    }
                } else
                {
                    if (DragMoveOnDetach)
                    {
                        Surface.Left  += cur.X - mouseLeftDownPoint.X;
                        Surface.Top   += cur.Y - mouseLeftDownPoint.Y;
                    }
                }
            }
        }
        private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (PanMove && Player != null && e.Delta != 0 && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                Player.Zoom += e.Delta > 0 ? Player.Config.Player.ZoomOffset : -Player.Config.Player.ZoomOffset;
            }
            //else if (IsAttached) // TBR ScrollViewer
            //{
            //    RaiseEvent(e);
            //}
        }
        private void Surface_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (!ToggleFullScreenOnDoubleClick) return; IsFullScreen = !IsFullScreen; e.Handled = true; }
        private void Surface_Deactivated(object sender, EventArgs e) { mouseLeftDownDeactivated = true; Surface.Cursor = Cursors.Arrow; ResizingSide = 0; }
        private void Surface_StateChanged(object sender, EventArgs e)
        {
            if (Surface.WindowState == WindowState.Minimized)
                preventFullScreenUpdate = true;
            else
            {
                preventFullScreenUpdate = false;
                RefreshNormalFullScreen();
            }
        }

        private void Overlay_Closing(object sender, CancelEventArgs e) { if (isSurfaceClosing) return; e.Cancel = true; Surface.Close(); }
        private void Overlay_KeyDown(object sender, KeyEventArgs e) { if (KeyBindingsMode == AvailableWindows.Overlay || KeyBindingsMode == AvailableWindows.Both) e.Handled = Player.KeyDown(Player, e); }
        private void Overlay_KeyUp(object sender, KeyEventArgs e) { if (KeyBindingsMode == AvailableWindows.Overlay || KeyBindingsMode == AvailableWindows.Both) e.Handled = Player.KeyUp(Player, e); }
        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ResizingSideOverlay != 0)
            {
                ReSetVisibleRect();
                IsResizing = true;
                Overlay.CaptureMouse();
            }
        }
        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsResizing)
            {
                ResizingSideOverlay = 0;
                //Surface.Cursor = Cursors.Arrow;
                Overlay.Cursor = Cursors.Arrow;
                IsResizing = false;
                Overlay.ReleaseMouseCapture();
            }
        }
        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            Point cur = e.GetPosition(Overlay);
            lastOverlayPosition = cur;

            if (e.MouseDevice.LeftButton != MouseButtonState.Pressed)
            {
                if ( Overlay.WindowState != WindowState.Maximized &&
                    (ResizeMode == AvailableWindows.Overlay || ResizeMode == AvailableWindows.Both) &&
                    (IsAttached && ResizeOnAttach) ||
                    (!IsAttached && ResizeOnDetach) )
                {
                    ResizingSideOverlay = ResizeSides(Overlay, cur, ResizeSensitivity);
                }
            }

            // Resize (MouseDown + ResizeSide != 0)
            else if (IsResizing) //AllowResize &&  && !IsAttached)
            {
                Point x1 = new Point(Overlay.Left, Overlay.Top);
                Resize(Overlay, OverlayHandle, cur, ResizingSideOverlay);
                if (IsAttached)
                {
                    Point x2 = new Point(Overlay.Left, Overlay.Top);

                    MarginTarget.Margin = new Thickness(MarginTarget.Margin.Left + x2.X - x1.X, MarginTarget.Margin.Top + x2.Y - x1.Y, MarginTarget.Margin.Right, MarginTarget.Margin.Bottom);
                    Width = Overlay.Width;
                    Height = Overlay.Height;
                }
            }
        }

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

        public static void Resize(Window Window, IntPtr WindowHandle, Point p, int resizingSide)
        {
            double WindowWidth = Window.ActualWidth, WindowHeight = Window.ActualHeight, WindowLeft = Window.Left, WindowTop = Window.Top;

            if (resizingSide == 2 || resizingSide == 3 || resizingSide == 6)
            {
                p.X += 5;
                if (p.X > Window.MinWidth)
                    WindowWidth = p.X;
            }

            if (resizingSide == 1 || resizingSide == 4 || resizingSide == 5)
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
                    WindowHeight = p.Y;
            }

            if (resizingSide == 1 || resizingSide == 3 || resizingSide == 7)
            {
                p.Y -= 5;
                double temp = Window.ActualHeight - p.Y;
                if (temp > Window.MinHeight && temp < Window.MaxHeight)
                {
                    WindowHeight = temp;
                    WindowTop += p.Y;
                }
            }

            SetWindowPos(WindowHandle, IntPtr.Zero, (int)(WindowLeft * DpiX), (int)(WindowTop * DpiY), (int)(WindowWidth * DpiX), (int)(WindowHeight * DpiY), (UInt32)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
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

        static FlyleafHost()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FlyleafHost), new FrameworkPropertyMetadata(typeof(FlyleafHost)));
            AllowDropProperty.OverrideMetadata(typeof(FlyleafHost), new FrameworkPropertyMetadata(true));
        }
        public FlyleafHost()
        {
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

            Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost   ] ");
            Loaded      += Host_Loaded; // Initialized event ??
            Unloaded    += Host_Unloaded;
            DataContextChanged 
                        += Host_DataContextChanged;

            SetSurface();
        }
        public FlyleafHost(Window standAloneOverlay)
        {
            Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost   ] ");

            isStandAlone = true;
            IsAttached = false;
            SetSurface();
            Overlay = standAloneOverlay;
        }

        public virtual void Attach()
        {
            rectDetachedLast = new Rect(Surface.Left, Surface.Top, Surface.Width, Surface.Height);

            if (TopMost)
                Surface.Topmost = false;

            Surface.MinWidth = MinWidth;
            Surface.MinHeight = MinHeight;
                
            Surface.Owner = Owner;

            if (Surface.IsVisible)
                Surface.Activate();

            rectInitLast = rectIntersectLast = new Rect(1, 2, 3, 4);
            Host_LayoutUpdated(null, null);
        }
        public virtual void Detach()
        {
            //if (AllowDetachedResize && MinWidth == 0)
            //{
            //    Surface.MinWidth = Math.Min(Surface.Width, 100);
            //    Surface.MinHeight= Math.Min(Surface.Height, 100);
            //}

            if (RememberLastDetachedRect && rectDetachedLast != Rect.Empty)
                SetRect(rectDetachedLast);
            else
            {
                if (DetachedRect == Rect.Empty)
                {
                    var width = 350; var height = 200;
                    DetachedRect = new Rect(SystemParameters.MaximizedPrimaryScreenWidth - width - 20, SystemParameters.MaximizedPrimaryScreenHeight - height - 20, width, height);
                }

                SetRect(DetachedRect);
            }

            ReSetVisibleRect();

            if (TopMost)
                Surface.Topmost = true;

            Surface.Owner = null;

            if (Surface.IsVisible)
                Surface.Activate();
        }

        public void RefreshNormalFullScreen()
        {
            if (preventFullScreenUpdate)
            {
                preventFullScreenUpdate = false;
                return;
            }

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

        public virtual void SetPlayer(Player oldPlayer)
        {
            // De-assign old Player's Handle/FlyleafHost
            if (oldPlayer != null)
            {
                Log.Debug($"De-assign Player #{oldPlayer.PlayerId}");

                if (oldPlayer.renderer != null)
                    oldPlayer.renderer.SetControl(null);
                
                oldPlayer.WPFHost = null;
                oldPlayer.IsFullScreen = false;
            }

            if (Player == null)
                return;

            // Set UniqueId (First Player's Id)
            if (UniqueId == -1)
            {
                UniqueId    = Player.PlayerId;
                Log.Prefix  = ("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost   ] ";
            }

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
        }
        public virtual void SetSurface()
        {
            Surface = new Window();
            Surface.ShowInTaskbar = isStandAlone;
            Surface.Background  = Brushes.Black;
            Surface.WindowStyle = WindowStyle.None;
            Surface.ResizeMode  = System.Windows.ResizeMode.NoResize;
            Surface.Width       = Surface.Height = 1; // Will be set on loaded
            Surface.AllowDrop   = AllowDrop;

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
            Surface.Deactivated += Surface_Deactivated;
            Surface.MouseWheel  += Surface_MouseWheel;
            Surface.StateChanged += Surface_StateChanged;
        }
        public virtual void SetOverlay()
        {
            //if (OverlayHandle == new WindowInteropHelper(overlay).Handle) // Don't create it yet
                //DisposeOverlay();

            if (Overlay == null)
                return;

            Overlay.Background = Brushes.Transparent;
            Overlay.WindowStyle = WindowStyle.None;
            Overlay.ResizeMode = System.Windows.ResizeMode.NoResize;
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

            if (isStandAlone)
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
            Overlay.SetBinding(Window.WindowStateProperty,  new Binding(nameof(Surface.WindowState)){ Source = Surface, Mode = System.Windows.Data.BindingMode.TwoWay });

            Overlay.KeyUp       += Overlay_KeyUp;
            Overlay.KeyDown     += Overlay_KeyDown;
            Overlay.Closing     += Overlay_Closing;
            Overlay.MouseLeftButtonDown
                                += Overlay_MouseLeftButtonDown;
            Overlay.MouseLeftButtonUp
                                += Overlay_MouseLeftButtonUp;
            Overlay.MouseMove   += Overlay_MouseMove;

            // Owner will close the overlay
            Overlay.KeyDown += (o, e)   => { if (e.Key == Key.System && e.SystemKey == Key.F4) Surface?.Focus(); };

            //??
            if (Surface.IsVisible)
                Overlay.Visibility = Visibility.Visible;
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
                    Surface.Deactivated -= Surface_Deactivated;
                }

                if (Overlay != null)
                {
                    Overlay.IsVisibleChanged -= Overlay_IsVisibleChanged;                        
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
    }

    public enum AttachedDragMoveMode
    {
        None,
        Owner,
        Self,
    }

    public enum ResizeMode
    {
        None,
        Attached,
        Detached,
        Both
    }

    public enum AvailableWindows
    {
        None,
        Surface,
        Overlay,
        Both
    }
}