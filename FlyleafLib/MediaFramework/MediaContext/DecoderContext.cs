﻿using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaRemuxer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaFramework.MediaContext;

public unsafe partial class DecoderContext : PluginHandler
{
    /* TODO
     *
     * 1) Lock delay on demuxers' Format Context (for network streams)
     *      Ensure we interrupt if we are planning to seek
     *      Merge Seek witih GetVideoFrame (To seek accurate or to ensure keyframe)
     *      Long delay on Enable/Disable demuxer's streams (lock might not required)
     *
     * 2) Resync implementation / CurTime
     *      Transfer player's resync implementation here
     *      Ensure we can trust CurTime on lower level (eg. on decoders - demuxers using dts)
     *
     * 3) Timestamps / Memory leak
     *      If we have embedded audio/video and the audio decoder will stop/fail for some reason the demuxer will keep filling audio packets
     *      Should also check at lower level (demuxer) to prevent wrong packet timestamps (too early or too late)
     *      This is normal if it happens on live HLS (probably an ffmpeg bug)
     */

    #region Properties
    public object               Tag                 { get; set; } // Upper Layer Object (eg. Player, Downloader) - mainly for plugins to access it
    public bool                 EnableDecoding      { get; set; }
    public new bool             Interrupt
    {
        get => base.Interrupt;
        set
        {
            base.Interrupt = value;

            if (value)
            {
                VideoDemuxer.Interrupter.ForceInterrupt = 1;
                AudioDemuxer.Interrupter.ForceInterrupt = 1;
                SubtitlesDemuxer.Interrupter.ForceInterrupt = 1;
                DataDemuxer.Interrupter.ForceInterrupt = 1;
            }
            else
            {
                VideoDemuxer.Interrupter.ForceInterrupt = 0;
                AudioDemuxer.Interrupter.ForceInterrupt = 0;
                SubtitlesDemuxer.Interrupter.ForceInterrupt = 0;
                DataDemuxer.Interrupter.ForceInterrupt = 0;
            }
        }
    }

    /// <summary>
    /// It will not resync by itself. Requires manual call to ReSync()
    /// </summary>
    public bool                 RequiresResync      { get; set; }

    public string               Extension           => VideoDemuxer.Disposed ? AudioDemuxer.Extension : VideoDemuxer.Extension;

    // Demuxers
    public Demuxer              AudioDemuxer        { get; private set; }
    public Demuxer              VideoDemuxer        { get; private set; }
    public Demuxer              SubtitlesDemuxer    { get; private set; }
    public Demuxer              DataDemuxer         { get; private set; }
    public Demuxer      GetDemuxerPtr(MediaType type) => type == MediaType.Audio ? AudioDemuxer : (type == MediaType.Video ? VideoDemuxer : (type == MediaType.Subs ? SubtitlesDemuxer : DataDemuxer));

    // Decoders
    public AudioDecoder         AudioDecoder        { get; private set; }
    public VideoDecoder         VideoDecoder        { get; internal set;}
    public SubtitlesDecoder     SubtitlesDecoder    { get; private set; }
    public DataDecoder          DataDecoder         { get; private set; }
    public DecoderBase  GetDecoderPtr(MediaType type) => type == MediaType.Audio ? AudioDecoder : (type == MediaType.Video ? VideoDecoder : (type == MediaType.Subs ? SubtitlesDecoder : DataDecoder));

    // Streams
    public AudioStream          AudioStream         => (VideoDemuxer?.AudioStream) ?? AudioDemuxer.AudioStream;
    public VideoStream          VideoStream         => VideoDemuxer?.VideoStream;
    public SubtitlesStream      SubtitlesStream     => (VideoDemuxer?.SubtitlesStream) ?? SubtitlesDemuxer.SubtitlesStream;
    public DataStream           DataStream          => (VideoDemuxer?.DataStream) ?? DataDemuxer.DataStream;

    public Tuple<ExternalAudioStream, int>      ClosedAudioStream       { get; private set; }
    public Tuple<ExternalVideoStream, int>      ClosedVideoStream       { get; private set; }
    public Tuple<ExternalSubtitlesStream, int>  ClosedSubtitlesStream   { get; private set; }
    #endregion

