using System;

namespace FlyleafLib.Plugins.MediaStream
{
    public class AudioStream : StreamBase
    {
        public string   SampleFormat{ get; set; }
        public int      SampleRate  { get; set; }
        public int      Channels    { get; set; }
        public int      Bits        { get; set; }
        public long     Delay       { get; set; }

        public virtual string GetDump() { return $"[Audio{(Language != null ? "-" + Language : "")}] {CodecName} {SampleFormat}@{Bits} {SampleRate/1000}KHz | {BitRate}"; }
    }
}