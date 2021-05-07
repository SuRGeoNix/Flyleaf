using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaStream;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public abstract unsafe class DecoderBase
    {
        public Status               Status          { get; internal set; } = Status.Stopped;
        public bool                 IsRunning       { get; protected set; }
        public bool                 OnVideoDemuxer  => demuxer?.Type == MediaType.Video;
        public MediaType            Type            { get; protected set; }
        public StreamBase           Stream          { get; protected set; }
        public AVCodecContext*      CodecCtx        => codecCtx;

        protected AVFrame*          frame;
        protected AVCodecContext*   codecCtx;
        internal  object            lockCodecCtx    = new object();

        protected Thread            thread;
        protected AutoResetEvent    threadARE       = new AutoResetEvent(false);
        protected long              threadCounter;

        protected DemuxerBase       demuxer;
        protected MediaContext.DecoderContext  decCtx;
        protected Config            cfg => decCtx.cfg;

        public DecoderBase(MediaContext.DecoderContext decCtx)
        {
            this.decCtx = decCtx;

            if (this is VideoDecoder)
                Type = MediaType.Video;
            else if (this is AudioDecoder)
                Type = MediaType.Audio;
            else if (this is SubtitlesDecoder)
                Type = MediaType.Subs;
        }

        public int Open(StreamBase stream)
        {
            int ret;

            Stop();

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
            StartThread();

            return ret; // 0 for success
        }

        public virtual void Stop()
        {
            if (Status == Status.Stopped) return;

            StopThread();

            if (Stream != null)
            {
                if (Stream.Demuxer.Type == MediaType.Video)
                    Stream.Demuxer.DisableStream(Stream);
                else
                    Stream.Demuxer.Stop();
            }
            
            avcodec_flush_buffers(codecCtx); // ??
            avcodec_close(codecCtx);
            if (frame != null) fixed (AVFrame** ptr = &frame) av_frame_free(ptr);
            if (codecCtx != null) fixed (AVCodecContext** ptr = &codecCtx) avcodec_free_context(ptr);
            demuxer = null;
            Stream = null;
            Status = Status.Stopped;
        }

        protected abstract int Setup(AVCodec* codec);

        public void StartThread()
        {
            if ((thread != null && thread.IsAlive) || Status == Status.Stopped) return;

            Status = Status.Opening;
            thread = new Thread(() => Decode());
            thread.Name = $"[#{decCtx.player.PlayerId}] [Decoder: {Type}]"; thread.IsBackground= true; thread.Start();
            while (Status == Status.Opening) Thread.Sleep(5);
        }
        public void Start()
        {
            StartThread();
            if (Status != Status.Paused) return;

            long prev =threadCounter;
            threadARE.Set();
            while (prev == threadCounter) Thread.Sleep(5);
        }
        public void Pause()
        {
            if (!IsRunning) return;

            Status = Status.Pausing;
            while (Status == Status.Pausing) Thread.Sleep(5);
        }

        public void StopThread()
        {
            if (thread == null || !thread.IsAlive) return;

            Status = Status.Stopping;
            threadARE.Set();
            while (Status == Status.Stopping) Thread.Sleep(5);
        }

        protected void Decode()
        {
            Log($"[Thread] Started");

            while (Status != Status.Stopped && Status != Status.Stopping && Status != Status.Ended)
            {
                threadARE.Reset();

                Status = Status.Paused;
                Log($"{Status}");
                threadARE.WaitOne();
                if (Status == Status.Stopped || Status == Status.Stopping) break;

                IsRunning = true;
                Status = Status.Decoding;
                Log($"{Status}");
                threadCounter++;
                DecodeInternal();
                IsRunning = false;
                Log($"{Status}");

            } // While !stopThread

            if (Status != Status.Ended) Status = Status.Stopped;
            Log($"[Thread] Stopped ({Status})");
        }
        protected abstract void DecodeInternal();

        protected void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{decCtx.player.PlayerId}] [Decoder: {Type.ToString().PadLeft(5, ' ')}] {msg}"); }
    }

    public enum Status
    {
        Stopping,
        Stopped,

        Opening,
        Pausing,
        Paused,

        Decoding,
        QueueFull,
        PacketsEmpty,
        Draining,

        Ended
    }
}