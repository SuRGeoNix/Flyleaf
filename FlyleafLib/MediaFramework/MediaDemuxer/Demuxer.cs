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

using static FlyleafLib.Config;

namespace FlyleafLib.MediaFramework.MediaDemuxer
{
    public unsafe class Demuxer : RunThreadBase
    {
        #region Properties
        public MediaType                Type            { get; private set; }
        public DemuxerConfig            Config          { get; set; }

        // Format Info
        public string                   Url             { get; private set; }
        public string                   Name            { get; private set; }
        public string                   LongName        { get; private set; }
        public string                   Extensions      { get; private set; }
        public string                   Extension       { get; private set; }
        public long                     StartTime       { get; private set; }
        public long                     Duration        { get; private set; }
        public long                     EndTimeLive     { get; private set; }

        /// <summary>
        /// The time of first packet in the queue
        /// </summary>
        public long                     CurTime         { get; private set; }

        /// <summary>
        /// The buffered time in the queue (last packet time - first packet time)
        /// </summary>
        public long                     BufferedDuration{ get; private set; }

        public bool                     IsLive          { get; private set; }

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

        // Audio/Video Stream's HLSPlaylist
        internal HLSPlaylist*           HLSPlaylist     { get; private set; }
        
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
        #endregion

        #region Constructor / Declaration
        public AVPacket*        packet;
        AVFormatContext*        fmtCtx;
        internal HLSContext*    hlsCtx;
        long                    hlsPrevFirstTimestamp = -1;
        long                    lastKnownPtsTimestamp = AV_NOPTS_VALUE;

        internal GCHandle       handle;
        public object           lockFmtCtx  = new object();
        internal bool           allowReadInterrupts;

        public Demuxer(DemuxerConfig config, MediaType type = MediaType.Video, int uniqueId = -1, bool useAVSPackets = true) : base(uniqueId)
        {
            Config          = config;
            Type            = type;
            UseAVSPackets   = useAVSPackets;
            CurPackets      = Packets; // Will be updated on stream switch in case of AVS

            Recorder        = new Remuxer(UniqueId);
            Interrupter     = new Interrupter(this);
            CustomIOContext = new CustomIOContext(this);

            threadName = $"Demuxer: {Type.ToString().PadLeft(5, ' ')}";
        }
        #endregion

        #region Dispose / Dispose Packets
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

            BufferedDuration = 0;
            lastKnownPtsTimestamp = AV_NOPTS_VALUE;
            hlsPrevFirstTimestamp = -1;
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

                CurTime = 0;
                EndTimeLive = 0;

                // Free Streams
                AudioStreams.Clear();
                VideoStreams.Clear();
                SubtitlesStreams.Clear();
                EnabledStreams.Clear();
                AudioStream = null;
                VideoStream = null;
                SubtitlesStream = null;

                DisposePackets();

                if (fmtCtx != null)
                {
                    Interrupter.Request(Requester.Close);
                    fixed (AVFormatContext** ptr = &fmtCtx) { avformat_close_input(ptr); fmtCtx = null; }
                }

                if (packet != null) fixed (AVPacket** ptr = &packet) av_packet_free(ptr);

                CustomIOContext.Dispose();

                if (handle.IsAllocated) handle.Free();

                TotalBytes = 0; VideoBytes = 0; AudioBytes = 0;
                Status = Status.Stopped;
                Disposed = true;

