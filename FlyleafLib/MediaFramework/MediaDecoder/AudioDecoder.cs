﻿using System.Collections.Concurrent;
using System.Threading;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRemuxer;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaDecoder;

/* TODO
 *
 * Circular Buffer
 * - Safe re-allocation and check also actual frames in queue (as now during draining we can overwrite them)
 * - Locking with Audio.AddSamples during re-allocation and re-write old data to the new buffer and update the pointers in queue
 *
 * Filters
 * - Note: Performance issue (for seek/speed change). We can't drain the buffersrc and re-use the filtergraph without re-initializing it, is not supported
 * - Check if av_buffersrc_get_nb_failed_requests required
 * - Add Config for filter threads?
 * - Review Access Violation issue with dynaudnorm/loudnorm filters in combination with atempo (when changing speed to fast?)
 * - Use multiple atempo for better quality (for < 0.5 and > 2, use eg. 2 of sqrt(X) * sqrt(X) to achive this)
 * - Review locks / recode RunInternal to be able to continue from where it stopped (eg. ProcessFilter)
 *
 * Custom Frames Queue to notify when the queue is not full anymore (to avoid thread sleep which can cause delays)
 * Support more output formats/channels/sampleRates (and currently output to 32-bit, sample rate to 48Khz and not the same as input? - should calculate possible delays)
 */

public unsafe partial class AudioDecoder : DecoderBase
{
    public AudioStream      AudioStream         => (AudioStream) Stream;
    public readonly
            VideoDecoder    VideoDecoder;
    public ConcurrentQueue<AudioFrame>
                            Frames              { get; protected set; } = new();

    static AVSampleFormat   AOutSampleFormat    = AVSampleFormat.S16;
    static string           AOutSampleFormatStr = av_get_sample_fmt_name(AOutSampleFormat);
    static AVChannelLayout  AOutChannelLayout   = AV_CHANNEL_LAYOUT_STEREO;// new() { order = AVChannelOrder.Native, nb_channels = 2, u = new AVChannelLayout_u() { mask = AVChannel.for AV_CH_FRONT_LEFT | AV_CH_FRONT_RIGHT} };
    static int              AOutChannels        = AOutChannelLayout.nb_channels;
    static int              ASampleBytes        = av_get_bytes_per_sample(AOutSampleFormat) * AOutChannels;

    public readonly object  CircularBufferLocker= new();
    internal Action         CBufAlloc;          // Informs Audio player to clear buffer pointers to avoid access violation
    static int              cBufTimesSize       = 4;
    int                     cBufTimesCur        = 1;
    byte[]                  cBuf;
    int                     cBufPos;
    int                     cBufSamples;
    internal bool           resyncWithVideoRequired;
    SwrContext*             swrCtx;

    internal long           nextPts;
    double                  sampleRateTimebase;

    public AudioDecoder(Config config, int uniqueId = -1, VideoDecoder syncDecoder = null) : base(config, uniqueId)
        => VideoDecoder = syncDecoder;

    protected override int Setup(AVCodec* codec) => 0;
    private int SetupSwr()
    {
        int ret;

        DisposeSwr();
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

        lock (CircularBufferLocker)
        cBuf            = null;
        cBufSamples     = 0;
        filledFromCodec = false;
        nextPts         = AV_NOPTS_VALUE;
    }
    public void DisposeFrames() => Frames = new();
    public void Flush()
    {
        lock (lockActions)
            lock (lockCodecCtx)
            {
                if (Disposed)
                    return;

                if (Status == Status.Ended)
                    Status = Status.Stopped;
                else if (Status == Status.Draining)
                    Status = Status.Stopping;

                resyncWithVideoRequired = !VideoDecoder.Disposed;
                nextPts = AV_NOPTS_VALUE;
                DisposeFrames();
                avcodec_flush_buffers(codecCtx);
                if (filterGraph != null)
                    SetupFilters();
            }
    }

