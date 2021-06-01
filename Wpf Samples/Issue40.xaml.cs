using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Wpf_Samples
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class Issue40 : Window
    {
        public Player Player { get; set; }

        public Issue40()
        {
            InitializeComponent();
            Master.RegisterFFmpeg(":2");
        }

        VideoView _videoView;

        Player _player;

        static string sampleVideo = (Environment.Is64BitProcess ? "../" : "") + "../../../Sample.mp4";

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            Master.Players.Clear();
            await Task.Run(() => _player?.Dispose());
            if (_videoView!= null) _videoView.WindowFront.Close();

            _videoView = new VideoView();
            _videoView.UpdateDefaultStyle();
            _videoView.ApplyTemplate();

            Config config = new Config();
            config.demuxer.VideoFormatOpt.Add("probesize",       (50 * (long) 1024 * 1024).ToString());
            config.demuxer.VideoFormatOpt.Add("analyzeduration", (10 * (long) 1000 * 1000).ToString());
            _player = new Player(config);

            _videoView.BeginInit();

            _player.Control   = _videoView.FlyleafWF;
            _videoView.Player = _player;

            ContentControl.Content = _videoView;

            _player.Open(sampleVideo);
            _player.OpenCompleted += (o, e2) => { _player.Play(); };
        }
    }
}
