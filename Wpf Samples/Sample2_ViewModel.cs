using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

using FlyleafWF = FlyleafLib.Controls.Flyleaf;

namespace Wpf_Samples
{
    public class Sample2_ViewModel : INotifyPropertyChanged
    {
        #region Airspace / Windows / Controls / Events
        /// <summary>
        /// VideoView wil be set once from the Binding OneWayToSource and then we can Initialize our ViewModel (Normally, this should be called only once)
        /// IsFullScreen/FullScreen/NormalScreen
        /// </summary>
        public VideoView    VideoView
        {
            get { return _VideoView; }
            set { _VideoView = value; Initialize(); }
        }
        private VideoView  _VideoView;

        /// <summary>
        /// WindowsFormsHost child control (to catch events and resolve airspace issues)
        /// </summary>
        public FlyleafWF    WinFormsControl => Player.Control;

        /// <summary>
        /// Foreground window with overlay content (to catch events and resolve airspace issues)
        /// </summary>
        public Window       WindowFront     => VideoView.WindowFront;

        /// <summary>
        /// Background/Main window
        /// </summary>
        public Window       WindowBack      => VideoView.WindowBack;
        #endregion

        #region ViewModel's Properties
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        private string _UserInput;
        public string   UserInput
        {
            get => _UserInput;
            set {  _UserInput = value; OnPropertyChanged(nameof(UserInput)); }
        }
        #endregion

        #region Using Library's Model & Basic ViewModel
        /// <summary>
        /// Player's functions (open new input or existing stream/play/pause/seek forewards or backwards etc.) + Plugins[Plugin_Name].[Media]Streams + Status [see Player.cs for details]
        /// </summary>
        public Player       Player      => VideoView?.Player;

        /// <summary>
        /// Audio Player's functions (play/pause) +  Volume/Mute [see AudioPlayer.cs]
        /// </summary>
        public AudioPlayer  AudioPlayer => Player.audioPlayer;

        /// <summary>
        /// Player's Current Session (CurTime/CanPlay/SubText/InitialUrl + [Media]Info/Cur[Media]Stream/ [see Session.cs for details]
        /// </summary>
        public Session      Session     => Player?.Session;

        /// <summary>
        /// Player's Configuration ([media].[config_attribute]) [see Config.cs for details]
        /// </summary>
        public Config       Config      => Player?.Config;
        public Config.Audio     Audio       => Config.audio;    // Enabled, DelayTicks, Languages (by priority)
        public Config.Subs      Subs        => Config.subs;     // Enabled, DelayTicks, Languages (by priority), UseOnlineDatabases
        public Config.Video     Video       => Config.video;    // AspectRatio, ClearColor, VSync
        public Config.Decoder   Decoder     => Config.decoder;  // HWAcceleration, VideoThreads + Buffering Configuration
        public Config.Demuxer   Demuxer     => Config.demuxer;  // [Media]FormatOpt + Buffering Configuration
        #endregion

        #region Initialize
        /// <summary>
        /// ViewMode's Constructor
        /// </summary>
        public Sample2_ViewModel()
        {
            OpenVideo   = new RelayCommand(OpenVideoAction);
            PauseVideo  = new RelayCommand(PauseVideoAction);
            PlayVideo   = new RelayCommand(PlayVideoAction);
        }

        /// <summary>
        /// ViewModel's Initialization as we have VideoView
        /// </summary>
        public void Initialize()
        {
            UserInput = "../../Sample.mp4";
            Player.OpenCompleted += Player_OpenCompleted;
        }
        #endregion

        #region Basic Open/Play/Pause Commands
        public ICommand     OpenVideo   { get ; set; }
        public ICommand     PauseVideo  { get ; set; }
        public ICommand     PlayVideo   { get ; set; }
        public void OpenVideoAction(object param)   { if (string.IsNullOrEmpty(UserInput)) UserInput = "../../Sample.mp4"; Player.Open(UserInput); }
        public void PauseVideoAction(object param)  { Player.Pause(); }
        public void PlayVideoAction(object param)   { Player.Play(); }
        #endregion

        #region Events
        private void Player_OpenCompleted(object sender, Player.OpenCompletedArgs e)
        {
            if (e.success && e.type == MediaType.Video)
                Player.Play();

            // Raise null is required for Player/Session/Config properties without property change updates (Normally, this should be called only once at the end of every OpenCompleted -mainly for video-)
            OnPropertyChanged(null);
        }
        #endregion
    }
}