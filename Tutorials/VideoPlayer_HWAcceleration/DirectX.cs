/* 
 * C# Video Demuxing | GPU Decoding & Processing Acceleration Tutorial
 * (Based on FFmpeg.Autogen bindings for FFmpeg & SharpDX bindings for DirectX)
 *                                           By John Stamatakis (aka SuRGeoNix)
 *
 * Implementing GPU Video Processing (D3D11VideoContext::VideoProcessorBlt) based on SharpDX
 * https://docs.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11videocontext-videoprocessorblt
 */

using System;

using SharpDX.DXGI;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;

using Device    = SharpDX.Direct3D11.Device;
using Resource  = SharpDX.Direct3D11.Resource;
using SharpDX;

namespace VideoPlayer_HWAcceleration
{
    class DirectX
    {
        #region Declaration
        Device                              _device;
        SwapChain                           _swapChain;

        Texture2D                           _backBuffer;

        VideoDevice1                        videoDevice1;
        VideoProcessor                      videoProcessor;
        VideoContext1                       videoContext1;
        VideoProcessorEnumerator vpe;
        VideoProcessorContentDescription    vpcd;
        VideoProcessorOutputViewDescription vpovd;
        VideoProcessorInputViewDescription  vpivd;
        VideoProcessorInputView             vpiv;
        VideoProcessorOutputView            vpov;
        VideoProcessorStream[]              vpsa;

        public DirectX(IntPtr outputHandle) { Initialize(outputHandle); }
        #endregion

        // Get's a handle to assosiate with the BackBuffer and Prepares Devices
        private void Initialize(IntPtr outputHandle)
        {
            // SwapChain Description
            var desc = new SwapChainDescription()
            {
                BufferCount         = 1,
                ModeDescription     = new ModeDescription(0, 0, new Rational(0, 0), Format.B8G8R8A8_UNorm), // RBGA | BGRA 32-bit
                IsWindowed          = true,
                OutputHandle        = outputHandle,
                SampleDescription   = new SampleDescription(1, 0),
                SwapEffect          = SwapEffect.Discard,
                Usage               = Usage.RenderTargetOutput
            };

            // Create Device, SwapChain & BackBuffer
            Device.CreateWithSwapChain(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.Debug | DeviceCreationFlags.BgraSupport, desc, out _device, out _swapChain);
            _backBuffer     = Texture2D.FromSwapChain<Texture2D>(_swapChain, 0);

            // Creates Association between outputHandle and BackBuffer
            var factory = _swapChain.GetParent<Factory>();
            factory.MakeWindowAssociation(outputHandle, WindowAssociationFlags.IgnoreAll);

            // Video Device | Video Context
            videoDevice1    = _device.QueryInterface<VideoDevice1>();
            videoContext1   = _device.ImmediateContext.QueryInterface<VideoContext1>();

            // Creates Video Processor Enumerator
            vpcd = new VideoProcessorContentDescription()
            {
                Usage = VideoUsage.PlaybackNormal,
                InputFrameFormat = VideoFrameFormat.Progressive,

                InputFrameRate = new Rational(1, 1),
                OutputFrameRate = new Rational(1, 1),

                // We Set those later
                InputWidth = 1,
                OutputWidth = 1,
                InputHeight = 1,
                OutputHeight = 1
            };
            videoDevice1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);
            videoDevice1.CreateVideoProcessor(vpe, 0, out videoProcessor);
            
            // Prepares Video Processor Input View Description for Video Processor Input View that we pass Shared NV12 Texture (nv12SharedResource) each time
            vpivd = new VideoProcessorInputViewDescription()
            {
                FourCC = 0,
                Dimension = VpivDimension.Texture2D,
                Texture2D = new Texture2DVpiv() { MipSlice = 0, ArraySlice = 0 }
            };

            // Creates Video Processor Output to our BackBuffer
            vpovd = new VideoProcessorOutputViewDescription() { Dimension = VpovDimension.Texture2D };
            videoDevice1.CreateVideoProcessorOutputView((Resource) _backBuffer, vpe, vpovd, out vpov);

            // Prepares Streams Array
            vpsa = new VideoProcessorStream[1];
        }

        /* ID3D11VideoContext::VideoProcessorBlt | https://docs.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11videocontext-videoprocessorblt
         * 
         * HRESULT VideoProcessorBlt (
         * ID3D11VideoProcessor               *pVideoProcessor,
         * ID3D11VideoProcessorOutputView     *pView,
         * UINT                               OutputFrame,
         * UINT                               StreamCount,
         * const D3D11_VIDEO_PROCESSOR_STREAM *pStreams );
         * 
         * 1. Opens Shared NV12 Texture (nv12SharedResource) on our SharpDX ID3Device from FFmpeg's ID3Device
         * 2. Creates a new Video Processor Input View that we pass in Video Processor Streams
         * 3. Calls Video Processor Blt to convert (in GPU) Shared NV12 Texture to our BackBuffer RBGA/BGRA Texture
         * 4. Finally Presents the Frame to the outputHandle (SampleUI Form)
         */
        public void PresentFrame(IntPtr nv12SharedResource)
        {
            Texture2D nv12SharedTexture = _device.OpenSharedResource<Texture2D>(nv12SharedResource);

            videoDevice1.CreateVideoProcessorInputView(nv12SharedTexture, vpe, vpivd, out vpiv);
            VideoProcessorStream vps = new VideoProcessorStream()
            {
                PInputSurface = vpiv,
                Enable = new RawBool(true)
            };
            vpsa[0] = vps;
            videoContext1.VideoProcessorBlt(videoProcessor, vpov, 0, 1, vpsa);

            _swapChain.Present(0, PresentFlags.None);

            Utilities.Dispose(ref vpiv);
            Utilities.Dispose(ref nv12SharedTexture);
        }
    }
}
