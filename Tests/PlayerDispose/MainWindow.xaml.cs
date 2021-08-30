using System;
using System.Windows;
using System.Windows.Controls;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace DisposePlayer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Flyleaf          Flyleaf         { get; set; }
        public VideoView        VideoView       { get; set; }
        public Player           Player          { get; set; }
        public Config           Config          { get; set; }

        public static string    SampleVideo     { get; set; } = Utils.FileExistsBelow("Sample.mp4");

        public MainWindow()
        {
            // Registers FFmpeg Libraries
            Master.RegisterFFmpeg(":2");

            // Prepares Player's Configuration
            Config = new Config();
            Config.Demuxer.FormatOpt.Add("probesize",(50 * (long)1024 * 1024).ToString());
            Config.Demuxer.FormatOpt.Add("analyzeduration",(10 * (long)1000 * 1000).ToString());

            // Initializes the Player
            Player = new Player(Config);

            InitializeComponent();
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            lock (this)
            {
                // Disposing from non UI thread
                if (Player != null) System.Threading.Tasks.Task.Run(() => { Player?.Dispose(); });

                // Disposing from UI thread
                //Player?.Dispose();

                // Remove to test naked control
                Flyleaf = new Flyleaf();

                VideoView = new VideoView();
                VideoView.UpdateDefaultStyle();
                VideoView.ApplyTemplate();

                // Remove to test naked control
                VideoView.Content = Flyleaf;

                Player = new Player(Config);
                VideoView.BeginInit();

                Player.Control = VideoView.FlyleafWF;
                VideoView.Player = Player;

                // Remove to test naked control
                Flyleaf.Player = Player;

                ContentControl.Content = VideoView;

                Player.OpenAsync(SampleVideo);

                // Add to test naked control
                //Player.OpenCompleted += (o, e2) => { Player.Play(); };
            }
        }

        private void Button_Click2(object sender, RoutedEventArgs e)
        {
            lock (this)
            {
                FlyleafWPFControl sample1 = new FlyleafWPFControl();

                //Sample1 sample1 = new Sample1();
                sample1.Show();
                sample1.Player.OpenAsync(SampleVideo);
                System.Threading.Thread.Sleep(500);

                // Test Stop/Start within the same instance
                //for (int i=1; i<100; i++)
                //{
                //    sample1.Player.Stop();
                //    sample1.Player.Open(sampleVideo);
                //    System.Threading.Thread.Sleep(300);
                //}

                sample1.Close();
            }
        }
    }
}
