using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

using FFmpeg.AutoGen;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer
{
    // TODO: Open New Window with the same size, Popup Menu Playlist will not resize the size?
    //       Add Play Next/Prev for Playlists (Page Up/Down?) this goes down to Player

    /// <summary>
    /// <para>FlyleafPlayer Sample</para>
    /// <para>A stand-alone Overlay which uses a customization of FlyleafME control</para>
    /// </summary>
    public partial class MainWindow : Window
    {
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
                ActivityTimeout = 2000,
                KeyBindings = AvailableWindows.Both,
                DetachedResize = AvailableWindows.Overlay,
                DetachedDragMove = AvailableWindows.Both,
                ToggleFullScreenOnDoubleClick = AvailableWindows.Both,
                KeepRatioOnResize = true,
                OpenOnDrop = AvailableWindows.Both
            };

            InitializeComponent();

            // Allowing FlyleafHost to access our Player
            DataContext = FlyleafME;
        }

        private Config DefaultConfig()
        {
            Config config = new Config();
            config.Subtitles.SearchLocal = true;
            config.Video.GPUAdapter = ""; // Set it empty so it will include it when we save it

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
            };

            // TODO: Extend FlyleafME's PopUp Menu to include those
            // Ctrl+ N / Ctrl + W (Open New/Close Window)
            var key = playerConfig.Player.KeyBindings.Get("New Window");
            if (key != null)
                key.SetAction(() => (new MainWindow()).Show(), true);
            else
                playerConfig.Player.KeyBindings.AddCustom(Key.N, true, () => { (new MainWindow()).Show(); }, "New Window", false, true, false);

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
                Engine.Config.FFmpegDevices  = false;

                try { Engine.Config.Save("Flyleaf.Engine.xml"); } catch { }
            }
            #endif
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => FlyleafME.IsMinimized = true;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
