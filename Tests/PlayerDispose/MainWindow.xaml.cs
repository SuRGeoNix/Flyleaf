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
        public Flyleaf          Flyleaf         { get; set; }
        public VideoView        VideoView       { get; set; }
        public Player           Player          { get; set; }
        public Config           Config          { get; set; }

        public static string    SampleVideo     { get; set; } = Utils.FindFileBelow("Sample.mp4");

        public MainWindow()
        {
            Master.RegisterFFmpeg(":2");

            InitializeComponent();
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            lock (this)
            {
                Player?.Dispose();
                Player = new Player();

                VideoView = new VideoView();
                VideoView.ApplyTemplate();
                ContentControl.Content = VideoView;

                // Add to test WPF control (possible memory leak, settings/tab control? -does not happen on new window?-) 
                //VideoView.Content = new Flyleaf();

                VideoView.Player = Player;
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
