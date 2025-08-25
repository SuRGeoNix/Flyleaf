﻿using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream;

public unsafe class VideoStream : StreamBase
{
    public AspectRatio                  AspectRatio         { get; set; }
    public ColorRange                   ColorRange          { get; set; }
    public ColorSpace                   ColorSpace          { get; set; }
    public AVColorTransferCharacteristic
                                        ColorTransfer       { get; set; }
    public DeInterlace                  FieldOrder          { get; set; }
    public double                       Rotation            { get; set; }
    public double                       FPS                 { get; set; }
    public long                         FrameDuration       { get ;set; }
    public double                       FPS2                { get; set; } // interlace
    public long                         FrameDuration2      { get ;set; } // interlace
    public uint                         Height              { get; set; }
    public bool                         IsRGB               { get; set; }
    public HDRFormat                    HDRFormat           { get; set; }

    public AVComponentDescriptor[]      PixelComps          { get; set; }
    public int                          PixelComp0Depth     { get; set; }
    public AVPixelFormat                PixelFormat         { get; set; }
    public AVPixFmtDescriptor*          PixelFormatDesc     { get; set; }
    public string                       PixelFormatStr      { get; set; }
    public int                          PixelPlanes         { get; set; }
    public bool                         PixelSameDepth      { get; set; }
    public bool                         PixelInterleaved    { get; set; }
    public int                          TotalFrames         { get; set; }
    public uint                         Width               { get; set; }
    public bool                         FixTimestamps       { get; set; } // TBR: For formats such as h264/hevc that have no or invalid pts values

    public override string GetDump()
        => $"[{Type} #{StreamIndex}] {Codec} {PixelFormatStr} {Width}x{Height} @ {FPS:#.###} | [Color: {ColorSpace}] [BR: {BitRate}] | {Utils.TicksToTime((long)(AVStream->start_time * Timebase))}/{Utils.TicksToTime((long)(AVStream->duration * Timebase))} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}";

    public VideoStream() { }
    public VideoStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
    {
        Demuxer = demuxer;
        AVStream = st;
        Refresh();
    }

