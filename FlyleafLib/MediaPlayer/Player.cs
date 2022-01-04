using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using FlyleafLib.Controls;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaDemuxer;

using static FlyleafLib.Utils;

namespace FlyleafLib.MediaPlayer
{
    public unsafe partial class Player : NotifyPropertyChanged, IDisposable
    {
        #region Properties
        public bool                 IsDisposed          { get; private set; }

        /// <summary>
        /// Flyleaf Control (WinForms)
        /// (Normally you should not access this directly)
        /// </summary>
        public Flyleaf              Control             { get => _Control; set { InitializeControl1(_Control, value); } }
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
        public Renderer             renderer            => decoder?.VideoDecoder?.Renderer;

        /// <summary>
        /// Player's Decoder Context
        /// (Normally you should not access this directly)
        /// </summary>
        public DecoderContext       decoder;

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
        public Demuxer              MainDemuxer         { get; private set; }

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

            MainDemuxer.UpdateCurTime();

            if (MainDemuxer.HLSPlaylist != null)
            {
                curTime  = MainDemuxer.CurTime;
                duration = MainDemuxer.Duration;
                Duration = Duration;
            }

            Set(ref _CurTime, curTime, true, nameof(CurTime));

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
                
                MainDemuxer.UpdateCurTime();
                return MainDemuxer.BufferedDuration;
            } 
                                                                            internal set => Set(ref _BufferedDuration, value); }
        long _BufferedDuration;

        /// <summary>
        /// Whether the input is live (duration might not be 0 on live sessions to allow live seek, eg. hls)
        /// </summary>
        public bool         IsLive              { get => isLive;            private set => Set(ref _IsLive, value); }
        bool _IsLive, isLive;

        public bool         IsPlaylist          { get => isPlaylist;        private set => Set(ref _IsPlaylist, value); }
        bool _IsPlaylist, isPlaylist;

        ///// <summary>
        ///// Total bitrate (Kbps)
        ///// </summary>
        public double       BitRate             { get => bitRate;           internal set => Set(ref _BitRate, value); }
        internal double _BitRate, bitRate;

        /// <summary>
        /// Input's title
        /// </summary>
        public string       Title               { get => title;             private set => Set(ref _Title, value); }
        string _Title, title;

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
                else if (value > 4)
                    newValue = 4;
                else if (value > 1)
                    newValue = (int) value;

                if (newValue == _Speed || newValue > 1 && ReversePlayback)
                    return;

                isSubsSwitch = true;
                isAudioSwitch = true;

                if (newValue > 1)
                {
                    VideoDecoder.Speed      = (int)newValue;
                    AudioDecoder.Speed      = (int)newValue;
                    SubtitlesDecoder.Speed  = (int)newValue;
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

                lock (lockPlayPause)
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

        // TBR: No UI updates and some of them maybe should not be exposed

        /// <summary>
        /// Whether playback has been completed
        /// </summary>
        public bool         HasEnded            => decoder != null && (VideoDecoder.Status == MediaFramework.Status.Ended || (VideoDecoder.Disposed && AudioDecoder.Status == MediaFramework.Status.Ended));
        public bool         IsSeeking           { get; private set; }
        public bool         IsSwaping           { get; private set; }
        public bool         IsOpening           { get; private set; }
        public bool         IsOpeningInput      { get; private set; }
        #endregion

        #region Properties Internal
        Thread tSeek, tPlay;
        object lockOpen         = new object();
        object lockSeek         = new object();
        object lockPlayPause    = new object();
        object lockSubtitles    = new object();

        ConcurrentStack<SeekData>       seeks       = new ConcurrentStack<SeekData>();
        ConcurrentStack<OpenData>       opens       = new ConcurrentStack<OpenData>();
        ConcurrentStack<OpenInputData>  inputopens  = new ConcurrentStack<OpenInputData>();
        internal ConcurrentQueue<Action> UIActions  = new ConcurrentQueue<Action>();

        internal AudioFrame     aFrame;
        internal VideoFrame     vFrame;
        internal SubtitlesFrame sFrame, sFramePrev;

        bool reversePlaybackResync;
        bool requiresBuffering;

        bool isVideoSwitch;
        bool isAudioSwitch;
        bool isSubsSwitch;

        long elapsedTicks;
        long startedAtTicks;
        long videoStartTicks;

        Flyleaf swap_Control; // Required for swap in WinForms
        bool swap_WasPlaying;
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
                Log($"Creating (Usage = {Config.Player.Usage}) ... (initializing the decoder)");
                InitializeDecoder();
            }
            else
                Log($"Creating (Usage = {Config.Player.Usage}) ... (1/3 waiting for control to set)");
        }
        private void InitializeControl1(Flyleaf oldValue, Flyleaf newValue)
        {
            lock (this)
            {
                if (newValue == null) return;

                if (oldValue != null && newValue != null)
                {
                    if (oldValue.Handle == newValue.Handle) return;

                    _Control = newValue;
                }
                else
                {
                    Log($"Creating (Usage = {Config.Player.Usage}) ... (2/3 waiting for handle to be created)");

                    if (newValue.Handle != IntPtr.Zero)
                        InitializeControl2(newValue);
                    else
                        newValue.HandleCreated += (o, e) => { InitializeControl2(newValue); };
                }
            }   
        }
        private void InitializeControl2(Flyleaf newValue)
        {
            lock (this)
            {
                _Control = newValue;
                _Control.Player = this;
                
                SubscribeEvents();
                Log($"Creating (Usage = {Config.Player.Usage}) ... (3/3 initializing the decoder)");
                InitializeDecoder();
            }
        }
        private void InitializeDecoder()
        {
            if (Master.GetPlayerPos(PlayerId) != -1)
            {
                if (Control == null || decoder == null || Config.Player.Usage != Usage.Audio)
                    throw new Exception("PlayerId already exists");
                else
                {
                    VideoDecoder.Dispose();
                    VideoDecoder.DisposeVA();
                    decoder.VideoDecoder = new VideoDecoder(decoder.Config, Control, decoder.UniqueId);
                    VideoDecoder.CodecChanged = Decoder_VideoCodecChanged;

                    Log("Created");
                    return;
                }
            }

            Master.AddPlayer(this);
            decoder = new DecoderContext(Config, Control, PlayerId);

            decoder.VideoInputOpened        += Decoder_VideoInputOpened;
            decoder.AudioInputOpened        += Decoder_AudioInputOpened;
            decoder.SubtitlesInputOpened    += Decoder_SubtitlesInputOpened;
            decoder.VideoStreamOpened       += Decoder_VideoStreamOpened;
            decoder.AudioStreamOpened       += Decoder_AudioStreamOpened;
            decoder.SubtitlesStreamOpened   += Decoder_SubtitlesStreamOpened;

            AudioDecoder.CodecChanged        = Decoder_AudioCodecChanged;
            VideoDecoder.CodecChanged        = Decoder_VideoCodecChanged;
            decoder.RecordingCompleted += (o, e) => { IsRecording = false; };

            MainDemuxer = VideoDemuxer;
            Reset();

            if (Config.Player.Usage != Usage.Audio)
                renderer.Present();

            Log("Created");
        }
        public void SubscribeEvents()
        {
            UnsubscribeEvents();

            #if DEBUG
            Log($"Subscribing Events to Player #{PlayerId}");
            #endif

            if (Config.Player.KeyBindings.Enabled)
            {
                if (VideoView != null)
                {
                    if (Config.Player.KeyBindings.FlyleafWindow)
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
                    if (Config.Player.KeyBindings.FlyleafWindow)
                        VideoView.WindowFront.KeyUp += WindowFront_KeyUp;

                    VideoView.WinFormsHost.KeyUp    += WinFormsHost_KeyUp;
                }
                else
                    _Control.KeyUp += Control_KeyUp;

                Config.Player.KeyBindings.Keys.Clear();
            }

            if (Config.Player.MouseBindigns.OpenOnDragAndDrop)
            {
                _Control.AllowDrop  = true;
                _Control.DragEnter += Control_DragEnter;
                _Control.DragDrop  += Control_DragDrop;
            }

            if (Config.Player.MouseBindigns.ToggleFullScreenOnDoubleClick)
                _Control.DoubleClick+= Control_DoubleClick;

            if (Config.Player.MouseBindigns.PanMoveOnDragAndCtrl)
            {
                _Control.MouseDown  += Control_MouseDown;
                _Control.MouseUp    += Control_MouseUp;
                _Control.MouseMove  += Control_MouseMove;

            } else if (Config.Player.ActivityMode)
            {
                _Control.MouseMove  += Control_MouseMove;
                _Control.MouseDown  += Control_MouseDown;
            }

            if (Config.Player.MouseBindigns.PanZoomOnWheelAndCtrl)
                _Control.MouseWheel += Control_MouseWheel;
            
            Activity.ForceFullActive();
        }
        public void UnsubscribeEvents()
        {
            #if DEBUG
            Log($"Unsubscribing Events from Player #{PlayerId}");
            #endif

            if (VideoView != null)
            {
                VideoView.WindowFront.KeyDown   -= WindowFront_KeyDown;
                VideoView.WinFormsHost.KeyDown  -= WinFormsHost_KeyDown;
                VideoView.WindowFront.KeyUp     -= WindowFront_KeyUp;
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
        /// Switch player with another's player control (WinForms only) - For WPF just set VideoView's Player
        /// </summary>
        /// <param name="player">Specify the Player that has the required control</param>
        public void SwitchPlayer(Player player)
        {
            SwapPlayer(player, null);
        }
        internal void SwapPlayer(Player player, VideoView videoView)
        {
            lock(this)
                lock(player)
                {
                    Log($"Swaping Player {PlayerId} with Player {player.PlayerId}");

                    bool swapCompleted = false;

                    if (!IsSwaping)
                    {
                        UnsubscribeEvents();
                        IsSwaping = true;
                        swap_WasPlaying = IsPlaying;
                        Pause();
                        decoder.Pause();
                        decoder.VideoDecoder.DisposeFrames();
                        canPlay = false;
                        UI(() => CanPlay = CanPlay);
                        if (VideoView == null)
                            swap_Control = _Control;
                    }
                    else
                        swapCompleted = true;

                    if (!player.IsSwaping)
                    {
                        player.UnsubscribeEvents();
                        player.IsSwaping = true;
                        player.swap_WasPlaying = player.IsPlaying;
                        player.Pause();
                        player.decoder.Pause();
                        player.decoder.VideoDecoder.DisposeFrames();
                        player.canPlay = false;
                        UI(() => player.CanPlay = player.CanPlay);
                    }
                    else
                        player.IsSwaping = false;

                    if (VideoView != null)
                    {
                        VideoView = videoView;
                        Control = VideoView.FlyleafWF;
                    }
                    else
                    {
                        _Control = player.swap_Control != null ? player.swap_Control : player._Control;
                        player.swap_Control = null;
                    }

                    VideoDecoder.Swap(player.VideoDecoder);
                    _Control.Player = this; // Changes the renderer to the control
                    SubscribeEvents();

                    IsSwaping = !swapCompleted;
                    canPlay = Video.IsOpened || Audio.IsOpened ? true : false;
                    UI(() => CanPlay = CanPlay);
                    ReSync(VideoDecoder.VideoStream);
                    if (swap_WasPlaying) Play();
                }
        }
        
        /// <summary>
        /// Disposes the Player and the hosted VideoView if any
        /// </summary>
        public void Dispose() { Master.DisposePlayer(this); }
        internal void DisposeInternal()
        {
            lock (this)
            {
                if (IsDisposed) return;

                try
                {
                    DisableNotifications = true;
                    Stop();

                    Audio.Dispose(); 
                    decoder.Dispose();

                    decoder = null;
                    Config = null;

                    IsDisposed = true;

                    if (VideoView != null && VideoView.WindowFront != null && !VideoView.WindowFront.Disposing)
                    {
                        if (VideoView.WindowFront.Disposed) return;

                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => { VideoView?.WindowFront?.Close(); GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced); } ));
                        return;
                    }
                    else
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                } catch (Exception) { }
            }
        }

        private void ResetMe()
        {
            if (renderer != null)
            {
                renderer.DisableRendering = true;
                renderer.Present();
            }

            bitRate     = 0;
            curTime     = 0;
            duration    = 0;
            isLive      = false;
            isPlaylist  = false;
            title       = "";

            UIAdd(() =>
            {
                BitRate     = BitRate;
                Duration    = Duration;
                IsLive      = IsLive;
                IsPlaylist  = IsPlaylist;
                Title       = Title;
                UpdateCurTime();
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
        private void Initialize()
        {
            try
            {
                Log($"[Initializing]");

                TimeBeginPeriod(1);

                status = Status.Stopped;
                canPlay = false;
                ReversePlayback = false;
                seeks.Clear();
                EnsureThreadDone(tSeek);
                EnsureThreadDone(tPlay);
                decoder.Initialize(); // Null exception if control's handle is not created yet
                UIAdd(() =>
                {
                    Status  = Status;
                    CanPlay = CanPlay;
                });
                Reset();

                Log($"[Initialized]");

            } catch (Exception e)
            {
                Log($"Initialize() Error: {e.Message}");

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
            if (Master.HighPerformaceTimers) return;

            NativeMethods.TimeBeginPeriod(i);
        }
        private void TimeEndPeriod(uint i)
        {
            if (Master.HighPerformaceTimers) return;

            NativeMethods.TimeEndPeriod(i);
        }
        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{PlayerId}] [Player] {msg}"); }
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