﻿using FlyleafLib.Controls;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaPlayer;

public unsafe partial class Player : NotifyPropertyChanged, IDisposable
{
    #region Properties
    public bool                 IsDisposed          { get; private set; }

    /// <summary>
    /// FlyleafHost (WinForms, WPF or WinUI)
    /// </summary>
    public IHostPlayer          Host                { get => _Host; set => Set(ref _Host, value); }
    IHostPlayer _Host;

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
    /// Player's Data
    /// </summary>
    public Data                 Data                { get; private set; }

    /// <summary>
    /// Player's Decoder Context
    /// (Normally you should not access this directly)
    /// </summary>
    public DecoderContext       decoder             { get; private set; }

    /// <summary>
    /// Playlist
    /// (Normally you should not access this directly)
    /// </summary>
    public Playlist             Playlist            { get; private set; }

    /// <summary>
    /// Audio Decoder
    /// (Normally you should not access this directly)
    /// </summary>
    public AudioDecoder         AudioDecoder;

    /// <summary>
    /// Video Decoder
    /// (Normally you should not access this directly)
    /// </summary>
    public VideoDecoder         VideoDecoder;
    ConcurrentQueue<VideoFrame> vFrames;

    /// <summary>
    /// Player's Renderer
    /// (Normally you should not access this directly)
    /// </summary>
    public Renderer             renderer            { get; private set; }

    /// <summary>
    /// Subtitles Decoder
    /// (Normally you should not access this directly)
    /// </summary>
    public SubtitlesDecoder     SubtitlesDecoder;

    /// <summary>
    /// Data Decoder
    /// (Normally you should not access this directly)
    /// </summary>
    public DataDecoder          DataDecoder;

    /// <summary>
    /// Main Demuxer (if video disabled or audio only can be AudioDemuxer instead of VideoDemuxer)
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              MainDemuxer;
    internal void UpdateMainDemuxer()
    {
        var main = !VideoDemuxer.Disposed ? VideoDemuxer : AudioDemuxer;
        if (main != MainDemuxer)
        {
            if (MainDemuxer != null)
            {
                main.HLSDurationChanged = null;
                main.HLSCurTimeChanged  = null;
            }

            MainDemuxer = main;
            main.HLSDurationChanged = UpdateDurationHLS;
            main.HLSCurTimeChanged  = UpdateCurTimeHLS;
            
        }
    }

    /// <summary>
    /// Audio Demuxer
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              AudioDemuxer;

    /// <summary>
    /// Video Demuxer
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              VideoDemuxer;
    PacketQueue vPackets;

    /// <summary>
    /// Subtitles Demuxer
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              SubtitlesDemuxer;

    /// <summary>
    /// Data Demuxer
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              DataDemuxer;


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
    public Status       Status
    {
        get => status;
        private set
        {
            if (Set(ref _Status, value))
            {
                // Loop Playback
                if (value == Status.Ended)
                {
                    if (LoopPlayback && !ReversePlayback)
                    {
                        int seekMs = (int)(MainDemuxer.StartTime == 0 ? 0 : MainDemuxer.StartTime / 10000);
                        Seek(seekMs);
                    }
                }
            }
        }
    }
    internal volatile Status status = Status.Stopped;
    internal Status _Status = Status.Stopped;
    public bool         IsPlaying           => status == Status.Playing;

    /// <summary>
    /// Whether the player's status is capable of accepting playback commands
    /// </summary>
    public bool         CanPlay             { get => canPlay;           internal set => Set(ref _CanPlay, value); }
    internal bool _CanPlay, canPlay;

    /// <summary>
    /// The list of chapters
    /// </summary>
    public ObservableCollection<Demuxer.Chapter>
                        Chapters            => VideoDemuxer?.Chapters;

