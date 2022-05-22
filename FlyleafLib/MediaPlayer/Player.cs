using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using FlyleafLib.Controls;
using FlyleafLib.Controls.WPF;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaDemuxer;

using static FlyleafLib.Utils;
using static FlyleafLib.Logger;

namespace FlyleafLib.MediaPlayer
{
    public unsafe partial class Player : NotifyPropertyChanged, IDisposable
    {
        #region Properties
        public bool                 IsDisposed          { get; private set; }

        /// <summary>
        /// Flyleaf Control (WinForms)
        /// (WPF: Normally you should not access this directly <see cref="SwapPlayers(Player, Player)"/>)
        /// </summary>
        public Flyleaf              Control             { get => _Control; set { SetControl(_Control, value); } }
        internal Flyleaf _Control;

        /// <summary>
        /// The Content Control which hosts WindowsFormsHost (WPF)
        /// (Normally you should not access this directly)
        /// </summary>
        public VideoView            VideoView           { get; set; }

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
        public bool         IsPlaying           => Status == Status.Playing;

        /// <summary>
        /// Whether the player's status is capable of accepting playback commands
        /// </summary>
        public bool         CanPlay             { get => canPlay;           internal set => Set(ref _CanPlay, value); }
        internal bool _CanPlay, canPlay;

        /// <summary>
        /// Whether the player's state is in fullscreen mode
        /// </summary>
        public bool         IsFullScreen        { get => _IsFullScreen;     private set { if (_IsFullScreen == value) return; _IsFullScreen = value; UI(() => Set(ref _IsFullScreen, value, false)); } }
        bool _IsFullScreen;

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
            if (MainDemuxer == null || seeks.Count != 0) return;

            if (MainDemuxer.HLSPlaylist != null)
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
            get => decoder != null ? decoder.IsRecording : false;
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

                if (newValue > 1)
                {
                    int skipFramesOffset = newValue % 1 == 0 ? 0 : 1;
                    VideoDecoder.Speed      = (int)newValue + skipFramesOffset;
                    AudioDecoder.Speed      = (int)newValue + skipFramesOffset;
                    SubtitlesDecoder.Speed  = (int)newValue + skipFramesOffset;
                }
                else
                {
                    VideoDecoder.Speed      = 1;
                    AudioDecoder.Speed      = 1;
                    SubtitlesDecoder.Speed  = 1;
                }

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

        /// <summary>
        /// Whether playback has been completed
        /// </summary>
        public bool         HasEnded            => decoder != null && (VideoDecoder.Status == MediaFramework.Status.Ended || (VideoDecoder.Disposed && AudioDecoder.Status == MediaFramework.Status.Ended));
        #endregion

        #region Properties Internal
        object lockActions  = new object();
        object lockSubtitles= new object();

        bool taskSeekRuns;
        bool taskPlayRuns;
        bool taskOpenAsyncRuns;

        ConcurrentStack<SeekData>   seeks       = new ConcurrentStack<SeekData>();
        ConcurrentQueue<Action>     UIActions  = new ConcurrentQueue<Action>();

        internal AudioFrame     aFrame;
        internal VideoFrame     vFrame;
        internal SubtitlesFrame sFrame, sFramePrev;
        internal PlayerStats    stats = new PlayerStats();
        internal LogHandler     Log;

        bool reversePlaybackResync;
        bool requiresBuffering;

        bool isVideoSwitch;
        bool isAudioSwitch;
        bool isSubsSwitch;

        long elapsedTicks;
        long startedAtTicks;
        long videoStartTicks;
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
            Log = new LogHandler($"[#{PlayerId}] [Player        ] ");
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

            Reset();
            Log.Debug("Created");
        }
        private void SetControl(Flyleaf oldValue, Flyleaf newValue)
        {
            lock (this)
            {
                if (newValue == null)
                    return;

                if (oldValue != null)
                {
                    if (oldValue.Handle == newValue.Handle)
                        return;

                    throw new Exception("Cannot change Player's control");
                }

                _Control = newValue;
                _Control.Player = this;
                SubscribeEvents();
                VideoDecoder.CreateRenderer(_Control);
            }   
        }

