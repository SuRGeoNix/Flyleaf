using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace WpfFlyleafHost
{
    public partial class MultipleAttached : Window
    {
        public string TestDataContext { get; set; } = "MainDataContext";
        public Player Player1 { get; set; } = new Player();
        public Player Player2 { get; set; } = new Player();
        public MultipleAttached()
        {
            InitializeComponent();
            DataContext = this;

            // Forward Surface/Overlay MouseWheel to Host's ScrollViewer
            foreach(FlyleafHost flyleafHost in FixMyScrollSurfaceOverlay.Children)
            {
                flyleafHost.Surface.MouseWheel += FlyleafHost_MouseWheel;
                flyleafHost.Overlay.MouseWheel += FlyleafHost_MouseWheel;
            }

        }

        private void FlyleafHost_MouseWheel(object sender, MouseWheelEventArgs e) => FixMyScrollSurfaceOverlay.RaiseEvent(e);
    }
}