    /// <summary>
    /// Player's current time or user's current seek time (useful for two-way slide bar binding)
    /// </summary>
    public long         CurTime
    {
        get => curTime;
        set
        {
            if (Config.Player.SeekAccurate)
                SeekAccurate((int) (value/10000));
            else
                // Note: forward seeking casues issues to some formats and can have serious delays (eg. dash with h264, dash with vp9 works fine)
                //  Seek forward only when not local file and we have a chance to find it in cache (we consider this comes from slide bars)
                Seek((int)(value / 10000),
                    Playlist.InputType != InputType.File && Video.isOpened &&
                    value > VideoDemuxer.CurTime &&
                    value < VideoDemuxer.CurTime + VideoDemuxer.BufferedDuration - TimeSpan.FromMilliseconds(300).Ticks);
        }
    }
    internal long _CurTime, curTime;
    internal void SetCurTime() => Set(ref _CurTime, curTime, true, nameof(CurTime));
    void UpdateCurTime(long ts, bool skipRefreshType = true)
    {
        if (!VideoDemuxer.IsHLSLive)
        {
            lock (seeks)
            {
                if (!seeks.IsEmpty)
                    return;

                curTime = ts;
            }

            if (skipRefreshType
                || Config.Player.UICurTime == UIRefreshType.PerFrame
                ||(Config.Player.UICurTime == UIRefreshType.PerFrameSecond && _CurTime / 1_000_0000 != ts / 1_000_0000))
                UI(SetCurTime);
        }
    }
    void UpdateCurTimeHLS(long ts)
    {
        lock (seeks)
        {
            if (!seeks.IsEmpty)
                return;

            curTime = ts;
        }

        if (   Config.Player.UICurTime == UIRefreshType.PerFrame
            ||(Config.Player.UICurTime == UIRefreshType.PerFrameSecond && _CurTime / 1_000_0000 != ts / 1_000_0000))
            UI(SetCurTime);
    }

    /// <summary>
    /// Input's duration
    /// </summary>
    public long         Duration            { get => duration;          private set => Set(ref _Duration, value); }
    long _Duration, duration;
    void UpdateDurationHLS(long duration)
    {
        this.duration = duration;
        UI(() => Duration = this.duration);
    }

    /// <summary>
    /// Forces Player's and Demuxer's Duration to allow Seek
    /// </summary>
    /// <param name="duration">Duration (Ticks)</param>
    /// <exception cref="ArgumentNullException">Demuxer must be opened before forcing the duration</exception>
    public void ForceDuration(long duration)
    {
        if (MainDemuxer == null)
            throw new ArgumentNullException(nameof(MainDemuxer));

        this.duration = duration;
        MainDemuxer.ForceDuration(duration);
        isLive = MainDemuxer.IsLive;
        UI(() =>
        {
            Duration= this.duration;
            IsLive  = isLive;
        });
    }

    /// <summary>
    /// The current buffered duration in the demuxer
    /// </summary>
    public long         BufferedDuration    { get => MainDemuxer.BufferedDuration; internal set => Set(ref _BufferedDuration, value); }
    long _BufferedDuration;

