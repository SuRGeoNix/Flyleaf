using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;
using System.Linq;

namespace FlyleafLib.MediaPlayer;

public class Subtitles : NotifyPropertyChanged
{
    /// <summary>Whether subtitles processing is enabled for this player.</summary>
    public bool Enabled
    {
        get => decoder.subtitlesEnabled;
        set
        {
            if (!Set(ref decoder.subtitlesEnabled, value)) return;
            if (value) Enable(); else Disable();
        }
    }
    internal void SetEnabled(bool enabled) => Set(ref decoder.subtitlesEnabled, enabled, true, nameof(Enabled));
    public void Toggle() => Enabled = !Enabled;

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
    public string   SubsText        { get => subsText;     private set => Set(ref _SubsText,  value); }
    string _SubsText = "", subsText = "";

    readonly List<SubtitlesFrame> activeTextCues = [];
    internal bool HasTextCues { get { lock (activeTextCues) return activeTextCues.Count != 0; } }

    public void ClearSubsText()
    {
        lock (activeTextCues)
        {
            activeTextCues.Clear();
            SetCueText("");
        }
    }

    internal void UpdateTextCues(long timestamp, List<SubtitlesFrame> newCues = null)
    {
        lock (activeTextCues)
        {
            bool expired = false;
            for (int i = activeTextCues.Count - 1; i >= 0; i--)
            {
                var cue = activeTextCues[i];
                if (timestamp < cue.Timestamp + cue.duration * 10000L) continue;
                activeTextCues.RemoveAt(i);
                expired = true;
            }
            bool started = newCues is { Count: > 0 };
            if (started)
                activeTextCues.AddRange(newCues);
            if (expired || started)
                SetCueText(activeTextCues.Count switch
                {
                    0 => "",
                    1 => activeTextCues[0].text,
                    _ => string.Join("\n", activeTextCues.Select(frame => frame.text))
                });
        }
    }

    void SetCueText(string text)
    {
        if (subsText == text) return;
        subsText = text;
        UI(() => SubsText = subsText);
    }

    /// <summary>
    /// Subtitle delay in ticks; reset to zero when opening a subtitle stream.
    /// </summary>
    public long Delay
    {
        get => player.SubtitlesDecoder.Delay;
        set
        {
            if (!IsOpened) return;
            if (Set(ref player.SubtitlesDecoder.Delay, value))
                player.ReSync(player.decoder.SubtitlesStream);
        }
    }
    internal void SetDelay(long delay) => Set(ref player.SubtitlesDecoder.Delay, delay, true, nameof(Delay));

    public void DelayAdd()      => Delay += player.Config.Subtitles.DelayOffset;
    public void DelayRemove()   => Delay -= player.Config.Subtitles.DelayOffset;
    public void DelayAdd2()     => Delay += player.Config.Subtitles.DelayOffset2;
    public void DelayRemove2()  => Delay -= player.Config.Subtitles.DelayOffset2;

    /// <summary>The renderer selected for the open subtitle track.</summary>
    public SubtitlesRendererInfo SubsRenderer => player.SubtitlesDecoder?.RendererInfo;
    internal void NotifyRendererChanged() => RaiseUI(nameof(SubsRenderer));

    SubtitlesRenderStyle renderStyle = SubtitlesRenderStyle.Default;
    /// <summary>Default appearance for SubRip rendered by a subtitle renderer plugin.</summary>
    public SubtitlesRenderStyle RenderStyle
    {
        get => Volatile.Read(ref renderStyle);
        set
        {
            var style = value ?? SubtitlesRenderStyle.Default;
            if (Interlocked.Exchange(ref renderStyle, style) != style && player.SubtitlesDecoder.HasOverlayRenderer)
                player.Renderer?.RenderRequest();
        }
    }

    public Player Player => player;

    Action uiAction;
    Player player;
    DecoderContext decoder => player?.decoder;

    public Subtitles(Player player)
    {
        this.player = player;

        uiAction = () =>
        {
            StreamIndex = streamIndex;
            IsOpened    = isOpened;
            Codec       = codec;
            SubsText    = subsText;
        };
    }
    internal void Reset()
    {
        streamIndex = -1;
        codec       = null;
        isOpened    = false;
        ClearSubsText();
        player.sFramePrev = null;
        player.Renderer?.SubsDispose();

        player.UIAdd(uiAction);
    }
    internal void Refresh()
    {
        if (decoder.SubtitlesStream == null) { Reset(); return; }

        streamIndex = decoder.SubtitlesStream.StreamIndex;
        codec       = decoder.SubtitlesStream.Codec;
        isOpened    =!decoder.SubtitlesDecoder.Disposed;
        ClearSubsText();
        player.sFramePrev = null;
        player.Renderer.SubsDispose();

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
}
