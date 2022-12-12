using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaDemuxer;

using static FlyleafLib.Utils;
using static FlyleafLib.Logger;

using WPFHost = FlyleafLib.Controls.WPF.FlyleafHost;
using WFHost  = FlyleafLib.Controls.WinForms.FlyleafHost;

namespace FlyleafLib.MediaPlayer
{
    public unsafe partial class Player : NotifyPropertyChanged, IDisposable
    {
        #region Properties
        public bool                 IsDisposed          { get; private set; }

        /// <summary>
        /// FlyleafHost WinForms
        /// (Normally you should not access this directly)
        /// </summary>
        public WFHost               WFHost              { get => _WFHost;  internal set => Set(ref _WFHost,  value); }
        WFHost _WFHost;

        /// <summary>
        /// FlyleafHost WPF
        /// (Normally you should not access this directly)
        /// </summary>
        public WPFHost              WPFHost             { get => _WPFHost; internal set => Set(ref _WPFHost, value); }
        WPFHost _WPFHost;

        /// <summary>
        /// Player's Activity (Idle/Active/FullActive)
        /// </summary>
        public Activity             Activity            { get; private set; }

        /// <summary>
        /// Helper ICommands for WPF MVVM
        /// </summary>
        public Commands             Commands            { get; private set; }

        public Playlist             Playlist            => decoder.Playlist;

        /// <summary>
        /// Player's Audio (In/Out)
        /// </summary>
        public Audio                Audio               { get; private set; }

        /// <summary>
        /// Player's Video
        /// </summary>
        public Video                Video               { get; private set; }

        /// <summary>
        /// Player's Subtitles
        /// </summary>
        public Subtitles            Subtitles           { get; private set; }

        /// <summary>
        /// Player's Renderer
        /// (Normally you should not access this directly)
        /// </summary>
        public Renderer             renderer            => decoder.VideoDecoder.Renderer;

        /// <summary>
        /// Player's Decoder Context
        /// (Normally you should not access this directly)
        /// </summary>
        public DecoderContext       decoder             { get; private set; }

        /// <summary>
        /// Audio Decoder
        /// (Normally you should not access this directly)
        /// </summary>
        public AudioDecoder         AudioDecoder        => decoder.AudioDecoder;

        /// <summary>
        /// Video Decoder
        /// (Normally you should not access this directly)
        /// </summary>
        public VideoDecoder         VideoDecoder        => decoder.VideoDecoder;

        /// <summary>
        /// Subtitles Decoder
        /// (Normally you should not access this directly)
        /// </summary>
        public SubtitlesDecoder     SubtitlesDecoder    => decoder.SubtitlesDecoder;

        /// <summary>
        /// Main Demuxer (if video disabled or audio only can be AudioDemuxer instead of VideoDemuxer)
        /// (Normally you should not access this directly)
        /// </summary>
        public Demuxer              MainDemuxer         => decoder.MainDemuxer;

        /// <summary>
        /// Audio Demuxer
        /// (Normally you should not access this directly)
        /// </summary>
        public Demuxer              AudioDemuxer        => decoder.AudioDemuxer;

        /// <summary>
        /// Video Demuxer
        /// (Normally you should not access this directly)
        /// </summary>
        public Demuxer              VideoDemuxer        => decoder.VideoDemuxer;

        /// <summary>
        /// Subtitles Demuxer
        /// (Normally you should not access this directly)
        /// </summary>
        public Demuxer              SubtitlesDemuxer    => decoder.SubtitlesDemuxer;


        /// <summary>
        /// Player's incremental unique id
        /// </summary>
        public int          PlayerId            { get; private set; }

        /// <summary>
        /// Player's configuration (set once in the constructor)
        /// </summary>
        public Config       Config              { get; protected set; }

        /// <summary>
        /// Player's Status
        /// </summary>
        public Status       Status              { get => status;            private set => Set(ref _Status, value); }
        Status _Status = Status.Stopped, status = Status.Stopped;
        public bool         IsPlaying           => status == Status.Playing;

        /// <summary>
        /// Whether the player's status is capable of accepting playback commands
        /// </summary>
        public bool         CanPlay             { get => canPlay;           internal set => Set(ref _CanPlay, value); }
        internal bool _CanPlay, canPlay;

        /// <summary>
        /// The list of chapters
        /// </summary>
        public List<Demuxer.Chapter> 
                            Chapters            => VideoDemuxer?.Chapters;