    #region Initialize
    LogHandler Log;
    bool shouldDispose;
    public DecoderContext(Config config = null, int uniqueId = -1, bool enableDecoding = true) : base(config, uniqueId)
    {
        Log = new(("[#" + UniqueId + "]").PadRight(8, ' ') + " [DecoderContext] ");
        Playlist.decoder    = this;

        EnableDecoding      = enableDecoding;

        AudioDemuxer        = new(Config.Demuxer, MediaType.Audio, UniqueId, EnableDecoding);
        VideoDemuxer        = new(Config.Demuxer, MediaType.Video, UniqueId, EnableDecoding);
        SubtitlesDemuxer    = new(Config.Demuxer, MediaType.Subs,  UniqueId, EnableDecoding);
        DataDemuxer         = new(Config.Demuxer, MediaType.Data, UniqueId, EnableDecoding);

        Recorder            = new(UniqueId);

        VideoDecoder        = new(Config, UniqueId);
        AudioDecoder        = new(Config, UniqueId, VideoDecoder);
        SubtitlesDecoder    = new(Config, UniqueId);
        DataDecoder         = new(Config, UniqueId);

        if (EnableDecoding && config.Player.Usage != MediaPlayer.Usage.Audio)
            VideoDecoder.CreateRenderer();

        VideoDecoder.recCompleted = RecordCompleted;
        AudioDecoder.recCompleted = RecordCompleted;
    }

    public void Initialize()
    {
        VideoDecoder.Renderer?.ClearScreen();
        RequiresResync = false;

        OnInitializing();
        Stop();
        OnInitialized();
    }
    public void InitializeSwitch()
    {
        VideoDecoder.Renderer?.ClearScreen();
        RequiresResync = false;
        ClosedAudioStream = null;
        ClosedVideoStream = null;
        ClosedSubtitlesStream = null;

        OnInitializingSwitch();
        Stop();
        OnInitializedSwitch();
    }
    #endregion

