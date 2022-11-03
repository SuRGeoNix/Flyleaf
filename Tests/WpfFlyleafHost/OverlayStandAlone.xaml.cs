using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfFlyleafHost
{
    /// <summary>
    /// Interaction logic for OverlayStandAlone.xaml
    /// </summary>
    public partial class OverlayStandAlone : Window
    {
        public FlyleafHost FlyleafHost { get; set; } = new FlyleafHost();
        public Player Player { get; set; }

        public OverlayStandAlone()
        {
            Engine.Start(new EngineConfig()
            {
                FFmpegPath = ":FFmpeg"
            });

            Player = new Player();

            FlyleafHost = new FlyleafHost(this)
            {
                KeyBindingsMode = AvailableWindows.Both,
                ResizeMode = AvailableWindows.Both,
                IsAttached = false,
                ResizeOnDetach = true,

                Player = Player
            };

            InitializeComponent();

            DataContext = this;

            Closed += (o, e) => Application.Current.Shutdown();
        }
    }
}
