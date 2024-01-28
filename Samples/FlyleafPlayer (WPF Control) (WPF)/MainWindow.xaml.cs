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
        const int ACTIVITY_TIMEOUT = 3500;
        public event PropertyChangedEventHandler PropertyChanged;
        public static string FlyleafLibVer => "FlyleafLib v" + System.Reflection.Assembly.GetAssembly(typeof(Engine)).GetName().Version;
        
        /// <summary>
        /// Flyleaf Player binded to FlyleafME (This can be swapped and will nto belong to this window)
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
            OpenWindow  = new RelayCommandSimple(() => new MainWindow() { Width = Width, Height = Height }.Show());
            CloseWindow = new RelayCommandSimple(Close);

            FlyleafME = new FlyleafME(this)
            {
                Tag = this,
                ActivityTimeout     = ACTIVITY_TIMEOUT,
                KeyBindings         = AvailableWindows.Both,
                DetachedResize      = AvailableWindows.Overlay,
                DetachedDragMove    = AvailableWindows.Both,
                ToggleFullScreenOnDoubleClick 
                                    = AvailableWindows.Both,
                KeepRatioOnResize   = true,
                OpenOnDrop          = AvailableWindows.Both,

                PreferredLandscapeWidth = 800,
                PreferredPortraitHeight = 600
            };

            // Allow Flyleaf WPF Control to Load UIConfig and Save both Config & UIConfig (Save button will be available in settings)
            FlyleafME.ConfigPath    = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.Config.json");
            FlyleafME.EnginePath    = App.EnginePath;
            FlyleafME.UIConfigPath  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.UIConfig.json");
            
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
            if (File.Exists(FlyleafME.ConfigPath))
                try { playerConfig = Config.Load(FlyleafME.ConfigPath); } catch { playerConfig = DefaultConfig(); }
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

            // Dispose Player on Window Close (the possible swapped player from FlyleafMe that actually belongs to us)
            Closing += (o, e) => FlyleafME.Player?.Dispose();

            // If the user requests reverse playback allocate more frames once
            Player.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == "ReversePlayback" && !GetWindowFromPlayer(Player).ReversePlaybackChecked)
                {
                    if (playerConfig.Decoder.MaxVideoFrames < 80)
                        playerConfig.Decoder.MaxVideoFrames = 80;

                    GetWindowFromPlayer(Player).ReversePlaybackChecked = true;
                }
                else if (e.PropertyName == nameof(Player.Rotation))
                    GetWindowFromPlayer(Player).Msg = $"Rotation {Player.Rotation}°";
                else if (e.PropertyName == nameof(Player.Speed))
                    GetWindowFromPlayer(Player).Msg = $"Speed x{Player.Speed}";
                else if (e.PropertyName == nameof(Player.Zoom))
                    GetWindowFromPlayer(Player).Msg = $"Zoom {Player.Zoom}%";
                else if (e.PropertyName == nameof(Player.Status) && Player.Activity.Mode == ActivityMode.Idle)
                    Player.Activity.ForceActive();

            };

            Player.Audio.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Audio.Volume))
                    GetWindowFromPlayer(Player).Msg = $"Volume {Player.Audio.Volume}%";
                else if (e.PropertyName == nameof(Player.Audio.Mute))
                    GetWindowFromPlayer(Player).Msg = Player.Audio.Mute ? "Muted" : "Unmuted";
            };

            Player.Config.Audio.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Config.Audio.Delay))
                    GetWindowFromPlayer(Player).Msg = $"Audio Delay {Player.Config.Audio.Delay / 10000}ms";
            };

            Player.Config.Subtitles.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Config.Subtitles.Delay))
                    GetWindowFromPlayer(Player).Msg = $"Subs Delay {Player.Config.Subtitles.Delay / 10000}ms";
            };

            // Ctrl+ N / Ctrl + W (Open New/Close Window)
            var key = playerConfig.Player.KeyBindings.Get("New Window");
            if (key != null)
                key.SetAction(() => (new MainWindow()).Show(), true);
            else
                playerConfig.Player.KeyBindings.AddCustom(Key.N, true, () => CreateNewWindow(Player), "New Window", false, true, false);

            key = playerConfig.Player.KeyBindings.Get("Close Window");
            if (key != null)
                key.SetAction(() => Close(), true);
            else
                playerConfig.Player.KeyBindings.AddCustom(Key.W, true, () => GetWindowFromPlayer(Player).Close(), "Close Window", false, true, false);
        }

        private static MainWindow GetWindowFromPlayer(Player player)
        {
            FlyleafHost flhost = null;
            MainWindow mw = null;

            Utils.UIInvokeIfRequired(() =>
            {
                flhost  = (FlyleafHost) player.Host;
                mw      = (MainWindow) flhost.Overlay;
            });

            return mw;
        }
        private static void CreateNewWindow(Player player) 
        {
            var mw = GetWindowFromPlayer(player);

            MainWindow mwNew = new()
            {
                Width   = mw.Width,
                Height  = mw.Height,
            };

            mwNew.Show();
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
                    playerConfig.Save(FlyleafME.ConfigPath);
                } catch { }
            }

            // Stops Logging (First Run)
            if (!Engine.Config.Loaded)
            {
                Engine.Config.LogOutput      = null;
                Engine.Config.LogLevel       = LogLevel.Quiet;
                //Engine.Config.FFmpegDevices  = false;

                try { Engine.Config.Save(App.EnginePath); } catch { }
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
            await Task.Delay(ACTIVITY_TIMEOUT, cancelMsgToken.Token);
            Utils.UIInvoke(() => { msg = ""; PropertyChanged?.Invoke(this, new(nameof(Msg))); });
        }
        #endregion
    }
}
