using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaPlayer;

public class Data : NotifyPropertyChanged
{
    /// <summary>Whether data processing is enabled for this player.</summary>
    public bool Enabled
    {
        get => decoder.dataEnabled;
        set
        {
            if (!Set(ref decoder.dataEnabled, value)) return;
            if (value) Enable(); else Disable();
        }
    }
    internal void SetEnabled(bool enabled) => Set(ref decoder.dataEnabled, enabled, true, nameof(Enabled));
    public void Toggle() => Enabled = !Enabled;

    /// <summary>
    /// Embedded Streams
    /// </summary>
    public ObservableCollection<DataStream>
                        Streams => decoder?.DataDemuxer.DataStreams;

    /// <summary>
    /// Whether the input has data and it is configured
    /// </summary>
    public bool IsOpened { get => isOpened; internal set => Set(ref _IsOpened, value); }
    internal bool   _IsOpened, isOpened;

    Action uiAction;
    Player player;
    DecoderContext decoder => player.decoder;

    public Data(Player player)
    {
        this.player = player;
        uiAction = () =>
        {
            IsOpened = IsOpened;
        };
    }
    internal void Reset()
    {
        isOpened = false;

        player.UIAdd(uiAction);
    }
    internal void Refresh()
    {
        if (decoder.DataStream == null)
        { Reset(); return; }

        isOpened = !decoder.DataDecoder.Disposed;

        player.UIAdd(uiAction);
    }
    internal void Enable()
    {
        if (!player.CanPlay)
            return;

        decoder.OpenSuggestedData();
        player.ReSync(decoder.DataStream, (int)(player.curTime / 10000), true);

        Refresh();
        player.UIAll();
    }
    internal void Disable()
    {
        if (!IsOpened)
            return;

        decoder.CloseData();

        player.dFrame = null;
        Reset();
        player.UIAll();
    }
}
