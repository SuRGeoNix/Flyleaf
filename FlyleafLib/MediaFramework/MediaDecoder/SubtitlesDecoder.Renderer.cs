using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaFramework.MediaDecoder;

public unsafe partial class SubtitlesDecoder
{
    internal PluginHandler SubtitlePlugins { get; set; }
    internal Action RenderCueAvailable { get; set; }
    internal Action RendererChanged { get; set; }
    internal bool HasOverlayRenderer => Volatile.Read(ref subtitleRenderer) is { } session && session is not ISubtitlesTextRenderSession;
    internal SubtitlesRendererInfo RendererInfo => Volatile.Read(ref rendererInfo);

    // Preload nearby events so libass can lay out simultaneous cues, including while paused.
    int MaxQueuedFrames => HasOverlayRenderer ? Math.Max(Config.Subtitles.MaxFrames, 32) : Config.Subtitles.MaxFrames;

    readonly object lockSubtitleRenderer = new();
    ISubtitlesRenderSession subtitleRenderer;
    SubtitlesRendererInfo rendererInfo;
    static readonly SubtitlesRendererInfo bitmapRenderer = new("Bitmap", SubtitlesRenderMode.Bitmap);

    void OpenSubtitleRenderer()
    {
        lock (lockSubtitleRenderer)
        {
            if (SubtitlesStream.IsBitmap)
            {
                SetSubtitleRenderer(null, bitmapRenderer);
                return;
            }

            if (SubtitlePlugins == null)
            {
                UseBasicText();
                return;
            }

            var (provider, session, reason) = SubtitlePlugins.OpenSubtitlesRenderer(CreateRenderTrack);
            if (provider is null or SubsRendererDefault)
            {
                SetSubtitleRenderer(session, new(SubsRendererDefault.DisplayName, SubtitlesRenderMode.Text, FallbackReason: reason));
                return;
            }

            var mode = session is ISubtitlesTextRenderSession ? SubtitlesRenderMode.Text : SubtitlesRenderMode.Overlay;
            SetSubtitleRenderer(session, new(provider.Name, mode, provider.Version));
        }
    }

    SubtitlesRenderTrack CreateRenderTrack()
    {
        byte[] header = codecCtx->subtitle_header_size > 0
            ? new ReadOnlySpan<byte>(codecCtx->subtitle_header, codecCtx->subtitle_header_size).ToArray() : [];
        return new(avcodec_get_name_str(Stream.CodecID), header, GetAttachedFonts());
    }

    void SelectBitmapRenderer()
    {
        if (RendererInfo == bitmapRenderer)
            return;

        lock (lockSubtitleRenderer)
            SetSubtitleRenderer(null, bitmapRenderer);
    }

    void UseBasicText(string reason = null) => SetSubtitleRenderer(SubsRendererDefault.Session,
        new(SubsRendererDefault.DisplayName, SubtitlesRenderMode.Text, FallbackReason: reason));

    void UseBasicTextAfterFailure(string operation, Exception error)
    {
        string reason = $"{RendererInfo?.Name} {operation}: {error.Message}";
        Log.Warn($"Subtitle renderer failed ({reason})");
        UseBasicText(reason);
    }

    List<SubtitlesFont> GetAttachedFonts()
    {
        List<SubtitlesFont> fonts = [];
        var format = demuxer.FormatContext;
        for (int i = 0; i < format->nb_streams; i++)
        {
            var stream = format->streams[i];
            var parameters = stream->codecpar;
            if (parameters->codec_type != AVMediaType.Attachment || parameters->extradata_size <= 0)
                continue;

            var filename = av_dict_get(stream->metadata, "filename", null, 0);
            if (filename == null)
                continue;

            string name = BytePtrToStringUTF8(filename->value);
            if (avcodec_get_name_str(parameters->codec_id) is "ttf" or "otf" ||
                Path.GetExtension(name).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc" or ".otc")
                fonts.Add(new(name, new ReadOnlySpan<byte>(parameters->extradata, parameters->extradata_size).ToArray()));
        }
        return fonts;
    }

    bool AddRendererCue(string cue, long timestamp, uint duration)
    {
        if (!HasOverlayRenderer)
            return false;

        lock (lockSubtitleRenderer)
        {
            if (subtitleRenderer is null or ISubtitlesTextRenderSession)
                return false;

            try
            {
                subtitleRenderer.AddCue(cue, timestamp / TimeSpan.TicksPerMillisecond, duration);
                return true;
            }
            catch (Exception e)
            {
                UseBasicTextAfterFailure("could not add a cue", e);
                return false;
            }
        }
    }

    string ParseTextCue(string text, bool isAss, out List<SubStyle> styles)
    {
        lock (lockSubtitleRenderer)
        {
            if (RendererInfo?.Mode == SubtitlesRenderMode.Bitmap)
                UseBasicText();

            var session = subtitleRenderer as ISubtitlesTextRenderSession ?? SubsRendererDefault.Session;
            try
            {
                return session.ParseCue(text, isAss, out styles);
            }
            catch (Exception e) when (session != SubsRendererDefault.Session)
            {
                UseBasicTextAfterFailure("could not parse a cue", e);
                return SubsRendererDefault.Session.ParseCue(text, isAss, out styles);
            }
        }
    }

    internal bool TryDecodeForRenderer(AVPacket* packet)
    {
        if (!HasOverlayRenderer)
            return false;

        lock (lockCodecCtx)
        {
            if (Disposed || !HasOverlayRenderer)
                return false;

            DecodePacket(packet, queueRendererCue: false, out _);
            return true;
        }
    }

    internal IReadOnlyList<SubtitlesOverlayRect> RenderSubtitles(long ticks, in SubtitlesRenderRequest request)
    {
        if (!HasOverlayRenderer)
            return null;

        lock (lockSubtitleRenderer)
        {
            try
            {
                return subtitleRenderer is { } session && session is not ISubtitlesTextRenderSession
                    ? session.Render(request with { Timestamp = (ticks - Interlocked.Read(ref Delay)) / TimeSpan.TicksPerMillisecond }) : null;
            }
            catch (Exception e)
            {
                UseBasicTextAfterFailure("could not render an overlay", e);
                return null;
            }
        }
    }

    void ResetSubtitleRenderer()
    {
        lock (lockSubtitleRenderer)
        {
            try { subtitleRenderer?.Seek(); }
            catch (Exception e)
            {
                UseBasicTextAfterFailure("could not reset after seeking", e);
            }
        }
    }

    void SetSubtitleRenderer(ISubtitlesRenderSession replacement, SubtitlesRendererInfo info)
    {
        var session = subtitleRenderer;
        Volatile.Write(ref subtitleRenderer, replacement);
        if (Interlocked.Exchange(ref rendererInfo, info) != info)
            RendererChanged?.Invoke();

        if (session != replacement)
        {
            try { session?.Dispose(); }
            catch (Exception e) { Log.Warn($"Closing subtitle renderer failed ({e.Message})"); }
        }
    }
}