    protected override void RunInternal()
    {
        int ret = 0;
        int allowedErrors = Config.Decoder.MaxErrors;
        int sleepMs = Config.Decoder.MaxAudioFrames > 5 && Config.Player.MaxLatency == 0 ? 10 : 4;
        AVPacket *packet;

        do
        {
            // Wait until Queue not Full or Stopped
            if (Frames.Count >= Config.Decoder.MaxAudioFrames)
            {
                lock (lockStatus)
                    if (Status == Status.Running)
                        Status = Status.QueueFull;

                while (Frames.Count >= Config.Decoder.MaxAudioFrames && Status == Status.QueueFull)
                    Thread.Sleep(sleepMs);

                lock (lockStatus)
                {
                    if (Status != Status.QueueFull)
                        break;

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
                        lock (lockStatus)
                        {
                            // TODO: let the demuxer push the draining packet
                            Log.Debug("Draining");
                            Status = Status.Draining;
                            var drainPacket = av_packet_alloc();
                            drainPacket->data = null;
                            drainPacket->size = 0;
                            demuxer.AudioPackets.Enqueue(drainPacket);
                        }

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

                    Thread.Sleep(sleepMs);
                }

                lock (lockStatus)
                {
                    CriticalArea = false;
                    if (Status != Status.QueueEmpty && Status != Status.Draining) break;
                    if (Status != Status.Draining) Status = Status.Running;
                }
            }

            Monitor.Enter(lockCodecCtx); // restore the old lock / add interrupters similar to the demuxer
            try
            {
                if (Status == Status.Stopped)
                    { Monitor.Exit(lockCodecCtx); continue; }

                packet = demuxer.AudioPackets.Dequeue();

                if (packet == null)
                    { Monitor.Exit(lockCodecCtx); continue; }

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

                        Monitor.Exit(lockCodecCtx); continue;
                    }
                }

                while (true)
                {
                    ret = avcodec_receive_frame(codecCtx, frame);
                    if (ret != 0)
                    {
                        av_frame_unref(frame);

                        if (ret == AVERROR_EOF && filterGraph != null)
                        {
                            lock (lockSpeed)
                            {
                                DrainFilters();
                                Status = Status.Ended;
                            }
                        }

                        break;
                    }

                    if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                        frame->pts = frame->best_effort_timestamp;
                    else if (frame->pts == AV_NOPTS_VALUE)
                    {
                        if (nextPts == AV_NOPTS_VALUE && filledFromCodec) // Possible after seek (maybe set based on pkt_pos?)
                        {
                            av_frame_unref(frame);
                            continue;
                        }

                        frame->pts = nextPts;
                    }

                    // We could fix it down to the demuxer based on size?
                    if (frame->duration <= 0)
                        frame->duration = av_rescale_q((long)(frame->nb_samples * sampleRateTimebase), Engine.FFmpeg.AV_TIMEBASE_Q, Stream.AVStream->time_base);

                    bool codecChanged = AudioStream.SampleFormat != codecCtx->sample_fmt || AudioStream.SampleRate != codecCtx->sample_rate || AudioStream.ChannelLayout != codecCtx->ch_layout.u.mask;

                    if (!filledFromCodec || codecChanged)
                    {
                        if (codecChanged && filledFromCodec)
                        {
                            byte[] buf = new byte[50];
                            fixed (byte* bufPtr = buf)
                            {
                                av_channel_layout_describe(&codecCtx->ch_layout, bufPtr, (nuint)buf.Length);
                                Log.Warn($"Codec changed {AudioStream.CodecIDOrig} {AudioStream.SampleFormat} {AudioStream.SampleRate} {AudioStream.ChannelLayoutStr} => {codecCtx->codec_id} {codecCtx->sample_fmt} {codecCtx->sample_rate} {Utils.BytePtrToStringUTF8(bufPtr)}");
                            }
                        }

                        DisposeInternal();
                        filledFromCodec = true;

                        avcodec_parameters_from_context(Stream.AVStream->codecpar, codecCtx);
                        AudioStream.AVStream->time_base = codecCtx->pkt_timebase;
                        AudioStream.Refresh();
                        resyncWithVideoRequired = !VideoDecoder.Disposed;
                        sampleRateTimebase = 1000 * 1000.0 / codecCtx->sample_rate;
                        nextPts = AudioStream.StartTimePts;

                        if (frame->pts == AV_NOPTS_VALUE)
                            frame->pts = nextPts;

                        ret = SetupFiltersOrSwr();

                        CodecChanged?.Invoke(this);

                        if (ret != 0)
                        {
                            Status = Status.Stopping;
                            av_frame_unref(frame);
                            break;
                        }

                        if (nextPts == AV_NOPTS_VALUE)
                        {
                            av_frame_unref(frame);
                            continue;
                        }
                    }

                    if (resyncWithVideoRequired)
                    {
                        // TODO: in case of long distance will spin (CPU issue), possible reseek?
                        while (VideoDecoder.StartTime == AV_NOPTS_VALUE && VideoDecoder.IsRunning && resyncWithVideoRequired)
                            Thread.Sleep(10);

                        long ts = (long)((frame->pts + frame->duration) * AudioStream.Timebase) - demuxer.StartTime + Config.Audio.Delay;

                        if (ts < VideoDecoder.StartTime)
                        {
                            if (CanTrace) Log.Trace($"Drops {Utils.TicksToTime(ts)} (< V: {Utils.TicksToTime(VideoDecoder.StartTime)})");
                            av_frame_unref(frame);
                            continue;
                        }
                        else
                            resyncWithVideoRequired = false;
                    }

                    lock (lockSpeed)
                    {
                        if (filterGraph != null)
                            ProcessFilters();
                        else
                            Process();

                        av_frame_unref(frame);
                    }
                }
            } catch { }

