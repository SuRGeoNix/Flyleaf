using System;
using System.Windows;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer
{
    /// <summary>
    /// FlyleafPlayer (Customization of FlyleafME) Sample
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Flyleaf Player binded to FlyleafME
        /// </summary>
        public Player       Player      { get; set; }

        /// <summary>
        /// FlyleafME Media Element Control
        /// </summary>
        public FlyleafME    FlyleafME   { get; set; }

        
        Config playerConfig;
        bool ReversePlaybackChecked;

        public MainWindow()
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

            FlyleafME = new FlyleafME(this)
            {
                ActivityTimeout = 2000,
                KeyBindings = AvailableWindows.Both,
                DetachedResize = AvailableWindows.Overlay,
                DetachedDragMove = AvailableWindows.Both,
                ToggleFullScreenOnDoubleClick = AvailableWindows.Both,
                KeepRatioOnResize = true,
                OpenOnDrop = AvailableWindows.Both,
                Player = Player
            };

            InitializeComponent();

            // Allowing FlyleafHost to access our Player
            DataContext = this;

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
                playerConfig.Player.KeyBindings.AddCustom(System.Windows.Input.Key.N, true, () => { (new MainWindow()).Show(); }, "New Window", false, true, false);

            key = playerConfig.Player.KeyBindings.Get("Close Window");
            if (key != null)
                key.SetAction(() => Close(), true);
            else
                playerConfig.Player.KeyBindings.AddCustom(System.Windows.Input.Key.W, true, () => { Close(); }, "Close Window", false, true, false);
        }



        private Config DefaultConfig()
        {
            Config config = new Config();
            config.Subtitles.SearchLocal = true;
            config.Video.GPUAdapter = ""; // Set it empty so it will include it when we save it

            return config;
        }

        static bool runOnce;
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
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

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximizeRestore_Checked(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;
        private void BtnMaximizeRestore_Unchecked(object sender, RoutedEventArgs e) => WindowState = WindowState.Normal;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
