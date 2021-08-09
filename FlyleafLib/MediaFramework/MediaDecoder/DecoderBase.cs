using System;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public abstract unsafe class DecoderBase : RunThreadBase
    {
        public MediaType                Type            { get; protected set; }

        public bool                     OnVideoDemuxer  => demuxer?.Type == MediaType.Video;
        public Demuxer                  Demuxer         => demuxer;
        public StreamBase               Stream          { get; protected set; }
        public AVCodecContext*          CodecCtx        => codecCtx;
        public Action<DecoderBase>      CodecChanged    { get; set; }
        public int                      Speed           { get; set; } = 1;

        protected AVFrame*          frame;
        protected AVCodecContext*   codecCtx;
        internal  object            lockCodecCtx    = new object();

        protected Demuxer           demuxer;
        protected Config            cfg;

        public DecoderBase(Config config, int uniqueId = -1) : base(uniqueId)
        {
            cfg     = config;

            if (this is VideoDecoder)
                Type = MediaType.Video;
            else if (this is AudioDecoder)
                Type = MediaType.Audio;
            else if (this is SubtitlesDecoder)
                Type = MediaType.Subs;

            threadName = $"Decoder: {Type.ToString().PadLeft(5, ' ')}";
        }

        public int Open(StreamBase stream)
        {
            lock (lockActions)
            {
                Dispose();
                int ret = -1;

                try
                {
                    
                    if (stream == null || stream.Demuxer.Interrupter.ForceInterrupt == 1 || stream.Demuxer.Disposed) return -1;
                    lock (stream.Demuxer.lockActions)
                    {
                        if (stream == null || stream.Demuxer.Interrupter.ForceInterrupt == 1 || stream.Demuxer.Disposed) return -1;
                    
                        Status  = Status.Opening;
                        Stream  = stream;
                        demuxer = stream.Demuxer;

                        AVCodec* codec = avcodec_find_decoder(stream.AVStream->codecpar->codec_id);
                        if (codec == null)
                            { Log($"[CodecOpen {Type}] [ERROR-1] No suitable codec found"); return -1; }

                        codecCtx = avcodec_alloc_context3(null);
                        if (codecCtx == null)
                            { Log($"[CodecOpen {Type}] [ERROR-2] Failed to allocate context3"); return -1; }

                        ret = avcodec_parameters_to_context(codecCtx, stream.AVStream->codecpar);
                        if (ret < 0)
                            { Log($"[CodecOpen {Type}] [ERROR-3] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); return ret; }

                        codecCtx->pkt_timebase  = stream.AVStream->time_base;
                        codecCtx->codec_id      = codec->id;

                        ret = Setup(codec);
                        if (ret < 0) return ret;

                        ret = avcodec_open2(codecCtx, codec, null);
                        if (ret < 0) return ret;

                        frame = av_frame_alloc();
                        demuxer.EnableStream(Stream);

                        CodecChanged?.Invoke(this);

                        Disposed = false;
                        Status = Status.Stopped;

                        return ret; // 0 for success
                    }
                }
                finally
                {
                    if (ret !=0 )
                        Dispose();
                }
            }
        }
        protected abstract int Setup(AVCodec* codec);

        public void Dispose()
        {
            if (Disposed) return;

            lock (lockActions)
            {
                if (Disposed) return;

                Stop();
                DisposeInternal();

                if (Stream != null && !Stream.Demuxer.Disposed)
                {
                    if (Stream.Demuxer.Type == MediaType.Video)
                        Stream.Demuxer.DisableStream(Stream);
                    else
                        Stream.Demuxer.Dispose();
                }

                avcodec_flush_buffers(codecCtx);
                avcodec_close(codecCtx);
                if (frame != null) fixed (AVFrame** ptr = &frame) av_frame_free(ptr);
                if (codecCtx != null) fixed (AVCodecContext** ptr = &codecCtx) avcodec_free_context(ptr);
                demuxer = null;
                Stream = null;
                Status = Status.Stopped;

                Disposed = true;
                Log("Disposed");
            }
        }
        protected abstract void DisposeInternal();
    }
}