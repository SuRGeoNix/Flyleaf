/*
 * Codec FFmpeg
 * 
 * Based on FFmpeg.AutoGen C# .NET bindings by Ruslan Balanukhin [https://github.com/Ruslan-B/FFmpeg.AutoGen]
 * 
 */
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Security;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;

using FFmpeg.AutoGen;

using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D11;

using static FFmpeg.AutoGen.ffmpeg;

using Device    = SharpDX.Direct3D11.Device;
using Resource  = SharpDX.Direct3D11.Resource;

namespace SuRGeoNix.Flyleaf
{
    unsafe public class MediaDecoder
    {
        #region Declaration

        // Audio Output Parameters [ BITS | CHANNELS | RATE ]
        AVSampleFormat _SAMPLE_FORMAT   = AVSampleFormat.AV_SAMPLE_FMT_S16; int _CHANNELS = 2; 
        public int _RATE { get; private set; } // Will be set from Input Format

        // Video Output Parameters
        public Device           d3d11Device;
        Texture2DDescription    textDescNV12;
        Texture2DDescription    textDescYUV;
        Texture2DDescription    textDescRGB;
        Texture2D               textureFFmpeg;
        Texture2D               textureNV12;

        AVPixelFormat _PIXEL_FORMAT = AVPixelFormat.AV_PIX_FMT_RGBA;
        int _SCALING_HQ             = SWS_ACCURATE_RND | SWS_BITEXACT | SWS_LANCZOS | SWS_FULL_CHR_H_INT | SWS_FULL_CHR_H_INP;
        int _SCALING_LQ             = SWS_BICUBIC;
        int vSwsOptFlags;
        
        // Video Output Buffer
        IntPtr                  outBufferPtr; 
        int                     outBufferSize;
        byte_ptrArray4          outData;
        int_array4              outLineSize;

        // Contexts             [Audio]     [Video]     [Subs]      [Audio/Video]       [Subs/Video]
        AVFormatContext*        aFmtCtx,    vFmtCtx,    sFmtCtx;
        AVIOContext*            aIOCtx,     vIOCtx;//,     sIOCtx;
        AVStream*               aStream,    vStream,    sStream;
        AVCodecContext*         aCodecCtx,  vCodecCtx,  sCodecCtx;
        AVCodec*                aCodec,     vCodec,     sCodec;
        SwrContext*             swrCtx;
        SwsContext*                         swsCtx;   //sSwsCtx;

        Thread                  aDecoder,   vDecoder,   sDecoder;

        // Status
        public enum Status
        {
            READY,
            RUNNING,
            SEEKING,
            STOPPING,
            STOPPED,
            OPENING
        }
        public Status           aStatus { get; set; }
        public Status           vStatus { get; set; }
        public Status           sStatus { get; set; }
        public Status           status  { get; set; }

        // Frames Callback | Main
        Action<MediaFrame, AVMediaType>     SendFrame;

        // AVIO Callbacks  | DecodeSilence2
        public Action<AVMediaType>          BufferingDone;
        public Action                       BufferingAudioDone;
        public Action                       BufferingSubsDone;

        private long                        aCurPos, vCurPos;//, sCurPos;
        List<object>                        gcPrevent       = new List<object>();
        private const int                   IOBufferSize    = 0x40000;

        public bool isSubsExternal { get; private set; }
        bool aFinish, vFinish, sFinish;

        // HW Acceleration
        const int                           AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        public Device                       avD3D11Device;
        AVBufferRef*                        hw_device_ctx;
        List<AVHWDeviceType>                hwDevices;
        List<HWDeviceSupported>             hwDevicesSupported;
        struct HWDeviceSupported
        {
            public AVHWDeviceType       type;
            public List<AVPixelFormat>  pixFmts;
        }
        #endregion

        #region Properties

        public class MediaFrame
        {
            public byte[]   data;

            public Texture2D   texture;
            public Texture2D   textureRGB;
            public Texture2D   textureY;
            public Texture2D   textureU;
            public Texture2D   textureV;

            public long     timestamp;
            public long     pts;

            public int      duration;
            public string   text;
        }
        public struct AudioStreamInfo
        {
            public short    streamIndex;
            public double   timebase;
            public double   timebaseLowTicks;
            public long     startTimeTicks;
            public long     durationTicks;
            public int      frameSize;
        }
        public struct VideoStreamInfo
        {
            public short    streamIndex;
            public double   timebase;
            public double   timebaseLowTicks;
            public long     startTimeTicks;
            public long     durationTicks;
            public long     frameAvgTicks;
            public int      height;
            public int      width;
            public double   fps;
        }

        public AudioStreamInfo  aStreamInfo;
        public VideoStreamInfo  vStreamInfo;
        double sTimbebaseLowTicks;

        public bool isVideoFinish   { get {  return vFinish; } }
        public bool isAudioFinish   { get {  return aFinish; } }
        public bool isSubsFinish    { get {  return sFinish; } }
        public bool isReady         { get; private set; }
        public bool isRunning       { get { return (status  == Status.RUNNING); } }
        public bool isSeeking       { get { return (status  == Status.SEEKING); } }
        public bool isStopped       { get { return (status  == Status.STOPPED); } }
        public bool isAudioRunning  { get { return (aStatus == Status.RUNNING); } }
        public bool isVideoRunning  { get { return (vStatus == Status.RUNNING); } }
        public bool isSubsRunning   { get { return (sStatus == Status.RUNNING); } }
        public bool hasAudio        { get; private set; }
        public bool hasVideo        { get; private set; }
        public bool hasSubs         { get; private set; }
        public bool doAudio         { get; set; } = true;   // Requires-reopen if was disabled
        public bool doSubs          { get; set; } = true;   // Requires-reopen if was disabled
        public bool HighQuality     { get; set; }
        public bool HWAccel         { get; set; } = true;   // Requires re-open
        public bool hwAccelSuccess  { get; private set; }
        public int  Threads         { get; set; } = 2;      // Requires re-open
        public int  verbosity       { get; set; }
        #endregion

        #region Initialization
        public MediaDecoder() { }
        public MediaDecoder (Action<MediaFrame, AVMediaType> RecvFrameCallback = null, int verbosity = 0) { Init(RecvFrameCallback, verbosity); }
        public void Init    (Action<MediaFrame, AVMediaType> RecvFrameCallback = null, int verbosity = 0)
        {
            RegisterFFmpegBinaries();
            
            if      (verbosity == 1) { av_log_set_level(AV_LOG_ERROR);        av_log_set_callback(ffmpegLogCallback); } 
            else if (verbosity  > 1) { av_log_set_level(AV_LOG_MAX_OFFSET);   av_log_set_callback(ffmpegLogCallback); }
            this.verbosity      = verbosity;
            SendFrame           = RecvFrameCallback;

            hwDevices           = GetHWDevices();
            hwDevicesSupported  = new List<HWDeviceSupported>();

            Initialize();
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void Initialize()
        {
            if (aDecoder != null && aDecoder.IsAlive) aDecoder.Abort();
            if (vDecoder != null && vDecoder.IsAlive) vDecoder.Abort();
            if (sDecoder != null && sDecoder.IsAlive) sDecoder.Abort();

            status      = Status.STOPPED; 
            aStatus     = Status.STOPPED; 
            vStatus     = Status.STOPPED; 
            sStatus     = Status.STOPPED;

            isReady     = false;
            hasAudio    = false;
            hasVideo    = false;
            hasSubs     = false;
            isSubsExternal = false;

            vStreamInfo = new VideoStreamInfo();
            aStreamInfo = new AudioStreamInfo();

            AVFormatContext* fmtCtxPtr;
            try
            {
                if (aFmtCtx != null) { fmtCtxPtr = aFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(aFmtCtx); }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }
                
            try
            {
                if (vFmtCtx != null) { avcodec_close(vCodecCtx); sws_freeContext(swsCtx); fmtCtxPtr = vFmtCtx; avformat_close_input(&fmtCtxPtr); /*av_freep(vFmtCtx);*/ }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }
                
            try
            {
                if (sFmtCtx != null) { fmtCtxPtr = sFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(sFmtCtx); }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }

            aFmtCtx = null; vFmtCtx = null; sFmtCtx = null; swsCtx = null; 
        }
        #endregion

        #region Setup Codecs/Streams
        private int SetupStream(AVMediaType mType)
        {
            int streamIndex     = -1;

            if      (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)   { streamIndex = av_find_best_stream(aFmtCtx, mType, -1, -1, null, 0); }
            else if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)   { streamIndex = av_find_best_stream(vFmtCtx, mType, -1, -1, null, 0); }
            else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE){ streamIndex = av_find_best_stream(sFmtCtx, mType, -1, -1, null, 0); }

