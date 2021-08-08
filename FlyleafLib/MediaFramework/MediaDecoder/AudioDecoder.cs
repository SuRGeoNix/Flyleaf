using System;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public unsafe class AudioDecoder : DecoderBase
    {
        public AudioStream      AudioStream         => (AudioStream) Stream;
        public VideoDecoder     RelatedVideoDecoder { get ; private set; }

        public ConcurrentQueue<AudioFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<AudioFrame>();

        static AVSampleFormat   AOutSampleFormat    = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        static int              AOutChannelLayout   = AV_CH_LAYOUT_STEREO;
        static int              AOutChannels        = av_get_channel_layout_nb_channels((ulong)AOutChannelLayout);

        public SwrContext*      swrCtx;
        public byte**           m_dst_data;
        public int              m_max_dst_nb_samples;
        public int              m_dst_linesize;

        public AudioDecoder(Config config, VideoDecoder rVideoDecoder = null, int uniqueId = -1) : base(config, uniqueId)
        {
            RelatedVideoDecoder = rVideoDecoder;
        }

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

                Frames = new ConcurrentQueue<AudioFrame>();
                avcodec_flush_buffers(codecCtx);
                if (Status == Status.Ended) Status = Status.Stopped;
            }
        }
        protected override void RunInternal()
        {
            int ret = 0;
            int allowedErrors = cfg.decoder.MaxErrors;
            AVPacket *packet;

            do
            {
                // Wait until Queue not Full or Stopped
                if (Frames.Count >= cfg.decoder.MaxAudioFrames)
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueFull;

                    while (Frames.Count >= cfg.decoder.MaxAudioFrames && Status == Status.QueueFull) Thread.Sleep(20);

                    lock (lockStatus)
                    {
                        if (Status != Status.QueueFull) break;
                        Status = Status.Running;
                    }       
                }

                // While Packets Queue Empty (Ended | Quit if Demuxer stopped | Wait until we get packets)
                if (demuxer.AudioPackets.Count == 0)
                {
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

                        AudioFrame mFrame = ProcessAudioFrame(frame);
                        if (mFrame != null) Frames.Enqueue(mFrame);

                        av_frame_unref(frame);
                    }
                }
                
            } while (Status == Status.Running);
        }

        private AudioFrame ProcessAudioFrame(AVFrame* frame)
        {
            AudioFrame mFrame = new AudioFrame();
            mFrame.pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
            if (mFrame.pts == AV_NOPTS_VALUE) return null;
            //long avDiff = RelatedVideoDecoder != null &&  RelatedVideoDecoder.VideoStream != null ? AudioStream.StartTime - RelatedVideoDecoder.VideoStream.StartTime : 0;
            mFrame.timestamp = ((long)(mFrame.pts * AudioStream.Timebase) - demuxer.StartTime) + cfg.audio.DelayTicks;
            //Log(Utils.TicksToTime((long)(mFrame.pts * AudioStream.Timebase)));

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