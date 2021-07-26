using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
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

        static string sampleVideo = (Environment.Is64BitProcess ? "../" : "") + "../../../Sample.mp4";

        public Sample3_MultiPlayer()
        {
            // Registering FFmpeg libraries (instead of specific path using default :2 option for Libs\(x86 or x64 dynamic)\FFmpeg from current to base)
            Master.RegisterFFmpeg(":2");

            InitializeComponent();
            DataContext = this;

            // Samples using custom configurations
            Config playerConfig1 = new Config();
            playerConfig1.video.AspectRatio = AspectRatio.Fill;

            // Even more advanced AVFormatOptions for main/Video demuxer (When streams are not fully identified and ffmpeg requires more analyzation)
            //playerConfig1.demuxer.VideoFormatOpt.Add("probesize",(116 * (long)1024 * 1024).ToString());
            //playerConfig1.demuxer.VideoFormatOpt.Add("analyzeduration",(333 * (long)1000 * 1000).ToString());
            Player1 = new Player(playerConfig1);

            Config playerConfig2 = new Config();
            playerConfig2.video.ClearColor = Colors.Orange;
            Player2 = new Player(playerConfig2);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Sample using rtsp stream (slow one)
            //Player1.Open("rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov");

            // Sample HLS videos https://ottverse.com/free-hls-m3u8-test-urls/
            Player1.Open("https://multiplatform-f.akamaihd.net/i/multi/will/bunny/big_buck_bunny_,640x360_400,640x360_700,640x360_1000,950x540_1500,.f4v.csmil/master.m3u8");

            // Sample using a 'custom' IO stream
            Stream customInput = new FileStream(sampleVideo, FileMode.Open);
            Player2.Open(customInput);

            // Sample using different (random) audio device on Player 2
            foreach(var device in Master.AudioMaster.Devices)
                Debug.WriteLine($"Available device: {device}");

            string selectedDevice = Master.AudioMaster.Devices[(new Random()).Next(0, Master.AudioMaster.Devices.Count)];
            Debug.WriteLine($"Selected device: {selectedDevice}");
            Player2.audioPlayer.Device = selectedDevice;

            // Sample performing Seek on Player1 (after 10 seconds -to ensure open completed- in the middle of the movie)
            Thread seekThread = new Thread(() =>
            {
                Thread.Sleep(10000);
                Player1.Seek((int) ((Player1.Session.Movie.Duration/10000) / 2));
            });
            seekThread.IsBackground = true;
            seekThread.Start();
        }
    }
}
