using System;

namespace FlyleafLib.Plugins.MediaStream
{
    public class SubtitleStream : StreamBase
    {
        public string   UrlName     { get; set; }
        public string   Rating      { get; set; }
        public bool     Downloaded  { get; set; }
        public bool     Converted   { get; set; }
        public long     Delay       { get; set; }

        public virtual string GetDump() { return $"[Subs ] {DecoderInput?.Url}"; }
    }
}