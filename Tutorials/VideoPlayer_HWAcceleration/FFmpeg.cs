/* 
 * C# Video Demuxing | GPU Decoding & Processing Acceleration Tutorial
 * (Based on FFmpeg.Autogen bindings for FFmpeg & SharpDX bindings for DirectX)
 *                                           By John Stamatakis (aka SuRGeoNix)
 *
 * Implementing Video Demuxing & GPU Decoding Accelration based on FFmpeg
 */

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Security;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.AVPixelFormat;

using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace VideoPlayer_HWAcceleration
{
    public unsafe class FFmpeg
    {
        #region Declaration

        // FFmpeg Basic Setup
        AVFormatContext*        fmtCtx;
        AVCodecContext*         vCodecCtx;
        AVStream*               vStream;

        // HW Acceleration
        const int               AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        const AVHWDeviceType    HW_DEVICE       = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
        const AVPixelFormat     HW_PIX_FMT      = AVPixelFormat.AV_PIX_FMT_D3D11;

        Device                  device;             // Direct3D 11 Device (We use it for HW Rendering and FFmpeg for HW Decoding)
        AVBufferRef*            hw_device_ctx;      // FFmpeg's HW Device Context (To setup our video codec while opening it)
        Texture2DDescription    textDescHW;         // HW Texture2D Description
        Texture2D               textureHW;          // HW Texture2D
        Texture2D               textureFFmpeg;      // HW Texture2D Array (FFmpeg's pool)

        public FFmpeg()
        {
            RegisterFFmpegBinaries();
            av_log_set_level(ffmpeg.AV_LOG_ERROR);
        }

        #endregion

        // Creates FFmpeg's HW Device Context based on our rendering Direct3D 11 Device
        public bool InitHWAccel(Device device)
        {
            int ret;

            if (hw_device_ctx != null) return false;

            hw_device_ctx  = av_hwdevice_ctx_alloc(HW_DEVICE);

            AVHWDeviceContext* device_ctx = (AVHWDeviceContext*) hw_device_ctx->data;
            AVD3D11VADeviceContext* d3d11va_device_ctx = (AVD3D11VADeviceContext*) device_ctx->hwctx;
            d3d11va_device_ctx->device = (ID3D11Device*) device.NativePointer;

            ret = av_hwdevice_ctx_init(hw_device_ctx);
            if (ret != 0)
            {
                Log($"[ERROR-1]{ErrorCodeToMsg(ret)} ({ret})");
                
                fixed(AVBufferRef** ptr = &hw_device_ctx) av_buffer_unref(ptr);
                hw_device_ctx = null;
                return false;
            }

            this.device = device;
            return true;
        }

        // Ensures that the current Video Codec is supported from our GPU (for HW Decoding)
        private bool CheckHWAccelCodecSupport(AVCodec* codec)
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

        /* 1. Format Context Open & Setup
         * 2. Streams Setup
         * 3. Codecs Setup
         * 4. HWDevice Setup (FFmpeg will do the GPU Video Decoding)
         */
        public bool Open(string url)
        {
            int ret = -1;

            Initialize();

            // Format Context Open & Setup
            fmtCtx = avformat_alloc_context();
            if (fmtCtx == null) return false;

            AVFormatContext* fmtCtxPtr = fmtCtx;
            ret = avformat_open_input(&fmtCtxPtr, url, null, null);
            if (ret < 0) { OpenFailed(ret, false); return false; }

            // Video Stream Setup
            ret = avformat_find_stream_info(fmtCtx, null);
            if (ret < 0) { OpenFailed(ret); return false; }

            ret = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_VIDEO,   -1, -1, null, 0);
            if (ret < 0) { OpenFailed(ret); return false; }
            vStream = fmtCtx->streams[ret];

            // We use only Video for this Project (Discards All Streams except Video)
            for (int i=0; i<fmtCtx->nb_streams; i++)
                if (i != vStream->index) fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

            SetupVideo();

            return true;
        }
        private bool SetupVideo()
        {
            int ret;

            AVCodec* codec = avcodec_find_decoder(vStream->codecpar->codec_id);
            if (codec == null)
                { Log($"[CodecOpen] [ERROR-1] No suitable codec found"); return false; }

            if (!CheckHWAccelCodecSupport(codec))
                { Log($"[CodecOpen] [ERROR-1] HW Acceleration not supported for this codec"); return false; }

            vCodecCtx = avcodec_alloc_context3(null);
            if (vCodecCtx == null)
                { Log($"[CodecOpen] [ERROR-2] Failed to allocate context3"); return false; }

            ret = avcodec_parameters_to_context(vCodecCtx, vStream->codecpar);
            if (ret < 0)
                { Log($"[CodecOpen] [ERROR-3] {ErrorCodeToMsg(ret)} ({ret})"); return false; }

            vCodecCtx->pkt_timebase  = vStream->time_base;
            vCodecCtx->codec_id      = codec->id;

            vCodecCtx->thread_count = Math.Min(Environment.ProcessorCount, vCodecCtx->codec_id == AVCodecID.AV_CODEC_ID_HEVC ? 32 : 16);
            vCodecCtx->thread_type  = 0;
            vCodecCtx->thread_safe_callbacks = 1;

            vCodecCtx->hw_device_ctx = av_buffer_ref(hw_device_ctx);

            textDescHW = new Texture2DDescription()
            {
	            Usage               = ResourceUsage.Default,

                Width               = vCodecCtx->width,
                Height              = vCodecCtx->height,

                BindFlags           = BindFlags.Decoder,
	            CpuAccessFlags      = CpuAccessFlags.None,
	            OptionFlags         = ResourceOptionFlags.None,

	            SampleDescription   = new SampleDescription(1, 0),
	            ArraySize           = 1,
	            MipLevels           = 1
            };

            ret = avcodec_open2(vCodecCtx, codec, null);
            if (ret == 0) return true;

            return false;
        }

        // Demuxing and HW Decoding
        private AVFrame* DecodeFrame(out int ret)
        {
            ret = 0;

            AVPacket* avpacket  = av_packet_alloc();
            AVFrame*  avframe   = av_frame_alloc();
            av_init_packet(avpacket);

            ret = av_read_frame(fmtCtx, avpacket);
            if (ret < 0)
            {
                if (ret == AVERROR_EOF || avio_feof(fmtCtx->pb) != 0)
                {
                    // EOF Handling & Draining
                    av_packet_unref(avpacket);
                    return null;
                }

                av_packet_unref(avpacket); ret = -1; return null;
            }

            if (avpacket->stream_index == vStream->index)
            {
                ret = avcodec_send_packet(vCodecCtx, avpacket);
                if (ret != 0)
                    { av_packet_unref(avpacket); ret = 0; return null; }

                ret = avcodec_receive_frame(vCodecCtx, avframe);
                if (ret == AVERROR_EOF || ret == AVERROR(EAGAIN)) { av_packet_unref(avpacket); ret = 0; return null; }
                if (ret != 0) { if (avframe != null) av_frame_free(&avframe);
                    av_packet_unref(avpacket); ret = 0; return null; }
            }

            av_packet_unref(avpacket);
            return avframe;
        }

        /* FFmpeg HW Decode and return Texture2D for rendering
         * 
         * FFmpeg source code -> https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hwcontext_d3d11va.c
         *     frame->data[0] = (uint8_t *)desc->texture;
         *     frame->data[1] = (uint8_t *)desc->index;
         *     
         * 1. Casting ID3D11Texture2D (d3d11.h) to Texture2D (SharpDX.Direct3D11) from avframe->data.ToArray()[0]
         * 2. Creating new Texture2D based on FFmpeg TextureDescription (eg. NV12 width/height etc)
         * 3. Copy Subresource from FFmpeg's Array Texture to our Texture and return it
         */
        public Texture2D GetFrame()
        {
            AVFrame* avframe;
            int ret = 0;
            do
            {
                // Demux & Decode Video Frame (FFmpeg HW decodes with the supplied threads)
                avframe = DecodeFrame(out ret);

                if (avframe != null)
                {
                    if (avframe->best_effort_timestamp != AV_NOPTS_VALUE)
                    {
                        textureFFmpeg       = new Texture2D((IntPtr) avframe->data.ToArray()[0]);
                        textDescHW.Format   = textureFFmpeg.Description.Format;
                        textureHW           = new Texture2D(device, textDescHW);
                        
                        //lock (device)
                        device.ImmediateContext.CopySubresourceRegion(textureFFmpeg, (int) avframe->data.ToArray()[1], new ResourceRegion(0,0,0,textureHW.Description.Width,textureHW.Description.Height,1), textureHW,0);

                        av_frame_free(&avframe);

                        return textureHW;
                    }
                }

            } while (ret != -1);

            return null;
        }

        #region Misc
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void Initialize()
        {
            AVCodecContext* codecCtxPtr;
            try
            {
                if (vStream != null)
                {
                    codecCtxPtr = vCodecCtx;
                    avcodec_free_context(&codecCtxPtr);
                }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }

            try
            {
                if (fmtCtx != null)
                {
                    AVFormatContext* fmtCtxPtr = fmtCtx;
                    avformat_close_input(&fmtCtxPtr);
                }
                fmtCtx  = null;
                vStream = null;
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }
        }
        private void OpenFailed(int ret, bool opened = true)
        {
            Log(ErrorCodeToMsg(ret));
            AVFormatContext* fmtCtxPtr = fmtCtx;
            if (fmtCtx != null && opened) avformat_close_input(&fmtCtxPtr);
            av_freep(fmtCtx);
        }
        private static string ErrorCodeToMsg(int error)
        {
            byte* buffer = stackalloc byte[1024];
            ffmpeg.av_strerror(error, buffer, 1024);
            return Marshal.PtrToStringAnsi((IntPtr)buffer);
        }

        public static bool alreadyRegister = false;
        private void RegisterFFmpegBinaries()
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
                    RootPath = ffmpegBinaryPath;
                    uint ver = ffmpeg.avformat_version();
                    Log($"[Version: {ver >> 16}.{ver >> 8 & 255}.{ver & 255}] [Location: {ffmpegBinaryPath}]");

                    return;
                }
                current = Directory.GetParent(current)?.FullName;
            }
        }
        private void Log(string msg) { Console.WriteLine($"[FFMPEG] {msg}"); }
        #endregion
    }
}