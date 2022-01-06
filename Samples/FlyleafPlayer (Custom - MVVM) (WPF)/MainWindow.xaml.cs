using System;
using System.ComponentModel;
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

        public string SampleVideo   { get; set; } = Utils.FindFileBelow("Sample.mp4");
        public string LastError     { get => _LastError; set { if (_LastError == value) return; _LastError = value;  PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastError))); } }
        string _LastError;

        public bool     ShowDebug   { get => _ShowDebug; set { if (_ShowDebug == value) return; _ShowDebug = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDebug))); } }
        bool _ShowDebug;

        public ICommand ToggleDebug { get; set; }

        public MainWindow()
        {
            // Registers FFmpeg Libraries
            Master.RegisterFFmpeg(":2");

            // Registers Plugins
            Master.RegisterPlugins(":2");

            // Use UIRefresh to update Stats/BufferDuration (and CurTime more frequently than a second)
            Master.UIRefresh = true;
            Master.UIRefreshInterval = 100;
            Master.UICurTimePerSecond = false; // If set to true it updates when the actual timestamps second change rather than a fixed interval

            ToggleDebug = new RelayCommandSimple(new Action(() => { ShowDebug = !ShowDebug; }));

            InitializeComponent();

            Config = new Config();

            // Inform the lib to refresh stats
            Config.Player.Stats = true; 

            // To prevent capturing keys while we type within textboxes etc
            Config.Player.KeyBindings.FlyleafWindow = false;

            Player = new Player(Config);

            DataContext = this;

            // Keep track of error messages
            Player.OpenCompleted += (o, e) => { LastError = e.Error; };
            Player.BufferingCompleted += (o, e) => { LastError = e.Error; };
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
