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
                FFmpegLogLevel  = FFmpegLogLevel.Warning,
                #endif
                
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
            Player.Control = flyleaf1;
            Player2.Control = flyleaf2;

            isPlayer1View = true;
            Player1View = Player;

            Player.PropertyChanged += Player_PropertyChanged; // On Swap you should unsubscribe player 1 and subscribe to player 2
            //Player.OpenCompleted += // To handle errors
            //Player.BufferingStarted += // To handle buffering
        }

        private void Player_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // No UI Invoke required

            switch (e.PropertyName)
            {
                case "CurTime":
                    label1.Text = (new TimeSpan(Player1View.CurTime)).ToString(@"hh\:mm\:ss\.fff");
                    break;

                case "BufferedDuration":
                    label2.Text =  (new TimeSpan(Player1View.BufferedDuration)).ToString(@"hh\:mm\:ss\.fff");
                    break;

                case "Status":
                    label6.Text = Player1View.Status.ToString();
                    break;
            }
        }

        private void RefreshAfterSwap()
        {
            label1.Text = (new TimeSpan(Player1View.CurTime)).ToString(@"hh\:mm\:ss\.fff");
            label2.Text = (new TimeSpan(Player1View.BufferedDuration)).ToString(@"hh\:mm\:ss\.fff");
            label6.Text = Player1View.Status.ToString();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //// Just to make sure that handles have been created before using the player (opening)
            //while (Player.Control.Handle == IntPtr.Zero || Player2.Control.Handle == IntPtr.Zero)
            //    System.Threading.Thread.Sleep(20);

            Player.OpenAsync(SampleVideo);

            // Sample using a 'custom' IO stream
            Stream customInput = new FileStream(SampleVideo, FileMode.Open);
            Player2.OpenAsync(customInput);
        }

        bool isPlayer1View;
        Player Player1View;
        private void btnSwap_Click(object sender, EventArgs e)
        {
            Player1View.PropertyChanged -= Player_PropertyChanged;

            Player.SwapPlayers(Player, Player2);

            Player1View = isPlayer1View ? Player2 : Player;
            Player1View.PropertyChanged += Player_PropertyChanged;

            RefreshAfterSwap();
            isPlayer1View = !isPlayer1View;
        }
    }
}
