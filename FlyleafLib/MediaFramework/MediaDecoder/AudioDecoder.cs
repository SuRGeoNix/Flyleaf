using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public unsafe class AudioDecoder : DecoderBase
    {
        public AudioStream      AudioStream         => (AudioStream) Stream;

        public VideoDecoder     VideoDecoder        { get; internal set; } // For Resync

        public ConcurrentQueue<AudioFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<AudioFrame>();

        static AVSampleFormat   AOutSampleFormat    = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        static int              AOutChannelLayout   = AV_CH_LAYOUT_STEREO;
        static int              AOutChannels        = av_get_channel_layout_nb_channels((ulong)AOutChannelLayout);

        public SwrContext*      swrCtx;
        public byte**           m_dst_data;
        public int              m_max_dst_nb_samples;
        public int              m_dst_linesize;

        internal bool           keyFrameRequired;

        public AudioDecoder(Config config, int uniqueId = -1, VideoDecoder syncDecoder = null) : base(config, uniqueId) { VideoDecoder = syncDecoder; }

        protected override unsafe int Setup(AVCodec* codec)
        {
            int ret;

            if (swrCtx == null) swrCtx = swr_alloc();
            
            m_max_dst_nb_samples    = -1;

            av_opt_set_int(swrCtx,           "in_channel_layout",   (int)codecCtx->channel_layout, 0);
            av_opt_set_int(swrCtx,           "in_channel_count",         codecCtx->channels, 0);
            av_opt_set_int(swrCtx,           "in_sample_rate",           codecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(swrCtx,    "in_sample_fmt",            codecCtx->sample_fmt, 0);

            av_opt_set_int(swrCtx,           "out_channel_layout",       AOutChannelLayout, 0);
            av_opt_set_int(swrCtx,           "out_channel_count",        AOutChannels, 0);
            av_opt_set_int(swrCtx,           "out_sample_rate",          codecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(swrCtx,    "out_sample_fmt",           AOutSampleFormat, 0);
            
            ret = swr_init(swrCtx);
            if (ret < 0) Log($"[AudioSetup] [ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

            keyFrameRequired = !VideoDecoder.Disposed;

            return ret;
        }

        protected override void DisposeInternal()
        {
            if (swrCtx != null) { swr_close(swrCtx); fixed(SwrContext** ptr = &swrCtx) swr_free(ptr); swrCtx = null; }
            if (m_dst_data != null) { av_freep(&m_dst_data[0]); fixed (byte*** ptr = &m_dst_data) av_freep(ptr); m_dst_data = null; }
            DisposeFrames();
        }

        public void Flush()
        {
            lock (lockActions)
            lock (lockCodecCtx)
            {
                if (Disposed) return;

                if (Status == Status.Ended) Status = Status.Stopped;
                //else if (Status == Status.Draining) Status = Status.Stopping;

                Frames = new ConcurrentQueue<AudioFrame>();
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
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private AudioFrame ProcessAudioFrame(AVFrame* frame)
        {
            AudioFrame mFrame;
            if (Speed != 1)
            {
                curSpeedFrame++;
                if (curSpeedFrame < Speed) return null;
                curSpeedFrame = 0;
                mFrame = new AudioFrame();
                mFrame.timestamp = ((long)(frame->pts * AudioStream.Timebase) - demuxer.StartTime) + Config.Audio.Delay;
                mFrame.timestamp /= Speed;
            }
            else
            {
                mFrame = new AudioFrame();
                mFrame.timestamp = ((long)(frame->pts * AudioStream.Timebase) - demuxer.StartTime) + Config.Audio.Delay;
            }
            //Log($"Decoding {Utils.TicksToTime(mFrame.timestamp)} | {Utils.TicksToTime((long)(mFrame.pts * AudioStream.Timebase))}");

            // Resync with VideoDecoder if required (drop early timestamps)
            if (keyFrameRequired)
            {
                while (VideoDecoder.StartTime == AV_NOPTS_VALUE && VideoDecoder.IsRunning) Thread.Sleep(10);
                if (mFrame.timestamp < VideoDecoder.StartTime/Speed)
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
                int dst_nb_samples;

                if (m_max_dst_nb_samples == -1)
                {
                    if (m_dst_data != null) { av_freep(&m_dst_data[0]); fixed (byte*** ptr = &m_dst_data) av_freep(ptr); m_dst_data = null; }

                    m_max_dst_nb_samples = (int)av_rescale_rnd(frame->nb_samples, codecCtx->sample_rate, codecCtx->sample_rate, AVRounding.AV_ROUND_UP);
                    fixed(byte*** dst_data = &m_dst_data)
                    fixed(int *dst_linesize = &m_dst_linesize)
                    ret = av_samples_alloc_array_and_samples(dst_data, dst_linesize, AOutChannels, m_max_dst_nb_samples, AOutSampleFormat, 0);
                }

                fixed (int* dst_linesize = &m_dst_linesize)
                {
                    dst_nb_samples = (int)av_rescale_rnd(swr_get_delay(swrCtx, codecCtx->sample_rate) + frame->nb_samples, codecCtx->sample_rate, codecCtx->sample_rate, AVRounding.AV_ROUND_UP);

                    if (dst_nb_samples > m_max_dst_nb_samples)
                    {
                        av_freep(&m_dst_data[0]);
                        ret = av_samples_alloc(m_dst_data, dst_linesize, AOutChannels, (int)dst_nb_samples, AOutSampleFormat, 0);
                    }

                    ret = swr_convert(swrCtx, m_dst_data, dst_nb_samples, (byte**)&frame->data, frame->nb_samples);
                    if (ret < 0) return null;

                    int dst_data_len = av_samples_get_buffer_size(dst_linesize, AOutChannels, ret, AOutSampleFormat, 1);

                    mFrame.audioData = new byte[dst_data_len];
                    Marshal.Copy((IntPtr)(*m_dst_data), mFrame.audioData, 0, mFrame.audioData.Length);
                }

            } catch (Exception e) {  Log("[ProcessAudioFrame] [Error] " + e.Message + " - " + e.StackTrace); return null; }

            return mFrame;
        }

        public void DisposeFrames()
        {
            while (Frames.Count > 0)
            {
                Frames.TryDequeue(out AudioFrame aFrame);
                if (aFrame != null) aFrame.audioData = new byte[0];
            }
            Frames = new ConcurrentQueue<AudioFrame>();
        }
    }
}