    public void Refresh(AVPixelFormat format = AVPixelFormat.None, AVFrame* frame = null)
    {
        base.Refresh();
        FieldOrder      = AVStream->codecpar->field_order == AVFieldOrder.Tt ? DeInterlace.TopField : (AVStream->codecpar->field_order == AVFieldOrder.Bb ? DeInterlace.BottomField : DeInterlace.Progressive);
        PixelFormat     = format == AVPixelFormat.None ? (AVPixelFormat)AVStream->codecpar->format : format;
        PixelFormatStr  = PixelFormat.ToString().Replace("AV_PIX_FMT_","").ToLower();
        Width           = (uint)AVStream->codecpar->width;
        Height          = (uint)AVStream->codecpar->height;

        if (Demuxer.FormatContext->iformat->flags.HasFlag(FmtFlags.Notimestamps))
        {
            FixTimestamps = true;

            if (Demuxer.Config.ForceFPS > 0)
                FPS = Demuxer.Config.ForceFPS;
            else
                FPS = av_q2d(av_guess_frame_rate(Demuxer.FormatContext, AVStream, frame));

            if (FPS == 0)
                FPS = 25;
        }
        else
        {
            FixTimestamps = false;
            FPS  = av_q2d(av_guess_frame_rate(Demuxer.FormatContext, AVStream, frame));
        }

        FrameDuration   = FPS > 0 ? (long) (10_000_000 / FPS) : 0;
        TotalFrames     = AVStream->duration > 0 && FrameDuration > 0 ? (int) (AVStream->duration * Timebase / FrameDuration) : (FrameDuration > 0 ? (int) (Demuxer.Duration / FrameDuration) : 0);

        int x, y;
        AVRational sar = av_guess_sample_aspect_ratio(null, AVStream, null);
        if (av_cmp_q(sar, av_make_q(0, 1)) <= 0)
            sar = av_make_q(1, 1);

        av_reduce(&x, &y, Width  * sar.Num, Height * sar.Den, 1024 * 1024);
        AspectRatio = new AspectRatio(x, y);

        AVPacketSideData* pktSideData;
        if ((pktSideData = av_packet_side_data_get(AVStream->codecpar->coded_side_data, AVStream->codecpar->nb_coded_side_data, AVPacketSideDataType.Displaymatrix)) != null && pktSideData->data != null)
        {
            double rotation = -Math.Round(av_display_rotation_get((int*)pktSideData->data));
            Rotation = rotation - (360*Math.Floor(rotation/360 + 0.9/360));
        }

        ColorRange = AVStream->codecpar->color_range == AVColorRange.Jpeg ? ColorRange.Full : ColorRange.Limited;

        var colorSpace = AVStream->codecpar->color_space;
        if (colorSpace == AVColorSpace.Bt709)
            ColorSpace = ColorSpace.BT709;
        else if (colorSpace == AVColorSpace.Bt470bg)
            ColorSpace = ColorSpace.BT601;
        else if (colorSpace == AVColorSpace.Bt2020Ncl || colorSpace == AVColorSpace.Bt2020Cl)
            ColorSpace = ColorSpace.BT2020;
            
        ColorTransfer = AVStream->codecpar->color_trc;

        // Avoid early check for HDR
        //if (ColorTransfer == AVColorTransferCharacteristic.AribStdB67)
        //    HDRFormat = HDRFormat.HLG;
        //else if (ColorTransfer == AVColorTransferCharacteristic.Smpte2084)
        //{
        //    for (int i = 0; i < AVStream->codecpar->nb_coded_side_data; i++)
        //    {
        //        var csdata = AVStream->codecpar->coded_side_data[i];
        //        switch (csdata.type)
        //        {
        //            case AVPacketSideDataType.DoviConf:
        //                HDRFormat = HDRFormat.DolbyVision;
        //                break;
        //            case AVPacketSideDataType.DynamicHdr10Plus:
        //                HDRFormat = HDRFormat.HDRPlus;
        //                break;
        //            case AVPacketSideDataType.ContentLightLevel:
        //                //AVContentLightMetadata t2 = *((AVContentLightMetadata*)csdata.data);
        //                break;
        //            case AVPacketSideDataType.MasteringDisplayMetadata:
        //                //AVMasteringDisplayMetadata t1 = *((AVMasteringDisplayMetadata*)csdata.data);
        //                HDRFormat = HDRFormat.HDR;
        //                break;
        //        }
        //    }
        //}

        if (frame != null)
        {
            AVFrameSideData* frameSideData;
            if ((frameSideData = av_frame_get_side_data(frame, AVFrameSideDataType.Displaymatrix)) != null && frameSideData->data != null)
            {
                var rotation = -Math.Round(av_display_rotation_get((int*)frameSideData->data));
                Rotation = rotation - (360*Math.Floor(rotation/360 + 0.9/360));
            }

            if (frame->flags.HasFlag(FrameFlags.Interlaced))
                FieldOrder = frame->flags.HasFlag(FrameFlags.TopFieldFirst) ? DeInterlace.TopField : DeInterlace.BottomField;

            ColorRange = frame->color_range == AVColorRange.Jpeg ? ColorRange.Full : ColorRange.Limited;

            if (frame->color_trc != AVColorTransferCharacteristic.Unspecified)
                ColorTransfer = frame->color_trc;

            if (frame->colorspace == AVColorSpace.Bt709)
                ColorSpace = ColorSpace.BT709;
            else if (frame->colorspace == AVColorSpace.Bt470bg)
                ColorSpace = ColorSpace.BT601;
            else if (frame->colorspace == AVColorSpace.Bt2020Ncl || frame->colorspace == AVColorSpace.Bt2020Cl)
                ColorSpace = ColorSpace.BT2020;

            if (ColorTransfer == AVColorTransferCharacteristic.AribStdB67)
                HDRFormat = HDRFormat.HLG;
            else if (ColorTransfer == AVColorTransferCharacteristic.Smpte2084)
            {
                var dolbyData = av_frame_get_side_data(frame, AVFrameSideDataType.DoviMetadata);
                if (dolbyData != null)
                    HDRFormat = HDRFormat.DolbyVision;
                else
                {
                    var hdrPlusData = av_frame_get_side_data(frame, AVFrameSideDataType.DynamicHdrPlus);
                    if (hdrPlusData != null)
                    {
                        //AVDynamicHDRPlus* x1 = (AVDynamicHDRPlus*)hdrPlusData->data;
                        HDRFormat = HDRFormat.HDRPlus;
                    }
                    else
                    {
                        //AVMasteringDisplayMetadata t1;
                        //AVContentLightMetadata t2;

                        //var masterData = av_frame_get_side_data(frame, AVFrameSideDataType.MasteringDisplayMetadata);
                        //if (masterData != null)
                        //    t1 = *((AVMasteringDisplayMetadata*)masterData->data);
                        //var lightData   = av_frame_get_side_data(frame, AVFrameSideDataType.ContentLightLevel);
                        //if (lightData != null)
                        //    t2 = *((AVContentLightMetadata*) lightData->data);

                        HDRFormat = HDRFormat.HDR;
                    }
                }
            }

            if (HDRFormat != HDRFormat.None) // Forcing BT.2020 with PQ/HLG transfer?
                ColorSpace = ColorSpace.BT2020;
            else if (ColorSpace == ColorSpace.None)
                ColorSpace = Height > 576 ? ColorSpace.BT709 : ColorSpace.BT601;
        }

        if (PixelFormat == AVPixelFormat.None || PixelPlanes > 0) // Should re-analyze? (possible to get different pixel format on 2nd... call?)
            return;

        PixelFormatDesc = av_pix_fmt_desc_get(PixelFormat);
        var comps       = PixelFormatDesc->comp.ToArray();
        PixelComps      = new AVComponentDescriptor[PixelFormatDesc->nb_components];
        for (int i=0; i<PixelComps.Length; i++)
            PixelComps[i] = comps[i];

        PixelInterleaved= PixelFormatDesc->log2_chroma_w != PixelFormatDesc->log2_chroma_h;
        IsRGB           = (PixelFormatDesc->flags & PixFmtFlags.Rgb) != 0;

        PixelSameDepth  = true;
        PixelPlanes     = 0;
        if (PixelComps.Length > 0)
        {
            PixelComp0Depth = PixelComps[0].depth;
            int prevBit     = PixelComp0Depth;
            for (int i=0; i<PixelComps.Length; i++)
            {
                if (PixelComps[i].plane > PixelPlanes)
                    PixelPlanes = PixelComps[i].plane;

                if (prevBit != PixelComps[i].depth)
                    PixelSameDepth = false;
            }

            PixelPlanes++;
        }
    }
}
