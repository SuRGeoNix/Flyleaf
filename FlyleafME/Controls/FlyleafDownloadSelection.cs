using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaStream;

namespace Flyleaf.Controls.WPF;

// Store descriptors, not expiring CDN URLs or native stream objects, while a job is queued.
internal sealed record FlyleafDownloadSelection(string ItemTitle, int ItemIndex, FlyleafExternalChoice? Video, FlyleafExternalChoice? Audio,
    FlyleafStreamChoice[] MainStreams, FlyleafStreamChoice[] AudioStreams)
{
    internal void Apply(Downloader d)
    {
        var ctx = d.DecCtx;
        var item = ctx.Playlist.Selected ?? throw new InvalidOperationException("No media item was resolved.");
        if (item.Index != ItemIndex || item.Title != ItemTitle)
            throw new InvalidOperationException("The resolved playlist item changed. Select the download streams again.");
        if (Video != null && MainStreams.Length != 0)
        {
            var selected = Video.Resolve(item.ExternalVideoStreams);
            if (!ReferenceEquals(selected, item.ExternalVideoStream) || ctx.VideoDemuxer.Disposed)
                Check(ctx.Open(selected, false).Error);
        }
        else if (MainStreams.Length != 0 && item.ExternalVideoStream != null)
            throw new InvalidOperationException("The available video input changed. Select the download streams again.");
        if (Audio != null && AudioStreams.Length != 0)
        {
            var selected = Audio.Resolve(item.ExternalAudioStreams);
            if (!ReferenceEquals(selected, item.ExternalAudioStream) || ctx.AudioDemuxer.Disposed)
                Check(ctx.Open(selected).Error);
        }
        else if (AudioStreams.Length != 0 && item.ExternalAudioStream != null)
            throw new InvalidOperationException("The available audio input changed. Select the download streams again.");
        // Validate both sets before changing either demuxer.
        var main = Resolve(ctx.VideoDemuxer, MainStreams);
        var audio = Resolve(ctx.AudioDemuxer, AudioStreams);
        Configure(ctx.VideoDemuxer, main); Configure(ctx.AudioDemuxer, audio);
    }

    internal static void ApplyAudioOnly(Downloader d)
    {
        var ctx = d.DecCtx;
        var main = ctx.VideoDemuxer.AudioStreams.Where(s => s.Enabled).Cast<StreamBase>().ToArray();
        var audio = ctx.AudioDemuxer.AudioStreams.Where(s => s.Enabled).Cast<StreamBase>().ToArray();
        if (main.Length == 0 && audio.Length == 0) throw new InvalidOperationException("No audio stream is available.");
        Configure(ctx.VideoDemuxer, main); Configure(ctx.AudioDemuxer, audio);
    }

    internal static void ApplyVideoOnly(Downloader d)
    {
        var ctx = d.DecCtx;
        var video = ctx.VideoDemuxer.VideoStreams.Where(s => s.Enabled).Cast<StreamBase>().ToArray();
        if (video.Length == 0) throw new InvalidOperationException("No video stream is available.");
        Configure(ctx.VideoDemuxer, video); Configure(ctx.AudioDemuxer, []);
    }

    static StreamBase[] Resolve(Demuxer demuxer, FlyleafStreamChoice[] choices)
    {
        if (choices.Length == 0) return [];
        if (demuxer.Disposed) throw new InvalidOperationException("The selected stream input is no longer available.");
        return choices.Select(c => demuxer.AVStreamToStream.TryGetValue(c.Index, out var s) && c == FlyleafStreamChoice.From(s) ? s :
            throw new InvalidOperationException($"Stream #{c.Index} changed. Select the download streams again.")).ToArray();
    }
    static void Configure(Demuxer demuxer, StreamBase[] selected)
    {
        if (demuxer.Disposed) return;
        if (selected.Length == 0) { demuxer.Dispose(); return; }
        // Disable first: DisableStream resets the demuxer's primary stream pointer.
        foreach (int index in demuxer.EnabledStreams.ToArray()) demuxer.DisableStream(demuxer.AVStreamToStream[index]);
        foreach (var stream in selected) demuxer.EnableStream(stream);
    }
    internal static void Check(string? error) { if (error != null) throw new InvalidOperationException(error); }
}

internal sealed record FlyleafExternalChoice(int Index, string Signature)
{
    internal static FlyleafExternalChoice From(ExternalStream s) => new(s.Index, Describe(s));
    internal static string Describe(ExternalStream s) => s switch
    {
        ExternalVideoStream v => $"{v.Width}×{v.Height} · {v.FPS:0.##} FPS · {v.Codec} · {v.BitRate} Kbps · {v.Protocol}",
        ExternalAudioStream a => $"{a.Codec} · {a.BitRate} Kbps · {a.SampleRate} Hz · {a.ChannelLayout} · {a.Language} · {a.Protocol}",
        _ => s.Codec ?? ""
    };
    internal T Resolve<T>(IEnumerable<T> streams) where T : ExternalStream
    {
        var match = streams.FirstOrDefault(s => s.Index == Index && Describe(s) == Signature);
        return match ?? throw new InvalidOperationException("The selected external input changed. Select the download streams again.");
    }
}

internal sealed record FlyleafStreamChoice(int Index, string Signature)
{
    internal static FlyleafStreamChoice From(StreamBase s) => new(s.StreamIndex, Describe(s));
    internal static string Describe(StreamBase s) => s switch
    {
        VideoStream v => $"#{v.StreamIndex} · {v.Width}×{v.Height} · {v.FPS:0.##} FPS · {v.Codec}",
        AudioStream a => $"#{a.StreamIndex} · {a.Codec} · {a.ChannelLayoutStr} · {a.SampleRate} Hz · {a.Language} · {a.Title}",
        _ => $"#{s.StreamIndex} · {s.Codec}"
    };
}
