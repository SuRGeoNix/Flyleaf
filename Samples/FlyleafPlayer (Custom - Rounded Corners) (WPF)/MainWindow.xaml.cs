using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer__Custom___Rounded_Corners___WPF_
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public Player Player        { get; set; }
        public Config Config        { get; set; }

        public string SampleVideo   { get; set; } = Utils.FindFileBelow("Sample.mp4");
        public string LastError     { get => _LastError; set { if (_LastError == value) return; _LastError = value;  PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastError))); } }
        string _LastError;

        public bool     ShowDebug   { get => _ShowDebug; set { if (_ShowDebug == value) return; _ShowDebug = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDebug))); } }
        bool _ShowDebug;

        public ICommand ToggleDebug { get; set; }

        public FlyleafHost FlyleafHost { get; set; }

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
            });

            FlyleafHost =  new FlyleafHost(this)
            {
                // Enables AllowTransparency on Surface and could cause performance issues (1 from 255 alpha to be able to use mouse events)
                VideoBackground     = new SolidColorBrush(new Color() { A = 1}),
                CornerRadius        = new CornerRadius(30),
                KeyBindings         = AvailableWindows.Surface,
                DetachedResize      = AvailableWindows.Surface,
                KeepRatioOnResize   = true,
            };

            ToggleDebug = new RelayCommandSimple(new Action(() => { ShowDebug = !ShowDebug; }));

            InitializeComponent();

            Config = new Config();

            // Inform the lib to refresh stats
            Config.Player.Stats = true;
            Config.Player.UICurTime = UIRefreshType.PerFrame;

            Player = new Player(Config);
            FlyleafHost.Player = Player;

            DataContext = this;

            // Keep track of error messages
            Player.OpenCompleted += (o, e) => { LastError = e.Error; };
            Player.BufferingCompleted += (o, e) => { LastError = e.Error; };
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
