using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaPlayer;

public class Video : NotifyPropertyChanged
{
    // TODO: Consider replacing with VideoStream and everyone (player/renderer) will update values there* (Update UI or event by groups -fill demux, fill codec/frame-)
    // We consider this as read only? (we have also read/write Video properties on Player that point to renderer)

    /// <summary>
    /// Embedded Streams
    /// </summary>
    public ObservableCollection<VideoStream>
                        Streams         => decoder?.VideoDemuxer.VideoStreams;

    public int          StreamIndex     { get => streamIndex;       internal set => Set(ref _StreamIndex, value); }
    int _StreamIndex, streamIndex = -1;

    /// <summary>
    /// Whether the input has video and it is configured
    /// </summary>
    public bool         IsOpened        { get => isOpened;          internal set => Set(ref _IsOpened, value); }
    internal bool   _IsOpened, isOpened;

    public string       Codec           { get => codec;             internal set => Set(ref _Codec, value); }
    internal string _Codec, codec;

    /// <summary>
    /// Video bitrate (Kbps)
    /// </summary>
    public double       BitRate         { get => bitRate;           internal set => Set(ref _BitRate, value); }
    internal double _BitRate, bitRate;

    /// <summary>
    /// Total Dropped Frames
    /// </summary>
    public int          FramesDropped   { get => framesDropped;     internal set => Set(ref _FramesDropped, value); }
    internal int    _FramesDropped, framesDropped;

    /// <summary>
    /// Total Frames
    /// </summary>
    public long         FramesTotal     { get => framesTotal;       internal set => Set(ref _FramesTotal, value); }
    internal long   _FramesTotal, framesTotal;

    public int          FramesDisplayed { get => framesDisplayed;   internal set => Set(ref _FramesDisplayed, value); }
    internal int    _FramesDisplayed, framesDisplayed;

    public double       FPS             { get => fps;               internal set => Set(ref _FPS, value); }
    internal double _FPS, fps;

    /// <summary>
    /// Actual Frames rendered per second (FPS)
    /// </summary>
    public double       FPSCurrent      { get => fpsCurrent;        internal set => Set(ref _FPSCurrent, value); }
    internal double _FPSCurrent, fpsCurrent;

    public string       PixelFormat     { get => pixelFormat;       internal set => Set(ref _PixelFormat, value); }
    internal string _PixelFormat, pixelFormat;

    public bool         VideoAcceleration
                                        { get => videoAcceleration; internal set => Set(ref _VideoAcceleration, value); }
    internal bool   _VideoAcceleration, videoAcceleration;

    public HDRFormat    HDRFormat       { get => hdrFormat;         internal set => Set(ref _HDRFormat, value); }
    internal HDRFormat _HDRFormat, hdrFormat;

    public string       ColorFormat     { get => colorFormat;       internal set => Set(ref _ColorFormat, value); }
    string          _ColorFormat, colorFormat;

    public int          Width           { get; internal set; }
    public int          Height          { get; internal set; }
    public AspectRatio  AspectRatio     { get; internal set; }
    //public int          Rotation        { get; internal set; } 

    public Player Player => player;

    Action uiAction;
    Player player;
    DecoderContext decoder => player.decoder;
    Config Config => player.Config;

    public Video(Player player)
    {
        this.player = player;

        uiAction = () =>
        {
            StreamIndex         = streamIndex;
            IsOpened            = IsOpened;
            Codec               = Codec;
            FramesTotal         = FramesTotal;
            FPS                 = FPS;
            PixelFormat         = PixelFormat;
            VideoAcceleration   = VideoAcceleration;
            HDRFormat           = HDRFormat;
            ColorFormat         = ColorFormat;

            FramesDisplayed     = FramesDisplayed;
            FramesDropped       = FramesDropped;
        };
    }

    internal void Reset()
    {
        streamIndex         = -1;
        codec               = null;
        bitRate             = 0;
        fps                 = 0;
        pixelFormat         = null;
        framesTotal         = 0;
        videoAcceleration   = false;
        isOpened            = false;
        hdrFormat           = HDRFormat.None;
        colorFormat         = "";

        player.UIAdd(uiAction);
        SetUISize(0, 0, new(0, 0));//, 0);
    }
    internal void Refresh()
    {
        /* TBR
         * We call this at least twice (OnOpen and OnCodecChanged)
         * To avoid keeping this updated twice+ should fix media streams (maybe fill once after analysed)
         * Should also clarify AVStream values vs Codec/Renderer's that we need to keep here
         */
        if (decoder.VideoStream == null) { Reset(); return; }

        streamIndex = decoder.VideoStream.StreamIndex;
        codec       = decoder.VideoStream.Codec;
        fps         = decoder.VideoStream.FPS;
        pixelFormat = decoder.VideoStream.PixelFormatStr;
        framesTotal = decoder.VideoStream.TotalFrames;
        videoAcceleration
                    = decoder.VideoDecoder.VideoAccelerated;
        hdrFormat   = decoder.VideoStream.HDRFormat;
        colorFormat = $"{decoder.VideoStream.ColorSpace}\r\n{decoder.VideoStream.ColorTransfer}\r\n{decoder.VideoStream.ColorRange}";
        isOpened    =!decoder.VideoDecoder.Disposed;

        framesDisplayed = 0;
        framesDropped   = 0;

        player.UIAdd(uiAction);
    }

    internal void SetUISize(int width, int height, AspectRatio aspectRatio)//, int rotation)
    {
        UI(() =>
        {
            if (Width != width)
            {
                Width = width;
                Raise(nameof(Width));
            }

            if (Height != height)
            {
                Height = height;
                Raise(nameof(Height));
            }

            //if (Rotation != rotation)
            //{
            //    Rotation = rotation;
            //    Raise(nameof(Rotation));
            //}

            if (AspectRatio != aspectRatio)
            {
                AspectRatio = aspectRatio;
                Raise(nameof(AspectRatio));
            }
        });
    }

    internal void Enable()
    {
        if (player.VideoDemuxer.Disposed || Config.Player.Usage == Usage.Audio)
            return;

        bool wasPlaying = player.IsPlaying;

        player.Pause();
        decoder.OpenSuggestedVideo();
        player.ReSync(decoder.VideoStream, (int) (player.CurTime / 10000), true);

        if (wasPlaying || Config.Player.AutoPlay)
            player.Play();
    }
    internal void Disable()
    {
        if (!IsOpened)
            return;

        bool wasPlaying = player.IsPlaying;

        player.Pause();
        decoder.CloseVideo();
        player.renderer.ClearOverlayTexture();
        player.Subtitles.subsText = "";
        player.UIAdd(() => player.Subtitles.SubsText = player.Subtitles.SubsText);

        if (!player.Audio.IsOpened)
        {
            player.canPlay = false;
            player.UIAdd(() => player.CanPlay = player.CanPlay);
        }

        Reset();
        player.UIAll();

        if (wasPlaying || Config.Player.AutoPlay)
            player.Play();
    }

    public void Toggle() => Config.Video.Enabled = !Config.Video.Enabled;
    public void ToggleKeepRatio()
    {
        if (Config.Video.AspectRatio == AspectRatio.Keep)
            Config.Video.AspectRatio = AspectRatio.Fill;
        else if (Config.Video.AspectRatio == AspectRatio.Fill)
            Config.Video.AspectRatio = AspectRatio.Keep;
    }
    public void ToggleVideoAcceleration() => Config.Video.VideoAcceleration = !Config.Video.VideoAcceleration;
}
