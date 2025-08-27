using System.Runtime.InteropServices;

using Vortice.DXGI;
using Vortice.Direct3D11;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaRemuxer;

namespace FlyleafLib.MediaFramework.MediaDecoder;

public unsafe class VideoDecoder : DecoderBase
{
    public ConcurrentQueue<VideoFrame>
                            Frames              { get; protected set; } = [];
    public Renderer         Renderer            { get; private set; }
    public bool             VideoAccelerated    { get; internal set; }
    public bool             ZeroCopy            { get; internal set; }

    public VideoStream      VideoStream         => (VideoStream) Stream;

    public long             StartTime           { get; internal set; } = AV_NOPTS_VALUE;
    public long             StartRecordTime     { get; internal set; } = AV_NOPTS_VALUE;

    const AVPixelFormat     PIX_FMT_HWACCEL     = AVPixelFormat.D3d11;
    const SwsFlags          SCALING_HQ          = SwsFlags.AccurateRnd | SwsFlags.Bitexact | SwsFlags.Lanczos | SwsFlags.FullChrHInt | SwsFlags.FullChrHInp;
    const SwsFlags          SCALING_LQ          = SwsFlags.Bicublin;

    internal SwsContext*    swsCtx;
    nint                    swsBufferPtr;
    internal byte_ptrArray4 swsData;
    internal int_array4     swsLineSize;

    internal bool           swFallback;
    internal bool           keyPacketRequired;
    internal bool           isIntraOnly;
    internal long           startPts;
    internal long           lastFixedPts;

    bool                    checkExtraFrames; // DecodeFrameNext

    // Reverse Playback
    ConcurrentStack<List<nint>>
                            curReverseVideoStack    = [];
    List<nint>              curReverseVideoPackets  = [];
    List<VideoFrame>        curReverseVideoFrames   = [];
    int                     curReversePacketPos     = 0;

    // Drop frames if FPS is higher than allowed
    int                     curSpeedFrame           = 9999; // don't skip first frame (on start/after seek-flush)
    double                  skipSpeedFrames         = 0;

    // Fixes Seek Backwards failure on broken formats
    long                    curFixSeekDelta         = 0;
    const long              FIX_SEEK_DELTA_MCS      = 2_100_000;

    public VideoDecoder(Config config, int uniqueId = -1) : base(config, uniqueId)
        => getHWformat = new AVCodecContext_get_format(get_format);

    protected override void OnSpeedChanged(double value)
    {
        if (VideoStream == null) return;
        speed = value;
        skipSpeedFrames = speed * VideoStream.FPS / Config.Video.MaxOutputFps;
    }

    /// <summary>
    /// Prevents to get the first frame after seek/flush
    /// </summary>
    public void ResetSpeedFrame()
        => curSpeedFrame = 0;

    public void CreateRenderer() // TBR: It should be in the constructor but DecoderContext will not work with null VideoDecoder for AudioOnly
    {
        if (Renderer == null)
            Renderer = new Renderer(this, 0, UniqueId);
        else if (Renderer.Disposed)
            Renderer.Initialize();
    }
    public void DestroyRenderer() => Renderer?.Dispose();
    public void CreateSwapChain(nint handle)
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
    const AVHWDeviceType    HW_DEVICE = AVHWDeviceType.D3d11va;

    internal ID3D11Texture2D
                            textureFFmpeg;
    AVCodecContext_get_format
                            getHWformat;
    AVBufferRef*            hwframes;
    AVBufferRef*            hw_device_ctx;