        /// <summary>
        /// Player's current time or user's current seek time (uses backward/forward direction based on previous time or accurate seek based on Config.Player.SeekAccurate)
        /// </summary>
        public long         CurTime             { get => curTime;           set { if (Config.Player.SeekAccurate) SeekAccurate((int) (value/10000)); else Seek((int) (value/10000), value > curTime); } }
        long _CurTime, curTime;
        internal void UpdateCurTime()
        {
            if (MainDemuxer == null || !seeks.IsEmpty)
                return;

            if (MainDemuxer.IsHLSLive)
            {
                curTime  = MainDemuxer.CurTime;
                duration = MainDemuxer.Duration;
                Duration = Duration;
            }

            Set(ref _CurTime, curTime, true, nameof(CurTime));

            UpdateBufferedDuration();
        }
        internal void UpdateBufferedDuration()
        {
            if (_BufferedDuration != MainDemuxer.BufferedDuration)
            {
                _BufferedDuration = MainDemuxer.BufferedDuration;
                Raise(nameof(BufferedDuration));
            }
        }

        /// <summary>
        /// Input's duration
        /// </summary>
        public long         Duration            { get => duration;          private set => Set(ref _Duration, value); }
        long _Duration, duration;

        /// <summary>
        /// The current buffered duration in the demuxer
        /// </summary>
        public long         BufferedDuration    { get 
            {
                if (MainDemuxer == null)
                    return 0;
                
                return MainDemuxer.BufferedDuration;
            } 
                                                                            internal set => Set(ref _BufferedDuration, value); }
        long _BufferedDuration;

        /// <summary>
        /// Whether the input is live (duration might not be 0 on live sessions to allow live seek, eg. hls)
        /// </summary>
        public bool         IsLive              { get => isLive;            private set => Set(ref _IsLive, value); }
        bool _IsLive, isLive;

        ///// <summary>
        ///// Total bitrate (Kbps)
        ///// </summary>
        public double       BitRate             { get => bitRate;           internal set => Set(ref _BitRate, value); }
        internal double _BitRate, bitRate;

        /// <summary>
        /// Whether the player is recording
        /// </summary>
        public bool         IsRecording
        {
            get => decoder != null && decoder.IsRecording;
            private set { if (_IsRecording == value) return; _IsRecording = value; UI(() => Set(ref _IsRecording, value, false)); }
        }
        bool _IsRecording;

        /// <summary>
        /// Pan X Offset to change the X location
        /// </summary>
        public int          PanXOffset          { get => renderer.PanXOffset; set { renderer.PanXOffset = value; Raise(nameof(PanXOffset)); } }

        /// <summary>
        /// Pan Y Offset to change the Y location
        /// </summary>
        public int          PanYOffset          { get => renderer.PanYOffset; set { renderer.PanYOffset = value; Raise(nameof(PanYOffset)); } }

        /// <summary>
        /// Playback's speed (x1 - x4)
        /// </summary>
        public double       Speed {
            get => _Speed; 
            set
            {
                double newValue = value;
                if (value <= 0 )
                    newValue = 0.25;
                else if (value > 16)
                    newValue = 16;

                if (newValue == _Speed || newValue > 1 && ReversePlayback)
                    return;

                isSubsSwitch = true;
                isAudioSwitch = true;

                int decodersSpeed = newValue < 1 ? 1 : (int)newValue;

                VideoDecoder.Speed      = decodersSpeed;
                AudioDecoder.Speed      = decodersSpeed;// + (newValue % 1 == 0 ? 0 : 1);
                //SubtitlesDecoder.Speed  = decodersSpeed;

                Subtitles.subsText = "";
                _Speed = newValue;

                UI(() =>
                {
                    Subtitles.SubsText = Subtitles.SubsText;
                    Raise(nameof(Speed));
                });

                aFrame = null;
                sFrame = null;
                sFramePrev = null;

                isSubsSwitch = false;
                isAudioSwitch = false;
            }
        }
        double _Speed = 1;

        /// <summary>
        /// Pan zoom in/out per pixel of each side (should be based on Control's width/height)
        /// </summary>
        public int          Zoom
        {
            get => renderer.Zoom;
            set { if (renderer.Zoom == value) return; renderer.Zoom = value; UI(() => Set(ref _Zoom, renderer.Zoom, false)); }
        }
        int _Zoom;