    /// <summary>
    /// Whether the input is live (duration might not be 0 on live sessions to allow live seek, eg. hls)
    /// </summary>
    public bool         IsLive              { get => MainDemuxer.IsLive;            private set => Set(ref _IsLive, value); }
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
        get => speed;
        set
        {
            double newValue = Math.Round(value, 3);
            if (value < 0.125)
                newValue = 0.125;
            else if (value > 16)
                newValue = 16;

            if (newValue == speed)
                return;

            AudioDecoder.Speed      = newValue;
            VideoDecoder.Speed      = newValue;
            speed                   = newValue;
            decoder.RequiresResync  = true;
            requiresBuffering       = true;
            Subtitles.subsText      = "";
            renderer.ClearOverlayTexture();
            UI(() =>
            {
                Subtitles.SubsText = Subtitles.subsText;
                Raise(nameof(Speed));
            });
        }
    }
    double speed = 1;

    /// <summary>
    /// Pan zoom percentage (100 for 100%)
    /// </summary>
    public int          Zoom
    {
        get => (int)(renderer.Zoom * 100);
        set { renderer.SetZoom(renderer.Zoom = value / 100.0); RaiseUI(nameof(Zoom)); }
        //set { renderer.SetZoomAndCenter(renderer.Zoom = value / 100.0, Renderer.ZoomCenterPoint); RaiseUI(nameof(Zoom)); } // should reset the zoom center point?
    }

    /// <summary>
    /// Pan rotation angle (for D3D11 VP allowed values are 0, 90, 180, 270 only)
    /// </summary>
    public uint Rotation            { get => renderer.Rotation;
        set
        {
            renderer.Rotation = value;
            RaiseUI(nameof(Rotation));
        }
    }

    /// <summary>
    /// Pan Horizontal Flip (FlyleafVP only)
    /// </summary>
    public bool HFlip { get => renderer.HFlip; set => renderer.HFlip = value; }

    /// <summary>
    /// Pan Vertical Flip (FlyleafVP only)
    /// </summary>
    public bool VFlip { get => renderer.VFlip; set => renderer.VFlip = value; }

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

            if (!Video.IsOpened || !canPlay | isLive)
                return;

            lock (lockActions)
            {
                bool shouldPlay = status == Status.Playing || (status == Status.Ended && Config.Player.AutoPlay);
                Pause();
                dFrame = null;
                sFrame = null;
                renderer.ClearOverlayTexture();
                Subtitles.ClearSubsText();
                decoder.StopThreads();
                decoder.Flush();

                if (status == Status.Ended)
                {
                    status = Status.Paused;
                    UI(() => Status = status);
                }

                if (value)
                {
                    Speed = 1;
                    VideoDemuxer.EnableReversePlayback(CurTime);
                }
                else
                {
                    VideoDemuxer.DisableReversePlayback();

                    var vFrame = VideoDecoder.GetFrame(VideoDecoder.GetFrameNumber(CurTime));
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

    public bool         LoopPlayback        { get => _LoopPlayback; set => Set(ref _LoopPlayback, value); }
    bool _LoopPlayback;

    public object       Tag                 { get => tag; set => Set(ref  tag, value); }
    object tag;

    public string       LastError           { get => lastError; set => Set(ref _LastError, value); }
    string _LastError, lastError;
    bool decoderHasEnded => (VideoDecoder.Status == MediaFramework.Status.Ended || (VideoDecoder.Disposed && AudioDecoder.Status == MediaFramework.Status.Ended));
    #endregion

    #region Properties Internal
    readonly object lockActions  = new();
    readonly object lockSubtitles= new();

    bool taskSeekRuns;
    bool taskPlayRuns;
    bool taskOpenAsyncRuns;

    readonly ConcurrentStack<SeekData>   seeks      = new();
    readonly ConcurrentQueue<Action>     UIActions  = new();

    internal AudioFrame     aFrame;
    internal VideoFrame     vFrame;
    internal SubtitlesFrame sFrame, sFramePrev;
    internal DataFrame      dFrame;
    internal PlayerStats    stats = new();
    internal LogHandler     Log;

    internal volatile bool requiresBuffering;
    bool reversePlaybackResync;

    volatile bool isVideoSwitch;
    volatile bool isAudioSwitch;
    volatile bool isSubsSwitch;
    volatile bool isDataSwitch;
    #endregion

    public Player(Config config = null)
    {
        if (config != null)
        {
            if (config.Player.player != null)
                throw new("Player's configuration is already assigned to another player");

            Config = config;
        }
        else
            Config = new Config();

        PlayerId    = GetUniqueId();
        Log         = new(("[#" + PlayerId + "]").PadRight(8, ' ') + " [Player        ] ");
        Log.Debug($"Creating Player (Usage = {Config.Player.Usage})");

        Activity    = new(this);
        Audio       = new(this);
        Video       = new(this);
        Subtitles   = new(this);
        Data        = new(this);
        Commands    = new(this);

        Config.SetPlayer(this);

        if (Config.Player.Usage == Usage.Audio)
        {
            Config.Video.Enabled = false;
            Config.Subtitles.Enabled = false;
        }

        decoder = new(Config, PlayerId) { Tag = this };
        Engine.AddPlayer(this);

        AudioDecoder    = decoder.AudioDecoder;
        VideoDecoder    = decoder.VideoDecoder;
        SubtitlesDecoder= decoder.SubtitlesDecoder;
        DataDecoder     = decoder.DataDecoder;
        AudioDemuxer    = decoder.AudioDemuxer;
        VideoDemuxer    = decoder.VideoDemuxer;
        SubtitlesDemuxer= decoder.SubtitlesDemuxer;
        DataDemuxer     = decoder.DataDemuxer;
        Playlist        = decoder.Playlist;

        // We keep the same instance
        renderer        = VideoDecoder.Renderer;
        vFrames         = VideoDecoder.Frames;
        vPackets        = VideoDemuxer.VideoPackets;

        UpdateMainDemuxer();
        if (renderer != null)
            renderer.forceNotExtractor = true;

        decoder.OpenAudioStreamCompleted               += Decoder_OpenAudioStreamCompleted;
        decoder.OpenVideoStreamCompleted               += Decoder_OpenVideoStreamCompleted;
        decoder.OpenSubtitlesStreamCompleted           += Decoder_OpenSubtitlesStreamCompleted;
        decoder.OpenDataStreamCompleted                += Decoder_OpenDataStreamCompleted;

        decoder.OpenExternalAudioStreamCompleted       += Decoder_OpenExternalAudioStreamCompleted;
        decoder.OpenExternalVideoStreamCompleted       += Decoder_OpenExternalVideoStreamCompleted;
        decoder.OpenExternalSubtitlesStreamCompleted   += Decoder_OpenExternalSubtitlesStreamCompleted;

        AudioDecoder.CodecChanged   = Decoder_AudioCodecChanged;
        VideoDecoder.CodecChanged   = Decoder_VideoCodecChanged;
        VideoDecoder.OpeningCodec   = () => { OnOpeningVideoStream(new() { Player = this, VideoStream = VideoDecoder.VideoStream, VideoAcceleration = VideoDecoder.VideoAccelerated }); };
        decoder.RecordingCompleted += (o, e) => { IsRecording = false; };
        Chapters.CollectionChanged += (o, e) => { RaiseUI(nameof(Chapters)); };

        status = Status.Stopped;
        Reset();
        Log.Debug("Created");
    }

    /// <summary>
    /// Disposes the Player and de-assigns it from FlyleafHost
    /// </summary>
    public void Dispose() => Engine.DisposePlayer(this);
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
                Host?.Player_Disposed();
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
            BitRate     = bitRate;
            Duration    = duration;
            IsLive      = isLive;
            Status      = status;
            CanPlay     = canPlay;
            LastError   = lastError;
            BufferedDuration = 0;
            SetCurTime();
        });
    }
    private void Reset()
    {
        // TODO: Consider partial reset on opening and full reset in case of open failed (otherwise let it overwrite)

        ResetMe();
        Video.Reset();
        Audio.Reset();
        Subtitles.Reset();
        Data.Reset();
        UIAll();
    }
    private void Initialize(Status status = Status.Stopped, bool andDecoder = true, bool isSwitch = false)
    {
        if (CanDebug) Log.Debug($"Initializing");

        lock (lockActions) // Required in case of OpenAsync and Stop requests
        {
            try
            {
                Engine.TimeBeginPeriod1();

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
                Engine.TimeEndPeriod1();
            }
        }
    }

    internal void UIAdd(Action action) => UIActions.Enqueue(action);
    internal void UIAll()
    {
        while (!UIActions.IsEmpty)
            if (UIActions.TryDequeue(out var action))
                UI(action);
    }

    public override bool Equals(object obj)
        => obj == null || !(obj is Player) ? false : ((Player)obj).PlayerId == PlayerId;
    public override int GetHashCode() => PlayerId.GetHashCode();

    // Avoid having this code in OnPaintBackground as it can cause designer issues (renderer will try to load FFmpeg.Autogen assembly because of HDR Data)
    internal bool WFPresent() { if (renderer == null || renderer.SCDisposed) return false; renderer?.RenderRequest(); return true; }
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
    Audio
}
