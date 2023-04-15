using System;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaDecoder;

public unsafe partial class AudioDecoder
{
    AVFilterContext*        abufferCtx;
    AVFilterContext*        abufferSinkCtx;
    AVFilterGraph*          filterGraph;
    long                    filterCurSamples;
    long                    filterFirstPts;
    bool                    setFirstPts;
    object                  lockAtempo = new();

    private AVFilterContext* CreateFilter(string name, string args, AVFilterContext* prevCtx = null, string id = null)
    {
        int ret;
        AVFilterContext*    filterCtx;
        AVFilter*           filter;

        id ??= name;

        filter  = avfilter_get_by_name(name);
        if (filter == null)
            throw new Exception($"[Filter {name}] not found");

        ret     = avfilter_graph_create_filter(&filterCtx, filter, id, args, null, filterGraph);
        if (ret < 0)
            throw new Exception($"[Filter {name}] avfilter_graph_create_filter failed ({FFmpegEngine.ErrorCodeToMsg(ret)})");

        if (prevCtx == null)
            return filterCtx;

        ret     = avfilter_link(prevCtx, 0, filterCtx, 0);

        return ret != 0
            ? throw new Exception($"[Filter {name}] avfilter_link failed ({FFmpegEngine.ErrorCodeToMsg(ret)})")
            : filterCtx;
    }
    private int SetupFilters()
    {
        int ret = -1;

        try
        {
            DisposeFilters();

            AVFilterContext* linkCtx;

            filterGraph     = avfilter_graph_alloc();
            setFirstPts     = true;

            // IN (abuffersrc)
            linkCtx = abufferCtx = CreateFilter("abuffer", 
                $"channel_layout={AudioStream.ChannelLayoutStr}:sample_fmt={AudioStream.SampleFormatStr}:sample_rate={codecCtx->sample_rate}:time_base={codecCtx->time_base.num}/{codecCtx->time_base.den}");

            // USER DEFINED
            if (Config.Audio.Filters != null)
                foreach (var filter in Config.Audio.Filters)
                    try
                    {
                        linkCtx = CreateFilter(filter.Name, filter.Args, linkCtx, filter.Id);
                    }
                    catch (Exception e) { Log.Error($"{e.Message}"); }

            // SPEED (atempo)
            linkCtx = CreateFilter("atempo", $"tempo={speed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}", linkCtx);
            
            // OUT (abuffersink)
            abufferSinkCtx = CreateFilter("abuffersink", null, null);

            AVSampleFormat[] sample_fmts = new AVSampleFormat[] { AOutSampleFormat, AVSampleFormat.AV_SAMPLE_FMT_NONE };
            int[] sample_rates = new int[] { AudioStream.SampleRate, -1 };

            fixed (AVSampleFormat* ptr = &sample_fmts[0])
                ret = av_opt_set_bin(abufferSinkCtx , "sample_fmts"         , (byte*)ptr, sizeof(AVSampleFormat) * 2    , AV_OPT_SEARCH_CHILDREN);
            fixed(int* ptr = &sample_rates[0])
                ret = av_opt_set_bin(abufferSinkCtx , "sample_rates"        , (byte*)ptr, sizeof(int)                   , AV_OPT_SEARCH_CHILDREN);
            // if ch_layouts is not set, all valid channel layouts are accepted except for UNSPEC layouts, unless all_channel_counts is set
            ret = av_opt_set_int(abufferSinkCtx     , "all_channel_counts"  , 0                                         , AV_OPT_SEARCH_CHILDREN);
            ret = av_opt_set(abufferSinkCtx         , "ch_layouts"          , "stereo"                                  , AV_OPT_SEARCH_CHILDREN);
            avfilter_link(linkCtx, 0, abufferSinkCtx, 0);
            
            // GRAPH CONFIG
            ret = avfilter_graph_config(filterGraph, null);

            return ret < 0 
                ? throw new Exception($"[FilterGraph] {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})") 
                : 0;
        }
        catch (Exception e)
        {
            fixed(AVFilterGraph** filterGraphPtr = &filterGraph)
                avfilter_graph_free(filterGraphPtr);

            Log.Error($"{e.Message}");

            return ret;
        }
    }
    private void DisposeFilters()
    {
        if (filterGraph == null)
            return;
        
        fixed(AVFilterGraph** filterGraphPtr = &filterGraph)
            avfilter_graph_free(filterGraphPtr);
        
        abufferCtx      = null;
        abufferSinkCtx  = null;
        filterGraph     = null;
    }
    
