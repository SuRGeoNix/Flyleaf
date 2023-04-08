using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Security;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRemuxer;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaDecoder;

public unsafe class AudioDecoder : DecoderBase
{
    public AudioStream      AudioStream         => (AudioStream) Stream;

    public VideoDecoder     VideoDecoder        { get; internal set; } // For Resync

    public ConcurrentQueue<AudioFrame>
                            Frames              { get; protected set; } = new ConcurrentQueue<AudioFrame>();

    static AVSampleFormat   AOutSampleFormat    = AVSampleFormat.AV_SAMPLE_FMT_S16;
    static AVChannelLayout  AOutChannelLayout   = new AVChannelLayout() { order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE, nb_channels = 2, u = new AVChannelLayout_u() { mask = AV_CH_FRONT_LEFT | AV_CH_FRONT_RIGHT} };
    static int              AOutChannels        = AOutChannelLayout.nb_channels;
    static int              ASampleBytes        = av_get_bytes_per_sample(AOutSampleFormat) * AOutChannels;

    SwrContext*             swrCtx;
    byte[]                  circularBuffer;
    int                     circularBufferPos;
    int                     maxSrcSamples;

    internal bool           resyncWithVideoRequired;

    public AudioDecoder(Config config, int uniqueId = -1, VideoDecoder syncDecoder = null) : base(config, uniqueId) { VideoDecoder = syncDecoder; }

    protected override unsafe int Setup(AVCodec* codec)
    {
        return 0;
    }

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

    protected override void DisposeInternal()
    {
        DisposeFrames();

        if (swrCtx != null)
        {
            swr_close(swrCtx);
            fixed(SwrContext** ptr = &swrCtx)
                swr_free(ptr);
            swrCtx = null;
        }

        circularBuffer  = null;
        filledFromCodec = false;
        maxSrcSamples   = 0;
    }

    public void DisposeFrames() => Frames = new ConcurrentQueue<AudioFrame>();

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

                resyncWithVideoRequired = !VideoDecoder.Disposed;
                curSpeedFrame = Speed;
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

                    AudioFrame mFrame = ProcessAudioFrame(frame);
                    if (mFrame != null) Frames.Enqueue(mFrame);

                    av_frame_unref(frame);
                }
            }
            
        } while (Status == Status.Running);

        if (isRecording) { StopRecording(); recCompleted(MediaType.Audio); }
    }

    [SecurityCritical]
    private AudioFrame ProcessAudioFrame(AVFrame* frame)
    {
        try
        {
            // TBR: AVStream doesn't refresh, we can get the updated info only from codecCtx (what about timebase, what about re-opening the codec?)
            bool codecChanged = AudioStream.SampleFormat != codecCtx->sample_fmt || AudioStream.SampleRate != codecCtx->sample_rate || AudioStream.ChannelLayout != codecCtx->ch_layout.u.mask;

            if (!filledFromCodec || codecChanged)
            {
                if (codecChanged && filledFromCodec)
                    Log.Warn($"Codec changed {AudioStream.CodecIDOrig} {AudioStream.SampleFormat} {AudioStream.SampleRate} {AudioStream.ChannelLayout} => {codecCtx->codec_id} {codecCtx->sample_fmt} {codecCtx->sample_rate} {codecCtx->ch_layout.u.mask}");

                DisposeInternal();
                filledFromCodec = true;

                avcodec_parameters_from_context(Stream.AVStream->codecpar, codecCtx);
                AudioStream.Refresh();
                resyncWithVideoRequired = !VideoDecoder.Disposed;
                SetupSwr();
                CodecChanged?.Invoke(this);
            }

            // TODO: based on VideoStream's StartTime and not Demuxer's
            //mFrame.timestamp = (long)(frame->pts * AudioStream.Timebase) - AudioStream.StartTime - (VideoDecoder.VideoStream.StartTime - AudioStream.StartTime) + Config.Audio.Delay;

            AudioFrame mFrame = new AudioFrame();
            mFrame.timestamp  = ((long)(frame->pts * AudioStream.Timebase) - demuxer.StartTime) + Config.Audio.Delay;
            if (CanTrace) Log.Trace($"Processes {Utils.TicksToTime(mFrame.timestamp)}");

            // Resync with VideoDecoder if required (drop early timestamps)
            if (resyncWithVideoRequired)
            {
                while (VideoDecoder.StartTime == AV_NOPTS_VALUE && VideoDecoder.IsRunning && resyncWithVideoRequired) Thread.Sleep(10);
                if (mFrame.timestamp < VideoDecoder.StartTime)
                {
                    // TODO: in case of long distance will spin (CPU issue), possible reseek?
                
                    if (CanTrace) Log.Trace($"Drops {Utils.TicksToTime(mFrame.timestamp)} (< V: {Utils.TicksToTime(VideoDecoder.StartTime)})");
                    return null;
                }
                else
                    resyncWithVideoRequired = false;
            }

            if (Speed != 1)
            {
                curSpeedFrame++;
                if (curSpeedFrame < Speed) return null;
                curSpeedFrame = 0;    
            }

            if (frame->nb_samples > maxSrcSamples)
            {
                int bufferSize      = (Config.Decoder.MaxAudioFrames * frame->nb_samples * ASampleBytes) * 2;
                Log.Debug($"Re-allocating circular buffer ({frame->nb_samples} > {maxSrcSamples}) with {bufferSize}bytes");
                maxSrcSamples       = frame->nb_samples;
                circularBuffer      = new byte[bufferSize];
                circularBufferPos   = 0;
            }

            mFrame.dataLen = frame->nb_samples * ASampleBytes;
            if (circularBufferPos + mFrame.dataLen > circularBuffer.Length)
                circularBufferPos = 0;

            fixed (byte *circularBufferPosPtr = &circularBuffer[circularBufferPos])
            {
                int ret = swr_convert(swrCtx, &circularBufferPosPtr, frame->nb_samples, (byte**)&frame->data, frame->nb_samples);
                if (ret < 0)
                    return null;
                
                
                mFrame.dataPtr = (IntPtr)circularBufferPosPtr;
            }

            circularBufferPos += mFrame.dataLen;

            return mFrame;

        }
        catch (Exception e)
        {
            Log.Error($"Failed to process frame ({e.Message})");
            return null;
        }
    }

    internal Action<MediaType> recCompleted;
    Remuxer curRecorder;
    bool recGotKeyframe;
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
}