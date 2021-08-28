using System;
using System.Threading;
using System.Windows;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace Wpf_Samples
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Sample1 : Window
    {
        public Player       Player      { get ; set; }

        public Sample1()
        {
            // Might not required, but it could cause UI issues if we have very few workers
            ThreadPool.GetMinThreads(out int workers, out int ports);
            ThreadPool.SetMinThreads(workers + 6, ports + 6);

            Master.RegisterFFmpeg(":2");

            Config config = new Config();
            //config.Player.Usage = Usage.Audio;
            //config.Player.Usage = Usage.LowLatencyVideo;
            config.Demuxer.FormatOpt.Add("probesize",(50 * (long)1024 * 1024).ToString());
            config.Demuxer.FormatOpt.Add("analyzeduration",(10 * (long)1000 * 1000).ToString());
            Player = new Player(config);

            InitializeComponent();
            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            flyleafControl.VideoView.WinFormsHost.Focus();
        }
    }
}