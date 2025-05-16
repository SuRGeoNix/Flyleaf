using System;
using System.ComponentModel;
using System.Security.Policy;
using System.Windows;
using System.Windows.Input;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer__Custom___MVVM_
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public Player Player        { get; set; }
        public Config Config        { get; set; }

        public string SampleVideo   { get; set; } = Utils.FindFileBelow("qie.webm");
        public string LastError     { get => _LastError; set { if (_LastError == value) return; _LastError = value;  PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastError))); } }
        string _LastError;

        public bool     ShowDebug   { get => _ShowDebug; set { if (_ShowDebug == value) return; _ShowDebug = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDebug))); } }
        bool _ShowDebug;

        public ICommand ToggleDebug { get; set; }

        public MainWindow()
        {
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

                // Use UIRefresh to update Stats/BufferDuration (and CurTime more frequently than a second)
                UIRefresh       = true,
                UIRefreshInterval= 100,
                UICurTimePerSecond = false // If set to true it updates when the actual timestamps second change rather than a fixed interval
            });

            ToggleDebug = new RelayCommandSimple(new Action(() => { ShowDebug = !ShowDebug; }));

            InitializeComponent();

            Config = new Config();

            // Inform the lib to refresh stats
            Config.Player.Stats = true;

            Player = new Player(Config);

            DataContext = this;

            Player.Opening += Player_Opening;
            // Keep track of error messages
            Player.OpenCompleted += (o, e) => { LastError = e.Error; };
            Player.BufferingCompleted += (o, e) => { LastError = e.Error; };
        }

        private void Player_Opening(object sender, OpeningArgs e)
        {
            var url = e.Url;
            if (url.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) && !url.Contains("8k", StringComparison.OrdinalIgnoreCase))
            {
                //the default codec is [vp8/vp9],it is not support alpha beacuse of the pixel format is [yuv420p] even then actually is [yuva420p].
                //change to [libvpx/libvpx-vp9],the pixel format will be [yuva420p] and it is support alpha.
                Player.Config.Video.Codec = url.Contains("vp8", StringComparison.OrdinalIgnoreCase) ? "libvpx" : "libvpx-vp9";
            }
            else
            {
                Player.Config.Video.Codec = null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