        /// <summary>
        /// Pan rotation angle (for D3D11 VP allowed values are 0, 90, 180, 270 only)
        /// </summary>
        public int     Rotation            { get => _Rotation; 
            set
            {
                renderer.Rotation = value;
                Set(ref _Rotation, renderer.Rotation);
            }
        }
        int _Rotation;

        /// <summary>
        /// Whether to use reverse playback mode
        /// </summary>
        public bool         ReversePlayback
        {
            get => _ReversePlayback;

            set
            {
                if (_ReversePlayback == value)
                    return;

                _ReversePlayback = value;
                UI(() => Set(ref _ReversePlayback, value, false));

                if (!Video.IsOpened || !CanPlay | IsLive)
                    return;

                lock (lockActions)
                {
                    bool shouldPlay = IsPlaying;
                    Pause();
                    sFrame = null;
                    Subtitles.subsText = "";
                    if (Subtitles._SubsText != "")
                        UI(() => Subtitles.SubsText = Subtitles.SubsText);
                    decoder.StopThreads();
                    decoder.Flush();

                    if (value)
                    {
                        Speed = 1;
                        VideoDemuxer.EnableReversePlayback(CurTime);
                    }
                    else
                    {
                        VideoDemuxer.DisableReversePlayback();

                        if (Status == Status.Ended)
                        {
                            status = Status.Paused;
                            UI(() => Status = Status);
                        }

                        VideoFrame vFrame = VideoDecoder.GetFrame(VideoDecoder.GetFrameNumber(CurTime));
                        VideoDecoder.DisposeFrame(vFrame);
                        vFrame = null;
                        decoder.RequiresResync = true;
                    }

                    reversePlaybackResync = false;
                    if (shouldPlay) Play();
                }
            }
        }
        bool _ReversePlayback;

        public object       Tag                 { get; set; }
        public string       LastError           { get => lastError; set => Set(ref _LastError, value); } 
        string _LastError, lastError;
        bool decoderHasEnded => decoder != null && (VideoDecoder.Status == MediaFramework.Status.Ended || (VideoDecoder.Disposed && AudioDecoder.Status == MediaFramework.Status.Ended));
        #endregion

        #region Properties Internal
        readonly object lockActions  = new object();
        readonly object lockSubtitles= new object();

        bool taskSeekRuns;
        bool taskPlayRuns;
        bool taskOpenAsyncRuns;

        readonly ConcurrentStack<SeekData>   seeks       = new ConcurrentStack<SeekData>();
        readonly ConcurrentQueue<Action>     UIActions  = new ConcurrentQueue<Action>();

        internal AudioFrame     aFrame;
        internal VideoFrame     vFrame;
        internal SubtitlesFrame sFrame, sFramePrev;
        internal PlayerStats    stats = new PlayerStats();
        internal LogHandler     Log;

        internal bool IsFullScreen;
        internal bool requiresBuffering;
        bool reversePlaybackResync;

        bool isVideoSwitch;
        bool isAudioSwitch;
        bool isSubsSwitch;
        #endregion

		public Player(Config config = null)
        {
            if (config != null)
            {
                if (config.Player.player != null)
                    throw new Exception("Player's configuration is already assigned to another player");

                Config = config;
            }
            else
                Config = new Config();

            PlayerId = GetUniqueId();
            Log = new LogHandler(("[#" + PlayerId + "]").PadRight(8, ' ') + " [Player        ] ");
            Log.Debug($"Creating Player (Usage = {Config.Player.Usage})");

            Activity    = new Activity(this);
            Audio       = new Audio(this);
            Video       = new Video(this);
            Subtitles   = new Subtitles(this);
            Commands    = new Commands(this);

            Config.SetPlayer(this);
            
            if (Config.Player.Usage == Usage.Audio)
            {
                Config.Video.Enabled = false;
                Config.Subtitles.Enabled = false;
            }

            Engine.AddPlayer(this);
            decoder = new DecoderContext(Config, PlayerId);

            //decoder.OpenPlaylistItemCompleted              += Decoder_OnOpenExternalSubtitlesStreamCompleted;
            
            decoder.OpenAudioStreamCompleted               += Decoder_OpenAudioStreamCompleted;
            decoder.OpenVideoStreamCompleted               += Decoder_OpenVideoStreamCompleted;
            decoder.OpenSubtitlesStreamCompleted           += Decoder_OpenSubtitlesStreamCompleted;

            decoder.OpenExternalAudioStreamCompleted       += Decoder_OpenExternalAudioStreamCompleted;
            decoder.OpenExternalVideoStreamCompleted       += Decoder_OpenExternalVideoStreamCompleted;
            decoder.OpenExternalSubtitlesStreamCompleted   += Decoder_OpenExternalSubtitlesStreamCompleted;

            AudioDecoder.CodecChanged = Decoder_AudioCodecChanged;
            VideoDecoder.CodecChanged = Decoder_VideoCodecChanged;
            decoder.RecordingCompleted += (o, e) => { IsRecording = false; };

            status = Status.Stopped;
            Reset();
            Log.Debug("Created");
        }

