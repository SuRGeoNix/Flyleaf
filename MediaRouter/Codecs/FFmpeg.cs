/*
 * Codec FFmpeg
 * 
 * Based on FFmpeg.AutoGen C# .NET bindings by Ruslan Balanukhin [https://github.com/Ruslan-B/FFmpeg.AutoGen]
 * 
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

using SharpDX.DXGI;
using SharpDX.Direct3D11;

using Device    = SharpDX.Direct3D11.Device;
using STexture2D= SharpDX.Direct3D11.Texture2D;

using FFmpeg.AutoGen;

namespace PartyTime.Codecs
{
    unsafe public class FFmpeg
    {
        // Audio Output Parameters [ BITS | CHANNELS | RATE ]
        AVSampleFormat _SAMPLE_FORMAT   = AVSampleFormat.AV_SAMPLE_FMT_S16; int _CHANNELS = 2; 
        public int _RATE { get; private set; } // Will be set from Input Format

        // Video Output Parameters
        STexture2D[]    sharedTextures;
        public int      _HW_TEXTURES_SIZE   = 20;
        int             sharedTextureIndex  = 0;

        AVPixelFormat _PIXEL_FORMAT     = AVPixelFormat.AV_PIX_FMT_RGBA;
        int _SCALING_HQ                 = ffmpeg.SWS_ACCURATE_RND | ffmpeg.SWS_BITEXACT | ffmpeg.SWS_LANCZOS | ffmpeg.SWS_FULL_CHR_H_INT | ffmpeg.SWS_FULL_CHR_H_INP;
        int _SCALING_LQ                 = ffmpeg.SWS_BICUBIC;
        int vSwsOptFlags;
        
        // Video Output Buffer
        IntPtr                  outBufferPtr; 
        int                     outBufferSize;
        byte_ptrArray4          outData;
        int_array4              outLineSize;

        // Contexts             [Audio]     [Video]     [Subs]      [Audio/Video]       [Subs/Video]
        AVFormatContext*        aFmtCtx,    vFmtCtx,    sFmtCtx;
        AVIOContext*            aIOCtx,     vIOCtx,     sIOCtx;
        AVStream*               aStream,    vStream,    sStream;
        AVCodecContext*         aCodecCtx,  vCodecCtx,  sCodecCtx;
        AVCodec*                aCodec,     vCodec,     sCodec;
        SwrContext*             swrCtx;
        SwsContext*                         swsCtx;   //sSwsCtx;

        Thread                  aDecoder,   vDecoder,   sDecoder;

        // Status
        public enum Status
        {
            READY   = 0,
            RUNNING = 1,
            SEEKING = 3,
            STOPPED = 4,
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

        private long                        aCurPos, vCurPos, sCurPos;
        List<object>                        gcPrevent       = new List<object>();
        private const int                   IOBufferSize    = 0x40000;

        public bool isSubsExternal { get; private set; }
        bool aFinish, vFinish, sFinish;

        // HW Acceleration
        const int                           AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        Device                              avD3D11Device;
        AVBufferRef*                        hw_device_ctx;
        List<AVHWDeviceType>                hwDevices;
        List<HWDeviceSupported>             hwDevicesSupported;
        struct HWDeviceSupported
        {
            public AVHWDeviceType       type;
            public List<AVPixelFormat>  pixFmts;
        }

        // Public Exposure [Properties & Structures]
        public struct MediaFrame
        {
            public byte[]   data;
            public IntPtr   texture;
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

        public AudioStreamInfo aStreamInfo;
        public VideoStreamInfo vStreamInfo;

        private double sTimbebaseLowTicks;

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
        public bool HighQuality     { get; set; }
        public bool HWAccel         { get; set; }
        public bool hwAccelSuccess  { get; private set; }
        public int  verbosity       { get; set; }

        // Constructors
        public FFmpeg(Action<MediaFrame, AVMediaType> RecvFrameCallback = null, int verbosity = 0)
        {
            RegisterFFmpegBinaries();

            if      (verbosity == 1) { ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);        ffmpeg.av_log_set_callback(ffmpegLogCallback); } 
            else if (verbosity  > 1) { ffmpeg.av_log_set_level(ffmpeg.AV_LOG_MAX_OFFSET);   ffmpeg.av_log_set_callback(ffmpegLogCallback); }
            this.verbosity      = verbosity;
            SendFrame           = RecvFrameCallback;

            hwDevices           = GetHWDevices();
            hwDevicesSupported  = new List<HWDeviceSupported>();
            sharedTextures      = new STexture2D[_HW_TEXTURES_SIZE];

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

            // TODO: Will not properly free HW textures | mFrame.texture = sharedResource.SharedHandle; Keeps them in GPU for some reason
            //for (int i=0; i<_HW_TEXTURES_SIZE; i++)
            //    if (sharedTextures[i] != null) sharedTextures[i].Dispose();

            //if (avD3D11Device != null)
            //{
            //    avD3D11Device.ImmediateContext.ClearState();
            //    avD3D11Device.ImmediateContext.Flush();
            //}

            AVFormatContext* fmtCtxPtr;
            try
            {
                if (aFmtCtx != null) { fmtCtxPtr = aFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(aFmtCtx); }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }
                
            try
            {
                if (vFmtCtx != null) { ffmpeg.avcodec_close(vCodecCtx); ffmpeg.sws_freeContext(swsCtx); fmtCtxPtr = vFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); /*ffmpeg.av_freep(vFmtCtx);*/ }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }
                
            try
            {
                if (sFmtCtx != null) { fmtCtxPtr = sFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(sFmtCtx); }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }

            aFmtCtx = null; vFmtCtx = null; sFmtCtx = null; swsCtx = null; 
        }
        private void InitializeSubs()
        {
            sStatus = Status.STOPPED;
            sFinish         = false;
            Thread.Sleep(30);
            if (sDecoder != null && sDecoder.IsAlive) sDecoder.Abort();
            AVFormatContext* fmtCtxPtr = sFmtCtx;
            ffmpeg.avformat_close_input(&fmtCtxPtr);

            hasSubs = false;
        }

        // Implementation [Setup]
        private int SetupStream(AVMediaType mType)
        {
            int streamIndex     = -1;

            if      (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)   { streamIndex = ffmpeg.av_find_best_stream(aFmtCtx, mType, -1, -1, null, 0); }
            else if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)   { streamIndex = ffmpeg.av_find_best_stream(vFmtCtx, mType, -1, -1, null, 0); }
            else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE){ streamIndex = ffmpeg.av_find_best_stream(sFmtCtx, mType, -1, -1, null, 0); }

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
                aCodec      = ffmpeg.avcodec_find_decoder(aStream->codec->codec_id);
                ret         = ffmpeg.avcodec_open2(aCodecCtx, aCodec, null); 
            }
            else if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO && hasVideo)
            {
                vCodecCtx   = vStream->codec;
                vCodec      = ffmpeg.avcodec_find_decoder(vStream->codec->codec_id);
                SetupHQAndHWAcceleration();
                ret = ffmpeg.avcodec_open2(vCodecCtx, vCodec, null); 
            }
            else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE && hasSubs)
            {
                sCodecCtx   = sStream->codec;
                sCodec      = ffmpeg.avcodec_find_decoder(sStream->codec->codec_id);
                ret         = ffmpeg.avcodec_open2(sCodecCtx, sCodec, null); }
            else return -1;

            if (ret != 0) Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));

            return ret;
        }
        private int SetupHQAndHWAcceleration()
        {
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
                        { if (ffmpeg.av_hwdevice_ctx_create(&hw_device_ctx2, hwDevice, "auto", null, 0) != 0) continue; }
                    else
                        hw_device_ctx2 = hw_device_ctx;

                    // Available Pixel Format's are supported from SWS (Currently using only NV12 for RGBA convert later with sws_scale)
                    AVHWFramesConstraints* hw_frames_const = ffmpeg.av_hwdevice_get_hwframe_constraints(hw_device_ctx2, null); // ffmpeg.av_hwdevice_hwconfig_alloc(hw_device_ctx)
                    if (hw_frames_const == null) { ffmpeg.av_buffer_unref(&hw_device_ctx2); continue; }
                    for (AVPixelFormat* p = hw_frames_const->valid_sw_formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
                        if (ffmpeg.sws_isSupportedInput(*p) > 0)
                            if (*p == AVPixelFormat.AV_PIX_FMT_NV12) { found = true; break; }
                    if (!found) { ffmpeg.av_buffer_unref(&hw_device_ctx2); continue; }

                    if ( hw_device_ctx == null ) 
                    {
                        hw_device_ctx = hw_device_ctx2;
                        AVHWDeviceContext* hw_device_ctx3 = (AVHWDeviceContext*)hw_device_ctx->data;
                        AVD3D11VADeviceContext* hw_d3d11_dev_ctx = (AVD3D11VADeviceContext*)hw_device_ctx3->hwctx;
                        avD3D11Device = Device.FromPointer<Device>((IntPtr) hw_d3d11_dev_ctx->device);
                    }

                    for (int i=0; i<_HW_TEXTURES_SIZE; i++)
                        sharedTextures[i] =  new STexture2D(avD3D11Device, new Texture2DDescription()
                        {
                            Usage               = ResourceUsage.Default,
                            Format              = Format.NV12,

                            Width               = vCodecCtx->width,
                            Height              = vCodecCtx->height,
                        
                            BindFlags           = BindFlags.ShaderResource | BindFlags.RenderTarget,
                            CpuAccessFlags      = CpuAccessFlags.None,
                            OptionFlags         = ResourceOptionFlags.Shared,

                            SampleDescription   = new SampleDescription(1, 0),
                            ArraySize           = 1,
                            MipLevels           = 1
                        });

                    vCodecCtx->hw_device_ctx = ffmpeg.av_buffer_ref(hw_device_ctx);
                    hwAccelSuccess = true;
                    Log("[HWACCEL] Enabled! Device -> " + hwDevice + ", Codec -> " + Marshal.PtrToStringAnsi((IntPtr)vCodec->name));
                    
                    break;
                }
            }
            
            return 0;
        }
        private int SetupAudio()
        {
            int ret = 0;

            aStreamInfo.timebase                    = ffmpeg.av_q2d(aStream->time_base);
            aStreamInfo.timebaseLowTicks            = ffmpeg.av_q2d(aStream->time_base) * 10000 * 1000;
            aStreamInfo.startTimeTicks              = (aStreamInfo.startTimeTicks != ffmpeg.AV_NOPTS_VALUE) ? (long)(aStream->start_time * aStreamInfo.timebaseLowTicks) : 0;
            aStreamInfo.durationTicks               = (aStream->duration > 0) ? (long)(aStream->duration * aStreamInfo.timebaseLowTicks) : aFmtCtx->duration * 10;
            aStreamInfo.frameSize                   = aCodecCtx->frame_size;
            _RATE                                   = aCodecCtx->sample_rate;
            swrCtx = ffmpeg.swr_alloc();

            ffmpeg.av_opt_set_int(swrCtx,           "in_channel_layout",   (int)aCodecCtx->channel_layout, 0);
            ffmpeg.av_opt_set_int(swrCtx,           "in_channel_count",         aCodecCtx->channels, 0);
            ffmpeg.av_opt_set_int(swrCtx,           "in_sample_rate",           aCodecCtx->sample_rate, 0);
            ffmpeg.av_opt_set_sample_fmt(swrCtx,    "in_sample_fmt",            aCodecCtx->sample_fmt, 0);

            ffmpeg.av_opt_set_int(swrCtx,           "out_channel_layout",       ffmpeg.av_get_default_channel_layout(_CHANNELS), 0);
            ffmpeg.av_opt_set_int(swrCtx,           "out_channel_count",        _CHANNELS, 0);
            ffmpeg.av_opt_set_int(swrCtx,           "out_sample_rate",          _RATE, 0);
            ffmpeg.av_opt_set_sample_fmt(swrCtx,    "out_sample_fmt",           _SAMPLE_FORMAT, 0);

            ret = ffmpeg.swr_init(swrCtx);
            
            if (ret != 0) Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));

            return ret;
        }
        private int SetupVideo()
        {
            int ret = 0;

            // Store Stream Info
            vStreamInfo.timebase            = ffmpeg.av_q2d(vStream->time_base) ;
            vStreamInfo.timebaseLowTicks    = ffmpeg.av_q2d(vStream->time_base) * 10000 * 1000;
            vStreamInfo.startTimeTicks      = (vStreamInfo.startTimeTicks != ffmpeg.AV_NOPTS_VALUE) ? (long)(vStream->start_time * vStreamInfo.timebaseLowTicks) : 0;
            vStreamInfo.durationTicks       = (vStream->duration > 0) ? (long)(vStream->duration * vStreamInfo.timebaseLowTicks) : vFmtCtx->duration * 10;
            vStreamInfo.fps                 = ffmpeg.av_q2d(vStream->avg_frame_rate);
            vStreamInfo.frameAvgTicks       = (long)((1 / vStreamInfo.fps) * 1000 * 10000);
            vStreamInfo.height              = vCodecCtx->height;
            vStreamInfo.width               = vCodecCtx->width;

            return ret;
        }

        // Implementation [Decode]
        private int Decode(AVMediaType mType)
        {
            Log("[DECODER START 0] " + status + " " + mType);

            int ret = 0;

            AVPacket* avpacket  = ffmpeg.av_packet_alloc();
            AVFrame*  avframe   = ffmpeg.av_frame_alloc();
            ffmpeg.av_init_packet(avpacket);
            
            try
            {
                if (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    aFinish = false;

                    while (isRunning && aStatus == Status.RUNNING   && (ret = ffmpeg.av_read_frame(aFmtCtx, avpacket)) == 0)
                    {
                        if (avpacket->stream_index == aStream->index)   ret = DecodeFrame(avframe, aCodecCtx, avpacket, false);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                    ffmpeg.av_packet_unref(avpacket);

                    if (ret == ffmpeg.AVERROR_EOF) aFinish = true;
                    if (ret < 0 && ret != ffmpeg.AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    vFinish = false;

                    while (isRunning && vStatus == Status.RUNNING   && (ret = ffmpeg.av_read_frame(vFmtCtx, avpacket)) == 0)
                    {
                        if (avpacket->stream_index == vStream->index)   ret = DecodeFrame(avframe, vCodecCtx, avpacket, false);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                    ffmpeg.av_packet_unref(avpacket);

                    if (ret < 0 && ret != ffmpeg.AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                    // Drain Mode
                    if (ret == ffmpeg.AVERROR_EOF)
                    { 
                        vFinish = true;
                        ret = DecodeFrame(avframe, vCodecCtx, null, true); 
                        if (ret != 0) Log(vCodecCtx->codec_type.ToString() + " - Warning[" + ret.ToString("D4") + "], Msg: Failed to decode frame, PTS: " + avpacket->pts);
                    }
                } 
                else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                {
                    sFinish = false;

                    while (isRunning && sStatus == Status.RUNNING   && (ret = ffmpeg.av_read_frame(sFmtCtx, avpacket)) == 0)
                    {
                        if (avpacket->stream_index == sStream->index)   ret = DecodeFrameSubs(sCodecCtx, avpacket);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                    ffmpeg.av_packet_unref(avpacket);

                    if (ret == ffmpeg.AVERROR_EOF) sFinish = true;
                    if (ret < 0 && ret != ffmpeg.AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                }

            } catch (ThreadAbortException)  { Log("[DECODER INFO  0] Killed " + mType);
            } catch (Exception e)           { Log("[DECODER ERROR 0] " + mType + " " + e.Message + " " + e.StackTrace); 
            } 
            finally 
            { 
                ffmpeg.av_packet_unref(avpacket);
                ffmpeg.av_frame_free  (&avframe);

                Log("[DECODER END   0] " + status + " " + mType);
            }

            return 0;
        }
        private int DecodeFrame(AVFrame* avframe, AVCodecContext* codecCtx, AVPacket* avpacket, bool drainMode)
        {
            int ret = 0;

            ret = ffmpeg.avcodec_send_packet(codecCtx, avpacket);
            if (ret != 0) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

            do
            {
                ret = ffmpeg.avcodec_receive_frame(codecCtx, avframe);

                if (avpacket == null && ret == 0) Log(codecCtx->codec_type.ToString() + " - Warning[" + ret.ToString("D4") + "], Msg: drain packet, PTS: <null>");
                if (ret == ffmpeg.AVERROR_EOF || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN)) return 0;
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

            ret = ffmpeg.avcodec_decode_subtitle2(codeCtx, &sub, &gotFrame, avpacket);
            if (ret < 0)  { Log(codeCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) ); return ret; }
            if (gotFrame < 1 || sub.num_rects < 1 ) return -1;

            ret = ProcessSubsFrame(avpacket, &sub);
            ffmpeg.avsubtitle_free(&sub);
            if (ret != 0) { Log(codeCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: Failed to process SUBS frame, PTS: " + avpacket->pts); return -1; }

            return 0;
        }
        private int DecodeFrameSilent(AVFrame* avframe, AVCodecContext* codecCtx, AVPacket* avpacket)
        {
            int ret = 0;

            ret = ffmpeg.avcodec_send_packet(codecCtx, avpacket);
            if (ret != 0) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", PTS: " + avpacket->pts); return ret; }

            ret = ffmpeg.avcodec_receive_frame(codecCtx, avframe);
            
            if (avpacket == null && ret == 0) Log(codecCtx->codec_type.ToString() + " - Warning[" + ret.ToString("D4") + "], Msg: drain packet, PTS: <null>");
            if (ret == ffmpeg.AVERROR_EOF || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN)) { return 0; }
            if (ret < 0) { Log(codecCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", PTS: " + avpacket->pts); return ret; }
            if (avframe->repeat_pict == 1) Log("Warning, Repeated Frame -> " + avframe->best_effort_timestamp.ToString());

            return 0;
        }

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

                        ret = ffmpeg.avformat_seek_file(aFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, ffmpeg.AVSEEK_FLAG_ANY); // Matroska (mkv/avi etc) requires to seek through Video Stream to avoid seeking the whole file
                        ffmpeg.avcodec_flush_buffers(aCodecCtx);

                        aStatus = oldStatus;
                        break;

                    case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        oldStatus = vStatus;
                    
                        if (calcTimestamp > vStreamInfo.durationTicks ) { vStatus = oldStatus; break; }
                        if (calcTimestamp < vStreamInfo.startTimeTicks) calcTimestamp = vStreamInfo.startTimeTicks + 20000; // Because of rationals
                        calcTimestamp = (long)(calcTimestamp / vStreamInfo.timebaseLowTicks);
                    
                        if (vDecoder != null && vDecoder.IsAlive) { vDecoder.Abort(); Thread.Sleep(30); }
                        vStatus = Status.SEEKING;

                        sharedTextureIndex = 0;
                        ret = ffmpeg.avformat_seek_file(vFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);                    
                        ffmpeg.avcodec_flush_buffers(vCodecCtx);
                        if (calcTimestamp * vStreamInfo.timebaseLowTicks >= vStreamInfo.startTimeTicks ) ret = DecodeSilent(mType, (long)ms * 10000);

                        vStatus = oldStatus;
                        break;

                    case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                        // TODO: For embeded Subtitles probably still going through the whole file
                        oldStatus = sStatus;
                        sStatus = Status.SEEKING;
                        ret = ffmpeg.avformat_seek_file(sFmtCtx, sStream->index, Int64.MinValue, (long) (calcTimestamp / sTimbebaseLowTicks), Int64.MaxValue, ffmpeg.AVSEEK_FLAG_BACKWARD);

                        ffmpeg.avcodec_flush_buffers(sCodecCtx);

                        sStatus = oldStatus;
                        break;
                }

                if (ret != 0) { Log(" - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", TS: " + calcTimestamp); return -1; }

                return 0;
            } catch (Exception e) { Log(e.Message + "\r\n" + e.StackTrace); return -1; }
        }
        private int DecodeSilent(AVMediaType mType, long endTimestamp)
        {
            Log("[DECODER START 1] " + mType);

            int ret = 0;

            AVPacket* avpacket  = ffmpeg.av_packet_alloc();
            AVFrame*  avframe   = ffmpeg.av_frame_alloc();
            long curPts = ffmpeg.AV_NOPTS_VALUE;

            ffmpeg.av_init_packet(avpacket);
            try { 
                if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    sharedTextureIndex = 0;

                    while (vStatus == Status.SEEKING && (ret = ffmpeg.av_read_frame(vFmtCtx, avpacket)) == 0)
                    {
                        if (curPts > endTimestamp) return -1;

                        if (avpacket->stream_index == vStream->index)
                        {
                            ret = DecodeFrameSilent(avframe, vCodecCtx, avpacket);
                            curPts = avframe->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? avframe->pts : avframe->best_effort_timestamp;
                        }

                        if (avpacket->stream_index == vStream->index && curPts != ffmpeg.AV_NOPTS_VALUE &&
                            endTimestamp - ((avpacket->duration * vStreamInfo.timebaseLowTicks * 2)) < (curPts * vStreamInfo.timebaseLowTicks) )
                        {
                            ProcessVideoFrame(avframe);
                            break;
                        }

                        Thread.Sleep(4);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    while (aStatus == Status.SEEKING && (ret = ffmpeg.av_read_frame(aFmtCtx, avpacket)) == 0)
                    {
                        if (curPts > endTimestamp) return -1;

                        if (avpacket->stream_index == aStream->index)
                        {
                            ret = DecodeFrameSilent(avframe, aCodecCtx, avpacket);
                            curPts = avframe->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? avframe->pts : avframe->best_effort_timestamp;
                        }

                        if (avpacket->stream_index == aStream->index && curPts != ffmpeg.AV_NOPTS_VALUE &&
                            endTimestamp - ((avpacket->duration * aStreamInfo.timebaseLowTicks * 2)) < (curPts * aStreamInfo.timebaseLowTicks) )
                            break;

                        Thread.Sleep(4);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                }

                ffmpeg.av_packet_unref(avpacket);

                if (ret < 0 && ret != ffmpeg.AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

            } catch (ThreadAbortException)  { Log("[DECODER INFO  1] Killed " + mType); 
            } catch (Exception e)           { Log("[DECODER ERROR 1] " + mType + " " + e.Message + " " + e.StackTrace); 
            } finally
            {
                ffmpeg.av_packet_unref(avpacket);
                ffmpeg.av_frame_free(&avframe);

                Log("[DECODER END   1] " + mType);
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

                        ret = ffmpeg.avformat_seek_file(aFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, ffmpeg.AVSEEK_FLAG_ANY);
                        ffmpeg.avcodec_flush_buffers(aCodecCtx);
                    
                        break;

                    case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        if (calcTimestamp > vStreamInfo.durationTicks ) break;
                        if (calcTimestamp < vStreamInfo.startTimeTicks) calcTimestamp = vStreamInfo.startTimeTicks + 20000;
                        calcTimestamp = (long)(calcTimestamp / vStreamInfo.timebaseLowTicks);
                    
                        if (vDecoder != null && vDecoder.IsAlive) { vDecoder.Abort(); Thread.Sleep(30); }

                        ret = ffmpeg.avformat_seek_file(vFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, ffmpeg.AVSEEK_FLAG_ANY);                    
                        ffmpeg.avcodec_flush_buffers(vCodecCtx);

                        break;

                    case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                        ret = ffmpeg.avformat_seek_file(sFmtCtx, sStream->index, Int64.MinValue, (long) (calcTimestamp / sTimbebaseLowTicks), Int64.MaxValue, ffmpeg.AVSEEK_FLAG_BACKWARD);
                        ffmpeg.avcodec_flush_buffers(sCodecCtx);

                        break;
                }

                Log($"[SEEK] End {mType.ToString()} ms -> {ms}");
                if (ret != 0) { Log(" - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", TS: " + calcTimestamp); return -1; }
            
                return 0;
            } catch (Exception e) { Log(e.Message + "\r\n" + e.StackTrace); return -1; }
        }
        public int DecodeSilent2(AVMediaType mType, long endTimestamp, bool single = false)
        {
            Log("[DECODER BUFFER START 1] " + mType);

            int ret = 0;
            bool informed = false;

            AVPacket* avpacket = ffmpeg.av_packet_alloc();
            ffmpeg.av_init_packet(avpacket);

            try
            { 
                if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    while (vStatus == Status.RUNNING && (ret = ffmpeg.av_read_frame(vFmtCtx, avpacket)) == 0)
                    {
                        if (!informed && avpacket->stream_index == vStream->index && endTimestamp < avpacket->dts * vStreamInfo.timebaseLowTicks) 
                        {
                            Log($"[VBUFFER] to -> {(avpacket->dts * vStreamInfo.timebaseLowTicks)/10000} ms");
                            BufferingDone?.BeginInvoke(AVMediaType.AVMEDIA_TYPE_VIDEO, null, null);
                            
                            informed = true;
                        }

                        //if ( informed ) Thread.Sleep(10);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                    
                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    while (aStatus == Status.RUNNING && (ret = ffmpeg.av_read_frame(aFmtCtx, avpacket)) == 0)
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
                        ffmpeg.av_packet_unref(avpacket);
                    }
                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                {
                    bool was = isSubsExternal;

                    while (sStatus == Status.RUNNING && (ret = ffmpeg.av_read_frame(sFmtCtx, avpacket)) == 0)
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
                        ffmpeg.av_packet_unref(avpacket);
                    }
                }

                ffmpeg.av_packet_unref(avpacket);
                
                if (!informed && ret == ffmpeg.AVERROR_EOF)
                {
                    if (single && mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                        BufferingAudioDone?.BeginInvoke(null, null); 
                    else if (single && mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                        BufferingSubsDone?.BeginInvoke(null, null); 
                    else
                        BufferingDone?.BeginInvoke(mType, null, null);
                }

                if (ret < 0 && ret != ffmpeg.AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

            } catch (ThreadAbortException)  { Log("[DECODER BUFFER INFO  1] Killed " + mType); 
            } catch (Exception e)           { Log("[DECODER BUFFER ERROR 1] " + mType + " " + e.Message + " " + e.StackTrace); 
            } finally
            {
                ffmpeg.av_packet_unref(avpacket);
                
                Log("[DECODER BUFFER END   1] " + mType);
            }

            return 0;
        }

        // Implementation [Output Format]
        private int ProcessAudioFrame(AVFrame* frame)
        {
            int ret = 0;

            try
            {
                var bufferSize  = ffmpeg.av_samples_get_buffer_size(null, _CHANNELS, frame->nb_samples, _SAMPLE_FORMAT, 1);
                byte[] buffer   = new byte[bufferSize];

                fixed (byte** buffers = new byte*[8])
                {
                    fixed (byte* bufferPtr = &buffer[0])
                    {
                        // Convert
                        buffers[0]          = bufferPtr;
                        ffmpeg.swr_convert(swrCtx, buffers, frame->nb_samples, (byte**)&frame->data, frame->nb_samples);

                        // Send Frame
                        if (frame->nb_samples > 0)
                        {
                            MediaFrame mFrame   = new MediaFrame();
                            mFrame.data         = new byte[bufferSize]; System.Buffer.BlockCopy(buffer, 0, mFrame.data, 0, bufferSize);
                            mFrame.pts          = frame->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                            mFrame.timestamp    = (long)(mFrame.pts * aStreamInfo.timebaseLowTicks);
                            if (mFrame.pts == ffmpeg.AV_NOPTS_VALUE) return -1;
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
        private int ProcessVideoFrame(AVFrame* frame2)
        {
            int ret = 0;
            
            try
            {
                // TODO: In case HW decoding not supported but HW Processing supported
                if (hwAccelSuccess)
                {
                    STexture2D nv12texture = null;
                    SharpDX.DXGI.Resource sharedResource = null;
                    
                    nv12texture = new STexture2D((IntPtr) frame2->data.ToArray()[0]);
                    avD3D11Device.ImmediateContext.CopySubresourceRegion(nv12texture, (int) frame2->data.ToArray()[1], null, sharedTextures[sharedTextureIndex], 0);

                    avD3D11Device.ImmediateContext.Flush();
                    Thread.Sleep(10); // Temporary to ensure Flushing is done (maybe GetData/CreateQuery)

                    sharedResource = sharedTextures[sharedTextureIndex].QueryInterface<SharpDX.DXGI.Resource>();
                    MediaFrame mFrame   = new MediaFrame();
                    mFrame.texture      = sharedResource.SharedHandle;
                    mFrame.pts          = frame2->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? frame2->pts : frame2->best_effort_timestamp;
                    mFrame.timestamp    = (long)(mFrame.pts * vStreamInfo.timebaseLowTicks);
                    if (mFrame.pts == ffmpeg.AV_NOPTS_VALUE) return -1;
                    SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_VIDEO);

                    sharedTextureIndex ++;
                    if (sharedTextureIndex > _HW_TEXTURES_SIZE - 1) sharedTextureIndex = 0;
                    return ret;
                }
                
                // TODO: In case HW decoding supported but HW Processing is not
                AVFrame* frame = frame2;
                
                // Decode one frame and added at the beginning/opening
                if (swsCtx == null)
                {
                    outData                         = new byte_ptrArray4();
                    outLineSize                     = new int_array4();
                    outBufferSize                   = ffmpeg.av_image_get_buffer_size(_PIXEL_FORMAT, frame->linesize.ToArray()[0], vStreamInfo.height, 1);
                    outBufferPtr                    = Marshal.AllocHGlobal(outBufferSize);
                    ffmpeg.av_image_fill_arrays(ref outData, ref outLineSize, (byte*)outBufferPtr, _PIXEL_FORMAT, frame->linesize.ToArray()[0], vStreamInfo.height, 1);
                    outLineSize.UpdateFrom(new int[]{ frame->linesize.ToArray()[0] * 4 , 0 , 0, 0 });

                    vStreamInfo.width               = frame->linesize.ToArray()[0];

                    swsCtx = ffmpeg.sws_getContext(vCodecCtx->width, vCodecCtx->height, vCodecCtx->pix_fmt, frame->linesize.ToArray()[0], vCodecCtx->height, _PIXEL_FORMAT, vSwsOptFlags, null, null, null);
                }
                
                ret = ffmpeg.sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, outData, outLineSize);

                // Send Frame
                if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); }
                else
                {
                    ret = 0;

                    MediaFrame mFrame   = new MediaFrame();
                    mFrame.data         = new byte[outBufferSize];
                    Marshal.Copy(outBufferPtr, mFrame.data, 0, outBufferSize);
                    mFrame.pts          = frame->best_effort_timestamp == ffmpeg.AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                    mFrame.timestamp    = (long)(mFrame.pts * vStreamInfo.timebaseLowTicks);
                    if (mFrame.pts == ffmpeg.AV_NOPTS_VALUE) return -1;
                    SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_VIDEO);
                }

            } catch (ThreadAbortException) {
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
                if (mFrame.pts == ffmpeg.AV_NOPTS_VALUE) return -1;
                SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_SUBTITLE);

            } catch (ThreadAbortException) {
            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessSubsFrame(), Msg: " + e.StackTrace); }

            return ret;
        }

        // Public Exposure [Methods]
        private void IOConfiguration(Func<long, int, AVMediaType, byte[]> ReadPacketClbk, long totalSize)
        {   
            aCurPos = 0;
            vCurPos = 0;
            sCurPos = 0;
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
                    if ( wehnce == ffmpeg.AVSEEK_SIZE )
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
            
            byte* aReadBuffer = (byte*)ffmpeg.av_malloc(IOBufferSize);
            aIOCtx = ffmpeg.avio_alloc_context(aReadBuffer, IOBufferSize, 0, null, aioread, null, aioseek);
            aFmtCtx->pb = aIOCtx;
            aFmtCtx->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;

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
                    if ( wehnce == ffmpeg.AVSEEK_SIZE )
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

            byte* vReadBuffer = (byte*)ffmpeg.av_malloc(IOBufferSize);
            vIOCtx = ffmpeg.avio_alloc_context(vReadBuffer, IOBufferSize, 0, null, vioread, null, vioseek);
            vFmtCtx->pb = vIOCtx;
            vFmtCtx->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;

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
                if ( wehnce == ffmpeg.AVSEEK_SIZE )
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

            byte* sReadBuffer = (byte*)ffmpeg.av_malloc(IOBufferSize);
            sIOCtx = ffmpeg.avio_alloc_context(sReadBuffer, IOBufferSize, 0, null, sioread, null, sioseek);
            sFmtCtx->pb = sIOCtx;
            sFmtCtx->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;
            
            gcPrevent.Add(sIOReadPacket);
            gcPrevent.Add(sIOSeek);
            gcPrevent.Add(sioread);
            gcPrevent.Add(sioseek);

            */
            #endregion
        }
        public int Open(string url, Func<long, int, AVMediaType, byte[]> ReadPacketClbk = null, long totalSize = 0)
        {
            int ret;

            try
            {
                Initialize();

                // Format Contexts | IO Configuration
                AVFormatContext* fmtCtxPtr;

                aFmtCtx = ffmpeg.avformat_alloc_context();
                vFmtCtx = ffmpeg.avformat_alloc_context();
                sFmtCtx = ffmpeg.avformat_alloc_context();

                if ( url == null ) {
                    if ( ReadPacketClbk == null || totalSize == 0 ) 
                        return -1;

                    IOConfiguration(ReadPacketClbk, totalSize);
                }

                fmtCtxPtr = aFmtCtx;
                ret     = ffmpeg.avformat_open_input(&fmtCtxPtr, url, null, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); aFmtCtx = null; return ret; }

                fmtCtxPtr = vFmtCtx;
                ret     = ffmpeg.avformat_open_input(&fmtCtxPtr, url, null, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); vFmtCtx = null; return ret; }

                if ( url != null )
                {
                    fmtCtxPtr = sFmtCtx;
                    ret     = ffmpeg.avformat_open_input(&fmtCtxPtr, url, null, null);
                    if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); sFmtCtx = null; return ret; }
                }

                // Streams
                ret     = ffmpeg.avformat_find_stream_info(aFmtCtx, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                ret     = ffmpeg.avformat_find_stream_info(vFmtCtx, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                if ( url != null )
                {
                    ret     = ffmpeg.avformat_find_stream_info(sFmtCtx, null);
                    if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                }

                ret     = SetupStream(AVMediaType.AVMEDIA_TYPE_AUDIO);
                if (ret < 0 && ret != ffmpeg.AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                ret     = SetupStream(AVMediaType.AVMEDIA_TYPE_VIDEO);
                if (ret < 0 && ret != ffmpeg.AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                if ( url != null )
                {
                    ret     = SetupStream(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                    if (ret < 0 && ret != ffmpeg.AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
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
                
                // Codecs
                if (hasAudio)   { ret = SetupCodec(AVMediaType.AVMEDIA_TYPE_AUDIO);    if (ret != 0) return ret; }
                if (hasVideo)   { ret = SetupCodec(AVMediaType.AVMEDIA_TYPE_VIDEO);    if (ret != 0) return ret; }
                if (hasSubs )   { ret = SetupCodec(AVMediaType.AVMEDIA_TYPE_SUBTITLE); }

                // Setups
                if (hasAudio)   { ret = SetupAudio(); if (ret != 0) return ret; }
                if (hasVideo)   { ret = SetupVideo(); if (ret != 0) return ret; }

                // Free
                if (!hasAudio)  { fmtCtxPtr = aFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(aFmtCtx); aFmtCtx = null; }
                if (!hasVideo)  { fmtCtxPtr = vFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(vFmtCtx); vFmtCtx = null; }
                if (!hasSubs && url != null)  { fmtCtxPtr = sFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(sFmtCtx); sFmtCtx = null; }

                if (hasSubs) sTimbebaseLowTicks = ffmpeg.av_q2d(sStream->time_base) * 10000 * 1000;

            } catch (Exception e) { Log(e.StackTrace); return -1; }

            isReady = true;

            return 0;
        }
        public int OpenSubs(string url)
        {
            int ret;

            try
            {
                isSubsExternal = true;
                InitializeSubs();

                // Format Context
                AVFormatContext* fmtCtxPtr;

                sFmtCtx     = ffmpeg.avformat_alloc_context(); fmtCtxPtr = sFmtCtx;
                ret         = ffmpeg.avformat_open_input(&fmtCtxPtr, url, null, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                // Stream
                ret         = ffmpeg.avformat_find_stream_info(sFmtCtx, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                ret         = SetupStream(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                if (ret < 0 && ret != ffmpeg.AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                if (!hasSubs) { Log("Error[" + (-1).ToString("D4") + "], Msg: No Subtitles stream found"); return -1; }

                for (int i = 0; i < sFmtCtx->nb_streams; i++)
                    if (i != sStream->index) sFmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

                // Codec
                ret         = SetupCodec(AVMediaType.AVMEDIA_TYPE_SUBTITLE); if (ret != 0) return ret;

                sTimbebaseLowTicks = ffmpeg.av_q2d(sStream->time_base) * 10000 * 1000;

            } catch (Exception e) { InitializeSubs(); Log(e.StackTrace); return -1; }

            return 0;
        }
        public int RunAudio()
        {
            if (!isReady) return -1;
            if (aDecoder != null && aDecoder.IsAlive) aDecoder.Abort();

            status = Status.RUNNING;

            if (hasAudio)
            {
                aStatus = Status.RUNNING;
                aDecoder = new Thread(() =>
                {
                    int res = Decode(AVMediaType.AVMEDIA_TYPE_AUDIO);
                    aStatus = Status.STOPPED;
                    if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
                    Log("[DECODER 1] " + aStatus + " " + AVMediaType.AVMEDIA_TYPE_AUDIO);
                });
                aDecoder.SetApartmentState(ApartmentState.STA);
                aDecoder.Start();
            }

            return 0;
        }
        public int RunVideo()
        {
            if (!isReady) return -1;
            if (vDecoder != null && vDecoder.IsAlive) vDecoder.Abort();

            status = Status.RUNNING;

            if (hasVideo)
            {
                vStatus = Status.RUNNING;
                vDecoder = new Thread(() =>
                {
                    int res = Decode(AVMediaType.AVMEDIA_TYPE_VIDEO);
                    vStatus = Status.STOPPED;
                    if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
                    Log("[DECODER 1] " + vStatus + " " + AVMediaType.AVMEDIA_TYPE_VIDEO);
                });
                vDecoder.SetApartmentState(ApartmentState.STA);
                vDecoder.Start();
            }

            return 0;
        }
        public int RunSubs()
        {
            if (!isReady) return -1;
            if (sDecoder != null && sDecoder.IsAlive) sDecoder.Abort();

            status = Status.RUNNING;

            if (hasSubs)
            {
                sStatus = Status.RUNNING;
                sDecoder = new Thread(() =>
                {
                    int res = Decode(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                    sStatus = Status.STOPPED;
                    if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
                    Log("[DECODER 1] " + sStatus + " " + AVMediaType.AVMEDIA_TYPE_SUBTITLE);   
                });
                sDecoder.SetApartmentState(ApartmentState.STA);
                sDecoder.Start();
            }

            return 0;
        }
        public int Pause()
        {
            if (!isReady) return -1;

            if (aDecoder != null && aDecoder.IsAlive) aDecoder.Abort();
            if (vDecoder != null && vDecoder.IsAlive) vDecoder.Abort();
            if (sDecoder != null && sDecoder.IsAlive) sDecoder.Abort();

            status = Status.STOPPED; aStatus = Status.STOPPED; vStatus = Status.STOPPED; sStatus = Status.STOPPED;

            return 0;
        }
        public void PauseAudio()
        {
            if (!isReady) return;
            if (aDecoder != null && aDecoder.IsAlive) aDecoder.Abort();
            aStatus = Status.STOPPED;
            if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
        }
        public void PauseSubs()
        {
            if (!isReady) return;
            if (sDecoder != null && sDecoder.IsAlive) sDecoder.Abort();
            sStatus = Status.STOPPED;
            if (aStatus == Status.STOPPED && vStatus == Status.STOPPED && sStatus == Status.STOPPED) status = Status.STOPPED;
        }
        public int Stop()
        {
            if (Pause() != 0) return -1;

            if (hasAudio) SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_AUDIO);
            if (hasVideo) SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_VIDEO);
            if (hasSubs)  SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_SUBTITLE);

            return 0;
        }

        // HWAccel Helpers
        private List<AVHWDeviceType>    GetHWDevices()
        {
            List<AVHWDeviceType> hwDevices  = new List<AVHWDeviceType>();
            AVHWDeviceType       type       = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

            while ( (type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE )
                hwDevices.Add(type);

            return hwDevices;
        }
        private List<HWDeviceSupported> GetHWDevicesSupported()
        {
            List<HWDeviceSupported> hwDevicesSupported = new List<HWDeviceSupported>();

            for (int i = 0; ; i++)
            {
                AVCodecHWConfig* config = ffmpeg.avcodec_get_hw_config(vCodec, i);
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

        // Misc
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine(msg); }
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
            ffmpeg.av_strerror(error, buffer, 1024);
            return Marshal.PtrToStringAnsi((IntPtr)buffer);
        }
        private av_log_set_callback_callback ffmpegLogCallback = (p0, level, format, vl) =>
        {
            if (level > ffmpeg.av_log_get_level()) return;

            var buffer = stackalloc byte[1024];
            var printPrefix = 1;
            ffmpeg.av_log_format_line(p0, level, format, vl, buffer, 1024, &printPrefix);
            var line = Marshal.PtrToStringAnsi((IntPtr)buffer);
            Console.WriteLine(line.Trim());
        };
        internal static void RegisterFFmpegBinaries()
        {
            var current = Environment.CurrentDirectory;
            var probe = Path.Combine("Codecs", "FFmpeg", Environment.Is64BitProcess ? "x64" : "x86");
            while (current != null)
            {
                var ffmpegBinaryPath = Path.Combine(current, probe);
                if (Directory.Exists(ffmpegBinaryPath))
                {
                    Console.WriteLine($"FFmpeg binaries found in: {ffmpegBinaryPath}");
                    ffmpeg.RootPath = ffmpegBinaryPath;
                    return;
                }
                current = Directory.GetParent(current)?.FullName;
            }
        }
    }
}