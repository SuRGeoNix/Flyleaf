using System;
using System.Threading;
using System.Windows;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace WpfNetFramework
{
    /// <summary>
    /// FlyleafPlayer (WPF Control) Sample
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Flyleaf Player binded to VideoView
        /// </summary>
        public Player Player { get; set; }
        public Config Config { get; set; }

        public MainWindow()
        {
            // Ensures that we have enough worker threads to avoid the UI from freezing or not updating on time
            ThreadPool.GetMinThreads(out int workers, out int ports);
            ThreadPool.SetMinThreads(workers + 6, ports + 6);

            // Registers FFmpeg Libraries
            Master.RegisterFFmpeg(":2");

            // Prepares Player's Configuration
            Config = new Config();
            Config.Demuxer.FormatOpt.Add("probesize",(50 * (long)1024 * 1024).ToString());
            Config.Demuxer.FormatOpt.Add("analyzeduration",(10 * (long)1000 * 1000).ToString());

            // Initializes the Player
            Player = new Player(Config);

            // Allowing VideoView to access our Player
            DataContext = this;

            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Giving access to keyboard events on start up
            //flyleafControl.VideoView.WinFormsHost.Focus();
        }
    }
}
