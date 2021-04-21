using System;
using System.Threading.Tasks;
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
            Utils.FFmpeg.RegisterFFmpeg(":2");
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
            Console.WriteLine("Load 1");
            Player1.Open("../../../Sample.mp4");
            Console.WriteLine("Load 2");
            Player2.Open("../../../Sample.mp4");
            Console.WriteLine("Load 3");
        }
    }
}
