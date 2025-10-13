using System;
using System.IO;
using System.Windows.Forms;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer__Custom_
{
    public partial class Form1 : Form
    {
        public Player Player { get; set; }
        public Player Player2 { get; set; }
        public Config Config { get; set; }

        public static string SampleVideo { get; set; } = Utils.FindFileBelow("Sample.mp4");
        public Form1()
        {
            // Initializes Engine (Specifies FFmpeg libraries path which is required)
            Engine.Start(new EngineConfig()
            {
                #if DEBUG
                LogOutput       = ":debug",
                LogLevel        = LogLevel.Debug,
                FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Warn,
                #endif

                UIRefresh       = true, // For Activity Mode usage
                PluginsPath     = ":Plugins",
                FFmpegPath      = ":FFmpeg",
            });

            // Prepares Player's Configuration
            Config = new Config();

            // Initializes the Player
            Player = new Player(Config);
            Player2 = new Player();

            InitializeComponent();

            // Parse the control to the Player
            flyleaf1.Player = Player;
            flyleaf1.Player.PropertyChanged += Player_PropertyChanged; // On Swap you should unsubscribe player 1 and subscribe to player 2
            flyleaf2.Player = Player2;
            flyleaf2.Player.Activity.Timeout = 2000; // Enabled on FlyleafHost 2 (2sec)

            //Player.OpenCompleted += // To handle errors
            //Player.BufferingStarted += // To handle buffering

            // Dispose on close
            FormClosing += (o, e) =>
            {
                while (Engine.Players.Count != 0)
                    Engine.Players[0].Dispose();
            };
        }

        private void Player_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // No UI Invoke required

            switch (e.PropertyName)
            {
                case "CurTime":
                    label1.Text = (new TimeSpan(flyleaf1.Player.CurTime)).ToString(@"hh\:mm\:ss\.fff");
                    break;

                case "BufferedDuration":
                    label2.Text = (new TimeSpan(flyleaf1.Player.BufferedDuration)).ToString(@"hh\:mm\:ss\.fff");
                    break;

                case "Status":
                    label6.Text = flyleaf1.Player.Status.ToString();
                    break;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Player.OpenAsync(SampleVideo);

            // Sample using a 'custom' IO stream
            Stream customInput = new FileStream(SampleVideo, FileMode.Open);
            Player2.OpenAsync(customInput);

            // Testing Replica (Don't set flyleaf2.Player in the constructor)
            // Player.renderer.SetReplica(flyleaf2.Handle);

            // Replica with any Handle (if you don't really need a FlyleafHost)
            //Player.renderer.SetChildHandle(pnlReplica.Handle);

        }

        private void btnSwap_Click(object sender, EventArgs e)
        {
            flyleaf1.Player.PropertyChanged -= Player_PropertyChanged;
            (flyleaf2.Player, flyleaf1.Player) = (flyleaf1.Player, flyleaf2.Player);
            flyleaf1.Player.PropertyChanged += Player_PropertyChanged;
        }
    }
}
