using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.ffmpegEx;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaRemuxer;

namespace FlyleafLib.MediaFramework.MediaDemuxer
{
    public unsafe class Demuxer : RunThreadBase
    { 
        public MediaType                Type            { get; private set; }
        public Config.Demuxer           Config          { get; set; }

        // Format Info
        public string                   Url             { get; private set; }
        public string                   Name            { get; private set; }
        public string                   LongName        { get; private set; }
        public string                   Extensions      { get; private set; }
        public long                     StartTime       { get; private set; }
        public long                     CurTimeLive     { get; private set; }
        public long                     Duration        { get; private set; }
        public bool                     IsLive          {
            get => _IsLive;
            set => Set(ref _IsLive, value);
        }
        bool _IsLive;

        public AVFormatContext*         FormatContext   => fmtCtx;
        public CustomIOContext          CustomIOContext { get; private set; }

        // Media Programs
        public int[][]                  Programs        { get; private set; } = new int[0][];

        // Media Streams
        public List<AudioStream>        AudioStreams    { get; private set; } = new List<AudioStream>();
        public List<VideoStream>        VideoStreams    { get; private set; } = new List<VideoStream>();
        public List<SubtitlesStream>    SubtitlesStreams{ get; private set; } = new List<SubtitlesStream>();
        public List<int>                EnabledStreams  { get; private set; } = new List<int>();
        public Dictionary<int, StreamBase> 
                                        AVStreamToStream{ get; private set; }

        public AudioStream              AudioStream     { get; private set; }
        public VideoStream              VideoStream     { get; private set; }
        public SubtitlesStream          SubtitlesStream { get; private set; }
        

        // Media Packets
        public ConcurrentQueue<IntPtr>  Packets         { get; private set; } = new ConcurrentQueue<IntPtr>();
        public ConcurrentQueue<IntPtr>  AudioPackets    { get; private set; } = new ConcurrentQueue<IntPtr>();
        public ConcurrentQueue<IntPtr>  VideoPackets    { get; private set; } = new ConcurrentQueue<IntPtr>();
        public ConcurrentQueue<IntPtr>  SubtitlesPackets{ get; private set; } = new ConcurrentQueue<IntPtr>();
        public ConcurrentQueue<IntPtr>  CurPackets      { get; private set; }
        public bool                     UseAVSPackets   { get; private set; }
        public ConcurrentQueue<IntPtr>  GetPacketsPtr(MediaType type) 
            { if (!UseAVSPackets) return Packets; return type == MediaType.Audio ? AudioPackets : (type == MediaType.Video ? VideoPackets : SubtitlesPackets); }

        // Stats
        public long                     TotalBytes      { get; private set; } = 0;
        public long                     VideoBytes      { get; private set; } = 0;
        public long                     AudioBytes      { get; private set; } = 0;

        // Interrupt
        public Interrupter              Interrupter     { get; private set; }

        AVFormatContext*            fmtCtx;
        public HLSContext*          hlsCtx;
        long                        hlsPrevFirstTimestamp = -1;
        internal GCHandle           handle;
        internal object             lockFmtCtx  = new object();
        object                      lockHLSTime = new object();

        public Demuxer(Config.Demuxer config, MediaType type = MediaType.Video, int uniqueId = -1, bool useAVSPackets = true) : base(uniqueId)
        {
            Config          = config;
            Type            = type;
            UseAVSPackets   = useAVSPackets;

            CurPackets      = GetPacketsPtr(Type);

            Recorder        = new Remuxer(UniqueId);
            Interrupter     = new Interrupter();
            CustomIOContext = new CustomIOContext(this);

            threadName = $"Demuxer: {Type.ToString().PadLeft(5, ' ')}";
        }

