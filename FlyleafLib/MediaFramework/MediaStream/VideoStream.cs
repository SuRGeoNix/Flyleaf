using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream;

public unsafe class VideoStream : StreamBase
{
    /* TODO
     * Color Primaries (not really required?)
     * Chroma Location (when we add support in renderer)
     */

    public AspectRatio                  AspectRatio         { get; set; }
    public AVRational                   SAR                 { get; set; }
    public ColorRange                   ColorRange          { get; set; }
    public ColorSpace                   ColorSpace          { get; set; }
    public ColorType                    ColorType           { get; set; }
    public AVColorTransferCharacteristic
                                        ColorTransfer       { get; set; }
    public DeInterlace                  FieldOrder          { get; set; }
    public double                       Rotation            { get; set; }
    public double                       FPS                 { get; set; }
    public long                         FrameDuration       { get ;set; }
    public double                       FPS2                { get; set; } // interlace
    public long                         FrameDuration2      { get ;set; } // interlace
    public uint                         Height              { get; set; }
    public HDRFormat                    HDRFormat           { get; set; }

    public AVComponentDescriptor[]      PixelComps          { get; set; }
    public int                          PixelComp0Depth     { get; set; }
    public AVPixelFormat                PixelFormat         { get; set; }
    public AVPixFmtDescriptor*          PixelFormatDesc     { get; set; }
    public string                       PixelFormatStr      { get; set; }
    public int                          PixelPlanes         { get; set; }
    public bool                         PixelInterleaved    { get; set; }
    public int                          TotalFrames         { get; set; }
    public uint                         Width               { get; set; }
    public bool                         FixTimestamps       { get; set; }

    public VideoStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
        => Type = MediaType.Video;

    /* NOTES
    * Initialize()  during Demuxer.FillInfo      (Analysed or Basic)
    * Refresh()     during Decoder.FillFromCodec (First valid frame + Format Changed)
    * 
    * Some fields might know only during Refresh, should make sure we fill them (especially if we didn't analyse the input)
    * Don't default (eg Color Range) during Initialize, wait for Refresh
    * Priorities: AVFrame => AVCodecContext => AVStream (AVCodecParameters) *Try to keep Color config from stream instead
    */

    // First time fill from AVStream's Codec Parameters | Info to help choosing stream quality mainly (not in use yet)
    public override void Initialize()
    {
        PixelFormat     = (AVPixelFormat)cp->format;
        if (PixelFormat != AVPixelFormat.None)
            AnalysePixelFormat();

        Width           = (uint)cp->width;
        Height          = (uint)cp->height;
        SAR             = av_guess_sample_aspect_ratio(null, AVStream, null);
        if (SAR.Num != 0)
        {
            int x, y;
            _ = av_reduce(&x, &y, Width * SAR.Num, Height * SAR.Den, 1024 * 1024);
            AspectRatio = new(x, y);
        }

        if (Demuxer.FormatContext->iformat->flags.HasFlag(FmtFlags.Notimestamps))
            FixTimestamps = true;

        if (Demuxer.Config.ForceFPS > 0)
            FPS = Demuxer.Config.ForceFPS;
        else
        {
            FPS = av_q2d(av_guess_frame_rate(Demuxer.FormatContext, AVStream, null));
            if (double.IsNaN(FPS) || double.IsInfinity(FPS) || FPS < 0.0)
                FPS = 0.0;
        }

        if (FPS > 0)
        {
            FrameDuration   = (long)(10_000_000 / FPS);
            TotalFrames     = (int)(Duration / FrameDuration);
        }

        FieldOrder      = cp->field_order == AVFieldOrder.Tt ? DeInterlace.TopField : (cp->field_order == AVFieldOrder.Bb ? DeInterlace.BottomField : DeInterlace.Progressive);
        ColorTransfer   = cp->color_trc;

        if (cp->color_range == AVColorRange.Mpeg)
            ColorRange = ColorRange.Limited;
        else if (cp->color_range == AVColorRange.Jpeg)
            ColorRange = ColorRange.Full;

        if (cp->color_space == AVColorSpace.Bt709)
            ColorSpace = ColorSpace.Bt709;
        else if (cp->color_space == AVColorSpace.Bt470bg)
            ColorSpace = ColorSpace.Bt601;
        else if (cp->color_space == AVColorSpace.Bt2020Ncl || cp->color_space == AVColorSpace.Bt2020Cl)
            ColorSpace = ColorSpace.Bt2020;
        
        AVPacketSideData* pktSideData;
        if ((pktSideData = av_packet_side_data_get(cp->coded_side_data, cp->nb_coded_side_data, AVPacketSideDataType.Displaymatrix)) != null && pktSideData->data != null)
        {
            double rotation = -Math.Round(av_display_rotation_get((int*)pktSideData->data));
            Rotation = rotation - (360*Math.Floor(rotation/360 + 0.9/360));
        }
    }

