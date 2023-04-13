using System;
using System.Collections.Concurrent;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRemuxer;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaDecoder;

public unsafe partial class AudioDecoder : DecoderBase
{
    public AudioStream      AudioStream         => (AudioStream) Stream;
    public readonly 
            VideoDecoder    VideoDecoder;
    public ConcurrentQueue<AudioFrame>
                            Frames              { get; protected set; } = new();

    static AVSampleFormat   AOutSampleFormat    = AVSampleFormat.AV_SAMPLE_FMT_S16;
    static string           AOutSampleFormatStr = av_get_sample_fmt_name(AOutSampleFormat);
    static AVChannelLayout  AOutChannelLayout   = new() { order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE, nb_channels = 2, u = new AVChannelLayout_u() { mask = AV_CH_FRONT_LEFT | AV_CH_FRONT_RIGHT} };
    static int              AOutChannels        = AOutChannelLayout.nb_channels;
    static int              ASampleBytes        = av_get_bytes_per_sample(AOutSampleFormat) * AOutChannels;
    byte[]                  cBuf;
    int                     cBufPos;
    int                     cBufSamples;
    internal bool           resyncWithVideoRequired;
    SwrContext*             swrCtx;

    public AudioDecoder(Config config, int uniqueId = -1, VideoDecoder syncDecoder = null) : base(config, uniqueId) => VideoDecoder = syncDecoder;

