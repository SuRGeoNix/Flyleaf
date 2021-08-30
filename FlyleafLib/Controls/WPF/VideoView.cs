/* This class is based on https://github.com/videolan/libvlcsharp/tree/3.x/src/LibVLCSharp.WPF */

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;

using FlyleafLib.MediaPlayer;
using FlyleafWF = FlyleafLib.Controls.Flyleaf;

namespace FlyleafLib.Controls.WPF
{
    [TemplatePart(Name = PART_PlayerGrid, Type = typeof(Grid))]
    [TemplatePart(Name = PART_PlayerHost, Type = typeof(WindowsFormsHost))]
    [TemplatePart(Name = PART_PlayerView, Type = typeof(FlyleafWF))]
    public class VideoView : ContentControl, IVideoView
    {
        private const string    PART_PlayerGrid = "PART_PlayerGrid";
        private const string    PART_PlayerHost = "PART_PlayerHost";
        private const string    PART_PlayerView = "PART_PlayerView";
        private IVideoView      ControlRequiresPlayer; // kind of dependency injection (used for WPF control)
        private bool            IsUpdatingContent;

        private bool            IsDesignMode    => (bool)DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;
        public WindowsFormsHost WinFormsHost    { get; set; }   // Airspace: catch back events (key events working, mouse event not working ... the rest not tested)
        public FlyleafWindow    WindowFront     { get; set; }   // Airspace: catch any front events
        public Window           WindowBack      => WindowFront.WindowBack;
        public FlyleafWF        FlyleafWF       { get; set; }   // Airspace: catch any back events, the problem is that they don't speak the same language (WinForms/WPF)
        public Grid             PlayerGrid      { get; set; }
        public Player           Player
        {
            get { return (Player)GetValue(PlayerProperty); }
            set { SetValue(PlayerProperty, value); }
        }

        public static readonly DependencyProperty PlayerProperty =
            DependencyProperty.Register("Player", typeof(Player), typeof(VideoView), new PropertyMetadata(null, OnPlayerChanged));

        private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue == null) return;

            VideoView VideoView = d as VideoView;
            if (VideoView.FlyleafWF == null) return;

            Player Player       = e.NewValue as Player;
            Player.VideoView    = VideoView;
            Player.Control      = VideoView.FlyleafWF;
            if (VideoView.ControlRequiresPlayer != null) VideoView.ControlRequiresPlayer.Player = Player;
        }

        public VideoView() { DefaultStyleKey = typeof(VideoView); }

        public override void OnApplyTemplate()
        {
            base.ApplyTemplate();

            if (IsDesignMode | disposed) return;
            
            PlayerGrid  = Template.FindName(PART_PlayerGrid, this) as Grid;
            WinFormsHost= Template.FindName(PART_PlayerHost, this) as WindowsFormsHost;
            FlyleafWF   = Template.FindName(PART_PlayerView, this) as FlyleafWF;
            WindowFront = new FlyleafWindow(WinFormsHost);

            var curContent = Content;
            IsUpdatingContent = true;
            try { Content= null; }
            finally { IsUpdatingContent = false; }

            WindowFront.SetContent((UIElement) curContent);
            WindowFront.DataContext = DataContext;
            WindowFront.VideoView   = this;

            if (curContent != null && curContent is IVideoView) 
                ControlRequiresPlayer = (IVideoView)curContent;

            if (Player != null && Player.VideoView == null)
            {
                Player.VideoView= this;
                Player.Control  = FlyleafWF;
                if (ControlRequiresPlayer != null) ControlRequiresPlayer.Player = Player;
            }
        }
        protected override void OnContentChanged(object oldContent, object newContent)
        {
            if (IsUpdatingContent | IsDesignMode | disposed) return;

            if (WindowFront != null)
            {
                IsUpdatingContent = true;
                try { Content = null; }
                finally { IsUpdatingContent = false; }

                WindowFront.SetContent((UIElement)newContent);
            }
        }

        public bool IsFullScreen { get ; private set; }

        object  oldContent;
        ResizeMode oldMode;
        WindowStyle oldStyle;
        WindowState oldState;
        public bool FullScreen()
        {
            if (WindowBack == null) return false;

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

            IsFullScreen = true;
            //WindowFront.Activate(); // GPU performance issue? (renderer should be the active window on fullscreen? but only when it goes to fullscreen, after that is fine if you activate front window) TBR...

            WindowBack.Visibility = Visibility.Visible;
            
            return true;
        }

        public bool NormalScreen()
        {
            if (WindowBack == null) return false;

            WindowBack.Content = null;
            WindowBack.Content = oldContent;

            PlayerGrid.Children.Add(WinFormsHost);

            WindowBack.ResizeMode   = oldMode;
            WindowBack.WindowStyle  = oldStyle;
            WindowBack.WindowState  = oldState;

            IsFullScreen = false;
            WindowFront.Activate();

            return true;
        }

        bool disposed = false;
        internal void Dispose()
        {
            lock (this)
            {
                if (disposed) return;

                try
                {
                    if (FlyleafWF != null)
                    {
                        FlyleafWF.Dispose();
                        FlyleafWF.Player = null;
                        FlyleafWF = null;
                    }
                
                    if (Player != null)
                    {
                        Player.VideoView = null;
                        Player._Control = null;
                        Player.Dispose();
                    }

                    Resources.MergedDictionaries.Clear();
                    Resources.Clear();
                    Template.Resources.MergedDictionaries.Clear();
                    Content = null;
                    DataContext = null;
                    PlayerGrid.Children.Clear();
                    PlayerGrid = null;
                } catch (Exception) { }

                disposed = true;
            }
        }
    }
}