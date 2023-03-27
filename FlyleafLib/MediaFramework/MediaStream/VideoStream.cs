using System;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream
{
    public unsafe class VideoStream : StreamBase
    {
        public AspectRatio                  AspectRatio         { get; set; }
        public string                       ColorRange          { get; set; }
        public string                       ColorSpace          { get; set; }
        public AVComponentDescriptor[]      Comps               { get; set; }
        public double                       FPS                 { get; set; }
        public long                         FrameDuration       { get ;set; }
        public int                          Height              { get; set; }
        public bool                         IsPlanar            { get; set; }
        public bool                         IsRGB               { get; set; }
        public int                          PixelBits           { get; set; }
        public AVPixelFormat                PixelFormat         { get; set; }
        public AVPixFmtDescriptor*          PixelFormatDesc     { get; set; }
        public string                       PixelFormatStr      { get; set; }
        public PixelFormatType              PixelFormatType     { get; set; }
        public int                          TotalFrames         { get; set; }
        public int                          Width               { get; set; }

        public override string GetDump() { return $"[{Type} #{StreamIndex}] {Codec} {PixelFormatStr} {Width}x{Height} @ {FPS.ToString("#.###")} | [Color: {ColorSpace}] [BR: {BitRate}] | {Utils.TicksToTime((long)(AVStream->start_time * Timebase))}/{Utils.TicksToTime((long)(AVStream->duration * Timebase))} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}"; }
        public VideoStream() { }
        public VideoStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
        {
            Demuxer = demuxer;
            AVStream = st;
            Refresh();
        }

        public void Refresh(AVPixelFormat format = AVPixelFormat.AV_PIX_FMT_NONE)
        {
            base.Refresh();

            PixelFormat     = format == AVPixelFormat.AV_PIX_FMT_NONE ? (AVPixelFormat) Enum.ToObject(typeof(AVPixelFormat), AVStream->codecpar->format) : format;
            PixelFormatStr  = PixelFormat.ToString().Replace("AV_PIX_FMT_","").ToLower();
            PixelFormatType = PixelFormatType.Software_Sws;

            Width           = AVStream->codecpar->width;
            Height          = AVStream->codecpar->height;
            FPS             = av_q2d(AVStream->avg_frame_rate) > 0 ? av_q2d(AVStream->avg_frame_rate) : av_q2d(AVStream->r_frame_rate);
            FrameDuration   = FPS > 0 ? (long) (10000000 / FPS) : 0;
            TotalFrames     = AVStream->duration > 0 && FrameDuration > 0 ? (int) (AVStream->duration * Timebase / FrameDuration) : (FrameDuration > 0 ? (int) (Demuxer.Duration / FrameDuration) : 0);

            var gcd = Utils.GCD(Width, Height);
            if (gcd != 0)
                AspectRatio = new AspectRatio(Width / gcd , Height / gcd);

            if (PixelFormat != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                ColorRange = AVStream->codecpar->color_range == AVColorRange.AVCOL_RANGE_JPEG ? "FULL" : "LIMITED";

                if (AVStream->codecpar->color_space == AVColorSpace.AVCOL_SPC_BT470BG)
                    ColorSpace = "BT601";
                else if (AVStream->codecpar->color_space == AVColorSpace.AVCOL_SPC_BT709)
                    ColorSpace = "BT709";
                else if (AVStream->codecpar->color_space == AVColorSpace.AVCOL_SPC_BT2020_CL || AVStream->codecpar->color_space == AVColorSpace.AVCOL_SPC_BT2020_NCL)
                    ColorSpace = "BT2020";
                else
                {
                    if (Width > 1024 || Height >= 600)
                        ColorSpace = "BT709";
                    else
                        ColorSpace = "BT601";
                }

                AVPixFmtDescriptor* pixFmtDesc = av_pix_fmt_desc_get((AVPixelFormat) Enum.ToObject(typeof(AVPixelFormat), PixelFormat));
                PixelFormatDesc = pixFmtDesc;
                Comps = PixelFormatDesc->comp.ToArray();
                PixelBits= Comps[0].depth;
                IsPlanar = (pixFmtDesc->flags & AV_PIX_FMT_FLAG_PLANAR) != 0;
                IsRGB    = (pixFmtDesc->flags & AV_PIX_FMT_FLAG_RGB   ) != 0;
                    
                bool isYuv = System.Text.RegularExpressions.Regex.IsMatch(PixelFormat.ToString(), "YU|YV", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // YUV Planar or Packed with half U/V (No Semi-Planar Support for Software)
                if (isYuv && pixFmtDesc->nb_components == 3 && (Comps[0].depth == 8 && Comps[1].depth == 8 && Comps[2].depth == 8))
                    PixelFormatType = PixelFormatType.Software_Handled;
            }
        }
    }
}