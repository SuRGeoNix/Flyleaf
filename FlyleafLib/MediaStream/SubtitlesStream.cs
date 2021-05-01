using System;

using FFmpeg.AutoGen;

namespace FlyleafLib.MediaStream
{
    public unsafe class SubtitlesStream : StreamBase
    {
        public string   UrlName     { get; set; }
        public string   Rating      { get; set; }
        public bool     Downloaded  { get; set; }
        public bool     Converted   { get; set; }
        public long     Delay       { get; set; }

        public override string GetDump() { return $"[{Type}  #{StreamIndex}{(Language == null || Language == Language.Get("und") ? "" : "-" + Language.IdSubLanguage)}] {CodecName} | {BitRate}"; }

        public SubtitlesStream() { }
        public SubtitlesStream(AVStream* st) : base(st)
        {
            Type = MediaType.Subs;
        }
    }
}
