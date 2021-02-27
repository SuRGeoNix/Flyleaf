using System;
using System.Collections.Generic;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;

using Device = SharpDX.Direct3D11.Device;

namespace SuRGeoNix.Flyleaf.MediaFramework
{
    public unsafe class VideoAcceleration
    {
        const int               AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        const AVHWDeviceType    HW_DEVICE       = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
        const AVPixelFormat     HW_PIX_FMT      = AVPixelFormat.AV_PIX_FMT_D3D11;

        public AVBufferRef*     hw_device_ctx;
        //public Device           device;

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
                Log($"[ERROR-1]{Utils.ErrorCodeToMsg(ret)} ({ret})");
                
                fixed(AVBufferRef** ptr = &hw_device_ctx) av_buffer_unref(ptr);
                hw_device_ctx = null;
                return ret;
            }

            return ret;
        }
        public VideoAcceleration()
        {

            //// Check if system has D3D11VA device
            //List<AVHWDeviceType> devices = GetHWDevices();

            //foreach (AVHWDeviceType device in devices)
            //    if (device == HW_DEVICE) { DeviceExists = true;  break; }

            //if (!DeviceExists) return;

            //// Create D3D11VA device
            //fixed (AVBufferRef** ptr = &hw_device_ctx)
            //    if (av_hwdevice_ctx_create(ptr, HW_DEVICE, "auto", null, 0) != 0) return;

            //// Parse AVD3D11Device to SharpDX
            //AVHWDeviceContext* hw_device_ctx3 = (AVHWDeviceContext*)hw_device_ctx->data;
            //AVD3D11VADeviceContext* hw_d3d11_dev_ctx = (AVD3D11VADeviceContext*)hw_device_ctx3->hwctx;
            //device = Device.FromPointer<Device>((IntPtr) hw_d3d11_dev_ctx->device);

            //DeviceCreated = true;







            // TEMPORARY TESTING
            //SwapChain   swapChain;
            
            //var desc = new SwapChainDescription()
            //{
            //    BufferCount         = 1,
            //    ModeDescription     = new ModeDescription(0, 0, new Rational(0, 0), Format.B8G8R8A8_UNorm), // BGRA | Required for Direct2D/DirectWrite (<Win8)
            //    IsWindowed          = true,
            //    OutputHandle        = tmpHandle,
            //    SampleDescription   = new SampleDescription(1, 0),
            //    SwapEffect          = SwapEffect.Discard,
            //    Usage               = Usage.RenderTargetOutput
            //};
            //Device.CreateWithSwapChain(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.Debug | DeviceCreationFlags.BgraSupport, desc, out renderDevice, out swapChain);

            // Check valid hw/sw formats
            /*
            AVHWFramesConstraints* hw_frames_const = av_hwdevice_get_hwframe_constraints(hw_device_ctx, null);
            for (AVPixelFo  rmat* p = hw_frames_const->valid_hw_formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
                Log((*p).ToString());

            Log("====");
            for (AVPixelFormat* p = hw_frames_const->valid_sw_formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
                Log((*p).ToString());

            av_hwframe_constraints_free(&hw_frames_const);*/


            


            //bool t1 = CodecSupported(avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264));
            //bool t2 = CodecSupported(avcodec_find_decoder(AVCodecID.AV_CODEC_ID_HEVC));
            //bool t3 = CodecSupported(avcodec_find_decoder(AVCodecID.AV_CODEC_ID_VP9));

            //AVCodec *codec = avcodec_find_decoder(AVCodecID.AV_CODEC_ID_VP9);
            //var tt2 = GetHWDevicesSupported(codec);
        }

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

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [HWAcceleration] {msg}"); }
    }
}