    #region Seek
    public int Seek(long ms = -1, bool forward = false, bool seekInQueue = true)
    {
        int ret = 0;

        if (ms == -1) ms = GetCurTimeMs();

        // Review decoder locks (lockAction should be added to avoid dead locks with flush mainly before lockCodecCtx)
        AudioDecoder.resyncWithVideoRequired = false; // Temporary to avoid dead lock on AudioDecoder.lockCodecCtx
        lock (VideoDecoder.lockCodecCtx)
        lock (AudioDecoder.lockCodecCtx)
        lock (SubtitlesDecoder.lockCodecCtx)
        lock (DataDecoder.lockCodecCtx)
        {
            long seekTimestamp = CalcSeekTimestamp(VideoDemuxer, ms, ref forward);

            // Should exclude seek in queue for all "local/fast" files
            lock (VideoDemuxer.lockActions)
            if (Playlist.InputType == InputType.Torrent || ms == 0 || !seekInQueue || VideoDemuxer.SeekInQueue(seekTimestamp, forward) != 0)
            {
                VideoDemuxer.Interrupter.ForceInterrupt = 1;
                OpenedPlugin.OnBuffering();
                lock (VideoDemuxer.lockFmtCtx)
                {
                    if (VideoDemuxer.Disposed) { VideoDemuxer.Interrupter.ForceInterrupt = 0; return -1; }
                    ret = VideoDemuxer.Seek(seekTimestamp, forward);
                }
            }

            VideoDecoder.Flush();
            if (ms == 0)
                VideoDecoder.keyFrameRequired = VideoDecoder.keyPacketRequired = false; // TBR

            if (AudioStream != null && AudioDecoder.OnVideoDemuxer)
            {
                AudioDecoder.Flush();
                if (ms == 0)
                    AudioDecoder.nextPts = AudioDecoder.Stream.StartTimePts;
            }

            if (SubtitlesStream != null && SubtitlesDecoder.OnVideoDemuxer)
                SubtitlesDecoder.Flush();

            if (DataStream != null && DataDecoder.OnVideoDemuxer)
                DataDecoder.Flush();
        }

        if (AudioStream != null && !AudioDecoder.OnVideoDemuxer)
        {
            AudioDecoder.Pause();
            AudioDecoder.Flush();
            AudioDemuxer.PauseOnQueueFull = true;
            RequiresResync = true;
        }

        if (SubtitlesStream != null && !SubtitlesDecoder.OnVideoDemuxer)
        {
            SubtitlesDecoder.Pause();
            SubtitlesDecoder.Flush();
            SubtitlesDemuxer.PauseOnQueueFull = true;
            RequiresResync = true;
        }

        if (DataStream != null && !DataDecoder.OnVideoDemuxer)
        {
            DataDecoder.Pause();
            DataDecoder.Flush();
            DataDemuxer.PauseOnQueueFull = true;
            RequiresResync = true;
        }

        return ret;
    }
    public int SeekAudio(long ms = -1, bool forward = false)
    {
        int ret = 0;

        if (AudioDemuxer.Disposed || AudioDecoder.OnVideoDemuxer || !Config.Audio.Enabled) return -1;

        if (ms == -1) ms = GetCurTimeMs();

        long seekTimestamp = CalcSeekTimestamp(AudioDemuxer, ms, ref forward);

        AudioDecoder.resyncWithVideoRequired = false; // Temporary to avoid dead lock on AudioDecoder.lockCodecCtx
        lock (AudioDecoder.lockActions)
        lock (AudioDecoder.lockCodecCtx)
        {
            lock (AudioDemuxer.lockActions)
                if (AudioDemuxer.SeekInQueue(seekTimestamp, forward) != 0)
                    ret = AudioDemuxer.Seek(seekTimestamp, forward);

            AudioDecoder.Flush();
            if (VideoDecoder.IsRunning)
            {
                AudioDemuxer.Start();
                AudioDecoder.Start();
            }
        }

        return ret;
    }
    public int SeekSubtitles(long ms = -1, bool forward = false)
    {
        int ret = 0;

        if (SubtitlesDemuxer.Disposed || SubtitlesDecoder.OnVideoDemuxer || !Config.Subtitles.Enabled) return -1;

        if (ms == -1) ms = GetCurTimeMs();

        long seekTimestamp = CalcSeekTimestamp(SubtitlesDemuxer, ms, ref forward);

        lock (SubtitlesDecoder.lockActions)
        lock (SubtitlesDecoder.lockCodecCtx)
        {
            // Currently disabled as it will fail to seek within the queue the most of the times
            //lock (SubtitlesDemuxer.lockActions)
                //if (SubtitlesDemuxer.SeekInQueue(seekTimestamp, forward) != 0)
            ret = SubtitlesDemuxer.Seek(seekTimestamp, forward);

            SubtitlesDecoder.Flush();
            if (VideoDecoder.IsRunning)
            {
                SubtitlesDemuxer.Start();
                SubtitlesDecoder.Start();
            }
        }

        return ret;
    }

    public int SeekData(long ms = -1, bool forward = false)
    {
        int ret = 0;

        if (DataDemuxer.Disposed || DataDecoder.OnVideoDemuxer)
            return -1;

        if (ms == -1)
            ms = GetCurTimeMs();

        long seekTimestamp = CalcSeekTimestamp(DataDemuxer, ms, ref forward);

        lock (DataDecoder.lockActions)
            lock (DataDecoder.lockCodecCtx)
            {
                ret = DataDemuxer.Seek(seekTimestamp, forward);

                DataDecoder.Flush();
                if (VideoDecoder.IsRunning)
                {
                    DataDemuxer.Start();
                    DataDecoder.Start();
                }
            }

        return ret;
    }

    public long GetCurTime()    => !VideoDemuxer.Disposed ? VideoDemuxer.CurTime : !AudioDemuxer.Disposed ? AudioDemuxer.CurTime : 0;
    public int GetCurTimeMs()   => !VideoDemuxer.Disposed ? (int)(VideoDemuxer.CurTime / 10000) : (!AudioDemuxer.Disposed ? (int)(AudioDemuxer.CurTime / 10000) : 0);

    private long CalcSeekTimestamp(Demuxer demuxer, long ms, ref bool forward)
    {
        long startTime = demuxer.hlsCtx == null ? demuxer.StartTime : demuxer.hlsCtx->first_timestamp * 10;
        long ticks = (ms * 10000) + startTime;

        if (demuxer.Type == MediaType.Audio) ticks -= Config.Audio.Delay;
        if (demuxer.Type == MediaType.Subs ) ticks -= Config.Subtitles.Delay + (2 * 1000 * 10000); // We even want the previous subtitles

        if (ticks < startTime)
        {
            ticks = startTime;
            forward = true;
        }
        else if (ticks > startTime + (!VideoDemuxer.Disposed ? VideoDemuxer.Duration : AudioDemuxer.Duration) - (50 * 10000) && demuxer.Duration > 0) // demuxer.Duration > 0 (allow blindly when duration 0)
        {
            ticks = Math.Max(startTime, startTime + demuxer.Duration - (50 * 10000));
            forward = false;
        }

        return ticks;
    }
    #endregion

