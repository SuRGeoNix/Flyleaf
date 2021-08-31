using System;
using System.Collections.Generic;
using System.Diagnostics;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.ffmpegEx;

using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream
{
    public abstract unsafe class StreamBase
    {
        public Demuxer                      Demuxer             { get; internal set; }
        public AVStream*                    AVStream            { get; internal set; }
        internal HLSPlaylist*               HLSPlaylist         { get; set; }
        public int                          StreamIndex         { get; internal set; } = -1;
        public double                       Timebase            { get; internal set; }

        public bool                         Enabled             { get; internal set; }
        public long                         BitRate             { get; internal set; }
        public Language                     Language            { get; internal set; }
        public string                       Codec               { get; internal set; }
        

        public AVCodecID                    CodecID             { get; internal set; }
        public long                         StartTime           { get; internal set; }
        public long                         Duration            { get; internal set; }
        public Dictionary<string, string>   Metadata            { get; internal set; }
        public MediaType                    Type                { get; internal set; }

        public abstract string GetDump();
        public StreamBase() { }
        public StreamBase(Demuxer demuxer, AVStream* st)
        {
            Demuxer     = demuxer;
            AVStream    = st;
            BitRate     = st->codecpar->bit_rate;
            CodecID     = st->codecpar->codec_id;
            Codec       = avcodec_get_name(st->codecpar->codec_id);
            StreamIndex = st->index;
            Timebase    = av_q2d(st->time_base) * 10000.0 * 1000.0;
            StartTime   = st->start_time != AV_NOPTS_VALUE && Demuxer.hlsCtx == null ? (long)(st->start_time * Timebase) : 0;
            Duration    = (long)(st->duration * Timebase);
            
            if (demuxer.hlsCtx != null)
                for (int i=0; i<demuxer.hlsCtx->n_playlists; i++)
                    for (int l=0; l<demuxer.hlsCtx->playlists[i]->n_main_streams; l++)
                        if (demuxer.hlsCtx->playlists[i]->main_streams[l]->index == StreamIndex)
                        {
                            Debug.WriteLine($"Stream #{StreamIndex} Found in playlist {i}");
                            HLSPlaylist = demuxer.hlsCtx->playlists[i];
                            break;
                        }

            Metadata    = new Dictionary<string, string>();

            AVDictionaryEntry* b = null;
            while (true)
            {
                b = av_dict_get(st->metadata, "", b, AV_DICT_IGNORE_SUFFIX);
                if (b == null) break;
                Metadata.Add(Utils.BytePtrToStringUTF8(b->key), Utils.BytePtrToStringUTF8(b->value));
            }

            foreach (var kv in Metadata)
                if (kv.Key.ToLower() == "language" || kv.Key.ToLower() == "lang") { Language = Language.Get(kv.Value); break; }

            // We consider default language english?
            if (Language == null || Language == Language.Get("und")) Language = Language.Get("eng");
        }
    }
}