    internal static bool CheckCodecSupport(AVCodec* codec)
    {
        for (int i = 0; ; i++)
        {
            var config = avcodec_get_hw_config(codec, i);
            if (config == null) break;
            if ((config->methods & AVCodecHwConfigMethod.HwDeviceCtx) == 0 || config->pix_fmt == AVPixelFormat.None) continue;

            if (config->device_type == HW_DEVICE && config->pix_fmt == PIX_FMT_HWACCEL) return true;
        }

        return false;
    }
    internal int InitVA()
    {
        int ret;
        AVHWDeviceContext*      device_ctx;
        AVD3D11VADeviceContext* d3d11va_device_ctx;

        if (Renderer.Device == null || hw_device_ctx != null) return -1;

        hw_device_ctx       = av_hwdevice_ctx_alloc(HW_DEVICE);

        device_ctx          = (AVHWDeviceContext*) hw_device_ctx->data;
        d3d11va_device_ctx  = (AVD3D11VADeviceContext*) device_ctx->hwctx;
        d3d11va_device_ctx->device
                            = (Flyleaf.FFmpeg.ID3D11Device*) Renderer.Device.NativePointer;

        ret                 = av_hwdevice_ctx_init(hw_device_ctx);
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
        if (CanDebug)
        {
            Log.Debug($"Codec profile '{VideoStream.Codec} {avcodec_profile_name(codecCtx->codec_id, codecCtx->profile)}'");

            if (CanTrace)
            {
                var save = pix_fmts;
                while (*pix_fmts != AVPixelFormat.None)
                {
                    Log.Trace($"{*pix_fmts}");
                    pix_fmts++;
                }
                pix_fmts = save;
            }
        }

        bool foundHWformat = false;

        while (*pix_fmts != AVPixelFormat.None)
        {
            if ((*pix_fmts) == PIX_FMT_HWACCEL)
            {
                foundHWformat = true;
                break;
            }

            pix_fmts++;
        }

        int ret = ShouldAllocateNew();

        if (foundHWformat && ret == 0)
        {
            if (codecCtx->hw_frames_ctx == null && hwframes != null)
                codecCtx->hw_frames_ctx = av_buffer_ref(hwframes);

            return PIX_FMT_HWACCEL;
        }

        lock (lockCodecCtx)
        {
            if (!foundHWformat || !VideoAccelerated || AllocateHWFrames() != 0)
            {
                if (CanWarn)
                    Log.Warn("HW format not found. Fallback to sw format");

                swFallback = true;
                return avcodec_default_get_format(avctx, pix_fmts);
            }

            if (CanDebug)
                Log.Debug("HW frame allocation completed");

            // TBR: Catch codec changed on live streams (check codec/profiles and check even on sw frames)
            if (ret == 2)
            {
                // NOTE: It seems that codecCtx changes but upcoming frame still has previous configuration (this will fire FillFromCodec twice, could cause issues?)
                filledFromCodec = false;
                codecChanged    = true;
                Log.Warn($"Codec changed {VideoStream.CodecID} {VideoStream.Width}x{VideoStream.Height} => {codecCtx->codec_id} {codecCtx->width}x{codecCtx->height}");
            }

            return PIX_FMT_HWACCEL;
        }
    }
    private int ShouldAllocateNew() // 0: No, 1: Yes, 2: Yes+Codec Changed
    {
        if (hwframes == null)
            return 1;

        AVHWFramesContext* t2 = (AVHWFramesContext*) hwframes->data;

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

        if (avcodec_get_hw_frames_parameters(codecCtx, codecCtx->hw_device_ctx, PIX_FMT_HWACCEL, &codecCtx->hw_frames_ctx) != 0)
            return -1;

        AVHWFramesContext* hw_frames_ctx = (AVHWFramesContext*)codecCtx->hw_frames_ctx->data;
        //hw_frames_ctx->initial_pool_size += Config.Decoder.MaxVideoFrames; // TBR: Texture 2D Array seems to have up limit to 128 (total=17+MaxVideoFrames)? (should use extra hw frames instead**)

        AVD3D11VAFramesContext *va_frames_ctx = (AVD3D11VAFramesContext *)hw_frames_ctx->hwctx;
        va_frames_ctx->BindFlags  |= (uint)BindFlags.Decoder | (uint)BindFlags.ShaderResource;

        hwframes = av_buffer_ref(codecCtx->hw_frames_ctx);

        int ret = av_hwframe_ctx_init(codecCtx->hw_frames_ctx);
        if (ret == 0)
        {
            lock (Renderer.lockDevice)
            {
                textureFFmpeg   = new ID3D11Texture2D((nint) va_frames_ctx->texture);
                ZeroCopy =
                    Config.Decoder.ZeroCopy  == FlyleafLib.ZeroCopy.Enabled ||
                    (Config.Decoder.ZeroCopy == FlyleafLib.ZeroCopy.Auto &&
                    codecCtx->width  == textureFFmpeg.Description.Width &&
                    codecCtx->height == textureFFmpeg.Description.Height);
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
            ZeroCopy = VideoAccelerated &&
                (Config.Decoder.ZeroCopy == FlyleafLib.ZeroCopy.Enabled ||
                (Config.Decoder.ZeroCopy == FlyleafLib.ZeroCopy.Auto &&
                codecCtx->width  == textureFFmpeg.Description.Width &&
                codecCtx->height == textureFFmpeg.Description.Height));
            if (save != ZeroCopy)
            {
                Renderer?.ConfigPlanes();
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

        if (!swFallback && Config.Video.VideoAcceleration && Renderer.Device.FeatureLevel >= Vortice.Direct3D.FeatureLevel.Level_10_0)
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

        // TBR: during swFallback (keyFrameRequiredPacket should not reset, currenlty saved in SWFallback)
        keyPacketRequired   = false; // allow no key packet after open (lot of videos missing this)
        ZeroCopy            = false;
        filledFromCodec     = false;

        lastFixedPts    = 0; // TBR: might need to set this to first known pts/dts

        if (VideoAccelerated)
        {
            codecCtx->thread_count      = 1;
            codecCtx->hwaccel_flags    |= HWAccelFlags.IgnoreLevel;
            if (Config.Decoder.AllowProfileMismatch)
                codecCtx->hwaccel_flags|= HWAccelFlags.AllowProfileMismatch;

            codecCtx->get_format        = getHWformat;
            codecCtx->extra_hw_frames   = Config.Decoder.MaxVideoFrames;
        }
        else
            codecCtx->thread_count = Math.Min(Config.Decoder.VideoThreads, codecCtx->codec_id == AVCodecID.Hevc ? 32 : 16);

        if (codecCtx->codec_descriptor != null)
            isIntraOnly = codecCtx->codec_descriptor->props.HasFlag(CodecPropFlags.IntraOnly);

        startPts = VideoStream.StartTimePts;

        return 0;
    }
    internal bool SetupSws()
    {
        Marshal.FreeHGlobal(swsBufferPtr);
        var fmt         = AVPixelFormat.Rgba;
        swsData         = new byte_ptrArray4();
        swsLineSize     = new int_array4();
        int outBufferSize
                        = av_image_get_buffer_size(fmt, codecCtx->width, codecCtx->height, 1);
        swsBufferPtr    = Marshal.AllocHGlobal(outBufferSize);
        _ = av_image_fill_arrays(ref swsData, ref swsLineSize, (byte*) swsBufferPtr, fmt, codecCtx->width, codecCtx->height, 1);
        swsCtx          = sws_getContext(codecCtx->coded_width, codecCtx->coded_height, codecCtx->pix_fmt, codecCtx->width, codecCtx->height, fmt, Config.Video.SwsHighQuality ? SCALING_HQ : SCALING_LQ, null, null, null);

        if (swsCtx == null)
        {
            Log.Error($"Failed to allocate SwsContext");
            return false;
        }

        return true;
    }
    internal void Flush()
    {
        lock (lockActions)
            lock (lockCodecCtx)
            {
                if (Disposed) return;

                if (Status == Status.Ended)
                    Status = Status.Stopped;
                else if (Status == Status.Draining)
                    Status = Status.Stopping;

                DisposeFrames();
                avcodec_flush_buffers(codecCtx);

                keyPacketRequired   = !isIntraOnly;
                StartTime           = AV_NOPTS_VALUE;
                curSpeedFrame       = 9999;
            }
    }

    protected override void RunInternal()
    {
        if (demuxer.IsReversePlayback)
        {
            RunInternalReverse();
            return;
        }

        int allowedErrors   = Config.Decoder.MaxErrors;
        int sleepMs         = Config.Decoder.MaxVideoFrames > 2 && Config.Player.MaxLatency == 0 ? 10 : 2;
        int ret;
        AVPacket *packet;

        do
        {
            // Wait until Queue not Full or Stopped
            if (Frames.Count >= Config.Decoder.MaxVideoFrames)
            {
                lock (lockStatus)
                    if (Status == Status.Running) Status = Status.QueueFull;

                while (Frames.Count >= Config.Decoder.MaxVideoFrames && Status == Status.QueueFull)
                    Thread.Sleep(sleepMs);

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
                            var drainPacket = av_packet_alloc();
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

                    Thread.Sleep(sleepMs);
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
                if (Status == Status.Stopped)
                    continue;

                packet = demuxer.VideoPackets.Dequeue();

                if (packet == null)
                    continue;

                if (isRecording)
                {
                    if (!recKeyPacketRequired && (packet->flags & PktFlags.Key) != 0)
                    {
                        recKeyPacketRequired = true;
                        StartRecordTime = (long)(packet->pts * VideoStream.Timebase) - demuxer.StartTime;
                    }

                    if (recKeyPacketRequired)
                        curRecorder.Write(av_packet_clone(packet));
                }

                if (keyPacketRequired)
                {
                    if (packet->flags.HasFlag(PktFlags.Key) || packet->pts == startPts)
                        keyPacketRequired = false;
                    else
                    {
                        if (CanWarn) Log.Warn("Ignoring non-key packet");
                        av_packet_unref(packet);
                        continue;
                    }
                }

                // TBR: AVERROR(EAGAIN) means avcodec_receive_frame but after resend the same packet
                ret = avcodec_send_packet(codecCtx, packet);

                if (swFallback) // Should use 'global' packet to reset it in get_format (same packet should use also from DecoderContext)
                {
                    SWFallback();
                    ret = avcodec_send_packet(codecCtx, packet);
                }

                if (ret != 0 && ret != AVERROR(EAGAIN))
                {
                    // TBR: Possible check for VA failed here (normally this will happen during get_format)
                    av_packet_free(&packet);

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

                    // GetFormat checks already for this but only for hardware accelerated (should also check for codec/fps* and possible reset sws if required)
                    // Might use AVERROR_INPUT_CHANGED to let ffmpeg check for those (requires a flag to be set*)
                    if ((frame->height != VideoStream.Height || frame->width != VideoStream.Width) && !codecChanged) // could be already changed on getformat
                    {
                        // THIS IS Wrong and can cause filledFromCodec all the time. comparing frame<->videostream dimensions but we update the videostream from codecparam dimensions (which we pass from codecCtx w/h)
                        // Related with display dimensions / coded dimensions / frame-crop dimensions (and apply_cropping) - it could happen when frame->crop... are not 0

                        // TBR: codecCtx w/h changes earlier than frame w/h (still receiving previous config frames?)* can cause issues?
                        codecChanged    = true;
                        filledFromCodec = false;
                        Log.Warn($"Codec changed {VideoStream.CodecID} {VideoStream.Width}x{VideoStream.Height} => {codecCtx->codec_id} {frame->width}x{frame->height}");
                    }

                    if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                        frame->pts = frame->best_effort_timestamp;

                    else if (frame->pts == AV_NOPTS_VALUE)
                    {
                        if (!VideoStream.FixTimestamps && VideoStream.Duration > TimeSpan.FromSeconds(1).Ticks)
                        {
                            // TBR: it is possible to have a single frame / image with no dts/pts which actually means pts = 0 ? (ticket_3449.264) - GenPts will not affect it
                            // TBR: first frame might no have dts/pts which probably means pts = 0 (and not start time!)
                            av_frame_unref(frame);
                            continue;
                        }

                        // Create timestamps for h264/hevc raw streams (Needs also to handle this with the remuxer / no recording currently supported!)
                        frame->pts = lastFixedPts + VideoStream.StartTimePts;
                        lastFixedPts += av_rescale_q(VideoStream.FrameDuration / 10, Engine.FFmpeg.AV_TIMEBASE_Q, VideoStream.AVStream->time_base);
                    }

                    if (StartTime == NoTs)
                        StartTime = (long)(frame->pts * VideoStream.Timebase) - demuxer.StartTime;

                    if (!filledFromCodec) // Ensures we have a proper frame before filling from codec
                    {
                        ret = FillFromCodec(frame);
                        if (ret == -1234)
                        {
                            Status = Status.Stopping;
                            break;
                        }
                    }

                    if (skipSpeedFrames > 1)
                    {
                        curSpeedFrame++;
                        if (curSpeedFrame < skipSpeedFrames)
                        {
                            av_frame_unref(frame);
                            continue;
                        }
                        curSpeedFrame = 0;
                    }

                    var mFrame = Renderer.FillPlanes(frame);
                    if (mFrame != null)
                        Frames.Enqueue(mFrame); // TBR: Does not respect Config.Decoder.MaxVideoFrames
                    else if (handleDeviceReset)
                    {
                        HandleDeviceReset();
                        break;
                    }

                    if (!Config.Video.PresentFlags.HasFlag(PresentFlags.DoNotWait) && Frames.Count > 2)
                        Thread.Sleep(10);
                }

                av_packet_free(&packet);
            }

        } while (Status == Status.Running);

        checkExtraFrames = true;

        if (isRecording) { StopRecording(); recCompleted(MediaType.Video); }

        if (Status == Status.Draining) Status = Status.Ended;
    }

    internal int FillFromCodec(AVFrame* frame)
    {
        lock (Renderer.lockDevice)
        {
            int ret = 0;

            filledFromCodec = true;
            curFixSeekDelta = 0;

            VideoStream.Refresh(this, frame);
            codecChanged    = false;
            startPts        = VideoStream.StartTimePts;
            skipSpeedFrames = speed * VideoStream.FPS / Config.Video.MaxOutputFps;
            CodecChanged?.Invoke(this);

            DisposeFrame(Renderer.LastFrame);
            if (VideoStream.PixelFormat == AVPixelFormat.None || !Renderer.ConfigPlanes(true))
            {
                Log.Error("[Pixel Format] Unknown");
                return -1234;
            }

            return ret;
        }
    }

    internal bool handleDeviceReset; // Let Renderer decide when we reset (within RunInternal)
    internal void HandleDeviceReset()
    {
        if (!handleDeviceReset)
            return;

        handleDeviceReset = false;
        DisposeInternal();
        if (codecCtx != null)
        {
            fixed (AVCodecContext** ptr = &codecCtx)
                avcodec_free_context(ptr);

            codecCtx = null;
        }
        Renderer.Flush();
        Open2(Stream, null, false);
        keyPacketRequired = !isIntraOnly;
    }

    internal string SWFallback()
    {
        lock (Renderer.lockDevice)
        {
            string ret;

            DisposeInternal();
            if (codecCtx != null)
                fixed (AVCodecContext** ptr = &codecCtx)
                    avcodec_free_context(ptr);

            codecCtx        = null;
            swFallback      = true;
            bool oldKeyFrameRequiredPacket
                            = keyPacketRequired;
            ret = Open2(Stream, null, false); // TBR:  Dispose() on failure could cause a deadlock
            keyPacketRequired
                            = oldKeyFrameRequiredPacket;
            swFallback      = false;
            filledFromCodec = false;

            return ret;
        }
    }

    private void RunInternalReverse()
    {
        // Bug with B-frames, we should not remove the ref packets (we miss frames each time we restart decoding the gop)

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
                    if (keyPacketRequired == true)
                    {
                        keyPacketRequired = false;
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
                            curReverseVideoPackets[curReversePacketPos - 1] = 0;
                            curReverseVideoPackets.RemoveAt(i);
                        }

                        avcodec_flush_buffers(codecCtx);
                        curReversePacketPos = 0;

                        for (int i = curReverseVideoFrames.Count - 1; i >= 0; i--)
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
                            curReverseVideoPackets[curReversePacketPos - 1] = 0;
                            var mFrame = Renderer.FillPlanes(frame);
                            if (mFrame != null)
                                curReverseVideoFrames.Add(mFrame);
                            else if (handleDeviceReset)
                            {
                                HandleDeviceReset();
                                continue;
                            }
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

                // Import Sleep required to prevent delay during Renderer.Present for waitable swap chains
                if (!Config.Video.PresentFlags.HasFlag(PresentFlags.DoNotWait) && Frames.Count > 2)
                    Thread.Sleep(10);

            } // while curReverseVideoPackets.Count > 0

        } while (Status == Status.Running);

        if (Status != Status.Pausing && Status != Status.Paused)
            curReversePacketPos = 0;
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

    // TBR (GetFrameNumberX): Still issues mainly with Prev, e.g. jumps from 279 to 281 frame | VFR / Timebase / FrameDuration / FPS inaccuracy
    // Should use just GetFramePrev/Next and work with pts (but we currenlty work with Player.CurTime)

    /// <summary>
    /// Gets the frame number of a VideoFrame timestamp
    /// </summary>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public int GetFrameNumber(long timestamp)
        => Math.Max(0, (int)((timestamp + 2_0000 - VideoStream.StartTime + demuxer.StartTime) / VideoStream.FrameDuration));

    /// <summary>
    /// Gets the frame number of an AVFrame timestamp
    /// </summary>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public int GetFrameNumber2(long timestamp)
        => Math.Max(0, (int)((timestamp + 2_0000 - VideoStream.StartTime) / VideoStream.FrameDuration));

    /// <summary>
    /// Gets the VideoFrame timestamp from the frame number
    /// </summary>
    /// <param name="frameNumber"></param>
    /// <returns></returns>
    public long GetFrameTimestamp(int frameNumber)
        => VideoStream.StartTime + (frameNumber * VideoStream.FrameDuration);

    /// <summary>
    /// Performs accurate seeking to the requested VideoFrame and returns it
    /// </summary>
    /// <param name="frameNumber">Zero based frame index</param>
    /// <returns>The requested VideoFrame or null on failure</returns>
    public VideoFrame GetFrame(int frameNumber, bool backwards = false)
    {
        frameNumber = Math.Max(0, frameNumber);
        long requiredTimestamp = GetFrameTimestamp(frameNumber);
        long curSeekMcs = requiredTimestamp / 10;
        int curFrameNumber;

        do
        {
            demuxer.Pause();
            Pause();
            demuxer.Interrupter.SeekRequest();
            int ret = av_seek_frame(demuxer.FormatContext, -1, curSeekMcs - curFixSeekDelta, SeekFlags.Frame | SeekFlags.Backward);

            if (ret < 0)
                ret = av_seek_frame(demuxer.FormatContext, -1, Math.Max((curSeekMcs - (long)TimeSpan.FromSeconds(1).TotalMicroseconds) - curFixSeekDelta, demuxer.StartTime / 10), SeekFlags.Frame);
            
            demuxer.DisposePackets();

            if (demuxer.Status == Status.Ended)
                demuxer.Status = Status.Stopped;

            if (ret < 0)
                return null;

            Flush();
            checkExtraFrames = false;

            if (DecodeFrameNext() != 0)
                return null;

            curFrameNumber = GetFrameNumber2((long)(frame->pts * VideoStream.Timebase));
            
            if (curFrameNumber > frameNumber)
            {
                curFixSeekDelta += FIX_SEEK_DELTA_MCS;
                continue;
            }

            do
            {
                if (curFrameNumber >= frameNumber ||
                    (backwards && curFrameNumber + 2 >= frameNumber && GetFrameNumber2((long)(frame->pts * VideoStream.Timebase) + VideoStream.FrameDuration + (VideoStream.FrameDuration / 2)) - curFrameNumber > 1))
                    // At least return a previous frame in case of Tb inaccuracy and don't stuck at the same frame
                {
                    if (backwards && curFrameNumber + 2 >= frameNumber && GetFrameNumber2((long)(frame->pts * VideoStream.Timebase) + VideoStream.FrameDuration + (VideoStream.FrameDuration / 2)) - curFrameNumber > 1)
                        Log.Debug("");

                    var mFrame = Renderer.FillPlanes(frame);
                    if (mFrame != null)
                        return mFrame;
                    else if (handleDeviceReset)
                    {
                        HandleDeviceReset();
                        continue;
                    }
                }

                av_frame_unref(frame);
                if (DecodeFrameNext() != 0)
                    break;

                curFrameNumber = GetFrameNumber2((long)(frame->pts * VideoStream.Timebase));

            } while (true);

            return null;
        } while (true);
    }

    /// <summary>
    /// Gets next VideoFrame (Decoder/Demuxer must not be running)
    /// </summary>
    /// <returns>The next VideoFrame</returns>
    public VideoFrame GetFrameNext()
    {
        if (DecodeFrameNext() == 0)
        {
            var mFrame = Renderer.FillPlanes(frame);
            if (mFrame != null)
                return mFrame;
            else if (handleDeviceReset)
                HandleDeviceReset();
        }

        return null;
    }

    /// <summary>
    /// Pushes the decoder to the next available VideoFrame (Decoder/Demuxer must not be running)
    /// </summary>
    /// <returns></returns>
    public int DecodeFrameNext()
    {
        int ret;
        int allowedErrors = Config.Decoder.MaxErrors;

        if (checkExtraFrames)
        {
            if (Status == Status.Ended)
                return AVERROR_EOF;

            if (DecodeFrameNextInternal() == 0)
                return 0;

            if (Demuxer.Status == Status.Ended && demuxer.VideoPackets.Count == 0 && Frames.IsEmpty)
            {
                Stop();
                Status = Status.Ended;
                return AVERROR_EOF;
            }

            checkExtraFrames = false;
        }

        while (true)
        {
            ret = demuxer.GetNextVideoPacket();
            if (ret != 0)
            {
                if (demuxer.Status != Status.Ended)
                    return ret;

                // Drain
                ret = avcodec_send_packet(codecCtx, demuxer.packet);
                av_packet_unref(demuxer.packet);

                if (ret != 0)
                    return AVERROR_EOF;

                checkExtraFrames = true;
                return DecodeFrameNext();
            }

            if (keyPacketRequired)
            {
                if (demuxer.packet->flags.HasFlag(PktFlags.Key) || demuxer.packet->pts == startPts)
                    keyPacketRequired = false;
                else
                {
                    if (CanWarn) Log.Warn("Ignoring non-key packet");
                    av_packet_unref(demuxer.packet);
                    continue;
                }
            }

            ret = avcodec_send_packet(codecCtx, demuxer.packet);

            if (swFallback) // Should use 'global' packet to reset it in get_format (same packet should use also from DecoderContext)
            {
                SWFallback();
                ret = avcodec_send_packet(codecCtx, demuxer.packet);
            }

            av_packet_unref(demuxer.packet);

            if (ret != 0 && ret != AVERROR(EAGAIN))
            {
                if (CanWarn) Log.Warn($"{FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                if (allowedErrors-- < 1)
                    { Log.Error("Too many errors!"); return ret; }

                continue;
            }

            if (DecodeFrameNextInternal() == 0)
            {
                checkExtraFrames = true;
                return 0;
            }
        }

    }
    private int DecodeFrameNextInternal()
    {
        int ret = avcodec_receive_frame(codecCtx, frame);
        if (ret != 0) { av_frame_unref(frame); return ret; }

        if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
            frame->pts = frame->best_effort_timestamp;

        else if (frame->pts == AV_NOPTS_VALUE)
        {
            if (!VideoStream.FixTimestamps)
            {
                av_frame_unref(frame);

                return DecodeFrameNextInternal();
            }

            frame->pts = lastFixedPts + VideoStream.StartTimePts;
            lastFixedPts += av_rescale_q(VideoStream.FrameDuration / 10, Engine.FFmpeg.AV_TIMEBASE_Q, VideoStream.AVStream->time_base);
        }

        if (StartTime == NoTs)
            StartTime = (long)(frame->pts * VideoStream.Timebase) - demuxer.StartTime;

        if (!filledFromCodec) // Ensures we have a proper frame before filling from codec
        {
            ret = FillFromCodec(frame);
            if (ret == -1234)
                return -1;
        }

        return 0;
    }

    #region Dispose
    public void DisposeFrames()
    {
        while (!Frames.IsEmpty)
        {
            Frames.TryDequeue(out var frame);
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
                if (t2[i] == 0) continue;
                AVPacket* packet = (AVPacket*)t2[i];
                av_packet_free(&packet);
            }
        }

        for (int i = 0; i<curReverseVideoPackets.Count; i++)
        {
            if (curReverseVideoPackets[i] == 0) continue;
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
        {
            for (int i=0; i<frame.textures.Length; i++)
                frame.textures[i].Dispose();

            frame.textures = null;
        }

        if (frame.srvs != null)
        {
            for (int i=0; i<frame.srvs.Length; i++)
                frame.srvs[i].Dispose();

            frame.srvs = null;
        }

        if (frame.avFrame != null)
            fixed(AVFrame** ptr = &frame.avFrame)
            av_frame_free(ptr);
    }
    protected override void DisposeInternal()
    {
        lock (lockCodecCtx)
        {
            DisposeFrames();

            if (hwframes != null)
                fixed(AVBufferRef** ptr = &hwframes)
                    av_buffer_unref(ptr);

            if (hw_device_ctx != null)
                fixed(AVBufferRef** ptr = &hw_device_ctx)
                    av_buffer_unref(ptr);

            if (swsCtx != null)
                sws_freeContext(swsCtx);

            hwframes    = null;
            hw_device_ctx
                        = null;
            swsCtx      = null;
            StartTime   = AV_NOPTS_VALUE;
            swFallback  = false;
            curSpeedFrame= 9999;
        }
    }
    #endregion

    #region Recording
    internal Action<MediaType> recCompleted;
    Remuxer curRecorder;
    bool recKeyPacketRequired;
    internal bool isRecording;

    internal void StartRecording(Remuxer remuxer)
    {
        if (Disposed || isRecording) return;

        StartRecordTime     = AV_NOPTS_VALUE;
        curRecorder         = remuxer;
        recKeyPacketRequired= false;
        isRecording         = true;
    }
    internal void StopRecording() => isRecording = false;
    #endregion
}
