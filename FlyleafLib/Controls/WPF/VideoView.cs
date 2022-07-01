/* This class is based on https://github.com/videolan/libvlcsharp/tree/3.x/src/LibVLCSharp.WPF */

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using FlyleafLib.MediaPlayer;
using FlyleafWF = FlyleafLib.Controls.Flyleaf;

namespace FlyleafLib.Controls.WPF
{
    [TemplatePart(Name = PART_PlayerGrid, Type = typeof(Grid))]
    [TemplatePart(Name = PART_PlayerHost, Type = typeof(WindowsFormsHostEx))]
    [TemplatePart(Name = PART_PlayerView, Type = typeof(FlyleafWF))]
    public class VideoView : ContentControl
    {
        public VideoView() { DefaultStyleKey = typeof(VideoView); }

        private const string    PART_PlayerGrid = "PART_PlayerGrid";
        private const string    PART_PlayerHost = "PART_PlayerHost";
        private const string    PART_PlayerView = "PART_PlayerView";
        private IVideoView      ControlRequiresPlayer; // kind of dependency injection (used for WPF control)
        
        readonly Point          _zeroPoint      = new Point(0, 0);
        private bool            IsDesignMode    => (bool)DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;
        private bool            IsUpdatingContent;

        public FlyleafWF        FlyleafWF       { get; set; }   // Airspace: catch any back events, the problem is that they don't speak the same language (WinForms/WPF)
        public Grid             PlayerGrid      { get; set; }
        
        public Window           WindowBack      { get; set; }
        public WindowsFormsHostEx
                                WinFormsHost    { get; set; }   // Airspace: catch back events (key events working, mouse event not working ... the rest not tested)
        public Window           WindowFront     { get; set; }   // Airspace: catch any front events
        public int              UniqueId        { get; set; }


        #region Player Set / Swap
        private Player          lastPlayer; // TBR: ToggleFullScreen will cause to loose binding and the Player
        public Player           Player
        {
            get { return (Player)GetValue(PlayerProperty) == null ? lastPlayer : (Player)GetValue(PlayerProperty); }
            set { if (!isSwitchingState) SetValue(PlayerProperty, value); }
        }
        public static readonly DependencyProperty PlayerProperty =
            DependencyProperty.Register("Player", typeof(Player), typeof(VideoView), new PropertyMetadata(null, OnPlayerChanged));

        private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoView VideoView = d as VideoView;

            if (e.NewValue == null || isSwitchingState)
                return;

            if (VideoView.FlyleafWF == null)
                return;

            Player Player   = e.NewValue as Player;
            Player oldPlayer= e.OldValue as Player;

            VideoView.lastPlayer = Player;

            if (oldPlayer == null)
            {
                VideoView.UniqueId = Player.PlayerId;
                Player.VideoView= VideoView;
                Player.Control  = VideoView.FlyleafWF;

                if (VideoView.ControlRequiresPlayer != null)
                    VideoView.ControlRequiresPlayer.Player = Player;
            }
        }
        #endregion

        #region Initialization / Content Change / (Un)Load
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (IsDesignMode | disposed) return;
            
            PlayerGrid  = Template.FindName(PART_PlayerGrid, this) as Grid;
            WinFormsHost= Template.FindName(PART_PlayerHost, this) as WindowsFormsHostEx;
            FlyleafWF   = Template.FindName(PART_PlayerView, this) as FlyleafWF;

            IsVisibleChanged                += VideoView_IsVisibleChanged;
            WinFormsHost.DataContextChanged += WFH_DataContextChanged;
            WinFormsHost.Loaded             += WFH_Loaded;
            WinFormsHost.Unloaded           += WFH_Unloaded;

            if (Content != null && ControlRequiresPlayer == null)
                FindIVideoView((Visual)Content);

            var curContent = Content;
            IsUpdatingContent = true;
            try { Content= null; }
            finally { IsUpdatingContent = false; }
            
