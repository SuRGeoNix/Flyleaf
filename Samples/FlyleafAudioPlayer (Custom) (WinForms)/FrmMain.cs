using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace FlyleafAudioPlayer__Custom___WinForms_
{
    public partial class FrmMain : Form
    {
        Player Player;
        Config Config;

        // Prevents hiting ValueChanged on trackbars when comes from flyleaf property changed
        bool curTimeChangedFromLib;
        bool volumeChangedFromLib;

        public FrmMain()
        {
            InitializeComponent();

            // Initializes Engine (Specifies FFmpeg libraries path which is required)
            Engine.Start(new EngineConfig()
            {
                #if DEBUG
                LogOutput       = ":debug",
                LogLevel        = LogLevel.Debug,
                FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Warn,
                #endif

                PluginsPath     = ":Plugins",
                FFmpegPath      = ":FFmpeg",
            });

            // Create new config
            Config = new Config();

            // Initiliaze the player as Audio Player
            Config.Player.Usage = Usage.Audio;
            Player = new Player(Config);

            // Listen to property changed events (No need to invoke UI as the library will do it automatically)
            Player.PropertyChanged      += Player_PropertyChanged;
            Player.Audio.PropertyChanged+= PlayerAudio_PropertyChanged;

            // Allow auto play on open
            Config.Player.AutoPlay      = true;

            // Prepare Volume Max/Offset/Initial values
            Config.Audio.VolumeMax      = 200;
            sliderVolume.Maximum        = Config.Audio.VolumeMax;
            Player.Audio.Volume         = 75;
            Config.Audio.VolumeOffset   = 5;
            btnMute.Click += (o, e) => { Config.Audio.ToggleMute(); };

            // Prepare Seek Offsets and Commands
            Config.Player.SeekOffset    = TimeSpan.FromSeconds( 5).Ticks;
            Config.Player.SeekOffset2   = TimeSpan.FromSeconds(15).Ticks;

            btnPlayPause.Enabled = false; // Player.CanPlay will control this
            btnPlayPause.Click  += (o, e) => { Player.TogglePlayPause(); };
            btnStop.Click       += (o, e) => { Player.Stop(); };
            btnBackward.Click   += (o, e) => { Player.SeekBackward(); };
            btnBackward2.Click  += (o, e) => { Player.SeekBackward2(); };
            btnForward.Click    += (o, e) => { Player.SeekForward(); };
            btnForward2.Click   += (o, e) => { Player.SeekForward2(); };

            // Prepare Sliders Keys Up/Down/Left/Right Arrows Commands
            sliderVolume.KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Up)
                    Config.Audio.VolumeUp();
                else if (e.KeyCode == Keys.Down)
                    Config.Audio.VolumeDown();

                e.Handled = true;
            };

            sliderCurTime.KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Left)
                    Player.SeekBackward();
                else if (e.KeyCode == Keys.Right)
                    Player.SeekForward();
                else if (e.KeyCode == Keys.Up)
                    Config.Audio.VolumeUp();
                else if (e.KeyCode == Keys.Down)
                    Config.Audio.VolumeDown();

                e.Handled = true;
            };

            // Open / OpenCompleted / BufferingStarted / BufferingCompleted / Stop / IsLive (+Messages)
            Player.OpenCompleted += (o, e) =>
            {
                Utils.UI(() =>
                {
                    lblMsgs.Text    = !e.Success ? e.Error.Replace("\r\n", "") : (Player.IsLive ? "Live" : "");
                    btnStop.Enabled = e.Success;
                });
            };

            lstPlaylist.MouseDoubleClick += (o, e) =>
            {
                if (lstPlaylist.SelectedItem != null)
                {
                    lblMsgs.Text = "Opening ...";
                    Player.OpenAsync(((PlaylistItem)lstPlaylist.SelectedItem).Url);
                }
            };

            Player.BufferingStarted     += (o, e) => { Utils.UI(() => lblMsgs.Text = "Buffering ..."); };
            Player.BufferingCompleted   += (o, e) => { Utils.UI(() => lblMsgs.Text = !e.Success ? "Failed" : (Player.IsLive ? "Live" : "")); };

            btnStop.Click   += (o, e) => { btnStop.Enabled = false; };
            btnStop.Enabled = false;
            lblMsgs.Text    = "";

            // Prepare Playlist and Open on double click
            lstPlaylist.Items.Add(new PlaylistItem(Utils.FindFileBelow("Sample.mp4"), "(Local File Test)"));
            lstPlaylist.Items.Add(new PlaylistItem("https://www.youtube.com/watch?v=cnVPm1dGQJc", "(Youtube) Deep House Mix 2020 Vol.1 | Mixed By TSG"));
            lstPlaylist.Items.Add(new PlaylistItem("http://62.212.82.197:8000/;", "(Live Radio) Rainbow 89.0 - Greece"));
            lstPlaylist.Items.Add(new PlaylistItem("http://kvhs.smrn.com:5561/live", "(Live Radio) KVHS 90.5 - California"));
            lstPlaylist.Items.Add(new PlaylistItem("https://kexp-mp3-128.streamguys1.com/kexp128.mp3", "(Live Radio) KEXP 90.3 - Seattle"));
            lstPlaylist.Items.Add(new PlaylistItem("https://wwoz-sc.streamguys1.com/wwoz-hi.mp3", "(Live Radio) WWOZ 90.7 - New Orleans"));
        }

        private void Player_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "CurTime":
                    var curTime = TimeSpan.FromTicks(Player.CurTime);

                    curTimeChangedFromLib   = true;
                    lblCurTime.Text         = curTime.ToString(@"hh\:mm\:ss");
                    if (Player.Duration != 0)
                        sliderCurTime.Value = (int) curTime.TotalSeconds;
                    curTimeChangedFromLib   = false;
                    break;

                case "Duration":
                    var duration = TimeSpan.FromTicks(Player.Duration);

                    lblDuration.Text        = duration.ToString(@"hh\:mm\:ss");
                    sliderCurTime.Maximum   = (int) duration.TotalSeconds;

                    break;

                case "Status":
                    btnPlayPause.Text       = Player.IsPlaying ? "Pause" : "Play";

                    if (!Player.IsLive && Player.Status == Status.Ended && chkRepeat.Checked)
                        Player.Seek(0);

                    break;

                case "CanPlay":
                    btnPlayPause.Enabled = Player.CanPlay;
                    break;
            }
        }

        private void PlayerAudio_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Volume":
                    volumeChangedFromLib= true;
                    lblVolume.Text      = Player.Audio.Volume.ToString() + "%";
                    sliderVolume.Value  = Player.Audio.Volume;
                    volumeChangedFromLib= false;
                    break;

                case "Mute":
                    btnMute.Text = Player.Audio.Mute ? "Unmute" : "Mute";
                    break;
            }
        }

        #region Sliders Events to set the value on mouse click and call the relative player's commands
        private void sliderCurTime_MouseDown(object sender, MouseEventArgs e)
        {
            double mouseHit = e.X < 0 ? 0 : (e.X > sliderCurTime.Width ? 1 : (double)e.X / sliderCurTime.Width);
            Player.CurTime = TimeSpan.FromSeconds(mouseHit * sliderCurTime.Maximum).Ticks;
        }
        private void sliderVolume_MouseDown(object sender, MouseEventArgs e)
        {
            double mouseHit = e.Y < 0 ? 0 : (e.Y > sliderVolume.Height ? 1 : (double)e.Y / sliderVolume.Height);
            Player.Audio.Volume = (int) ((1 - mouseHit) * sliderVolume.Maximum);
        }
        private void sliderCurTime_MouseMove(object sender, MouseEventArgs e)
        {
            if (MouseButtons != MouseButtons.Left)
                return;

            double mouseHit = e.X < 0 ? 0 : (e.X > sliderCurTime.Width ? 1 : (double)e.X / sliderCurTime.Width);
            Player.CurTime = TimeSpan.FromSeconds(mouseHit * sliderCurTime.Maximum).Ticks;
        }
        private void sliderVolume_MouseMove(object sender, MouseEventArgs e)
        {
            if (MouseButtons != MouseButtons.Left)
                return;

            double mouseHit = e.Y < 0 ? 0 : (e.Y > sliderVolume.Height ? 1 : (double)e.Y / sliderVolume.Height);
            Player.Audio.Volume = (int) ((1 - mouseHit) * sliderVolume.Maximum);
        }
        private void sliderCurTime_ValueChanged(object sender, EventArgs e)
        {
            if (curTimeChangedFromLib)
                return;

            Player.CurTime = TimeSpan.FromSeconds(sliderCurTime.Value).Ticks;
        }
        private void sliderVolume_ValueChanged(object sender, EventArgs e)
        {
            if (volumeChangedFromLib)
                return;

            Player.Audio.Volume = sliderVolume.Value;
        }
        #endregion
    }

    /// <summary>
    /// Playlist Item
    /// </summary>
    public class PlaylistItem
    {
        public string Title = "";
        public string Url = "";

        public PlaylistItem(string url)
        {
            Url = url;
        }

        public PlaylistItem(string url, string title)
        {
            Url = url;
            Title = title;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Title) ? Url : Title;
        }
    }

    /// <summary>
    /// Removes trackbar's default border
    /// </summary>
    public class TrackBarWithoutFocus : TrackBar
    {
        private const int WM_SETFOCUS = 0x0007;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SETFOCUS)
            {
                return;
            }

            base.WndProc(ref m);
        }
    }
}
