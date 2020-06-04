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

namespace VideoPlayer_HWAcceleration
{
    public unsafe class FFmpeg
    {
        #region Declaration
        int verbosity;

        // FFmpeg Setup
        AVFormatContext*        fmtCtx;
        AVCodecContext*         aCodecCtx,  vCodecCtx,  sCodecCtx;
        AVStream*               aStream,    vStream,    sStream;
        AVCodec*                vCodec;

        // HW Acceleration
        Texture2D               nv12SharedTexture;
        public bool             hwAccelStatus;

        public FFmpeg(int verbosity = 0)
        {
            RegisterFFmpegBinaries();

            this.verbosity = verbosity;
            av_log_set_level(ffmpeg.AV_LOG_ERROR);
        }

        #endregion

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

            // Streams Setup
            ret = avformat_find_stream_info(fmtCtx, null);
            if (ret < 0) { OpenFailed(ret); return false; }

            ret = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_VIDEO,   -1, -1, null, 0);
            if (ret < 0) { OpenFailed(ret); return false; }
            vStream = fmtCtx->streams[ret];

            ret = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_AUDIO,   -1, vStream->index, null, 0);
            if (ret > 0) aStream = fmtCtx->streams[ret];

            ret = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_SUBTITLE,-1, aStream != null ? aStream->index : vStream->index, null, 0);
            if (ret > 0) sStream = fmtCtx->streams[ret];

            // Codecs Setup
            ret = SetupCodec(vStream->index);
            if (ret < 0) { OpenFailed(ret); return false; }

            if (aStream != null)
            {
                ret = SetupCodec(aStream->index);
                if (ret < 0) { OpenFailed(ret); return false; }
            }

            if (sStream != null)
            {
                ret = SetupCodec(sStream->index);
                if (ret < 0) ErrorCodeToMsg(ret);
            }

            // We use only Video for this Project (Discards All Streams except Video)
            for (int i=0; i<fmtCtx->nb_streams; i++)
                if ( i != vStream->index ) fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;


            // HWDevice Setup (FFmpeg will do the GPU Decoding)
            AVBufferRef* hw_device_ctx;
            ret = av_hwdevice_ctx_create(&hw_device_ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, "auto", null, 0);
            if ( ret != 0 ) { OpenFailed(ret); return false; } else hwAccelStatus = true;
            vCodecCtx->hw_device_ctx = av_buffer_ref(hw_device_ctx);
            av_buffer_unref(&hw_device_ctx);

            return true;
        }

        // Codec Setup for Stream
        private int SetupCodec(int streamId)
        {
            int ret = 0;

            if ( streamId == 0)
            {
                vCodecCtx   = vStream->codec;
                vCodec      = ffmpeg.avcodec_find_decoder(vStream->codec->codec_id);
                ret = ffmpeg.avcodec_open2(vCodecCtx, vCodec, null);
                return 0;
            }

            AVCodecContext* codecCtx;
            codecCtx = avcodec_alloc_context3(null);
            if (codecCtx == null) return -1;

            ret = avcodec_parameters_to_context(codecCtx, fmtCtx->streams[streamId]->codecpar);
            if (ret < 0) { avcodec_free_context(&codecCtx); return ret; }

            codecCtx->pkt_timebase = fmtCtx->streams[streamId]->time_base;

            AVCodec *codec = avcodec_find_decoder(codecCtx->codec_id);
            if (codec == null) { avcodec_free_context(&codecCtx); return -1; }
            codecCtx->codec_id = codec->id;

            AVDictionary *opt = null;

            if (codecCtx->codec_type == AVMEDIA_TYPE_AUDIO || codecCtx->codec_type == AVMEDIA_TYPE_VIDEO)
                av_dict_set(&opt, "refcounted_frames", "0", 0); // Probably Deprecated

            ret = avcodec_open2(codecCtx, codec, &opt);
            av_dict_free(&opt);
            if (ret < 0) { avcodec_free_context(&codecCtx); return ret; }

            fmtCtx->streams[streamId]->discard = AVDiscard.AVDISCARD_DEFAULT;

            switch (codecCtx->codec_type)
            {
                case AVMEDIA_TYPE_AUDIO:
                    aCodecCtx = codecCtx;
                    break;
                case AVMEDIA_TYPE_VIDEO:
                    vCodecCtx = codecCtx;
                    break;
                case AVMEDIA_TYPE_SUBTITLE:
                    sCodecCtx = codecCtx;
                    break;
            }

            return 0;
        }
        
        // Video Decoding (GPU)
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
                if (ret != 0) { 
                    av_packet_unref(avpacket); ret = 0; return null; }

                ret = avcodec_receive_frame(vCodecCtx, avframe);
                if (ret == AVERROR_EOF || ret == AVERROR(EAGAIN)) { av_packet_unref(avpacket); ret = 0; return null; }
                if (ret != 0) { if (avframe != null) av_frame_free(&avframe);
                    av_packet_unref(avpacket); ret = 0; return null; }
            }

            av_packet_unref(avpacket);
            return avframe;
        }

        /* We will do the GPU Video Processing (Convert NV12 to RGBA)
         * 
         * FFmpeg source code -> https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/hwcontext_d3d11va.c
         *     frame->data[0] = (uint8_t *)desc->texture;
         *     frame->data[1] = (uint8_t *)desc->index;
         *     
         * 1. Casting ID3D11Texture2D (d3d11.h) to Texture2D (SharpDX.Direct3D11) from avframe->data.ToArray()[0]
         * 2. Subresource Array Index from avframe->data.ToArray()[1]
         * 3. Creates a Shared Texture Copy by using FFmpeg ID3Device (nv12Texture.Device) so we can use it from our SharpDX.Direct3D11.Device later on
         * 4. Returns Shared Texture's Handle
        */
        public IntPtr GetFrame()
        {
            AVFrame* avframe;
            int ret = 0;
            do
            {
                // Decode Video Frame
                avframe = DecodeFrame(out ret);

                // Return If we get an Actual Video Frame
                if ( avframe != null )
                {
                    if ( avframe->best_effort_timestamp != AV_NOPTS_VALUE)
                    {
                        // Casting ID3D11Texture2D (d3d11.h) to Texture2D (SharpDX.Direct3D11) from avframe->data.ToArray()[0]
                        Texture2D nv12Texture = new Texture2D((IntPtr) avframe->data.ToArray()[0]);

                        // First Time Setup (for Height/Width) | Could be done while opening and decoding a single frame
                        if (nv12SharedTexture == null)
                        {
                            // We expect AV_PIX_FMT_D3D11 Hardware Surface Format (NV12 | P010 not implemented)
                            if ( avframe->format != (int) vCodecCtx->pix_fmt ) { hwAccelStatus = false; return IntPtr.Zero; }

                            nv12SharedTexture =  new Texture2D(nv12Texture.Device, new Texture2DDescription()
                            {
                                Usage               = ResourceUsage.Default,
                                Format              = Format.NV12,

                                Width               = nv12Texture.Description.Width,
                                Height              = nv12Texture.Description.Height,

                                BindFlags           = BindFlags.ShaderResource | BindFlags.RenderTarget,
                                CpuAccessFlags      = CpuAccessFlags.None,
                                OptionFlags         = ResourceOptionFlags.Shared,

                                SampleDescription   = new SampleDescription(1, 0),
                                ArraySize           = 1,
                                MipLevels           = 1
                            });
                        }

                        // Creates a Shared Texture Copy by using FFmpeg ID3Device (nv12Texture.Device) so we can use it from our SharpDX.Direct3D11.Device later on
                        nv12Texture.Device.ImmediateContext.CopySubresourceRegion(nv12Texture, (int)avframe->data.ToArray()[1], null, nv12SharedTexture, 0);
                        var nv12SharedResource = nv12SharedTexture.QueryInterface<SharpDX.DXGI.Resource>();

                        av_frame_free(&avframe);

                        // Returns Shared Texture's Handle
                        return nv12SharedResource.SharedHandle;
                    }
                }

                if (avframe != null) av_frame_free(&avframe);

            } while (ret != -1);

            return IntPtr.Zero;
        }

        #region Misc
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void Initialize()
        {
            AVCodecContext* codecCtxPtr;
            try
            {
                if (aStream != null)
                {
                    codecCtxPtr = aCodecCtx;
                    avcodec_free_context( &codecCtxPtr);
                }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }

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
                if (sStream != null)
                {
                    codecCtxPtr = sCodecCtx;
                    avcodec_free_context(&codecCtxPtr);
                }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Msg: " + e.Message + "\n" + e.StackTrace); }

            try
            {
                if ( fmtCtx != null)
                {
                    AVFormatContext* fmtCtxPtr = fmtCtx;
                    avformat_close_input(&fmtCtxPtr);
                }
                fmtCtx  = null; aStream = null; vStream = null; sStream = null;
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
        internal static void RegisterFFmpegBinaries()
        {
            var current = Environment.CurrentDirectory;
            var probe = Environment.Is64BitProcess ? "x64" : "x86";
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
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[FFMPEG] {msg}"); }
        #endregion
    }
}