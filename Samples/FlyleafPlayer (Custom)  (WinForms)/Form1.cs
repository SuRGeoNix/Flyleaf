using System;
using System.Windows.Forms;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer__Custom_
{
    public partial class Form1 : Form
    {
        public Player Player { get; set; }
        public Config Config { get; set; }

        public static string SampleVideo { get; set; } = Utils.FileExistsBelow("Sample.mp4");
        public Form1()
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

            // Parse the control to the Player
            Player.Control = flyleaf1;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Player.OpenAsync(SampleVideo);
            Player.OpenCompleted += (o, x) => { if (x.Success && x.Type == MediaType.Video) Player.Play(); };
        }
    }
}
