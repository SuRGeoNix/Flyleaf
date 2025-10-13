using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace WpfFlyleafHost
{
    /// <summary>
    /// Testing Interactive Zoom with another child FlyleafHost by 'replicating' the main's FlyleafHost player (has been deprecated for now)
    /// </summary>
    public partial class OverlayStandAlone : Window
    {
        public FlyleafHost  FlyleafHost { get; set; }
        public Player       Player      { get; set; } = new Player();

        public OverlayStandAlone()
        {
            FlyleafHost = new FlyleafHost(this)
            {
                KeyBindings         = AvailableWindows.Both,
                DetachedResize      = AvailableWindows.Both,
                DetachedDragMove    = AvailableWindows.Both,
                ToggleFullScreenOnDoubleClick
                                    = AvailableWindows.Both,
                KeepRatioOnResize   = true,
                CornerRadius        = new CornerRadius(30),
                Player              = Player
            };

            InitializeComponent();

            DataContext = this;
            Player.Config.Video.VideoProcessor = FlyleafLib.VideoProcessors.Flyleaf; // TBR: D3D11 has bad performance with Zoom and Replica
            //Player.PropertyChanged += Player_PropertyChanged;
        }

        // Replica Implementation (Disabled for now)
        //#region Interactive Zoom Implementation
        //Point mouseLeftDownPoint;
        //bool isCapturing;
        //private void ZoomRect_MouseDown(object sender, MouseButtonEventArgs e)
        //{
        //    e.Handled = true; // prevent drag move of the surface

        //    mouseLeftDownPoint = e.MouseDevice.GetPosition(ZoomRect);
        //    isCapturing = true;
        //    ZoomRect.CaptureMouse();
        //}
        //private void ZoomRect_MouseMove(object sender, MouseEventArgs e)
        //{
        //    if (e.MouseDevice.LeftButton != MouseButtonState.Pressed || !isCapturing)
        //        return;

        //    var curPos = e.MouseDevice.GetPosition(ZoomRect);

        //    if (curPos == mouseLeftDownPoint)
        //        return;

        //    double newX = ZoomRect.Margin.Left + curPos.X - mouseLeftDownPoint.X;
        //    double newY = ZoomRect.Margin.Top + curPos.Y - mouseLeftDownPoint.Y;
        //    newX = Math.Min(Math.Max(newX, 0), FlyleafHostReplica.Width - ZoomRect.Width);
        //    newY = Math.Min(Math.Max(newY, 0), FlyleafHostReplica.Height - ZoomRect.Height);
        //    ZoomRect.Margin = new(newX, newY, ZoomRect.Margin.Right, ZoomRect.Margin.Bottom);

        //    double todoX = (ZoomRect.Margin.Left + ((ZoomRect.Margin.Left / (FlyleafHostReplica.Surface.Width - ZoomRect.Width)) * ZoomRect.Width)) / FlyleafHostReplica.Surface.Width;
        //    double todoY = (ZoomRect.Margin.Top + ((ZoomRect.Margin.Top / (FlyleafHostReplica.Surface.Height - ZoomRect.Height)) * ZoomRect.Height)) / FlyleafHostReplica.Surface.Height;
        //    Player.renderer.SetZoomCenter(new(Math.Min(Math.Max((float) (todoX), 0.0f), 0.99f), Math.Min(Math.Max((float) (todoY), 0.0f), 0.99f)), true);
        //}
        //private void ZoomRect_MouseUp(object sender, MouseButtonEventArgs e)
        //{
        //    if (isCapturing)
        //        ZoomRect.ReleaseMouseCapture();

        //    isCapturing = false;
        //}
        //private void ZoomRect_LostMouseCapture(object sender, MouseEventArgs e)
        //{
        //    if (isCapturing)
        //        ZoomRect.ReleaseMouseCapture();

        //    isCapturing = false;
        //}
        //private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        //{
        //    if (e.PropertyName == nameof(Player.Zoom))
        //    {
        //        var zoom = Player.Zoom / 100.0;
        //        if (zoom < 1)
        //        {
        //            ZoomRect.Margin = new(0);
        //            ZoomRect.Width = FlyleafHostReplica.Surface.Width;
        //            ZoomRect.Height = FlyleafHostReplica.Surface.Height;
        //        }
        //        else
        //        {
        //            ZoomRect.Width  = FlyleafHostReplica.Surface.Width / zoom;
        //            ZoomRect.Height = FlyleafHostReplica.Surface.Height / zoom;
        //        }
        //    }
        //}
        //#endregion
    }
}
