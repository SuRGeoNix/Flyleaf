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

        // 
        private void Initialize(IntPtr outputHandle)
        {
            // SwapChain Description
            var desc = new SwapChainDescription()
            {
                BufferCount         = 1,
                ModeDescription     = new ModeDescription(0, 0, new Rational(0, 0), Format.B8G8R8A8_UNorm),
                IsWindowed          = true,
                OutputHandle        = outputHandle,
                SampleDescription   = new SampleDescription(1, 0),
                SwapEffect          = SwapEffect.Discard,
                Usage               = Usage.RenderTargetOutput
            };

            // Create Device, SwapChain & BackBuffer
            Device.CreateWithSwapChain(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.Debug | DeviceCreationFlags.BgraSupport, desc, out _device, out _swapChain);
            _backBuffer     = Texture2D.FromSwapChain<Texture2D>(_swapChain, 0);

            // Ignore all windows events
            var factory = _swapChain.GetParent<Factory>();
            factory.MakeWindowAssociation(outputHandle, WindowAssociationFlags.IgnoreAll);

            // Prepare Video Processor Emulator | Input | Output | Stream
            videoDevice1    = _device.QueryInterface<VideoDevice1>();
            videoContext1   = _device.ImmediateContext.QueryInterface<VideoContext1>();

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
            
            vpivd = new VideoProcessorInputViewDescription()
            {
                FourCC = 0,
                Dimension = VpivDimension.Texture2D,
                Texture2D = new Texture2DVpiv() { MipSlice = 0, ArraySlice = 0 }
            };

            vpovd = new VideoProcessorOutputViewDescription() { Dimension = VpovDimension.Texture2D };

            videoDevice1.CreateVideoProcessorOutputView((Resource) _backBuffer, vpe, vpovd, out vpov);

            vpsa = new VideoProcessorStream[1];
        }

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
