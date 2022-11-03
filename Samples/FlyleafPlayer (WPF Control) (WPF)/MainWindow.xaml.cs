using System;
using System.IO;
using System.Threading;
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
        public Player       PlayerX     { get; set; }

        /// <summary>
        /// FlyleafME Media Element Control
        /// </summary>
        public FlyleafME    FlyleafME   { get; set; }

        EngineConfig engineConfig;
        Config playerConfig;

        public MainWindow()
        {
            // NOTE: Loads/Saves configs only in RELEASE mode

            // Ensures that we have enough worker threads to avoid the UI from freezing or not updating on time
            ThreadPool.GetMinThreads(out int workers, out int ports);
            ThreadPool.SetMinThreads(workers + 6, ports + 6);

            // Engine's Config
            #if RELEASE
            if (File.Exists("Flyleaf.Engine.xml"))
                try { engineConfig = EngineConfig.Load("Flyleaf.Engine.xml"); } catch { engineConfig = DefaultEngineConfig(); }
            else
                engineConfig = DefaultEngineConfig();
            #else
            engineConfig = DefaultEngineConfig();
            #endif

            Engine.Start(engineConfig);

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
            PlayerX = new Player(playerConfig);

            FlyleafME = new FlyleafME(this)
            {
                ActivityTimeout = 2000,
                KeyBindingsMode = AvailableWindows.Both,
                ResizeMode = AvailableWindows.Overlay,
                DragMoveOnDetach = true,
                Player = PlayerX
            };

            InitializeComponent();

            // Allowing FlyleafHost to access our Player
            DataContext = this;

            // Allow Flyleaf WPF Control to Load UIConfig and Save both Config & UIConfig (Save button will be available in settings)
            FlyleafME.ConfigPath    = "Flyleaf.Config.xml";
            FlyleafME.EnginePath    = "Flyleaf.Engine.xml";
            FlyleafME.UIConfigPath  = "Flyleaf.UIConfig.xml";

            MouseLeftButtonDown += (o, e) =>
            { 
                if (e.ClickCount == 2)
                    FlyleafME.IsFullScreen = !FlyleafME.IsFullScreen;
                else if (FlyleafME.ResizingSideOverlay == 0 && e.ClickCount == 1) 
                    DragMove();
            };

            // If the user requests reverse playback allocate more frames once
            PlayerX.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == "ReversePlayback" && !ReversePlaybackChecked)
                {
                    if (playerConfig.Decoder.MaxVideoFrames < 80)
                        playerConfig.Decoder.MaxVideoFrames = 80;

                    ReversePlaybackChecked = true;
                }
            };
        }
        bool ReversePlaybackChecked;

        private EngineConfig DefaultEngineConfig()
        {
            EngineConfig engineConfig = new EngineConfig();

            engineConfig.PluginsPath    = ":Plugins";
            engineConfig.FFmpegPath     = ":FFmpeg";
            engineConfig.HighPerformaceTimers
                                        = false;
            engineConfig.UIRefresh      = true;

            #if RELEASE
            engineConfig.LogOutput      = "Flyleaf.FirstRun.log";
            engineConfig.LogLevel       = LogLevel.Debug;
            engineConfig.FFmpegDevices  = true;
            #else
            engineConfig.LogOutput      = ":debug";
            engineConfig.LogLevel       = LogLevel.Debug;
            engineConfig.FFmpegLogLevel = FFmpegLogLevel.Warning;
            #endif

            return engineConfig;
        }

        private Config DefaultConfig()
        {
            Config config = new Config();
            config.Subtitles.SearchLocal = true;
            config.Video.GPUAdapter = ""; // Set it empty so it will include it when we save it

            return config;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
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
            if (!engineConfig.Loaded)
            {
                engineConfig.LogOutput      = null;
                engineConfig.LogLevel       = LogLevel.Quiet;
                engineConfig.FFmpegDevices  = false;

                try { engineConfig.Save("Flyleaf.Engine.xml"); } catch { }
            }
            #endif
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximizeRestore_Checked(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;
        private void BtnMaximizeRestore_Unchecked(object sender, RoutedEventArgs e) => WindowState = WindowState.Normal;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void Grid_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }

            DragMove();
        }
    }
}
