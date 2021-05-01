using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaStream;
using FlyleafLib.MediaFramework.MediaContext;

namespace FlyleafLib.MediaFramework.MediaDemuxer
{
    public abstract unsafe class DemuxerBase
    {
        public Status                   Status          { get; internal set; } = Status.Stopped;
        public bool                     IsRunning       { get; private set; }
        public MediaType                Type            { get; private set; }

        // Format Info
        public string                   Name            { get; private set; }
        public string                   LongName        { get; private set; }
        public string                   Extensions      { get; private set; }
        public long                     StartTime       { get; private set; }
        public long                     Duration        { get; private set; }

        public AVFormatContext*         FormatContext   => fmtCtx;
        public CustomIOContext          CustomIOContext { get; private set; }

        // Media Streams
        public List<AudioStream>        AudioStreams    { get; private set; } = new List<AudioStream>();
        public List<VideoStream>        VideoStreams    { get; private set; } = new List<VideoStream>();
        public List<SubtitlesStream>    SubtitlesStreams{ get; private set; } = new List<SubtitlesStream>();
        public List<int>                EnabledStreams  { get; private set; } = new List<int>();

        // Media Packets
        public ConcurrentQueue<IntPtr>  AudioPackets    { get; private set; } = new ConcurrentQueue<IntPtr>();
        public ConcurrentQueue<IntPtr>  VideoPackets    { get; private set; } = new ConcurrentQueue<IntPtr>();
        public ConcurrentQueue<IntPtr>  SubtitlesPackets{ get; private set; } = new ConcurrentQueue<IntPtr>();
        public ConcurrentQueue<IntPtr>  CurPackets      { get; private set; }
        public ConcurrentQueue<IntPtr>  GetPacketsPtr(MediaType type) 
            { return type == MediaType.Audio ? AudioPackets : (type == MediaType.Video ? VideoPackets : SubtitlesPackets); }

        public int                      DemuxInterrupt      { get; internal set; }

        internal IntPtr handle;

        Thread          thread;
        AutoResetEvent  threadARE = new AutoResetEvent(false);
        long            threadCounter;

        AVFormatContext*fmtCtx;
        AVPacket*       packet;
        object          lockFmtCtx = new object();

        DecoderContext  decCtx;
        Config          cfg     => decCtx.cfg;

        AVIOInterruptCB_callback_func   interruptClbk = new AVIOInterruptCB_callback_func();     
        AVIOInterruptCB_callback        InterruptClbk = (opaque) =>
        {
            GCHandle demuxerHandle = (GCHandle)((IntPtr)opaque);
            DemuxerBase demuxer = (DemuxerBase)demuxerHandle.Target;

            int interrupt = demuxer.DemuxInterrupt != 0 || demuxer.decCtx.player.Status == MediaPlayer.Status.Stopped || demuxer.Status == Status.Stopping || demuxer.Status == Status.Stopped ? 1 : 0;
            
            //if (demuxer.DemuxInterrupt == 1) demuxer.Log("Interrupt 1");
            //if (interrupt == 1) demuxer.Log("Interrupt");

            return interrupt;
        };
        
        public DemuxerBase(DecoderContext decCtx)
        {
            this.decCtx = decCtx;

            if (this is VideoDemuxer)
                Type = MediaType.Video;
            else if (this is AudioDemuxer)
                Type = MediaType.Audio;
            else if (this is SubtitlesDemuxer)
                Type = MediaType.Subs;

            CurPackets = GetPacketsPtr(Type);

            interruptClbk.Pointer = Marshal.GetFunctionPointerForDelegate(InterruptClbk);
            CustomIOContext = new CustomIOContext(this);

            GCHandle demuxerHandle = GCHandle.Alloc(this);
            handle = (IntPtr) demuxerHandle;
        }

