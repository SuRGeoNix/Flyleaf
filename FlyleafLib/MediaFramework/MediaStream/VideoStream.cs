using System;

using FFmpeg.AutoGen;
using FlyleafLib.MediaFramework.MediaDemuxer;
using static FFmpeg.AutoGen.ffmpeg;

namespace FlyleafLib.MediaFramework.MediaStream
{
    public unsafe class VideoStream : StreamBase
    {

        /// <summary>
        /// Stream's Movie info from the current plugin's Playlist
        /// </summary>
        public Movie    Movie       { get; set; } = new Movie();

        public AspectRatio                  AspectRatio         { get; set; }
        public string                       ColorRange          { get; set; }
        public string                       ColorSpace          { get; set; }
        public int                          Comp0Step           { get; set; }
        public int                          Comp1Step           { get; set; }
        public int                          Comp2Step           { get; set; }
        public double                       FPS                 { get; set; }
        public int                          Height              { get; set; }
        public bool                         IsPlanar            { get; set; }
        public bool                         IsRGB               { get; set; }
        public int                          PixelBits           { get; set; }
        public AVPixelFormat                PixelFormat         { get; set; }
        public AVPixFmtDescriptor*          PixelFormatDesc     { get; set; }
        public string                       PixelFormatStr      { get; set; }
        public PixelFormatType              PixelFormatType     { get; set; }
        public int                          Width               { get; set; }

        public override string GetDump() { return $"[{Type} #{StreamIndex}] {CodecName} {PixelFormatStr} {Width}x{Height} @ {FPS.ToString("#.###")} | [BR: {BitRate}] | {Utils.TicksToTime((long)(AVStream->start_time * Timebase))}/{Utils.TicksToTime((long)(AVStream->duration * Timebase))} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}"; }
        public VideoStream() { }
        public VideoStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
        {
            Type            = MediaType.Video;
            PixelFormat     = (AVPixelFormat) Enum.ToObject(typeof(AVPixelFormat), st->codecpar->format);
            PixelFormatStr  = PixelFormat.ToString().Replace("AV_PIX_FMT_","").ToLower();
            PixelFormatType = PixelFormatType.Software_Sws;

            Width           = st->codecpar->width;
            Height          = st->codecpar->height;
            FPS             = av_q2d(st->r_frame_rate);

            var gcd = Utils.GCD(Width, Height);
            if (gcd != 0)
                AspectRatio = new AspectRatio(Width / gcd , Height / gcd);

            if (PixelFormat != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                ColorRange = st->codecpar->color_range == AVColorRange.AVCOL_RANGE_JPEG ? "FULL" : "LIMITED";

                if (st->codecpar->color_space == AVColorSpace.AVCOL_SPC_BT470BG)
                    ColorSpace = "BT601";
                if (Width > 1024 || Height >= 600)
                    ColorSpace = "BT709";
                else
                    ColorSpace = "BT601";

                AVPixFmtDescriptor* pixFmtDesc = av_pix_fmt_desc_get((AVPixelFormat) Enum.ToObject(typeof(AVPixelFormat), PixelFormat));
                PixelFormatDesc = pixFmtDesc;
                var comp0 = pixFmtDesc->comp.ToArray()[0];
                var comp1 = pixFmtDesc->comp.ToArray()[1];
                var comp2 = pixFmtDesc->comp.ToArray()[2];

                PixelBits= comp0.depth;
                IsPlanar = (pixFmtDesc->flags & AV_PIX_FMT_FLAG_PLANAR) != 0;
                IsRGB    = (pixFmtDesc->flags & AV_PIX_FMT_FLAG_RGB   ) != 0;

                Comp0Step = comp0.step;
                Comp1Step = comp1.step;
                Comp2Step = comp2.step;
                    
                bool isYuv = System.Text.RegularExpressions.Regex.IsMatch(PixelFormat.ToString(), "YU|YV", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // YUV Planar or Packed with half U/V (No Semi-Planar Support for Software)
                if (isYuv && pixFmtDesc->nb_components == 3 && (comp0.depth == 8 && comp1.depth == 8 && comp2.depth == 8))
                    PixelFormatType = PixelFormatType.Software_Handled;
            }
        }
    }
}