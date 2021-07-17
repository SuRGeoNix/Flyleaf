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

namespace FlyleafLib.MediaFramework.MediaDemuxer
{
    public abstract unsafe class DemuxerBase : NotifyPropertyChanged
    {
        public int                      UniqueId        { get ; set; }
        public Status                   Status          { get; internal set; } = Status.Stopped;
        public bool                     IsRunning       { get; private set; }
        public MediaType                Type            { get; private set; }

        // Format Info
        public string                   Name            { get; private set; }
        public string                   LongName        { get; private set; }
        public string                   Extensions      { get; private set; }
        public long                     StartTime       { get; private set; }
        public long                     Duration        { get => _Duration; set => Set(ref _Duration,  value); }
        long _Duration;

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

        internal GCHandle handle;

        Thread          thread;
        AutoResetEvent  threadARE = new AutoResetEvent(false);
        long            threadCounter;

        AVFormatContext*fmtCtx;
        AVPacket*       packet;
        internal object lockFmtCtx = new object();

        Config          cfg;

        AVIOInterruptCB_callback_func   interruptClbk = new AVIOInterruptCB_callback_func();     
        AVIOInterruptCB_callback        InterruptClbk = (opaque) =>
        {
            GCHandle demuxerHandle = (GCHandle)((IntPtr)opaque);
            DemuxerBase demuxer = (DemuxerBase)demuxerHandle.Target;

            int interrupt = demuxer.DemuxInterrupt != 0 || demuxer.Status == Status.Stopping || demuxer.Status == Status.Stopped ? 1 : 0;
            
            //if (demuxer.DemuxInterrupt == 1) demuxer.Log($"Interrupt 1 | {demuxer.Status}");
            //if (interrupt == 1) demuxer.Log("Interrupt");

            return interrupt;
        };
        
        public DemuxerBase(Config config, int uniqueId)
        {
            cfg     = config;
            UniqueId= uniqueId;

            if (this is VideoDemuxer)
                Type = MediaType.Video;
            else if (this is AudioDemuxer)
                Type = MediaType.Audio;
            else if (this is SubtitlesDemuxer)
                Type = MediaType.Subs;

            CurPackets = GetPacketsPtr(Type);

            interruptClbk.Pointer = Marshal.GetFunctionPointerForDelegate(InterruptClbk);
            CustomIOContext = new CustomIOContext(this);
        }

