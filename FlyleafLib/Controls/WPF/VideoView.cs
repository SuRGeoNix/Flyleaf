/* This class is based on https://github.com/videolan/libvlcsharp/tree/3.x/src/LibVLCSharp.WPF */

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;

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
            get { return (Player)GetValue(PlayerProperty) == null ? lastPlayer : (Player)GetValue(PlayerProperty); }
            set { if (!isSwitchingState) SetValue(PlayerProperty, value); }
        }
        internal Player lastPlayer; // TBR: ToggleFullScreen will cause to loose binding and the Player

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

            if (oldPlayer != null && oldPlayer.PlayerId == Player.PlayerId)
                return;

            if (oldPlayer == null)
            {
                Player.VideoView= VideoView;
                Player.Control  = VideoView.FlyleafWF;
            }
            else
                Player.SwapPlayer(oldPlayer, VideoView);

            if (VideoView.ControlRequiresPlayer != null)
                VideoView.ControlRequiresPlayer.Player = Player;
        }
        public VideoView() { DefaultStyleKey = typeof(VideoView); }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (IsDesignMode | disposed) return;
            
            PlayerGrid  = Template.FindName(PART_PlayerGrid, this) as Grid;
            WinFormsHost= Template.FindName(PART_PlayerHost, this) as WindowsFormsHost;
            FlyleafWF   = Template.FindName(PART_PlayerView, this) as FlyleafWF;
            WindowFront = new FlyleafWindow(WinFormsHost);

            if (Content != null && ControlRequiresPlayer == null)
                FindIVideoView((Visual)Content);

            var curContent = Content;
            IsUpdatingContent = true;
            try { Content= null; }
            finally { IsUpdatingContent = false; }

            WindowFront.SetContent((UIElement) curContent);
            WindowFront.DataContext = DataContext;
            WindowFront.VideoView   = this;

            if (Player != null && Player.VideoView == null)
            {
                Player.VideoView= this;
                Player.Control  = FlyleafWF;
                lastPlayer = Player;

                if (ControlRequiresPlayer != null)
                    ControlRequiresPlayer.Player = Player;
            }
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

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            if (newContent != null)
                FindIVideoView((Visual)newContent);

            if (IsUpdatingContent | IsDesignMode | disposed) return;

            if (WindowFront != null)
            {
                IsUpdatingContent = true;
                try { Content = null; }
                finally { IsUpdatingContent = false; }

                WindowFront.SetContent((UIElement)newContent);
            }
        }

        object      oldContent;
        ResizeMode  oldMode;
        WindowStyle oldStyle;
        WindowState oldState;

        internal static bool isSwitchingState;
        public bool FullScreen()
        {
            /* TBR:
             * 1) As we don't remove VideoView but only WinFormsHost we loose the bindings on VideoView (currently only the player)
             *    This causes loosing the Player on VideoView however so far nobody uses this from here
             * 
             * 2) Suspend/Resume Layout failed so far
             */

            if (WindowBack == null) return false;

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
            if (WindowBack == null) return false;

            isSwitchingState = true;

            WindowBack.Content = null;
            WindowBack.Content = oldContent;

            PlayerGrid.Children.Add(WinFormsHost);

            WindowBack.ResizeMode   = oldMode;
            WindowBack.WindowStyle  = oldStyle;
            WindowBack.WindowState  = oldState;

            WindowFront.Activate();

            isSwitchingState = false;
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
                } catch (Exception) { }

                disposed = true;
            }
        }
    }
}