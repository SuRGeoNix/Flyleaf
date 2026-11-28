using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaDecoder;

public unsafe partial class SubtitlesDecoder : DecoderBase
{
    // Runtime timestamp adjustment in ticks; also used by standalone decoder sessions.
    internal long Delay;
    bool bitmapOutput;

    public SubtitlesStream  SubtitlesStream     => (SubtitlesStream) Stream;

    public ConcurrentQueue<SubtitlesFrame>
                            Frames              { get; protected set; } = [];

    public SubtitlesDecoder(Config config, int uniqueId = -1) : base(config, uniqueId) { }

    protected override unsafe bool Setup()
    {
        AVCodec* codec = string.IsNullOrEmpty(Config.Subtitles._Codec) ? avcodec_find_decoder(Stream.CodecID) : avcodec_find_decoder_by_name(Config.Subtitles._Codec);
        if (codec == null)
        {
            Log.Error($"Codec not found ({(string.IsNullOrEmpty(Config.Subtitles._Codec) ? Stream.CodecID : Config.Subtitles._Codec)})");
            return false;
        }

        if (CanDebug) Log.Debug($"Using {avcodec_get_name_str(codec->id)} codec");

        codecCtx = avcodec_alloc_context3(codec); // Pass codec to use default settings
        if (codecCtx == null)
        {
            Log.Error($"Failed to allocate context");
            return false;
        }

        int ret = avcodec_parameters_to_context(codecCtx, Stream.AVStream->codecpar);
        if (ret < 0)
        {
            Log.Error($"Failed to pass parameters to context - {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
            return false;
        }

        codecCtx->pkt_timebase  = Stream.AVStream->time_base;
        codecCtx->codec_id      = codec->id; // avcodec_parameters_to_context will change this we need to set Stream's Codec Id (eg we change mp2 to mp3)

        var codecOpts = Config.Subtitles.CodecOpt;
        AVDictionary* avopt = null;
        foreach(var optKV in codecOpts)
            _ = av_dict_set(ref avopt, optKV.Key, optKV.Value, 0);

        ret = avcodec_open2(codecCtx, null, avopt == null ? null : &avopt);
        if (ret < 0)
        {
            if (avopt != null) av_dict_free(&avopt);
            Log.Error($"Failed to open codec - {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
            return false;
        }

        if (avopt != null)
        {
            AVDictionaryEntry *t = null;
            while ((t = av_dict_get(avopt, "", t, DictReadFlags.IgnoreSuffix)) != null)
                Log.Debug($"Ignoring codec option {BytePtrToStringUTF8(t->key)}");

            av_dict_free(&avopt);
        }

        OpenSubtitleRenderer();
        return true;
    }

    protected override void DisposeInternal()
    {
        DisposeFrames();
        bitmapOutput = false;
        lock (lockSubtitleRenderer)
            SetSubtitleRenderer(null, null);
    }

    public void Flush()
    {
        lock (lockActions)
        lock (lockCodecCtx)
        {
            if (Disposed) return;

            if (Status == Status.Ended) Status = Status.Stopped;
            //else if (Status == Status.Draining) Status = Status.Stopping;

            DisposeFrames();
            avcodec_flush_buffers(codecCtx);
            ResetSubtitleRenderer();
        }
    }

    protected override void RunInternal()
    {
        int ret = 0;
        int allowedErrors = Config.Subtitles.MaxErrors;

        do
        {
            // Wait until Queue not Full or Stopped
            if (Frames.Count >= MaxQueuedFrames)
            {
                lock (lockStatus)
                    if (Status == Status.Running) Status = Status.QueueFull;

                while (Frames.Count >= MaxQueuedFrames && Status == Status.QueueFull) Thread.Sleep(20);

                lock (lockStatus)
                {
                    if (Status != Status.QueueFull) break;
                    Status = Status.Running;
                }
            }

            // While Packets Queue Empty (Ended | Quit if Demuxer stopped | Wait until we get packets)
            if (demuxer.SubtitlesPackets.Count == 0)
            {
                CriticalArea = true;

                lock (lockStatus)
                    if (Status == Status.Running) Status = Status.QueueEmpty;

                while (demuxer.SubtitlesPackets.Count == 0 && Status == Status.QueueEmpty)
                {
                    if (demuxer.Status == Status.Ended)
                    {
                        Status = Status.Ended;
                        break;
                    }
                    else if (!demuxer.IsRunning)
                    {
                        if (CanDebug) Log.Debug($"Demuxer is not running [Demuxer Status: {demuxer.Status}]");

                        int retries = 5;

                        while (retries > 0)
                        {
                            retries--;
                            Thread.Sleep(10);
                            if (demuxer.IsRunning) break;
                        }

                        lock (demuxer.lockStatus)
                        lock (lockStatus)
                        {
                            if (demuxer.IsRunning)
                                continue;

                            if (demuxer.Status == Status.Pausing || demuxer.Status == Status.Paused)
                                Status = Status.Pausing;
                            else if (demuxer.Status != Status.Ended)
                                Status = Status.Stopping;
                            else
                                continue;
                        }

                        break;
                    }

                    Thread.Sleep(20);
                }

                lock (lockStatus)
                {
                    CriticalArea = false;
                    if (Status != Status.QueueEmpty) break;
                    Status = Status.Running;
                }
            }

            bool submitted;
            lock (lockCodecCtx)
            {
                if (Status == Status.Stopped || demuxer.SubtitlesPackets.Count == 0) continue;
                var packet = demuxer.SubtitlesPackets.Dequeue();
                ret = DecodePacket(packet, queueRendererCue: true, out submitted);
                if (ret < 0 && --allowedErrors == 0)
                {
                    Log.Error("Too many errors!");
                    Status = Status.Stopping;
                    break;
                }
            }
            if (submitted && HasOverlayRenderer)
                RenderCueAvailable?.Invoke();
        } while (Status == Status.Running);
    }

    int DecodePacket(AVPacket* packet, bool queueRendererCue, out bool submitted)
    {
        submitted = false;
        int gotFrame = 0;
        SubtitlesFrame frame = new();
        bool queued = false;
        try
        {
            int ret;
            fixed (AVSubtitle* sub = &frame.sub)
                ret = avcodec_decode_subtitle2(codecCtx, sub, &gotFrame, packet);

            if (ret < 0)
            {
                if (CanWarn) Log.Warn($"{FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
                return ret;
            }
            if (gotFrame == 0)
                return ret;

            long pts = frame.sub.pts != AV_NOPTS_VALUE ? frame.sub.pts * 10 :
                packet->pts != AV_NOPTS_VALUE ? (long)(packet->pts * SubtitlesStream.Timebase) : AV_NOPTS_VALUE;
            if (pts == AV_NOPTS_VALUE)
                return ret;

            long start = pts + frame.sub.start_display_time * 10000L - demuxer.StartTime;
            frame.Timestamp = start + Interlocked.Read(ref Delay);
            frame.duration = frame.sub.end_display_time == uint.MaxValue ? uint.MaxValue :
                frame.sub.end_display_time - Math.Min(frame.sub.start_display_time, frame.sub.end_display_time);

            if (!filledFromCodec)
            {
                filledFromCodec = true;
                SubtitlesStream.Refresh(this);
                CodecChanged?.Invoke(this);
            }

            if (frame.sub.num_rects > 0)
                bitmapOutput = frame.sub.rects[0]->type == AVSubtitleType.Bitmap;

            if (bitmapOutput || (frame.sub.num_rects == 0 && SubtitlesStream.IsBitmap))
            {
                SelectBitmapRenderer();
                frame.BitmapWidth = codecCtx->width;
                frame.BitmapHeight = codecCtx->height;
                if (frame.sub.num_rects == 0)
                    frame.duration = uint.MaxValue;

                Frames.Enqueue(frame);
                queued = true;
                return ret;
            }

            List<string> lines = null;
            int textOffset = 0;
            for (int i = 0; i < frame.sub.num_rects; i++)
            {
                var rect = frame.sub.rects[i];
                bool isAss = rect->type == AVSubtitleType.Ass;
                var data = isAss ? rect->ass : rect->type == AVSubtitleType.Text ? rect->text : null;
                if (data == null)
                    continue;

                string cue = BytePtrToStringUTF8(data);
                if (isAss && AddRendererCue(cue, start, frame.duration))
                {
                    frame.SubmittedToOverlay = submitted = true;
                    continue;
                }

                string text = ParseTextCue(cue, isAss, out var styles);
                if (string.IsNullOrEmpty(text))
                    continue;

                if (styles is { Count: > 0 })
                {
                    frame.subStyles ??= new(styles.Count);
                    foreach (var span in styles)
                    {
                        var style = span;
                        style.from += textOffset;
                        frame.subStyles.Add(style);
                    }
                }

                if (frame.text == null)
                    frame.text = text;
                else
                    (lines ??= [frame.text]).Add(text);
                textOffset += text.Length + 1;
            }

            frame.text = lines != null ? string.Join("\n", lines) : frame.text ?? "";
            DisposeFrame(frame);
            if (frame.text.Length > 0 || (frame.SubmittedToOverlay && queueRendererCue))
            {
                Frames.Enqueue(frame);
                queued = true;
            }

            return ret;
        }
        finally
        {
            av_packet_free(&packet);
            if (!queued)
                DisposeFrame(frame);
        }
    }

    public static void DisposeFrame(SubtitlesFrame frame)
    {
        if (frame.sub.num_rects > 0)
            fixed(AVSubtitle* ptr = &frame.sub)
                avsubtitle_free(ptr);
    }

    public void DisposeFrames()
    {
        while (Frames.TryDequeue(out var frame))
            DisposeFrame(frame);
    }
}