            if (streamIndex < 0) return streamIndex;

            if      (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)   { aStream = aFmtCtx->streams[streamIndex]; hasAudio = true; }
            else if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)   { vStream = vFmtCtx->streams[streamIndex]; hasVideo = true; }
            else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE){ sStream = sFmtCtx->streams[streamIndex]; hasSubs  = true; }
            else return -1;

            return 0;
        }
        private int SetupCodec(AVMediaType mType)
        {
            int ret = 0;

            if      (mType == AVMediaType.AVMEDIA_TYPE_AUDIO && hasAudio)
            {
                aCodecCtx   = aStream->codec;
                aCodec      = avcodec_find_decoder(aStream->codec->codec_id);
                ret         = avcodec_open2(aCodecCtx, aCodec, null); 
            }
            else if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO && hasVideo)
            {
                vCodecCtx   = vStream->codec;

                // Threading
                vCodecCtx->thread_count = Threads;
                vCodecCtx->thread_type  = FF_THREAD_FRAME;
                //vCodecCtx->active_thread_type = FF_THREAD_FRAME;
                //vCodecCtx->thread_safe_callbacks = 1;

                vCodec      = avcodec_find_decoder(vStream->codec->codec_id);
                SetupHQAndHWAcceleration();
                ret = avcodec_open2(vCodecCtx, vCodec, null); 
            }
            else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE && hasSubs)
            {
                sCodecCtx   = sStream->codec;
                sCodec      = avcodec_find_decoder(sStream->codec->codec_id);
                ret         = avcodec_open2(sCodecCtx, sCodec, null); }
            else return -1;

            if (ret != 0) Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));

            return ret;
        }
        private int SetupHQAndHWAcceleration()
        {
            // For SWS
            vSwsOptFlags    = HighQuality ? _SCALING_HQ : _SCALING_LQ;

            hwAccelSuccess  = false;

            if (HWAccel && hwDevices.Count > 0)
            {
                hwDevicesSupported = GetHWDevicesSupported();
                foreach (AVHWDeviceType hwDevice in hwDevices)
                {
                    if (hwDevice != AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA) continue;

                    // GPU device is in Codec's supported list
                    bool found = false;
                    foreach (HWDeviceSupported hwDeviceSupported in hwDevicesSupported)
                        if (hwDeviceSupported.type == hwDevice) { found = true; break; }
                    if (!found) continue;
                    found = false;

                    // HW Deivce Context (Temporary)
                    AVBufferRef* hw_device_ctx2 = null;
                    if ( hw_device_ctx == null )
                        { if (av_hwdevice_ctx_create(&hw_device_ctx2, hwDevice, "auto", null, 0) != 0) continue; }
                    else
                        hw_device_ctx2 = hw_device_ctx;

                    // Available Pixel Format's are supported from SWS (Currently using only NV12 for RGBA convert later with sws_scale)
                    AVHWFramesConstraints* hw_frames_const = av_hwdevice_get_hwframe_constraints(hw_device_ctx2, null);
                    if (hw_frames_const == null) { av_buffer_unref(&hw_device_ctx2); continue; }
                    for (AVPixelFormat* p = hw_frames_const->valid_sw_formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
                        if (sws_isSupportedInput(*p) > 0)
                            if (*p == AVPixelFormat.AV_PIX_FMT_NV12) { found = true; break; }
                    if (!found) { av_buffer_unref(&hw_device_ctx2); continue; }

                    if ( hw_device_ctx == null ) 
                    {
                        hw_device_ctx = hw_device_ctx2;
                        AVHWDeviceContext* hw_device_ctx3 = (AVHWDeviceContext*)hw_device_ctx->data;
                        AVD3D11VADeviceContext* hw_d3d11_dev_ctx = (AVD3D11VADeviceContext*)hw_device_ctx3->hwctx;
                        avD3D11Device = Device.FromPointer<Device>((IntPtr) hw_d3d11_dev_ctx->device);
                    }

                    // Hardware Textures
                    textDescNV12 = new Texture2DDescription()
                    {
	                    Usage               = ResourceUsage.Default,
	                    //Format              = Format.NV12 | Format.P010

	                    Width               = vCodecCtx->width,
	                    Height              = vCodecCtx->height,

	                    BindFlags           = BindFlags.Decoder,
	                    CpuAccessFlags      = CpuAccessFlags.None,
	                    OptionFlags         = ResourceOptionFlags.Shared,

	                    SampleDescription   = new SampleDescription(1, 0),
	                    ArraySize           = 1,
	                    MipLevels           = 1
                    };


                    vCodecCtx->hw_device_ctx = av_buffer_ref(hw_device_ctx);
                    hwAccelSuccess = true;
                    Log("[HWACCEL] Enabled! Device -> " + hwDevice + ", Codec -> " + Marshal.PtrToStringAnsi((IntPtr)vCodec->name));
                    
                    break;
                }
            }

            // Software Textures
            textDescYUV = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Immutable,
                Format              = Format.R8_UNorm,

                Width               = vCodecCtx->width,
                Height              = vCodecCtx->height,

                BindFlags           = BindFlags.ShaderResource,
                CpuAccessFlags      = CpuAccessFlags.None,
                OptionFlags         = ResourceOptionFlags.None,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };

            textDescRGB = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Immutable,
                Format              = Format.R8G8B8A8_UNorm,

                Width               = vCodecCtx->width,
                Height              = vCodecCtx->height,

                BindFlags           = BindFlags.ShaderResource,
                CpuAccessFlags      = CpuAccessFlags.None,
                OptionFlags         = ResourceOptionFlags.None,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };
            
            return 0;
        }
        private int SetupAudio()
        {
            int ret = 0;

            aStreamInfo.timebase                    = av_q2d(aStream->time_base);
            aStreamInfo.timebaseLowTicks            = av_q2d(aStream->time_base) * 10000 * 1000;
            aStreamInfo.startTimeTicks              = (aStreamInfo.startTimeTicks != AV_NOPTS_VALUE) ? (long)(aStream->start_time * aStreamInfo.timebaseLowTicks) : 0;
            aStreamInfo.durationTicks               = (aStream->duration > 0) ? (long)(aStream->duration * aStreamInfo.timebaseLowTicks) : aFmtCtx->duration * 10;
            aStreamInfo.frameSize                   = aCodecCtx->frame_size;
            _RATE                                   = aCodecCtx->sample_rate;
            swrCtx = swr_alloc();

            av_opt_set_int(swrCtx,           "in_channel_layout",   (int)aCodecCtx->channel_layout, 0);
            av_opt_set_int(swrCtx,           "in_channel_count",         aCodecCtx->channels, 0);
            av_opt_set_int(swrCtx,           "in_sample_rate",           aCodecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(swrCtx,    "in_sample_fmt",            aCodecCtx->sample_fmt, 0);

            av_opt_set_int(swrCtx,           "out_channel_layout",       av_get_default_channel_layout(_CHANNELS), 0);
            av_opt_set_int(swrCtx,           "out_channel_count",        _CHANNELS, 0);
            av_opt_set_int(swrCtx,           "out_sample_rate",          _RATE, 0);
            av_opt_set_sample_fmt(swrCtx,    "out_sample_fmt",           _SAMPLE_FORMAT, 0);

            ret = swr_init(swrCtx);
            
            if (ret != 0) Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));

            return ret;
        }
        private int SetupVideo()
        {
            int ret = 0;

            // Store Stream Info
            vStreamInfo.timebase            = av_q2d(vStream->time_base) ;
            vStreamInfo.timebaseLowTicks    = av_q2d(vStream->time_base) * 10000 * 1000;
            vStreamInfo.startTimeTicks      = (vStreamInfo.startTimeTicks != AV_NOPTS_VALUE) ? (long)(vStream->start_time * vStreamInfo.timebaseLowTicks) : 0;
            vStreamInfo.durationTicks       = (vStream->duration > 0) ? (long)(vStream->duration * vStreamInfo.timebaseLowTicks) : vFmtCtx->duration * 10;
            vStreamInfo.fps                 = av_q2d(vStream->avg_frame_rate);
            vStreamInfo.frameAvgTicks       = (long)((1 / vStreamInfo.fps) * 1000 * 10000);
            vStreamInfo.height              = vCodecCtx->height;
            vStreamInfo.width               = vCodecCtx->width;

            return ret;
        }
        #endregion

        #region Decoding
        private int Decode(AVMediaType mType)
        {
            Log("[START 0] " + status + " " + mType);

            int ret = 0;

            AVPacket* avpacket  = av_packet_alloc();
            AVFrame*  avframe   = av_frame_alloc();
            //AVFrame*  avframeV;
            av_init_packet(avpacket);
            
            try
            {
                if (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    aFinish = false;

                    while (isRunning && aStatus == Status.RUNNING   && (ret = av_read_frame(aFmtCtx, avpacket)) == 0)
                    {
                        if (avpacket->stream_index == aStream->index)   ret = DecodeFrame(avframe, aCodecCtx, avpacket, false);
                        av_packet_unref(avpacket);
                    }
                    av_packet_unref(avpacket);

                    if (ret == AVERROR_EOF) aFinish = true;
                    if (ret < 0 && ret != AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    vFinish = false;

                    while (isRunning && vStatus == Status.RUNNING   && (ret = av_read_frame(vFmtCtx, avpacket)) == 0)
                    {
                        //avframeV   = av_frame_alloc();
                        if (avpacket->stream_index == vStream->index)   ret = DecodeFrame(avframe, vCodecCtx, avpacket, false);
                        av_packet_unref(avpacket);
                    }
                    av_packet_unref(avpacket);

                    if (ret < 0 && ret != AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                    // Drain Mode
                    if (ret == AVERROR_EOF)
                    { 
                        vFinish = true;
                        ret = DecodeFrame(avframe, vCodecCtx, null, true); 
                        if (ret != 0) Log(vCodecCtx->codec_type.ToString() + " - Warning[" + ret.ToString("D4") + "], Msg: Failed to decode frame, PTS: " + avpacket->pts);
                    }
                } 
                else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                {
                    sFinish = false;

                    while (isRunning && sStatus == Status.RUNNING   && (ret = av_read_frame(sFmtCtx, avpacket)) == 0)
                    {
                        if (avpacket->stream_index == sStream->index)   ret = DecodeFrameSubs(sCodecCtx, avpacket);
                        av_packet_unref(avpacket);
                    }
                    av_packet_unref(avpacket);

                    if (ret == AVERROR_EOF) sFinish = true;
                    if (ret < 0 && ret != AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                }

            } catch (ThreadAbortException)  { Log("[INFO  0] Killed " + mType);
            } catch (Exception e)           { Log("[ERROR 0] " + mType + " " + e.Message + " " + e.StackTrace); 
            } 
            finally 
            { 
                av_packet_unref(avpacket);
                av_frame_free  (&avframe);
                //av_frame_free  (&avframeV);

                Log("[END   0] " + status + " " + mType);
            }

            return 0;
        }
        private int DecodeFrame(AVFrame* avframe, AVCodecContext* codecCtx, AVPacket* avpacket, bool drainMode)
        {
            int ret = 0;

            ret = avcodec_send_packet(codecCtx, avpacket);
            if (ret != 0 ) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

            do
            {
                ret = avcodec_receive_frame(codecCtx, avframe);

                //if (avpacket == null && ret == 0) Log(codecCtx->codec_type.ToString() + " - Warning[" + ret.ToString("D4") + "], Msg: drain packet, PTS: <null>");
                if (avpacket == null) if (ret != 0) return ret; else Log(codecCtx->codec_type.ToString() + " - Warning[" + ret.ToString("D4") + "], Msg: drain packet, PTS: <null>");
                if (ret == AVERROR_EOF || ret == AVERROR(EAGAIN)) return 0;
                if (ret < 0) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", PTS: " + avpacket->pts); return ret; }
                if (avframe->repeat_pict == 1) Log("Warning, Repeated Frame -> " + avframe->best_effort_timestamp.ToString());

                if (codecCtx->codec_type ==         AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    ret = ProcessAudioFrame(avframe);
                    if (ret != 0) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: Failed to process AUDIO frame, PTS: " + avpacket->pts); return -1; }
                }
                else if (codecCtx->codec_type ==    AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    ret = ProcessVideoFrame(avframe);
                    if (ret != 0) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: Failed to process VIDEO frame, PTS: " + avpacket->pts); return -1; }
                }
            } while (isRunning && drainMode);

            return 0;
        }
        private int DecodeFrameSubs(AVCodecContext* codeCtx, AVPacket* avpacket)
        {
            int ret = 0;
            int gotFrame = 0;
            AVSubtitle sub = new AVSubtitle();
            
            ret = avcodec_decode_subtitle2(codeCtx, &sub, &gotFrame, avpacket);
            if (ret < 0)  { Log(codeCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) ); return ret; }
            if (gotFrame < 1 || sub.num_rects < 1 ) return -1;

            ret = ProcessSubsFrame(avpacket, &sub);
            avsubtitle_free(&sub);
            if (ret != 0) { Log(codeCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: Failed to process SUBS frame, PTS: " + avpacket->pts); return -1; }

            return 0;
        }
        private int DecodeFrameSilent(AVFrame* avframe, AVCodecContext* codecCtx, AVPacket* avpacket)
        {
            int ret = 0;

            ret = avcodec_send_packet(codecCtx, avpacket);
            if (ret != 0) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", PTS: " + avpacket->pts); return ret; }

            ret = avcodec_receive_frame(codecCtx, avframe);
            
            if (avpacket == null && ret == 0) Log(codecCtx->codec_type.ToString() + " - Warning[" + ret.ToString("D4") + "], Msg: drain packet, PTS: <null>");
            if (ret == AVERROR_EOF || ret == AVERROR(EAGAIN)) { return 0; }
            if (ret < 0) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", PTS: " + avpacket->pts); return ret; }
            if (avframe->repeat_pict == 1) Log("Warning, Repeated Frame -> " + avframe->best_effort_timestamp.ToString());

            return 0;
        }
        #endregion

        #region Process AVS
        private int ProcessAudioFrame(AVFrame* frame)
        {
            int ret = 0;

            try
            {
                var bufferSize  = av_samples_get_buffer_size(null, _CHANNELS, frame->nb_samples, _SAMPLE_FORMAT, 1);
                byte[] buffer   = new byte[bufferSize];

                fixed (byte** buffers = new byte*[8])
                {
                    fixed (byte* bufferPtr = &buffer[0])
                    {
                        // Convert
                        buffers[0]          = bufferPtr;
                        swr_convert(swrCtx, buffers, frame->nb_samples, (byte**)&frame->data, frame->nb_samples);

                        // Send Frame
                        if (frame->nb_samples > 0)
                        {
                            MediaFrame mFrame   = new MediaFrame();
                            mFrame.data         = new byte[bufferSize]; System.Buffer.BlockCopy(buffer, 0, mFrame.data, 0, bufferSize);
                            mFrame.pts          = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                            mFrame.timestamp    = (long)(mFrame.pts * aStreamInfo.timebaseLowTicks);
                            if (mFrame.pts == AV_NOPTS_VALUE) return -1;
                            SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_AUDIO);
                        }
                    }
                }
            } catch (ThreadAbortException) { 
            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessAudioFrame(), Msg: " + e.StackTrace); }

            return ret;
        }
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private int ProcessVideoFrame(AVFrame* frame)
        {
            int ret = 0;

            try
            {
                MediaFrame mFrame   = new MediaFrame();
                mFrame.pts          = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                mFrame.timestamp    = (long)(mFrame.pts * vStreamInfo.timebaseLowTicks);
                if (mFrame.pts == AV_NOPTS_VALUE) return -1;

                // Hardware Frame (NV12)        | AVDevice NV12 -> Device NV12 -> VideoProcessBlt RGBA
                if (hwAccelSuccess)
                {    
                    SharpDX.DXGI.Resource sharedResource = null;

                    textureFFmpeg       = new Texture2D((IntPtr) frame->data.ToArray()[0]);
                    textDescNV12.Format = textureFFmpeg.Description.Format;
                    textureNV12         = new Texture2D(avD3D11Device, textDescNV12);

                    avD3D11Device.ImmediateContext.CopySubresourceRegion(textureFFmpeg, (int) frame->data.ToArray()[1], null, textureNV12, 0);
                    avD3D11Device.ImmediateContext.Flush();
                    Thread.Sleep(5); // Temporary to ensure Flushing is done (maybe GetData/CreateQuery)

                    sharedResource      = textureNV12.QueryInterface<SharpDX.DXGI.Resource>();
                    Texture2D texture   = d3d11Device.OpenSharedResource<Texture2D>(sharedResource.SharedHandle);

                    mFrame.texture      = texture;
                    SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_VIDEO);

                    Utilities.Dispose(ref sharedResource);
                    Utilities.Dispose(ref textureNV12);
                }

                // Software Frame (YUV420P)     | YUV byte* -> Device YUV (srv R8 * 3) -> PixelShader YUV->RGBA
                else if (frame->format == (int)AVPixelFormat.AV_PIX_FMT_YUV420P || false)
                {
                    textDescYUV.Width   = vCodecCtx->width;
                    textDescYUV.Height  = vCodecCtx->height;

                    DataStream dsY = new DataStream(frame->linesize.ToArray()[0] * vCodecCtx->height, true, true);
                    DataStream dsU = new DataStream(frame->linesize.ToArray()[1] * vCodecCtx->height / 2, true, true);
                    DataStream dsV = new DataStream(frame->linesize.ToArray()[2] * vCodecCtx->height / 2, true, true);

                    DataBox dbY = new DataBox();
                    DataBox dbU = new DataBox();
                    DataBox dbV = new DataBox();

                    dbY.DataPointer = dsY.DataPointer;
                    dbU.DataPointer = dsU.DataPointer;
                    dbV.DataPointer = dsV.DataPointer;

                    dbY.RowPitch = frame->linesize.ToArray()[0];
                    dbU.RowPitch = frame->linesize.ToArray()[1];
                    dbV.RowPitch = frame->linesize.ToArray()[2];

                    dsY.WriteRange((IntPtr)frame->data.ToArray()[0], dsY.Length);
                    dsU.WriteRange((IntPtr)frame->data.ToArray()[1], dsU.Length);
                    dsV.WriteRange((IntPtr)frame->data.ToArray()[2], dsV.Length);

                    mFrame.textureY = new Texture2D(d3d11Device, textDescYUV, new DataBox[] { dbY });
                    textDescYUV.Width = vCodecCtx->width / 2;
                    textDescYUV.Height = vCodecCtx->height / 2;

                    mFrame.textureU = new Texture2D(d3d11Device, textDescYUV, new DataBox[] { dbU });
                    mFrame.textureV = new Texture2D(d3d11Device, textDescYUV, new DataBox[] { dbV });

                    Utilities.Dispose(ref dsY);
                    Utilities.Dispose(ref dsU);
                    Utilities.Dispose(ref dsV);

                    SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_VIDEO);
                }

                // Software Frame (OTHER/sws_scale) | X byte* -> Sws_Scale RGBA -> Device RGA
                else if (!hwAccelSuccess) 
                {
                    if (swsCtx == null)
                    {
                        outData                         = new byte_ptrArray4();
                        outLineSize                     = new int_array4();
                        outBufferSize                   = av_image_get_buffer_size(_PIXEL_FORMAT, vCodecCtx->width, vCodecCtx->height, 1);
                        outBufferPtr                    = Marshal.AllocHGlobal(outBufferSize);

                        av_image_fill_arrays(ref outData, ref outLineSize, (byte*)outBufferPtr, _PIXEL_FORMAT, vCodecCtx->width, vCodecCtx->height, 1);
                        swsCtx = sws_getContext(vCodecCtx->width, vCodecCtx->height, vCodecCtx->pix_fmt, vCodecCtx->width, vCodecCtx->height, _PIXEL_FORMAT, vSwsOptFlags, null, null, null);
                    }
                    ret = sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, outData, outLineSize);

                    if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); }
                    ret = 0;

                    DataStream ds = new DataStream(outLineSize[0] * vCodecCtx->height, true, true);
                    DataBox db = new DataBox();

                    db.DataPointer = ds.DataPointer;
                    db.RowPitch = outLineSize[0];
                    ds.WriteRange((IntPtr)outData.ToArray()[0], ds.Length);

                    mFrame.textureRGB = new Texture2D(d3d11Device, textDescRGB, new DataBox[] { db });

                    Utilities.Dispose(ref ds);

                    SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_VIDEO);
                }

                return ret;

            } catch (ThreadAbortException) { //throw t;
            } catch (Exception e) { ret = -1;  Log("Error[" + (ret).ToString("D4") + "], Func: ProcessVideoFrame(), Msg: " + e.Message + " - " + e.StackTrace); }

            return ret;
        }
        private int ProcessSubsFrame(AVPacket* avpacket, AVSubtitle* sub)
        {
            int ret = 0;

            try
            {
                string line = "";
                byte[] buffer;
                AVSubtitleRect** rects = sub->rects;
                AVSubtitleRect* cur = rects[0];
                
                switch (cur->type)
                {
                    case AVSubtitleType.SUBTITLE_ASS:
                        buffer = new byte[1024];
                        line = BytePtrToStringUTF8(cur->ass);
                        break;

                    case AVSubtitleType.SUBTITLE_TEXT:
                        buffer = new byte[1024];
                        line = BytePtrToStringUTF8(cur->ass);

                        break;

                    case AVSubtitleType.SUBTITLE_BITMAP:
                        Log("Subtitles BITMAP -> Not Implemented yet");

                        break;
                }
                
                MediaFrame mFrame   = new MediaFrame();
                mFrame.text         = line;
                mFrame.pts          = avpacket->pts;
                mFrame.timestamp    = (long) (mFrame.pts * sTimbebaseLowTicks);
                mFrame.duration     = (int) (sub->end_display_time - sub->start_display_time);
                if (mFrame.pts == AV_NOPTS_VALUE) return -1;
                SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_SUBTITLE);

            } catch (ThreadAbortException) {
            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessSubsFrame(), Msg: " + e.StackTrace); }

            return ret;
        }
        #endregion

        #region Seeking
        // For Seeking from I -> B/P Frame
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public int SeekAccurate(int ms, AVMediaType mType)
        {
            try
            {
                int ret = 0;
                if (!isReady) return -1;

                if (ms < 0) ms = 0;
            
                long calcTimestamp =(long) ms * 10000;
                Status oldStatus;
            
                switch (mType)
                {
                    case AVMediaType.AVMEDIA_TYPE_AUDIO:
                        oldStatus = aStatus;

                        if (calcTimestamp > vStreamInfo.durationTicks) { aStatus = oldStatus; break; }
                        if (calcTimestamp < vStreamInfo.startTimeTicks) calcTimestamp = vStreamInfo.startTimeTicks + 20000;
                        calcTimestamp = (long)(calcTimestamp / vStreamInfo.timebaseLowTicks);

                        if (aDecoder != null && aDecoder.IsAlive) { aDecoder.Abort(); Thread.Sleep(30); }
                        aStatus = Status.SEEKING;

                        ret = avformat_seek_file(aFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, AVSEEK_FLAG_ANY); // Matroska (mkv/avi etc) requires to seek through Video Stream to avoid seeking the whole file
                        avcodec_flush_buffers(aCodecCtx);

                        aStatus = oldStatus;
                        break;

                    case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        oldStatus = vStatus;
                    
                        if (calcTimestamp > vStreamInfo.durationTicks ) { vStatus = oldStatus; break; }
                        if (calcTimestamp < vStreamInfo.startTimeTicks) calcTimestamp = vStreamInfo.startTimeTicks + 20000; // Because of rationals
                        calcTimestamp = (long)(calcTimestamp / vStreamInfo.timebaseLowTicks);
                    
                        if (vDecoder != null && vDecoder.IsAlive) { vDecoder.Abort(); Thread.Sleep(30); }
                        vStatus = Status.SEEKING;

                        ret = avformat_seek_file(vFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, AVSEEK_FLAG_BACKWARD);                    
                        avcodec_flush_buffers(vCodecCtx);
                        if (calcTimestamp * vStreamInfo.timebaseLowTicks >= vStreamInfo.startTimeTicks ) ret = DecodeSilent(mType, (long)ms * 10000);

                        vStatus = oldStatus;
                        break;

                    case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                        // TODO: For embeded Subtitles probably still going through the whole file
                        oldStatus = sStatus;
                        sStatus = Status.SEEKING;
                        ret = avformat_seek_file(sFmtCtx, sStream->index, Int64.MinValue, (long) (calcTimestamp / sTimbebaseLowTicks), Int64.MaxValue, AVSEEK_FLAG_BACKWARD);

                        avcodec_flush_buffers(sCodecCtx);

                        sStatus = oldStatus;
                        break;
                }

                if (ret != 0) { Log(" - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", TS: " + calcTimestamp); return -1; }

                return 0;
            } catch (Exception e) { Log(e.Message + "\r\n" + e.StackTrace); return -1; }
        }
        private int DecodeSilent(AVMediaType mType, long endTimestamp)
        {
            Log("[START 1] " + mType);

            int ret = 0;

            AVPacket* avpacket  = av_packet_alloc();
            AVFrame*  avframe   = av_frame_alloc();
            long curPts = AV_NOPTS_VALUE;

            av_init_packet(avpacket);
            try { 
                if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {

                    while (vStatus == Status.SEEKING && (ret = av_read_frame(vFmtCtx, avpacket)) == 0)
                    {
                        if (curPts > endTimestamp) return -1;

                        if (avpacket->stream_index == vStream->index)
                        {
                            ret = DecodeFrameSilent(avframe, vCodecCtx, avpacket);
                            curPts = avframe->best_effort_timestamp == AV_NOPTS_VALUE ? avframe->pts : avframe->best_effort_timestamp;
                        }

                        if (avpacket->stream_index == vStream->index && curPts != AV_NOPTS_VALUE &&
                            endTimestamp - ((avpacket->duration * vStreamInfo.timebaseLowTicks * 2)) < (curPts * vStreamInfo.timebaseLowTicks) )
                        {
                            ProcessVideoFrame(avframe);
                            break;
                        }

                        Thread.Sleep(4);
                        av_packet_unref(avpacket);
                    }
                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    while (aStatus == Status.SEEKING && (ret = av_read_frame(aFmtCtx, avpacket)) == 0)
                    {
                        if (curPts > endTimestamp) return -1;

                        if (avpacket->stream_index == aStream->index)
                        {
                            ret = DecodeFrameSilent(avframe, aCodecCtx, avpacket);
                            curPts = avframe->best_effort_timestamp == AV_NOPTS_VALUE ? avframe->pts : avframe->best_effort_timestamp;
                        }

                        if (avpacket->stream_index == aStream->index && curPts != AV_NOPTS_VALUE &&
                            endTimestamp - ((avpacket->duration * aStreamInfo.timebaseLowTicks * 2)) < (curPts * aStreamInfo.timebaseLowTicks) )
                            break;

                        Thread.Sleep(4);
                        av_packet_unref(avpacket);
                    }
                }

                av_packet_unref(avpacket);

                if (ret < 0 && ret != AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

            } catch (ThreadAbortException)  { Log("[INFO  1] Killed " + mType); 
            } catch (Exception e)           { Log("[ERROR 1] " + mType + " " + e.Message + " " + e.StackTrace); 
            } finally
            {
                av_packet_unref(avpacket);
                av_frame_free(&avframe);

                Log("[END   1] " + mType);
            }

            return 0;
        }

        // For Stream Buffering before Running
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public int SeekAccurate2(int ms, AVMediaType mType)
        {
            try
            {
                Log($"[SEEK] Start {mType.ToString()} ms -> {ms}");

                int ret = 0;
                if (!isReady) return -1;

                if (ms < 0) ms = 0;
                long calcTimestamp =(long) ms * 10000;

                switch (mType)
                {
                    case AVMediaType.AVMEDIA_TYPE_AUDIO:
                        if (calcTimestamp > vStreamInfo.durationTicks ) break;
                        if (calcTimestamp < vStreamInfo.startTimeTicks) calcTimestamp = vStreamInfo.startTimeTicks + 20000;
                        calcTimestamp = (long)(calcTimestamp / vStreamInfo.timebaseLowTicks);

                        if (aDecoder != null && aDecoder.IsAlive) { aDecoder.Abort(); Thread.Sleep(30); }

                        ret = avformat_seek_file(aFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, AVSEEK_FLAG_ANY);
                        avcodec_flush_buffers(aCodecCtx);
                    
                        break;

                    case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        if (calcTimestamp > vStreamInfo.durationTicks ) break;
                        if (calcTimestamp < vStreamInfo.startTimeTicks) calcTimestamp = vStreamInfo.startTimeTicks + 20000;
                        calcTimestamp = (long)(calcTimestamp / vStreamInfo.timebaseLowTicks);
                    
                        if (vDecoder != null && vDecoder.IsAlive) { vDecoder.Abort(); Thread.Sleep(30); }

                        ret = avformat_seek_file(vFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, AVSEEK_FLAG_ANY);                    
                        avcodec_flush_buffers(vCodecCtx);

                        break;

                    case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                        ret = avformat_seek_file(sFmtCtx, sStream->index, Int64.MinValue, (long) (calcTimestamp / sTimbebaseLowTicks), Int64.MaxValue, AVSEEK_FLAG_BACKWARD);
                        avcodec_flush_buffers(sCodecCtx);

                        break;
                }

                Log($"[SEEK] End {mType.ToString()} ms -> {ms}");
                if (ret != 0) { Log(" - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", TS: " + calcTimestamp); return -1; }
            
                return 0;
            } catch (Exception e) { Log(e.Message + "\r\n" + e.StackTrace); return -1; }
        }
        public int DecodeSilent2(AVMediaType mType, long endTimestamp, bool single = false)
        {
            Log("[BUFFER START 1] " + mType);

            int ret = 0;
            bool informed = false;

            AVPacket* avpacket = av_packet_alloc();
            av_init_packet(avpacket);

            try
            { 
                if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    while (vStatus == Status.RUNNING && (ret = av_read_frame(vFmtCtx, avpacket)) == 0)
                    {
                        if (!informed && avpacket->stream_index == vStream->index && endTimestamp < avpacket->dts * vStreamInfo.timebaseLowTicks) 
                        {
                            Log($"[VBUFFER] to -> {(avpacket->dts * vStreamInfo.timebaseLowTicks)/10000} ms");
                            BufferingDone?.BeginInvoke(AVMediaType.AVMEDIA_TYPE_VIDEO, null, null);
                            
                            informed = true;
                        }

                        //if ( informed ) Thread.Sleep(10);
                        av_packet_unref(avpacket);
                    }
                    
                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    while (aStatus == Status.RUNNING && (ret = av_read_frame(aFmtCtx, avpacket)) == 0)
                    {
                        if (!informed && avpacket->stream_index == aStream->index && endTimestamp < avpacket->dts * aStreamInfo.timebaseLowTicks)
                        { 
                            Log($"[ABUFFER] to -> {(avpacket->dts * aStreamInfo.timebaseLowTicks) / 10000} ms");

                            if (single)
                                BufferingAudioDone?.BeginInvoke(null, null); 
                            else
                                BufferingDone?.BeginInvoke(AVMediaType.AVMEDIA_TYPE_AUDIO, null, null);

                            informed = true;
                        }

                        if ( informed ) Thread.Sleep(10);
                        av_packet_unref(avpacket);
                    }
                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                {
                    bool was = isSubsExternal;

                    while (sStatus == Status.RUNNING && (ret = av_read_frame(sFmtCtx, avpacket)) == 0)
                    {
                        if (was != isSubsExternal) break; // No reason to run

                        if (!informed && avpacket->stream_index == sStream->index && endTimestamp < avpacket->dts * sTimbebaseLowTicks) 
                        { 
                            Log($"[SBUFFER] to -> {(avpacket->dts * sTimbebaseLowTicks) / 10000} ms");

                            if (single)
                                BufferingSubsDone?.BeginInvoke(null, null); 
                            else
                                BufferingDone?.BeginInvoke(AVMediaType.AVMEDIA_TYPE_SUBTITLE, null, null);

                            informed = true;
                        }

                        if ( informed ) Thread.Sleep(10);
                        av_packet_unref(avpacket);
                    }
                }

                av_packet_unref(avpacket);
                
                if (!informed && ret == AVERROR_EOF)
                {
                    if (single && mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                        BufferingAudioDone?.BeginInvoke(null, null); 
                    else if (single && mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                        BufferingSubsDone?.BeginInvoke(null, null); 
                    else
                        BufferingDone?.BeginInvoke(mType, null, null);
                }

                if (ret < 0 && ret != AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

            } catch (ThreadAbortException)  { Log("[BUFFER INFO  1] Killed " + mType); 
            } catch (Exception e)           { Log("[BUFFER ERROR 1] " + mType + " " + e.Message + " " + e.StackTrace); 
            } finally
            {
                av_packet_unref(avpacket);
                
                Log("[BUFFER END   1] " + mType);
            }

            return 0;
        }
        #endregion

        #region Actions
        private void IOConfiguration(Func<long, int, AVMediaType, byte[]> ReadPacketClbk, long totalSize)
        {   
            aCurPos = 0;
            vCurPos = 0;
            //sCurPos = 0;
            gcPrevent.Clear();

            avio_alloc_context_read_packet aIOReadPacket = (opaque, buffer, bufferSize) =>
            {
                try
                {
                    int bytesRead   = aCurPos + bufferSize > totalSize ? (int) (totalSize - aCurPos) : bufferSize;
                    byte[] data     = ReadPacketClbk(aCurPos, bytesRead, AVMediaType.AVMEDIA_TYPE_VIDEO);
                    if (data == null || data.Length < bytesRead) { Log($"[CASE 001] A Empty Data"); return -1; }

                    Marshal.Copy(data, 0, (IntPtr) buffer, bytesRead);
                    aCurPos += bytesRead;

                    return bytesRead;
                } 
                catch (ThreadAbortException t) { Log($"[CASE 001] A Killed Empty Data"); throw t; }
                catch (Exception e) { Log("[CASE 001] A " + e.Message + "\r\n" + e.StackTrace); return -1; }
            };

            avio_alloc_context_seek aIOSeek = (opaque, offset, wehnce) =>
            {
                try
                {
                    if ( wehnce == AVSEEK_SIZE )
                        return totalSize;
                    else if ( (SeekOrigin) wehnce == SeekOrigin.Begin )
                        aCurPos = offset;
                    else if ( (SeekOrigin) wehnce == SeekOrigin.Current )
                        aCurPos += offset;
                    else if ( (SeekOrigin) wehnce == SeekOrigin.End )
                        aCurPos = totalSize - offset;
                    else
                        aCurPos = -1;

                    return aCurPos;
                }
                catch (ThreadAbortException t) { Log($"[CASE 001] A Seek Killed"); throw t; }
            };

            avio_alloc_context_read_packet_func aioread = new avio_alloc_context_read_packet_func();
            aioread.Pointer = Marshal.GetFunctionPointerForDelegate(aIOReadPacket);
            
            avio_alloc_context_seek_func aioseek = new avio_alloc_context_seek_func();
            aioseek.Pointer = Marshal.GetFunctionPointerForDelegate(aIOSeek);
            
            byte* aReadBuffer = (byte*)av_malloc(IOBufferSize);
            aIOCtx = avio_alloc_context(aReadBuffer, IOBufferSize, 0, null, aioread, null, aioseek);
            aFmtCtx->pb = aIOCtx;
            aFmtCtx->flags |= AVFMT_FLAG_CUSTOM_IO;

            avio_alloc_context_read_packet vIOReadPacket = (opaque, buffer, bufferSize) =>
            {
                try
                {
                    int bytesRead   = vCurPos + bufferSize > totalSize ? (int) (totalSize - vCurPos) : bufferSize;
                    byte[] data     = ReadPacketClbk(vCurPos, bytesRead, AVMediaType.AVMEDIA_TYPE_VIDEO);
                    if (data == null || data.Length < bytesRead) { Log($"[CASE 001] V Empty Data"); return -1; }

                    Marshal.Copy(data, 0, (IntPtr) buffer, bytesRead);
                    vCurPos += bytesRead;

                    return bytesRead;
                } 
                catch (ThreadAbortException t) { Log($"[CASE 001] V Killed Empty Data"); throw t; }
                catch (Exception e) { Log("[CASE 001] V " + e.Message + "\r\n" + e.StackTrace); return -1; }
            };

            avio_alloc_context_seek vIOSeek = (opaque, offset, wehnce) =>
            {
                try
                {
                    if ( wehnce == AVSEEK_SIZE )
                        return totalSize;
                    else if ( (SeekOrigin) wehnce == SeekOrigin.Begin )
                        vCurPos = offset;
                    else if ( (SeekOrigin) wehnce == SeekOrigin.Current )
                        vCurPos += offset;
                    else if ( (SeekOrigin) wehnce == SeekOrigin.End )
                        vCurPos = totalSize - offset;
                    else
                        vCurPos = -1;

                    return vCurPos;
                }
                catch (ThreadAbortException t) { Log($"[CASE 001] V Seek Killed"); throw t; }
            };

            avio_alloc_context_read_packet_func vioread = new avio_alloc_context_read_packet_func();
            vioread.Pointer = Marshal.GetFunctionPointerForDelegate(vIOReadPacket);

            avio_alloc_context_seek_func vioseek = new avio_alloc_context_seek_func();
            vioseek.Pointer = Marshal.GetFunctionPointerForDelegate(vIOSeek);

            byte* vReadBuffer = (byte*)av_malloc(IOBufferSize);
            vIOCtx = avio_alloc_context(vReadBuffer, IOBufferSize, 0, null, vioread, null, vioseek);
            vFmtCtx->pb = vIOCtx;
            vFmtCtx->flags |= AVFMT_FLAG_CUSTOM_IO;

            gcPrevent.Add(aioread);
            gcPrevent.Add(aioseek);
            gcPrevent.Add(vioread);
            gcPrevent.Add(vioseek);

            gcPrevent.Add(aIOReadPacket);
            gcPrevent.Add(aIOSeek);
            gcPrevent.Add(vIOReadPacket);
            gcPrevent.Add(vIOSeek);

            #region Embedded Subtitles Currently Disabled
            /* 
            avio_alloc_context_read_packet sIOReadPacket = (opaque, buffer, bufferSize) =>
            {
                int bytesRead = bufferSize;

                if (sCurPos + bufferSize > totalSize)
                    bytesRead = (int) (totalSize - sCurPos);

                try
                {
                    byte[] data = ReadPacketClbk(sCurPos, bytesRead, AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                    if (data == null || data.Length < bytesRead) { Log($"[CASE 001] Empty Data"); return 0; }
                    Marshal.Copy(data, 0, (IntPtr) buffer, bytesRead);
                    sCurPos += bytesRead;
                } catch (ThreadAbortException t)
                {
                    Log($"[CASE 001] Killed Empty Data"); 
                    bytesRead = 0;
                    throw t;
                }
                
                return bytesRead;
            };
            avio_alloc_context_seek sIOSeek = (opaque, offset, wehnce) =>
            {
                if ( wehnce == AVSEEK_SIZE )
                    return totalSize;
                else if ( (SeekOrigin) wehnce == SeekOrigin.Begin )
                    sCurPos = offset;
                else
                    sCurPos += offset;

                return sCurPos;
            };

            avio_alloc_context_read_packet_func sioread = new avio_alloc_context_read_packet_func();
            sioread.Pointer = Marshal.GetFunctionPointerForDelegate(sIOReadPacket);

            avio_alloc_context_seek_func sioseek = new avio_alloc_context_seek_func();
            sioseek.Pointer = Marshal.GetFunctionPointerForDelegate(sIOSeek);

            byte* sReadBuffer = (byte*)av_malloc(IOBufferSize);
            sIOCtx = avio_alloc_context(sReadBuffer, IOBufferSize, 0, null, sioread, null, sioseek);
            sFmtCtx->pb = sIOCtx;
            sFmtCtx->flags |= AVFMT_FLAG_CUSTOM_IO;
            
            gcPrevent.Add(sIOReadPacket);
            gcPrevent.Add(sIOSeek);
            gcPrevent.Add(sioread);
            gcPrevent.Add(sioseek);

            */
            #endregion
        }
        public int  Open(string url, Func<long, int, AVMediaType, byte[]> ReadPacketClbk = null, long totalSize = 0)
        {
            int ret;

            long escapeInfinity = 250 / 10;
            while (status == Status.OPENING && escapeInfinity > 0)
            {
                Thread.Sleep(10);
                escapeInfinity--;
            }

            if (status == Status.OPENING) return -4;

            try
            {
                Initialize();
                status = Status.OPENING;

                // Format Contexts | IO Configuration
                AVFormatContext* fmtCtxPtr;

                aFmtCtx = avformat_alloc_context();
                vFmtCtx = avformat_alloc_context();
                sFmtCtx = avformat_alloc_context();

                if ( url == null ) {
                    if ( ReadPacketClbk == null || totalSize == 0 ) 
                        return -1;

                    IOConfiguration(ReadPacketClbk, totalSize);
                }

                if (doAudio)
                {
                    fmtCtxPtr = aFmtCtx;
                    ret = avformat_open_input(&fmtCtxPtr, url, null, null);
                    if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); aFmtCtx = null; return ret; }
                }
                

                if ( status != Status.OPENING )
                {
                    if (doAudio) { fmtCtxPtr = aFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(aFmtCtx); }
                    
                    aFmtCtx = null; vFmtCtx = null; sFmtCtx = null; swsCtx = null;

                    status = Status.STOPPED;
                    ret = -2;
                    Log("Error[" + ret.ToString("D4") + "], Msg: Opening was canceled, Status -> " + status.ToString());
                    return ret; 
                }
                
                fmtCtxPtr = vFmtCtx;
                ret = avformat_open_input(&fmtCtxPtr, url, null, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); vFmtCtx = null; return ret; }

                if ( status != Status.OPENING )
                {
                    if (doAudio) { fmtCtxPtr = aFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(aFmtCtx); }
                    fmtCtxPtr = vFmtCtx; avformat_close_input(&fmtCtxPtr);

                    aFmtCtx = null; vFmtCtx = null; sFmtCtx = null; swsCtx = null;

                    status = Status.STOPPED;
                    ret = -2;
                    Log("Error[" + ret.ToString("D4") + "], Msg: Opening was canceled, Status -> " + status.ToString());
                    return ret; 
                }

                if ( url != null )
                {
                    if (doSubs)
                    {
                        fmtCtxPtr = sFmtCtx;
                        ret     = avformat_open_input(&fmtCtxPtr, url, null, null);
                        if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); sFmtCtx = null; return ret; }
                    }
                }

                if ( status != Status.OPENING )
                {
                    if (doAudio)    { fmtCtxPtr = aFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(aFmtCtx); }
                    if (doSubs)     { fmtCtxPtr = vFmtCtx; avformat_close_input(&fmtCtxPtr); }
                    fmtCtxPtr = sFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(sFmtCtx);

                    aFmtCtx = null; vFmtCtx = null; sFmtCtx = null; swsCtx = null;

                    status = Status.STOPPED;
                    ret = -2;
                    Log("Error[" + ret.ToString("D4") + "], Msg: Opening was canceled, Status -> " + status.ToString());
                    return ret; 
                }

                // Streams Find
                ret = avformat_find_stream_info(vFmtCtx, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                if (doAudio)
                {
                    ret = avformat_find_stream_info(aFmtCtx, null);
                    if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                }

                if ( url != null) // TEMPORARY DISABLE SUBS
                {
                    if (doSubs)
                    {
                        ret = avformat_find_stream_info(sFmtCtx, null);
                        if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                    }
                }

                if ( status != Status.OPENING )
                {
                    if (doAudio)    { fmtCtxPtr = aFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(aFmtCtx); }
                    if (doSubs)     { fmtCtxPtr = vFmtCtx; avformat_close_input(&fmtCtxPtr); }
                    fmtCtxPtr = vFmtCtx; avformat_close_input(&fmtCtxPtr);

                    aFmtCtx = null; vFmtCtx = null; sFmtCtx = null; swsCtx = null;

                    status = Status.STOPPED;
                    ret = -2;
                    Log("Error[" + ret.ToString("D4") + "], Msg: Opening was canceled, Status -> " + status.ToString());
                    return ret; 
                }

                // Stream Setup
                ret = SetupStream(AVMediaType.AVMEDIA_TYPE_VIDEO);
                if (ret < 0 && ret != AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                if (doAudio)
                {
                    ret = SetupStream(AVMediaType.AVMEDIA_TYPE_AUDIO);
                    if (ret < 0 && ret != AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); hasAudio = false; }
                }

                if ( url != null) // TEMPORARY DISABLE SUBS
                {
                    if (doSubs)
                    {
                        ret = SetupStream(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                        if (ret < 0 && ret != AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                    }
                }
                
                if (!hasVideo) { Log("Error[" + (-1).ToString("D4") + "], Msg: No Video stream found"); return -1; }
                
                if (hasAudio)
                    for (int i = 0; i < aFmtCtx->nb_streams; i++)
                        if (i != aStream->index && i != vStream->index) aFmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
                    
                if (hasVideo)
                    for (int i = 0; i < vFmtCtx->nb_streams; i++)
                        if (i != vStream->index) vFmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

                if (hasSubs)
                    for (int i = 0; i < sFmtCtx->nb_streams; i++)
                        if (i != sStream->index) sFmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
                

                if ( status != Status.OPENING )
                {
                    if (doAudio)    { fmtCtxPtr = aFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(aFmtCtx); }
                    if (doSubs)     { fmtCtxPtr = vFmtCtx; avformat_close_input(&fmtCtxPtr); }
                    fmtCtxPtr = vFmtCtx; avformat_close_input(&fmtCtxPtr);

                    aFmtCtx = null; vFmtCtx = null; sFmtCtx = null; swsCtx = null;

                    status = Status.STOPPED;
                    ret = -2;
                    Log("Error[" + ret.ToString("D4") + "], Msg: Opening was canceled, Status -> " + status.ToString());
                    return ret; 
                }

                // Codecs
                if (hasAudio)   { ret = SetupCodec(AVMediaType.AVMEDIA_TYPE_AUDIO);    if (ret != 0) hasAudio = false; }
                if (hasVideo)   { ret = SetupCodec(AVMediaType.AVMEDIA_TYPE_VIDEO);    if (ret != 0) return ret; }
                if (hasSubs )   { ret = SetupCodec(AVMediaType.AVMEDIA_TYPE_SUBTITLE); }

                // Setups
                if (hasAudio)   { ret = SetupAudio(); if (ret != 0) hasAudio = false; }
                if (hasVideo)   { ret = SetupVideo(); if (ret != 0) return ret; }
                if (hasSubs)    sTimbebaseLowTicks = av_q2d(sStream->time_base) * 10000 * 1000;

                // Free
                if (!hasAudio)  { fmtCtxPtr = aFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(aFmtCtx); aFmtCtx = null; }
                if (!hasVideo)  { fmtCtxPtr = vFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(vFmtCtx); vFmtCtx = null; }
                if (!hasSubs && url != null)  { fmtCtxPtr = sFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(sFmtCtx); sFmtCtx = null; }

            } catch (Exception e) { Log(e.StackTrace); return -1;

            } finally {
                status = Status.STOPPED;
            }

            isReady = true;

            return 0;
        }
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public int  OpenSubs(string url)
        {
            int ret = 0;
            AVFormatContext* fmtCtxPtr;

            if (hasSubs)
            {
                PauseSubs();

                try
                {
                    if (sFmtCtx != null) { fmtCtxPtr = sFmtCtx; avformat_close_input(&fmtCtxPtr); av_freep(sFmtCtx); }
                } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }
                
                sFmtCtx = null;
            }
            
            try
            {
                Log("[OPEN SUBS START 0] " + sStatus + " " + AVMediaType.AVMEDIA_TYPE_SUBTITLE);   

                sFinish         = false;
                hasSubs         = false;
                isSubsExternal  = true;

                sFmtCtx     = avformat_alloc_context(); fmtCtxPtr = sFmtCtx;
                ret         = avformat_open_input(&fmtCtxPtr, url, null, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); av_freep(sFmtCtx); sFmtCtx = null; return ret; }

                // Stream
                ret         = avformat_find_stream_info(sFmtCtx, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                ret         = SetupStream(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                if (ret < 0 && ret != AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); hasSubs = false; }

                if (!hasSubs) { Log("Error[" + (-1).ToString("D4") + "], Msg: No Subtitles stream found"); avformat_close_input(&fmtCtxPtr); av_freep(sFmtCtx); sFmtCtx = null; return ret; }

                for (int i = 0; i < sFmtCtx->nb_streams; i++)
                    if (i != sStream->index) sFmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

                // Codec
                ret         = SetupCodec(AVMediaType.AVMEDIA_TYPE_SUBTITLE); if (ret != 0) return ret;

                sTimbebaseLowTicks = av_q2d(sStream->time_base) * 10000 * 1000;

            } catch (Exception e) { Log("[OPEN SUBS ERROR 0] " + sStatus + " " + e.Message + " " + e.StackTrace); }

            Log("[OPEN SUBS END 0] " + sStatus + " " + AVMediaType.AVMEDIA_TYPE_SUBTITLE);   

            return ret;
        }
        public void RunAudio()
        {
            if (!isReady || !hasAudio) return;
            PauseAudio();

            status = Status.RUNNING;
            aStatus = Status.RUNNING;

            aDecoder = new Thread(() =>
            {
                int res = Decode(AVMediaType.AVMEDIA_TYPE_AUDIO);
                aStatus = Status.STOPPED;
                if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
                Log("[END 1] " + aStatus + " " + AVMediaType.AVMEDIA_TYPE_AUDIO);
            });
            aDecoder.SetApartmentState(ApartmentState.STA);
            aDecoder.Start();
        }
        public void RunVideo()
        {
            if (!isReady || !hasVideo) return;
            PauseVideo();

            status = Status.RUNNING;
            vStatus = Status.RUNNING;

            if (hasVideo)
            {
                vDecoder = new Thread(() =>
                {
                    int res = Decode(AVMediaType.AVMEDIA_TYPE_VIDEO);
                    vStatus = Status.STOPPED;
                    if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
                    Log("[1] " + vStatus + " " + AVMediaType.AVMEDIA_TYPE_VIDEO);
                });
                vDecoder.SetApartmentState(ApartmentState.STA);
                vDecoder.Start();
            }
        }
        public void RunSubs()
        {
            if (!isReady || !hasSubs) return;
            PauseSubs();

            status = Status.RUNNING;
            sStatus = Status.RUNNING;
            sDecoder = new Thread(() =>
            {
                int res = Decode(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                sStatus = Status.STOPPED;
                if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
                Log("[END 1] " + sStatus + " " + AVMediaType.AVMEDIA_TYPE_SUBTITLE);   
            });
            sDecoder.SetApartmentState(ApartmentState.STA);
            sDecoder.Start();
        }
        public void Pause()
        {
            if (!isReady) return;

            status = Status.STOPPING; aStatus = Status.STOPPING; vStatus = Status.STOPPING; sStatus = Status.STOPPING;
            
            Utils.EnsureThreadDone(aDecoder);
            Utils.EnsureThreadDone(vDecoder);
            Utils.EnsureThreadDone(sDecoder);

            status = Status.STOPPED; aStatus = Status.STOPPED; vStatus = Status.STOPPED; sStatus = Status.STOPPED;
        }
        
        public void PauseVideo()
        {
            if (!isReady || !hasVideo) return;

            vStatus = Status.STOPPING;
            Utils.EnsureThreadDone(vDecoder);
            vStatus = Status.STOPPED;

            if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
        }
        public void PauseAudio()
        {
            if (!isReady || !hasAudio) return;

            aStatus = Status.STOPPING;
            Utils.EnsureThreadDone(aDecoder);
            aStatus = Status.STOPPED;

            if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
        }
        public void PauseSubs()
        {
            if (!isReady || !hasSubs) return;

            sStatus = Status.STOPPING;
            Utils.EnsureThreadDone(sDecoder);
            sStatus = Status.STOPPED;
            
            if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
        }
        public int  Stop()
        {
            Pause();

            if (hasAudio) SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_AUDIO);
            if (hasVideo) SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_VIDEO);
            if (hasSubs)  SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_SUBTITLE);

            return 0;
        }
        #endregion

        #region Misc
        private List<AVHWDeviceType>    GetHWDevices()
        {
            List<AVHWDeviceType> hwDevices  = new List<AVHWDeviceType>();
            AVHWDeviceType       type       = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

            while ( (type = av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE )
                hwDevices.Add(type);

            return hwDevices;
        }
        private List<HWDeviceSupported> GetHWDevicesSupported()
        {
            List<HWDeviceSupported> hwDevicesSupported = new List<HWDeviceSupported>();

            for (int i = 0; ; i++)
            {
                AVCodecHWConfig* config = avcodec_get_hw_config(vCodec, i);
                if (config == null) break;

                Log("[HWACCEL] Codec Supports " + config->device_type + " - " + config->pix_fmt + " - " + config->methods);
                if ((config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == 0 || config->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE) continue;

                foreach (HWDeviceSupported hwDeviceExists in hwDevicesSupported)
                    if (hwDeviceExists.type == config->device_type) { hwDeviceExists.pixFmts.Add(config->pix_fmt); continue; }

                HWDeviceSupported hwDeviceNew = new HWDeviceSupported();
                hwDeviceNew.type        = config->device_type;
                hwDeviceNew.pixFmts     = new List<AVPixelFormat>();
                hwDeviceNew.pixFmts.Add(config->pix_fmt);
                hwDevicesSupported.Add(hwDeviceNew);
            }
#pragma warning disable CS0162 // Unreachable code detected
            return hwDevicesSupported;
#pragma warning restore CS0162 // Unreachable code detected
        }

        private void Log(string msg) { if (verbosity > 0) Console.WriteLine("[DECODER]" + msg); }
        public unsafe string BytePtrToStringUTF8(byte* bytePtr)
        {
            if (bytePtr == null) return null;
            if (*bytePtr == 0) return string.Empty;

            var byteBuffer = new List<byte>(1024);
            var currentByte = default(byte);

            while (true)
            {
                currentByte = *bytePtr;
                if (currentByte == 0)
                    break;

                byteBuffer.Add(currentByte);
                bytePtr++;
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }
        private static string ErrorCodeToMsg(int error)
        {
            byte* buffer = stackalloc byte[1024];
            av_strerror(error, buffer, 1024);
            return Marshal.PtrToStringAnsi((IntPtr)buffer);
        }
        private av_log_set_callback_callback ffmpegLogCallback = (p0, level, format, vl) =>
        {
            if (level > av_log_get_level()) return;

            var buffer = stackalloc byte[1024];
            var printPrefix = 1;
            av_log_format_line(p0, level, format, vl, buffer, 1024, &printPrefix);
            var line = Marshal.PtrToStringAnsi((IntPtr)buffer);
            Console.WriteLine(line.Trim());
        };

        public static bool alreadyRegister = false;
        public static void RegisterFFmpegBinaries()
        {
            if (alreadyRegister) 
                return;
            alreadyRegister = true;

            var current = Environment.CurrentDirectory;
            var probe = Path.Combine("Libs", Environment.Is64BitProcess ? "x64" : "x86", "FFmpeg");

            while (current != null)
            {
                var ffmpegBinaryPath = Path.Combine(current, probe);
                if (Directory.Exists(ffmpegBinaryPath))
                {
                    Console.WriteLine($"FFmpeg binaries found in: {ffmpegBinaryPath}");
                    RootPath = ffmpegBinaryPath;
                    return;
                }
                current = Directory.GetParent(current)?.FullName;
            }
        }
        #endregion
    }
}