    #region Start/Pause/Stop
    public void Pause()
    {
        VideoDecoder.Pause();
        AudioDecoder.Pause();
        SubtitlesDecoder.Pause();
        DataDecoder.Pause();

        VideoDemuxer.Pause();
        AudioDemuxer.Pause();
        SubtitlesDemuxer.Pause();
        DataDemuxer.Pause();
    }
    public void PauseDecoders()
    {
        VideoDecoder.Pause();
        AudioDecoder.Pause();
        SubtitlesDecoder.Pause();
        DataDecoder.Pause();
    }
    public void PauseOnQueueFull()
    {
        VideoDemuxer.PauseOnQueueFull = true;
        AudioDemuxer.PauseOnQueueFull = true;
        SubtitlesDemuxer.PauseOnQueueFull = true;
        DataDecoder.PauseOnQueueFull = true;
    }
    public void Start()
    {
        //if (RequiresResync) Resync();

        if (Config.Audio.Enabled)
        {
            AudioDemuxer.Start();
            AudioDecoder.Start();
        }

        if (Config.Video.Enabled)
        {
            VideoDemuxer.Start();
            VideoDecoder.Start();
        }

        if (Config.Subtitles.Enabled)
        {
            SubtitlesDemuxer.Start();
            SubtitlesDecoder.Start();
        }

        if (Config.Data.Enabled)
        {
            DataDemuxer.Start();
            DataDecoder.Start();
        }
    }
    public void Stop()
    {
        Interrupt = true;

        VideoDecoder.Dispose();
        AudioDecoder.Dispose();
        SubtitlesDecoder.Dispose();
        DataDecoder.Dispose();
        AudioDemuxer.Dispose();
        SubtitlesDemuxer.Dispose();
        DataDemuxer.Dispose();
        VideoDemuxer.Dispose();

        Interrupt = false;
    }
    public void StopThreads()
    {
        Interrupt = true;

        VideoDecoder.Stop();
        AudioDecoder.Stop();
        SubtitlesDecoder.Stop();
        DataDecoder.Stop();
        AudioDemuxer.Stop();
        SubtitlesDemuxer.Stop();
        DataDemuxer.Stop();
        VideoDemuxer.Stop();

        Interrupt = false;
    }
    #endregion

    public void Resync(long timestamp = -1)
    {
        bool isRunning = VideoDemuxer.IsRunning;

        if (AudioStream != null && AudioStream.Demuxer.Type != MediaType.Video && Config.Audio.Enabled)
        {
            if (timestamp == -1) timestamp = VideoDemuxer.CurTime;
            if (CanInfo) Log.Info($"Resync audio to {TicksToTime(timestamp)}");

            SeekAudio(timestamp / 10000);
            if (isRunning)
            {
                AudioDemuxer.Start();
                AudioDecoder.Start();
            }
        }

        if (SubtitlesStream != null && SubtitlesStream.Demuxer.Type != MediaType.Video && Config.Subtitles.Enabled)
        {
            if (timestamp == -1) timestamp = VideoDemuxer.CurTime;
            if (CanInfo) Log.Info($"Resync subs to {TicksToTime(timestamp)}");

            SeekSubtitles(timestamp / 10000);
            if (isRunning)
            {
                SubtitlesDemuxer.Start();
                SubtitlesDecoder.Start();
            }
        }

        if (DataStream != null && Config.Data.Enabled) // Should check if it actually an external (not embedded) stream DataStream.Demuxer.Type != MediaType.Video ?
        {
            if (timestamp == -1)
                timestamp = VideoDemuxer.CurTime;
            if (CanInfo)
                Log.Info($"Resync data to {TicksToTime(timestamp)}");

            SeekData(timestamp / 10000);
            if (isRunning)
            {
                DataDemuxer.Start();
                DataDecoder.Start();
            }
        }

        RequiresResync = false;
    }

