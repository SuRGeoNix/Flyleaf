using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using Vortice;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaRemuxer;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public unsafe class VideoDecoder : DecoderBase
    {
        public ConcurrentQueue<VideoFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<VideoFrame>();
        public Renderer         Renderer            { get; private set; }
        public bool             VideoAccelerated    { get; internal set; }
        public bool             ZeroCopy            { get; internal set; }
        
        public VideoStream      VideoStream         => (VideoStream) Stream;

        public long             StartTime           { get; internal set; } = AV_NOPTS_VALUE;
        public long             StartRecordTime     { get; internal set; } = AV_NOPTS_VALUE;

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

        // Reverse Playback
        ConcurrentStack<List<IntPtr>>   curReverseVideoStack    = new ConcurrentStack<List<IntPtr>>();
        List<IntPtr>                    curReverseVideoPackets  = new List<IntPtr>();
        List<VideoFrame>                curReverseVideoFrames   = new List<VideoFrame>();
        int                             curReversePacketPos     = 0;

        static VideoDecoder()
        {
            if (Engine.FFmpeg.IsVer5OrGreater)
            {
                HW_PIX_FMT              -= 2;
                //AV_PIX_FMT_P010LE       -= 2;
                //AV_PIX_FMT_P010BE       -= 2;
                //AV_PIX_FMT_YUV420P10LE  -= 2;
            }
        }
        public VideoDecoder(Config config, int uniqueId = -1) : base(config, uniqueId)
        {
            getHWformat = new AVCodecContext_get_format(get_format);
        }

        public void CreateRenderer() // TBR: It should be in the constructor but DecoderContext will not work with null VideoDecoder for AudioOnly
        {
            if (Renderer == null)
                Renderer = new Renderer(this, IntPtr.Zero, UniqueId);
            else if (Renderer.Disposed)
                Renderer.Initialize();

            Disposed = false;
        }
        public void DestroyRenderer() => Renderer?.Dispose();
        public void CreateSwapChain(IntPtr handle)
        {
            CreateRenderer();
            Renderer.InitializeSwapChain(handle);
        }
        public void CreateSwapChain(Action<IDXGISwapChain2> swapChainWinUIClbk)
        {
            Renderer.SwapChainWinUIClbk = swapChainWinUIClbk;
            if (Renderer.SwapChainWinUIClbk != null)
                Renderer.InitializeWinUISwapChain();

        }
        public void DestroySwapChain() => Renderer?.DisposeSwapChain();

        #region Video Acceleration (Should be disposed seperately)
        const int               AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        const AVHWDeviceType    HW_DEVICE   = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA; // To fully support Win7/8 should consider AV_HWDEVICE_TYPE_DXVA2

        // We can't use AVPixelFormat enum as it is different between ffmpeg v4 and v5 (two new entries after 44 so we need -2 for >46 )
        static int HW_PIX_FMT               = (int)AVPixelFormat.AV_PIX_FMT_D3D11;
        //static int AV_PIX_FMT_P010LE        = (int)AVPixelFormat.AV_PIX_FMT_P010LE;
        //static int AV_PIX_FMT_P010BE        = (int)AVPixelFormat.AV_PIX_FMT_P010BE;
        //static int AV_PIX_FMT_YUV420P10LE   = (int)AVPixelFormat.AV_PIX_FMT_YUV420P10LE;

        internal ID3D11Texture2D
                                textureFFmpeg;
        AVCodecContext_get_format 
                                getHWformat;
        bool                    disableGetFormat;
        AVBufferRef*            hwframes;
        AVBufferRef*            hw_device_ctx;

        internal static bool CheckCodecSupport(AVCodec* codec)
        {
            for (int i = 0; ; i++)
            {
                AVCodecHWConfig* config = avcodec_get_hw_config(codec, i);
                if (config == null) break;
                if ((config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == 0 || config->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE) continue;
                
                if (config->device_type == HW_DEVICE && (int)config->pix_fmt == HW_PIX_FMT) return true;
            }

            return false;
        }
        internal int InitVA()
        {
            int ret;
            AVHWDeviceContext*      device_ctx;
            AVD3D11VADeviceContext* d3d11va_device_ctx;

            if (Renderer.Device == null || hw_device_ctx != null) return -1;

            hw_device_ctx  = av_hwdevice_ctx_alloc(HW_DEVICE);

            device_ctx          = (AVHWDeviceContext*) hw_device_ctx->data;
            d3d11va_device_ctx  = (AVD3D11VADeviceContext*) device_ctx->hwctx;
            d3d11va_device_ctx->device
                                = (FFmpeg.AutoGen.ID3D11Device*) Renderer.Device.NativePointer;

            ret = av_hwdevice_ctx_init(hw_device_ctx);
            if (ret != 0)
            {
                Log.Error($"VA Failed - {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
                
                fixed(AVBufferRef** ptr = &hw_device_ctx)
                    av_buffer_unref(ptr);

                hw_device_ctx = null;
            }

            Renderer.Device.AddRef(); // Important to give another reference for FFmpeg so we can dispose without issues

            return ret;
        }

        private AVPixelFormat get_format(AVCodecContext* avctx, AVPixelFormat* pix_fmts)
        {
            if (disableGetFormat)
                return avcodec_default_get_format(avctx, pix_fmts);

            if (CanDebug) Log.Debug($"Codec profile {avcodec_profile_name(codecCtx->codec_id, codecCtx->profile)}");

            int  ret = 0;
            bool foundHWformat = false;
            
            while (*pix_fmts != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                if (CanTrace) Log.Trace($"{*pix_fmts}");

                if ((int)(*pix_fmts) == HW_PIX_FMT)
                {
                    foundHWformat = true;
                    break;
                }

                pix_fmts++;
            }

            ret = ShouldAllocateNew();

            if (foundHWformat && ret == 0)
            {
                if (CanTrace) Log.Trace("HW frames already allocated");

                if (hwframes != null && codecCtx->hw_frames_ctx == null)
                    codecCtx->hw_frames_ctx = av_buffer_ref(hwframes);

                textDesc.Format = textureFFmpeg.Description.Format;

                return (AVPixelFormat)HW_PIX_FMT;
            }

            lock (lockCodecCtx)
            {
                if (!foundHWformat || !VideoAccelerated || AllocateHWFrames() != 0)
                {
                    if (CanWarn) Log.Warn("HW format not found. Fallback to sw format");

                    lock (Renderer.lockDevice)
                    {
                        VideoAccelerated = false;
                        ZeroCopy = false;

                        if (hw_device_ctx != null)
                        fixed(AVBufferRef** ptr = &hw_device_ctx)
                            av_buffer_unref(ptr);

                        if (codecCtx->hw_device_ctx != null)
                            av_buffer_unref(&codecCtx->hw_device_ctx);

                        if (codecCtx->hw_frames_ctx != null)
                            av_buffer_unref(&codecCtx->hw_frames_ctx);

                        hw_device_ctx = null;
                        codecCtx->hw_device_ctx = null;
                        filledFromCodec = false;
                        disableGetFormat = true;
                    }

                    return avcodec_default_get_format(avctx, pix_fmts);
                }
                
                if (CanDebug) Log.Debug("HW frame allocation completed");

                // TBR: Catch codec changed on live streams (check codec/profiles and check even on sw frames)
                if (ret == 2)
                {
                    Log.Warn($"Codec changed {VideoStream.CodecID} {VideoStream.Width}x{VideoStream.Height} => {codecCtx->codec_id} {codecCtx->width}x{codecCtx->height}");
                    filledFromCodec = false;
                }

                return (AVPixelFormat)HW_PIX_FMT;
            }
        }
        private int ShouldAllocateNew() // 0: No, 1: Yes, 2: Yes+Codec Changed
        {
            if (hwframes == null)
                return 1;

            var t2 = (AVHWFramesContext*) hwframes->data;

            if (codecCtx->coded_width != t2->width)
                return 2;

            if (codecCtx->coded_height != t2->height)
                return 2;

            // TBR: Codec changed (seems ffmpeg changes codecCtx by itself
            //if (codecCtx->codec_id != VideoStream.CodecID)
            //    return 2;

            //var fmt = codecCtx->sw_pix_fmt == (AVPixelFormat)AV_PIX_FMT_YUV420P10LE ? (AVPixelFormat)AV_PIX_FMT_P010LE : (codecCtx->sw_pix_fmt == (AVPixelFormat)AV_PIX_FMT_P010BE ? (AVPixelFormat)AV_PIX_FMT_P010BE : AVPixelFormat.AV_PIX_FMT_NV12);
            //if (fmt != t2->sw_format)
            //    return 2;

            return 0;
        }

        private int AllocateHWFrames()
        {
            if (hwframes != null)
                fixed(AVBufferRef** ptr = &hwframes)
                    av_buffer_unref(ptr);
            
            hwframes = null;

            if (codecCtx->hw_frames_ctx != null)
                av_buffer_unref(&codecCtx->hw_frames_ctx);

            if (avcodec_get_hw_frames_parameters(codecCtx, codecCtx->hw_device_ctx, (AVPixelFormat)HW_PIX_FMT, &codecCtx->hw_frames_ctx) != 0)
                return -1;

            AVHWFramesContext* hw_frames_ctx = (AVHWFramesContext*)(codecCtx->hw_frames_ctx->data);
            hw_frames_ctx->initial_pool_size += Config.Decoder.MaxVideoFrames;

            AVD3D11VAFramesContext *va_frames_ctx = (AVD3D11VAFramesContext *)hw_frames_ctx->hwctx;
            va_frames_ctx->BindFlags  |= (uint)BindFlags.Decoder | (uint)BindFlags.ShaderResource;
            
            hwframes = av_buffer_ref(codecCtx->hw_frames_ctx);

            int ret = av_hwframe_ctx_init(codecCtx->hw_frames_ctx);
            if (ret == 0)
            {
                lock (Renderer.lockDevice)
                {
                    textureFFmpeg   = new ID3D11Texture2D((IntPtr) va_frames_ctx->texture);
                    textDesc.Format = textureFFmpeg.Description.Format;
                    ZeroCopy = Config.Decoder.ZeroCopy == FlyleafLib.ZeroCopy.Enabled || (Config.Decoder.ZeroCopy == FlyleafLib.ZeroCopy.Auto && codecCtx->width == textureFFmpeg.Description.Width && codecCtx->height == textureFFmpeg.Description.Height);
                    filledFromCodec = false;
                }
            }

            return ret;
        }
        internal void RecalculateZeroCopy()
        {
            lock (Renderer.lockDevice)
            {
                bool save = ZeroCopy;
                ZeroCopy = Config.Decoder.ZeroCopy == FlyleafLib.ZeroCopy.Enabled || (Config.Decoder.ZeroCopy == FlyleafLib.ZeroCopy.Auto && codecCtx->width == textureFFmpeg.Description.Width && codecCtx->height == textureFFmpeg.Description.Height);
                if (save != ZeroCopy)
                {
                    Renderer?.FrameResized();
                    CodecChanged?.Invoke(this);
                }
            }
        }
        #endregion

        protected override int Setup(AVCodec* codec)
        {
            // Ensures we have a renderer (no swap chain is required)
            CreateRenderer();
            
            VideoAccelerated = false;
            if (Config.Video.VideoAcceleration && Renderer.Device.FeatureLevel >= Vortice.Direct3D.FeatureLevel.Level_10_0)
            {
                if (CheckCodecSupport(codec))
                {
                    if (InitVA() == 0)
                    {
                        codecCtx->hw_device_ctx = av_buffer_ref(hw_device_ctx);
                        VideoAccelerated = true;
                        Log.Debug("VA Success");
                    }
                }
                else
                    Log.Info($"VA {codec->id} not supported");
            }
            else
                Log.Debug("VA Disabled");

            // Can't get data from here?
            //var t1 = av_stream_get_side_data(VideoStream.AVStream, AVPacketSideDataType.AV_PKT_DATA_MASTERING_DISPLAY_METADATA, null);
            //var t2 = av_stream_get_side_data(VideoStream.AVStream, AVPacketSideDataType.AV_PKT_DATA_CONTENT_LIGHT_LEVEL, null);

            HDRDataSent = false;
            keyFrameRequired = true;
            ZeroCopy = false;
            filledFromCodec = false;

            if (VideoAccelerated)
            {
                codecCtx->thread_count = 1;
                codecCtx->hwaccel_flags |= AV_HWACCEL_FLAG_IGNORE_LEVEL;
                if (Config.Decoder.AllowProfileMismatch)
                    codecCtx->hwaccel_flags |= AV_HWACCEL_FLAG_ALLOW_PROFILE_MISMATCH;

                codecCtx->pix_fmt = (AVPixelFormat)HW_PIX_FMT;
                codecCtx->get_format = getHWformat;
                disableGetFormat = false;
            }
            else
            {
                codecCtx->thread_count = Math.Min(Config.Decoder.VideoThreads, codecCtx->codec_id == AV_CODEC_ID_HEVC ? 32 : 16);
                codecCtx->thread_type  = 0;
            }

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
            if (demuxer.IsReversePlayback)
            {
                RunInternalReverse();
                return;
            }

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
                                // TODO: let the demuxer push the draining packet
                                Log.Debug("Draining");
                                Status = Status.Draining;
                                AVPacket* drainPacket = av_packet_alloc();
                                drainPacket->data = null;
                                drainPacket->size = 0;
                                demuxer.VideoPackets.Enqueue(drainPacket);
                            }
                            
                            break;
                        }
                        else if (!demuxer.IsRunning)
                        {
                            if (CanDebug) Log.Debug($"Demuxer is not running [Demuxer Status: {demuxer.Status}]");

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
                    packet = demuxer.VideoPackets.Dequeue();

                    if (isRecording)
                    {
                        if (!recGotKeyframe && (packet->flags & AV_PKT_FLAG_KEY) != 0)
                        {
                            recGotKeyframe = true;
                            StartRecordTime = (long)(packet->pts * VideoStream.Timebase) - demuxer.StartTime;
                        }

                        if (recGotKeyframe)
                            curRecorder.Write(av_packet_clone(packet));
                    }

                    // TBR: AVERROR(EAGAIN) means avcodec_receive_frame but after resend the same packet
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
                            if (CanWarn) Log.Warn($"{FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                            if (allowedErrors == 0) { Log.Error("Too many errors!"); Status = Status.Stopping; break; }

                            continue;
                        }
                    }
                    
                    while (true)
                    {
                        ret = avcodec_receive_frame(codecCtx, frame);
                        if (ret != 0) { av_frame_unref(frame); break; }

                        if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                            frame->pts = frame->best_effort_timestamp;
                        else if (frame->pts == AV_NOPTS_VALUE)
                            { av_frame_unref(frame); continue; }

                        if (keyFrameRequired)
                        {
                            if (frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I && frame->key_frame != 1)
                            {
                                if (CanWarn) Log.Warn($"Seek to keyframe failed [{frame->pict_type} | {frame->key_frame}]");
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

            if (isRecording) { StopRecording(); recCompleted(MediaType.Video); }

            if (Status == Status.Draining) Status = Status.Ended;
        }

        private void RunInternalReverse()
        {
            int ret = 0;
            int allowedErrors = Config.Decoder.MaxErrors;
            AVPacket *packet;

            do
            {
                // While Packets Queue Empty (Drain | Quit if Demuxer stopped | Wait until we get packets)
                if (demuxer.VideoPacketsReverse.IsEmpty && curReverseVideoStack.IsEmpty && curReverseVideoPackets.Count == 0)
                {
                    CriticalArea = true;

                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueEmpty;
                    
                    while (demuxer.VideoPacketsReverse.IsEmpty && Status == Status.QueueEmpty)
                    {
                        if (demuxer.Status == Status.Ended) // TODO
                        {
                            lock (lockStatus) Status = Status.Ended;
                            
                            break;
                        }
                        else if (!demuxer.IsRunning)
                        {
                            if (CanDebug) Log.Debug($"Demuxer is not running [Demuxer Status: {demuxer.Status}]");

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
                        if (Status != Status.QueueEmpty) break;
                        Status = Status.Running;
                    }
                }

                if (curReverseVideoPackets.Count == 0)
                {
                    if (curReverseVideoStack.IsEmpty)
                        demuxer.VideoPacketsReverse.TryDequeue(out curReverseVideoStack);

                    curReverseVideoStack.TryPop(out curReverseVideoPackets);
                    curReversePacketPos = 0;
                }

                keyFrameRequired = false;

                while (curReverseVideoPackets.Count > 0 && Status == Status.Running)
                {
                    // Wait until Queue not Full or Stopped
                    if (Frames.Count + curReverseVideoFrames.Count >= Config.Decoder.MaxVideoFrames)
                    {
                        lock (lockStatus)
                            if (Status == Status.Running) Status = Status.QueueFull;

                        while (Frames.Count + curReverseVideoFrames.Count >= Config.Decoder.MaxVideoFrames && Status == Status.QueueFull) Thread.Sleep(20);

                        lock (lockStatus)
                        {
                            if (Status != Status.QueueFull) break;
                            Status = Status.Running;
                        }
                    }

                    lock (lockCodecCtx)
                    {
                        if (keyFrameRequired == true)
                        {
                            curReversePacketPos = 0;
                            break;
                        }

                        packet = (AVPacket*)curReverseVideoPackets[curReversePacketPos++];
                        ret = avcodec_send_packet(codecCtx, packet);

                        if (ret != 0 && ret != AVERROR(EAGAIN))
                        {
                            if (ret == AVERROR_EOF) { Status = Status.Ended; break; }

                            if (CanWarn) Log.Warn($"{FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                            allowedErrors--;
                            if (allowedErrors == 0) { Log.Error("Too many errors!"); Status = Status.Stopping; break; }

                            for (int i=curReverseVideoPackets.Count-1; i>=curReversePacketPos-1; i--)
                            {
                                packet = (AVPacket*)curReverseVideoPackets[i];
                                av_packet_free(&packet);
                                curReverseVideoPackets[curReversePacketPos - 1] = IntPtr.Zero;
                                curReverseVideoPackets.RemoveAt(i);
                            }

                            avcodec_flush_buffers(codecCtx);
                            curReversePacketPos = 0;

                            for (int i=curReverseVideoFrames.Count -1; i>=0; i--)
                                Frames.Enqueue(curReverseVideoFrames[i]);

                            curReverseVideoFrames.Clear();

                            continue;
                        }

                        while (true)
                        {
                            ret = avcodec_receive_frame(codecCtx, frame);
                            if (ret != 0) { av_frame_unref(frame); break; }

                            if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                                frame->pts = frame->best_effort_timestamp;
                            else if (frame->pts == AV_NOPTS_VALUE)
                                { av_frame_unref(frame); continue; }

                            bool shouldProcess = curReverseVideoPackets.Count - curReversePacketPos < Config.Decoder.MaxVideoFrames;

                            if (shouldProcess)
                            {
                                av_packet_free(&packet);
                                curReverseVideoPackets[curReversePacketPos - 1] = IntPtr.Zero;
                                VideoFrame mFrame = ProcessVideoFrame(frame);
                                if (mFrame != null) curReverseVideoFrames.Add(mFrame);
                            }
                            else
                            av_frame_unref(frame);
                        }

                        if (curReversePacketPos == curReverseVideoPackets.Count)
                        {
                            curReverseVideoPackets.RemoveRange(Math.Max(0, curReverseVideoPackets.Count - Config.Decoder.MaxVideoFrames), Math.Min(curReverseVideoPackets.Count, Config.Decoder.MaxVideoFrames) );
                            avcodec_flush_buffers(codecCtx);
                            curReversePacketPos = 0;

                            for (int i=curReverseVideoFrames.Count -1; i>=0; i--)
                                Frames.Enqueue(curReverseVideoFrames[i]);

                            curReverseVideoFrames.Clear();
                            
                            break; // force recheck for max queues etc...
                        }

                    } // Lock CodecCtx

                    // Import Sleep required to prevent delay during Renderer.Present
                    // TBR: Might Monitor.TryEnter with priorities between decoding and rendering will work better
                    Thread.Sleep(10);
                    
                } // while curReverseVideoPackets.Count > 0

            } while (Status == Status.Running);

            if (Status != Status.Pausing && Status != Status.Paused)
                curReversePacketPos = 0;
        }
        
        internal VideoFrame ProcessVideoFrame(AVFrame* frame)
        {
            try
            {
                if (!filledFromCodec)
                {
                    filledFromCodec = true;

                    avcodec_parameters_from_context(Stream.AVStream->codecpar, codecCtx);
                    VideoStream.Refresh(codecCtx->sw_pix_fmt != AVPixelFormat.AV_PIX_FMT_NONE ? codecCtx->sw_pix_fmt : codecCtx->pix_fmt);
                    
                    if (VideoStream.PixelFormat != AVPixelFormat.AV_PIX_FMT_NONE)
                    {
                        textDesc = new Texture2DDescription()
                        {
                            Usage               = ResourceUsage.Default,
                            BindFlags           = Renderer.Device.FeatureLevel < Vortice.Direct3D.FeatureLevel.Level_10_0 ? BindFlags.ShaderResource : BindFlags.ShaderResource | BindFlags.RenderTarget,

                            Width               = codecCtx->width,
                            Height              = codecCtx->height,

                            SampleDescription   = new SampleDescription(1, 0),
                            ArraySize           = 1,
                            MipLevels           = 1
                        };

                        textDesc.Format = VideoAccelerated ? textureFFmpeg.Description.Format : (VideoStream.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm);

                        textDescUV = new Texture2DDescription()
                        {
                            Usage               = ResourceUsage.Default,
                            BindFlags           = Renderer.Device.FeatureLevel < Vortice.Direct3D.FeatureLevel.Level_10_0 ? BindFlags.ShaderResource : BindFlags.ShaderResource | BindFlags.RenderTarget,

                            Format              = VideoStream.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                            Width               = codecCtx->width  >> VideoStream.PixelFormatDesc->log2_chroma_w,
                            Height              = codecCtx->height >> VideoStream.PixelFormatDesc->log2_chroma_h,

                            SampleDescription   = new SampleDescription(1, 0),
                            ArraySize           = 1,
                            MipLevels           = 1
                        };
                    }

                    if (!(VideoStream.FPS > 0)) // NaN
                    {
                        VideoStream.FPS             = av_q2d(codecCtx->framerate) > 0 ? av_q2d(codecCtx->framerate) : 0;
                        VideoStream.FrameDuration   = VideoStream.FPS > 0 ? (long) (10000000 / VideoStream.FPS) : 0;
                    }

                    Renderer?.FrameResized();
                    CodecChanged?.Invoke(this);
                }

                if (Speed != 1)
                {
                    curSpeedFrame++;
                    if (curSpeedFrame < Speed)
                        return null;

                    curSpeedFrame = 0;                    
                }

                // TODO
                //mFrame.timestamp = (long)(frame->pts * VideoStream.Timebase) - VideoStream.StartTime;

                VideoFrame mFrame = new VideoFrame();
                mFrame.timestamp = (long)(frame->pts * VideoStream.Timebase) - demuxer.StartTime;
                if (CanTrace) Log.Trace($"Processes {Utils.TicksToTime(mFrame.timestamp)}");

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

                    // TBR: It is possible that FFmpeg will decide to re-create a new hw frames pool (if we provide wrong threads/initial pool size etc?)
                    //if ((IntPtr) frame->data.ToArray()[0] != (IntPtr) (((AVD3D11VAFramesContext *)((AVHWFramesContext*)hwframes->data)->hwctx)->texture))
                        //textureFFmpeg = new ID3D11Texture2D((IntPtr) frame->data.ToArray()[0]);

                    if (ZeroCopy)
                    {
                        mFrame.bufRef       = av_buffer_ref(frame->buf.ToArray()[0]);
                        mFrame.subresource  = (int) frame->data.ToArray()[1];
                    }
                    else
                    {
                        mFrame.textures     = new ID3D11Texture2D[1];
                        mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc);
                        Renderer.Device.ImmediateContext.CopySubresourceRegion(
                            mFrame.textures[0], 0, 0, 0, 0, // dst
                            textureFFmpeg, (int)frame->data.ToArray()[1],  // src
                            new Box(0, 0, 0, mFrame.textures[0].Description.Width, mFrame.textures[0].Description.Height, 1)); // crop decoder's padding
                    }
                }

                // Software Frame (8-bit YUV)   | YUV byte* -> Device Texture[3] (RX) / SRV (RX_RX_RX) -> PixelShader (Y_U_V)
                else if (VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                {
                    /* TODO
                     * Check which formats are suported from DXGI textures and the possibility to upload them directly so we can just blit them to rgba
                     * If not supported from DXGI just uploaded to one supported and process it on GPU with pixelshader
                     * Support > 8 bit
                     */

                    mFrame.textures = new ID3D11Texture2D[3];

                    // YUV Planar [Y0 ...] [U0 ...] [V0 ....]
                    if (VideoStream.IsPlanar)
                    {
                        SubresourceData db  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[0];
                        db.RowPitch         = frame->linesize.ToArray()[0];
                        mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc, new SubresourceData[] { db });
                        
                        db                  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[1];
                        db.RowPitch         = frame->linesize.ToArray()[1];
                        mFrame.textures[1]  = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { db });

                        db                  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[2];
                        db.RowPitch         = frame->linesize.ToArray()[2];
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

                        dbY.RowPitch    = textDesc.  Width;
                        dbU.RowPitch    = textDescUV.Width;
                        dbV.RowPitch    = textDescUV.Width;

                        long totalSize = frame->linesize.ToArray()[0] * textDesc.Height;

                        byte* dataPtr = frame->data.ToArray()[0];
                        AVComponentDescriptor[] comps = VideoStream.PixelFormatDesc->comp.ToArray();

                        for (int i=comps[0].offset; i<totalSize; i+=comps[0].step)
                            dsY.WriteByte(*(dataPtr + i));

                        for (int i=comps[1].offset; i<totalSize; i+=comps[1].step)
                            dsU.WriteByte(*(dataPtr + i));

                        for (int i=comps[2].offset; i<totalSize; i+=comps[2].step)
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
                        if (swsCtx == null) { Log.Error($"Failed to allocate SwsContext"); return null; }
                    }

                    sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, outData, outLineSize);

                    SubresourceData db  = new SubresourceData();
                    db.DataPointer      = (IntPtr)outData.ToArray()[0];
                    db.RowPitch         = outLineSize[0];
                    mFrame.textures     = new ID3D11Texture2D[1];
                    mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc, new SubresourceData[] { db });
                }

                av_frame_unref(frame);

                return mFrame;

            } catch (Exception e)
            {
                Log.Error($"Failed to process frame ({e.Message})");
                av_frame_unref(frame);

                return null; 
            }
        }

        public void RefreshMaxVideoFrames()
        {
            lock (lockActions)
            {
                if (VideoStream == null)
                    return;

                bool wasRunning = IsRunning;

                var stream = Stream;

                Dispose();
                Open(stream);

                if (wasRunning)
                    Start();
            }
        }

        public int GetFrameNumber(long timestamp)
        {
            // Incoming timestamps are zero-base from demuxer start time (not from video stream start time)
            timestamp -= (VideoStream.StartTime - demuxer.StartTime);

            if (timestamp < 1)
                return 0;

            // offset 2ms
            return (int) ((timestamp + 20000) / VideoStream.FrameDuration);
        }

        /// <summary>
        /// Performs accurate seeking to the requested video frame and returns it
        /// </summary>
        /// <param name="index">Zero based frame index</param>
        /// <returns>The requested VideoFrame or null on failure</returns>
        public VideoFrame GetFrame(int index)
        {
            int ret;

            // Calculation of FrameX timestamp (based on fps/avgFrameDuration) | offset 2ms
            long frameTimestamp = VideoStream.StartTime + (long) (index * VideoStream.FrameDuration) - 20000;
            //Log.Debug($"Searching for {Utils.TicksToTime(frameTimestamp)}");

            demuxer.Pause();
            Pause();

            // TBR
            //if (demuxer.FormatContext->pb != null)
            //    avio_flush(demuxer.FormatContext->pb);
            //avformat_flush(demuxer.FormatContext);

            // Seeking at frameTimestamp or previous I/Key frame and flushing codec | Temp fix (max I/distance 3sec) for ffmpeg bug that fails to seek on keyframe with HEVC
            // More issues with mpegts seeking backwards (those should be used also in the reverse playback in the demuxer)
            demuxer.Interrupter.Request(MediaDemuxer.Requester.Seek);
            if (codecCtx->codec_id == AV_CODEC_ID_HEVC || (demuxer.FormatContext->iformat != null && demuxer.FormatContext->iformat->read_seek.Pointer == IntPtr.Zero))
                ret = av_seek_frame(demuxer.FormatContext, -1, Math.Max(0, frameTimestamp - (3 * (long)1000 * 10000)) / 10, AVSEEK_FLAG_ANY);
            else
                ret = av_seek_frame(demuxer.FormatContext, -1, frameTimestamp / 10, AVSEEK_FLAG_FRAME | AVSEEK_FLAG_BACKWARD);

            demuxer.DisposePackets();

            if (demuxer.Status == Status.Ended) demuxer.Status = Status.Stopped;
            if (ret < 0) return null; // handle seek error
            Flush();
            keyFrameRequired = false;
            StartTime = frameTimestamp - VideoStream.StartTime; // required for audio sync

            // Decoding until requested frame/timestamp
            bool checkExtraFrames = false;

            while (GetFrameNext(checkExtraFrames) == 0)
            {
                // Skip frames before our actual requested frame
                if ((long)(frame->pts * VideoStream.Timebase) < frameTimestamp)
                {
                    //Log.Debug($"[Skip] [pts: {frame->pts}] [time: {Utils.TicksToTime((long)(frame->pts * VideoStream.Timebase))}] | [fltime: {Utils.TicksToTime(((long)(frame->pts * VideoStream.Timebase) - demuxer.StartTime))}]");
                    av_frame_unref(frame);
                    checkExtraFrames = true;
                    continue; 
                }

                //Log.Debug($"[Found] [pts: {frame->pts}] [time: {Utils.TicksToTime((long)(frame->pts * VideoStream.Timebase))}] | {Utils.TicksToTime(VideoStream.StartTime + (index * VideoStream.FrameDuration))} | [fltime: {Utils.TicksToTime(((long)(frame->pts * VideoStream.Timebase) - demuxer.StartTime))}]");
                return ProcessVideoFrame(frame);
            }

            return null;
        }

        /// <summary>
        /// Demuxes until the next valid video frame (will be stored in AVFrame* frame)
        /// </summary>
        /// <returns>0 on success</returns>
        /// 
        public VideoFrame GetFrameNext()
        {
            if (GetFrameNext(true) != 0) return null;

            return ProcessVideoFrame(frame);
        }

        /// <summary>
        /// Pushes the demuxer and the decoder to the next available video frame
        /// </summary>
        /// <param name="checkExtraFrames">Whether to check for extra frames within the decoder's cache. Set to true if not sure.</param>
        /// <returns></returns>
        public int GetFrameNext(bool checkExtraFrames)
        {
            // TODO: Should know if draining to be able to get more than one drained frames

            int ret;
            int allowedErrors = Config.Decoder.MaxErrors;

            if (checkExtraFrames)
            {
                ret = avcodec_receive_frame(codecCtx, frame);

                if (ret == 0)
                {
                    if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                        frame->pts = frame->best_effort_timestamp;
                    else if (frame->pts == AV_NOPTS_VALUE)
                    {
                        av_frame_unref(frame);
                        return GetFrameNext(true);
                    }

                    return 0;
                }

                if (ret != AVERROR(EAGAIN)) return ret;
            }

            while (true)
            {
                ret = demuxer.GetNextVideoPacket();
                if (ret != 0 && demuxer.Status != Status.Ended)
                    return ret;

                ret = avcodec_send_packet(codecCtx, demuxer.packet);
                av_packet_unref(demuxer.packet);

                if (ret != 0)
                {
                    if (allowedErrors < 1 || demuxer.Status == Status.Ended) return ret;
                    allowedErrors--;
                    continue;
                }

                ret = avcodec_receive_frame(codecCtx, frame);
                
                if (ret == AVERROR(EAGAIN))
                    continue;

                if (ret != 0)
                {
                    av_frame_unref(frame);
                    return ret;
                }

                if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                    frame->pts = frame->best_effort_timestamp;
                else if (frame->pts == AV_NOPTS_VALUE)
                {
                    av_frame_unref(frame);
                    return GetFrameNext(true);
                }

                return 0;
            }
        }

        public void DisposeFrames()
        {
            while (!Frames.IsEmpty)
            {
                Frames.TryDequeue(out VideoFrame frame);
                DisposeFrame(frame);
            }

            DisposeFramesReverse();
        }
        private void DisposeFramesReverse()
        {
            while (!curReverseVideoStack.IsEmpty)
            {
                curReverseVideoStack.TryPop(out var t2);
                for (int i = 0; i<t2.Count; i++)
                { 
                    if (t2[i] == IntPtr.Zero) continue;
                    AVPacket* packet = (AVPacket*)t2[i];
                    av_packet_free(&packet);
                }
            }

            for (int i = 0; i<curReverseVideoPackets.Count; i++)
            { 
                if (curReverseVideoPackets[i] == IntPtr.Zero) continue;
                AVPacket* packet = (AVPacket*)curReverseVideoPackets[i];
                av_packet_free(&packet);
            }

            curReverseVideoPackets.Clear();

            for (int i=0; i<curReverseVideoFrames.Count; i++)
                DisposeFrame(curReverseVideoFrames[i]);

            curReverseVideoFrames.Clear();
        }
        public static void DisposeFrame(VideoFrame frame)
        {
            if (frame == null)
                return;

            if (frame.textures != null)
                for (int i=0; i<frame.textures.Length; i++)
                    frame.textures[i].Dispose();

            if (frame.bufRef != null)
                fixed (AVBufferRef** ptr = &frame.bufRef)
                    av_buffer_unref(ptr);

            frame.textures  = null;
            frame.bufRef    = null;
        }
        protected override void DisposeInternal()
        {
            lock (lockCodecCtx)
            {
                DisposeFrames();

                if (codecCtx != null)
                {
                    avcodec_close(codecCtx);
                    fixed (AVCodecContext** ptr = &codecCtx) avcodec_free_context(ptr);

                    codecCtx = null;
                }

                if (hwframes != null)
                fixed(AVBufferRef** ptr = &hwframes)
                    av_buffer_unref(ptr);
                
                if (hw_device_ctx != null)
                    fixed(AVBufferRef** ptr = &hw_device_ctx)
                        av_buffer_unref(ptr);

                if (swsCtx != null)
                    sws_freeContext(swsCtx);
                
                hwframes    = null;
                swsCtx      = null;
                StartTime   = AV_NOPTS_VALUE;
                
                #if DEBUG
                Renderer.ReportLiveObjects();
                #endif
            }
        }

        internal Action<MediaType> recCompleted;
        Remuxer curRecorder;
        bool recGotKeyframe;
        internal bool isRecording;

        internal void StartRecording(Remuxer remuxer)
        {
            if (Disposed || isRecording) return;

            StartRecordTime     = AV_NOPTS_VALUE;
            curRecorder         = remuxer;
            recGotKeyframe      = false;
            isRecording         = true;
        }

        internal void StopRecording()
        {
            isRecording = false;
        }
    }
}