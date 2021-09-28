using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using Vortice;
using Vortice.DXGI;
using Vortice.Direct3D11;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public unsafe class VideoDecoder : DecoderBase
    {
        public ConcurrentQueue<VideoFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<VideoFrame>();
        public Renderer         Renderer            { get; private set; }
        public bool             VideoAccelerated    { get; internal set; }
        public VideoStream      VideoStream         => (VideoStream) Stream;

        public long             StartTime             { get; internal set; }

        // Hardware & Software_Handled (Y_UV | Y_U_V)
        Texture2DDescription    textDesc, textDescUV;

        // Software_Sws (RGBA)
        const AVPixelFormat     VOutPixelFormat = AVPixelFormat.AV_PIX_FMT_RGBA;
        const int               SCALING_HQ = SWS_ACCURATE_RND | SWS_BITEXACT | SWS_LANCZOS | SWS_FULL_CHR_H_INT | SWS_FULL_CHR_H_INP;
        const int               SCALING_LQ = SWS_BICUBIC;

        SwsContext*             swsCtx;
        IntPtr                  outBufferPtr;
        int                     outBufferSize;
        byte_ptrArray4          outData;
        int_array4              outLineSize;

        internal bool           keyFrameRequired;
        bool HDRDataSent;

        #region Video Acceleration (Should be disposed seperately)
        const int               AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        const AVHWDeviceType    HW_DEVICE   = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA; // To fully support Win7/8 should consider AV_HWDEVICE_TYPE_DXVA2
        const AVPixelFormat     HW_PIX_FMT  = AVPixelFormat.AV_PIX_FMT_D3D11;
        AVBufferRef*            hw_device_ctx;

        internal static bool CheckCodecSupport(AVCodec* codec)
        {
            for (int i = 0; ; i++)
            {
                AVCodecHWConfig* config = avcodec_get_hw_config(codec, i);
                if (config == null) break;
                if ((config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == 0 || config->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE) continue;

                if (config->device_type == HW_DEVICE && config->pix_fmt == HW_PIX_FMT) return true;
            }

            return false;
        }
        internal int InitVA(Control control = null)
        {
            int ret;

            Renderer = new Renderer(this, Config, control, UniqueId);

            if (Renderer.Device == null || hw_device_ctx != null) return -1;

            hw_device_ctx  = av_hwdevice_ctx_alloc(HW_DEVICE);

            AVHWDeviceContext* device_ctx = (AVHWDeviceContext*) hw_device_ctx->data;
            AVD3D11VADeviceContext* d3d11va_device_ctx = (AVD3D11VADeviceContext*) device_ctx->hwctx;
            d3d11va_device_ctx->device = (FFmpeg.AutoGen.ID3D11Device*) Renderer.Device.NativePointer;

            ret = av_hwdevice_ctx_init(hw_device_ctx);
            if (ret != 0)
            {
                Log($"[ERROR-1]{Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                
                fixed(AVBufferRef** ptr = &hw_device_ctx) av_buffer_unref(ptr);
                hw_device_ctx = null;
                return ret;
            }

            return ret;
        }
        public void DisposeVA()
        {
            Renderer.Dispose();
            fixed(AVBufferRef** ptr = &hw_device_ctx) av_buffer_unref(ptr);
            hw_device_ctx = null;
        }
        #endregion

        public VideoDecoder(Config config, Control control = null, int uniqueId = -1) : base(config, uniqueId)
        {
            InitVA(control);
        }

        protected override int Setup(AVCodec* codec)
        {
            VideoAccelerated = false;

            if (Config.Video.VideoAcceleration)
            {
                if (CheckCodecSupport(codec))
                {
                    if (hw_device_ctx != null)
                    {
                        codecCtx->hw_device_ctx = av_buffer_ref(hw_device_ctx);
                        VideoAccelerated = true;
                        Log("[VA] Success");
                    }
                    else
                        Log("[VA] Failed");
                }
                else
                    Log("[VA] Failed");
            }
            else
                Log("[VA] Disabled");

            codecCtx->thread_count = Math.Min(Config.Decoder.VideoThreads, codecCtx->codec_id == AV_CODEC_ID_HEVC ? 32 : 16);
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
            
            // Can't get data from here?
            //var t1 = av_stream_get_side_data(VideoStream.AVStream, AVPacketSideDataType.AV_PKT_DATA_MASTERING_DISPLAY_METADATA, null);
            //var t2 = av_stream_get_side_data(VideoStream.AVStream, AVPacketSideDataType.AV_PKT_DATA_CONTENT_LIGHT_LEVEL, null);

            HDRDataSent = false;
            Renderer?.FrameResized();

            keyFrameRequired = true;

            return 0;
        }
        internal void Flush()
        {
            lock (lockActions)
            lock (lockCodecCtx)
            {
                if (Disposed) return;

                if (Status == Status.Ended) Status = Status.Stopped;
                else if (Status == Status.Draining) Status = Status.Stopping;

                DisposeFrames();
                avcodec_flush_buffers(codecCtx);
                
                keyFrameRequired = true;
                StartTime = AV_NOPTS_VALUE;
                curSpeedFrame = Speed;
            }
        }

        protected override void RunInternal()
        {
            int ret = 0;
            int allowedErrors = Config.Decoder.MaxErrors;
            AVPacket *packet;

            do
            {
                // Wait until Queue not Full or Stopped
                if (Frames.Count >= Config.Decoder.MaxVideoFrames)
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueFull;

                    while (Frames.Count >= Config.Decoder.MaxVideoFrames && Status == Status.QueueFull) Thread.Sleep(20);

                    lock (lockStatus)
                    {
                        if (Status != Status.QueueFull) break;
                        Status = Status.Running;
                    }       
                }

                // While Packets Queue Empty (Drain | Quit if Demuxer stopped | Wait until we get packets)
                if (demuxer.VideoPackets.Count == 0)
                {
                    CriticalArea = true;

                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueEmpty;

                    while (demuxer.VideoPackets.Count == 0 && Status == Status.QueueEmpty)
                    {
                        if (demuxer.Status == Status.Ended)
                        {
                            lock (lockStatus)
                            {
                                Log("Draining...");
                                Status = Status.Draining;
                                AVPacket* drainPacket = av_packet_alloc();
                                drainPacket->data = null;
                                drainPacket->size = 0;
                                demuxer.VideoPackets.Enqueue((IntPtr)drainPacket);
                            }
                            
                            break;
                        }
                        else if (!demuxer.IsRunning)
                        {
                            Log($"Demuxer is not running [Demuxer Status: {demuxer.Status}]");

                            int retries = 5;

                            while (retries > 0)
                            {
                                retries--;
                                Thread.Sleep(10);
                                if (demuxer.IsRunning) break;
                            }

                            lock (demuxer.lockStatus)
                            lock (lockStatus)
                            {
                                if (demuxer.Status == Status.Pausing || demuxer.Status == Status.Paused)
                                    Status = Status.Pausing;
                                else if (demuxer.Status != Status.Ended)
                                    Status = Status.Stopping;
                                else
                                    continue;
                            }

                            break;
                        }
                        
                        Thread.Sleep(20);
                    }

                    lock (lockStatus)
                    {
                        CriticalArea = false;
                        if (Status != Status.QueueEmpty && Status != Status.Draining) break;
                        if (Status != Status.Draining) Status = Status.Running;
                    }
                }

                lock (lockCodecCtx)
                {
                    if (Status == Status.Stopped || demuxer.VideoPackets.Count == 0) continue;
                    demuxer.VideoPackets.TryDequeue(out IntPtr pktPtr);
                    packet = (AVPacket*) pktPtr;

                    ret = avcodec_send_packet(codecCtx, packet);
                    av_packet_free(&packet);

                    if (ret != 0 && ret != AVERROR(EAGAIN))
                    {
                        if (ret == AVERROR_EOF)
                        {
                            if (demuxer.VideoPackets.Count > 0) { avcodec_flush_buffers(codecCtx); continue; } // TBR: Happens on HLS while switching video streams
                            Status = Status.Ended;
                            break;
                        }
                        else
                        {
                            allowedErrors--;
                            Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                            if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); Status = Status.Stopping; break; }

                            continue;
                        }
                    }
                    
                    while (true)
                    {
                        ret = avcodec_receive_frame(codecCtx, frame);
                        if (ret != 0) { av_frame_unref(frame); break; }

                        frame->pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                        if (frame->pts == AV_NOPTS_VALUE) { av_frame_unref(frame); continue; }

                        if (keyFrameRequired)
                        {
                            if (frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                            {
                                Log($"Seek to keyframe failed [{frame->pict_type} | {frame->key_frame}]");
                                av_frame_unref(frame);
                                continue;
                            }
                            else
                            {
                                StartTime = (long)(frame->pts * VideoStream.Timebase) - demuxer.StartTime;
                                keyFrameRequired = false;
                            }
                        }

                        VideoFrame mFrame = ProcessVideoFrame(frame);
                        if (mFrame != null) Frames.Enqueue(mFrame);
                    }

                } // Lock CodecCtx

            } while (Status == Status.Running);

            if (Status == Status.Draining) Status = Status.Ended;
        }

        internal VideoFrame ProcessVideoFrame(AVFrame* frame)
        {
            try
            {
                VideoFrame mFrame;
                if (Speed != 1)
                {
                    curSpeedFrame++;
                    if (curSpeedFrame < Speed) return null;
                    curSpeedFrame = 0;
                    mFrame = new VideoFrame();
                    mFrame.timestamp = (long)(frame->pts * VideoStream.Timebase) - demuxer.StartTime + Config.Audio.Latency;
                    mFrame.timestamp /= Speed;
                }
                else
                {
                    mFrame = new VideoFrame();
                    mFrame.timestamp = (long)(frame->pts * VideoStream.Timebase) - demuxer.StartTime + Config.Audio.Latency;
                }
                //Log($"Decoding {Utils.TicksToTime(mFrame.timestamp)}");

                if (!HDRDataSent && frame->side_data != null && *frame->side_data != null)
                {
                    HDRDataSent = true;
                    AVFrameSideData* sideData = *frame->side_data;
                    if (sideData->type == AVFrameSideDataType.AV_FRAME_DATA_MASTERING_DISPLAY_METADATA)
                        Renderer?.UpdateHDRtoSDR((AVMasteringDisplayMetadata*)sideData->data);
                }

                // Hardware Frame (NV12|P010)   | CopySubresourceRegion FFmpeg Texture Array -> Device Texture[1] (NV12|P010) / SRV (RX_RXGX) -> PixelShader (Y_UV)
                if (VideoAccelerated)
                {
                    if (frame->hw_frames_ctx == null)
                    {
                        Log("[VA] Failed 2");
                        VideoAccelerated = false;
                        Renderer?.FrameResized();
                        CodecChanged?.Invoke(this);
                        return ProcessVideoFrame(frame);
                    }

                    ID3D11Texture2D textureFFmpeg = new ID3D11Texture2D((IntPtr) frame->data.ToArray()[0]);
                    textDesc.Format     = textureFFmpeg.Description.Format;
                    mFrame.textures     = new ID3D11Texture2D[1];
                    mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc);
                    Renderer.Device.ImmediateContext.CopySubresourceRegion(mFrame.textures[0], 0, 0, 0, 0, textureFFmpeg, (int) frame->data.ToArray()[1], new Vortice.Mathematics.Box(0, 0, 0, mFrame.textures[0].Description.Width, mFrame.textures[0].Description.Height, 1));
                }

                // Software Frame (8-bit YUV)   | YUV byte* -> Device Texture[3] (RX) / SRV (RX_RX_RX) -> PixelShader (Y_U_V)
                else if (VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                {
                    mFrame.textures = new ID3D11Texture2D[3];

                    // YUV Planar [Y0 ...] [U0 ...] [V0 ....]
                    if (VideoStream.IsPlanar)
                    {
                        SubresourceData db  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[0];
                        db.Pitch            = frame->linesize.ToArray()[0];
                        mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc, new SubresourceData[] { db });

                        db                  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[1];
                        db.Pitch            = frame->linesize.ToArray()[1];
                        mFrame.textures[1]  = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { db });

                        db                  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[2];
                        db.Pitch            = frame->linesize.ToArray()[2];
                        mFrame.textures[2]  = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { db });
                    }

                    // YUV Packed ([Y0U0Y1V0] ....)
                    else
                    {
                        DataStream dsY  = new DataStream(textDesc.  Width * textDesc.  Height, true, true);
                        DataStream dsU  = new DataStream(textDescUV.Width * textDescUV.Height, true, true);
                        DataStream dsV  = new DataStream(textDescUV.Width * textDescUV.Height, true, true);
                        SubresourceData    dbY  = new SubresourceData();
                        SubresourceData    dbU  = new SubresourceData();
                        SubresourceData    dbV  = new SubresourceData();

                        dbY.DataPointer = dsY.BasePointer;
                        dbU.DataPointer = dsU.BasePointer;
                        dbV.DataPointer = dsV.BasePointer;

                        dbY.Pitch    = textDesc.  Width;
                        dbU.Pitch    = textDescUV.Width;
                        dbV.Pitch    = textDescUV.Width;

                        long totalSize = frame->linesize.ToArray()[0] * textDesc.Height;

                        byte* dataPtr = frame->data.ToArray()[0];
                        AVComponentDescriptor[] comps = VideoStream.PixelFormatDesc->comp.ToArray();

                        for (int i=0; i<totalSize; i+=VideoStream.Comp0Step)
                            dsY.WriteByte(*(dataPtr + i));

                        for (int i=1; i<totalSize; i+=VideoStream.Comp1Step)
                            dsU.WriteByte(*(dataPtr + i));

                        for (int i=3; i<totalSize; i+=VideoStream.Comp2Step)
                            dsV.WriteByte(*(dataPtr + i));

                        mFrame.textures[0] = Renderer.Device.CreateTexture2D(textDesc,   new SubresourceData[] { dbY });
                        mFrame.textures[1] = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { dbU });
                        mFrame.textures[2] = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { dbV });

                        dsY.Dispose(); dsU.Dispose(); dsV.Dispose();
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
                        
                        int vSwsOptFlags= Config.Video.SwsHighQuality ? SCALING_HQ : SCALING_LQ;
                        swsCtx          = sws_getContext(codecCtx->coded_width, codecCtx->coded_height, codecCtx->pix_fmt, codecCtx->width, codecCtx->height, VOutPixelFormat, vSwsOptFlags, null, null, null);
                        if (swsCtx == null) { Log($"[ProcessVideoFrame] [Error] Failed to allocate SwsContext"); return null; }
                    }

                    sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, outData, outLineSize);

                    SubresourceData db  = new SubresourceData();
                    db.DataPointer      = (IntPtr)outData.ToArray()[0];
                    db.Pitch            = outLineSize[0];
                    mFrame.textures     = new ID3D11Texture2D[1];
                    mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc, new SubresourceData[] { db });
                }

                av_frame_unref(frame);
                return mFrame;

            } catch (Exception e)
            {
                Log("[ProcessVideoFrame] [Error] " + e.Message + " - " + e.StackTrace);
                av_frame_unref(frame);
                return null; 
            }
        }

        /// <summary>
        /// Seeks and demuxes until the requested frame
        /// </summary>
        /// <param name="index"></param>
        /// <returns>The requested VideoFrame or null on failure</returns>
        public VideoFrame GetFrame(int index) // Zero-based frame index
        {
            int ret;

            // Calculation of FrameX timestamp (based on fps/avgFrameDuration) | offset 2ms
            long frameTimestamp = VideoStream.StartTime + (long) (index * (10000000 / VideoStream.Fps)) - 20000;
            //Log($"Searching for {Utils.TicksToTime(frameTimestamp)}");

            // Seeking at frameTimestamp or previous I/Key frame and flushing codec | Temp fix (max I/distance 3sec) for ffmpeg bug that fails to seek on keyframe with HEVC
            if (codecCtx->codec_id == AV_CODEC_ID_HEVC)
                ret = av_seek_frame(demuxer.FormatContext, -1, Math.Max(VideoStream.StartTime, frameTimestamp - (3 * (long)1000 * 10000)) / 10, AVSEEK_FLAG_ANY);
            else
                ret = av_seek_frame(demuxer.FormatContext, -1, frameTimestamp / 10, AVSEEK_FLAG_FRAME | AVSEEK_FLAG_BACKWARD);

            if (ret < 0) return null; // handle seek error
            avcodec_flush_buffers(codecCtx);

            // Decoding until requested frame/timestamp
            while (GetNextFrame() == 0)
            {
                // Skip frames before our actual requested frame
                if ((long)(frame->best_effort_timestamp * VideoStream.Timebase) < frameTimestamp)
                {
                    //Log($"[Skip] [pts: {frame->best_effort_timestamp}] [time: {Utils.TicksToTime((long)(frame->best_effort_timestamp * VideoStream.Timebase))}]");
                    av_frame_unref(frame);
                    continue; 
                }

                //Log($"[Found] [pts: {frame->best_effort_timestamp}] [time: {Utils.TicksToTime((long)(frame->best_effort_timestamp * VideoStream.Timebase))}]");
                return ProcessVideoFrame(frame);
            }

            return null;
        }


        /// <summary>
        /// Demuxes until the next valid video frame (will be stored in AVFrame* frame)
        /// </summary>
        /// <returns>0 on success</returns>
        public int GetNextFrame()
        {

            /* TODO
             * What about packets with more than one frames?!
             */

            int ret;
            bool draining = false;

            while (!draining)
            {
                ret = demuxer.GetNextPacket(VideoStream.StreamIndex);
                if (ret != 0)
                {
                    if (ret != AVERROR_EOF) return ret;
                    draining = true;
                    demuxer.packet = av_packet_alloc();
                    demuxer.packet->data = null;
                    demuxer.packet->size = 0;
                }

                // Send Packet for decoding
                ret = avcodec_send_packet(codecCtx, demuxer.packet);
                av_packet_unref(demuxer.packet);
                if (ret != 0) return ret; // handle EOF/error

                while (true)
                {
                    // Receive all available frames for the decoder
                    ret = avcodec_receive_frame(codecCtx, frame);
                    if (ret == AVERROR(EAGAIN)) return GetNextFrame();
                    if (ret != 0) { av_frame_unref(frame); return ret; }

                    // Get frame pts (prefer best_effort_timestamp)
                    if (frame->best_effort_timestamp == AV_NOPTS_VALUE) frame->best_effort_timestamp = frame->pts;
                    if (frame->best_effort_timestamp == AV_NOPTS_VALUE) { av_frame_unref(frame); continue; }

                    av_packet_unref(demuxer.packet);
                    return 0;
                }
            }

            return -1;
        }

        public void DisposeFrames()
        {
            while (!Frames.IsEmpty)
            {
                Frames.TryDequeue(out VideoFrame frame);
                DisposeFrame(frame);
            }
        }
        public static void DisposeFrame(VideoFrame frame) { if (frame != null && frame.textures != null) for (int i=0; i<frame.textures.Length; i++) frame.textures[i].Dispose(); }
        protected override void DisposeInternal()
        {
            av_buffer_unref(&codecCtx->hw_device_ctx);
            if (swsCtx != null) { sws_freeContext(swsCtx); swsCtx = null; }
            DisposeFrames();
            StartTime = AV_NOPTS_VALUE;
        }
    }
}