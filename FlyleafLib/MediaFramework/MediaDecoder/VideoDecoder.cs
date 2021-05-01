using System;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

using FlyleafLib.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using System.Collections.Concurrent;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public unsafe class VideoDecoder : DecoderBase
    {
        public ConcurrentQueue<VideoFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<VideoFrame>();
        public VideoAcceleration
                                VideoAcceleration   { get; private set; }
        public bool             VideoAccelerated    { get; internal set; }
        public VideoStream      VideoStream         => (VideoStream) Stream;

        // Hardware & Software_Handled (Y_UV | Y_U_V)
        Texture2DDescription    textDesc, textDescUV;

        // Software_Sws (RGBA)
        const AVPixelFormat     VOutPixelFormat  = AVPixelFormat.AV_PIX_FMT_RGBA;
        const int               SCALING_HQ = SWS_ACCURATE_RND | SWS_BITEXACT | SWS_LANCZOS | SWS_FULL_CHR_H_INT | SWS_FULL_CHR_H_INP;
        const int               SCALING_LQ = SWS_BICUBIC;

        SwsContext*             swsCtx;
        IntPtr                  outBufferPtr; 
        int                     outBufferSize;
        byte_ptrArray4          outData;
        int_array4              outLineSize;

        internal bool           keyFrameRequired;

        public VideoDecoder(MediaContext.DecoderContext decCtx) : base(decCtx)
        {
            VideoAcceleration   = new VideoAcceleration(decCtx.player.renderer.device);
        }

        protected override unsafe int Setup(AVCodec* codec)
        {
            VideoAccelerated = false;

            if (cfg.decoder.HWAcceleration)
            {
                if (VideoAcceleration.CheckCodecSupport(codec))
                {
                    if (VideoAcceleration.hw_device_ctx != null)
                    {
                        codecCtx->hw_device_ctx = av_buffer_ref(VideoAcceleration.hw_device_ctx);
                        VideoAccelerated = true;
                        Log("[VA] Success");
                    }
                }
                else
                    Log("[VA] Failed");
            }
            else
                Log("[VA] Disabled");

            codecCtx->thread_count = Math.Min(decCtx.cfg.decoder.VideoThreads, codecCtx->codec_id == AV_CODEC_ID_HEVC ? 32 : 16);
            codecCtx->thread_type  = 0;

            int bits = VideoStream.PixelFormatDesc->comp.ToArray()[0].depth;

            textDesc = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Default,
                BindFlags           = BindFlags.ShaderResource,

                Format              = bits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                Width               = codecCtx->width,
                Height              = codecCtx->height,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };

            textDescUV = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Default,
                BindFlags           = BindFlags.ShaderResource,

                Format              = bits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                Width               = codecCtx->width  >> VideoStream.PixelFormatDesc->log2_chroma_w,
                Height              = codecCtx->height >> VideoStream.PixelFormatDesc->log2_chroma_h,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };

            decCtx.player.renderer.FrameResized();

            return 0;
        }

        public override void Stop()
        {
            if (Status == Status.Stopped) return;

            av_buffer_unref(&codecCtx->hw_device_ctx);
            if (swsCtx != null) { sws_freeContext(swsCtx); swsCtx = null; }
            base.Stop();
            DisposeFrames();
        }

        public void Flush()
        {
            if (Status == Status.Stopped) return;
            //lock (lockCodecCtx)
            DisposeFrames();
            avcodec_flush_buffers(codecCtx);
            if (Status == Status.Ended) Status = Status.Paused;
            keyFrameRequired = true;
            //demuxer.DisposePackets(demuxer.VideoPackets);
        }

        protected override void DecodeInternal()
        {
            int ret = 0;
            int allowedErrors = cfg.decoder.MaxErrors;
            AVPacket *packet;

            while (Status == Status.Decoding)
            {
                // While Frames Queue Full
                if (Frames.Count >= cfg.decoder.MaxVideoFrames)
                {
                    Status = Status.QueueFull;

                    while (Frames.Count >= cfg.decoder.MaxVideoFrames && Status == Status.QueueFull) Thread.Sleep(20);
                    if (Status != Status.QueueFull) break;
                    Status = Status.Decoding;
                }

                // While Packets Queue Empty (Drain | Quit if Demuxer stopped | Wait until we get packets)
                if (demuxer.VideoPackets.Count == 0)
                {
                    Status = Status.PacketsEmpty;
                    while (demuxer.VideoPackets.Count == 0 && Status == Status.PacketsEmpty)
                    {
                        if (demuxer.Status == MediaDemuxer.Status.Ended)
                        {
                            Log("Draining...");
                            Status = Status.Draining;
                            AVPacket* drainPacket = av_packet_alloc();
                            drainPacket->data = null;
                            drainPacket->size = 0;
                            demuxer.VideoPackets.Enqueue((IntPtr)drainPacket);
                            break;
                        }
                        else if (demuxer.Status != MediaDemuxer.Status.Demuxing && demuxer.Status != MediaDemuxer.Status.QueueFull)
                        {
                            Log($"Demuxer is not running [Demuxer Status: {demuxer.Status}]");
                            Status = demuxer.Status == MediaDemuxer.Status.Stopping || demuxer.Status == MediaDemuxer.Status.Stopped ? Status.Stopped : Status.Pausing;
                            return;
                        }
                        
                        Thread.Sleep(20);
                    }
                    if (Status != Status.PacketsEmpty && Status != Status.Draining) break;
                    if (Status != Status.Draining) Status = Status.Decoding;
                }

                lock (lockCodecCtx)
                {
                    if (demuxer.VideoPackets.Count == 0) continue;
                    demuxer.VideoPackets.TryDequeue(out IntPtr pktPtr);
                    packet = (AVPacket*) pktPtr;
                        

                    ret = avcodec_send_packet(codecCtx, packet);
                    av_packet_free(&packet);

                    if (ret != 0 && ret != AVERROR(EAGAIN))
                    {
                        if (ret == AVERROR_EOF)
                        {
                            Status = Status.Ended;
                            return;
                        }
                        else
                        {
                            allowedErrors--;
                            Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                            if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); return; }

                            continue;
                        }
                    }

                    while (true)
                    {
                        ret = avcodec_receive_frame(codecCtx, frame);
                        if (ret != 0) { av_frame_unref(frame); break; }

                        if (keyFrameRequired)
                        {
                            if (frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                            {
                                Log($"Seek to keyframe failed [{frame->pict_type} | {frame->key_frame}]");
                                av_frame_unref(frame);
                                continue;
                            }
                            else
                                keyFrameRequired = false;
                        }

                        VideoFrame mFrame = ProcessVideoFrame(frame);
                        if (mFrame != null)
                        {
                            //Log(Utils.TicksToTime((long)(mFrame.pts * VideoStream.Timebase)) + " | pts -> " + mFrame.pts);
                            Frames.Enqueue(mFrame);
                        }

                        av_frame_unref(frame);
                    }

                } // Lock CodecCtx

            } // While Decoding

            if (Status == Status.Draining) Status = Status.Ended;
        }

        internal VideoFrame ProcessVideoFrame(AVFrame* frame)
        {
            try
            {
                VideoFrame mFrame = new VideoFrame();
                mFrame.pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                if (mFrame.pts == AV_NOPTS_VALUE) return null;
                mFrame.timestamp = ((long)(mFrame.pts * VideoStream.Timebase) - VideoStream.StartTime) + cfg.audio.LatencyTicks;

                // Hardware Frame (NV12|P010)   | CopySubresourceRegion FFmpeg Texture Array -> Device Texture[1] (NV12|P010) / SRV (RX_RXGX) -> PixelShader (Y_UV)
                if (VideoAccelerated)
                {
                    if (frame->hw_frames_ctx == null)
                    {
                        Log("[VA] Failed 2");
                        VideoAccelerated = false;
                        decCtx.player.renderer.FrameResized();
                        return ProcessVideoFrame(frame);
                    }

                    Texture2D textureFFmpeg = new Texture2D((IntPtr) frame->data.ToArray()[0]);
                    textDesc.Format     = textureFFmpeg.Description.Format;
                    mFrame.textures     = new Texture2D[1];
                    mFrame.textures[0]  = new Texture2D(decCtx.player.renderer.device, textDesc);
                    decCtx.player.renderer.device.ImmediateContext.CopySubresourceRegion(textureFFmpeg, (int) frame->data.ToArray()[1], new ResourceRegion(0, 0, 0, mFrame.textures[0].Description.Width, mFrame.textures[0].Description.Height, 1), mFrame.textures[0], 0);
                }

                // Software Frame (8-bit YUV)   | YUV byte* -> Device Texture[3] (RX) / SRV (RX_RX_RX) -> PixelShader (Y_U_V)
                else if (VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                {
                    mFrame.textures = new Texture2D[3];

                    // YUV Planar [Y0 ...] [U0 ...] [V0 ....]
                    if (VideoStream.IsPlanar)
                    {
                        DataBox db          = new DataBox();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[0];
                        db.RowPitch         = frame->linesize.ToArray()[0];
                        mFrame.textures[0]  = new Texture2D(decCtx.player.renderer.device, textDesc,  new DataBox[] { db });

                        db                  = new DataBox();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[1];
                        db.RowPitch         = frame->linesize.ToArray()[1];
                        mFrame.textures[1]  = new Texture2D(decCtx.player.renderer.device, textDescUV, new DataBox[] { db });

                        db                  = new DataBox();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[2];
                        db.RowPitch         = frame->linesize.ToArray()[2];
                        mFrame.textures[2]  = new Texture2D(decCtx.player.renderer.device, textDescUV, new DataBox[] { db });
                    }

                    // YUV Packed ([Y0U0Y1V0] ....)
                    else
                    {
                        DataStream dsY  = new DataStream(textDesc.  Width * textDesc.  Height, true, true);
                        DataStream dsU  = new DataStream(textDescUV.Width * textDescUV.Height, true, true);
                        DataStream dsV  = new DataStream(textDescUV.Width * textDescUV.Height, true, true);
                        DataBox    dbY  = new DataBox();
                        DataBox    dbU  = new DataBox();
                        DataBox    dbV  = new DataBox();

                        dbY.DataPointer = dsY.DataPointer;
                        dbU.DataPointer = dsU.DataPointer;
                        dbV.DataPointer = dsV.DataPointer;

                        dbY.RowPitch    = textDesc.  Width;
                        dbU.RowPitch    = textDescUV.Width;
                        dbV.RowPitch    = textDescUV.Width;

                        long totalSize = frame->linesize.ToArray()[0] * textDesc.Height;

                        byte* dataPtr = frame->data.ToArray()[0];
                        AVComponentDescriptor[] comps = VideoStream.PixelFormatDesc->comp.ToArray();

                        for (int i=0; i<totalSize; i+=VideoStream.Comp0Step)
                            dsY.WriteByte(*(dataPtr + i));

                        for (int i=1; i<totalSize; i+=VideoStream.Comp1Step)
                            dsU.WriteByte(*(dataPtr + i));

                        for (int i=3; i<totalSize; i+=VideoStream.Comp2Step)
                            dsV.WriteByte(*(dataPtr + i));

                        mFrame.textures[0] = new Texture2D(decCtx.player.renderer.device, textDesc,   new DataBox[] { dbY });
                        mFrame.textures[1] = new Texture2D(decCtx.player.renderer.device, textDescUV, new DataBox[] { dbU });
                        mFrame.textures[2] = new Texture2D(decCtx.player.renderer.device, textDescUV, new DataBox[] { dbV });

                        Utilities.Dispose(ref dsY); Utilities.Dispose(ref dsU); Utilities.Dispose(ref dsV);
                    }
                }

                // Software Frame (OTHER/sws_scale) | X byte* -> Sws_Scale RGBA -> Device Texture[1] (RGBA) / SRV (RGBA) -> PixelShader (RGBA)
                else
                {
                    if (swsCtx == null)
                    {
                        textDesc.Format = Format.R8G8B8A8_UNorm;
                        outData         = new byte_ptrArray4();
                        outLineSize     = new int_array4();
                        outBufferSize   = av_image_get_buffer_size(VOutPixelFormat, codecCtx->width, codecCtx->height, 1);
                        Marshal.FreeHGlobal(outBufferPtr);
                        outBufferPtr    = Marshal.AllocHGlobal(outBufferSize);
                        av_image_fill_arrays(ref outData, ref outLineSize, (byte*) outBufferPtr, VOutPixelFormat, codecCtx->width, codecCtx->height, 1);
                        
                        int vSwsOptFlags= cfg.video.SwsHighQuality ? SCALING_HQ : SCALING_LQ;
                        swsCtx          = sws_getContext(codecCtx->coded_width, codecCtx->coded_height, codecCtx->pix_fmt, codecCtx->width, codecCtx->height, VOutPixelFormat, vSwsOptFlags, null, null, null);
                        if (swsCtx == null) { Log($"[ProcessVideoFrame] [Error] Failed to allocate SwsContext"); return null; }
                    }

                    sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, outData, outLineSize);

                    DataBox db          = new DataBox();
                    db.DataPointer      = (IntPtr)outData.ToArray()[0];
                    db.RowPitch         = outLineSize[0];
                    mFrame.textures     = new Texture2D[1];
                    mFrame.textures[0]  = new Texture2D(decCtx.player.renderer.device, textDesc, new DataBox[] { db });
                }

                return mFrame;

            } catch (Exception e)
            {
                Log("[ProcessVideoFrame] [Error] " + e.Message + " - " + e.StackTrace);
                return null; 
            }
        }

        public void DisposeFrames()
        {

            while (!Frames.IsEmpty)
            {
                Frames.TryDequeue(out VideoFrame frame);
                DisposeFrame(frame);
            }


            //foreach (VideoFrame frame in Frames) DisposeFrame(frame);
            //Frames = new ConcurrentQueue<VideoFrame>();
        }
        public static void DisposeFrame(VideoFrame frame) { if (frame != null && frame.textures != null) for (int i=0; i<frame.textures.Length; i++) Utilities.Dispose(ref frame.textures[i]); }
    }
}