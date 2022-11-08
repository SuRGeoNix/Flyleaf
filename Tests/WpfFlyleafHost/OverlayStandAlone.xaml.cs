using System;
using System.Windows;

using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace WpfFlyleafHost
{
    public partial class OverlayStandAlone : Window
    {
        public FlyleafHost FlyleafHost { get; set; } = new FlyleafHost();
        public Player Player { get; set; } = new Player();

        public OverlayStandAlone()
        {
            FlyleafHost = new FlyleafHost(this)
            {
                KeyBindings = AvailableWindows.Both,
                DetachedResize = AvailableWindows.Both,
                IsAttached = false,

                Player = Player
            };

            InitializeComponent();

            DataContext = this;

            Closed += (o, e) => Application.Current.Shutdown();
        }
    }
}