            Monitor.Exit(lockCodecCtx);

        } while (Status == Status.Running);

        if (isRecording) { StopRecording(); recCompleted(MediaType.Audio); }

        if (Status == Status.Draining) Status = Status.Ended;
    }
    private void Process()
    {
        try
        {
            nextPts = frame->pts + frame->duration;

            var dataLen     = frame->nb_samples * ASampleBytes;
            var speedDataLen= Utils.Align((int)(dataLen / speed), ASampleBytes);

            AudioFrame mFrame = new()
            {
                timestamp   = (long)(frame->pts * AudioStream.Timebase) - demuxer.StartTime + Config.Audio.Delay,
                dataLen     = speedDataLen
            };
            if (CanTrace) Log.Trace($"Processes {Utils.TicksToTime(mFrame.timestamp)}");

            if (frame->nb_samples > cBufSamples)
                AllocateCircularBuffer(frame->nb_samples);
            else if (cBufPos + Math.Max(dataLen, speedDataLen) >= cBuf.Length)
                cBufPos     = 0;

            fixed (byte *circularBufferPosPtr = &cBuf[cBufPos])
            {
                int ret = swr_convert(swrCtx, &circularBufferPosPtr, frame->nb_samples, (byte**)&frame->data, frame->nb_samples);
                if (ret < 0)
                    return;

                mFrame.dataPtr = (IntPtr)circularBufferPosPtr;
            }

            // Fill silence
            if (speed < 1)
                for (int p = dataLen; p < speedDataLen; p++)
                    cBuf[cBufPos + p] = 0;

            cBufPos += Math.Max(dataLen, speedDataLen);
            Frames.Enqueue(mFrame);

            // Wait until Queue not Full or Stopped
            if (Frames.Count >= Config.Decoder.MaxAudioFrames * cBufTimesCur)
            {
                Monitor.Exit(lockCodecCtx);
                lock (lockStatus)
                    if (Status == Status.Running) Status = Status.QueueFull;

                while (Frames.Count >= Config.Decoder.MaxAudioFrames * cBufTimesCur && Status == Status.QueueFull)
                    Thread.Sleep(20);

                Monitor.Enter(lockCodecCtx);

                lock (lockStatus)
                {
                    if (Status != Status.QueueFull)
                        return;

                    Status = Status.Running;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to process frame ({e.Message})");
        }
    }

    private void AllocateCircularBuffer(int samples)
    {
        /* TBR
        * 1. If we change to different in/out sample rates we need to calculate delay
        * 2. By destorying the cBuf can create critical issues while the audio decoder reads the data? (add lock) | we need to copy the lost data and change the pointers
        * 3. Recalculate on Config.Decoder.MaxAudioFrames change (greater)
        * 4. cBufTimesSize cause filters can pass the limit when we need to use lockSpeed
        */

        samples = Math.Max(10000, samples); // 10K samples to ensure that currently we will not re-allocate?
        int size    = Config.Decoder.MaxAudioFrames * samples * ASampleBytes * cBufTimesSize;
        Log.Debug($"Re-allocating circular buffer ({samples} > {cBufSamples}) with {size}bytes");

        lock (CircularBufferLocker)
        {
            DisposeFrames(); // TODO: copy data
            CBufAlloc?.Invoke();
            cBuf        = new byte[size];
            cBufPos     = 0;
            cBufSamples = samples;
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
    internal void StopRecording() => isRecording = false;
    #endregion
}
