using System;
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
            Config config = new Config();
            config.demuxer.VideoFormatOpt.Add("probesize",(50 * (long)1024 * 1024).ToString());
            config.demuxer.VideoFormatOpt.Add("analyzeduration",(10 * (long)1000 * 1000).ToString());
            Player = new Player(config);

            Master.RegisterFFmpeg(":2");
            InitializeComponent();
            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            flyleafControl.VideoView.WinFormsHost.Focus();
        }
    }
}