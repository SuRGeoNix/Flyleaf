using Vortice.Direct3D11;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream;

public unsafe class VideoStream : StreamBase
{
    /* TODO
     * Color Primaries (not really required?)
     * Chroma Location (when we add support in renderer)
     */

    public AVRational                   SAR                 { get; set; }
    public ColorRange                   ColorRange          { get; set; }
    public ColorSpace                   ColorSpace          { get; set; }
    public ColorType                    ColorType           { get; set; }
    public AVColorTransferCharacteristic
                                        ColorTransfer       { get; set; }
    public Cropping                     Cropping            { get; set; }
    public VideoFrameFormat             FieldOrder          { get; set; }
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
    public long                         TotalFrames         { get; set; }
    public uint                         Width               { get; set; }
    public bool                         FixTimestamps       { get; set; }
    
    internal CropRect cropStreamRect, cropFrameRect, cropRect; // Stream Crop + Codec Padding + Texture Padding
    bool hasStreamCrop;

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

        Width       = (uint)cp->width;
        Height      = (uint)cp->height;
        SAR         = av_guess_sample_aspect_ratio(null, AVStream, null);
        TotalFrames = AVStream->nb_frames;

        if (Demuxer.FormatContext->iformat->flags.HasFlag(FmtFlags.Notimestamps))
            FixTimestamps = true;

        if (Demuxer.Config.ForceFPS > 0)
            FPS = Demuxer.Config.ForceFPS;
        else
        {
            var fps1 = av_q2d(cp->framerate);
            if (double.IsNaN(fps1) || double.IsInfinity(fps1) || fps1 <= 0.0 || fps1 > 144)
            {
                var fps2 = av_q2d(av_guess_frame_rate(Demuxer.FormatContext, AVStream, null));
                if (double.IsNaN(fps2) || double.IsInfinity(fps2) || fps2 <= 0.0 || fps2 > 144)
                    FPS = 0.0;
                else
                    FPS = fps2;
            }
            else
                FPS = fps1;
        }

        if (FPS > 0)
            FrameDuration = (long)(10_000_000 / FPS);

        FieldOrder      = cp->field_order == AVFieldOrder.Tt ? VideoFrameFormat.InterlacedTopFieldFirst : (cp->field_order == AVFieldOrder.Bb ? VideoFrameFormat.InterlacedBottomFieldFirst : VideoFrameFormat.Progressive);
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

        if (cp->nb_coded_side_data == 0)
            return;
        
        var rotData = av_packet_side_data_get(cp->coded_side_data, cp->nb_coded_side_data, AVPacketSideDataType.Displaymatrix);
        if (rotData != null && rotData->data != null)
        {
            double rotation = -Math.Round(av_display_rotation_get((int*)rotData->data));
            Rotation = rotation - (360 * Math.Floor(rotation / 360 + 0.9 / 360));
        }

