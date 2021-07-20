using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using FlyleafLib.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public unsafe class AudioDecoder : DecoderBase
    {
        public AudioStream      AudioStream         => (AudioStream) Stream;

        public ConcurrentQueue<AudioFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<AudioFrame>();

        static AVSampleFormat   AOutSampleFormat    = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        static int              AOutChannelLayout   = AV_CH_LAYOUT_STEREO;
        static int              AOutChannels        = av_get_channel_layout_nb_channels((ulong)AOutChannelLayout);

        public SwrContext*      swrCtx;
        public byte**           m_dst_data;
        public int              m_max_dst_nb_samples;
        public int              m_dst_linesize;

        public AudioDecoder(MediaContext.DecoderContext decCtx) : base(decCtx) { }

        protected override unsafe int Setup(AVCodec* codec)
        {
            lock (lockCodecCtx)
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

            decCtx.player.audioPlayer.Initialize(codecCtx->sample_rate);

            return ret;
            }
        }

        public override void Stop()
        {
            lock (lockCodecCtx)
            {
                base.Stop();
                while (Frames.Count > 0)
                {
                    Frames.TryDequeue(out AudioFrame aFrame);
                    if (aFrame != null) aFrame.audioData = new byte[0];
                }
                Frames = new ConcurrentQueue<AudioFrame>();
                if (swrCtx != null) { swr_close(swrCtx); fixed(SwrContext** ptr = &swrCtx) swr_free(ptr); swrCtx = null; }
                if (m_dst_data != null) { av_freep(&m_dst_data[0]); fixed (byte*** ptr = &m_dst_data) av_freep(ptr); m_dst_data = null; }
            }
        }
        public void Flush()
        {
            lock (lockCodecCtx)
            {
                if (Status == Status.Stopped) return;

                Frames = new ConcurrentQueue<AudioFrame>();
                avcodec_flush_buffers(codecCtx);
                if (Status == Status.Ended) Status = Status.Paused;
            }
        }
        protected override void DecodeInternal()
        {
            int ret = 0;
            int allowedErrors = cfg.decoder.MaxErrors;
            AVPacket *packet;

            while (Status == Status.Decoding)
            {
                // While Frames Queue Full
                if (Frames.Count >= cfg.decoder.MaxAudioFrames)
                {
                    Status = Status.QueueFull;

                    while (Frames.Count >= cfg.decoder.MaxAudioFrames && Status == Status.QueueFull) Thread.Sleep(20);
                    if (Status != Status.QueueFull) break;
                    Status = Status.Decoding;
                }

                // While Packets Queue Empty (Ended | Quit if Demuxer stopped | Wait until we get packets)
                if (demuxer.AudioPackets.Count == 0)
                {
                    Status = Status.PacketsEmpty;
                    while (demuxer.AudioPackets.Count == 0 && Status == Status.PacketsEmpty)
                    {
                        if (demuxer.Status == MediaDemuxer.Status.Ended)
                        {
                            Status = Status.Ended;
                            break;
                        }
                        else if (demuxer.Status != MediaDemuxer.Status.Demuxing && demuxer.Status != MediaDemuxer.Status.QueueFull)
                        {
                            Log($"Demuxer is not running [Demuxer Status: {demuxer.Status}]");
                            Status = demuxer.Status == MediaDemuxer.Status.Stopping || demuxer.Status == MediaDemuxer.Status.Stopped ? Status.Stopping2 : Status.Paused;
                            break;
                        }
                        
                        Thread.Sleep(20);
                    }
                    if (Status != Status.PacketsEmpty) break;
                    Status = Status.Decoding;
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
                            Log("EOF");
                            return;
                        }
                        else
                        {
                            allowedErrors--;
                            Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                            //avcodec_flush_buffers(codecCtx); ??

                            if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); return; }
                            
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
                
            } // While Decoding

            if (Status == Status.Draining) Status = Status.Ended;
        }

        private AudioFrame ProcessAudioFrame(AVFrame* frame)
        {
            AudioFrame mFrame = new AudioFrame();
            mFrame.pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
            if (mFrame.pts == AV_NOPTS_VALUE) return null;
            mFrame.timestamp = ((long)(mFrame.pts * AudioStream.Timebase) - AudioStream.StartTime) + cfg.audio.DelayTicks + (AudioStream.StartTime - decCtx.VideoDecoder.VideoStream.StartTime);
            //Log(Utils.TicksToTime(mFrame.timestamp));

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

    }
}