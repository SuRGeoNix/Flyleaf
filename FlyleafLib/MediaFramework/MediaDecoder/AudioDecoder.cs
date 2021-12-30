using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Security;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRemuxer;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public unsafe class AudioDecoder : DecoderBase
    {
        public AudioStream      AudioStream         => (AudioStream) Stream;

        public VideoDecoder     VideoDecoder        { get; internal set; } // For Resync

        public ConcurrentQueue<AudioFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<AudioFrame>();

        static AVSampleFormat   AOutSampleFormat    = AVSampleFormat.AV_SAMPLE_FMT_S16;
        static int              AOutChannelLayout   = AV_CH_LAYOUT_STEREO;
        static int              AOutChannels        = av_get_channel_layout_nb_channels((ulong)AOutChannelLayout);

        SwrContext*             swrCtx;
        byte[]                  circularBuffer;
        AVFrame*                circularFrame;
        int                     circularBufferPos;

        internal bool           keyFrameRequired;

        public AudioDecoder(Config config, int uniqueId = -1, VideoDecoder syncDecoder = null) : base(config, uniqueId) { VideoDecoder = syncDecoder; }

        protected override unsafe int Setup(AVCodec* codec)
        {
            int ret;

            if (swrCtx == null)
                swrCtx = swr_alloc();

            circularBufferPos = 0;
            circularBuffer  = new byte[2 * 1024 * 1024]; // TBR: Should be based on max audio frames, max samples buffer size & max buffers used by xaudio2
            circularFrame   = av_frame_alloc();

            av_opt_set_int(swrCtx,           "in_channel_layout",   (int)codecCtx->channel_layout, 0);
            av_opt_set_int(swrCtx,           "in_channel_count",         codecCtx->channels, 0);
            av_opt_set_int(swrCtx,           "in_sample_rate",           codecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(swrCtx,    "in_sample_fmt",            codecCtx->sample_fmt, 0);

            av_opt_set_int(swrCtx,           "out_channel_layout",       AOutChannelLayout, 0);
            av_opt_set_int(swrCtx,           "out_channel_count",        AOutChannels, 0);
            av_opt_set_int(swrCtx,           "out_sample_rate",          codecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(swrCtx,    "out_sample_fmt",           AOutSampleFormat, 0);

            ret = swr_init(swrCtx);
            if (ret < 0)
                Log($"[AudioSetup] [ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

            keyFrameRequired = !VideoDecoder.Disposed;

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

            if (circularFrame != null)
            {
                fixed(AVFrame** ptr = &circularFrame)
                    av_frame_free(ptr);

                circularFrame = null;
            }

            circularBuffer = null;
        }

        public void DisposeFrames()
        {
            Frames = new ConcurrentQueue<AudioFrame>();
        }

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

                    keyFrameRequired = !VideoDecoder.Disposed;
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
                            Log($"Demuxer is not running [Demuxer Status: {demuxer.Status}]");

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
                    demuxer.AudioPackets.TryDequeue(out IntPtr pktPtr);
                    packet = (AVPacket*) pktPtr;

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
                            Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                            if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); Status = Status.Stopping; break; }
                            
                            continue;
                        }
                    }

                    while (true)
                    {
                        ret = avcodec_receive_frame(codecCtx, frame);
                        if (ret != 0) { av_frame_unref(frame); break; }

                        frame->pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                        if (frame->pts == AV_NOPTS_VALUE) { av_frame_unref(frame); continue; }

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
            if (Speed != 1)
            {
                curSpeedFrame++;
                if (curSpeedFrame < Speed) return null;
                curSpeedFrame = 0;    
            }

            AudioFrame mFrame = new AudioFrame();
            mFrame.timestamp = ((long)(frame->pts * AudioStream.Timebase) - demuxer.StartTime) + Config.Audio.Delay;

            // TODO: based on VideoStream's StartTime and not Demuxer's
            //mFrame.timestamp = (long)(frame->pts * AudioStream.Timebase) - AudioStream.StartTime - (VideoDecoder.VideoStream.StartTime - AudioStream.StartTime) + Config.Audio.Delay;

            //Log($"Decoding {Utils.TicksToTime(mFrame.timestamp)} | {Utils.TicksToTime((long)(mFrame.pts * AudioStream.Timebase))}");

            // Resync with VideoDecoder if required (drop early timestamps)
            if (keyFrameRequired)
            {
                while (VideoDecoder.StartTime == AV_NOPTS_VALUE && VideoDecoder.IsRunning && keyFrameRequired) Thread.Sleep(10);
                if (mFrame.timestamp < VideoDecoder.StartTime)
                {
                    // TODO: in case of long distance will spin (CPU issue), possible reseek?

                    //Log($"Droping {Utils.TicksToTime(mFrame.timestamp)} < {Utils.TicksToTime(VideoDecoder.StartTime)}");
                    return null;
                }
                else
                    keyFrameRequired = false;
            }

            try
            {
                int ret;

                if (circularFrame->nb_samples != frame->nb_samples)
                {
                    circularFrame->nb_samples = (int)av_rescale_rnd(swr_get_delay(swrCtx, codecCtx->sample_rate) + frame->nb_samples, codecCtx->sample_rate, codecCtx->sample_rate, AVRounding.AV_ROUND_UP);

                    fixed (byte *ptr = &circularBuffer[circularBufferPos])
                        av_samples_fill_arrays((byte**)&circularFrame->data, (int*)&circularFrame->linesize, ptr, AOutChannels, circularFrame->nb_samples, AOutSampleFormat, 0);    
                }

                fixed (byte *circularBufferPosPtr = &circularBuffer[circularBufferPos])
                {
                    *(byte**)&circularFrame->data = circularBufferPosPtr;
                    ret = swr_convert(swrCtx, (byte**)&circularFrame->data, circularFrame->nb_samples, (byte**)&frame->data, frame->nb_samples);
                    if (ret < 0) return null;

                    mFrame.dataLen = av_samples_get_buffer_size((int*)&circularFrame->linesize, AOutChannels, ret, AOutSampleFormat, 1);
                    mFrame.dataPtr = (IntPtr)circularBufferPosPtr;
                }

                // TBR: Randomly gives the max samples size to half buffer
                circularBufferPos += mFrame.dataLen;
                if (circularBufferPos > circularBuffer.Length / 2)
                    circularBufferPos = 0;

            } catch (Exception e) {  Log("[ProcessAudioFrame] [Error] " + e.Message + " - " + e.StackTrace); return null; }

            return mFrame;
        }


        internal Action<MediaType> recCompleted;
        Remuxer curRecorder;
        bool recGotKeyframe;
        internal bool isRecording;
        internal void StartRecording(Remuxer remuxer, long startAt = -1)
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
}