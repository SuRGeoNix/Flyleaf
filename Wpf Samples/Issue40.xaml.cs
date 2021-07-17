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
    /// Testing memory leaks with new instances/dispose and stop/start
    /// </summary>
    public partial class Issue40 : Window
    {
        public Issue40()
        {
            InitializeComponent();
            Master.RegisterFFmpeg(":2");

            _config = new Config();
            //_config.audio.Enabled = false;
            //_config.decoder.HWAcceleration = false;
            _config.demuxer.VideoFormatOpt.Add("probesize",       (50 * (long) 1024 * 1024).ToString());
            _config.demuxer.VideoFormatOpt.Add("analyzeduration", (10 * (long) 1000 * 1000).ToString());
        }

        VideoView _videoView;

        Player _player;
        Flyleaf _flyleaf;
        Config _config;

        static string sampleVideo = (Environment.Is64BitProcess ? "../" : "") + "../../../Sample.mp4";
        //static string sampleVideo = @"c:\root\down\samples\hd\Snow Monkeys in Japan 5K Retina 60p (Ultra HD).mp4";

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            lock (this)
            {
                _player?.Dispose();

                // Remove to test naked control
                _flyleaf?.Dispose();
                _flyleaf = new Flyleaf();

                _videoView = new VideoView();
                _videoView.UpdateDefaultStyle();
                _videoView.ApplyTemplate();

                // Remove to test naked control
                _videoView.Content = _flyleaf;

                _player = new Player(_config);
                _videoView.BeginInit();

                _player.Control = _videoView.FlyleafWF;
                _videoView.Player = _player;

                // Remove to test naked control
                _flyleaf.Player = _player;

                ContentControl.Content = _videoView;

                _player.Open(sampleVideo);

                // Add to test naked control
                //_player.OpenCompleted += (o, e2) => { _player.Play(); };
            }
        }

        private void Button_Click2(object sender, RoutedEventArgs e)
        {
            lock (this)
            {
                Sample1 sample1 = new Sample1();
                sample1.Show();
                sample1.Player.Open(sampleVideo);
                System.Threading.Thread.Sleep(300);

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