    protected override void OnSpeedChanged(double value)
    {
        if (!Config.Audio.FiltersEnabled)
        {
            oldSpeed = speed;
            speed = value;

            return;
        }

        lock (lockAtempo)
        {
            oldSpeed = speed;
            speed = value;

            if (filterGraph == null)
                return;

            // TBR: To fix timestamps in Queue (should add pts in mFrame or change the way we get the pts generally)
            var frames = Frames.ToArray();
            if (Frames.TryPeek(out var mFrame) && frames.Length > 0)
            {
                long avgSamples = (long)((frames[^1].timestamp + demuxer.StartTime - Config.Audio.Delay) / AudioStream.Timebase) - filterFirstPts;
                avgSamples = filterCurSamples - av_rescale_q((long)(avgSamples / oldSpeed), AudioStream.AVStream->time_base, codecCtx->time_base);
                filterCurSamples = (long)(filterCurSamples * oldSpeed / speed);

                for (int i = frames.Length - 1; i >= 0; i--)
                {
                    long newPts         = filterFirstPts + (long)(av_rescale_q(filterCurSamples - (avgSamples * (frames.Length - i)), codecCtx->time_base, AudioStream.AVStream->time_base) * speed);
                    frames[i].timestamp = (long)((newPts * AudioStream.Timebase) - demuxer.StartTime + Config.Audio.Delay);
                    int dataLen         = Utils.Align((int) (frames[i].dataLen * oldSpeed / speed), ASampleBytes);
                    frames[i].dataLen   = dataLen;

                    fixed (byte* cBufStartPosPtr = &cBuf[0])
                    {
                        var curOffset = (long)frames[i].dataPtr - (long)cBufStartPosPtr;
                        if (oldSpeed > speed && curOffset + dataLen >= cBuf.Length)
                            frames[i].dataPtr = (IntPtr)cBufStartPosPtr;
                    }
                }
            }
            else
                filterCurSamples = (long)(filterCurSamples * oldSpeed / speed);

            string speedStr = speed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            int ret = avfilter_graph_send_command(filterGraph, "atempo", "tempo", speedStr, null, 0, 0);
            Log.Debug($"[atempo] atempo={speedStr} {(ret >=0 ? "success" : "failed")}");
        }
    }
    public int UpdateFilter(string filterId, string key, string value)
    {
        lock (lockCodecCtx)
        {
            if (filterGraph == null)
                return -1;

            int ret = avfilter_graph_send_command(filterGraph, filterId, key, value, null, 0, 0);
            Log.Info($"[{filterId}] {key}={value} {(ret >=0 ? "success" : "failed")}");

            return ret;
        }
    }
    public int ReloadFilters()
    {
        lock (lockActions)
            lock (lockCodecCtx)
                return !Engine.FFmpeg.FiltersLoaded || Config.Audio.FiltersEnabled ? -1 : SetupFilters();
    }

    private void ProcessWithFilters(AVFrame* frame)
    {
        int ret;

        if (setFirstPts && frame != null)
        {
            setFirstPts     = false;
            filterFirstPts  = frame->pts;
            filterCurSamples= 0;
        }

        if ((ret = av_buffersrc_add_frame(abufferCtx, frame)) < 0) 
        {
            Log.Warn($"[buffersrc] {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
            Status = Status.Stopping;
            return; 
        }
        
        while (true)
        {
            if (frame == null)
                frame = av_frame_alloc(); // Drain frame (TODO: Drain from main loop)

            if ((ret = av_buffersink_get_frame_flags(abufferSinkCtx, frame, 0)) < 0) // Sometimes we get AccessViolationException while we UpdateFilter (possible related with .NET7 debug only bug)
            {
                if (ret == AVERROR_EOF)
                    Status = Status.Ended;

                return; // EAGAIN (Some filters will send EAGAIN even if EOF currently we handled cause our Status will be Draining)
            }

            if (frame->pts == AV_NOPTS_VALUE)
                { av_frame_unref(frame); continue; } // ? filterCurSamples   += frame->nb_samples; 

            AudioFrame mFrame   = new();
            long newPts         = filterFirstPts + (long)(av_rescale_q(filterCurSamples, codecCtx->time_base, AudioStream.AVStream->time_base) * speed);
            mFrame.timestamp    = (long)((newPts * AudioStream.Timebase) - demuxer.StartTime + Config.Audio.Delay);
            mFrame.dataLen      = frame->nb_samples * ASampleBytes;
            filterCurSamples   += frame->nb_samples;
            
            if (CanTrace) Log.Trace($"Processes {Utils.TicksToTime(mFrame.timestamp)}");

            if (frame->nb_samples > cBufSamples)
            {
                int size    = Config.Decoder.MaxAudioFrames * mFrame.dataLen * 4;
                Log.Debug($"Re-allocating circular buffer ({frame->nb_samples} > {cBufSamples}) with {size}bytes");
                cBuf        = new byte[size];
                cBufPos     = 0;
                cBufSamples = frame->nb_samples * 4;
            }
            else if (cBufPos + mFrame.dataLen >= cBuf.Length)
                cBufPos     = 0;

            fixed (byte* circularBufferPosPtr = &cBuf[cBufPos])
                mFrame.dataPtr = (IntPtr)circularBufferPosPtr;

            Marshal.Copy((IntPtr)frame->data.ToArray()[0], cBuf, cBufPos, mFrame.dataLen);
            cBufPos += mFrame.dataLen;

            // Ensure we don't get frames with large duration (could cause a/v desync even with one xaudio buffer latency)
            //if (frame->nb_samples / codecCtx->time_base.den > 1)
                //Log.Warn($"Too many samples {frame->nb_samples} dur: {frame->nb_samples / codecCtx->time_base.den}");

            Frames.Enqueue(mFrame);
            av_frame_unref(frame);

            // Wait until Queue not Full or Stopped
            if (Frames.Count >= Config.Decoder.MaxAudioFrames)
            {
                Monitor.Exit(lockCodecCtx);
                lock (lockStatus)
                    if (Status == Status.Running)
                        Status = Status.QueueFull;

                while (Frames.Count >= Config.Decoder.MaxAudioFrames && (Status == Status.QueueFull || Status == Status.Draining))
                    Thread.Sleep(20);

                Monitor.Enter(lockCodecCtx);

                lock (lockStatus)
                {
                    if (Status == Status.QueueFull)
                        Status = Status.Running;
                    else if (Status != Status.Draining)
                        return;
                }
            }
        }
    }
}

/// <summary>
/// FFmpeg Filter
/// </summary>
public class Filter
{
    /// <summary>
    /// <para>
    /// FFmpeg valid filter id
    /// (Required only to send commands)
    /// </para>
    /// </summary>
    public string Id    { get; set; }

    /// <summary>
    /// FFmpeg valid filter name
    /// </summary>
    public string Name  { get; set; }

    /// <summary>
    /// FFmpeg valid filter args
    /// </summary>
    public string Args  { get; set; }
}
