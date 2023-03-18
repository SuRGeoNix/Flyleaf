using System;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream
{
    public unsafe class AudioStream : StreamBase
    {
        public int              Bits                { get; set; }
        public int              Channels            { get; set; }
        public ulong            ChannelLayout       { get; set; }
        public string           ChannelLayoutStr    { get; set; }
        public AVSampleFormat   SampleFormat        { get; set; }
        public string           SampleFormatStr     { get; set; }
        public int              SampleRate          { get; set; }
        public AVCodecID        CodecIDOrig         { get; set; }

        public override string GetDump() { return $"[{Type} #{StreamIndex}-{Language.IdSubLanguage}{(Title != null ? "(" + Title + ")" : "")}] {Codec} {SampleFormatStr}@{Bits} {SampleRate/1000}KHz {ChannelLayoutStr} | [BR: {BitRate}] | {Utils.TicksToTime((long)(AVStream->start_time * Timebase))}/{Utils.TicksToTime((long)(AVStream->duration * Timebase))} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}"; }

        public AudioStream() { }
        public AudioStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
        {
            Refresh();
        }

        public override void Refresh()
        {
            base.Refresh();

            SampleFormat    = (AVSampleFormat) Enum.ToObject(typeof(AVSampleFormat), AVStream->codecpar->format);
            SampleFormatStr = SampleFormat.ToString().Replace("AV_SAMPLE_FMT_","").ToLower();
            SampleRate      = AVStream->codecpar->sample_rate;
            ChannelLayout   = AVStream->codecpar->channel_layout;
            Channels        = AVStream->codecpar->channels;
            Bits            = AVStream->codecpar->bits_per_coded_sample;

            // https://trac.ffmpeg.org/ticket/7321
            CodecIDOrig = CodecID;
            if (CodecID == AVCodecID.AV_CODEC_ID_MP2 && (SampleFormat == AVSampleFormat.AV_SAMPLE_FMT_FLTP || SampleFormat == AVSampleFormat.AV_SAMPLE_FMT_FLT))
                CodecID = AVCodecID.AV_CODEC_ID_MP3; // OR? st->codecpar->format = (int) AVSampleFormat.AV_SAMPLE_FMT_S16P;

            byte[] buf = new byte[50];
            fixed (byte* bufPtr = buf)
            {
                av_get_channel_layout_string(bufPtr, 50, Channels, ChannelLayout);
                ChannelLayoutStr = Utils.BytePtrToStringUTF8(bufPtr);
            }
        }
    }
}
