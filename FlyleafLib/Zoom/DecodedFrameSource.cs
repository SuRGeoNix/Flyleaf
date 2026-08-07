using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11VideoContext = Vortice.Direct3D11.ID3D11VideoContext;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;

namespace FlyleafLib.Zoom
{
    /// <summary>
    /// Provides access to decoded frames from the video decoder to the child renderer.
    /// </summary>
    internal unsafe class DecodedFrameSource : IDisposable
    {
        private readonly Renderer _parentRenderer;
        private readonly object _lock = new object();
        public IntPtr SharedTextureHandle { get; private set; }
        public bool HasValidFrame { get; private set; }

        /// <summary>SRV of the converted BGRA frame, on the player render device.</summary>
        public ID3D11ShaderResourceView FrameSrv { get; private set; }
        public int VideoWidth { get; private set; }
        public int VideoHeight { get; private set; }
        /// <summary>Raised (on the render thread) after a new frame has been converted.</summary>
        public event Action FrameReady;

        // D3D11
        private  ID3D11Device          _device;
        private  ID3D11DeviceContext   _context;
        private readonly VideoDecoder          _decoder;

        // Video Processor (HW path)
        private ID3D11VideoDevice              _videoDevice;
        private ID3D11VideoContext             _videoContext;
        private ID3D11VideoProcessorEnumerator _vpEnum;
        private ID3D11VideoProcessor           _videoProcessor;
        private ID3D11VideoProcessorOutputView _vpOutputView;

        // BGRA Target texture
        private ID3D11Texture2D  _convertedTex;
        private int              _convertedW, _convertedH;
        private bool             _vpReady;

        private bool _disposed;

        public DecodedFrameSource(Renderer renderer)
        {
            _parentRenderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

            _device = _parentRenderer.Device;
            _decoder = _parentRenderer.VideoDecoder;
            _context = _parentRenderer.DeviceContext;

            // VideoDevice/Context for HW conversion
            _videoDevice = _device.QueryInterface<ID3D11VideoDevice>();
            _videoContext = _context.QueryInterface<ID3D11VideoContext>();

            if (_decoder.Renderer is not Renderer)
                return;
            lock (_lock)
            {
                _decoder.Renderer.RenderChild += OnRenderFrame;
            }
        }
        public DecodedFrameSource(ID3D11Device device, VideoDecoder decoder)
        {
            _device  = device  ?? throw new ArgumentNullException(nameof(device));
            _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            _context = device.ImmediateContext;

            // VideoDevice/Context for HW conversion
            _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
            _videoContext = _context.QueryInterface<ID3D11VideoContext>();

            if (decoder.Renderer is not Renderer)
                return;
            _parentRenderer = decoder.Renderer;
            lock (_lock)
            {
                _parentRenderer.RenderChild += OnRenderFrame;
            }
        }
        public void OnRenderFrame(VideoFrame frame)
        {
            if(_disposed || frame == null) return;

            bool isHW = _decoder.VideoAccelerated && frame.VPIV != null;

            bool ok = isHW ? UpdateHW(frame) : UpdateSW(frame);
            if (ok)
                FrameReady?.Invoke();
        }

        private void CheckAndUpdateContext()
        {
            if (_device == null || _device != _parentRenderer.Device)
            {
                _device = _parentRenderer.Device;
                _context = _parentRenderer.DeviceContext;

                _vpOutputView?.Dispose();
                _vpOutputView = null;
                FrameSrv?.Dispose();
                FrameSrv = null;
                _convertedTex?.Dispose();
                _convertedTex = null;

                _videoProcessor?.Dispose();
                _videoProcessor = null;
                _vpEnum?.Dispose();
                _vpEnum = null;
                _vpReady = false;

                _videoDevice?.Dispose();
                _videoContext?.Dispose();

                // VideoDevice/Context for HW conversion
                _videoDevice = _device.QueryInterface<ID3D11VideoDevice>();
                _videoContext = _context.QueryInterface<ID3D11VideoContext>();
            }
        }
        // Hardware path: VPIV already finished → VideoProcessorBlt → BGRA
        private bool UpdateHW(VideoFrame frame)
        {
            // Coded resolution from the video stream
            int w = (int)(_decoder.VideoStream?.Width  ?? 0);
            int h = (int)(_decoder.VideoStream?.Height ?? 0);
            if (w == 0 || h == 0) return false;

            CheckAndUpdateContext();
            EnsureConvertedTex(w, h);
            if (!EnsureVideoProcessor(w, h)) return false;

            var stream = new VideoProcessorStream
            {
                Enable       = true,
                InputSurface = frame.VPIV
            };

            _videoContext.VideoProcessorBlt(
                _videoProcessor, _vpOutputView, 0, 1, new[] { stream });

            RefreshSrv();
            HasValidFrame = true;
            return true;
        }