        var cropData= av_packet_side_data_get(cp->coded_side_data, cp->nb_coded_side_data, AVPacketSideDataType.FrameCropping);
        if (cropData != null && cropData->size == 16)
        {
            var cropByes = new ReadOnlySpan<byte>(cropData->data, 16).ToArray();
            cropStreamRect = new(
                top:    BitConverter.ToUInt32(cropByes, 0),
                bottom: BitConverter.ToUInt32(cropByes, 4),
                left:   BitConverter.ToUInt32(cropByes, 8),
                right:  BitConverter.ToUInt32(cropByes, 12)
                );

            hasStreamCrop = cropStreamRect != CropRect.Empty;
        }
    }

    internal override void UpdateDuration()
    {
        base.UpdateDuration();

        if (AVStream->nb_frames < 1 && FrameDuration > 0)
            TotalFrames = Duration / FrameDuration;
    }

    // >= Second time fill from Decoder / Frame | TBR: We could avoid re-filling it when re-enabling a stream ... when same PixelFormat (VideoAcceleration)
    public void Refresh(VideoDecoder decoder, AVFrame* frame) // TBR: Can be filled multiple times from different Codecs
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

        if (SAR.Num == 0)
        {
            if (frame->sample_aspect_ratio.Num != 0)
                SAR = frame->sample_aspect_ratio;
            else if (codecCtx->sample_aspect_ratio.Num != 0)
                SAR = codecCtx->sample_aspect_ratio;
            else if (SAR.Num == 0)
                SAR = new(1, 1);
        }

        cropRect = CropRect.Empty;

        // Stream's Crop
        if (hasStreamCrop)
        {
            Cropping =  Cropping.Stream;

            cropRect.Top    += cropStreamRect.Top;
            cropRect.Bottom += cropStreamRect.Bottom;
            cropRect.Left   += cropStreamRect.Left;
            cropRect.Right  += cropStreamRect.Right;
            
        }
        else
            Cropping = Cropping.None;

        // Codec's Crop (Frame)
        cropFrameRect = new(
            top:    (uint)frame->crop_top,
            bottom: (uint)frame->crop_bottom,
            left:   (uint)frame->crop_left,
            right:  (uint)frame->crop_right
            );

        if (cropFrameRect != CropRect.Empty)
        {
            Cropping |= Cropping.Codec;

            cropRect.Top    += cropFrameRect.Top;
            cropRect.Bottom += cropFrameRect.Bottom;
            cropRect.Left   += cropFrameRect.Left;
            cropRect.Right  += cropFrameRect.Right;
        }

        // HW Texture's Crop
        if (decoder.VideoAccelerated)
        {
            var desc = decoder.textureFFmpeg.Description;

            if (desc.Width > codecCtx->coded_width)
            {
                cropRect.Right += (uint)(desc.Width - codecCtx->coded_width);
                Cropping |= Cropping.Texture;
            }

            if (desc.Height > codecCtx->coded_height)
            {
                cropRect.Bottom += (uint)(desc.Height - codecCtx->coded_height);
                Cropping |= Cropping.Texture;
            }
        }

        Width   = (uint)(frame->width  - (cropRect.Left + cropRect.Right));
        Height  = (uint)(frame->height - (cropRect.Top  + cropRect.Bottom));

        if (frame->flags.HasFlag(FrameFlags.Interlaced))
            FieldOrder = frame->flags.HasFlag(FrameFlags.TopFieldFirst) ? VideoFrameFormat.InterlacedTopFieldFirst : VideoFrameFormat.InterlacedBottomFieldFirst;
        else
            FieldOrder = codecCtx->field_order == AVFieldOrder.Tt ? VideoFrameFormat.InterlacedTopFieldFirst : (codecCtx->field_order == AVFieldOrder.Bb ? VideoFrameFormat.InterlacedBottomFieldFirst : VideoFrameFormat.Progressive);

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
        if (FPS == 0)
        {
            var newFps      = av_q2d(codecCtx->framerate);
            FPS             = double.IsNaN(newFps) || double.IsInfinity(newFps) || newFps <= 0.0 || newFps > 144 ? 25 : newFps; // Force default to 25 fps
            FrameDuration   = (long)(10_000_000 / FPS);
        }

        // FPS2 / FrameDuration2 (DeInterlace) | we consider FPS (CFR) is progressive
        FPS2            = FPS * 2;
        FrameDuration2  = FrameDuration / 2;

        if (AVStream->nb_frames < 1)
            TotalFrames = Duration / FrameDuration;

        Demuxer.VideoPackets.frameDuration = FrameDuration;

        var rotData = av_frame_get_side_data(frame, AVFrameSideDataType.Displaymatrix);
        if (rotData != null && rotData->data != null)
        {
            var rotation = -Math.Round(av_display_rotation_get((int*)rotData->data));
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

    public AspectRatio GetDAR()
    {
        int x, y;
        _ = av_reduce(&x, &y, Width * SAR.Num, Height * SAR.Den, 1024 * 1024);
        return new(x, y);
    }
}
