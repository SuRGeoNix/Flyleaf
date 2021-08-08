using System;

using FFmpeg.AutoGen;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream
{
    public unsafe class SubtitlesStream : StreamBase
    {
        public string   UrlName     { get; set; }
        public string   Rating      { get; set; }
        public bool     Downloaded  { get; set; }
        public bool     Converted   { get; set; }
        public long     Delay       { get; set; }

        public override string GetDump() { return $"[{Type}  #{StreamIndex}{(Language == null || Language == Language.Get("und") ? "" : "-" + Language.IdSubLanguage)}] {CodecName} | [BR: {BitRate}] | {Utils.TicksToTime((long)(AVStream->start_time * Timebase))}/{Utils.TicksToTime((long)(AVStream->duration * Timebase))} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}"; }

        public SubtitlesStream() { }
        public SubtitlesStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
        {
            Type = MediaType.Subs;
        }
    }
}