    // >= Second time fill from Decoder / Frame | TBR: We could avoid re-filling it when re-enabling a stream ... when same PixelFormat (VideoAcceleration)
    public void Refresh(VideoDecoder decoder, AVFrame* frame)
    {
        var codecCtx= decoder.CodecCtx;
        var format  = decoder.VideoAccelerated && codecCtx->sw_pix_fmt != AVPixelFormat.None ? codecCtx->sw_pix_fmt : codecCtx->pix_fmt;

        if (PixelFormat != format)
        {
            if (format == AVPixelFormat.None)
                return;

            PixelFormat = format;
            AnalysePixelFormat();
        }
        else if (format == AVPixelFormat.None) // Both None (Should be removed from Demuxer's streams?*)
            return;

        ReUpdate();

        if (codecCtx->bit_rate > 0)
            BitRate = codecCtx->bit_rate; // for logging only

        if (SAR.Num == 0 || decoder.codecChanged)
        {
            Width   = (uint)frame->width;
            Height  = (uint)frame->height;

            if (frame->sample_aspect_ratio.Num != 0)
                SAR = frame->sample_aspect_ratio;
            else if (codecCtx->sample_aspect_ratio.Num != 0)
                SAR = codecCtx->sample_aspect_ratio;
            else if (SAR.Num == 0)
                SAR = new(1, 1);

            int x, y;
            _ = av_reduce(&x, &y, Width * SAR.Num, Height * SAR.Den, 1024 * 1024);
            AspectRatio = new(x, y);
        }

        if (frame->flags.HasFlag(FrameFlags.Interlaced))
            FieldOrder = frame->flags.HasFlag(FrameFlags.TopFieldFirst) ? DeInterlace.TopField : DeInterlace.BottomField;
        else
            FieldOrder = codecCtx->field_order == AVFieldOrder.Tt ? DeInterlace.TopField : (codecCtx->field_order == AVFieldOrder.Bb ? DeInterlace.BottomField : DeInterlace.Progressive);

        if (ColorTransfer == AVColorTransferCharacteristic.Unspecified) // TBR: AVStream has AribStdB67 and Frame/CodecCtx has Bt2020_10 (priority to stream?)*
        {
            if (frame->color_trc != AVColorTransferCharacteristic.Unspecified)
                ColorTransfer = frame->color_trc;
            else if (codecCtx->color_trc != AVColorTransferCharacteristic.Unspecified)
                ColorTransfer = codecCtx->color_trc;
        }

        if (ColorRange == ColorRange.None)
        {
            if (frame->color_range == AVColorRange.Mpeg)
                ColorRange = ColorRange.Limited;
            else if (frame->color_range == AVColorRange.Jpeg)
                ColorRange = ColorRange.Full;
            else if (codecCtx->color_range == AVColorRange.Mpeg)
                ColorRange = ColorRange.Limited;
            else if (codecCtx->color_range == AVColorRange.Jpeg)
                ColorRange = ColorRange.Full;
            else if (ColorRange == ColorRange.None)
                ColorRange = ColorType == ColorType.YUV && !PixelFormatStr.Contains('j') ? ColorRange.Limited : ColorRange.Full; // yuvj family defaults to full
        }
        
        if (ColorTransfer == AVColorTransferCharacteristic.AribStdB67)
            HDRFormat = HDRFormat.HLG;
        else if (ColorTransfer == AVColorTransferCharacteristic.Smpte2084)
        {
            if (av_frame_get_side_data(frame, AVFrameSideDataType.DoviMetadata) != null)
                HDRFormat = HDRFormat.DolbyVision;
            else if (av_frame_get_side_data(frame, AVFrameSideDataType.DynamicHdrPlus) != null)
                HDRFormat = HDRFormat.HDRPlus;
            else
                HDRFormat = HDRFormat.HDR;
        }

        if (HDRFormat != HDRFormat.None) // Forcing BT.2020 with PQ/HLG transfer?
            ColorSpace = ColorSpace.Bt2020;

        if (ColorSpace == ColorSpace.None)
        {
            if (frame->colorspace == AVColorSpace.Bt709)
                ColorSpace = ColorSpace.Bt709;
            else if (frame->colorspace == AVColorSpace.Bt470bg)
                ColorSpace = ColorSpace.Bt601;
            else if (frame->colorspace == AVColorSpace.Bt2020Ncl || frame->colorspace == AVColorSpace.Bt2020Cl)
                ColorSpace = ColorSpace.Bt2020;
            else if (codecCtx->colorspace == AVColorSpace.Bt709)
                ColorSpace = ColorSpace.Bt709;
            else if (codecCtx->colorspace == AVColorSpace.Bt470bg)
                ColorSpace = ColorSpace.Bt601;
            else if (codecCtx->colorspace == AVColorSpace.Bt2020Ncl || codecCtx->colorspace == AVColorSpace.Bt2020Cl)
                ColorSpace = ColorSpace.Bt2020;
            else if (ColorSpace == ColorSpace.None)
                ColorSpace = Height > 576 ? ColorSpace.Bt709 : ColorSpace.Bt601;
        }
        
        // We consider that FPS can't change (only if it was missing we fill it)
        if (FPS == 0.0)
        {
            var newFps      = av_q2d(codecCtx->framerate);
            FPS             = double.IsNaN(newFps) || double.IsInfinity(newFps) || newFps <= 0.0 ? 25 : newFps; // Force default to 25 fps
            FrameDuration   = (long)(10_000_000 / FPS);
            TotalFrames     = (int)(Duration / FrameDuration);
            Demuxer.VideoPackets.frameDuration = FrameDuration;
        }

        // FPS2 / FrameDuration2 (DeInterlace)
        if (FieldOrder != DeInterlace.Progressive)
        {
            FPS2 = FPS;
            FrameDuration2 = FrameDuration;
            FPS /= 2;
            FrameDuration *= 2;
        }
        else
        {
            FPS2 = FPS * 2;
            FrameDuration2 = FrameDuration / 2;
        }

        AVFrameSideData* frameSideData;
        if ((frameSideData = av_frame_get_side_data(frame, AVFrameSideDataType.Displaymatrix)) != null && frameSideData->data != null)
        {
            var rotation = -Math.Round(av_display_rotation_get((int*)frameSideData->data));
            Rotation = rotation - (360*Math.Floor(rotation/360 + 0.9/360));
        }

        if (CanDebug)
            Demuxer.Log.Debug($"Stream Info (Filled)\r\n{GetDump()}");
    }

    void AnalysePixelFormat()
    {
        PixelFormatStr  = LowerCaseFirstChar(PixelFormat.ToString());
        PixelFormatDesc = av_pix_fmt_desc_get(PixelFormat);
        PixelComps      = PixelFormatDesc->comp.ToArray();
        PixelInterleaved= PixelFormatDesc->log2_chroma_w != PixelFormatDesc->log2_chroma_h;
        ColorType       = PixelComps.Length == 1 ? ColorType.Gray : ((PixelFormatDesc->flags & PixFmtFlags.Rgb) != 0 ? ColorType.RGB : ColorType.YUV);
        PixelPlanes     = 0;
        
        if (PixelComps.Length > 0)
        {
            PixelComp0Depth = PixelComps[0].depth;
            for (int i = 0; i < PixelComps.Length; i++)
                if (PixelComps[i].plane > PixelPlanes)
                    PixelPlanes = PixelComps[i].plane;

            PixelPlanes++;
        }
    }
}
