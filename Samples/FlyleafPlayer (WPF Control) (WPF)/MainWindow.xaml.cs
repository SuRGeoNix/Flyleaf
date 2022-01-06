using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer
{
    /// <summary>
    /// FlyleafPlayer (WPF Control) Sample
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Flyleaf Player binded to VideoView
        /// </summary>
        public Player Player { get; set; }
        public Config Config { get; set; }

        public MainWindow()
        {
            // Ensures that we have enough worker threads to avoid the UI from freezing or not updating on time
            ThreadPool.GetMinThreads(out int workers, out int ports);
            ThreadPool.SetMinThreads(workers + 6, ports + 6);

            // Less power consumption (safe to use only for single player)
            Master.HighPerformaceTimers = false;

            // Registers FFmpeg Libraries
            Master.RegisterFFmpeg(":2");

            // Registers Plugins
            Master.RegisterPlugins(":2");

            // Prepares Player's Configuration (Load from file if already exists, Flyleaf WPF Control will save at this path)
            #if DEBUG
            Config = new Config();
            #else
            if (File.Exists("Flyleaf.Config.xml"))
                Config = Config.Load("Flyleaf.Config.xml");
            else
            {
                Utils.AddFirewallRule();
                Config = new Config();
                Config.Player.KeyBindings.LoadDefault();
                Config.Save("Flyleaf.Config.xml");
            }
                
            #endif

            // Initializes the Player
            Player = new Player(Config);

            // Allowing VideoView to access our Player
            DataContext = this;

            InitializeComponent();

            // Allow Flyleaf WPF Control to Load UIConfig and Save both Config & UIConfig (Save button will be available in settings)
            flyleafControl.ConfigPath = "Flyleaf.Config.xml";
            flyleafControl.UIConfigPath = "Flyleaf.UIConfig.xml";

            // If the user requests reverse playback allocate more frames once
            Player.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == "ReversePlayback" && !ReversePlaybackChecked)
                {
                    ReversePlaybackChecked = true;
                    if (Config.Decoder.MaxVideoFrames < 40)
                        Config.Decoder.MaxVideoFrames = 40;
                }
            };
        }
        bool ReversePlaybackChecked;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Giving access to keyboard events on start up
            Player.VideoView.WinFormsHost.Focus();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            //Enable Dark Titlebar
            DwmApi.ToggleImmersiveDarkMode(new WindowInteropHelper(this).Handle, true);
        }

        public static class DwmApi
        {
            private const int S_OK = 0;

            // This two flags are not currently documented
            // and they might change in the future
            private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
            private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

            private const int WINDOWS10_MIN_BUILD_NUMBER = 17763;
            private const int WINDOWS10_20H1_BUILD_NUMBER = 18985;

            public static void ToggleImmersiveDarkMode(IntPtr window, bool enable)
            {
                if (!IsWindows10OrGreater(WINDOWS10_MIN_BUILD_NUMBER))
                {
                    // Dark mode is not supported
                    //_ = MessageBox.Show($"{Environment.OSVersion.Version.Build}not s");
                    return;
                }

                int useImmersiveDarkMode = enable ? 1 : 0;
                CheckHResult(DwmSetWindowAttribute(window, ImmersiveDarkModeAttribute, ref useImmersiveDarkMode, sizeof(int)));
            }

            [DllImport("dwmapi.dll", PreserveSig = true)]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attributeValue, int attributeSize);

            private static bool IsWindows10OrGreater(int build)
            {
                return IsWindow10 && HasAtLeastBuildNumber(build);
            }

            private static bool IsWindow10
                => Environment.OSVersion.Version.Major >= 10;

            private static bool HasAtLeastBuildNumber(int build)
            {
                return Environment.OSVersion.Version.Build >= build;
            }

            private static int ImmersiveDarkModeAttribute
                => HasAtLeastBuildNumber(WINDOWS10_20H1_BUILD_NUMBER)
                    ? DWMWA_USE_IMMERSIVE_DARK_MODE
                    : DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;

            private static void CheckHResult(int hResult)
            {
                if (hResult != S_OK)
                {
                    //throw new Win32Exception(hResult);
                    //_ = MessageBox.Show(new Win32Exception(hResult).Message);
                }
            }
        }
    }
}
