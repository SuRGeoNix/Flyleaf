using System;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

using Vortice.Direct3D11;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public unsafe partial class Renderer
    {
        ID3D11Buffer psBuffer;
        PSBufferType psBufferData = new PSBufferType();
        float lastLumin;

        [StructLayout(LayoutKind.Sequential)]
        struct PSBufferType
        {
            public PSFormat format;
            public int coefsIndex;
            public HDRtoSDRMethod hdrmethod;

            public float brightness;
            public float contrast;

            public float g_luminance;
            public float g_toneP1;
            public float g_toneP2;

            public float texWidth;
        }
        enum PSFormat : int
        {
            RGB     = 1,
            Y_UV    = 2,
            Y_U_V   = 3,
            YUYV    = 4,
            UYUV    = 5,
        }
        public void UpdateHDRtoSDR(AVMasteringDisplayMetadata* displayData = null, bool updateResource = true)
        {
            if (VideoDecoder.VideoStream == null || VideoDecoder.VideoStream.ColorSpace != ColorSpace.BT2020) return;
            
            float lumin = displayData == null || displayData->has_luminance == 0 ? lastLumin : displayData->max_luminance.num / (float)displayData->max_luminance.den;
            lastLumin = lumin;

            psBufferData.hdrmethod = Config.Video.HDRtoSDRMethod;

            if (psBufferData.hdrmethod == HDRtoSDRMethod.Reinhard)
            {
                psBufferData.g_toneP1 = lastLumin > 0 ? (float)(Math.Log10(100) / Math.Log10(lastLumin)) : 0.72f;
                if (psBufferData.g_toneP1 < 0.1f || psBufferData.g_toneP1 > 5.0f)
                    psBufferData.g_toneP1 = 0.72f;
            }
            else if (psBufferData.hdrmethod == HDRtoSDRMethod.Aces)
            {
                psBufferData.g_luminance = lastLumin > 1 ? lastLumin : 400.0f;
                psBufferData.g_toneP1 = Config.Video.HDRtoSDRTone;
            }
            else if (psBufferData.hdrmethod == HDRtoSDRMethod.Hable)
            {
                psBufferData.g_luminance = lastLumin > 1 ? lastLumin : 400.0f;
                psBufferData.g_toneP1 = (10000.0f / psBufferData.g_luminance) * (2.0f / Config.Video.HDRtoSDRTone);
                psBufferData.g_toneP2 = psBufferData.g_luminance / (100.0f * Config.Video.HDRtoSDRTone);
            }

            context.UpdateSubresource(psBufferData, psBuffer);

            Present();


            /* TODO
             * 
             * https://github.com/xbmc/xbmc/blob/1d0dc77d43b4730f3b8708a84d931ce3c161d2d0/xbmc/cores/VideoPlayer/VideoRenderers/HwDecRender/DXVAHD.cpp
             * 
                var sc4 = swapChain.QueryInterface<IDXGISwapChain4>();
                var t1 = new HdrMetadataHdr10();
                t1.RedPrimary[0]   = 34000; // Display P3 primaries
                t1.RedPrimary[1]   = 16000;
                t1.GreenPrimary[0] = 13250;
                t1.GreenPrimary[1] = 34500;
                t1.BluePrimary[0]  = 7500;
                t1.BluePrimary[1]  = 3000;
                t1.WhitePoint[0]   = 15635;
                t1.WhitePoint[1]   = 16450;
                t1.MaxMasteringLuminance = 1000 * 10000; // 1000 nits
                t1.MinMasteringLuminance = 100;          // 0.01 nits

                sc4.SetHDRMetaData(HdrMetadataType.Hdr10, t1);
                sc4.Dispose();


                vc1.VideoProcessorSetStreamColorSpace1(vp, 0, ColorSpaceType.YcbcrStudioGhlgTopLeftP2020);
                vc1.VideoProcessorSetStreamColorSpace1(vp, 0, ColorSpaceType.YcbcrStudioG2084LeftP2020);

                // HDR output?
                pVideoContext1->VideoProcessorSetOutputColorSpace1(m_pVideoProcessor, DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020);
             * 
             */
        }
    }
}
