using System;
using System.Collections.Generic;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.ffmpegEx;

using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream
{
    public abstract unsafe class StreamBase
    {
        public ExternalStream               ExternalStream      { get; set; }

        public Demuxer                      Demuxer             { get; internal set; }
        public AVStream*                    AVStream            { get; internal set; }
        internal HLSPlaylistv4*             HLSPlaylistv4       { get; set; }
        internal HLSPlaylistv5*             HLSPlaylistv5       { get; set; }
        public int                          StreamIndex         { get; internal set; } = -1;
        public double                       Timebase            { get; internal set; }

        public bool                         Enabled             { get; internal set; }
        public long                         BitRate             { get; internal set; }
        public Language                     Language            { get; internal set; }
        public string                       Codec               { get; internal set; }
        

        public AVCodecID                    CodecID             { get; internal set; }
        public long                         StartTime           { get; internal set; }
        public long                         StartTimePts        { get; internal set; }
        public long                         Duration            { get; internal set; }
        public Dictionary<string, string>   Metadata            { get; internal set; } = new Dictionary<string, string>();
        public MediaType                    Type                { get; internal set; }

        public abstract string GetDump();
        public StreamBase() { }
        public StreamBase(Demuxer demuxer, AVStream* st)
        {
            Refresh(demuxer, st);
        }

        public void Refresh(Demuxer demuxer, AVStream* st)
        {
            Demuxer     = demuxer;
            AVStream    = st;
            BitRate     = st->codecpar->bit_rate;
            CodecID     = st->codecpar->codec_id;
            Codec       = avcodec_get_name(st->codecpar->codec_id);
            StreamIndex = st->index;
            Timebase    = av_q2d(st->time_base) * 10000.0 * 1000.0;
            StartTime   = st->start_time != AV_NOPTS_VALUE && Demuxer.hlsCtx == null ? (long)(st->start_time * Timebase) : demuxer.StartTime;
            StartTimePts= st->start_time != AV_NOPTS_VALUE ? st->start_time : av_rescale_q(StartTime/10, av_get_time_base_q(), st->time_base);
            Duration    = st->duration   != AV_NOPTS_VALUE ? (long)(st->duration * Timebase) : demuxer.Duration;
            
            if (demuxer.hlsCtx != null)
            {
                if (Engine.FFmpeg.IsVer5)
                {
                    for (int i=0; i<demuxer.hlsCtx->n_playlists; i++)
                    {
                        HLSPlaylistv5** playlists = (HLSPlaylistv5**) demuxer.hlsCtx->playlists;
                        for (int l=0; l<playlists[i]->n_main_streams; l++)
                            if (playlists[i]->main_streams[l]->index == StreamIndex)
                            {
                                demuxer.Log.Debug($"Stream #{StreamIndex} Found in playlist {i}");
                                HLSPlaylistv5 = playlists[i];
                                break;
                            }   
                    }
                }
                else
                {
                    for (int i=0; i<demuxer.hlsCtx->n_playlists; i++)
                    {
                        HLSPlaylistv4** playlists = (HLSPlaylistv4**) demuxer.hlsCtx->playlists;
                        for (int l=0; l<playlists[i]->n_main_streams; l++)
                            if (playlists[i]->main_streams[l]->index == StreamIndex)
                            {
                                demuxer.Log.Debug($"Stream #{StreamIndex} Found in playlist {i}");
                                HLSPlaylistv4 = playlists[i];
                                break;
                            }   
                    }
                }
            }
                
            Metadata.Clear();

            AVDictionaryEntry* b = null;
            while (true)
            {
                b = av_dict_get(st->metadata, "", b, AV_DICT_IGNORE_SUFFIX);
                if (b == null) break;
                Metadata.Add(Utils.BytePtrToStringUTF8(b->key), Utils.BytePtrToStringUTF8(b->value));
            }

            foreach (var kv in Metadata)
                if (kv.Key.ToLower() == "language" || kv.Key.ToLower() == "lang") { Language = Language.Get(kv.Value); break; }

            if (Language == null) Language = Language.Get("und");
        }
    }
}