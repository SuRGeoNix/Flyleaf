﻿using System.Collections.Generic;
using System.Diagnostics;

namespace FlyleafLib.MediaFramework.MediaRemuxer;

public unsafe class Remuxer
{
    public int                  UniqueId            { get; set; }
    public bool                 Disposed            { get; private set; } = true;
    public string               Filename            { get; private set; }
    public bool                 HasStreams          => mapInOutStreams2.Count > 0 || mapInOutStreams.Count > 0;
    public bool                 HeaderWritten       { get; private set; }

    Dictionary<IntPtr, IntPtr>  mapInOutStreams     = new();
    Dictionary<int, IntPtr>     mapInInStream       = new();
    Dictionary<int, long>       mapInStreamToDts    = new();
    Dictionary<IntPtr, IntPtr>  mapInOutStreams2    = new();
    Dictionary<int, IntPtr>     mapInInStream2      = new();
    Dictionary<int, long>       mapInStreamToDts2   = new();

    AVFormatContext* fmtCtx;
    AVOutputFormat* fmt;

    public Remuxer(int uniqueId = -1)
        => UniqueId = uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;

    public int Open(string filename)
    {
        int ret;
        Filename = filename;

        fixed (AVFormatContext** ptr = &fmtCtx)
            ret = avformat_alloc_output_context2(ptr, null, null, Filename);

        if (ret < 0) return ret;

        fmt = fmtCtx->oformat;
        mapInStreamToDts = new Dictionary<int, long>();
        Disposed = false;

        return 0;
    }

    public int AddStream(AVStream* in_stream, bool isAudioDemuxer = false)
    {
        int ret = -1;

        if (in_stream == null || (in_stream->codecpar->codec_type != AVMediaType.Video && in_stream->codecpar->codec_type != AVMediaType.Audio)) return ret;

        AVStream *out_stream;
        var in_codecpar = in_stream->codecpar;

        out_stream = avformat_new_stream(fmtCtx, null);
        if (out_stream == null) return -1;

        ret = avcodec_parameters_copy(out_stream->codecpar, in_codecpar);
        if (ret < 0) return ret;

        // Copy metadata (currently only language)
        AVDictionaryEntry* b = null;
        while (true)
        {
            b = av_dict_get(in_stream->metadata, "", b, DictReadFlags.IgnoreSuffix);
            if (b == null) break;

            if (Utils.BytePtrToStringUTF8(b->key).ToLower() == "language" || Utils.BytePtrToStringUTF8(b->key).ToLower() == "lang")
                av_dict_set(&out_stream->metadata, Utils.BytePtrToStringUTF8(b->key), Utils.BytePtrToStringUTF8(b->value), 0);
        }

        out_stream->codecpar->codec_tag = 0;

        // TODO:
        // validate that the output format container supports the codecs (if not change the container?)
        // check whether we remux from mp4 to mpegts (requires bitstream filter h264_mp4toannexb)

        //if (av_codec_get_tag(fmtCtx->oformat->codec_tag, in_stream->codecpar->codec_id) == 0)
        //    Log("Not Found");
        //else
        //    Log("Found");

        if (isAudioDemuxer)
        {
            mapInOutStreams2.Add((IntPtr)in_stream, (IntPtr)out_stream);
            mapInInStream2.Add(in_stream->index, (IntPtr)in_stream);
        }
        else
        {
            mapInOutStreams.Add((IntPtr)in_stream, (IntPtr)out_stream);
            mapInInStream.Add(in_stream->index, (IntPtr)in_stream);
        }

        return 0;
    }

    public int WriteHeader()
    {
        if (!HasStreams) throw new Exception("No streams have been configured for the remuxer");

        int ret;

        ret = avio_open(&fmtCtx->pb, Filename, IOFlags.Write);
        if (ret < 0) { Dispose(); return ret; }

        ret = avformat_write_header(fmtCtx, null);

        if (ret < 0) { Dispose(); return ret; }

        HeaderWritten = true;

        return 0;
    }

    public int Write(AVPacket* packet, bool isAudioDemuxer = false)
    {
        lock (this)
        {
            var mapInInStream       = !isAudioDemuxer? this.mapInInStream   : mapInInStream2;
            var mapInOutStreams     = !isAudioDemuxer? this.mapInOutStreams : mapInOutStreams2;
            var mapInStreamToDts    = !isAudioDemuxer? this.mapInStreamToDts: mapInStreamToDts2;

            AVStream* in_stream     =  (AVStream*) mapInInStream[packet->stream_index];
            AVStream* out_stream    =  (AVStream*) mapInOutStreams[(IntPtr)in_stream];

            if (packet->dts != AV_NOPTS_VALUE)
            {

                if (!mapInStreamToDts.ContainsKey(in_stream->index))
                {
                    // TODO: In case of AudioDemuxer calculate the diff with the VideoDemuxer and add it in one of them - all stream - (in a way to have positive)
                    mapInStreamToDts.Add(in_stream->index, packet->dts);
                }

                packet->pts = packet->pts == AV_NOPTS_VALUE ? AV_NOPTS_VALUE : av_rescale_q_rnd(packet->pts - mapInStreamToDts[in_stream->index], in_stream->time_base, out_stream->time_base, AVRounding.NearInf | AVRounding.PassMinmax);
                packet->dts = av_rescale_q_rnd(packet->dts - mapInStreamToDts[in_stream->index], in_stream->time_base, out_stream->time_base, AVRounding.NearInf | AVRounding.PassMinmax);
            }
            else
            {
                packet->pts = packet->pts == AV_NOPTS_VALUE ? AV_NOPTS_VALUE : av_rescale_q_rnd(packet->pts, in_stream->time_base, out_stream->time_base, AVRounding.NearInf | AVRounding.PassMinmax);
                packet->dts = AV_NOPTS_VALUE;
            }

            packet->duration        = av_rescale_q(packet->duration,in_stream->time_base, out_stream->time_base);
            packet->stream_index    = out_stream->index;
            packet->pos             = -1;

            int ret = av_interleaved_write_frame(fmtCtx, packet);
            av_packet_free(&packet);

            return ret;
        }
    }

    public int WriteTrailer() => Dispose();
    public int Dispose()
    {
        if (Disposed) return -1;

        int ret = 0;

        if (fmtCtx != null)
        {
            if (HeaderWritten)
            {
                ret = av_write_trailer(fmtCtx);
                avio_closep(&fmtCtx->pb);
            }

            avformat_free_context(fmtCtx);
        }

        fmtCtx = null;
        Filename = null;
        Disposed = true;
        HeaderWritten = false;
        mapInOutStreams.Clear();
        mapInInStream.Clear();
        mapInOutStreams2.Clear();
        mapInInStream2.Clear();

        return ret;
    }

    private void Log(string msg)
        => Debug.WriteLine($"[{DateTime.Now:hh.mm.ss.fff}] [#{UniqueId}] [Remuxer] {msg}");
}
