using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaPlayer;

public class Subtitles : NotifyPropertyChanged
{
    /// <summary>
    /// Embedded Streams
    /// </summary>
    public ObservableCollection<SubtitlesStream>
                    Streams         => decoder?.VideoDemuxer.SubtitlesStreams;

    public int      StreamIndex     { get => streamIndex;       internal set => Set(ref _StreamIndex, value); }
    int _StreamIndex, streamIndex = -1;

    /// <summary>
    /// Whether the input has subtitles and it is configured
    /// </summary>
    public bool     IsOpened        { get => isOpened;     internal set => Set(ref _IsOpened, value); }
    internal bool   _IsOpened, isOpened;

    public string   Codec           { get => codec;        internal set => Set(ref _Codec, value); }
    internal string _Codec, codec;

    /// <summary>
    /// Subtitles Text (updates dynamically while playing based on the duration that it should be displayed)
    /// </summary>
    public string   SubsText        { get => subsText;     internal set => Set(ref _SubsText,  value); }
    internal string _SubsText = "", subsText = "";

    public Player Player => player;

    Action uiAction;
    Player player;
    DecoderContext decoder => player?.decoder;
    Config Config => player.Config;

    public Subtitles(Player player)
    {
        this.player = player;

        uiAction = () =>
        {
            StreamIndex = streamIndex;
            IsOpened    = IsOpened;
            Codec       = Codec;
            SubsText    = SubsText;
        };
    }
    internal void Reset()
    {
        streamIndex = -1;
        codec       = null;
        isOpened    = false;
        subsText    = "";
        player.sFramePrev = null;
        player.renderer?.ClearOverlayTexture();

        player.UIAdd(uiAction);
    }
    internal void Refresh()
    {
        if (decoder.SubtitlesStream == null) { Reset(); return; }

        streamIndex = decoder.SubtitlesStream.StreamIndex;
        codec       = decoder.SubtitlesStream.Codec;
        isOpened    =!decoder.SubtitlesDecoder.Disposed;
        subsText    = "";
        player.sFramePrev = null;
        player.renderer?.ClearOverlayTexture();

        player.UIAdd(uiAction);
    }
    internal void Enable()
    {
        if (!player.CanPlay)
            return;

        decoder.OpenSuggestedSubtitles();
        player.ReSync(decoder.SubtitlesStream, (int) (player.CurTime / 10000), true);

        Refresh();
        player.UIAll();
    }
    internal void Disable()
    {
        if (!IsOpened)
            return;

        decoder.CloseSubtitles();
        Reset();
        player.UIAll();
    }

    public void DelayRemove()   => Config.Subtitles.Delay -= Config.Player.SubtitlesDelayOffset;
    public void DelayAdd()      => Config.Subtitles.Delay += Config.Player.SubtitlesDelayOffset;
    public void DelayRemove2()  => Config.Subtitles.Delay -= Config.Player.SubtitlesDelayOffset2;
    public void DelayAdd2()     => Config.Subtitles.Delay += Config.Player.SubtitlesDelayOffset2;
    public void Toggle()        => Config.Subtitles.Enabled = !Config.Subtitles.Enabled;
}
