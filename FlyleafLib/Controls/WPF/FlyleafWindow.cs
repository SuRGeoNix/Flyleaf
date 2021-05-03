/* This class is based on https://github.com/videolan/libvlcsharp/tree/3.x/src/LibVLCSharp.WPF */

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;

namespace FlyleafLib.Controls.WPF
{
    public class FlyleafWindow : Window
    {
        public   Window             WindowBack { get; private set; }
        readonly WindowsFormsHost   windowsFormsHost;
        readonly Point              _zeroPoint   = new Point(0, 0);

        private readonly Grid grid = new Grid();

        internal void SetContent(UIElement newContent)
        {
            grid.Children.Clear();
            if (newContent == null) return;
            grid.Children.Add(newContent);
        }

        public FlyleafWindow(WindowsFormsHost windowsFormsHost)
        {
            //Console.WriteLine("FlyleafWindow");

            Title               = "FlyleafWindow";
            Height              = 300;
            Width               = 300;
            WindowStyle         = WindowStyle.None;
            Background          = Brushes.Transparent;
            ResizeMode          = ResizeMode.NoResize;
            AllowsTransparency  = true;
            ShowInTaskbar       = false;
            Content             = grid;

            this.windowsFormsHost                       = windowsFormsHost;
            this.windowsFormsHost.DataContextChanged    += WFH_DataContextChanged;
            this.windowsFormsHost.Loaded                += WFH_Loaded;
            this.windowsFormsHost.Unloaded              += WFH_Unloaded;            
        }

        void WFH_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { Console.WriteLine("WFH_DataContextChanged"); DataContext = e.NewValue; }

        void WFH_Unloaded(object sender, RoutedEventArgs e)
        {
            //Console.WriteLine("WFH_Unloaded");
            
            windowsFormsHost.SizeChanged    -= Wndhost_SizeChanged;

            if (WindowBack != null)
            {
                WindowBack.Closing          -= Wndhost_Closing;
                WindowBack.LocationChanged  -= Wndhost_LocationChanged;
            }

            Hide();
        }

        void WFH_Loaded(object sender, RoutedEventArgs e)
        {
            //Console.WriteLine("WFH_Loaded");

            if (WindowBack != null)
            {
                WindowBack.Closing          += Wndhost_Closing;
                WindowBack.LocationChanged  += Wndhost_LocationChanged;
                windowsFormsHost.SizeChanged+= Wndhost_SizeChanged;
            }
            else
            {
                WindowBack = GetWindow(windowsFormsHost);
                if (WindowBack == null) return;

                Owner = WindowBack;

                WindowBack.Closing          += Wndhost_Closing;
                windowsFormsHost.SizeChanged+= Wndhost_SizeChanged;
                WindowBack.LocationChanged  += Wndhost_LocationChanged;
            }

            var locationFromScreen  = windowsFormsHost.PointToScreen(_zeroPoint);
            var source              = PresentationSource.FromVisual(WindowBack);
            var targetPoints        = source.CompositionTarget.TransformFromDevice.Transform(locationFromScreen);
            Left                    = targetPoints.X;
            Top                     = targetPoints.Y;
            var size                = new Point(windowsFormsHost.ActualWidth, windowsFormsHost.ActualHeight);
            Height                  = size.Y;
            Width                   = size.X;
            Show();
            WindowBack.Focus();
        }

        public void Wndhost_LocationChanged(object sender, EventArgs e)
        {
            //Console.WriteLine("Wndhost_LocationChanged");
            if (WindowBack == null) return;

            var locationFromScreen  = windowsFormsHost.PointToScreen(_zeroPoint);
            var source              = PresentationSource.FromVisual(WindowBack);
            var targetPoints        = source.CompositionTarget.TransformFromDevice.Transform(locationFromScreen);
            Left                    = targetPoints.X;
            Top                     = targetPoints.Y;
        }

        public void Wndhost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //Console.WriteLine("Wndhost_SizeChanged");
            var source = PresentationSource.FromVisual(WindowBack);
            if (source == null)
            {
                return;
            }

            var locationFromScreen  = windowsFormsHost.PointToScreen(_zeroPoint);
            var targetPoints        = source.CompositionTarget.TransformFromDevice.Transform(locationFromScreen);
            Left                    = targetPoints.X;
            Top                     = targetPoints.Y;
            var size                = new Point(windowsFormsHost.ActualWidth, windowsFormsHost.ActualHeight);
            Height                  = size.Y;
            Width                   = size.X;
        }

        void Wndhost_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //Console.WriteLine("Wndhost_Closing");
            Close();

            windowsFormsHost.DataContextChanged  -= WFH_DataContextChanged;
            windowsFormsHost.Loaded              -= WFH_Loaded;
            windowsFormsHost.Unloaded            -= WFH_Unloaded;
        }

        protected override void OnKeyDown(KeyEventArgs e) { if (e.Key == Key.System && e.SystemKey == Key.F4) WindowBack?.Focus(); }
    }
}