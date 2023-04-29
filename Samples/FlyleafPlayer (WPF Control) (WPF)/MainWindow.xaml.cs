using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer
{
    // TODO: Popup Menu Playlist will not resize the size?
    //       Add Play Next/Prev for Playlists (Page Up/Down?) this goes down to Player

    /// <summary>
    /// <para>FlyleafPlayer Sample</para>
    /// <para>A stand-alone Overlay which uses a customization of FlyleafME control</para>
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public static string FlyleafLibVer => "FlyleafLib v" + System.Reflection.Assembly.GetAssembly(typeof(Engine)).GetName().Version;
        
        /// <summary>
        /// Flyleaf Player binded to FlyleafME
        /// </summary>
        public Player       Player      { get; set; }

        /// <summary>
        /// FlyleafME Media Element Control
        /// </summary>
        public FlyleafME    FlyleafME   { get; set; }

        public ICommand     OpenWindow  { get; set; }
        public ICommand     CloseWindow { get; set; }

        static bool runOnce;
        Config playerConfig;
        bool ReversePlaybackChecked;
        
        public MainWindow()
        {
            OpenWindow  = new RelayCommandSimple(() => (new MainWindow()).Show());
            CloseWindow = new RelayCommandSimple(() => Close());

            FlyleafME = new FlyleafME(this)
            {
                Tag = this,
                ActivityTimeout     = 2000,
                KeyBindings         = AvailableWindows.Both,
                DetachedResize      = AvailableWindows.Overlay,
                DetachedDragMove    = AvailableWindows.Both,
                ToggleFullScreenOnDoubleClick 
                                    = AvailableWindows.Both,
                KeepRatioOnResize   = true,
                OpenOnDrop          = AvailableWindows.Both
            };

            InitializeComponent();

            // Allowing FlyleafHost to access our Player
            DataContext = FlyleafME;
        }

        private Config DefaultConfig()
        {
            Config config = new Config();
            config.Audio.FiltersEnabled     = true;         // To allow embedded atempo filter for speed
            config.Video.GPUAdapter         = "";           // Set it empty so it will include it when we save it
            config.Subtitles.SearchLocal    = true;
            return config;
        }

        private void LoadPlayer()
        {
            // NOTE: Loads/Saves configs only in RELEASE mode

            // Player's Config (Cannot be initialized before Engine's initialization)
            #if RELEASE
            // Load Player's Config
            if (File.Exists("Flyleaf.Config.xml"))
                try { playerConfig = Config.Load("Flyleaf.Config.xml"); } catch { playerConfig = DefaultConfig(); }
            else
                playerConfig = DefaultConfig();
            #else
                playerConfig = DefaultConfig();
            #endif

            #if DEBUG
            // Testing audio filters
            //playerConfig.Audio.Filters = new()
            //{
              ////new() { Name = "loudnorm", Args = "I=-24:LRA=7:TP=-2", Id = "loudnorm1" },
              ////new() { Name = "dynaudnorm", Args = "f=4150", Id = "dynaudnorm1" },
              ////new() { Name ="afftfilt", Args = "real='hypot(re,im)*sin(0)':imag='hypot(re,im)*cos(0)':win_size=512:overlap=0.75" }, // robot
              ////new() { Name ="tremolo", Args="f=5:d=0.5" },
              ////new() { Name ="vibrato", Args="f=10:d=0.5" },
              ////new() { Name ="rubberband", Args="pitch=1.5" }
            //};
            #endif

            // Initializes the Player
            Player = new Player(playerConfig);

            // Dispose Player on Window Close
            Closed += (o, e) => Player?.Dispose();

            // Allow Flyleaf WPF Control to Load UIConfig and Save both Config & UIConfig (Save button will be available in settings)
            FlyleafME.ConfigPath    = "Flyleaf.Config.xml";
            FlyleafME.EnginePath    = "Flyleaf.Engine.xml";
            FlyleafME.UIConfigPath  = "Flyleaf.UIConfig.xml";
            
            // If the user requests reverse playback allocate more frames once
            Player.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == "ReversePlayback" && !ReversePlaybackChecked)
                {
                    if (playerConfig.Decoder.MaxVideoFrames < 80)
                        playerConfig.Decoder.MaxVideoFrames = 80;

                    ReversePlaybackChecked = true;
                }
                else if (e.PropertyName == nameof(Player.Rotation))
                    Msg = $"Rotation {Player.Rotation}°";
                else if (e.PropertyName == nameof(Player.Speed))
                    Msg = $"Speed x{Player.Speed}";
                else if (e.PropertyName == nameof(Player.Zoom))
                    Msg = $"Zoom {Player.Zoom}%";
            };

            Player.Audio.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Audio.Volume))
                    Msg = $"Volume {Player.Audio.Volume}%";
                else if (e.PropertyName == nameof(Player.Audio.Mute))
                    Msg = Player.Audio.Mute ? "Muted" : "Unmuted";
            };

            Player.Config.Audio.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Config.Audio.Delay))
                    Msg = $"Audio Delay {Player.Config.Audio.Delay / 10000}ms";
            };

            Player.Config.Subtitles.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Config.Subtitles.Delay))
                    Msg = $"Subs Delay {Player.Config.Subtitles.Delay / 10000}ms";
            };

            // Ctrl+ N / Ctrl + W (Open New/Close Window)
            var key = playerConfig.Player.KeyBindings.Get("New Window");
            if (key != null)
                key.SetAction(() => (new MainWindow()).Show(), true);
            else
                playerConfig.Player.KeyBindings.AddCustom(Key.N, true, () => { (new MainWindow() { Width = Width, Height = Height }).Show(); }, "New Window", false, true, false);

            key = playerConfig.Player.KeyBindings.Get("Close Window");
            if (key != null)
                key.SetAction(() => Close(), true);
            else
                playerConfig.Player.KeyBindings.AddCustom(Key.W, true, () => { Close(); }, "Close Window", false, true, false);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Engine.IsLoaded)
            {
                LoadPlayer();
                FlyleafME.Player = Player;
            }
            else
            {
                Engine.Loaded += (o, e) =>
                {
                    LoadPlayer();
                    Utils.UIInvokeIfRequired(() => FlyleafME.Player = Player);
                };
            }

            if (runOnce)
                return;

            runOnce = true;

            if (App.CmdUrl != null)
                Player.OpenAsync(App.CmdUrl);

            #if RELEASE
            // Save Player's Config (First Run)
            // Ensures that the Control's handle has been created and the renderer has been fully initialized (so we can save also the filters parsed by the library)
            if (!playerConfig.Loaded)
            {
                try
                {
                    Utils.AddFirewallRule();
                    playerConfig.Save("Flyleaf.Config.xml");
                } catch { }
            }

            // Stops Logging (First Run)
            if (!Engine.Config.Loaded)
            {
                Engine.Config.LogOutput      = null;
                Engine.Config.LogLevel       = LogLevel.Quiet;
                //Engine.Config.FFmpegDevices  = false;

                try { Engine.Config.Save("Flyleaf.Engine.xml"); } catch { }
            }
            #endif
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => FlyleafME.IsMinimized = true;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        #region OSD Msg
        CancellationTokenSource cancelMsgToken = new();
        public string Msg { get => msg; set { cancelMsgToken.Cancel(); msg = value; PropertyChanged?.Invoke(this, new(nameof(Msg))); cancelMsgToken = new(); Task.Run(FadeOutMsg, cancelMsgToken.Token); } }
        string msg;
        private async Task FadeOutMsg()
        {
            await Task.Delay(2000, cancelMsgToken.Token);
            Utils.UIInvoke(() => { msg = ""; PropertyChanged?.Invoke(this, new(nameof(Msg))); });
        }
        #endregion
    }
}