        public int Open(string url)     { return Open(url, null,    cfg.demuxer.GetFormatOptPtr(Type)); }
        public int Open(Stream stream)  { return Open(null, stream, cfg.demuxer.GetFormatOptPtr(Type)); }
        public int Open(string url, Stream stream, Dictionary<string, string> opt)
        {
            int ret = -1;
            Status = Status.Opening;

            try
            {
                if (!handle.IsAllocated) handle = GCHandle.Alloc(this);

                // Parse Options to AV Dictionary Format Options
                AVDictionary *avopt = null;
                foreach (var optKV in opt) av_dict_set(&avopt, optKV.Key, optKV.Value, 0);

                // Allocate / Prepare Format Context
                fmtCtx = avformat_alloc_context();
                fmtCtx->interrupt_callback.callback = interruptClbk;
                fmtCtx->interrupt_callback.opaque = (void*) GCHandle.ToIntPtr(handle);
                fmtCtx->flags |= AVFMT_FLAG_DISCARD_CORRUPT;
                if (stream != null)
                    CustomIOContext.Initialize(stream);

                // Open Format Context
                AVFormatContext* fmtCtxPtr = fmtCtx;
                lock (lockFmtCtx)
                { 
                    ret = avformat_open_input(&fmtCtxPtr, stream == null ? url : null, null, &avopt);
                    if (ret < 0) { Log($"[Format] [ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); fmtCtx = null; return ret; }
                }
                if (Status != Status.Opening) return -1;

                // Find Streams Info
                lock (lockFmtCtx)
                {
                    if (Status != Status.Opening) return -1;
                    ret = avformat_find_stream_info(fmtCtx, null);
                    if (ret < 0) { Log($"[Format] [ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); avformat_close_input(&fmtCtxPtr); fmtCtx = null; return ret; }
                }
                if (Status != Status.Opening) return -1;

                lock (lockFmtCtx)
                {
                    if (Status != Status.Opening) return -1;
                    bool hasVideo = FillInfo();

                    if (Type == MediaType.Video && !hasVideo) 
                        { Log($"[Format] [ERROR-3] No video stream found");     avformat_close_input(&fmtCtxPtr); fmtCtx = null; return -3; }
                    else if (Type == MediaType.Audio && AudioStreams.Count == 0)
                        { Log($"[Format] [ERROR-4] No audio stream found");     avformat_close_input(&fmtCtxPtr); fmtCtx = null; return -4; }
                    else if (Type == MediaType.Subs && SubtitlesStreams.Count == 0)
                        { Log($"[Format] [ERROR-5] No subtitles stream found"); avformat_close_input(&fmtCtxPtr); fmtCtx = null; return -5; }

                    StartThread();

                    if (ret > 0) ret = 0;
                }
            }
            finally { if (Status == Status.Opening) Status = Status.Stopped; }

            packet = av_packet_alloc();

            return ret; // 0 for success
        }

        public bool FillInfo()
        {
            Name        = Utils.BytePtrToStringUTF8(fmtCtx->iformat->name);
            LongName    = Utils.BytePtrToStringUTF8(fmtCtx->iformat->long_name);
            Extensions  = Utils.BytePtrToStringUTF8(fmtCtx->iformat->extensions);
            StartTime   = fmtCtx->start_time * 10;
            Duration    = fmtCtx->duration   * 10;

            bool hasVideo = false;
            mapInAVStreamsToStreams = new Dictionary<int, StreamBase>();

            for (int i=0; i<fmtCtx->nb_streams; i++)
            {
                fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

                switch (fmtCtx->streams[i]->codecpar->codec_type)
                {
                    case AVMEDIA_TYPE_AUDIO:
                        AudioStreams.Add(new AudioStream(fmtCtx->streams[i]) { Demuxer = this });
                        mapInAVStreamsToStreams.Add(i, AudioStreams[AudioStreams.Count-1]);
                        break;

                    case AVMEDIA_TYPE_VIDEO:
                        // Might excludes valid video streams for rtsp/web cams? Better way to ensure that they are actually image streams? (fps maybe?)
                        if (avcodec_get_name(fmtCtx->streams[i]->codecpar->codec_id) == "mjpeg") { Log($"Excluding image stream #{i}"); continue; }

                        VideoStreams.Add(new VideoStream(fmtCtx->streams[i]) { Demuxer = this });
                        mapInAVStreamsToStreams.Add(i, VideoStreams[VideoStreams.Count-1]);
                        if (VideoStreams[VideoStreams.Count-1].PixelFormat != AVPixelFormat.AV_PIX_FMT_NONE) hasVideo = true;
                        break;

                    case AVMEDIA_TYPE_SUBTITLE:
                        SubtitlesStreams.Add(new SubtitlesStream(fmtCtx->streams[i]) { Demuxer = this, Converted = true, Downloaded = true });
                        mapInAVStreamsToStreams.Add(i, SubtitlesStreams[SubtitlesStreams.Count-1]);
                        break;
                }
            }

            PrintDump();
            return hasVideo;
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
            thread.Name = $"[#{UniqueId}] [Demuxer: {Type}]"; thread.IsBackground= true; thread.Start();
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

        //[System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
        //[System.Security.SecurityCritical]
        public void Stop()
        {
            if (Status == Status.Stopped) return;
           
            StopThread();

            lock (lockFmtCtx)
            {
                // Free Streams
                AudioStreams.Clear();
                VideoStreams.Clear();
                SubtitlesStreams.Clear();
                EnabledStreams.Clear();

                // Free Packets
                DisposePackets(AudioPackets);
                DisposePackets(VideoPackets);
                DisposePackets(SubtitlesPackets);

                // Close Format / Custom Contexts
                if (fmtCtx != null)
                    fixed (AVFormatContext** ptr = &fmtCtx) { avformat_close_input(ptr); fmtCtx = null; }

                if (packet != null) fixed (AVPacket** ptr = &packet) av_packet_free(ptr);
                CustomIOContext.Dispose();

                if (handle.IsAllocated) handle.Free();

                Status = Status.Stopped;
            }
        }
        public void StopThread()
        {
            if (thread == null || !thread.IsAlive) return;

            Status = Status.Stopping;
            threadARE.Set();
            while (Status == Status.Stopping) Thread.Sleep(5);
        }

        public int Seek(long ticks, bool foreward = false)
        {
            //Log($"[SEEK({(foreward ? "->" : "<-")})] Requested at {new TimeSpan(ms * (long)10000)}");

            if (Status == Status.Stopped) return -1;

            // TBR...
            //if (status == Status.Ended) { if (fmtCtx->pb == null) Open(url, decCtx.cfg.audio.Enabled, false); else status = Status.Paused; } //Open(url, ...); // Usefull for HTTP

            int ret;

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
                    ret =  av_seek_frame(fmtCtx, -1, ticks / 10, foreward ? AVSEEK_FLAG_FRAME : AVSEEK_FLAG_BACKWARD); // AVSEEK_FLAG_BACKWARD will not work on .dav even it it returns 0
                }
                else
                {
                    ret = foreward ?
                        avformat_seek_file(fmtCtx, -1, ticks / 10,     ticks / 10, Int64.MaxValue, AVSEEK_FLAG_ANY):
                        avformat_seek_file(fmtCtx, -1, Int64.MinValue, ticks / 10, ticks / 10,    AVSEEK_FLAG_ANY);
                }

                if (ret < 0)
                {
                    Log($"[SEEK] Failed 1/2 (retrying) {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                    ret = av_seek_frame(fmtCtx, -1, ticks / 10, foreward ? AVSEEK_FLAG_BACKWARD : AVSEEK_FLAG_FRAME);
                    if (ret < 0) Log($"[SEEK] Failed 2/2 {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                }
            }
            
            return ret;  // >= 0 for success
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

                while (Status == Status.Demuxing)
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

                            if (ret == AVERROR_EXIT && DemuxInterrupt != 0)
                            {
                                gotAVERROR_EXIT = true;
                                continue;
                            }

                            Status = Status.Ended;

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

        #region Preparing Remuxing / Download (Should be transfered to a new MediaContext eg. DownloaderContext and include also the external demuxers - mainly for Youtube-dl)
        AVOutputFormat* oFmt;
        AVFormatContext *oFmtCtx;

        Dictionary<int, int>        mapInOutStreams;
        Dictionary<int, StreamBase> mapInAVStreamsToStreams;

        public double   DownloadPercentage    { get => _DownloadPercentage;     set => Set(ref _DownloadPercentage,  value); }
        double _DownloadPercentage;

        public long   CurTime    { get => _CurTime;     set => Set(ref _CurTime,  value); }
        long _CurTime;

        /// <summary>
        /// Fires on partial or full download completed
        /// </summary>
        public event EventHandler<bool> DownloadCompleted;
        protected virtual void OnDownloadCompleted(bool success) { System.Threading.Tasks.Task.Run(() => { Stop(); DownloadCompleted?.Invoke(this, success); }); }

        /// <summary>
        /// Downloads the currently configured AVS streams (Experimental)
        /// </summary>
        /// <param name="filename">The filename for the downloaded video. The file extension will let the demuxer to choose the output format (eg. mp4).</param>
        public void Download(string filename)
        {
            int ret;

            DownloadPercentage = 0;
            Log2("Output format initializing");
            ret = OpenOutputFormat(filename);
            if (ret < 0) { OnDownloadCompleted(false); return; }
            Log2("Output format initialized");
            StartDownloadThread();
            threadARE.Set();
        }
        public void StartDownloadThread()
        {
            if (thread != null && thread.IsAlive) StopThread();

            Status = Status.Opening;
            thread = new Thread(() => Remux());
            thread.Name = $"[#{UniqueId}] [Remuxer: {Type}]"; thread.IsBackground= true; thread.Start();
            while (Status == Status.Opening) Thread.Sleep(5); // Wait for thread to come up
        }
        private int OpenOutputFormat(string filename)
        {
            int ret;

            fixed (AVFormatContext** ptr = &oFmtCtx)
                ret = avformat_alloc_output_context2(ptr, null, null, filename);

            if (ret < 0) return ret;

            oFmt = oFmtCtx->oformat;
            mapInOutStreams = new Dictionary<int, int>();
            int outputStreamsCounter = 0;

            for (int i=0; i<EnabledStreams.Count; i++)
            {
                AVStream *out_stream;
                AVStream *in_stream = fmtCtx->streams[EnabledStreams[i]];
                AVCodecParameters *in_codecpar = in_stream->codecpar;
                out_stream = avformat_new_stream(oFmtCtx, null);
                ret = avcodec_parameters_copy(out_stream->codecpar, in_codecpar);
                if ((oFmt->flags & AVFMT_GLOBALHEADER) != 0)
#pragma warning disable CS0618 // Type or member is obsolete
                    out_stream->codec->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
#pragma warning restore CS0618 // Type or member is obsolete
                out_stream->codecpar->codec_tag = 0;
                mapInOutStreams.Add(EnabledStreams[i], outputStreamsCounter++);
            }

            ret = avio_open(&oFmtCtx->pb, filename, AVIO_FLAG_WRITE);
            if (ret < 0) { avformat_free_context(oFmtCtx); return ret; }

            ret = avformat_write_header(oFmtCtx, null);
            if (ret < 0) { avformat_free_context(oFmtCtx); return ret; }

            return ret;
        }
        private void CloseOutputFormat()
        {
            int ret = -1;
            ret = av_write_trailer(oFmtCtx);
            avio_closep(&oFmtCtx->pb);
            avformat_free_context(oFmtCtx);
            Log2("Output format closed");
            OnDownloadCompleted(ret == 0);
        }
        private void Remux()
        {
            Log2($"[Thread] Started");

            while (Status != Status.Stopped && Status != Status.Stopping && Status != Status.Ended)
            {
                threadARE.Reset();
                Status = Status.Paused;

                Log2($"{Status}");
                threadARE.WaitOne();
                if (Status == Status.Stopped || Status == Status.Stopping) break;

                IsRunning = true;
                Status = Status.Demuxing;
                Log2($"Remuxing");

                threadCounter++;
                int ret = 0;
                double downPercentageFactor = Duration / 100.0;

                AVStream* in_stream;
                AVStream* out_stream;
                AVPacket* packet;

                while (Status == Status.Demuxing)
                {
                    // Demux Packet
                    packet  = av_packet_alloc();
                    ret     = av_read_frame(fmtCtx, packet);

                    // Check for Errors / End
                    if (ret != 0)
                    {
                        av_packet_free(&packet);

                        if (Status == Status.Demuxing)
                            { Status = Status.Ended; DownloadPercentage = 100; }
                        
                        break; // Stopping | Pausing    
                    }

                    // Skip Disabled Streams
                    if (!EnabledStreams.Contains(packet->stream_index)) { av_packet_free(&packet); continue; }

                    in_stream       =  fmtCtx->streams[packet->stream_index];
                    out_stream      = oFmtCtx->streams[mapInOutStreams[packet->stream_index]];

                    if (packet->dts > 0 && fmtCtx->streams[packet->stream_index]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO)
                    {
                        double curTime = (packet->dts * mapInAVStreamsToStreams[in_stream->index].Timebase) - StartTime;
                        if (Duration > 0) DownloadPercentage = curTime / downPercentageFactor;
                        CurTime = (long) curTime;
                    }

                    // Fixes the StartTime/Duration (eg. for live streams) but changes the codec_tag / codec_id etc...
                    //packet->pts       = av_rescale_q_rnd(((long)(packet->pts * mapInAVStreamsToStreams[in_stream->index].Timebase) - StartTime) / 10, av_get_time_base_q(), out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                    //packet->dts       = av_rescale_q_rnd(((long)(packet->dts * mapInAVStreamsToStreams[in_stream->index].Timebase) - StartTime) / 10, av_get_time_base_q(), out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);

                    packet->pts       = av_rescale_q_rnd(packet->pts, in_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                    packet->dts       = av_rescale_q_rnd(packet->dts, in_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                    packet->duration  = av_rescale_q(packet->duration,in_stream->time_base, out_stream->time_base);
                    packet->stream_index = out_stream->index;
                    packet->pos       = -1;

                    ret = av_interleaved_write_frame(oFmtCtx, packet);
                    if (ret != 0) Log2("Writing packet failed");
                    av_packet_free(&packet);

                } // While Demuxing

                IsRunning = false;
                Log2($"{Status}");

            } // While !stopThread

            if (Status != Status.Ended) Status = Status.Stopped;
            Log2($"[Thread] Stopped ({Status})");
            CloseOutputFormat();
        }
        #endregion

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

        private void Log2(string msg)
        {
            #if DEBUG
                Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Remuxer: {Type.ToString().PadLeft(5, ' ')}] {msg}");
            #endif
        }
        private void Log (string msg)
        { 
            #if DEBUG
                Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Demuxer: {Type.ToString().PadLeft(5, ' ')}] {msg}");
            #endif
        }
    }

    public enum Status
    {
        Stopping,
        Stopped,

        Opening,
        Pausing,
        Paused,

        //Seeking,

        Demuxing,
        QueueFull,

        Ended
    }
}