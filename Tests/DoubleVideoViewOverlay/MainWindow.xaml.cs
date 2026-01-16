using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace DoubleFlyleafHostOverlay
{
    /// <summary>
    /// Testing FlyleafHost within another FlyleafHost
    ///
    /// This sample demonstrates a second videoview which follows the first videoview's input
    /// and previews the seeking frame/position before the actual seeking on the main player
    /// </summary>
    public partial class MainWindow : Window
    {
        public Player Player        { get; set; }
        public Player PlayerSeek    { get; set; }
        public bool   IsSeeking     { get; set; }

        public string SampleVideo   { get; set; } = Utils.FindFileBelow("Sample.mp4");

        Binding sliderBinding;

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
                UIRefreshInterval= 100
            });

            InitializeComponent();

            Player = new Player();
            PlayerSeek = new Player();

            // Disables Mouse/Keys/Audio on Preview/Seek Player
            PlayerSeek.Config.Audio.Enabled = false;
            PlayerSeek.Config.Player.AutoPlay = false;
            //PlayerSeek.Config.Video.AspectRatio = AspectRatio.Fill;

            DataContext = this;

            Player.OpenCompleted += Player_OpenCompleted;

            sliderBinding = new Binding("Player.CurTime");
            sliderBinding.Mode = BindingMode.OneWay;
        }

        private void Player_OpenCompleted(object sender, OpenCompletedArgs e)
        {
            if (!e.Success)
                return;

            // Prepares the Preview/Seek Player with the same input as the main player
            PlayerSeek.Open(Player.decoder.Playlist.Url);
        }

        private void Slider_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed && !IsSeeking)
            {
                // Prevents the CurTime update on Slider
                BindingOperations.ClearBinding(SliderSeek, Slider.ValueProperty);
                SeekView.Visibility = Visibility.Visible;
                IsSeeking = true;
            }
        }

        private void Slider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsSeeking)
            {
                SeekView.Visibility = Visibility.Collapsed;

                // Seek released so it will seek on the main player at current Preview/Seek player position
                Player.SeekAccurate((int) (PlayerSeek.CurTime / 10000));

                // Enables the CurTime update on Slider
                BindingOperations.SetBinding(SliderSeek, Slider.ValueProperty, sliderBinding);
            }

            IsSeeking = false;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsSeeking)
                return;

            // While sliding/seeking updates the Preview/Seek player's frame by seeking (accurate)
            PlayerSeek.SeekAccurate((int) (e.NewValue / 10000));
        }
    }
}