                Log("Disposed");
            }
        }
        #endregion

        #region Open / Seek / Run
        public string Open(string url)     { return Open(url, null); }
        public string Open(Stream stream)  { return Open(null, stream); }
        public string Open(string url, Stream stream)
        {
            lock (lockActions) 
            {
                Dispose();

                if (String.IsNullOrEmpty(url) && stream == null) return "Invalid empty/null input";

                int ret = -1;
                string error = null;
                Url = url;

                try
                {
                    Disposed = false;
                    Status = Status.Opening;
                    if (!handle.IsAllocated) handle = GCHandle.Alloc(this);

                    // Parse Options to AV Dictionary Format Options
                    AVDictionary *avopt = null;

                    var curFormats = Type == MediaType.Video ? Config.FormatOpt : (Type == MediaType.Audio ? Config.AudioFormatOpt : Config.SubtitlesFormatOpt);
                    foreach (var optKV in curFormats)
                        av_dict_set(&avopt, optKV.Key, optKV.Value, 0);

                    // Allocate / Prepare Format Context
                    fmtCtx = avformat_alloc_context();
                    if (Config.AllowInterrupts)
                    {
                        fmtCtx->interrupt_callback.callback = Interrupter.GetCallBackFunc();
                        fmtCtx->interrupt_callback.opaque = (void*) GCHandle.ToIntPtr(handle);
                    }

                    fmtCtx->flags |= Config.FormatFlags;

                    if (stream != null)
                        CustomIOContext.Initialize(stream);

                    lock (lockFmtCtx)
                    {
                        allowReadInterrupts = true; // allow Open interrupts always

                        if (stream != null)
                            stream.Seek(0, SeekOrigin.Begin);

                        // Open Format Context
                        AVFormatContext* fmtCtxPtr = fmtCtx;
                        Interrupter.Request(Requester.Open);
                        ret = avformat_open_input(&fmtCtxPtr, stream == null ? url : null, null, &avopt);

                        if (ret == AVERROR_EXIT || Status != Status.Opening || Interrupter.ForceInterrupt == 1) { fmtCtx = null; return error = "Cancelled"; }
                        if (ret < 0) { fmtCtx = null; return error = $"[avformat_open_input] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"; }

                        // Find Streams Info
                        ret = avformat_find_stream_info(fmtCtx, null);
                        if (ret == AVERROR_EXIT || Status != Status.Opening || Interrupter.ForceInterrupt == 1) return error = "Cancelled";
                        if (ret < 0) return error = $"[avformat_find_stream_info] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})";
                    }

                    bool hasVideo = FillInfo();

                    if (Type == MediaType.Video && !hasVideo && AudioStreams.Count == 0) 
                        return error = $"No audio / video stream found";
                    else if (Type == MediaType.Audio && AudioStreams.Count == 0)
                        return error = $"No audio stream found";
                    else if (Type == MediaType.Subs && SubtitlesStreams.Count == 0)
                        return error = $"No subtitles stream found";

                    packet = av_packet_alloc();
                    Status = Status.Stopped;
                    allowReadInterrupts = Config.AllowReadInterrupts && !Config.ExcludeInterruptFmts.Contains(Name);

                    return null;
                }
                finally
                {
                    if (error != null)
                        Dispose();
                }
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
                        if ((fmtCtx->streams[i]->disposition & AV_DISPOSITION_ATTACHED_PIC) != 0) 
                            { Log($"Excluding image stream #{i}"); continue; }

                        VideoStreams.Add(new VideoStream(this, fmtCtx->streams[i]));
                        AVStreamToStream.Add(i, VideoStreams[VideoStreams.Count-1]);
                        if (VideoStreams[VideoStreams.Count-1].PixelFormat != AVPixelFormat.AV_PIX_FMT_NONE) hasVideo = true;
                        break;

                    case AVMEDIA_TYPE_SUBTITLE:
                        SubtitlesStreams.Add(new SubtitlesStream(this, fmtCtx->streams[i]));
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

            Extension = GetValidExtension();
            PrintDump();
            return hasVideo;
        }

        public int SeekInQueue(long ticks, bool foreward = false)
        {
            lock (lockActions)
            {
                if (Disposed) return -1;

                /* Seek within current bufffered queue
                 * 
                 * 10 seconds because of video keyframe distances or 1 seconds for other (can be fixed also with CurTime+X seek instead of timestamps)
                 * It doesn't work for HLS live streams
                 * It doesn't work for decoders buffered queue (which is small only subs might be an issue if we have large decoder queue)
                 */
                if (hlsCtx == null && ticks + (VideoStream != null && foreward ? (10000 * 10000) : 1000 * 10000) > CurTime + StartTime && ticks < CurTime + StartTime + BufferedDuration)
                {
                    bool found = false;
                    while (VideoPackets.Count > 0)
                    {
                        VideoPackets.TryPeek(out IntPtr packetPtr);
                        if (packetPtr == IntPtr.Zero) continue;
                        AVPacket* packet = (AVPacket*)packetPtr;
                        if (packet->pts != AV_NOPTS_VALUE && ticks < packet->pts * VideoStream.Timebase && (packet->flags & AV_PKT_FLAG_KEY) != 0)
                        {
                            found = true;
                            ticks = (long) (packet->pts * VideoStream.Timebase);
                            break;
                        }
                        av_packet_free(&packet);
                        VideoPackets.TryDequeue(out IntPtr devnull);
                    }

                    while (AudioPackets.Count > 0)
                    {
                        AudioPackets.TryPeek(out IntPtr packetPtr);
                        if (packetPtr == IntPtr.Zero) continue;
                        AVPacket* packet = (AVPacket*)packetPtr;
                        if (packet->pts != AV_NOPTS_VALUE && ticks < packet->pts * AudioStream.Timebase)
                        {
                            if (Type == MediaType.Audio) found = true;
                            break;
                        }
                        av_packet_free(&packet);
                        AudioPackets.TryDequeue(out IntPtr devnull);
                    }

                    while (SubtitlesPackets.Count > 0)
                    {
                        SubtitlesPackets.TryPeek(out IntPtr packetPtr);
                        if (packetPtr == IntPtr.Zero) continue;
                        AVPacket* packet = (AVPacket*)packetPtr;
                        if (packet->pts != AV_NOPTS_VALUE && ticks < packet->pts * SubtitlesStream.Timebase)
                        {
                            if (Type == MediaType.Subs) found = true;
                            break;
                        }
                        av_packet_free(&packet);
                        SubtitlesPackets.TryDequeue(out IntPtr devnull);
                    }

                    if (found)
                    {
                        Log("[SEEK] Found in Queue");
                        UpdateCurTime();
                        return 0;
                    }
                }

                return -1;
            }
        }
        public int Seek(long ticks, bool foreward = false)
        {
            /* Current Issues
             * 
             * HEVC/MPEG-TS: Fails to seek to keyframe https://blog.csdn.net/Annie_heyeqq/article/details/113649501 | https://trac.ffmpeg.org/ticket/9412
             * AVSEEK_FLAG_BACKWARD will not work on .dav even if it returns 0 (it will work after it fills the index table)
             * Strange delay (could be 200ms!) after seek on HEVC/yuv420p10le (10-bits) while trying to Present on swapchain (possible recreates texturearray?)
             */

            lock (lockActions)
            {
                if (Disposed) return -1;

                int ret;

                Interrupter.ForceInterrupt = 1;
                lock (lockFmtCtx)
                {
                    Interrupter.ForceInterrupt = 0;

                    // Flush required because of the interrupt
                    if (fmtCtx->pb != null) avio_flush(fmtCtx->pb);
                    avformat_flush(fmtCtx);

                    if (hlsCtx != null) fmtCtx->ctx_flags &= ~AVFMTCTX_UNSEEKABLE;
                    Interrupter.Request(Requester.Seek);
                    if (VideoStream != null)
                    {
                        Log($"[SEEK({(foreward ? "->" : "<-")})] Requested at {new TimeSpan(ticks)}");
                        ret = av_seek_frame(fmtCtx, -1, ticks / 10, foreward ? AVSEEK_FLAG_FRAME : AVSEEK_FLAG_BACKWARD);
                    }
                    else
                    {
                        Log($"[SEEK({(foreward ? "->" : "<-")})] Requested at {new TimeSpan(ticks)} | ANY");
                        ret = foreward ?
                            avformat_seek_file(fmtCtx, -1, ticks / 10,     ticks / 10, Int64.MaxValue,  AVSEEK_FLAG_ANY):
                            avformat_seek_file(fmtCtx, -1, Int64.MinValue, ticks / 10, ticks / 10,      AVSEEK_FLAG_ANY);
                    }

                    if (ret < 0)
                    {
                        if (hlsCtx != null) fmtCtx->ctx_flags &= ~AVFMTCTX_UNSEEKABLE;
                        Log($"[SEEK] Failed 1/2 (retrying) {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                        if (VideoStream != null)
                            ret = av_seek_frame(fmtCtx, -1, ticks / 10, foreward ? AVSEEK_FLAG_BACKWARD : AVSEEK_FLAG_FRAME);
                        else
                            ret = foreward ?
                                avformat_seek_file(fmtCtx, -1, Int64.MinValue   , ticks / 10, ticks / 10    , AVSEEK_FLAG_ANY):
                                avformat_seek_file(fmtCtx, -1, ticks / 10       , ticks / 10, Int64.MaxValue, AVSEEK_FLAG_ANY);

                        if (ret < 0) Log($"[SEEK] Failed 2/2 {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                    }

                    DisposePackets();
                    UpdateCurTime();

                    lock (lockStatus) if (Status == Status.Ended) Status = Status.Stopped;
                }

                return ret;  // >= 0 for success
            }
        }

        protected override void RunInternal()
        {
            int ret = 0;
            int allowedErrors = Config.MaxErrors;
            bool gotAVERROR_EXIT = false;

            do
            {
                // Wait until not QueueFull
                if (BufferedDuration > Config.BufferDuration)
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueFull;

                    while (!PauseOnQueueFull && BufferedDuration > Config.BufferDuration && Status == Status.QueueFull) { Thread.Sleep(20); UpdateCurTime(true); }

                    lock (lockStatus)
                    {
                        if (PauseOnQueueFull) { PauseOnQueueFull = false; Status = Status.Pausing; }
                        if (Status != Status.QueueFull) break;
                        Status = Status.Running;
                    }
                }

                // Wait possible someone asks for lockFmtCtx
                else if (gotAVERROR_EXIT)
                {
                    gotAVERROR_EXIT = false;
                    Thread.Sleep(5);
                }

                // Demux Packet
                lock (lockFmtCtx)
                {
                    Interrupter.Request(Requester.Read);
                    ret = av_read_frame(fmtCtx, packet);
                    if (Interrupter.ForceInterrupt != 0) 
                    {
                        av_packet_unref(packet); gotAVERROR_EXIT = true;
                        continue;
                    }

                    // Possible check if interrupt/timeout and we dont seek to reset the backend pb->pos = 0?
                    if (ret != 0)
                    {
                        av_packet_unref(packet);

                        if ((ret == AVERROR_EXIT && fmtCtx->pb != null && fmtCtx->pb->eof_reached != 0) || ret == AVERROR_EOF)
                        { 
                            // AVERROR_EXIT && fmtCtx->pb->eof_reached probably comes from Interrupts (should ensure we seek after that)
                            Status = Status.Ended;
                            break;
                        }

                        allowedErrors--;
                        Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                        if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); Status = Status.Stopping; break; }

                        gotAVERROR_EXIT = true;
                        continue;
                    }

                    TotalBytes += packet->size;

                    // Skip Disabled Streams
                    if (!EnabledStreams.Contains(packet->stream_index)) { av_packet_unref(packet); continue; }

                    UpdateCurTime();

                    if (IsRecording)
                    {
                        if (StartRecordingAt == -1)
                        {
                            if (!recGotKeyframe && VideoStream == null)
                                recGotKeyframe = true;
                            else if (!recGotKeyframe && fmtCtx->streams[packet->stream_index]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO && (packet->flags & AV_PKT_FLAG_KEY) != 0)
                                recGotKeyframe = true;
                        }
                        else
                        {
                            if (!recGotKeyframe && (long)(packet->pts * AVStreamToStream[packet->stream_index].Timebase) >= StartRecordingAt)
                            {
                                recGotKeyframe = true;
                                Log($"Starts recording at {Utils.TicksToTime((long)(packet->pts * AVStreamToStream[packet->stream_index].Timebase))}");
                            }
                            
                        }

                        if (recGotKeyframe && (fmtCtx->streams[packet->stream_index]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO || fmtCtx->streams[packet->stream_index]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO))
                            CurRecorder.Write(av_packet_clone(packet), Type == MediaType.Audio);
                    }
                    
                    // Enqueue Packet (AVS Queue or Single Queue)
                    if (UseAVSPackets)
                    {
                        switch (fmtCtx->streams[packet->stream_index]->codecpar->codec_type)
                        {
                            case AVMEDIA_TYPE_AUDIO:
                                //Log($"Audio => {Utils.TicksToTime((long)(packet->pts * AudioStream.Timebase))} | {Utils.TicksToTime(CurTime)}");

                                AudioBytes += packet->size;
                                AudioPackets.Enqueue((IntPtr)packet);
                                packet = av_packet_alloc();

                                break;

                            case AVMEDIA_TYPE_VIDEO:
                                //Log($"Video => {Utils.TicksToTime((long)(packet->pts * VideoStream.Timebase))} | {Utils.TicksToTime(CurTime)}");

                                VideoBytes += packet->size;
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
                    else
                    {
                        Packets.Enqueue((IntPtr)packet);
                        packet = av_packet_alloc();
                    }
                }
            } while (Status == Status.Running);

            if (IsRecording) { StopRecording(); OnRecordingCompleted(); }
        }
        #endregion

        #region Switch Programs / Streams
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
            lock (lockFmtCtx)
            {
                if (Disposed || stream == null || EnabledStreams.Contains(stream.StreamIndex)) return;

                EnabledStreams.Add(stream.StreamIndex);
                fmtCtx->streams[stream.StreamIndex]->discard = AVDiscard.AVDISCARD_DEFAULT;
                stream.Enabled = true;
                EnableProgram(stream);

                switch (stream.Type)
                {
                    case MediaType.Audio:
                        AudioStream = (AudioStream) stream;
                        if (VideoStream == null) HLSPlaylist = AudioStream.HLSPlaylist;
                        UpdateCurTime();
                        break;

                    case MediaType.Video:
                        VideoStream = (VideoStream) stream;
                        HLSPlaylist = VideoStream.HLSPlaylist;
                        UpdateCurTime();
                        break;

                    case MediaType.Subs:
                        SubtitlesStream = (SubtitlesStream) stream;
                        break;
                }

                if (UseAVSPackets)
                    CurPackets = VideoStream != null ? VideoPackets : (AudioStream != null ? AudioPackets : SubtitlesPackets);

                Log($"[{stream.Type} #{stream.StreamIndex}] Enabled");
            }
        }
        public void DisableStream(StreamBase stream)
        {
            lock (lockFmtCtx)
            {
                if (Disposed || stream == null || !EnabledStreams.Contains(stream.StreamIndex)) return;

                /* AVDISCARD_ALL causes syncing issues between streams (TBR: bandwidth?)
                 * 1) While switching video streams will not switch at the same timestamp
                 * 2) By disabling video stream after a seek, audio will not seek properly
                 */

                fmtCtx->streams[stream.StreamIndex]->discard = AVDiscard.AVDISCARD_ALL; 
                EnabledStreams.Remove(stream.StreamIndex);
                stream.Enabled = false;
                DisableProgram(stream);

                switch (stream.Type)
                {
                    case MediaType.Audio:
                        AudioStream = null;
                        if (VideoStream != null) HLSPlaylist = VideoStream.HLSPlaylist;
                        break;

                    case MediaType.Video:
                        VideoStream = null;
                        if (AudioStream != null) HLSPlaylist = AudioStream.HLSPlaylist;
                        break;

                    case MediaType.Subs:
                        SubtitlesStream = null;
                        break;
                }

                DisposePackets(GetPacketsPtr(stream.Type));

                if (UseAVSPackets)
                    CurPackets = VideoStream != null ? VideoPackets : (AudioStream != null ? AudioPackets : SubtitlesPackets);

                Log($"[{stream.Type} #{stream.StreamIndex}] Disabled");
            }
        }
        public void SwitchStream(StreamBase stream)
        {
            lock (lockFmtCtx)
            {
                if (stream.Type == MediaType.Audio)
                    DisableStream(AudioStream);
                else if (stream.Type == MediaType.Video)
                    DisableStream(VideoStream);
                else
                    DisableStream(SubtitlesStream);

                EnableStream(stream);
            }
        }
        #endregion

        #region Misc
        internal bool UpdateCurTime(bool useLastPts = false)
        {
            if (HLSPlaylist != null && hlsPrevFirstTimestamp != hlsCtx->first_timestamp)
            {
                hlsPrevFirstTimestamp = hlsCtx->first_timestamp;

                long curTimeLive= 0;
                long duration   = 0;

                for (int i=0; i<HLSPlaylist->n_segments; i++)
                {
                    if (i<=HLSPlaylist->cur_seq_no - HLSPlaylist->start_seq_no )
                        curTimeLive += HLSPlaylist->segments[i]->duration;
                    duration += HLSPlaylist->segments[i]->duration;
                }

                EndTimeLive = curTimeLive * 10;
                Duration    = duration * 10;

                if (HLSPlaylist->finished == 1) IsLive = false;
            }
            
            long curTimestamp = -1;

            try
            {
                if (!CurPackets.TryPeek(out IntPtr firstPacketPtr)) { BufferedDuration = 0; return true; }

                AVPacket* firstPacket = (AVPacket*) firstPacketPtr;
                if (firstPacket->pts == AV_NOPTS_VALUE) return false;
                curTimestamp = (long)(firstPacket->pts * AVStreamToStream[firstPacket->stream_index].Timebase);

                long bufferedDuration;

                if (useLastPts)
                {
                    if (lastKnownPtsTimestamp == AV_NOPTS_VALUE) return false;
                    bufferedDuration = lastKnownPtsTimestamp - curTimestamp;
                }
                else
                {
                    if (packet->pts != AV_NOPTS_VALUE)
                        lastKnownPtsTimestamp = (long)(packet->pts * AVStreamToStream[packet->stream_index].Timebase);
                    else if (lastKnownPtsTimestamp == AV_NOPTS_VALUE)
                        return false;

                    bufferedDuration = lastKnownPtsTimestamp - curTimestamp;
                }

                if (bufferedDuration < 0) return false;
                BufferedDuration = bufferedDuration;

                return true;

            } finally
            {
                if (HLSPlaylist != null)
                {
                    CurTime = EndTimeLive - BufferedDuration > Duration - (HLSPlaylist->target_duration * 10) ? Duration : (EndTimeLive - BufferedDuration < HLSPlaylist->target_duration * 10 ? 0 : EndTimeLive - BufferedDuration);
                }
                else if (curTimestamp != -1)
                    CurTime = curTimestamp - StartTime;
            }
        }
        
        private string GetValidExtension()
        {
            // TODO
            // Should check for all supported output formats (there is no list in ffmpeg.autogen ?)
            // Should check for inner input format (not outer protocol eg. hls/rtsp)
            // Should check for raw codecs it can be mp4/mov but it will not work in mp4 only in mov (or avi for raw)

            List<string> supportedOutput = new List<string>() { "mp4", "avi", "flv", "flac", "mpeg", "mpegts", "mkv", "ogg", "ts"};
            string defaultExtenstion = "mp4";
            bool hasPcm = false;
            bool isRaw = false;

            foreach (AudioStream stream in AudioStreams)
                if (stream.Codec.Contains("pcm")) hasPcm = true;

            foreach (VideoStream stream in VideoStreams)
                if (stream.Codec.Contains("raw")) isRaw = true;

            if (isRaw) defaultExtenstion = "avi";

            // MP4 container doesn't support PCM
            if (hasPcm) defaultExtenstion = "mkv";

            // TODO
            // Check also shortnames
            //if (Name == "mpegts") return "ts";
            //if ((fmtCtx->iformat->flags & AVFMT_TS_DISCONT) != 0) should be mp4 or container that supports segments

            if (string.IsNullOrEmpty(Extensions)) return defaultExtenstion;
            string[] extensions = Extensions.Split(',');
            if (extensions == null || extensions.Length < 1) return defaultExtenstion;

            // Try to set the output container same as input
            for (int i=0; i<extensions.Length; i++)
                if (supportedOutput.Contains(extensions[i]))
                    return extensions[i] == "mp4" && isRaw ? "mov" : extensions[i];

            return defaultExtenstion;
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

        /// <summary>
        /// Demuxes until the a valid packet within EnabledStreams or the specified stream (Will be stored in AVPacket* packet)
        /// </summary>
        /// <param name="streamIndex">Find packets only for the specified stream index</param>
        /// <returns></returns>
        public int GetNextPacket(int streamIndex = -1)
        {
            int ret;

            while (true)
            {
                ret = av_read_frame(fmtCtx, packet);
                if (ret != 0) { av_packet_unref(packet); return ret; }

                if ((streamIndex == -1 && !EnabledStreams.Contains(packet->stream_index)) || 
                    (streamIndex != -1 && packet->stream_index != streamIndex))
                { av_packet_unref(packet); continue; }

                return 0;
            }
        }

        #region Recording
        Remuxer Recorder, CurRecorder;
        bool        recGotKeyframe;
        bool        isExternalRecorder;
        
        public long StartRecordingAt { get; private set; } = -1;
        public bool IsRecording { get; private set; }
        public event EventHandler RecordingCompleted;
        public void OnRecordingCompleted() { RecordingCompleted.Invoke(this, new EventArgs()); }
        public void StartRecording(Remuxer remuxer, long startAt = -1)
        {
            if (Disposed) return;

            StartRecordingAt    = startAt;
            CurRecorder         = remuxer;
            isExternalRecorder  = true;
            recGotKeyframe      = false;
            IsRecording         = true;

        }

        /// <summary>
        /// Records the currently enabled AVS streams
        /// </summary>
        /// <param name="filename">The filename to save the recorded video. The file extension will let the demuxer to choose the output format (eg. mp4). If you useRecommendedExtension will be updated with the extension.</param>
        /// <param name="useRecommendedExtension">Will try to match the output container with the input container</param>
        public void StartRecording(ref string filename, bool useRecommendedExtension = true)
        {
            if (Disposed) return;

            lock (lockFmtCtx)
            {
                if (IsRecording) StopRecording();

                Log("Record Start");
                CurRecorder = Recorder;
                isExternalRecorder = false;

                if (useRecommendedExtension)
                    filename = $"{filename}.{Extension}";

                Recorder.Open(filename);
                for(int i=0; i<EnabledStreams.Count; i++)
                    Log(Recorder.AddStream(fmtCtx->streams[EnabledStreams[i]]).ToString());
                if (!Recorder.HasStreams || Recorder.WriteHeader() != 0) return; //throw new Exception("Invalid remuxer configuration");
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
                IsRecording = false;
                if (isExternalRecorder) return;
                Recorder.Dispose();
            }
        }
        #endregion

        #endregion
    }
}