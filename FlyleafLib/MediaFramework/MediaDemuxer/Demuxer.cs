using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.ffmpegEx;

using FlyleafLib.MediaFramework.MediaStream;

using static FlyleafLib.Config;
using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaDemuxer
{
    public unsafe class Demuxer : RunThreadBase
    {
        /* TODO
         * 1) Review lockFmtCtx on Enable/Disable Streams causes delay and is not fully required
         */

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
        public long                     Duration        { get; internal set; }

        public Dictionary<string, string>
                                        Metadata        { get; internal set; } = new Dictionary<string, string>();

        /// <summary>
        /// The time of first packet in the queue (zero based, substracts start time)
        /// </summary>
        public long                     CurTime         => CurPackets.CurTime != 0 ? CurPackets.CurTime : lastSeekTime;

        /// <summary>
        /// The buffered time in the queue (last packet time - first packet time)
        /// </summary>
        public long                     BufferedDuration=> CurPackets.BufferedDuration;

        public bool                     IsLive          { get; private set; }

        public AVFormatContext*         FormatContext   => fmtCtx;
        public CustomIOContext          CustomIOContext { get; private set; }

        // Media Programs
        public int[][]                  Programs        { get; private set; } = new int[0][];

        // Media Streams
        public ObservableCollection<AudioStream>        AudioStreams    { get; private set; } = new ObservableCollection<AudioStream>();
        public ObservableCollection<VideoStream>        VideoStreams    { get; private set; } = new ObservableCollection<VideoStream>();
        public ObservableCollection<SubtitlesStream>    SubtitlesStreams{ get; private set; } = new ObservableCollection<SubtitlesStream>();
        object lockStreams = new object();

        public List<int>                EnabledStreams  { get; private set; } = new List<int>();
        public Dictionary<int, StreamBase> 
                                        AVStreamToStream{ get; private set; }

        public AudioStream              AudioStream     { get; private set; }
        public VideoStream              VideoStream     { get; private set; }
        public SubtitlesStream          SubtitlesStream { get; private set; }

        // Audio/Video Stream's HLSPlaylist
        internal HLSPlaylist*           HLSPlaylist     { get; private set; }
        
        // Media Packets
        public PacketQueue              Packets         { get; private set; }
        public PacketQueue              AudioPackets    { get; private set; }
        public PacketQueue              VideoPackets    { get; private set; }
        public PacketQueue              SubtitlesPackets{ get; private set; }
        public PacketQueue              CurPackets      { get; private set; }

        public bool                     UseAVSPackets   { get; private set; }
        public PacketQueue GetPacketsPtr(MediaType type)
            { if (!UseAVSPackets) return Packets; return type == MediaType.Audio ? AudioPackets : (type == MediaType.Video ? VideoPackets : SubtitlesPackets); }

        public ConcurrentQueue<ConcurrentStack<List<IntPtr>>>
                                        VideoPacketsReverse
                                                        { get; private set; } = new ConcurrentQueue<ConcurrentStack<List<IntPtr>>>();
        public bool                     IsReversePlayback
                                                        { get; private set; }
        
        public long                     TotalBytes      { get; private set; } = 0;

        // Interrupt
        public Interrupter              Interrupter     { get; private set; }

        public List<Chapter>            Chapters        { get; private set; } = new List<Chapter>();
        public class Chapter
        {
            public long     StartTime   { get; set; }
            public long     EndTime     { get; set; }
            public string   Title       { get; set; }
        }


        public event EventHandler AudioLimit;
        bool audioBufferLimitFired;
        void OnAudioLimit()
        {
            System.Threading.Tasks.Task.Run(() => AudioLimit?.Invoke(this, new EventArgs()));
        }

        public event EventHandler TimedOut;
        internal void OnTimedOut()
        {
            System.Threading.Tasks.Task.Run(() => TimedOut?.Invoke(this, new EventArgs()));
        }
        #endregion

        #region Constructor / Declaration
        public AVPacket*        packet;
        AVFormatContext*        fmtCtx;
        internal HLSContext*    hlsCtx;

        long                    hlsPrevSeqNo            = AV_NOPTS_VALUE;   // Identifies the change of the m3u8 playlist (wraped)
        internal long           hlsStartTime            = AV_NOPTS_VALUE;   // Calculation of first timestamp (lastPacketTs - hlsCurDuration)
        long                    hlsCurDuration;                             // Duration until the start of the current segment
        long                    lastSeekTime;                               // To set CurTime while no packets are available

        internal GCHandle       handle;
        public object           lockFmtCtx              = new object();
        internal bool           allowReadInterrupts;

        /* Reverse Playback
         * 
         * Video Packets Queue (FIFO)                       ConcurrentQueue<ConcurrentStack<List<IntPtr>>>
         *      Video Packets Seek Stacks (FILO)            ConcurrentStack<List<IntPtr>>
         *          Video Packets List Keyframe (List)      List<IntPtr>
         */

        long                    curReverseStopPts       = AV_NOPTS_VALUE;
        long                    curReverseStopRequestedPts 
                                                        = AV_NOPTS_VALUE;
        long                    curReverseStartPts      = AV_NOPTS_VALUE;
        List<IntPtr>            curReverseVideoPackets  = new List<IntPtr>();
        ConcurrentStack<List<IntPtr>>
                                curReverseVideoStack    = new ConcurrentStack<List<IntPtr>>();
        long                    curReverseSeekOffset;

        public Demuxer(DemuxerConfig config, MediaType type = MediaType.Video, int uniqueId = -1, bool useAVSPackets = true) : base(uniqueId)
        {
            Config          = config;
            Type            = type;
            UseAVSPackets   = useAVSPackets;
            Interrupter     = new Interrupter(this);
            CustomIOContext = new CustomIOContext(this);

            Packets         = new PacketQueue(this);
            AudioPackets    = new PacketQueue(this);
            VideoPackets    = new PacketQueue(this);
            SubtitlesPackets= new PacketQueue(this);
            CurPackets      = Packets; // Will be updated on stream switch in case of AVS

            string typeStr = Type == MediaType.Video ? "Main" : Type.ToString();
            threadName = $"Demuxer: {typeStr.PadLeft(5, ' ')}";

            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(AudioStreams, lockStreams);
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(VideoStreams, lockStreams);
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(SubtitlesStreams, lockStreams);
        }
        #endregion

        #region Dispose / Dispose Packets
        public void DisposePackets()
        {
            if (UseAVSPackets)
            {
                AudioPackets.Clear();
                VideoPackets.Clear();
                SubtitlesPackets.Clear();

                DisposePacketsReverse();
            }
            else
                Packets.Clear();

            hlsStartTime = AV_NOPTS_VALUE;
        }
        
        public void DisposePacketsReverse()
        {
            while (!VideoPacketsReverse.IsEmpty)
            {
                VideoPacketsReverse.TryDequeue(out var t1);
                while (!t1.IsEmpty)
                {
                    t1.TryPop(out var t2);
                    for (int i = 0; i < t2.Count; i++)
                    {
                        if (t2[i] == IntPtr.Zero) continue;
                        AVPacket* packet = (AVPacket*)t2[i];
                        av_packet_free(&packet);
                    }
                }
            }

            while (!curReverseVideoStack.IsEmpty)
            {
                curReverseVideoStack.TryPop(out var t2);
                for (int i = 0; i < t2.Count; i++)
                { 
                    if (t2[i] == IntPtr.Zero) continue;
                    AVPacket* packet = (AVPacket*)t2[i];
                    av_packet_free(&packet);
                }
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

                IsReversePlayback   = false;
                curReverseStopPts   = AV_NOPTS_VALUE;
                curReverseStartPts  = AV_NOPTS_VALUE;
                hlsPrevSeqNo        = AV_NOPTS_VALUE;
                lastSeekTime        = 0;

                // Free Streams
                if (AudioStreams.Count != 0 || VideoStreams.Count != 0 || SubtitlesStreams.Count != 0)
                {
                    lock (lockStreams)
                    {
                        AudioStreams.Clear();
                        VideoStreams.Clear();
                        SubtitlesStreams.Clear();
                    }
                }
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

                TotalBytes = 0;
                Status = Status.Stopped;
                Disposed = true;

                Log.Info("Disposed");
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
                        if (ret < 0) { fmtCtx = null; return error = $"[avformat_open_input] {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})"; }

                        // Find Streams Info
                        ret = avformat_find_stream_info(fmtCtx, null);
                        if (ret == AVERROR_EXIT || Status != Status.Opening || Interrupter.ForceInterrupt == 1) return error = "Cancelled";
                        if (ret < 0) return error = $"[avformat_find_stream_info] {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})";
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

            // TBR: Possible we can get Apple HTTP Live Streaming/hls with HLSPlaylist->finished with Duration != 0
            if (Duration == 0 && Name == "hls" && Environment.Is64BitProcess)
            {
                hlsCtx = (HLSContext*) fmtCtx->priv_data;
                StartTime = 0;
                //UpdateHLSTime(); Maybe with default 0 playlist
            }

            IsLive = Duration == 0 || hlsCtx != null;

            bool hasVideo = false;
            AVStreamToStream = new Dictionary<int, StreamBase>();

            Metadata.Clear();
            AVDictionaryEntry* b = null;
            while (true)
            {
                b = av_dict_get(fmtCtx->metadata, "", b, AV_DICT_IGNORE_SUFFIX);
                if (b == null) break;
                Metadata.Add(Utils.BytePtrToStringUTF8(b->key), Utils.BytePtrToStringUTF8(b->value));
            }

            Chapters.Clear();
            string dump = "";
            for (int i=0; i<fmtCtx->nb_chapters; i++)
            {
                var chp = fmtCtx->chapters[i];
                double tb = av_q2d(chp->time_base) * 10000.0 * 1000.0;
                string title = "";

                b = null;
                while (true)
                {
                    b = av_dict_get(chp->metadata, "", b, AV_DICT_IGNORE_SUFFIX);
                    if (b == null) break;
                    if (Utils.BytePtrToStringUTF8(b->key).ToLower() == "title")
                        title = Utils.BytePtrToStringUTF8(b->value);
                }

                if (CanDebug)
                    dump += $"[Chapter {(i+1).ToString().PadRight(2, ' ')}] {Utils.TicksToTime((long)(chp->start * tb) - StartTime)} - {Utils.TicksToTime((long)(chp->end * tb) - StartTime)} | {title}\r\n";

                Chapters.Add(new Chapter()
                {
                    StartTime   = (long)((chp->start * tb) - StartTime),
                    EndTime     = (long)((chp->end * tb) - StartTime),
                    Title       = title
                });
            }

            if (CanDebug && dump != "") Log.Debug($"Chapters\r\n\r\n{dump}");

            bool audioHasEng = false;
            bool subsHasEng = false;

            lock (lockStreams) {  
                for (int i=0; i<fmtCtx->nb_streams; i++)
                {
                    fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

                    switch (fmtCtx->streams[i]->codecpar->codec_type)
                    {
                        case AVMEDIA_TYPE_AUDIO:
                            AudioStreams.Add(new AudioStream(this, fmtCtx->streams[i]));
                            AVStreamToStream.Add(fmtCtx->streams[i]->index, AudioStreams[AudioStreams.Count-1]);
                            if (AudioStreams[AudioStreams.Count-1].Language == Language.Get("eng")) audioHasEng = true;
                            break;

                        case AVMEDIA_TYPE_VIDEO:
                            if ((fmtCtx->streams[i]->disposition & AV_DISPOSITION_ATTACHED_PIC) != 0) 
                                { Log.Info($"Excluding image stream #{i}"); continue; }

                            VideoStreams.Add(new VideoStream(this, fmtCtx->streams[i]));
                            AVStreamToStream.Add(fmtCtx->streams[i]->index, VideoStreams[VideoStreams.Count-1]);
                            if (VideoStreams[VideoStreams.Count-1].PixelFormat != AVPixelFormat.AV_PIX_FMT_NONE) hasVideo = true;
                            break;

                        case AVMEDIA_TYPE_SUBTITLE:
                            SubtitlesStreams.Add(new SubtitlesStream(this, fmtCtx->streams[i]));
                            AVStreamToStream.Add(fmtCtx->streams[i]->index, SubtitlesStreams[SubtitlesStreams.Count-1]);
                            if (SubtitlesStreams[SubtitlesStreams.Count-1].Language == Language.Get("eng")) subsHasEng = true;
                            break;

                        default:
                            Log.Info($"#[Unknown #{i}] {fmtCtx->streams[i]->codecpar->codec_type}");
                            break;
                    }
                }
            }

            if (!audioHasEng)
            {
                for (int i=0; i<AudioStreams.Count; i++)
                {
                    if (AudioStreams[i].Language.IdSubLanguage == "und" && (string.IsNullOrEmpty(AudioStreams[i].Language.OriginalInput) || System.Text.RegularExpressions.Regex.IsMatch(AudioStreams[i].Language.OriginalInput, "^un", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                        AudioStreams[i].Language = Language.Get("eng");
                }
            }

            if (!subsHasEng && Type == MediaType.Video)
            {
                for (int i=0; i<SubtitlesStreams.Count; i++)
                {
                    if (SubtitlesStreams[i].Language.IdSubLanguage == "und" && (string.IsNullOrEmpty(SubtitlesStreams[i].Language.OriginalInput) || System.Text.RegularExpressions.Regex.IsMatch(SubtitlesStreams[i].Language.OriginalInput, "^un", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                        SubtitlesStreams[i].Language = Language.Get("eng");
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

        public int SeekInQueue(long ticks, bool forward = false)
        {
            lock (lockActions)
            {
                if (Disposed) return -1;

                /* Seek within current bufffered queue
                 * 
                 * 10 seconds because of video keyframe distances or 1 seconds for other (can be fixed also with CurTime+X seek instead of timestamps)
                 * For subtitles it should keep (prev packet) the last presented as it can have a lot of distance with CurTime (cur packet)
                 * It doesn't work for HLS live streams
                 * It doesn't work for decoders buffered queue (which is small only subs might be an issue if we have large decoder queue)
                 */

                long startTime = StartTime;

                if (HLSPlaylist != null)
                {
                    ticks    += hlsStartTime - (hlsCtx->first_timestamp * 10);
                    startTime = hlsStartTime;
                }

                if (ticks + (VideoStream != null && forward ? (10000 * 10000) : 1000 * 10000) > CurTime + startTime && ticks < CurTime + startTime + BufferedDuration)
                {
                    bool found = false;
                    while (VideoPackets.Count > 0)
                    {
                        AVPacket* packet = VideoPackets.Peek();
                        if (packet->pts != AV_NOPTS_VALUE && ticks < packet->pts * VideoStream.Timebase && (packet->flags & AV_PKT_FLAG_KEY) != 0)
                        {
                            found = true;
                            ticks = (long) (packet->pts * VideoStream.Timebase);

                            break;
                        }

                        VideoPackets.Dequeue();
                        av_packet_free(&packet);
                    }

                    while (AudioPackets.Count > 0)
                    {
                        AVPacket* packet = AudioPackets.Peek();
                        if (packet->pts != AV_NOPTS_VALUE && ticks < packet->pts * AudioStream.Timebase)
                        {
                            if (Type == MediaType.Audio || VideoStream == null)
                                found = true;

                            break;
                        }

                        AudioPackets.Dequeue();
                        av_packet_free(&packet);
                    }

                    while (SubtitlesPackets.Count > 0)
                    {
                        AVPacket* packet = SubtitlesPackets.Peek();
                        if (packet->pts != AV_NOPTS_VALUE && ticks < packet->pts * SubtitlesStream.Timebase)
                        {
                            if (Type == MediaType.Subs)
                                found = true;

                            break;
                        }

                        SubtitlesPackets.Dequeue();
                        av_packet_free(&packet);
                    }

                    if (found)
                    {
                        Log.Debug("[Seek] Found in Queue");
                        return 0;
                    }
                }

                return -1;
            }
        }
        public int Seek(long ticks, bool forward = false)
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
                    if (fmtCtx->pb != null)
                        avio_flush(fmtCtx->pb);
                    avformat_flush(fmtCtx);

                    // Forces seekable HLS
                    if (hlsCtx != null)
                        fmtCtx->ctx_flags &= ~AVFMTCTX_UNSEEKABLE;

                    Interrupter.Request(Requester.Seek);
                    if (VideoStream != null)
                    {
                        if (CanDebug) Log.Debug($"[Seek({(forward ? "->" : "<-")})] Requested at {new TimeSpan(ticks)}");
                        ret = av_seek_frame(fmtCtx, -1, ticks / 10, forward ? AVSEEK_FLAG_FRAME : AVSEEK_FLAG_BACKWARD);

                        curReverseStopPts = AV_NOPTS_VALUE;
                        curReverseStartPts= AV_NOPTS_VALUE;
                    }
                    else
                    {
                        if (CanDebug) Log.Debug($"[Seek({(forward ? "->" : "<-")})] Requested at {new TimeSpan(ticks)} | ANY");
                        ret = forward ?
                            avformat_seek_file(fmtCtx, -1, ticks / 10,     ticks / 10, Int64.MaxValue,  AVSEEK_FLAG_ANY):
                            avformat_seek_file(fmtCtx, -1, Int64.MinValue, ticks / 10, ticks / 10,      AVSEEK_FLAG_ANY);
                    }

                    if (ret < 0)
                    {
                        if (hlsCtx != null) fmtCtx->ctx_flags &= ~AVFMTCTX_UNSEEKABLE;
                        Log.Info($"Seek failed 1/2 (retrying) {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                        if (VideoStream != null)
                            ret = av_seek_frame(fmtCtx, -1, ticks / 10, forward ? AVSEEK_FLAG_BACKWARD : AVSEEK_FLAG_FRAME);
                        else
                            ret = forward ?
                                avformat_seek_file(fmtCtx, -1, Int64.MinValue   , ticks / 10, ticks / 10    , AVSEEK_FLAG_ANY):
                                avformat_seek_file(fmtCtx, -1, ticks / 10       , ticks / 10, Int64.MaxValue, AVSEEK_FLAG_ANY);

                        if (ret < 0)
                            Log.Warn($"Seek failed 2/2 {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
                        else
                            lastSeekTime = ticks - StartTime - (HLSPlaylist != null ? hlsStartTime : 0);
                    }
                    else
                        lastSeekTime = ticks - StartTime - (HLSPlaylist != null ? hlsStartTime : 0);

                    DisposePackets();
                    lock (lockStatus) if (Status == Status.Ended) Status = Status.Stopped;
                }

                return ret; // >= 0 for success
            }
        }

        protected override void RunInternal()
        {
            if (IsReversePlayback)
            {
                RunInternalReverse();
                return;
            }

            int ret = 0;
            int allowedErrors = Config.MaxErrors;
            bool gotAVERROR_EXIT = false;
            audioBufferLimitFired = false;

            do
            {
                // Wait until not QueueFull
                if (BufferedDuration > Config.BufferDuration || (Config.BufferPackets != 0 && CurPackets.Count > Config.BufferPackets))
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueFull;

                    while (!PauseOnQueueFull && (BufferedDuration > Config.BufferDuration || (Config.BufferPackets != 0 && CurPackets.Count > Config.BufferPackets)) && Status == Status.QueueFull)
                        Thread.Sleep(20);

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
                        if (CanWarn) Log.Warn($"{FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                        if (allowedErrors == 0) { Log.Error("Too many errors!"); Status = Status.Stopping; break; }

                        gotAVERROR_EXIT = true;
                        continue;
                    }

                    TotalBytes += packet->size;

                    // Skip Disabled Streams | TODO: It's possible that the streams will changed (add/remove or even update/change of codecs)
                    if (!EnabledStreams.Contains(packet->stream_index)) { av_packet_unref(packet); continue; }

                    UpdateHLSTime();

                    if (CanTrace)
                    {
                        StreamBase stream = AVStreamToStream[packet->stream_index];
                        long dts = packet->dts == AV_NOPTS_VALUE ? -1 : (long)(packet->dts * stream.Timebase);
                        long pts = packet->pts == AV_NOPTS_VALUE ? -1 : (long)(packet->pts * stream.Timebase);
                        Log.Trace($"[{stream.Type}] DTS: {(dts == -1 ? "-" : Utils.TicksToTime(dts))} PTS: {(pts == -1 ? "-" : Utils.TicksToTime(pts))} | FLPTS: {(pts == -1 ? "-" : Utils.TicksToTime(pts - StartTime))} | CurTime: {Utils.TicksToTime(CurTime)} | Buffered: {Utils.TicksToTime(BufferedDuration)}");
                    }

                    // Enqueue Packet (AVS Queue or Single Queue)
                    if (UseAVSPackets)
                    {
                        switch (fmtCtx->streams[packet->stream_index]->codecpar->codec_type)
                        {
                            case AVMEDIA_TYPE_AUDIO:
                                //Log($"Audio => {Utils.TicksToTime((long)(packet->pts * AudioStream.Timebase))} | {Utils.TicksToTime(CurTime)}");

                                // Handles A/V de-sync and ffmpeg bug with 2^33 timestamps wrapping
                                if (Config.MaxAudioPackets != 0 && AudioPackets.Count > Config.MaxAudioPackets)
                                {
                                    av_packet_unref(packet);

                                    if (!audioBufferLimitFired)
                                    {
                                        audioBufferLimitFired = true;
                                        OnAudioLimit();
                                    }

                                    break;
                                }

                                AudioPackets.Enqueue();

                                break;

                            case AVMEDIA_TYPE_VIDEO:
                                //Log($"Video => {Utils.TicksToTime((long)(packet->pts * VideoStream.Timebase))} | {Utils.TicksToTime(CurTime)}");

                                VideoPackets.Enqueue();

                                break;

                            case AVMEDIA_TYPE_SUBTITLE:
                                SubtitlesPackets.Enqueue();
                            
                                break;

                            default:
                                av_packet_unref(packet);
                                break;
                        }
                    }
                    else
                    {
                        Packets.Enqueue();
                    }
                }
            } while (Status == Status.Running);
        }
        private void RunInternalReverse()
        {
            int ret = 0;
            int allowedErrors = Config.MaxErrors;
            bool gotAVERROR_EXIT = false;

            // To demux further for buffering (related to BufferDuration)
            int maxQueueSize = 2;
            
            do
            {
                // Wait until not QueueFull
                if (VideoPacketsReverse.Count > maxQueueSize)
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueFull;

                    while (!PauseOnQueueFull && VideoPacketsReverse.Count > maxQueueSize && Status == Status.QueueFull) { Thread.Sleep(20); }

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

                            if (curReverseVideoPackets.Count > 0)
                            {
                                AVPacket* drainPacket = av_packet_alloc();
                                drainPacket->data = null;
                                drainPacket->size = 0;
                                curReverseVideoPackets.Add((IntPtr)drainPacket);
                                curReverseVideoStack.Push(curReverseVideoPackets);
                                curReverseVideoPackets = new List<IntPtr>();
                            }

                            if (curReverseVideoStack.Count > 0)
                            {
                                VideoPacketsReverse.Enqueue(curReverseVideoStack);
                                curReverseVideoStack = new ConcurrentStack<List<IntPtr>>();
                            }

                            if (curReverseStartPts != AV_NOPTS_VALUE && curReverseStartPts <= VideoStream.StartTimePts)
                            {
                                Status = Status.Ended;
                                break;
                            }

                            //Log($"[][][SEEK END] {curReverseStartPts} | {Utils.TicksToTime((long) (curReverseStartPts * VideoStream.Timebase))}");
                            Interrupter.Request(Requester.Seek);
                            ret = av_seek_frame(fmtCtx, VideoStream.StreamIndex, Math.Max(curReverseStartPts - curReverseSeekOffset, VideoStream.StartTimePts), AVSEEK_FLAG_BACKWARD);

                            if (ret != 0)
                            {
                                Status = Status.Stopping;
                                break;
                            }

                            curReverseStopPts = curReverseStartPts;
                            curReverseStartPts = AV_NOPTS_VALUE;
                            continue;
                        }

                        allowedErrors--;
                        if (CanWarn) Log.Warn($"{ FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                        if (allowedErrors == 0) { Log.Error("Too many errors!"); Status = Status.Stopping; break; }

                        gotAVERROR_EXIT = true;
                        continue;
                    }

                    if (VideoStream.StreamIndex != packet->stream_index) { av_packet_unref(packet); continue; }

                    if ((packet->flags & AV_PKT_FLAG_KEY) != 0)
                    {
                        if (curReverseStartPts == AV_NOPTS_VALUE)
                            curReverseStartPts = packet->pts;

                        if (curReverseVideoPackets.Count > 0)
                        {
                            AVPacket* drainPacket = av_packet_alloc();
                            drainPacket->data = null;
                            drainPacket->size = 0;
                            curReverseVideoPackets.Add((IntPtr)drainPacket);
                            curReverseVideoStack.Push(curReverseVideoPackets);
                            curReverseVideoPackets = new List<IntPtr>();
                        }
                    }

                    if (packet->pts != AV_NOPTS_VALUE && (
                        (curReverseStopRequestedPts != AV_NOPTS_VALUE && curReverseStopRequestedPts <= packet->pts)  ||
                        (curReverseStopPts == AV_NOPTS_VALUE && (packet->flags & AV_PKT_FLAG_KEY) != 0 && packet->pts != curReverseStartPts)     ||
                        (packet->pts == curReverseStopPts)
                        ))
                    {
                        if (curReverseStartPts == AV_NOPTS_VALUE || curReverseStopPts == curReverseStartPts)
                        {
                            curReverseSeekOffset *= 2;
                            if (curReverseStartPts == AV_NOPTS_VALUE) curReverseStartPts = curReverseStopPts;
                            if (curReverseStartPts == AV_NOPTS_VALUE) curReverseStartPts = curReverseStopRequestedPts;
                        }

                        curReverseStopRequestedPts = AV_NOPTS_VALUE;

                        if ((packet->flags & AV_PKT_FLAG_KEY) == 0 && curReverseVideoPackets.Count > 0)
                        {
                            AVPacket* drainPacket = av_packet_alloc();
                            drainPacket->data = null;
                            drainPacket->size = 0;
                            curReverseVideoPackets.Add((IntPtr)drainPacket);
                            curReverseVideoStack.Push(curReverseVideoPackets);
                            curReverseVideoPackets = new List<IntPtr>();
                        }

                        if (curReverseVideoStack.Count > 0)
                        {
                            VideoPacketsReverse.Enqueue(curReverseVideoStack);
                            curReverseVideoStack = new ConcurrentStack<List<IntPtr>>();
                        }

                        av_packet_unref(packet);

                        if (curReverseStartPts != AV_NOPTS_VALUE && curReverseStartPts <= VideoStream.StartTimePts)
                        {
                            Status = Status.Ended;
                            break;
                        }

                        //Log($"[][][SEEK] {curReverseStartPts} | {Utils.TicksToTime((long) (curReverseStartPts * VideoStream.Timebase))}");
                        Interrupter.Request(Requester.Seek);
                        ret = av_seek_frame(fmtCtx, VideoStream.StreamIndex, Math.Max(curReverseStartPts - curReverseSeekOffset, VideoStream.StartTimePts), AVSEEK_FLAG_BACKWARD);

                        if (ret != 0)
                        {
                            Status = Status.Stopping;
                            break;
                        }

                        curReverseStopPts = curReverseStartPts;
                        curReverseStartPts = AV_NOPTS_VALUE;
                    }
                    else
                    {
                        if (curReverseStartPts != AV_NOPTS_VALUE)
                        {
                            curReverseVideoPackets.Add((IntPtr)packet);
                            packet = av_packet_alloc();
                        }
                        else
                            av_packet_unref(packet);
                    }
                }

            } while (Status == Status.Running);
        }

        public int EnableReversePlayback(long timestamp)
        {
            //if (IsReversePlayback) return -1;
            IsReversePlayback = true;

            int ret;

            //Log($"[SEEK <-] {Utils.TicksToTime(timestamp) }");
            curReverseStopRequestedPts = av_rescale_q((StartTime + timestamp) / 10, av_get_time_base_q(), VideoStream.AVStream->time_base);

            Interrupter.Request(Requester.Seek);
            ret = av_seek_frame(fmtCtx, VideoStream.StreamIndex, curReverseStopRequestedPts, AVSEEK_FLAG_BACKWARD);
            lock (lockStatus) if (Status == Status.Ended) Status = Status.Stopped;
            curReverseStartPts = AV_NOPTS_VALUE;
            return ret;
        }
        public int DisableReversePlayback()
        {
            IsReversePlayback = false;
            return 0;
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
                if (CanDebug) Log.Debug($"[Stream #{stream.StreamIndex}] Program already enabled");
                return;
            }

            for (int i=0; i<Programs.Length; i++)
                for (int l=0; l<Programs[i].Length; l++)
                    if (Programs[i][l] == stream.StreamIndex)
                    {
                        if (CanDebug) Log.Debug($"[Stream #{stream.StreamIndex}] Enables program #{i}");
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
                            if (CanDebug) Log.Debug($"[Stream #{stream.StreamIndex}] Disables program #{i}");
                            fmtCtx->programs[i]->discard = AVDiscard.AVDISCARD_ALL;
                        }
                        else if (CanDebug)
                            Log.Debug($"[Stream #{stream.StreamIndex}] Program #{i} is needed");
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
                        if (VideoStream == null)
                        {
                            HLSPlaylist = AudioStream.HLSPlaylist;
                            UpdateHLSTime();
                        }

                        break;

                    case MediaType.Video:
                        VideoStream = (VideoStream) stream;
                        HLSPlaylist = VideoStream.HLSPlaylist;
                        UpdateHLSTime();

                        curReverseSeekOffset = av_rescale_q((3 * 1000 * 10000) / 10, av_get_time_base_q(), VideoStream.AVStream->time_base);

                        break;

                    case MediaType.Subs:
                        SubtitlesStream = (SubtitlesStream) stream;

                        break;
                }

                if (UseAVSPackets)
                    CurPackets = VideoStream != null ? VideoPackets : (AudioStream != null ? AudioPackets : SubtitlesPackets);

                if (CanInfo) Log.Info($"[{stream.Type} #{stream.StreamIndex}] Enabled");
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
                 * 3) HLS needs to update hlsCtx->first_time and read at least on package before seek to be accurate
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

                        AudioPackets.Clear();

                        break;

                    case MediaType.Video:
                        VideoStream = null;
                        if (AudioStream != null) HLSPlaylist = AudioStream.HLSPlaylist;


                        VideoPackets.Clear();
                        break;

                    case MediaType.Subs:
                        SubtitlesStream = null;

                        SubtitlesPackets.Clear();

                        break;
                }

                if (UseAVSPackets)
                    CurPackets = VideoStream != null ? VideoPackets : (AudioStream != null ? AudioPackets : SubtitlesPackets);

                if (CanInfo) Log.Info($"[{stream.Type} #{stream.StreamIndex}] Disabled");
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
        internal void UpdateHLSTime()
        {
            if (HLSPlaylist == null)
                return;

            if (hlsPrevSeqNo != HLSPlaylist->cur_seq_no)
            {
                hlsPrevSeqNo = HLSPlaylist->cur_seq_no;
                hlsStartTime = AV_NOPTS_VALUE;

                hlsCurDuration = 0;
                long duration = 0;

                for (long i=0; i<HLSPlaylist->cur_seq_no - HLSPlaylist->start_seq_no; i++)
                {
                    hlsCurDuration += HLSPlaylist->segments[i]->duration;
                    duration += HLSPlaylist->segments[i]->duration;
                }

                for (long i=HLSPlaylist->cur_seq_no - HLSPlaylist->start_seq_no; i<HLSPlaylist->n_segments; i++)
                    duration += HLSPlaylist->segments[i]->duration;

                hlsCurDuration *= 10;
                duration *= 10;
                Duration = duration;
            }

            //if (HLSPlaylist->finished == 1) IsLive = false;

            if (hlsStartTime == AV_NOPTS_VALUE && CurPackets.LastTimestamp != AV_NOPTS_VALUE)
            {
                hlsStartTime = CurPackets.LastTimestamp - hlsCurDuration;
                CurPackets.UpdateCurTime();
            }

            //Log($"[S: {HLSPlaylist->start_seq_no} C: {HLSPlaylist->cur_seq_no} L: {HLSPlaylist->last_seq_no} T:{HLSPlaylist->n_segments} BD: {Utils.TicksToTime(BufferedDuration)} SD: {Utils.TicksToTime(hlsCurDuration)} DUR: {Utils.TicksToTime(Duration)}] [FT: {Utils.TicksToTime(hlsCtx->first_timestamp * 10)} ST: {Utils.TicksToTime(hlsStartTime)} FP: {Utils.TicksToTime(firstPacketTs)} CP: {Utils.TicksToTime(lastPacketTs)} <> {Utils.TicksToTime(lastPacketTs-firstPacketTs)} | CT: {Utils.TicksToTime(CurTime)}]");
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
            string dump = $"\r\n[Format  ] {LongName}/{Name} | {Extensions} | {Utils.TicksToTime(fmtCtx->start_time * 10)}/{Utils.TicksToTime(fmtCtx->duration * 10)} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}";

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

            if (CanInfo) Log.Info($"Format Context Info {dump}\r\n");
        }

        /// <summary>
        /// Pushes the demuxer to the next available video packet (uses also the buffer queue)
        /// </summary>
        /// <returns>0 on success</returns>
        public int GetNextVideoPacket()
        {
            if (VideoPackets.Count > 0)
            {
                packet = VideoPackets.Dequeue();
                return 0;
            }
            else
                return GetNextPacket(VideoStream.StreamIndex);
        }

        /// <summary>
        /// Pushes the demuxer to the next available packet
        /// </summary>
        /// <param name="streamIndex">Packet's stream index</param>
        /// <returns>0 on success</returns>
        public int GetNextPacket(int streamIndex = -1)
        {
            int ret;

            while (true)
            {
                Interrupter.Request(Requester.Read);
                ret = av_read_frame(fmtCtx, packet);

                if (ret != 0) 
                {
                    av_packet_unref(packet);

                    if ((ret == AVERROR_EXIT && fmtCtx->pb != null && fmtCtx->pb->eof_reached != 0) || ret == AVERROR_EOF)
                    {
                        packet = av_packet_alloc();
                        packet->data = null;
                        packet->size = 0;

                        Status = Status.Ended;
                    }

                    return ret; 
                }

                if (streamIndex != -1)
                {
                    if (packet->stream_index == streamIndex)
                        return 0;
                }
                else if (EnabledStreams.Contains(packet->stream_index))
                    return 0;

                av_packet_unref(packet);
            }
        }
        #endregion
    }

    public unsafe class PacketQueue
    {
        /* TODO
         * 
         * 1) Review thread safe for Enqueue/Dequeue/Clear
         * 
         */
        Demuxer demuxer;
        ConcurrentQueue<IntPtr> packets = new ConcurrentQueue<IntPtr>();

        public long Bytes               { get; private set; }
        public long BufferedDuration    { get; private set; }
        public long CurTime             { get; private set; }
        public int  Count               => packets.Count;

        public long FirstTimestamp      { get; private set; } = AV_NOPTS_VALUE;
        public long LastTimestamp       { get; private set; } = AV_NOPTS_VALUE;

        public PacketQueue(Demuxer demuxer)
        {
            this.demuxer = demuxer;
        }

        public void Clear()
        {
            while (!packets.IsEmpty)
            {
                packets.TryDequeue(out IntPtr packetPtr);
                if (packetPtr == IntPtr.Zero) continue;
                AVPacket* packet = (AVPacket*)packetPtr;
                av_packet_free(&packet);
            }

            FirstTimestamp = AV_NOPTS_VALUE;
            LastTimestamp = AV_NOPTS_VALUE;
            Bytes = 0;
            BufferedDuration = 0;
            CurTime = 0;
        }

        public void Enqueue()
        {
            Enqueue(demuxer.packet);
            demuxer.packet = av_packet_alloc();
        }
        public void Enqueue(AVPacket* packet)
        {
            lock (packets)
            {
                packets.Enqueue((IntPtr)packet);

                if (packet->dts != AV_NOPTS_VALUE)
                {
                    LastTimestamp = (long)(packet->dts * demuxer.AVStreamToStream[packet->stream_index].Timebase);

                    if (FirstTimestamp == AV_NOPTS_VALUE)
                    {
                        FirstTimestamp = LastTimestamp;
                        UpdateCurTime();
                    }
                    else
                        BufferedDuration = LastTimestamp - FirstTimestamp;
                }

                Bytes += packet->size;
            }
        }

        public AVPacket* Dequeue()
        {
            lock(packets)
                if (packets.TryDequeue(out IntPtr packetPtr))
                {
                    AVPacket* packet = (AVPacket*)packetPtr;

                    if (packet->dts != AV_NOPTS_VALUE)
                    {
                        FirstTimestamp = (long)(packet->dts * demuxer.AVStreamToStream[packet->stream_index].Timebase);
                        UpdateCurTime();
                    }

                    return (AVPacket*)packetPtr;
                }

            return null;
        }

        public AVPacket* Peek()
        {
            if (packets.TryPeek(out IntPtr packetPtr))
                return (AVPacket*)packetPtr;

            return null;
        }

        internal void UpdateCurTime()
        {
            if (demuxer.HLSPlaylist != null)
            {
                if (demuxer.hlsStartTime != AV_NOPTS_VALUE)
                {
                    if (FirstTimestamp < demuxer.hlsStartTime)
                    {
                        demuxer.Duration += demuxer.hlsStartTime - FirstTimestamp;
                        demuxer.hlsStartTime = FirstTimestamp;
                    }
                    CurTime = LastTimestamp - demuxer.hlsStartTime - BufferedDuration;
                    CurTime = FirstTimestamp - demuxer.hlsStartTime;
                }
            }
            else
                CurTime = FirstTimestamp - demuxer.StartTime;

            BufferedDuration = LastTimestamp - FirstTimestamp;
        }
    }
}