    protected override int Setup(AVCodec* codec) => 0;
    private int SetupSwr()
    {
        int ret;

        if (swrCtx == null)
            swrCtx = swr_alloc();

        av_opt_set_chlayout(swrCtx,     "in_chlayout",          &codecCtx->ch_layout,   0);
        av_opt_set_int(swrCtx,          "in_sample_rate",       codecCtx->sample_rate,  0);
        av_opt_set_sample_fmt(swrCtx,   "in_sample_fmt",        codecCtx->sample_fmt,   0);

        fixed(AVChannelLayout* ptr = &AOutChannelLayout)
        av_opt_set_chlayout(swrCtx,     "out_chlayout",         ptr, 0);
        av_opt_set_int(swrCtx,          "out_sample_rate",      codecCtx->sample_rate,  0);
        av_opt_set_sample_fmt(swrCtx,   "out_sample_fmt",       AOutSampleFormat,       0);

        ret = swr_init(swrCtx);
        if (ret < 0)
            Log.Error($"Swr setup failed {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

        return ret;
    }
    private void DisposeSwr()
    {
        if (swrCtx == null)
            return;

        swr_close(swrCtx);

        fixed(SwrContext** ptr = &swrCtx)
            swr_free(ptr);

        swrCtx = null;
    }

    protected override void DisposeInternal()
    {
        DisposeFrames();
        DisposeSwr();
        DisposeFilters();

        cBuf            = null;
        cBufSamples     = 0;
        filledFromCodec = false;
    }
    public void DisposeFrames() => Frames = new();
    public void Flush()
    {
        lock (lockActions)
            lock (lockCodecCtx)
            {
                if (Disposed) return;

                if (Status == Status.Ended)
                    Status = Status.Stopped;

                DisposeFrames();
                avcodec_flush_buffers(codecCtx);

                //av_buffersrc_close(abufferCtx, AV_NOPTS_VALUE, 0);
                //if (av_buffersrc_add_frame(abufferCtx, null) < 0) { Status = Status.Stopping; return; }
                SetupFilters();

                // Ensure no frames left in bufferSink //av_buffersrc_add_frame(abufferCtx, null); //while (av_buffersink_get_frame(abufferSinkCtx, frame) >= 0) ;
                resyncWithVideoRequired = !VideoDecoder.Disposed;
                curSpeedFrame = (int)speed;
            }
    }

    protected override void RunInternal()
    {
        int ret = 0;
        int allowedErrors = Config.Decoder.MaxErrors;
        AVPacket *packet;
        
        do
        {
            // Wait until Queue not Full or Stopped
            if (Frames.Count >= Config.Decoder.MaxAudioFrames)
            {
                lock (lockStatus)
                    if (Status == Status.Running) Status = Status.QueueFull;

                while (Frames.Count >= Config.Decoder.MaxAudioFrames && Status == Status.QueueFull) Thread.Sleep(20);

                lock (lockStatus)
                {
                    if (Status != Status.QueueFull) break;
                    Status = Status.Running;
                }       
            }

            // While Packets Queue Empty (Ended | Quit if Demuxer stopped | Wait until we get packets)
            if (demuxer.AudioPackets.Count == 0)
            {
                CriticalArea = true;

                lock (lockStatus)
                    if (Status == Status.Running) Status = Status.QueueEmpty;

                while (demuxer.AudioPackets.Count == 0 && Status == Status.QueueEmpty)
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

            lock (lockCodecCtx)
            {
                if (Status == Status.Stopped || demuxer.AudioPackets.Count == 0) continue;
                packet = demuxer.AudioPackets.Dequeue();

                if (isRecording)
                {
                    if (!recGotKeyframe && VideoDecoder.StartRecordTime != AV_NOPTS_VALUE && (long)(packet->pts * AudioStream.Timebase) - demuxer.StartTime > VideoDecoder.StartRecordTime)
                        recGotKeyframe = true;

                    if (recGotKeyframe)
                        curRecorder.Write(av_packet_clone(packet), !OnVideoDemuxer);
                }

                ret = avcodec_send_packet(codecCtx, packet);
                av_packet_free(&packet);

                if (ret != 0 && ret != AVERROR(EAGAIN))
                {
                    if (ret == AVERROR_EOF)
                    {
                        Status = Status.Ended;
                        break;
                    }
                    else
                    {
                        allowedErrors--;
                        if (CanWarn) Log.Warn($"{FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                        if (allowedErrors == 0) { Log.Error("Too many errors!"); Status = Status.Stopping; break; }
                        
                        continue;
                    }
                }
                
                while (true)
                {
                    ret = avcodec_receive_frame(codecCtx, frame);
                    if (ret != 0) { av_frame_unref(frame); break; }

                    if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                        frame->pts = frame->best_effort_timestamp;
                    else if (frame->pts == AV_NOPTS_VALUE)
                        { av_frame_unref(frame); continue; }

                    bool codecChanged = AudioStream.SampleFormat != codecCtx->sample_fmt || AudioStream.SampleRate != codecCtx->sample_rate || AudioStream.ChannelLayout != codecCtx->ch_layout.u.mask;

                    if (!filledFromCodec || codecChanged)
                    {
                        if (codecChanged && filledFromCodec)
                        {
                            byte[] buf = new byte[50];
                            fixed (byte* bufPtr = buf)
                            {
                                av_channel_layout_describe(&codecCtx->ch_layout, bufPtr, (ulong)buf.Length);
                                Log.Warn($"Codec changed {AudioStream.CodecIDOrig} {AudioStream.SampleFormat} {AudioStream.SampleRate} {AudioStream.ChannelLayoutStr} => {codecCtx->codec_id} {codecCtx->sample_fmt} {codecCtx->sample_rate} {Utils.BytePtrToStringUTF8(bufPtr)}");
                            }
                        }

                        DisposeInternal();
                        filledFromCodec = true;

                        avcodec_parameters_from_context(Stream.AVStream->codecpar, codecCtx);
                        AudioStream.AVStream->time_base = codecCtx->pkt_timebase;
                        AudioStream.Refresh();
                        resyncWithVideoRequired = !VideoDecoder.Disposed;

                        ret = Config.Audio.FiltersEnabled ? SetupFilters() : SetupSwr();

                        CodecChanged?.Invoke(this);

                        if (ret != 0)
                            { Status = Status.Stopping; break; }
                    }

                    if (resyncWithVideoRequired) // frame->pts can be NAN here
                    {
                        // TODO: in case of long distance will spin (CPU issue), possible reseek?

                        long ts = (long)(frame->pts * AudioStream.Timebase) - demuxer.StartTime + Config.Audio.Delay;
                        while (VideoDecoder.StartTime == AV_NOPTS_VALUE && VideoDecoder.IsRunning && resyncWithVideoRequired) Thread.Sleep(10);

                        if (ts < VideoDecoder.StartTime)
                        {
                            if (CanTrace) Log.Trace($"Drops {Utils.TicksToTime(ts)} (< V: {Utils.TicksToTime(VideoDecoder.StartTime)})");
                            av_frame_unref(frame);
                            continue;
                        }
                        else
                            resyncWithVideoRequired = false;
                    }

                    if (Config.Audio.FiltersEnabled)
                        ProcessWithFilters(frame);
                    else
                        Process(frame);
                }
            }
            
        } while (Status == Status.Running);

        if (isRecording) { StopRecording(); recCompleted(MediaType.Audio); }
    }    
    private void Process(AVFrame* frame)
    {
        try
        {
            if (speed != 1)
            {
                curSpeedFrame++;

                if (curSpeedFrame < speed)
                    return;

                curSpeedFrame = 0;    
            }

            AudioFrame mFrame = new()
            {
                timestamp   = (long)(frame->pts * AudioStream.Timebase) - demuxer.StartTime + Config.Audio.Delay,
                dataLen     = frame->nb_samples * ASampleBytes
            };
            if (CanTrace) Log.Trace($"Processes {Utils.TicksToTime(mFrame.timestamp)}");

            if (frame->nb_samples > cBufSamples)
            {
                /* TBR
                 * 1. If we change to different in/out sample rates we need to calculate delay
                 * 2. By destorying the cBuf can create critical issues while the audio decoder reads the data? (add lock)
                 * 3. Recalculate on Config.Decoder.MaxAudioFrames change (greater)
                 */

                int size    = Config.Decoder.MaxAudioFrames * mFrame.dataLen * 10;
                Log.Debug($"Re-allocating circular buffer ({frame->nb_samples} > {cBufSamples}) with {size}bytes");
                cBuf        = new byte[size];
                cBufPos     = 0;
                cBufSamples = frame->nb_samples * 10;
            }
            else if (cBufPos + mFrame.dataLen >= cBuf.Length)
                cBufPos     = 0;

            fixed (byte *circularBufferPosPtr = &cBuf[cBufPos])
            {
                int ret = swr_convert(swrCtx, &circularBufferPosPtr, frame->nb_samples, (byte**)&frame->data, frame->nb_samples);
                if (ret < 0)
                    return;

                mFrame.dataPtr = (IntPtr)circularBufferPosPtr;
            }

            cBufPos += mFrame.dataLen;
            Frames.Enqueue(mFrame);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to process frame ({e.Message})");
            
            return;
        }
        finally
        {
            av_frame_unref(frame);
        }
    }

    #region Recording
    internal Action<MediaType> 
            recCompleted;
    Remuxer curRecorder;
    bool    recGotKeyframe;
    internal bool isRecording;
    internal void StartRecording(Remuxer remuxer)
    {
        if (Disposed || isRecording) return;

        curRecorder     = remuxer;
        isRecording     = true;
        recGotKeyframe  = VideoDecoder.Disposed || VideoDecoder.Stream == null;
    }
    internal void StopRecording()
    {
        isRecording = false;
    }
    #endregion
}