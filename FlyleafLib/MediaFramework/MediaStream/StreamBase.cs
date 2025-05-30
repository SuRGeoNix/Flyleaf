﻿using System.Collections.Generic;

using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream;

public abstract unsafe class StreamBase : NotifyPropertyChanged
{
    public ExternalStream               ExternalStream      { get; set; }

    public Demuxer                      Demuxer             { get; internal set; }
    public AVStream*                    AVStream            { get; internal set; }
    internal playlist*                  HLSPlaylist         { get; set; }
    public int                          StreamIndex         { get; internal set; } = -1;
    public double                       Timebase            { get; internal set; }

    // TBR: To update Pop-up menu's (Player.Audio/Player.Video ... should inherit this?)
    public bool                         Enabled             { get => _Enabled; internal set => SetUI(ref _Enabled, value); }
    bool _Enabled;

    public long                         BitRate             { get; internal set; }
    public Language                     Language            { get; internal set; }
    public string                       Title               { get; internal set; }
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
        Demuxer     = demuxer;
        AVStream    = st;
    }

    public virtual void Refresh()
    {
        BitRate     = AVStream->codecpar->bit_rate;
        CodecID     = AVStream->codecpar->codec_id;
        Codec       = avcodec_get_name(AVStream->codecpar->codec_id);
        StreamIndex = AVStream->index;
        Timebase    = av_q2d(AVStream->time_base) * 10000.0 * 1000.0;
        StartTime   = AVStream->start_time != AV_NOPTS_VALUE && Demuxer.hlsCtx == null ? (long)(AVStream->start_time * Timebase) : Demuxer.StartTime;
        StartTimePts= AVStream->start_time != AV_NOPTS_VALUE ? AVStream->start_time : av_rescale_q(StartTime/10, Engine.FFmpeg.AV_TIMEBASE_Q, AVStream->time_base);
        Duration    = AVStream->duration   != AV_NOPTS_VALUE ? (long)(AVStream->duration * Timebase) : Demuxer.Duration;
        Type        = this is VideoStream ? MediaType.Video : (this is AudioStream ? MediaType.Audio : (this is SubtitlesStream ? MediaType.Subs : MediaType.Data));

        if (Demuxer.hlsCtx != null)
        {
            for (int i=0; i<Demuxer.hlsCtx->n_playlists; i++)
            {
                playlist** playlists = Demuxer.hlsCtx->playlists;
                for (int l=0; l<playlists[i]->n_main_streams; l++)
                    if (playlists[i]->main_streams[l]->index == StreamIndex)
                    {
                        Demuxer.Log.Debug($"Stream #{StreamIndex} Found in playlist {i}");
                        HLSPlaylist = playlists[i];
                        break;
                    }
            }
        }

        Metadata.Clear();

        AVDictionaryEntry* b = null;
        while (true)
        {
            b = av_dict_get(AVStream->metadata, "", b, DictReadFlags.IgnoreSuffix);
            if (b == null) break;
            Metadata.Add(Utils.BytePtrToStringUTF8(b->key), Utils.BytePtrToStringUTF8(b->value));
        }

        foreach (var kv in Metadata)
        {
            string keyLower = kv.Key.ToLower();

            if (Language == null && (keyLower == "language" || keyLower == "lang"))
                Language = Language.Get(kv.Value);
            else if (keyLower == "title")
                Title = kv.Value;
        }

        if (Language == null)
            Language = Language.Unknown;
    }
}
