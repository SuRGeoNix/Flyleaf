using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace Wpf_Samples
{
    /// <summary>
    /// Interaction logic for Sample3_MultiPlayer.xaml
    /// </summary>
    public partial class Sample3_MultiPlayer : Window
    {
        public Player       Player1      { get ; set; }
        public Player       Player2      { get ; set; }

        public Sample3_MultiPlayer()
        {
            Master.RegisterFFmpeg(":2");
            InitializeComponent();

            DataContext = this;

            Config playerConfig1 = new Config();
            playerConfig1.video.AspectRatio = AspectRatio.Fill;
            Player1 = new Player(playerConfig1);

            Config playerConfig2 = new Config();
            playerConfig2.video.ClearColor = Colors.Orange;
            Player2 = new Player(playerConfig2);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Player1.Open("../../../Sample.mp4");

            // Sample using different audio device on Player 2
            //Player2.audioPlayer.Device = "4 - 24G2W1G4 (AMD High Definition Audio Device)";

            // Sample using a 'custom' stream input
            Player2.Open(new FileStream("../../../Sample.mp4", FileMode.Open));
        }
    }
}