        public int Open(string url)     { return Open(url, null,    Config.FormatOpt, Config.FormatFlags); }
        public int Open(Stream stream)  { return Open(null, stream, Config.FormatOpt, Config.FormatFlags); }
        public int Open(string url, Stream stream, Dictionary<string, string> opt, int flags)
        {
            //lock (lockFmtCtx)
            lock (lockActions) 
            {
                Dispose();

                int ret = -1;
                Url = url;

                try
                {
                    Status = Status.Opening;
                    if (!handle.IsAllocated) handle = GCHandle.Alloc(this);

                    // Parse Options to AV Dictionary Format Options
                    AVDictionary *avopt = null;
                    foreach (var optKV in opt)
                        av_dict_set(&avopt, optKV.Key, optKV.Value, 0);

                    // Allocate / Prepare Format Context
                    fmtCtx = avformat_alloc_context();
                    fmtCtx->interrupt_callback.callback = Interrupter.GetCallBackFunc();
                    fmtCtx->interrupt_callback.opaque = (void*) GCHandle.ToIntPtr(handle);
                    fmtCtx->flags |= flags;

                    if (stream != null)
                        CustomIOContext.Initialize(stream);
                    
                    // Open Format Context
                    AVFormatContext* fmtCtxPtr = fmtCtx;
                    Interrupter.Request(Requester.Open);
                    ret = avformat_open_input(&fmtCtxPtr, stream == null ? url : null, null, &avopt);
                    if (ret == AVERROR_EXIT || Status != Status.Opening || Interrupter.ForceInterrupt == 1) { Log("[Format] [ERROR-10] Cancelled"); fmtCtx = null; return ret = -10; }
                    if (ret < 0) { Log($"[Format] [ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); fmtCtx = null; return ret; }

                    // Find Streams Info
                    ret = avformat_find_stream_info(fmtCtx, null);
                    if (ret == AVERROR_EXIT || Status != Status.Opening || Interrupter.ForceInterrupt == 1) { Log("[Format] [ERROR-10] Cancelled"); return ret = -10; }
                    if (ret < 0) { Log($"[Format] [ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); return ret; }

                    bool hasVideo = FillInfo();

                    if (Type == MediaType.Video && !hasVideo) 
                        { Log($"[Format] [ERROR-3] No video stream found");     return ret = -3; }
                    else if (Type == MediaType.Audio && AudioStreams.Count == 0)
                        { Log($"[Format] [ERROR-4] No audio stream found");     return ret = -4; }
                    else if (Type == MediaType.Subs && SubtitlesStreams.Count == 0)
                        { Log($"[Format] [ERROR-5] No subtitles stream found"); return ret = -5; }

                    Interrupter.AllowInterrupts = Config.AllowInterrupts && !Config.ExcludeInterruptFmts.Contains(Name);
                    Disposed = false;
                    Status = Status.Stopped;

                    return ret = 0;
                }
                finally
                {
                    if (ret != 0)
                        Dispose();
                }
            }
        }

        private void UpdateHLSTime()
        {
            if (Type == MediaType.Video && VideoStream != null && VideoStream.HLSPlaylist != null && hlsPrevFirstTimestamp != hlsCtx->first_timestamp)
            {
                // TBR: lockFmtCtx is required (for dynamic segments[])

                hlsPrevFirstTimestamp = hlsCtx->first_timestamp;

                long curTimeLive= 0;
                long duration   = 0;
                for (int i=0; i<VideoStream.HLSPlaylist->n_segments; i++)
                {
                    if (i<=VideoStream.HLSPlaylist->cur_seq_no - VideoStream.HLSPlaylist->start_seq_no )
                        curTimeLive += VideoStream.HLSPlaylist->segments[i]->duration;
                    duration += VideoStream.HLSPlaylist->segments[i]->duration;
                }

                CurTimeLive = curTimeLive * 10;
                Duration    = duration * 10;

                if (VideoStream.HLSPlaylist->finished == 1) IsLive = false;
            }
        }
        private bool FillInfo()
        {
            Name        = Utils.BytePtrToStringUTF8(fmtCtx->iformat->name);
            LongName    = Utils.BytePtrToStringUTF8(fmtCtx->iformat->long_name);
            Extensions  = Utils.BytePtrToStringUTF8(fmtCtx->iformat->extensions);
            StartTime   = fmtCtx->start_time != AV_NOPTS_VALUE ? fmtCtx->start_time * 10 : 0;
            Duration    = fmtCtx->duration > 0 ? fmtCtx->duration * 10 : 0;

            if (Duration == 0 && Name == "hls" && Environment.Is64BitProcess)
            {
                hlsCtx = (HLSContext*) fmtCtx->priv_data;
                StartTime = 0;
                //UpdateHLSTime(); Maybe with default 0 playlist
            }

            IsLive = Duration == 0 || hlsCtx != null;

            bool hasVideo = false;
            AVStreamToStream = new Dictionary<int, StreamBase>();

            for (int i=0; i<fmtCtx->nb_streams; i++)
            {
                fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

                switch (fmtCtx->streams[i]->codecpar->codec_type)
                {
                    case AVMEDIA_TYPE_AUDIO:
                        AudioStreams.Add(new AudioStream(this, fmtCtx->streams[i]));
                        AVStreamToStream.Add(i, AudioStreams[AudioStreams.Count-1]);
                        break;

                    case AVMEDIA_TYPE_VIDEO:
                        // Might excludes valid video streams for rtsp/web cams? Better way to ensure that they are actually image streams? (fps maybe?)
                        if (avcodec_get_name(fmtCtx->streams[i]->codecpar->codec_id) == "mjpeg") { Log($"Excluding image stream #{i}"); continue; }

                        VideoStreams.Add(new VideoStream(this, fmtCtx->streams[i]));
                        AVStreamToStream.Add(i, VideoStreams[VideoStreams.Count-1]);
                        if (VideoStreams[VideoStreams.Count-1].PixelFormat != AVPixelFormat.AV_PIX_FMT_NONE) hasVideo = true;
                        break;

                    case AVMEDIA_TYPE_SUBTITLE:
                        SubtitlesStreams.Add(new SubtitlesStream(this, fmtCtx->streams[i]) { Converted = true, Downloaded = true });
                        AVStreamToStream.Add(i, SubtitlesStreams[SubtitlesStreams.Count-1]);
                        break;

                    default:
                        Log($"#[Unknown #{i}] {fmtCtx->streams[i]->codecpar->codec_type}");
                        break;
                }
            }

            Programs = new int[0][];
            if (fmtCtx->nb_programs > 0)
            {
                Programs = new int[fmtCtx->nb_programs][];
                for (int i=0; i<fmtCtx->nb_programs; i++)
                {
                    fmtCtx->programs[i]->discard = AVDiscard.AVDISCARD_ALL;
                    Programs[i] = new int[fmtCtx->programs[i]->nb_stream_indexes];

                    for (int l=0; l<Programs[i].Length; l++)
                        Programs[i][l] = (int) fmtCtx->programs[i]->stream_index[l];
                }
            }

            PrintDump();
            return hasVideo;
        }
        private void PrintDump()
        {
            string dump = $"\r\n[Format  ] {LongName}/{Name} | {Extensions} {new TimeSpan(StartTime)}/{new TimeSpan(Duration)} | [Seekable: {(fmtCtx->ctx_flags & AVFMTCTX_UNSEEKABLE) == 0}]";

            foreach(var stream in VideoStreams)     dump += "\r\n" + stream.GetDump();
            foreach(var stream in AudioStreams)     dump += "\r\n" + stream.GetDump();
            foreach(var stream in SubtitlesStreams) dump += "\r\n" + stream.GetDump();

            if (fmtCtx->nb_programs > 0)
                dump += $"\r\n[Programs] {fmtCtx->nb_programs}";

            for (int i=0; i<fmtCtx->nb_programs; i++)
            {
                dump += $"\r\n\tProgram #{i}";

                if (fmtCtx->programs[i]->nb_stream_indexes > 0)
                    dump += $"\r\n\t\tStreams [{fmtCtx->programs[i]->nb_stream_indexes}]: ";

                for (int l=0; l<fmtCtx->programs[i]->nb_stream_indexes; l++)
                    dump += $"{fmtCtx->programs[i]->stream_index[l]},";

                if (fmtCtx->programs[i]->nb_stream_indexes > 0)
                    dump = dump.Substring(0, dump.Length - 1);
            }

            Log(dump);
        }

        public bool IsProgramEnabled(StreamBase stream)
        {
            for (int i=0; i<Programs.Length; i++)
                for (int l=0; l<Programs[i].Length; l++)
                    if (Programs[i][l] == stream.StreamIndex && fmtCtx->programs[i]->discard != AVDiscard.AVDISCARD_ALL)
                        return true;

            return false;
        }
        public void EnableProgram(StreamBase stream)
        {
            if (IsProgramEnabled(stream))
            {
                Log($"[Stream #{stream.StreamIndex}] Program already enabled");
                return;
            }

            for (int i=0; i<Programs.Length; i++)
                for (int l=0; l<Programs[i].Length; l++)
                    if (Programs[i][l] == stream.StreamIndex)
                    {
                        Log($"[Stream #{stream.StreamIndex}] Enables program #{i}");
                        fmtCtx->programs[i]->discard = AVDiscard.AVDISCARD_DEFAULT;
                        return;
                    }

        }
        public void DisableProgram(StreamBase stream)
        {
            for (int i=0; i<Programs.Length; i++)
                for (int l=0; l<Programs[i].Length; l++)
                    if (Programs[i][l] == stream.StreamIndex && fmtCtx->programs[i]->discard != AVDiscard.AVDISCARD_ALL)
                    {
                        bool isNeeded = false;
                        for (int l2=0; l2<Programs[i].Length; l2++)
                        {
                            if (Programs[i][l2] != stream.StreamIndex && EnabledStreams.Contains(Programs[i][l2]))
                                {isNeeded = true; break; }
                        }

                        if (!isNeeded)
                        {
                            Log($"[Stream #{stream.StreamIndex}] Disables program #{i}");
                            fmtCtx->programs[i]->discard = AVDiscard.AVDISCARD_ALL;
                        }
                        else
                            Log($"[Stream #{stream.StreamIndex}] Program #{i} is needed");
                    }
                        
        }

        public void EnableStream(StreamBase stream)
        {
            //lock (lockFmtCtx)
            lock (lockActions)
            {
                if (Disposed || stream == null || EnabledStreams.Contains(stream.StreamIndex)) return;

                EnabledStreams.Add(stream.StreamIndex);
                fmtCtx->streams[stream.StreamIndex]->discard = AVDiscard.AVDISCARD_DEFAULT;
                stream.InUse = true;
                EnableProgram(stream);

                switch (stream.Type)
                {
                    case MediaType.Audio:
                        AudioStream = (AudioStream) stream;
                        break;

                    case MediaType.Video:
                        VideoStream = (VideoStream) stream;
                        UpdateHLSTime();
                        break;

                    case MediaType.Subs:
                        SubtitlesStream = (SubtitlesStream) stream;
                        break;
                }

                Log($"[{stream.Type} #{stream.StreamIndex}] Enabled");
            }
        }
        public void DisableStream(StreamBase stream)
        {
            //lock (lockFmtCtx)
            lock (lockActions)
            {
                if (Disposed || stream == null || !EnabledStreams.Contains(stream.StreamIndex)) return;

                fmtCtx->streams[stream.StreamIndex]->discard = AVDiscard.AVDISCARD_ALL;
                EnabledStreams.Remove(stream.StreamIndex);
                stream.InUse = false;
                DisableProgram(stream);

                switch (stream.Type)
                {
                    case MediaType.Audio:
                        AudioStream = null;
                        break;

                    case MediaType.Video:
                        VideoStream = null;
                        break;

                    case MediaType.Subs:
                        SubtitlesStream = null;
                        break;
                }

                DisposePackets(GetPacketsPtr(stream.Type)); // That causes 1-2 seconds delay in av_read_frame ?? (should be fixed with hls patch)
                Log($"[{stream.Type} #{stream.StreamIndex}] Disabled");
            }
        }

        public int Seek(long ticks, bool foreward = false)
        {
            lock (lockActions)
            {
                Log($"[SEEK({(foreward ? "->" : "<-")})] Requested at {new TimeSpan(ticks)}");

                if (Disposed) return -1;
                if (Status == Status.Ended) Status = Status.Stopped;

                int ret;

                Interrupter.ForceInterrupt = 1;
                lock (lockFmtCtx)
                {
                    Interrupter.ForceInterrupt = 0;

                    //if (hlsCtx != null && VideoStream != null) Log($"1 Seq. [Cur: {VideoStream.HLSPlaylist->cur_seq_no}]");
                    if (hlsCtx != null) fmtCtx->ctx_flags &= ~AVFMTCTX_UNSEEKABLE;
                    Interrupter.Request(Requester.Seek);
                    if (Type == MediaType.Video)
                    {
                        ret = av_seek_frame(fmtCtx, -1, ticks / 10, foreward ? AVSEEK_FLAG_FRAME : AVSEEK_FLAG_BACKWARD); // AVSEEK_FLAG_BACKWARD will not work on .dav even if it returns 0 (it will work after it fills the index table)
                    }
                    else
                    {
                        ret = foreward ?
                            avformat_seek_file(fmtCtx, -1, ticks / 10,     ticks / 10, Int64.MaxValue, AVSEEK_FLAG_ANY):
                            avformat_seek_file(fmtCtx, -1, Int64.MinValue, ticks / 10, ticks / 10,    AVSEEK_FLAG_ANY);
                    }

                    if (ret < 0)
                    {
                        if (hlsCtx != null) fmtCtx->ctx_flags &= ~AVFMTCTX_UNSEEKABLE;
                        Log($"[SEEK] Failed 1/2 (retrying) {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                        ret = av_seek_frame(fmtCtx, -1, ticks / 10, foreward ? AVSEEK_FLAG_BACKWARD : AVSEEK_FLAG_FRAME);
                        if (ret < 0) Log($"[SEEK] Failed 2/2 {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                    }
                    
                    DisposePackets();
                    hlsPrevFirstTimestamp = -1;
                    UpdateHLSTime();
                    //if (hlsCtx != null && VideoStream != null) { Log($"2 Seq. [Cur: {VideoStream.HLSPlaylist->cur_seq_no}]"); hlsPrevFirstTimestamp = -1; }
                }

                return ret;  // >= 0 for success
            }
        }

        protected override void RunInternal()
        {
            int ret = 0;
            int allowedErrors = Config.MaxErrors;
            bool gotAVERROR_EXIT = false;

            AVPacket* packet = av_packet_alloc();
            do
            {
                // Wait until Queue not Full or Stopped
                if (CurPackets.Count >= Config.MaxQueueSize)
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueFull;

                    while (CurPackets.Count >= Config.MaxQueueSize && Status == Status.QueueFull) Thread.Sleep(10);

                    lock (lockStatus)
                    {
                        if (Status != Status.QueueFull) break;
                        Status = Status.Running;
                    }
                        
                }

                // Wait possible someone asks for lockFmtCtx
                else if (gotAVERROR_EXIT)
                {
                    gotAVERROR_EXIT = false;
                    Thread.Sleep(5); // Possible come from seek or Player's stopping...
                }
                    
                lock (lockFmtCtx)
                {
                    // Demux Packet
                    Interrupter.Request(Requester.Read);
                    ret = av_read_frame(fmtCtx, packet);
                    //long t1 = (DateTime.UtcNow.Ticks - interruptRequestedAt)/10000;
                    //if (t1 > 100) Log($"Took {t1}ms");
                }

                // Check for Errors / End
                if (Interrupter.ForceInterrupt != 0 && Config.AllowInterrupts) { av_packet_unref(packet); gotAVERROR_EXIT = true; continue; }

                // Possible check if interrupt/timeout and we dont seek to reset the backend pb->pos = 0?

                if (ret != 0)
                {
                    av_packet_unref(packet);

                    if ((ret == AVERROR_EXIT && fmtCtx->pb->eof_reached != 0) || ret == AVERROR_EOF) { Status = Status.Ended; break; }

                    allowedErrors--;
                    Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                    if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); Status = Status.Stopping; break; }

                    gotAVERROR_EXIT = true;
                    continue;
                }
                    
                TotalBytes += packet->size;

                // Skip Disabled Streams
                if (!EnabledStreams.Contains(packet->stream_index)) { av_packet_unref(packet); continue; }

                if (IsRecording)
                {
                    if (!recGotKeyframe && fmtCtx->streams[packet->stream_index]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO && (packet->flags & AV_PKT_FLAG_KEY) != 0)
                        recGotKeyframe = true;

                    if (recGotKeyframe && (fmtCtx->streams[packet->stream_index]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO || fmtCtx->streams[packet->stream_index]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO))
                        Recorder.Write(av_packet_clone(packet));
                }

                // Enqueue Packet (AVS Queue or Single Queue)
                if (UseAVSPackets)
                {
                    switch (fmtCtx->streams[packet->stream_index]->codecpar->codec_type)
                    {
                        case AVMEDIA_TYPE_AUDIO:
                            AudioBytes += packet->size;
                            AudioPackets.Enqueue((IntPtr)packet);
                            packet = av_packet_alloc();

                            break;

                        case AVMEDIA_TYPE_VIDEO:
                            VideoBytes += packet->size;
                            VideoPackets.Enqueue((IntPtr)packet);
                            packet = av_packet_alloc();

                            UpdateHLSTime();

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
                else
                {
                    Packets.Enqueue((IntPtr)packet);
                    packet = av_packet_alloc();
                }

            } while (Status == Status.Running);

            if (IsRecording) StopRecording();
        }

        public void DisposePackets()
        {
            if (UseAVSPackets)
            {
                DisposePackets(AudioPackets);
                DisposePackets(VideoPackets);
                DisposePackets(SubtitlesPackets);
            }
            else
                DisposePackets(Packets);
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
        public void Dispose()
        {
            if (Disposed) return;

            lock (lockActions)
            {
                if (Disposed) return;

                Stop();

                Url = null;
                hlsCtx = null;
                Programs = new int[0][];

                // Free Streams
                AudioStreams.Clear();
                VideoStreams.Clear();
                SubtitlesStreams.Clear();
                EnabledStreams.Clear();
                AudioStream = null;
                VideoStream = null;
                SubtitlesStream = null;

                DisposePackets();

                // Close Format / Custom Contexts
                if (fmtCtx != null)
                {
                    Interrupter.Request(Requester.Close);
                    fixed (AVFormatContext** ptr = &fmtCtx) { avformat_close_input(ptr); fmtCtx = null; }
                }

                CustomIOContext.Dispose();

                if (handle.IsAllocated) handle.Free();

                TotalBytes = 0; VideoBytes = 0; AudioBytes = 0;
                Status = Status.Stopped;
                Disposed = true;
                Log("Disposed");
            }
        }

        #region Recording
        Remuxer     Recorder;
        bool        recGotKeyframe;
        bool        _IsRecording;
        
        public bool IsRecording
        {
            get => _IsRecording;
            set => Set(ref _IsRecording, value);
        }
        
        public void StartRecording(string filename)
        {
            lock (lockFmtCtx)
            {
                if (IsRecording) StopRecording();

                Log("Record Start");
                Recorder.Open(filename);
                for(int i=0; i<EnabledStreams.Count; i++)
                    Log(Recorder.AddStream(fmtCtx->streams[EnabledStreams[i]]).ToString());
                Log("Record ok? " + Recorder.WriteHeader());
                recGotKeyframe = false;
                IsRecording = true;
            }
        }
        public void StopRecording()
        {
            lock (lockFmtCtx)
            {
                if (!IsRecording) return;

                Log("Record Completed");
                Recorder.Dispose();
                IsRecording = false;
            }
        }
        #endregion
    }
}