        public void SubscribeEvents()
        {
            UnsubscribeEvents();
            Log.Trace($"Subscribing Events to Player #{PlayerId}");

            if (Config.Player.KeyBindings.Enabled)
            {
                if (VideoView != null)
                {
                    if (Config.Player.KeyBindings.FlyleafWindow && VideoView.WindowFront != null)
                    {
                        VideoView.WindowFront.KeyUp     += WindowFront_KeyUp;
                        VideoView.WindowFront.KeyDown   += WindowFront_KeyDown;
                    }

                    VideoView.WinFormsHost.KeyUp    += WinFormsHost_KeyUp;
                    VideoView.WinFormsHost.KeyDown  += WinFormsHost_KeyDown;
                }
                else
                {
                    _Control.KeyDown += Control_KeyDown;
                    _Control.KeyUp += Control_KeyUp;
                }
            }
            else if (Config.Player.ActivityMode)
            {
                if (VideoView != null)
                {
                    if (Config.Player.KeyBindings.FlyleafWindow && VideoView.WindowFront != null)
                        VideoView.WindowFront.KeyUp += WindowFront_KeyUp;

                    VideoView.WinFormsHost.KeyUp    += WinFormsHost_KeyUp;
                }
                else
                    _Control.KeyUp += Control_KeyUp;

                Config.Player.KeyBindings.Keys.Clear();
            }

            if (Config.Player.MouseBindings.OpenOnDragAndDrop)
            {
                _Control.AllowDrop  = true;
                _Control.DragEnter += Control_DragEnter;
                _Control.DragDrop  += Control_DragDrop;
            }

            if (Config.Player.MouseBindings.ToggleFullScreenOnDoubleClick)
                _Control.DoubleClick+= Control_DoubleClick;

            if (Config.Player.MouseBindings.PanMoveOnDragAndCtrl)
            {
                _Control.MouseDown  += Control_MouseDown;
                _Control.MouseUp    += Control_MouseUp;
                _Control.MouseMove  += Control_MouseMove;

            } else if (Config.Player.ActivityMode)
            {
                _Control.MouseMove  += Control_MouseMove;
                _Control.MouseDown  += Control_MouseDown;
            }

            if (Config.Player.MouseBindings.PanZoomOnWheelAndCtrl)
                _Control.MouseWheel += Control_MouseWheel;
            
            Activity.ForceFullActive();
        }
        public void UnsubscribeEvents()
        {
            Log.Trace($"Unsubscribing Events from Player #{PlayerId}");

            if (VideoView != null)
            {
                if (VideoView.WindowFront != null)
                {
                    VideoView.WindowFront.KeyDown   -= WindowFront_KeyDown;
                    VideoView.WindowFront.KeyUp     -= WindowFront_KeyUp;
                }
                
                VideoView.WinFormsHost.KeyDown  -= WinFormsHost_KeyDown;
                VideoView.WinFormsHost.KeyUp    -= WinFormsHost_KeyUp;
            }
            
            if (_Control != null)
            {
                _Control.KeyDown    -= Control_KeyDown;
                _Control.KeyUp      -= Control_KeyUp;
                _Control.DoubleClick-= Control_DoubleClick;
                _Control.MouseDown  -= Control_MouseDown;
                _Control.MouseUp    -= Control_MouseUp;
                _Control.MouseMove  -= Control_MouseMove;
                _Control.MouseWheel -= Control_MouseWheel;
                _Control.AllowDrop  = false;
                _Control.DragEnter  -= Control_DragEnter;
                _Control.DragDrop   -= Control_DragDrop;
            }
        }

        /// <summary>
        /// Disposes the Player and the hosted VideoView if any
        /// </summary>
        public void Dispose() { Engine.DisposePlayer(this); }
        internal void DisposeInternal()
        {
            lock (lockActions)
            {
                if (IsDisposed) return;
                IsDisposed = true;

                try
                {
                    Stop();
                    Audio.Dispose(); 
                    decoder.Dispose();

                    if (VideoView != null)
                        UI(new Action(() => { VideoView?.Dispose(); GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced); } ));
                    else
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);

                    Log.Info("Disposed");
                } catch (Exception e) { Log.Warn($"Disposed ({e.Message})"); }
            }
        }

        private void ResetMe()
        {
            if (renderer != null)
            {
                // TBR: Probably we just need to clear the screen with config's backcolor sometimes but render can be disposed
                renderer.DisableRendering = true;
                renderer.Present();
            }

            canPlay     = false;
            status      = Status.Stopped;
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
        private void Initialize(Status status = Status.Stopped, bool andDecoder = true)
        {
            try
            {
                if (CanDebug) Log.Debug($"Initializing");

                TimeBeginPeriod(1);

                this.status = status;
                canPlay = false;
                isVideoSwitch = false;
                seeks.Clear();
                
                while (taskPlayRuns || taskSeekRuns) Thread.Sleep(5);

                if (andDecoder)
                    decoder.Stop();

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

        internal void UIAdd(Action action)
        {
            UIActions.Enqueue(action);
        }
        internal void UIAll()
        {
            while (UIActions.Count > 0)
                if (UIActions.TryDequeue(out Action action))
                    UI(action);
        }

        private void TimeBeginPeriod(uint i)
        {
            if (Engine.Config.HighPerformaceTimers)
                return;

            NativeMethods.TimeBeginPeriod(i);
        }
        private void TimeEndPeriod(uint i)
        {
            if (Engine.Config.HighPerformaceTimers)
                return;

            NativeMethods.TimeEndPeriod(i);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is Player))
                return false;

            if (((Player)obj).PlayerId == PlayerId)
                return true;

            return false;
        }
        public override int GetHashCode()
        {
            return PlayerId.GetHashCode();
        }
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
        LowLatencyVideo
    }
}