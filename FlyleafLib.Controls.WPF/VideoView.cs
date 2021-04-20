/* This class is based on https://github.com/videolan/libvlcsharp/tree/3.x/src/LibVLCSharp.WPF */

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;

using FlyleafLib.MediaPlayer;
using FlyleafWF = FlyleafLib.Controls.Flyleaf;

namespace FlyleafLib.Controls.WPF
{
    [TemplatePart(Name = PART_PlayerHost, Type = typeof(WindowsFormsHost))]
    [TemplatePart(Name = PART_PlayerView, Type = typeof(FlyleafWF))]
    public class VideoView : ContentControl, IVideoView
    {
        private const string    PART_PlayerHost = "PART_PlayerHost";
        private const string    PART_PlayerView = "PART_PlayerView";

        bool                    IsUpdatingContent;
        WindowsFormsHost        windowsFormsHost;
        private bool            IsDesignMode => (bool)DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;

        public Player           Player      { get; private set; }
        public FlyleafWindow    WindowFront { get; set; }
        public Window           WindowBack  => WindowFront.WindowBack;

        public VideoView FlyleafView
        {
            get { return (VideoView)GetValue(FlyleafViewProperty); }
            set { SetValue(FlyleafViewProperty, value); }
        }
        public static readonly DependencyProperty FlyleafViewProperty =
            DependencyProperty.Register("FlyleafView", typeof(VideoView), typeof(VideoView), new PropertyMetadata(null));

        public VideoView() { DefaultStyleKey = typeof(VideoView); }
        public override void OnApplyTemplate()
        {
            base.ApplyTemplate();

            if (IsDesignMode) return;

            FlyleafWF flyleafWF = (FlyleafWF) Template.FindName(PART_PlayerView, this);
            Player              = flyleafWF.Player;
            windowsFormsHost    = (WindowsFormsHost) Template.FindName(PART_PlayerHost, this);
            WindowFront         = new FlyleafWindow(windowsFormsHost);

            var curContent = Content;
            IsUpdatingContent = true;
            try { Content = null; }
            finally { IsUpdatingContent = false; }

            // TBR: Parsing the Player and VideoView to Control/ViewModel
            if (curContent != null && curContent is IVideoView) ((IVideoView)curContent).FlyleafView = this;
            WindowFront.SetContent((UIElement) curContent);
            WindowFront.DataContext = DataContext;
            FlyleafView = this;
            Player.Start();
        }
        protected override void OnContentChanged(object oldContent, object newContent)
        {
            if (IsUpdatingContent | IsDesignMode) return;

            if (WindowFront != null)
            {
                IsUpdatingContent = true;
                try { Content = null; }
                finally { IsUpdatingContent = false; }

                WindowFront.SetContent((UIElement)newContent);
            }
        }


        public bool IsFullScreen { get ; private set; }

        object  oldContent, oldParent;
        public bool FullScreen()
        {
            if (WindowBack == null) return false;

            oldParent = Parent;

            if (Parent is StackPanel)
                ((StackPanel)Parent).Children.Remove(this);
            else if (Parent is Grid)
                ((Grid)Parent).Children.Remove(this);
            else
                return false;

            oldContent  = WindowBack.Content;
            WindowBack.Content = this;

            WindowBack.ResizeMode   = ResizeMode.NoResize;
            WindowBack.WindowStyle  = WindowStyle.None;
            WindowBack.WindowState  = WindowState.Maximized;

            IsFullScreen = true;
            WindowFront.Activate();

            return true;
        }

        public bool NormalScreen()
        {
            if (oldParent == null || WindowBack == null) return false;

            WindowBack.Content = null;
            WindowBack.Content = oldContent;

            if (oldParent is StackPanel)
                ((StackPanel)oldParent).Children.Add(this);
            else if (oldParent is Grid)
                ((Grid)oldParent).Children.Add(this);
            else
                return false;

            WindowBack.ResizeMode   = ResizeMode.CanResize;
            WindowBack.WindowStyle  = WindowStyle.SingleBorderWindow;
            WindowBack.WindowState  = WindowState.Normal;

            IsFullScreen = false;
            WindowFront.Activate();

            return true;
        }
    }
}