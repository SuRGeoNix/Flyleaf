using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaPlayer;

public class Video : NotifyPropertyChanged
{
    /// <summary>
    /// Embedded Streams
    /// </summary>
    public ObservableCollection<VideoStream>
                        Streams         => decoder?.VideoDemuxer.VideoStreams;

    public int      StreamIndex     { get => streamIndex;       internal set => Set(ref _StreamIndex, value); }
    int _StreamIndex, streamIndex = -1;

    /// <summary>
    /// Whether the input has video and it is configured
    /// </summary>
    public bool         IsOpened        { get => isOpened;          internal set => Set(ref _IsOpened, value); }
    internal bool   _IsOpened, isOpened;

    public string       Codec           { get => codec;             internal set => Set(ref _Codec, value); }
    internal string _Codec, codec;

    ///// <summary>
    ///// Video bitrate (Kbps)
    ///// </summary>
    public double       BitRate         { get => bitRate;           internal set => Set(ref _BitRate, value); }
    internal double _BitRate, bitRate;

    public AspectRatio  AspectRatio     { get => aspectRatio;       internal set => Set(ref _AspectRatio, value, false); } // false: Updates FlyleafHost's ratio from renderer
    internal AspectRatio
                    _AspectRatio, aspectRatio;

    ///// <summary>
    ///// Total Dropped Frames
    ///// </summary>
    public int          FramesDropped   { get => framesDropped;     internal set => Set(ref _FramesDropped, value); }
    internal int    _FramesDropped, framesDropped;

    /// <summary>
    /// Total Frames
    /// </summary>
    public int          FramesTotal     { get => framesTotal;       internal set => Set(ref _FramesTotal, value); }
    internal int    _FramesTotal, framesTotal;

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

    public int          Width           { get => width;             internal set => Set(ref _Width, value); }
    internal int    _Width, width;

    public int          Height          { get => height;            internal set => Set(ref _Height, value); }
    internal int    _Height, height;

    public bool         VideoAcceleration
                                        { get => videoAcceleration; internal set => Set(ref _VideoAcceleration, value); }
    internal bool   _VideoAcceleration, videoAcceleration;

    public HDRFormat    HDRFormat       { get => hdrFormat;         internal set => Set(ref _HDRFormat, value); }
    internal HDRFormat _HDRFormat, hdrFormat;

    public string       ColorFormat     { get => colorFormat;       internal set => Set(ref _ColorFormat, value); }
    string          _ColorFormat, colorFormat;

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
            AspectRatio         = AspectRatio;
            FramesTotal         = FramesTotal;
            FPS                 = FPS;
            PixelFormat         = PixelFormat;
            Width               = Width;
            Height              = Height;
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
        aspectRatio         = new AspectRatio(0, 0);
        bitRate             = 0;
        fps                 = 0;
        pixelFormat         = null;
        width               = 0;
        height              = 0;
        framesTotal         = 0;
        videoAcceleration   = false;
        isOpened            = false;
        hdrFormat           = HDRFormat.None;
        colorFormat         = "";

        player.UIAdd(uiAction);
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

        var renderer = player.renderer;
        if (renderer != null && renderer.DAR.Value > 0)
        {
            aspectRatio = renderer.DAR;
            width       = (int)renderer.VisibleWidth;
            height      = (int)renderer.VisibleHeight;
        }
        else
        {
            width       = (int)decoder.VideoStream.Width;
            height      = (int)decoder.VideoStream.Height;
            aspectRatio = decoder.VideoStream.GetDAR();
        }

        player.UIAdd(uiAction);
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