    public void ResyncSubtitles(long timestamp = -1)
    {
        if (SubtitlesStream != null && Config.Subtitles.Enabled)
        {
            if (timestamp == -1) timestamp = VideoDemuxer.CurTime;
            if (CanInfo) Log.Info($"Resync subs to {TicksToTime(timestamp)}");

            if (SubtitlesStream.Demuxer.Type != MediaType.Video)
                SeekSubtitles(timestamp / 10000);
            else

            if (VideoDemuxer.IsRunning)
            {
                SubtitlesDemuxer.Start();
                SubtitlesDecoder.Start();
            }
        }
    }
    public void Flush()
    {
        VideoDemuxer.DisposePackets();
        AudioDemuxer.DisposePackets();
        SubtitlesDemuxer.DisposePackets();
        DataDemuxer.DisposePackets();

        VideoDecoder.Flush();
        AudioDecoder.Flush();
        SubtitlesDecoder.Flush();
        DataDecoder.Flush();
    }

    // !!! NEEDS RECODING
    public long GetVideoFrame(long timestamp = -1)
    {
        // TBR: Between seek and GetVideoFrame lockCodecCtx is lost and if VideoDecoder is running will already have decoded some frames (Currently ensure you pause VideDecoder before seek)

        int ret;
        int allowedErrors = Config.Decoder.MaxErrors;
        AVPacket* packet;
        
        lock (VideoDemuxer.lockFmtCtx)
        lock (VideoDecoder.lockCodecCtx)
        while (VideoDemuxer.VideoStream != null && !Interrupt)
        {
            if (VideoDemuxer.VideoPackets.IsEmpty)
            {
                packet = av_packet_alloc();
                VideoDemuxer.Interrupter.ReadRequest();
                ret = av_read_frame(VideoDemuxer.FormatContext, packet);
                if (ret != 0)
                {
                    av_packet_free(&packet);
                    return -1;
                }
            }
            else
                packet = VideoDemuxer.VideoPackets.Dequeue();

            if (!VideoDemuxer.EnabledStreams.Contains(packet->stream_index)) { av_packet_free(&packet); continue; }

            if (CanTrace)
            {
                var stream = VideoDemuxer.AVStreamToStream[packet->stream_index];
                long dts = packet->dts == AV_NOPTS_VALUE ? -1 : (long)(packet->dts * stream.Timebase);
                long pts = packet->pts == AV_NOPTS_VALUE ? -1 : (long)(packet->pts * stream.Timebase);
                Log.Trace($"[{stream.Type}] DTS: {(dts == -1 ? "-" : TicksToTime(dts))} PTS: {(pts == -1 ? "-" : TicksToTime(pts))} | FLPTS: {(pts == -1 ? "-" : TicksToTime(pts - VideoDemuxer.StartTime))} | CurTime: {TicksToTime(VideoDemuxer.CurTime)} | Buffered: {TicksToTime(VideoDemuxer.BufferedDuration)}");
            }

            var codecType = VideoDemuxer.FormatContext->streams[packet->stream_index]->codecpar->codec_type;

            if (codecType == AVMediaType.Video && VideoDecoder.keyPacketRequired)
            {
                if (packet->flags.HasFlag(PktFlags.Key) || packet->pts == VideoDecoder.startPts)
                {
                    VideoDecoder.keyPacketRequired = false;
                    VideoDecoder.keyFrameRequired  = Config.Decoder._KeyFrameValidation;
                }
                else
                {
                    if (CanWarn) Log.Warn("Ignoring non-key packet");
                    av_packet_free(&packet);
                    continue;
                }
            }

            if (VideoDemuxer.IsHLSLive)
                VideoDemuxer.UpdateHLSTime();

            switch (codecType)
            {
                case AVMediaType.Audio:
                    if (timestamp == -1 || (long)(packet->pts * AudioStream.Timebase) - VideoDemuxer.StartTime + (VideoStream.FrameDuration / 2) > timestamp)
                        VideoDemuxer.AudioPackets.Enqueue(packet);
                    else
                        av_packet_free(&packet);

                    continue;

                case AVMediaType.Subtitle:
                    if (timestamp == -1 || (long)(packet->pts * SubtitlesStream.Timebase) - VideoDemuxer.StartTime + (VideoStream.FrameDuration / 2) > timestamp)
                        VideoDemuxer.SubtitlesPackets.Enqueue(packet);
                    else
                        av_packet_free(&packet);

                    continue;

                case AVMediaType.Data: // this should catch the data stream packets until we have a valid vidoe keyframe (it should fill the pts if NOPTS with lastVideoPacketPts similarly to the demuxer)
                    if ((timestamp == -1 && VideoDecoder.StartTime != NoTs) || (long)(packet->pts * DataStream.Timebase) - VideoDemuxer.StartTime + (VideoStream.FrameDuration / 2) > timestamp)
                        VideoDemuxer.DataPackets.Enqueue(packet);

                    packet = av_packet_alloc();

                    continue;

                case AVMediaType.Video:
                    ret = avcodec_send_packet(VideoDecoder.CodecCtx, packet);

                    if (VideoDecoder.swFallback)
                    {
                        VideoDecoder.SWFallback();
                        ret = avcodec_send_packet(VideoDecoder.CodecCtx, packet);
                    }

                    av_packet_free(&packet);

                    if (ret != 0)
                    {
                        allowedErrors--;
                        if (CanWarn) Log.Warn($"{FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                        if (allowedErrors == 0) { Log.Error("Too many errors!");  return -1; }

                        continue;
                    }

                    //VideoDemuxer.UpdateCurTime();

                    var frame = av_frame_alloc();
                    while (VideoDemuxer.VideoStream != null && !Interrupt)
                    {
                        ret = avcodec_receive_frame(VideoDecoder.CodecCtx, frame);
                        if (ret != 0) { av_frame_unref(frame); break; }

                        if (VideoDecoder.keyFrameRequired)
                        {
                            if (!frame->flags.HasFlag(FrameFlags.Key)) { av_frame_unref(frame); continue; }
                            VideoDecoder.keyFrameRequired = false;
                        }

                        if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                            frame->pts = frame->best_effort_timestamp;
                        else if (frame->pts == AV_NOPTS_VALUE)
                        {
                            if (!VideoStream.FixTimestamps)
                            {
                                av_frame_unref(frame);
                                continue;
                            }

                            frame->pts = VideoDecoder.lastFixedPts + VideoStream.StartTimePts;
                            VideoDecoder.lastFixedPts += av_rescale_q(VideoStream.FrameDuration / 10, Engine.FFmpeg.AV_TIMEBASE_Q, VideoStream.AVStream->time_base);
                        }

                        if (!VideoDecoder.filledFromCodec)
                        {
                            ret = VideoDecoder.FillFromCodec(frame);

                            if (ret == -1234)
                            {
                                av_frame_free(&frame);
                                return -1;
                            }
                        }

                        // Accurate seek with +- half frame distance
                        // TBR: Live streams should never been seeked at first place (maybe allow HLSLive?) * can cause infinite loop
                        if (timestamp != -1 && !VideoDemuxer.IsLive && (long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime + (VideoStream.FrameDuration / 2) < timestamp)
                        {
                            av_frame_unref(frame);
                            continue;
                        }

                        //if (CanInfo) Info($"Asked for {TicksToTime(timestamp)} and got {TicksToTime((long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime)} | Diff {TicksToTime(timestamp - ((long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime))}");
                        VideoDecoder.StartTime = (long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime;

                        var mFrame = VideoDecoder.Renderer.FillPlanes(frame);
                        if (mFrame != null)
                            VideoDecoder.Frames.Enqueue(mFrame);
                        else if (VideoDecoder.handleDeviceReset)
                        {
                            VideoDecoder.HandleDeviceReset();
                            continue;
                        }

                        do
                        {
                            ret = avcodec_receive_frame(VideoDecoder.CodecCtx, frame);
                            if (ret != 0) break;
                            mFrame = VideoDecoder.Renderer.FillPlanes(frame);
                            if (mFrame != null)
                                VideoDecoder.Frames.Enqueue(mFrame);
                            else if (VideoDecoder.handleDeviceReset)
                                VideoDecoder.HandleDeviceReset();
                        } while (!VideoDemuxer.Disposed && !Interrupt);

                        av_frame_free(&frame);
                        return mFrame.timestamp;
                    }

                    av_frame_free(&frame);

                    break; // Switch break

                default:
                    av_packet_free(&packet);
                    continue;

            } // Switch

        } // While

        return -1;
    }
    public new void Dispose()
    {
        shouldDispose = true;
        Stop();
        Interrupt = true;
        VideoDecoder.DestroyRenderer();
        base.Dispose();
    }

    public void PrintStats()
    {
        string dump = "\r\n-===== Streams / Packets / Frames =====-\r\n";
        dump += $"\r\n AudioPackets      ({VideoDemuxer.AudioStreams.Count}): {VideoDemuxer.AudioPackets.Count}";
        dump += $"\r\n VideoPackets      ({VideoDemuxer.VideoStreams.Count}): {VideoDemuxer.VideoPackets.Count}";
        dump += $"\r\n SubtitlesPackets  ({VideoDemuxer.SubtitlesStreams.Count}): {VideoDemuxer.SubtitlesPackets.Count}";
        dump += $"\r\n AudioPackets      ({AudioDemuxer.AudioStreams.Count}): {AudioDemuxer.AudioPackets.Count} (AudioDemuxer)";
        dump += $"\r\n SubtitlesPackets  ({SubtitlesDemuxer.SubtitlesStreams.Count}): {SubtitlesDemuxer.SubtitlesPackets.Count} (SubtitlesDemuxer)";

        dump += $"\r\n Video Frames         : {VideoDecoder.Frames.Count}";
        dump += $"\r\n Audio Frames         : {AudioDecoder.Frames.Count}";
        dump += $"\r\n Subtitles Frames     : {SubtitlesDecoder.Frames.Count}";

        if (CanInfo) Log.Info(dump);
    }

    #region Recorder
    Remuxer Recorder;
    public event EventHandler RecordingCompleted;
    public bool IsRecording => VideoDecoder.isRecording || AudioDecoder.isRecording;
    int oldMaxAudioFrames;
    bool recHasVideo;
    public void StartRecording(ref string filename, bool useRecommendedExtension = true)
    {
        if (IsRecording) StopRecording();

        oldMaxAudioFrames = -1;
        recHasVideo = false;

        if (CanInfo) Log.Info("Record Start");

        recHasVideo = !VideoDecoder.Disposed && VideoDecoder.Stream != null;

        if (useRecommendedExtension)
            filename = $"{filename}.{(recHasVideo ? VideoDecoder.Stream.Demuxer.Extension : AudioDecoder.Stream.Demuxer.Extension)}";

        Recorder.Open(filename);

        bool failed;

        if (recHasVideo)
        {
            failed = Recorder.AddStream(VideoDecoder.Stream.AVStream) != 0;
            if (CanInfo) Log.Info(failed ? "Failed to add video stream" : "Video stream added to the recorder");
        }

        if (!AudioDecoder.Disposed && AudioDecoder.Stream != null)
        {
            failed = Recorder.AddStream(AudioDecoder.Stream.AVStream, !AudioDecoder.OnVideoDemuxer) != 0;
            if (CanInfo) Log.Info(failed ? "Failed to add audio stream" : "Audio stream added to the recorder");
        }

        if (!Recorder.HasStreams || Recorder.WriteHeader() != 0) return; //throw new Exception("Invalid remuxer configuration");

        // Check also buffering and possible Diff of first audio/video timestamp to remuxer to ensure sync between each other (shouldn't be more than 30-50ms)
        oldMaxAudioFrames = Config.Decoder.MaxAudioFrames;
        //long timestamp = Math.Max(VideoDemuxer.CurTime + VideoDemuxer.BufferedDuration, AudioDemuxer.CurTime + AudioDemuxer.BufferedDuration) + 1500 * 10000;
        Config.Decoder.MaxAudioFrames = Config.Decoder.MaxVideoFrames;

        VideoDecoder.StartRecording(Recorder);
        AudioDecoder.StartRecording(Recorder);
    }
    public void StopRecording()
    {
        if (oldMaxAudioFrames != -1) Config.Decoder.MaxAudioFrames = oldMaxAudioFrames;

        VideoDecoder.StopRecording();
        AudioDecoder.StopRecording();
        Recorder.Dispose();
        oldMaxAudioFrames = -1;
        if (CanInfo) Log.Info("Record Completed");
    }
    internal void RecordCompleted(MediaType type)
    {
        if (!recHasVideo || (recHasVideo && type == MediaType.Video))
        {
            StopRecording();
            RecordingCompleted?.Invoke(this, new EventArgs());
        }
    }
    #endregion
}
