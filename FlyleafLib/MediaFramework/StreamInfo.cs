using System;
using System.Collections.Generic;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;

namespace FlyleafLib.MediaFramework
{
    public enum PixelFormatType
    {
        Hardware,
        Software_Handled,
        Software_Sws
    }

    public unsafe class StreamInfo
    {
        // All
        public AVMediaType                  Type                { get; private set; }
        public AVCodecID                    CodecID             { get; private set; }
        public string                       CodecName           { get; private set; } //{ get { return CodecID.ToString().Replace("AV_CODEC_ID_", ""); } }
        public int                          StreamIndex         { get; private set; }
        public double                       Timebase            { get; private set; }
        public string                       Language            { get; private set; }

        public long                         BitRate             { get; private set; }
        public long                         Duration            { get; private set; }
        public long                         StartTime           { get; private set; }
        
        public Dictionary<string, string>   Metadata            { get; private set; }

        // Video
        public PixelFormatType              PixelFormatType     { get; private set; }
        public AVPixelFormat                PixelFormat         { get; private set; }
        public string                       PixelFormatStr      { get; private set; }
        public AVPixFmtDescriptor*          PixelFormatDesc     { get; private set; }
        public int                          Comp0Step           { get; private set; }
        public int                          Comp1Step           { get; private set; }
        public int                          Comp2Step           { get; private set; }
        public int                          PixelBits           { get; private set; }
        public bool                         IsPlanar            { get; private set; }
        public bool                         IsRGB               { get; private set; }
        public string                       ColorRange          { get; private set; }
        public string                       ColorSpace          { get; private set; }
        public int                          Height              { get; private set; }
        public int                          Width               { get; private set; }
        //public long                         VideoBitRate        { get; private set; }
        public double                       FPS                 { get; private set; }
        public AspectRatio                  AspectRatio         { get; private set; }// = 16f/9f;

        // Audio
        public AVSampleFormat               SampleFormat        { get; private set; }
        public string                       SampleFormatStr     { get; private set; }
        public int                          SampleRate          { get; private set; }
        public ulong                        ChannelLayout       { get; private set; }
        public string                       ChannelLayoutStr    { get; private set; }
        public int                          Channels            { get; private set; }
        public int                          Bits                { get; private set; }
        //public long                         AudioBitRate        { get; private set; }

        public override string ToString()
        {
            if (Type == AVMEDIA_TYPE_AUDIO)
                return $"[#{StreamIndex} Audio{(Language != null ? "-" + Language : "")}] {CodecName} {SampleFormatStr}@{Bits} {SampleRate/1000}KHz {ChannelLayoutStr} | {BitRate}";
            else if (Type == AVMEDIA_TYPE_VIDEO)
                return $"[#{StreamIndex} Video] {CodecName} {PixelFormatStr} {Width}x{Height} @ {FPS.ToString("#.###")} | {BitRate}";
            else
                return $"[#{StreamIndex}  Subs{(Language != null ? "-" + Language : "")}] {CodecName}";
        }

