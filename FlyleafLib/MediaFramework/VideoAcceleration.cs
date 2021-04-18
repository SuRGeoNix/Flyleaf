using System;
using System.Collections.Generic;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;

using Device = SharpDX.Direct3D11.Device;

namespace FlyleafLib.MediaFramework
{
    public unsafe class VideoAcceleration
    {
        const int               AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        const AVHWDeviceType    HW_DEVICE       = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
        const AVPixelFormat     HW_PIX_FMT      = AVPixelFormat.AV_PIX_FMT_D3D11;

        public AVBufferRef*     hw_device_ctx;

        public int Init(Device device)
        {
            int ret;

            if (hw_device_ctx != null) return -1;

            hw_device_ctx  = av_hwdevice_ctx_alloc(HW_DEVICE);

            AVHWDeviceContext* device_ctx = (AVHWDeviceContext*) hw_device_ctx->data;
            AVD3D11VADeviceContext* d3d11va_device_ctx = (AVD3D11VADeviceContext*) device_ctx->hwctx;
            d3d11va_device_ctx->device = (ID3D11Device*) device.NativePointer;

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
        public VideoAcceleration() { }

        public static bool CheckCodecSupport(AVCodec* codec)
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

        private List<AVHWDeviceType> GetHWDevices()
        {
            List<AVHWDeviceType> hwDevices  = new List<AVHWDeviceType>();
            AVHWDeviceType       type       = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

            while ( (type = av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE )
                hwDevices.Add(type);
            
            return hwDevices;
        }

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [HWAcceleration] {msg}"); }
    }
}