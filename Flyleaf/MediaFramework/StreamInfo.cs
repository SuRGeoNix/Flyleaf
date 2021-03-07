using System;
using System.Collections.Generic;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;

namespace SuRGeoNix.Flyleaf.MediaFramework
{
    public unsafe class StreamInfo
    {
        // All
        public AVMediaType                  Type                { get; private set; }
        public AVCodecID                    CodecID             { get; private set; }
        public string                       CodecName           { get; private set; } //{ get { return CodecID.ToString().Replace("AV_CODEC_ID_", ""); } }
        public int                          StreamIndex         { get; private set; }
        public double                       Timebase            { get; private set; }

        public long                         BitRate             { get; private set; }
        public long                         DurationTicks       { get; private set; }
        public long                         StartTime           { get; private set; }
        
        public Dictionary<string, string>   Metadata            { get; private set; }

        // Video
        public AVPixelFormat                PixelFormat         { get; private set; }
        public string                       PixelFormatStr      { get { return PixelFormat.ToString().Replace("AV_PIX_FMT_",""); } }
        public int                          Height              { get; private set; }
        public int                          Width               { get; private set; }
        public long                         VideoBitRate        { get; private set; }
        public double                       FPS                 { get; private set; }
        public AVRational                   AspectRatio         { get; private set; }

        // Audio
        public AVSampleFormat               SampleFormat        { get; private set; }
        public string                       SampleFormatStr     { get { return SampleFormat.ToString().Replace("AV_SAMPLE_FMT_",""); } }
        public int                          SampleRate          { get; private set; }
        public ulong                        ChannelLayout       { get; private set; }
        public string                       ChannelLayoutStr    { get; private set; }
        public int                          Channels            { get; private set; }
        public int                          Bits                { get; private set; }
        public long                         AudioBitRate        { get; private set; }

        public static StreamInfo Get(AVStream* st)
        {
            StreamInfo si = new StreamInfo();

            si.Type             = st->codecpar->codec_type;
            si.CodecID          = st->codecpar->codec_id;
            si.CodecName        = avcodec_get_name(st->codecpar->codec_id);
            si.StreamIndex      = st->index;
            si.Timebase         = av_q2d(st->time_base) * 10000.0 * 1000.0;
            si.DurationTicks    = (long)(st->duration * si.Timebase);
            si.StartTime        = (st->start_time != AV_NOPTS_VALUE) ? (long)(st->start_time * si.Timebase) : 0;
            si.BitRate          = st->codecpar->bit_rate;

            if (si.Type == AVMEDIA_TYPE_VIDEO)
            {
                si.PixelFormat  = (AVPixelFormat) Enum.ToObject(typeof(AVPixelFormat), st->codecpar->format);
                si.Width        = st->codecpar->width;
                si.Height       = st->codecpar->height;
                si.FPS          = av_q2d(st->r_frame_rate);
                si.AspectRatio  = st->codecpar->sample_aspect_ratio;
                si.VideoBitRate = st->codecpar->bit_rate;
            }
            else if (si.Type == AVMEDIA_TYPE_AUDIO)
            {
                si.SampleFormat = (AVSampleFormat) Enum.ToObject(typeof(AVSampleFormat), st->codecpar->format);
                si.SampleRate   = st->codecpar->sample_rate;
                si.ChannelLayout= st->codecpar->channel_layout;
                si.Channels     = st->codecpar->channels;
                si.Bits         = st->codecpar->bits_per_coded_sample;
                si.AudioBitRate = st->codecpar->bit_rate;

                byte[] buf = new byte[50];
                fixed (byte* bufPtr = buf)
                {
                    av_get_channel_layout_string(bufPtr, 50, si.Channels, si.ChannelLayout);
                    si.ChannelLayoutStr = Utils.BytePtrToStringUTF8(bufPtr);
                }
            }
            
            si.Metadata = new Dictionary<string, string>();

            AVDictionaryEntry* b = null;
            while (true)
            {
                b = av_dict_get(st->metadata, "", b, AV_DICT_IGNORE_SUFFIX);
                if (b == null) break;
                si.Metadata.Add(Utils.BytePtrToStringUTF8(b->key), Utils.BytePtrToStringUTF8(b->value));
            }

            //foreach (KeyValuePair<string, string> metaEntry in streamInfo.metadata)
                //Log($"{metaEntry.Key} -> {metaEntry.Value}");

            return si;
        }

        public string GetDump()
        {
            string dump = "";

            if (Type == AVMEDIA_TYPE_AUDIO)
                dump = $"[#{StreamIndex} Audio] {CodecName} {SampleFormatStr}@{Bits} {SampleRate/1000}KHz {ChannelLayoutStr} | {AudioBitRate}";
            else if (Type == AVMEDIA_TYPE_VIDEO)
                dump = $"[#{StreamIndex} Video] {CodecName} {PixelFormatStr} {Width}x{Height} @ {FPS.ToString("#.###")} ({AspectRatio.den}/{AspectRatio.num}) | {VideoBitRate}";
            else if (Type == AVMEDIA_TYPE_SUBTITLE)
                dump = $"[#{StreamIndex}  Subs] {CodecName} " + (Metadata.ContainsKey("language") ? Metadata["language"] : (Metadata.ContainsKey("lang") ? Metadata["language"] : ""));

            return dump;
        }

        public static void Dump(StreamInfo si)
        {
            if (si.Type == AVMEDIA_TYPE_AUDIO)
                Console.WriteLine($"[#{si.StreamIndex} Audio] {si.CodecName} {si.SampleFormatStr}@{si.Bits} {si.SampleRate/1000}KHz {si.ChannelLayoutStr} | {si.AudioBitRate}");
            else if (si.Type == AVMEDIA_TYPE_VIDEO)
                Console.WriteLine($"[#{si.StreamIndex} Video] {si.CodecName} {si.PixelFormatStr} {si.Width}x{si.Height} @ {si.FPS.ToString("#.###")} ({si.AspectRatio.den}/{si.AspectRatio.num}) | {si.VideoBitRate}");
            else if (si.Type == AVMEDIA_TYPE_SUBTITLE)
                Console.WriteLine($"[#{si.StreamIndex}  Subs] {si.CodecName} " + (si.Metadata.ContainsKey("language") ? si.Metadata["language"] : (si.Metadata.ContainsKey("lang") ? si.Metadata["language"] : "")));
        }

        public static void Fill(Demuxer demuxer)
        {
            Console.WriteLine($"[# Format] {Utils.BytePtrToStringUTF8(demuxer.fmtCtx->iformat->long_name)}/{Utils.BytePtrToStringUTF8(demuxer.fmtCtx->iformat->name)} | {Utils.BytePtrToStringUTF8(demuxer.fmtCtx->iformat->extensions)} | {new TimeSpan(demuxer.fmtCtx->start_time * 10)}/{new TimeSpan(demuxer.fmtCtx->duration * 10)}");

            demuxer.streams = new StreamInfo[demuxer.fmtCtx->nb_streams];
            for (int i=0; i<demuxer.fmtCtx->nb_streams; i++)
            {
                demuxer.streams[i] = Get(demuxer.fmtCtx->streams[i]);
                if (demuxer.streams[i].DurationTicks <= 0) demuxer.streams[i].DurationTicks = demuxer.decCtx.demuxer.fmtCtx->duration * 10;
                Dump(demuxer.streams[i]);                
            }
                
        }
    }
}