        /// <summary>
        /// Disposes the Player and the hosted FlyleafHost if any
        /// </summary>
        public void Dispose() { Engine.DisposePlayer(this); }
        internal void DisposeInternal()
        {
            lock (lockActions)
            {
                if (IsDisposed)
                    return;

                try
                {
                    Initialize();
                    Audio.Dispose(); 
                    decoder.Dispose();

                    // De-assign Player from Host
                    if (WPFHost != null)
                        UI(() => { if (WPFHost != null) WPFHost.Player = null; }); // UI Required for DP?
                    else if (WFHost != null)
                        WFHost.Player = null;

                    Log.Info("Disposed");
                } catch (Exception e) { Log.Warn($"Disposed ({e.Message})"); }

                IsDisposed = true;
            }
        }
        internal void RefreshMaxVideoFrames()
        {
            lock (lockActions)
            {
                if (!Video.isOpened)
                    return;

                bool wasPlaying = IsPlaying;
                Pause();
                VideoDecoder.RefreshMaxVideoFrames();
                ReSync(decoder.VideoStream, (int) (CurTime / 10000), true);

                if (wasPlaying)
                    Play();
            }
        }

        private void ResetMe()
        {
            canPlay     = false;
            bitRate     = 0;
            curTime     = 0;
            duration    = 0;
            isLive      = false;
            lastError   = null;

            UIAdd(() =>
            {
                BitRate     = BitRate;
                Duration    = Duration;
                IsLive      = IsLive;
                Status      = Status;
                CanPlay     = CanPlay;
                LastError   = LastError;
                BufferedDuration = 0;
                Set(ref _CurTime, curTime, true, nameof(CurTime));
            });
        }
        private void Reset()
        {
            ResetMe();
            Video.Reset();
            Audio.Reset();
            Subtitles.Reset();
            UIAll();
        }
        private void Initialize(Status status = Status.Stopped, bool andDecoder = true, bool isSwitch = false)
        {
            if (CanDebug) Log.Debug($"Initializing");

            lock (lockActions) // Required in case of OpenAsync and Stop requests
            {
                try
                {
                    TimeBeginPeriod(1);

                    this.status = status;
                    canPlay = false;
                    isVideoSwitch = false;
                    seeks.Clear();
                
                    while (taskPlayRuns || taskSeekRuns) Thread.Sleep(5);

                    if (andDecoder)
                    {
                        if (isSwitch)
                            decoder.InitializeSwitch();
                        else
                            decoder.Initialize();
                    }

                    Reset();
                    VideoDemuxer.DisableReversePlayback();
                    ReversePlayback = false;

                    if (CanDebug) Log.Debug($"Initialized");

                } catch (Exception e)
                {
                    Log.Error($"Initialize() Error: {e.Message}");

                } finally
                {
                    TimeEndPeriod(1);
                }
            }
        }

        internal void UIAdd(Action action)
        {
            UIActions.Enqueue(action);
        }
        internal void UIAll()
        {
            while (!UIActions.IsEmpty)
                if (UIActions.TryDequeue(out Action action))
                    UI(action);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is Player))
                return false;

            if (((Player)obj).PlayerId == PlayerId)
                return true;

            return false;
        }
        public override int GetHashCode() => PlayerId.GetHashCode();
    }

    public enum Status
    {
        Opening,
        Failed,
        Stopped,
        Paused,
        Playing,
        Ended
    }
    public enum Usage
    {
        AVS,
        Audio,
        LowLatencyVideo,
        ZeroLatencyAudioVideo
    }
}