        public static StreamInfo Get(AVStream* st)
        {
            StreamInfo si = new StreamInfo();

            si.Type         = st->codecpar->codec_type;
            si.CodecID      = st->codecpar->codec_id;
            si.CodecName    = avcodec_get_name(st->codecpar->codec_id);
            si.StreamIndex  = st->index;
            si.Timebase     = av_q2d(st->time_base) * 10000.0 * 1000.0;
            si.Duration     = (long)(st->duration * si.Timebase);
            si.StartTime    = (st->start_time != AV_NOPTS_VALUE) ? (long)(st->start_time * si.Timebase) : 0;
            si.BitRate      = st->codecpar->bit_rate;

            if (si.Type == AVMEDIA_TYPE_VIDEO)
            {
                si.PixelFormat      = (AVPixelFormat) Enum.ToObject(typeof(AVPixelFormat), st->codecpar->format);
                si.PixelFormatStr   = si.PixelFormat.ToString().Replace("AV_PIX_FMT_","").ToLower();
                si.PixelFormatType  = PixelFormatType.Software_Sws;

                si.Width            = st->codecpar->width;
                si.Height           = st->codecpar->height;
                si.FPS              = av_q2d(st->r_frame_rate);
                var gcd = Utils.GCD(si.Width, si.Height);
                si.AspectRatio      = new AspectRatio(si.Width / gcd , si.Height / gcd);

                if (si.PixelFormat != AVPixelFormat.AV_PIX_FMT_NONE)
                {
                    si.ColorRange = st->codecpar->color_range == AVColorRange.AVCOL_RANGE_JPEG ? "FULL" : "LIMITED";

                    if (st->codecpar->color_space == AVColorSpace.AVCOL_SPC_BT470BG)
                        si.ColorSpace = "BT601";
                    if (si.Width > 1024 || si.Height >= 600)
                        si.ColorSpace = "BT709";
                    else
                        si.ColorSpace = "BT601";

                    AVPixFmtDescriptor* pixFmtDesc = av_pix_fmt_desc_get((AVPixelFormat) Enum.ToObject(typeof(AVPixelFormat), si.PixelFormat));
                    si.PixelFormatDesc = pixFmtDesc;
                    var comp0 = pixFmtDesc->comp.ToArray()[0];
                    var comp1 = pixFmtDesc->comp.ToArray()[1];
                    var comp2 = pixFmtDesc->comp.ToArray()[2];

                    si.PixelBits= comp0.depth;
                    si.IsPlanar = (pixFmtDesc->flags & AV_PIX_FMT_FLAG_PLANAR) != 0;
                    si.IsRGB    = (pixFmtDesc->flags & AV_PIX_FMT_FLAG_RGB   ) != 0;

                    si.Comp0Step = comp0.step;
                    si.Comp1Step = comp1.step;
                    si.Comp2Step = comp2.step;
                    
                    bool isYuv = System.Text.RegularExpressions.Regex.IsMatch(si.PixelFormat.ToString(), "YU|YV", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // YUV Planar or Packed with half U/V (No Semi-Planar Support for Software)
                    if (isYuv && pixFmtDesc->nb_components == 3 && 
                       (comp0.depth == 8 && comp1.depth == 8 && comp2.depth == 8))/* 
                      ((comp0.step == 1 && comp1.step == 1 && comp2.step == 1) || 
                       (comp0.step == 2 && comp1.step == 4 && comp2.step == 4)))*/
                        si.PixelFormatType = PixelFormatType.Software_Handled;
                }
            }
            else if (si.Type == AVMEDIA_TYPE_AUDIO)
            {
                si.SampleFormat = (AVSampleFormat) Enum.ToObject(typeof(AVSampleFormat), st->codecpar->format);
                si.SampleFormatStr = si.SampleFormat.ToString().Replace("AV_SAMPLE_FMT_","").ToLower();
                si.SampleRate   = st->codecpar->sample_rate;
                si.ChannelLayout= st->codecpar->channel_layout;
                si.Channels     = st->codecpar->channels;
                si.Bits         = st->codecpar->bits_per_coded_sample;

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

            foreach (var kv in si.Metadata)
                if (kv.Key.ToLower() == "language" || kv.Key.ToLower() == "lang") { si.Language = kv.Value; break; }

            return si;
        }

        public string GetDump()
        {
            string dump = "";

            if (Type == AVMEDIA_TYPE_AUDIO)
                dump = $"[#{StreamIndex} Audio{(Language != null ? "-" + Language : "")}] {CodecName} {SampleFormatStr}@{Bits} {SampleRate/1000}KHz {ChannelLayoutStr} | {BitRate}";
            else if (Type == AVMEDIA_TYPE_VIDEO)
                dump = $"[#{StreamIndex} Video] {CodecName} {PixelFormatStr} {Width}x{Height} @ {FPS.ToString("#.###")} | {BitRate}";
                
            else if (Type == AVMEDIA_TYPE_SUBTITLE)
                dump = $"[#{StreamIndex}  Subs{(Language != null ? "-" + Language : "")}] {CodecName}";

            return dump;
        }

        public static void PrintDump(StreamInfo si) { Console.WriteLine(si.GetDump()); }

        public static void Fill(Demuxer demuxer)
        {
            demuxer.streams = new StreamInfo[demuxer.fmtCtx->nb_streams];
            for (int i = 0; i < demuxer.fmtCtx->nb_streams; i++)
            {
                demuxer.streams[i] = Get(demuxer.fmtCtx->streams[i]);
                if (demuxer.streams[i].Duration <= 0) demuxer.streams[i].Duration = demuxer.decCtx.demuxer.fmtCtx->duration * 10;
            }
        }
    }
}