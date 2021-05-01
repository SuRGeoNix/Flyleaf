using System;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

namespace FlyleafLib.MediaStream
{    
    public unsafe class AudioStream : StreamBase
    {
        public long     Delay       { get; set; }

        public int                          Bits                { get; set; }
        public int                          Channels            { get; set; }
        public ulong                        ChannelLayout       { get; set; }
        public string                       ChannelLayoutStr    { get; set; }
        public AVSampleFormat               SampleFormat        { get; set; }
        public string                       SampleFormatStr     { get; set; }
        public int                          SampleRate          { get; set; }

        public override string GetDump() { return $"[{Type} #{StreamIndex}{(Language == null || Language == Language.Get("und") ? "" : "-" + Language.IdSubLanguage)}] {CodecName} {SampleFormatStr}@{Bits} {SampleRate/1000}KHz {ChannelLayoutStr} | {BitRate}"; }

        public AudioStream() { }
        public AudioStream(AVStream* st) : base(st)
        {
            Type            = MediaType.Audio;
            SampleFormat    = (AVSampleFormat) Enum.ToObject(typeof(AVSampleFormat), st->codecpar->format);
            SampleFormatStr = SampleFormat.ToString().Replace("AV_SAMPLE_FMT_","").ToLower();
            SampleRate      = st->codecpar->sample_rate;
            ChannelLayout   = st->codecpar->channel_layout;
            Channels        = st->codecpar->channels;
            Bits            = st->codecpar->bits_per_coded_sample;

            byte[] buf = new byte[50];
            fixed (byte* bufPtr = buf)
            {
                av_get_channel_layout_string(bufPtr, 50, Channels, ChannelLayout);
                ChannelLayoutStr = Utils.BytePtrToStringUTF8(bufPtr);
            }
        }
    }
}
