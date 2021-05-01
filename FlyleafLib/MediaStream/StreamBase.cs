using System;
using System.Collections.Generic;
using System.IO;
using FFmpeg.AutoGen;
using FlyleafLib.MediaFramework.MediaDemuxer;
using static FFmpeg.AutoGen.ffmpeg;

namespace FlyleafLib.MediaStream
{
    public abstract unsafe class StreamBase
    {
        /// <summary>
        /// Whether the current stream is enabled
        /// </summary>
        public bool         InUse       { get => _InUse; set { _InUse = value; if (value) Used++; } }
        bool _InUse;

        /// <summary>
        /// How many times has been used
        /// </summary>
        public int          Used        { get; set; }

        /// <summary>
        /// Tag/Opaque for plugins (mainly to match streams with their own streams)
        /// </summary>
        public object       Tag         { get; set; }

        public string       Url         { get; set; }

        public Stream       Stream      { get; set; }

        public DemuxerBase  Demuxer     { get; set; }

        public AVStream*                    AVStream            { get; set; }
        public long                         BitRate             { get; set; }
        public AVCodecID                    CodecID             { get; set; }
        public string                       CodecName           { get; set; }
        public long                         Duration            { get; set; }
        public Language                     Language            { get; set; }
        public Dictionary<string, string>   Metadata            { get; set; }
        public long                         StartTime           { get; set; }
        public int                          StreamIndex         { get; set; } = -1;
        public double                       Timebase            { get; set; }
        public MediaType                    Type                { get; set; }

        public abstract string GetDump();
        public StreamBase() { }
        public StreamBase(AVStream* st)
        {
            AVStream    = st;
            BitRate     = st->codecpar->bit_rate;
            CodecID     = st->codecpar->codec_id;
            CodecName   = avcodec_get_name(st->codecpar->codec_id);
            StreamIndex = st->index;
            Timebase    = av_q2d(st->time_base) * 10000.0 * 1000.0;
            StartTime   = (st->start_time != AV_NOPTS_VALUE) ? (long)(st->start_time * Timebase) : 0;
            Duration    = (long)(st->duration * Timebase);
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
        }
    }
}