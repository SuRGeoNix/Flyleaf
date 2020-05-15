/*
 * Codec FFmpeg
 * 
 * Based on FFmpeg.AutoGen C# .NET bindings by Ruslan Balanukhin [https://github.com/Ruslan-B/FFmpeg.AutoGen]
 * 
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using FFmpeg.AutoGen;

namespace PartyTime.Codecs
{
    unsafe public class FFmpeg
    {
        // Audio Output Parameters [ BITS | CHANNELS | RATE ]
        AVSampleFormat _SAMPLE_FORMAT   = AVSampleFormat.AV_SAMPLE_FMT_S16; int _CHANNELS = 2; int _RATE = 48000;

        // Video Output Parameters
        AVPixelFormat _PIXEL_FORMAT     = AVPixelFormat.AV_PIX_FMT_RGBA;
        int _SCALING_HQ                 = ffmpeg.SWS_ACCURATE_RND | ffmpeg.SWS_BITEXACT | ffmpeg.SWS_LANCZOS | ffmpeg.SWS_FULL_CHR_H_INT | ffmpeg.SWS_FULL_CHR_H_INP;
        int _SCALING_LQ                 = ffmpeg.SWS_FAST_BILINEAR;
        int vSwsOptFlags;
        
        IntPtr                  outBufferPtr; 
        int                     outBufferSize;
        byte_ptrArray4          outData;
        int_array4              outLineSize;

        // Contexts             [Audio]     [Video]     [Subs]
        AVFormatContext*        aFmtCtx,    vFmtCtx,    sFmtCtx;
        AVStream*               aStream,    vStream,    sStream;
        AVCodecContext*         aCodecCtx,  vCodecCtx,  sCodecCtx;
        AVCodec*                aCodec,     vCodec,     sCodec;
        SwrContext*             swrCtx;
        SwsContext*                         swsCtx;   //sSwsCtx;

        Status                  aStatus,    vStatus,    sStatus,    status;
        Thread                  aDecoder,   vDecoder,   sDecoder;

        Action<MediaFrame, AVMediaType>     SendFrame;

        // HW Acceleration
        const int                           AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        bool                                hwAccelSuccess;
        List<AVHWDeviceType>                hwDevices;
        List<HWDeviceSupported>             hwDevicesSupported;
        struct HWDeviceSupported
        {
            public AVHWDeviceType       type;
            public List<AVPixelFormat>  pixFmts;
        }

        enum Status
        {
            READY   = 0,
            RUNNING = 1,
            SEEKING = 3,
            STOPPED = 4
        }

        // Public Exposure [Properties & Structures]
        public struct MediaFrame
        {
            public byte[]   data;
            public long     timestamp;
            public long     pts;

            public int      duration;
            public string   text;
        }
        public struct AudioStreamInfo
        {
            public double   timebase;
            public double   timebaseLowTicks;
            public long     startTimeTicks;
            public long     durationTicks;
            public int      frameSize;
        }
        public struct VideoStreamInfo
        {
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
        public bool HWAcceleration  { get; set; }
        public int  verbosity       { get; set; }

        // Constructors
        public FFmpeg(Action<MediaFrame, AVMediaType> RecvFrameCallback, int verbosity = 0)
        {
            RegisterFFmpegBinaries();

            if      (verbosity == 1) { ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);        ffmpeg.av_log_set_callback(ffmpegLogCallback); } 
            else if (verbosity == 2) { ffmpeg.av_log_set_level(ffmpeg.AV_LOG_MAX_OFFSET);   ffmpeg.av_log_set_callback(ffmpegLogCallback); }
            this.verbosity      = verbosity;
            SendFrame           = RecvFrameCallback;

            HWAcceleration      = true;
            hwDevices           = GetHWDevices();
            hwDevicesSupported  = new List<HWDeviceSupported>();

            Initialize();
        }
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
            
            vStreamInfo = new VideoStreamInfo();
            aStreamInfo = new AudioStreamInfo();

            try {
                // Let's try to proper free all those
                AVFormatContext* fmtCtxPtr;
                if (aFmtCtx != null) { fmtCtxPtr = aFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(aFmtCtx); }
                if (vFmtCtx != null) { Marshal.FreeHGlobal(outBufferPtr); ffmpeg.avcodec_close(vCodecCtx); ffmpeg.sws_freeContext(swsCtx); fmtCtxPtr = vFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); /*ffmpeg.av_freep(vFmtCtx);*/ }
                if (sFmtCtx != null) { fmtCtxPtr = sFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(sFmtCtx); }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace);
            } finally { aFmtCtx = null; vFmtCtx = null; sFmtCtx = null; }
        }
        private void InitializeSubs()
        {
            sStatus = Status.STOPPED;

            //ffmpeg.avcodec_close(sCodecCtx);
            AVFormatContext* fmtCtxPtr = sFmtCtx;
            ffmpeg.avformat_close_input(&fmtCtxPtr);

            Thread.Sleep(30);
            if (sDecoder != null && sDecoder.IsAlive) sDecoder.Abort();
            hasSubs = false;
        }

        // Implementation [Setup]
        private int SetupStream(AVMediaType mType)
        {
            int streamIndex = -1;

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
                SetupHWAcceleration();
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
        private int SetupHWAcceleration()
        {
            vSwsOptFlags = (HighQuality == true) ? _SCALING_HQ : _SCALING_LQ;

            hwAccelSuccess = false;
            if (HWAcceleration && hwDevices.Count > 0)
            {
                hwDevicesSupported = GetHWDevicesSupported();

                foreach (AVHWDeviceType hwDevice in hwDevices)
                {
                    //if (hwDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2) continue;
                    //if (hwDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA) continue;

                    // GPU device is in Codec's supported list
                    bool found = false;
                    foreach (HWDeviceSupported hwDeviceSupported in hwDevicesSupported)
                        if (hwDeviceSupported.type == hwDevice) { found = true; break; }
                    if (!found) continue;
                    found = false;

                    // HW Deivce Context (Temporary)
                    AVBufferRef* hw_device_ctx;
                    if (ffmpeg.av_hwdevice_ctx_create(&hw_device_ctx, hwDevice, null, null, 0) != 0) continue;

                    // Available Pixel Format's are supported from SWS (Currently using only NV12 for RGBA convert later with sws_scale)
                    AVHWFramesConstraints* hw_frames_const = ffmpeg.av_hwdevice_get_hwframe_constraints(hw_device_ctx, null);
                    if (hw_frames_const == null) { ffmpeg.av_buffer_unref(&hw_device_ctx); continue; }
                    for (AVPixelFormat* p = hw_frames_const->valid_sw_formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
                        if (ffmpeg.sws_isSupportedInput(*p) > 0)
                            if (*p == AVPixelFormat.AV_PIX_FMT_NV12) { found = true; break; }
                    if (!found) { ffmpeg.av_buffer_unref(&hw_device_ctx); continue; }

                    // HW Deivce Context / SWS Context
                    vCodecCtx->hw_device_ctx = ffmpeg.av_buffer_ref(hw_device_ctx);
                    ffmpeg.av_buffer_unref(&hw_device_ctx);
                    //vCodecCtx->get_format = new AVCodecContext_get_format(get_format_dxva);
                    swsCtx = ffmpeg.sws_getContext(vCodecCtx->width, vCodecCtx->height, AVPixelFormat.AV_PIX_FMT_NV12, vCodecCtx->width, vCodecCtx->height, _PIXEL_FORMAT, vSwsOptFlags, null, null, null);
                    if (swsCtx == null) continue;

                    hwAccelSuccess = true;
                    Log("[HWACCEL] Enabled! Device -> " + hwDevice + ", Codec -> " + Marshal.PtrToStringAnsi((IntPtr)vCodec->name));
                    
                    break;
                }
            }

            if (!hwAccelSuccess) swsCtx = ffmpeg.sws_getContext(vCodecCtx->width, vCodecCtx->height, vCodecCtx->pix_fmt, vCodecCtx->width, vCodecCtx->height, _PIXEL_FORMAT, vSwsOptFlags, null, null, null);
            if (swsCtx == null) return -1;

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

            // Prepare Output Buffer
            outData                         = new byte_ptrArray4();
            outLineSize                     = new int_array4();
            outBufferSize                   = ffmpeg.av_image_get_buffer_size(_PIXEL_FORMAT, vStreamInfo.width, vStreamInfo.height, 1);
            outBufferPtr                    = Marshal.AllocHGlobal(outBufferSize);
            ffmpeg.av_image_fill_arrays(ref outData, ref outLineSize, (byte*)outBufferPtr, _PIXEL_FORMAT, vStreamInfo.width, vStreamInfo.height, 1);

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
                    while (isRunning && aStatus == Status.RUNNING   && (ret = ffmpeg.av_read_frame(aFmtCtx, avpacket)) == 0)
                    {
                        if (avpacket->stream_index == aStream->index)   ret = DecodeFrame(avframe, aCodecCtx, avpacket, false);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                    ffmpeg.av_packet_unref(avpacket);

                    if (ret < 0 && ret != ffmpeg.AVERROR_EOF) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
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
                        ret = DecodeFrame(avframe, vCodecCtx, null, true); 
                        if (ret != 0) Log(vCodecCtx->codec_type.ToString() + " - Warning[" + ret.ToString("D4") + "], Msg: Failed to decode frame, PTS: " + avpacket->pts);
                    }
                } 
                else if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                {
                    while (isRunning && sStatus == Status.RUNNING   && (ret = ffmpeg.av_read_frame(sFmtCtx, avpacket)) == 0)
                    {
                        if (avpacket->stream_index == sStream->index)   ret = DecodeFrameSubs(sCodecCtx, avpacket);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                    ffmpeg.av_packet_unref(avpacket);

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
        private int DecodeSilent(AVMediaType mType, long endTimestamp)
        {
            Log("[DECODER START 1] " + mType);

            int ret = 0;

            AVPacket* avpacket = ffmpeg.av_packet_alloc();
            AVFrame* avframe = ffmpeg.av_frame_alloc();
            ffmpeg.av_init_packet(avpacket);
            try { 
                if (mType == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    while (vStatus == Status.SEEKING && (ret = ffmpeg.av_read_frame(vFmtCtx, avpacket)) == 0)
                    {
                        if (avframe->best_effort_timestamp > endTimestamp) return -1;

                        if (avpacket->stream_index == vStream->index)   ret = DecodeFrameSilent(avframe, vCodecCtx, avpacket);

                        if (avpacket->stream_index == vStream->index    && 
                            avframe->best_effort_timestamp != 0         &&
                            avframe->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE         &&
                            endTimestamp - ((avpacket->duration * vStreamInfo.timebaseLowTicks * 2)) < (avframe->best_effort_timestamp * vStreamInfo.timebaseLowTicks) )
                            break;

                        Thread.Sleep(4);
                        ffmpeg.av_packet_unref(avpacket);
                    }
                }
                else if (mType == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    while (aStatus == Status.SEEKING && (ret = ffmpeg.av_read_frame(aFmtCtx, avpacket)) == 0)
                    {
                        if (avframe->best_effort_timestamp > endTimestamp) return -1;

                        if (avpacket->stream_index == aStream->index)   ret = DecodeFrameSilent(avframe, aCodecCtx, avpacket);

                        if (avpacket->stream_index == aStream->index    && 
                            avframe->best_effort_timestamp != 0         &&
                            avframe->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE         &&
                            endTimestamp - ((avpacket->duration * aStreamInfo.timebaseLowTicks * 2)) < (avframe->best_effort_timestamp * aStreamInfo.timebaseLowTicks) )
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
                            mFrame.data         = new byte[bufferSize]; Buffer.BlockCopy(buffer, 0, mFrame.data, 0, bufferSize);
                            mFrame.pts          = frame->best_effort_timestamp;
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
        private int ProcessVideoFrame(AVFrame* frame2)
        {
            int ret = 0;

            try
            {
                AVFrame* frame;

                // [HW ACCEL]
                if (hwAccelSuccess)
                {
                    frame                        = ffmpeg.av_frame_alloc();
                    ret                          = ffmpeg.av_hwframe_transfer_data(frame, frame2, 0);
                    frame->pts                   = frame2->pts;
                    frame->best_effort_timestamp = frame2->best_effort_timestamp;
                    ffmpeg.av_frame_unref(frame2);

                } else frame = frame2;

                ret = ffmpeg.sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, outData, outLineSize);

                // Send Frame
                if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); } 
                else 
                {
                    ret = 0;

                    //byte_ptrArray8 data = new byte_ptrArray8();
                    //int_array8 linesize = new int_array8();
                    //data.UpdateFrom(outData);
                    //linesize.UpdateFrom(outLineSize);

                    MediaFrame mFrame   = new MediaFrame();
                    mFrame.data         = new byte[outBufferSize];
                    Marshal.Copy(outBufferPtr, mFrame.data, 0, outBufferSize);
                    mFrame.pts = frame->best_effort_timestamp;
                    mFrame.timestamp    = (long)(mFrame.pts * vStreamInfo.timebaseLowTicks);
                    if (mFrame.pts == ffmpeg.AV_NOPTS_VALUE) return -1;
                    SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_VIDEO);
                }

                // TODO [HW ACCEL]
                if (hwAccelSuccess) ffmpeg.av_frame_free(&frame);

            } catch (ThreadAbortException) {
            } catch (Exception e) { ret = -1;  Log("Error[" + (ret).ToString("D4") + "], Func: ProcessVideoFrame(), Msg: " + e.StackTrace); }

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
                        //line = Marshal.PtrToStringAnsi((IntPtr)cur->text);
                        break;

                    case AVSubtitleType.SUBTITLE_BITMAP:
                        Log("Subtitles BITMAP -> Not Implemented yet");
                        //sSwsCtx = ffmpeg.sws_getContext(cur->w, cur->h, sCodecCtx->pix_fmt, cur->w, cur->h, _PIXEL_FORMAT, ffmpeg.SWS_ACCURATE_RND, null, null, null);

                        //// Convert (4 -> 32bpp)
                        //byte[] buffer = new byte[cur->w * cur->h * 4];
                        //fixed (byte* ptr = &buffer[8])
                        //{
                        //    byte*[] srcData = { ptr, null, null, null };
                        //    int[] srcLinesize = { cur->w * 4, 0, 0, 0 };
                        //    ret = ffmpeg.sws_scale(sSwsCtx, cur->pict.data, cur->pict.linesize, 0, cur->h, srcData, srcLinesize);
                        //}
                        break;
                }
                
                MediaFrame mFrame = new MediaFrame();
                mFrame.text = line;
                mFrame.pts = avpacket->pts;
                mFrame.timestamp = mFrame.pts * 10000;
                mFrame.duration = (int) (sub->end_display_time - sub->start_display_time);
                if (mFrame.pts == ffmpeg.AV_NOPTS_VALUE) return -1;
                SendFrame(mFrame, AVMediaType.AVMEDIA_TYPE_SUBTITLE);

            } catch (ThreadAbortException) {
            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessSubsFrame(), Msg: " + e.StackTrace); }

            return ret;
        }

        // Public Exposure [Methods]
        public int Open(string url)
        {
            int ret;

            try
            {
                Initialize();

                // Format Contexts
                AVFormatContext* fmtCtxPtr;

                aFmtCtx = ffmpeg.avformat_alloc_context(); fmtCtxPtr = aFmtCtx;
                ret     = ffmpeg.avformat_open_input(&fmtCtxPtr, url, null, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); aFmtCtx = null; return ret; }

                vFmtCtx = ffmpeg.avformat_alloc_context(); fmtCtxPtr = vFmtCtx;
                ret     = ffmpeg.avformat_open_input(&fmtCtxPtr, url, null, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); vFmtCtx = null; return ret; }

                sFmtCtx = ffmpeg.avformat_alloc_context(); fmtCtxPtr = sFmtCtx;
                ret     = ffmpeg.avformat_open_input(&fmtCtxPtr, url, null, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); sFmtCtx = null; return ret; }

                // Streams
                ret     = ffmpeg.avformat_find_stream_info(aFmtCtx, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                ret     = ffmpeg.avformat_find_stream_info(vFmtCtx, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                ret     = ffmpeg.avformat_find_stream_info(sFmtCtx, null);
                if (ret != 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                ret     = SetupStream(AVMediaType.AVMEDIA_TYPE_AUDIO);
                if (ret < 0 && ret != ffmpeg.AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                ret     = SetupStream(AVMediaType.AVMEDIA_TYPE_VIDEO);
                if (ret < 0 && ret != ffmpeg.AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                ret     = SetupStream(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                if (ret < 0 && ret != ffmpeg.AVERROR_STREAM_NOT_FOUND) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }
                
                if (!hasAudio && !hasVideo) { Log("Error[" + (-1).ToString("D4") + "], Msg: No Audio/Video stream found"); return -1; }

                if (hasAudio)
                    for (int i = 0; i < aFmtCtx->nb_streams; i++)
                        if (i != aStream->index) aFmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

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

                if (!hasAudio)  { fmtCtxPtr = aFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(aFmtCtx); aFmtCtx = null; }
                if (!hasVideo)  { fmtCtxPtr = vFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(vFmtCtx); vFmtCtx = null; }
                if (!hasSubs )  { fmtCtxPtr = sFmtCtx; ffmpeg.avformat_close_input(&fmtCtxPtr); ffmpeg.av_freep(sFmtCtx); sFmtCtx = null; }

            } catch (Exception e) { Log(e.StackTrace); return -1; }

            isReady = true;

            return 0;
        }
        public int OpenSubs(string url)
        {
            int ret;

            try
            {
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
                aDecoder.Priority = ThreadPriority.AboveNormal;
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
                vDecoder.Priority = ThreadPriority.AboveNormal;
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
                sDecoder.Priority = ThreadPriority.AboveNormal;
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
        public int Stop()
        {
            if (Pause() != 0) return -1;

            if (hasAudio) SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_AUDIO);
            if (hasVideo) SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_VIDEO);
            if (hasSubs)  SeekAccurate(0, AVMediaType.AVMEDIA_TYPE_SUBTITLE);

            return 0;
        }
        public int SeekAccurate(int ms, AVMediaType mType)
        {
            int ret = 0;
            if (!isReady) return -1;

            if (ms < 0) ms = 0;
            long calcTimestamp =(long) ms * 10000;
            Status oldStatus;
            
            switch (mType)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    calcTimestamp = (long) (calcTimestamp / aStreamInfo.timebaseLowTicks);
                    oldStatus = aStatus;
                    if (aDecoder != null && aDecoder.IsAlive) aDecoder.Abort();
                    aStatus = Status.SEEKING;

                    ret = ffmpeg.avformat_seek_file(aFmtCtx, aStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, ffmpeg.AVSEEK_FLAG_FRAME | ffmpeg.AVSEEK_FLAG_BACKWARD);
                    ffmpeg.avcodec_flush_buffers(aCodecCtx);
                    if (calcTimestamp * aStreamInfo.timebaseLowTicks > aStreamInfo.startTimeTicks ) ret = DecodeSilent(mType, (long)ms * 10000);

                    aStatus = oldStatus;
                    break;

                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    oldStatus = vStatus;

                    if (calcTimestamp > vStreamInfo.durationTicks ) { vStatus = oldStatus; break; }
                    if (calcTimestamp < vStreamInfo.startTimeTicks) calcTimestamp = vStreamInfo.startTimeTicks;
                    calcTimestamp = (long)(calcTimestamp / vStreamInfo.timebaseLowTicks);
                    
                    if (vDecoder != null && vDecoder.IsAlive) vDecoder.Abort();
                    vStatus = Status.SEEKING;

                    ret = ffmpeg.avformat_seek_file(vFmtCtx, vStream->index, Int64.MinValue, calcTimestamp, calcTimestamp, ffmpeg.AVSEEK_FLAG_FRAME | ffmpeg.AVSEEK_FLAG_BACKWARD);                    
                    ffmpeg.avcodec_flush_buffers(vCodecCtx);
                    if (calcTimestamp * vStreamInfo.timebaseLowTicks > vStreamInfo.startTimeTicks ) ret = DecodeSilent(mType, (long)ms * 10000);

                    vStatus = oldStatus;
                    break;

                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    oldStatus = sStatus;
                    if (sDecoder != null && sDecoder.IsAlive) sDecoder.Abort();
                    sStatus = Status.SEEKING;
                    calcTimestamp = (long)(calcTimestamp / 10000);

                    ret = ffmpeg.avformat_seek_file(sFmtCtx, sStream->index, Int64.MinValue, ms, Int64.MaxValue, ffmpeg.AVSEEK_FLAG_BACKWARD);
                    ffmpeg.avcodec_flush_buffers(sCodecCtx);

                    sStatus = oldStatus;
                    break;
            }

            if (ret != 0) { Log(" - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) + ", TS: " + calcTimestamp); return -1; }

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

        /* TODO: HWAccel get_format...
        private unsafe AVPixelFormat get_format(AVCodecContext* avctx, AVPixelFormat* pix_fmts)
        {
            while (*pix_fmts != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                Log("------ " + *pix_fmts);
                if (*pix_fmts == AVPixelFormat.AV_PIX_FMT_DXVA2_VLD) { return AVPixelFormat.AV_PIX_FMT_NV12; }
                pix_fmts++;
            }

            Log("Never asked by get_format()\n");

            return AVPixelFormat.AV_PIX_FMT_NONE;
        }

        private static AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
        {
            switch (hWDevice)
            {
                case AVHWDeviceType.AV_HWDEVICE_TYPE_NONE:
                    return AVPixelFormat.AV_PIX_FMT_NONE;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU:
                    return AVPixelFormat.AV_PIX_FMT_VDPAU;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA:
                    return AVPixelFormat.AV_PIX_FMT_CUDA;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI:
                    return AVPixelFormat.AV_PIX_FMT_VAAPI;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2:
                    return AVPixelFormat.AV_PIX_FMT_NV12;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_QSV:
                    return AVPixelFormat.AV_PIX_FMT_QSV;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX:
                    return AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA:
                    return AVPixelFormat.AV_PIX_FMT_NV12;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_DRM:
                    return AVPixelFormat.AV_PIX_FMT_DRM_PRIME;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL:
                    return AVPixelFormat.AV_PIX_FMT_OPENCL;
                case AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC:
                    return AVPixelFormat.AV_PIX_FMT_MEDIACODEC;
                default:
                    return AVPixelFormat.AV_PIX_FMT_NONE;
            }
        }*/

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