        public int Open(string url)     { return Open(url, null,    cfg.demuxer.GetFormatOptPtr(Type)); }
        public int Open(Stream stream)  { return Open(null, stream, cfg.demuxer.GetFormatOptPtr(Type)); }
        public int Open(string url, Stream stream, Dictionary<string, string> opt)
        {
            int ret = -1;
            Status = Status.Opening;

            try
            {
                // Parse Options to AV Dictionary Format Options
                AVDictionary *avopt = null;
                foreach (var optKV in opt) av_dict_set(&avopt, optKV.Key, optKV.Value, 0);

                // Allocate / Prepare Format Context
                fmtCtx = avformat_alloc_context();
                fmtCtx->interrupt_callback.callback = interruptClbk;
                fmtCtx->interrupt_callback.opaque = (void*)handle;
                fmtCtx->flags |= AVFMT_FLAG_DISCARD_CORRUPT;
                if (stream != null)
                    CustomIOContext.Initialize(stream);

                // Open Format Context
                AVFormatContext* fmtCtxPtr = fmtCtx;
                ret = avformat_open_input(&fmtCtxPtr, stream == null ? url : null, null, &avopt);
                if (ret < 0) { Log($"[Format] [ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); return ret; }

                // Find Streams Info
                ret = avformat_find_stream_info(fmtCtx, null);
                if (ret < 0) { Log($"[Format] [ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); avformat_close_input(&fmtCtxPtr); return ret; }
                FillInfo();

                StartThread();

                if (ret > 0) ret = 0;
            }
            finally { if (Status == Status.Opening) Status = Status.Stopped; }

            packet = av_packet_alloc();

            return ret; // 0 for success
        }

        public void FillInfo()
        {
            Name        = Utils.BytePtrToStringUTF8(fmtCtx->iformat->name);
            LongName    = Utils.BytePtrToStringUTF8(fmtCtx->iformat->long_name);
            Extensions  = Utils.BytePtrToStringUTF8(fmtCtx->iformat->extensions);
            StartTime   = fmtCtx->start_time * 10;
            Duration    = fmtCtx->duration   * 10;

            if (Type != MediaType.Video && Duration <= 0) Duration = decCtx.VideoDemuxer.Duration;
            //Streams     = new StreamBase[fmtCtx->nb_streams];

            for (int i=0; i<fmtCtx->nb_streams; i++)
            {
                fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

                switch (fmtCtx->streams[i]->codecpar->codec_type)
                {
                    case AVMEDIA_TYPE_AUDIO:
                        AudioStreams.Add(new AudioStream(fmtCtx->streams[i]) { Demuxer = this });
                        break;
                    case AVMEDIA_TYPE_VIDEO:
                        VideoStreams.Add(new VideoStream(fmtCtx->streams[i]) { Demuxer = this });
                        break;
                    case AVMEDIA_TYPE_SUBTITLE:
                        SubtitlesStreams.Add(new SubtitlesStream(fmtCtx->streams[i]) { Demuxer = this, Converted = true, Downloaded = true });
                        break;
                }
            }

            PrintDump();
        }
        public void PrintDump()
        {
            string dump = $"\r\n[Format  ] {LongName}/{Name} | {Extensions} {new TimeSpan(StartTime)}/{new TimeSpan(Duration)}";

            foreach(var stream in VideoStreams)     dump += "\r\n" + stream.GetDump();
            foreach(var stream in AudioStreams)     dump += "\r\n" + stream.GetDump();
            foreach(var stream in SubtitlesStreams) dump += "\r\n" + stream.GetDump();
            Log(dump);
        }

        public void EnableStream(StreamBase stream)
        {
            if (stream == null || EnabledStreams.Contains(stream.StreamIndex)) return;

            DisposePackets(GetPacketsPtr(stream.Type));

            EnabledStreams.Add(stream.StreamIndex);
            fmtCtx->streams[stream.StreamIndex]->discard = AVDiscard.AVDISCARD_DEFAULT;
            stream.InUse = true;

            Log($"[{stream.Type} #{stream.StreamIndex}] Enabled");
        }
        public void DisableStream(StreamBase stream)
        {
            if (stream == null || !EnabledStreams.Contains(stream.StreamIndex)) return;

            fmtCtx->streams[stream.StreamIndex]->discard = AVDiscard.AVDISCARD_ALL;
            EnabledStreams.Remove(stream.StreamIndex);
            stream.InUse = false;

            //DisposePackets(GetPacketsPtr(stream.Type)); // That causes 1-2 seconds delay in av_read_frame ??
            Log($"[{stream.Type} #{stream.StreamIndex}] Disabled");
        }

        public void StartThread()
        {
            if (thread != null && thread.IsAlive) return;

            Status = Status.Opening;
            thread = new Thread(() => Demux());
            thread.Name = $"[#{decCtx.player.PlayerId}] [Demuxer: {Type}]"; thread.IsBackground= true; thread.Start();
            while (Status == Status.Opening) Thread.Sleep(5); // Wait for thread to come up
        }
        public void Start()
        {
            if (Status != Status.Stopped && (thread == null || !thread.IsAlive)) StartThread();
            if (Status != Status.Paused) return;

            long prev = threadCounter;
            threadARE.Set();
            while (prev == threadCounter) Thread.Sleep(5);
        }
        public void Pause()
        {
            if (!IsRunning) return;

            Status = Status.Pausing;
            while (Status == Status.Pausing) Thread.Sleep(5);
        }
        public void Stop()
        {
            if (Status == Status.Stopped) return;

            StopThread();

            // Free Streams
            AudioStreams.Clear();
            VideoStreams.Clear();
            SubtitlesStreams.Clear();
            EnabledStreams.Clear();

            // Free Packets
            DisposePackets(AudioPackets);
            DisposePackets(VideoPackets);
            DisposePackets(SubtitlesPackets);
            if (packet != null) fixed (AVPacket** ptr = &packet) av_packet_free(ptr);

            // Close Format / Custom Contexts
            if (fmtCtx != null) fixed (AVFormatContext** ptr = &fmtCtx) avformat_close_input(ptr);
            CustomIOContext.Dispose();

            Status = Status.Stopped;
        }
        public void StopThread()
        {
            if (thread == null || !thread.IsAlive) return;

            Status = Status.Stopping;
            threadARE.Set();
            while (Status == Status.Stopping) Thread.Sleep(5);
        }

        public int Seek(long ms, bool foreward = false)
        {
            //Log($"[SEEK({(foreward ? "->" : "<-")})] Requested at {new TimeSpan(ms * (long)10000)}");

            if (Status == Status.Stopped) return -1;

            // TBR...
            //if (status == Status.Ended) { if (fmtCtx->pb == null) Open(url, decCtx.cfg.audio.Enabled, false); else status = Status.Paused; } //Open(url, ...); // Usefull for HTTP

            int ret;
            long seekTs = CalcSeekTimestamp(ms, ref foreward);

            // Interrupt av_read_frame (will cause AVERROR_EXITs)
            DemuxInterrupt = 1;
            lock (lockFmtCtx)
            {
                DemuxInterrupt = 0;

                // Free Packets
                DisposePackets(AudioPackets);
                DisposePackets(VideoPackets);
                DisposePackets(SubtitlesPackets);

                if (Type == MediaType.Video)
                {
                    ret =  av_seek_frame(fmtCtx, -1, seekTs / 10, foreward ? AVSEEK_FLAG_FRAME : AVSEEK_FLAG_BACKWARD);
                } 
                else
                {
                    ret = foreward ?
                        avformat_seek_file(fmtCtx, -1, seekTs / 10,     seekTs / 10, Int64.MaxValue, AVSEEK_FLAG_ANY):
                        avformat_seek_file(fmtCtx, -1, Int64.MinValue,  seekTs / 10, seekTs / 10,    AVSEEK_FLAG_ANY);
                }

                if (ret < 0)
                {
                    Log($"[SEEK] Failed 1/2 (retrying) {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                    ret = av_seek_frame(fmtCtx, -1, seekTs / 10, foreward ? AVSEEK_FLAG_BACKWARD : AVSEEK_FLAG_FRAME);
                    if (ret < 0) Log($"[SEEK] Failed 2/2 {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                }
            }

            return ret;  // >= 0 for success
        }
        public long CalcSeekTimestamp(long ms, ref bool foreward)
        {
            long ticks = ((ms * 10000) + StartTime);

            if (Type == MediaType.Audio) ticks -= (decCtx.cfg.audio.DelayTicks + decCtx.cfg.audio.LatencyTicks);
            if (Type == MediaType.Subs ) ticks -=  decCtx.cfg.subs. DelayTicks;

            if (ticks < StartTime) 
            {
                ticks = StartTime;
                foreward = true;
            }
            else if (ticks >= StartTime + Duration) 
            {
                ticks = StartTime + Duration;
                foreward = false;
            }

            return ticks;
        }

        private void Demux()
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
                Status = Status.Demuxing;
                Log($"{Status}");

                threadCounter++;
                int ret = 0;
                int allowedErrors = cfg.demuxer.MaxErrors;
                bool gotAVERROR_EXIT = false;

                while (Status == Status.Demuxing || Status == Status.Seeking)
                {
                    // Wait until Queue not Full or Stopped
                    if (CurPackets.Count == cfg.demuxer.MaxQueueSize)
                    {
                        Status = Status.QueueFull;

                        while (CurPackets.Count == cfg.demuxer.MaxQueueSize && Status == Status.QueueFull) Thread.Sleep(20);
                        if (Status != Status.QueueFull) break;
                        Status = Status.Demuxing;
                    } // While Queue Full
                    else if (gotAVERROR_EXIT)
                    {
                        gotAVERROR_EXIT = false;
                        Thread.Sleep(5); // Possible come from seek or Player's stopping...
                    }

                    lock (lockFmtCtx)
                    {
                        // Demux Packet
                        ret = av_read_frame(fmtCtx, packet);

                        // Check for Errors / End
                        if (ret != 0)
                        {
                            av_packet_unref(packet);

                            if (ret == AVERROR_EXIT)
                            {
                                //Log("AVERROR_EXIT");
                                if (DemuxInterrupt != 0) allowedErrors--;
                                if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); break; }
                                gotAVERROR_EXIT = true;
                                continue;
                            }

                            if (ret == AVERROR_EOF)
                                Status = Status.Ended;
                            else
                                Log($"[ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                            break;
                        }

                        // Skip Disabled Streams
                        if (!EnabledStreams.Contains(packet->stream_index)) { av_packet_unref(packet); continue; }

                        // Enqueue Packet
                        switch (fmtCtx->streams[packet->stream_index]->codecpar->codec_type)
                        {
                            case AVMEDIA_TYPE_AUDIO:
                                AudioPackets.Enqueue((IntPtr)packet);
                                packet = av_packet_alloc();

                                break;

                            case AVMEDIA_TYPE_VIDEO:
                                VideoPackets.Enqueue((IntPtr)packet);
                                packet = av_packet_alloc();
                            
                                break;

                            case AVMEDIA_TYPE_SUBTITLE:
                                SubtitlesPackets.Enqueue((IntPtr)packet);
                                packet = av_packet_alloc();
                            
                                break;

                            default:
                                av_packet_unref(packet);
                                break;
                        }
                    }

                } // While Demuxing

                IsRunning = false;
                Log($"{Status}");

            } // While !stopThread

            if (Status != Status.Ended) Status = Status.Stopped;
            Log($"[Thread] Stopped ({Status})");
        }

        public void DisposePackets(ConcurrentQueue<IntPtr> packets)
        {
            while (!packets.IsEmpty)
            {
                packets.TryDequeue(out IntPtr packetPtr);
                if (packetPtr == IntPtr.Zero) continue;
                AVPacket* packet = (AVPacket*)packetPtr;
                av_packet_free(&packet);
            }
        }
        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{decCtx.player.PlayerId}] [Demuxer: {Type.ToString().PadLeft(5, ' ')}] {msg}"); }
    }

    public enum Status
    {
        Stopping,
        Stopped,

        Opening,
        Pausing,
        Paused,

        Seeking,

        Demuxing,
        QueueFull,

        Ended
    }
}