        // SW path: SRV[0] is BGRA → CopyResource to own texture
        private bool UpdateSW(VideoFrame frame)
        {
            if (frame.SRV == null || frame.SRV.Length == 0 || frame.SRV[0] == null)
                return false;
            CheckAndUpdateContext();
            // Extract texture from SRV[0]
            var resource = frame.SRV[0].Resource;
            if (resource == null) return false;

            using (resource)
            {
                var srcTex = resource.QueryInterface<ID3D11Texture2D>();
                if (srcTex == null) return false;

                using (srcTex)
                {
                    var desc = srcTex.Description;

                    // Ensure that the format is BGRA (SW frames in FlyleafLib)
                    if (desc.Format != Format.B8G8R8A8_UNorm
                        && desc.Format != Format.B8G8R8A8_UNorm_SRgb)
                        return false;

                    EnsureConvertedTex((int)desc.Width, (int)desc.Height);
                    if (_convertedTex == null) return false;

                    _context.CopyResource(_convertedTex, srcTex);
                }
            }

            RefreshSrv();
            HasValidFrame = true;
            return true;
        }

        // Ensure BGRA target texture
        private void EnsureConvertedTex(int w, int h)
        {
            if (_convertedTex != null && _convertedW == w && _convertedH == h) return;

            _vpOutputView?.Dispose();   _vpOutputView   = null;
            FrameSrv?.Dispose();        FrameSrv        = null;
            _convertedTex?.Dispose();   _convertedTex   = null;
            _vpReady = false;

            _convertedTex = _device.CreateTexture2D(new Texture2DDescription
            {
                Width             = (uint)w,
                Height            = (uint)h,
                MipLevels         = 1,
                ArraySize         = 1,
                Format            = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage             = ResourceUsage.Default,
                // RenderTarget: required for VideoProcessorBlt output
                // ShaderResource: for Overview-Shader
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });

            // SRV consumed in-process by the overview shader (same render device).
            FrameSrv = _device.CreateShaderResourceView(_convertedTex, new ShaderResourceViewDescription
            {
                Format = Format.B8G8R8A8_UNorm,
                ViewDimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = new Texture2DShaderResourceView { MipLevels = 1 }
            });

            _convertedW = w;
            _convertedH = h;
            VideoWidth = w;
            VideoHeight = h;
        }

        // Setting up the D3D11 Video Processor
        private bool EnsureVideoProcessor(int outW, int outH)
        {
            if (_vpReady && _vpOutputView != null) return true;

            _vpOutputView?.Dispose();   _vpOutputView   = null;
            _videoProcessor?.Dispose(); _videoProcessor = null;
            _vpEnum?.Dispose();         _vpEnum         = null;
            _vpReady = false;

            // Content description: Input size = Output size (no scaling)
            // FlyleafLib has already configured the VideoProcessor for the correct size.
            // We need to use the same size.
            var contentDesc = new VideoProcessorContentDescription
            {
                Usage            = VideoUsage.PlaybackNormal,
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputWidth       = (uint)outW,
                InputHeight      = (uint)outH,
                OutputWidth      = (uint)outW,
                OutputHeight     = (uint)outH,
                InputFrameRate   = new Rational(60, 1),
                OutputFrameRate  = new Rational(60, 1)
            };

            if (_videoDevice.CreateVideoProcessorEnumerator(
                    contentDesc, out _vpEnum).Failure)
                return false;

            if (_videoDevice.CreateVideoProcessor(
                    _vpEnum, 0, out _videoProcessor).Failure)
                return false;

            if (_convertedTex == null) return false;

            // OutputView on _convertedTex (BGRA)
            var ovDesc = new VideoProcessorOutputViewDescription
            {
                ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                Texture2D     = new Texture2DVideoProcessorOutputView { MipSlice = 0 }
            };

            if (_videoDevice.CreateVideoProcessorOutputView(
                    _convertedTex, _vpEnum, ovDesc, out _vpOutputView).Failure)
                return false;

            _vpReady = true;
            return true;
        }

        // Update SRV to _convertedTex
        private void RefreshSrv()
        {
            if (_convertedTex == null) return;

            try
            {
                using var res = _convertedTex.QueryInterface<IDXGIResource1>();
                IntPtr handle = res.SharedHandle;

                SharedTextureHandle = handle;

            }
            catch { /* silently skip if not D3D11.1 */ }
        }
        private void DisposeLocal()
        {
            if (_decoder is null) return;

            try
            {
                lock (_lock)
                {
                    _parentRenderer.RenderChild -= OnRenderFrame;
                }

            }
            catch { }
        }
        // IDisposable
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            DisposeLocal();

            _vpOutputView?.Dispose();
            _videoProcessor?.Dispose();
            _vpEnum?.Dispose();
            _videoContext?.Dispose();
            _videoDevice?.Dispose();
            FrameSrv?.Dispose();
            _convertedTex?.Dispose();
        }
    }
}
