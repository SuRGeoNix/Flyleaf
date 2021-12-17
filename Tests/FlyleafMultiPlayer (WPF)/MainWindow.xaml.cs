using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
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

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand RotatePlayers { get; set; }

        List<Player> Players = new List<Player>();

        public MainWindow()
        {
            Master.RegisterFFmpeg(":2");

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

            RotatePlayers = new RelayCommand(RotatePlayersAction);

            DataContext = this;

            InitializeComponent();
        }

        private void RotatePlayersAction(object obj)
        {
            // Clockwise rotation

            // User should unsubscribe from all Player events before swaping
            flyleafControl1.UnsubscribePlayer();
            flyleafControl2.UnsubscribePlayer();
            flyleafControl3.UnsubscribePlayer();
            flyleafControl4.UnsubscribePlayer();

            Player old1 = PlayerView1;
            Player old2 = PlayerView2;
            Player old3 = PlayerView3;

            PlayerView1 = PlayerView4;
            PlayerView2 = old1;
            PlayerView3 = old2;
            PlayerView4 = old3;

            // User should subscribe to all Player events after swaping and possible Raise(null) (Flyleaf WPF Control will handle the re-subscribe to the new player automatically)
        }
    }
}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