            if (curContent != null)
            {
                WindowFront = new Window();
                WindowFront.Title               = "FlyleafWindow";
                WindowFront.Height              = 300;
                WindowFront.Width               = 300;
                WindowFront.WindowStyle         = WindowStyle.None;
                WindowFront.Background          = Brushes.Transparent;
                WindowFront.ResizeMode          = ResizeMode.NoResize;
                WindowFront.AllowsTransparency  = true;
                WindowFront.ShowInTaskbar       = false;
                WindowFront.Content             = curContent;
                WindowFront.Tag                 = this;
                WindowFront.KeyDown += (o, e)   => { if (e.Key == Key.System && e.SystemKey == Key.F4) WindowBack?.Focus(); };
                WindowFront.Loaded  += (o, e)   => { WinFormsHost.WinFrontHandle = new WindowInteropHelper(WindowFront).Handle; WinFormsHost.RefreshFront(); };
            }

            if (WindowFront != null)
            {
                WindowFront.DataContext = DataContext;
                WindowFront.Owner = WindowBack;
            }

            if (Player != null && Player.VideoView == null)
            {
                UniqueId = Player.PlayerId;
                Player.VideoView= this;
                Player.Control  = FlyleafWF;
                lastPlayer = Player;

                if (ControlRequiresPlayer != null)
                    ControlRequiresPlayer.Player = Player;
            }
        }

        private void VideoView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (isSwitchingState)
                return;

            if (Visibility == Visibility.Visible)
                WindowFront?.Show();
            else
                WindowFront?.Hide();
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            if (newContent != null)
                FindIVideoView((Visual)newContent);

            if (IsUpdatingContent | IsDesignMode | disposed) return;

            if (WindowFront != null)
            {
                IsUpdatingContent = true;
                try { Content = null; WindowFront.Content = null; }
                finally { IsUpdatingContent = false; }

                WindowFront.Content = newContent;
            }
        }
        void WFH_Loaded(object sender, RoutedEventArgs e)
        {
            WindowBack = Window.GetWindow(WinFormsHost);

            if (WindowBack != null)
                WindowBack.Closed += WindowBack_Closed;

            if (WindowBack == null || WinFormsHost == null || WindowFront == null)
                return;
            
            WindowFront.Owner            = WindowBack;
            WindowBack.LocationChanged  += RefreshFrontPosition;
            WinFormsHost.LayoutUpdated  += RefreshFrontPosition;
            WinFormsHost.SizeChanged    += WFH_SizeChanged;

            var locationFromScreen  = WinFormsHost.PointToScreen(_zeroPoint);
            var source              = PresentationSource.FromVisual(WindowBack);
            var targetPoints        = source.CompositionTarget.TransformFromDevice.Transform(locationFromScreen);
            WindowFront.Left        = targetPoints.X;
            WindowFront.Top         = targetPoints.Y;
            var size                = new Point(WinFormsHost.ActualWidth, WinFormsHost.ActualHeight);
            WindowFront.Height      = size.Y;
            WindowFront.Width       = size.X;

            if (Visibility == Visibility.Visible)
            {
                WindowFront.Show();
                //WindowBack.Focus();
            }
        }
        void WFH_Unloaded(object sender, RoutedEventArgs e)
        {
            WinFormsHost.SizeChanged -= WFH_SizeChanged;
            WinFormsHost.LayoutUpdated -= RefreshFrontPosition;
            if (WindowBack != null)
            {
                WindowBack.Closed -= WindowBack_Closed;
                WindowBack.LocationChanged -= RefreshFrontPosition;
            }

            WindowFront?.Hide();
        }
        public void FindIVideoView(Visual parent)
        {
            if (parent is IVideoView)
            {
                ControlRequiresPlayer = (IVideoView)parent;
                return;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                Visual visual  = (Visual)VisualTreeHelper.GetChild(parent, i);
                
                if (visual == null)
                    break;

                if (visual is IVideoView)
                {
                    ControlRequiresPlayer = (IVideoView)visual;
                    break;
                }

                FindIVideoView(visual);
            }
        }
        #endregion

        #region Foreground Window follows Background/WinFormsHost
        void WFH_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue == null || WindowFront == null || isSwitchingState)
                return;

            WindowFront.DataContext = e.NewValue;
        }
        void WFH_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var source = PresentationSource.FromVisual(WindowBack);
            if (source == null)
                return;

            var locationFromScreen  = WinFormsHost.PointToScreen(_zeroPoint);
            var targetPoints        = source.CompositionTarget.TransformFromDevice.Transform(locationFromScreen);
            WindowFront.Left        = targetPoints.X;
            WindowFront.Top         = targetPoints.Y;
            var size                = new Point(WinFormsHost.ActualWidth, WinFormsHost.ActualHeight);
            WindowFront.Height      = size.Y;
            WindowFront.Width       = size.X;
        }
        void RefreshFrontPosition(object sender, EventArgs e) // TBR: Visible area can be less than actual size
        {
            try
            {
                var locationFromScreen  = WinFormsHost.PointToScreen(_zeroPoint);
                var source              = PresentationSource.FromVisual(WindowBack);
                var targetPoints        = source.CompositionTarget.TransformFromDevice.Transform(locationFromScreen);
                WindowFront.Left        = targetPoints.X;
                WindowFront.Top         = targetPoints.Y;
            } catch { } // When WinFormsHost is not visible (mainly on ToggleFullscreen)
        }
        #endregion

        #region Full Screen
        object oldContent;
        ResizeMode  oldMode;
        WindowStyle oldStyle;
        WindowState oldState;
        static bool isSwitchingState;

        public bool FullScreen()
        {
            /* TBR:
             * 1) As we don't remove VideoView but only WinFormsHost we loose the bindings on VideoView (currently only the player)
             *    This causes loosing the Player on VideoView however so far nobody uses this from here
             * 
             * 2) Suspend/Resume Layout failed so far
             */

            if (WindowBack == null)
                return false;

            isSwitchingState = true;

            WindowBack.Visibility = Visibility.Hidden;
            PlayerGrid.Children.Remove(WinFormsHost);
            oldContent = WindowBack.Content;
            WindowBack.Content = WinFormsHost;

            oldMode     = WindowBack.ResizeMode;
            oldStyle    = WindowBack.WindowStyle;
            oldState    = WindowBack.WindowState;

            WindowBack.ResizeMode   = ResizeMode. NoResize;
            WindowBack.WindowStyle  = WindowStyle.None;
            WindowBack.WindowState  = WindowState.Maximized;
            WindowBack.Visibility   = Visibility.Visible;

            isSwitchingState = false;

            return true;
        }
        public bool NormalScreen()
        {
            if (WindowBack == null)
                return false;

            isSwitchingState = true;

            WindowBack.Content = null;
            WindowBack.Content = oldContent;

            PlayerGrid.Children.Add(WinFormsHost);

            WindowBack.ResizeMode   = oldMode;
            WindowBack.WindowStyle  = oldStyle;
            WindowBack.WindowState  = oldState;

            WindowFront?.Activate();

            isSwitchingState = false;

            return true;
        }
        #endregion

        #region Disposal
        bool disposed = false;
        internal void Dispose()
        {
            lock (this)
            {
                if (disposed) return;

                try
                {
                    if (WindowFront != null)
                        WindowFront.Close();

                    if (FlyleafWF != null)
                    {
                        FlyleafWF.Dispose();
                        FlyleafWF.Player = null;
                        FlyleafWF = null;
                    }
                
                    if (Player != null)
                    {
                        Player.Dispose();
                        Player.VideoView = null;
                        Player._Control = null;
                    }

                    Resources.MergedDictionaries.Clear();
                    Resources.Clear();
                    Template.Resources.MergedDictionaries.Clear();
                    Content = null;
                    DataContext = null;
                    PlayerGrid.Children.Clear();
                    PlayerGrid = null;

                } catch (Exception e) { Debug.WriteLine("VideoView: " + e.Message); }

                disposed = true;
            }
        }
        void WindowBack_Closed(object sender, EventArgs e)
        {
            Dispose();
        }
        #endregion
    }
}