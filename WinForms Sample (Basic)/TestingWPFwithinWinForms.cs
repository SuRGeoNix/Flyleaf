using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Interop;

namespace WinForms_Sample__Basic_
{
    /// <summary>
    /// Just for testing, ideally should use a new FlyleafWindow for WinForms to host Flyleaf WPF Control (no need for videoview here)
    /// </summary>
    public partial class TestingWPFwithinWinForms : Form
    {
        Player _player;
        Flyleaf _flyleaf;
        Config _config;

        static string sampleVideo = (Environment.Is64BitProcess ? "../" : "") + "../../../Sample.mp4";

        public TestingWPFwithinWinForms()
        {
            Master.RegisterFFmpeg(":2");
            InitializeComponent();

            _config = new Config();
            //_config.audio.Enabled = false;
            //_config.decoder.HWAcceleration = false;
            _config.demuxer.FormatOpt.Add("probesize",       (50 * (long) 1024 * 1024).ToString());
            _config.demuxer.FormatOpt.Add("analyzeduration", (10 * (long) 1000 * 1000).ToString());
        }

        private void Form2_Load(object sender, EventArgs e)
        {
            _videoView = new VideoView();
            _videoView.UpdateDefaultStyle();
            _videoView.ApplyTemplate();

            #region Required code as WindowFront doesn't know WindowsBack here
            WindowInteropHelper helper = new WindowInteropHelper(_videoView.WindowFront);
            helper.Owner = this.Handle;
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(_videoView.WindowFront);
            _videoView.WindowFront.Show();
            
            LocationChanged += (o, l) =>
            {
                var location = _videoView.WinFormsHost.PointToScreen(new System.Windows.Point(0,0));
                _videoView.WindowFront.Top = location.Y; _videoView.WindowFront.Left = location.X;
            };

            _videoView.WinFormsHost.SizeChanged += (o, l) =>
            {
                var location = _videoView.WinFormsHost.PointToScreen(new System.Windows.Point(0,0));
                _videoView.WindowFront.Top = location.Y; _videoView.WindowFront.Left = location.X;

                _videoView.WindowFront.Height = _videoView.WinFormsHost.ActualHeight;
                _videoView.WindowFront.Width = _videoView.WinFormsHost.ActualWidth;
            };
            #endregion

            _flyleaf = new Flyleaf();
            _videoView.Content = _flyleaf;

            _player = new Player(_config);
            _videoView.BeginInit();

            _player.Control = _videoView.FlyleafWF;
            _videoView.Player = _player;
            _flyleaf.Player = _player;

            ContentControl1.Content = _videoView;

            _player.Open(sampleVideo);

            // Add to test naked control
            //_player.OpenCompleted += (o, e2) => { _player.Play(); };
        }
    }
}
