using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafMultiPlayer__WPF_
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public Player PlayerView1 { get => _PlayerView1; set { _PlayerView1 = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerView1))); } }
        Player _PlayerView1;
        public Player PlayerView2 { get => _PlayerView2; set { _PlayerView2 = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerView2))); } }
        Player _PlayerView2;
        public Player PlayerView3 { get => _PlayerView3; set { _PlayerView3 = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerView3))); } }
        Player _PlayerView3;
        public Player PlayerView4 { get => _PlayerView4; set { _PlayerView4 = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerView4))); } }
        Player _PlayerView4;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RotatePlayers { get; set; }

        List<Player> Players = new List<Player>();

        public MainWindow()
        {
            // Initializes Engine (Specifies FFmpeg libraries path which is required)
            Engine.Start(new EngineConfig()
            {
                #if DEBUG
                LogOutput       = ":debug",
                LogLevel        = LogLevel.Debug,
                FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Warn,
                #endif

                PluginsPath     = ":Plugins",
                FFmpegPath      = ":FFmpeg",
            });

            // Creates 4 Players and adds them in the PlayerViews
            for (int i=0; i<4; i++)
            {
                // Use performance wise config for multiple players
                var config = new Config();

                config.Demuxer.BufferDuration = TimeSpan.FromSeconds(5).Ticks; // Reduces RAM as the demuxer will not buffer large number of packets
                config.Decoder.MaxVideoFrames = 2; // Reduces VRAM as video decoder will not keep large queues in VRAM (should be tested for smooth video playback, especially for 4K)
                config.Decoder.VideoThreads = 2; // Reduces VRAM/GPU (should be tested for smooth video playback, especially for 4K)
                // Consider using lower quality streams on normal screen and higher quality on fullscreen (if available)

                Players.Add(new Player(config));
            }

            PlayerView1 = Players[0];
            PlayerView2 = Players[1];
            PlayerView3 = Players[2];
            PlayerView4 = Players[3];
            DataContext = this;
            RotatePlayers = new RelayCommand(RotatePlayersAction);

            InitializeComponent();

            Closing += (o, e) =>
            {
                while (Engine.Players.Count != 0)
                    Engine.Players[0].Dispose();
            };
        }

        private void RotatePlayersAction(object obj)
        {
            // User should review and possible unsubscribe from player/control events

            Player tmp2 = PlayerView2;
            PlayerView2 = PlayerView1;
            PlayerView1 = PlayerView4;
            PlayerView4 = PlayerView3;
            PlayerView3 = tmp2;

            // User should review and possible re-subscribe from player/control events
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Testing removing and re-adding FlyleafME
            var prevPlayer = FlyleafME1.Player;
            MultiPlayer.Children.Remove(FlyleafME1);
            FullScreenWindow fullScreenWindow = new FullScreenWindow();
            fullScreenWindow.FullGrid.Children.Add(FlyleafME1);
            FlyleafME1.Player = prevPlayer;
            fullScreenWindow.Show();
        }
    }
}
