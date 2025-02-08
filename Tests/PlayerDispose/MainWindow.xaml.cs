using System;
using System.Threading.Tasks;
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
        public FlyleafME        FlyleafME       { get; set; }
        public FlyleafHost      FlyleafHost     { get; set; } = new FlyleafHost();
        public Player           Player          { get; set; }
        public Config           Config          { get; set; }

        public static string    SampleVideo     { get; set; } = Utils.FindFileBelow("Sample.mp4");

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

            InitializeComponent();

            Closing += (o, e) =>
            FlyleafHost?.Dispose();
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            lock (this)
            {
                Player?.Dispose();
                Player = new Player();

                // If you want to dispose also the video view each time
                //FlyleafHost?.Dispose();
                //FlyleafHost = new FlyleafHost();

                ContentControl.Content = FlyleafHost;

                // Add to test WPF control
                //FlyleafHost.Content = new Flyleaf();

                FlyleafHost.Player = Player;
                Player.OpenAsync(SampleVideo);
            }
        }

        private void Button_Click2(object sender, RoutedEventArgs e)
        {
            lock (this)
            {
                FlyleafWPFControl sample1 = new FlyleafWPFControl();

                sample1.Show();
                System.Threading.Thread.Sleep(100);
                sample1.Player.OpenAsync(SampleVideo);
                Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(5500);

                    // Test Stop/Start within the same instance
                    //for (int i = 1; i < 100; i++)
                    //{
                    //    sample1.Player.Stop();
                    //    sample1.Player.Open(SampleVideo);
                    //    System.Threading.Thread.Sleep(300);
                    //}

                    Application.Current.Dispatcher.BeginInvoke(new Action(() => sample1.Close()));
                });



            }
        }
    }
}
