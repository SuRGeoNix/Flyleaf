using FlyleafLib.Custom;
using FlyleafLib.MediaPlayer;
using System;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using MapFlags = Vortice.Direct3D11.MapFlags;
using Format = Vortice.DXGI.Format;

namespace FlyleafLib.Zoom
{
    /// <summary>
    /// Zoom-Overview Renderer. Runs entirely on the player's render device: samples
    /// the decoded frame (via <see cref="DecodedFrameSource"/>, same device — no
    /// cross-adapter shared handle) and draws the minimap into an owned render-target
    /// texture. A presenter delivers that texture to WPF (GPU or software).
    /// </summary>
    public sealed class ZoomOverviewRenderer : IDisposable
    {
        public bool IsInitialized { get; private set; }
        public Viewport Viewport { get; private set; }
        public int ControlWidth { get; private set; }
        public int ControlHeight { get; private set; }
        public bool ShowZoomBox { get => _showZoomBox; set => _showZoomBox = value; }
        private bool _showZoomBox;
        public int SideXPixels { get; private set; }
        public int SideYPixels { get; private set; }

        public Action VideoViewSizeChanged;

        /// <summary>Raised (render thread) when a new decoded frame is available to draw.</summary>
        public event Action FrameReady;

        // D3D11 pipeline (render device)
        private ID3D11Device _device;
        private ID3D11DeviceContext _context;
        private DecodedFrameSource _frameSource;

        // Minimap output (render device)
        private ID3D11Texture2D _minimapTex;
        private ID3D11RenderTargetView _minimapRtv;
        private int _minimapWidth;
        private int _minimapHeight;

        private ID3D11VertexShader _vertexShader;
        private ID3D11PixelShader _pixelShader;
        private ID3D11PixelShader _pixelShaderWithZoomBox;
        private ID3D11Buffer _cbViewport;
        private ID3D11SamplerState _sampler;
        private ID3D11RasterizerState _rasterizer;
        private ID3D11BlendState _blend;

        private readonly Player _player;
        private bool _disposed;
        private int _videoWidth;
        private int _videoHeight;
        private readonly object _lockRecreatedResources = new();
        internal LogHandler Log;

        // cbuffer (32 bytes)
        [StructLayout(LayoutKind.Sequential, Size = 32)]
        private struct CbViewport
        {
            public float ViewX, ViewY, ViewW, ViewH;  // UV-Rect of the viewport
            public float MapW, MapH;                   // Minimap pixel size
            public float _pad0, _pad1;
        }

        private const string VSSrc = @"
struct VSOut { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
VSOut main(uint id:SV_VertexID)
{
    float2 uv  = float2((id<<1)&2, id&2);
    float4 pos = float4(uv*float2(2,-2)+float2(-1,1), 0, 1);
    VSOut o; o.pos=pos; o.uv=uv; return o;
}";

        private const string PSSrc = @"
Texture2D    src : register(t0);
SamplerState sam : register(s0);
cbuffer CB : register(b0)
{
    float4 viewRect;   // x y w h  in UV [0..1]  — current zoom viewport
    float2 mapSize;    // Minimap pixel size for frame thickness
    float2 _pad;
};
struct PSIn { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
float4 main(PSIn i) : SV_TARGET
{
    float4 c  = src.Sample(sam, i.uv);
    float2 uv = i.uv;

    bool inV = uv.x >= viewRect.x && uv.x <= (viewRect.x + viewRect.z)
            && uv.y >= viewRect.y && uv.y <= (viewRect.y + viewRect.w);

    float bw = 2.5 / mapSize.x;
    float bh = 2.5 / mapSize.y;
    bool border = inV && (uv.x < viewRect.x + bw
                       || uv.x > viewRect.x + viewRect.z - bw
                       || uv.y < viewRect.y + bh
                       || uv.y > viewRect.y + viewRect.w - bh);

    float3 res = border ? float3(0.0, 0.56, 0.81)   // Blue frame
               : inV   ? c.rgb                       // visible area
                        : c.rgb * 0.40;              // hidden area
    return float4(res, 1.0);
}";

        private const string PSSrcSimple = @"
Texture2D    src : register(t0);
SamplerState sam : register(s0);
struct PSIn { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
float4 main(PSIn i) : SV_TARGET
{
    return src.Sample(sam, i.uv);
}";

        public ZoomOverviewRenderer(Player player, int miniWidth = 256, int miniHeight = 144)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            ControlWidth = miniWidth;
            ControlHeight = miniHeight;

            var uniqueId = GetUniqueId();
            Log = new(("[#" + uniqueId + "]").PadRight(8, ' ') + " [ZOVRenderer    ] ");
        }

        /// <summary>Initializes the D3D11 pipeline + frame source on the player render device.</summary>
        public void Initialize()
        {
            if (IsInitialized || _disposed)
                return;

            _device = _player.Renderer.Device;
            _context = _player.Renderer.DeviceContext;
            if (_device is null)
                return;

            CompileShaders();
            CreateConstantBuffer();
            CreateSamplerAndStates();

            _frameSource = new DecodedFrameSource(_player.Renderer);
            _frameSource.FrameReady += OnFrameReady;

            IsInitialized = true;

            if (_player is ICustomPlayer custom)
                custom.OverviewRenderer = this;
        }

        private void OnFrameReady() => FrameReady?.Invoke();

        /// <summary>
        /// Renders the minimap into the owned render-device texture and returns it
        /// (null if nothing to draw). Call on the render thread.
        /// </summary>
        public ID3D11Texture2D RenderMinimap()
        {
            if (!IsInitialized || _disposed || _device is null)
                return null;

            CheckDeviceChanged();

            var srv = _frameSource?.FrameSrv;
            if (srv == null)
                return null;

            _videoWidth = _frameSource.VideoWidth;
            _videoHeight = _frameSource.VideoHeight;

            if (!EnsureMinimapTexture(ControlWidth, ControlHeight))
                return null;

            lock (_lockRecreatedResources)
            {
                if (_showZoomBox)
                    UpdateConstantBuffer();

                _context.RSSetViewports(new[] { Viewport });
                _context.RSSetState(_rasterizer);
                _context.OMSetBlendState(_blend);
                _context.OMSetRenderTargets(_minimapRtv);
                _context.ClearRenderTargetView(_minimapRtv, new Color4(0f, 0f, 0f, 1f));

                _context.VSSetShader(_vertexShader);
                _context.PSSetShader(_showZoomBox ? _pixelShaderWithZoomBox : _pixelShader);
                _context.PSSetShaderResource(0, srv);
                _context.PSSetSampler(0, _sampler);
                if (_showZoomBox)
                    _context.PSSetConstantBuffer(0, _cbViewport);

                _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _context.Draw(3, 0);

                _context.OMSetRenderTargets((ID3D11RenderTargetView)null);
                _context.PSSetShaderResource(0, null);
            }

            return _minimapTex;
        }

        private void CheckDeviceChanged()
        {
            if (_device == _player.Renderer.Device)
                return;

            Log.Warn($"D3D11 render device changed, reinitializing");
            LocalDispose();
            Initialize();
        }

        private void CompileShaders()
        {
            var vsBlob = Compiler.Compile(VSSrc, "main", "vs", "vs_5_0");
            _vertexShader = _device.CreateVertexShader(vsBlob.Span);

            var psBlob = Compiler.Compile(PSSrc, "main", "ps", "ps_5_0");
            _pixelShaderWithZoomBox = _device.CreatePixelShader(psBlob.Span);

            psBlob = Compiler.Compile(PSSrcSimple, "main", "ps_simple", "ps_5_0");
            _pixelShader = _device.CreatePixelShader(psBlob.Span);
        }

        private void CreateSamplerAndStates()
        {
            _sampler = _device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            });
            _rasterizer = _device.CreateRasterizerState(RasterizerDescription.CullNone);
            _blend = _device.CreateBlendState(BlendDescription.Opaque);
        }

        private void CreateConstantBuffer()
        {
            _cbViewport = _device.CreateBuffer(new BufferDescription
            {
                ByteWidth = (uint)Marshal.SizeOf<CbViewport>(),
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });
        }

        private bool EnsureMinimapTexture(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return false;

            if (_minimapTex != null && _minimapWidth == width && _minimapHeight == height)
                return true;

            _minimapRtv?.Dispose(); _minimapRtv = null;
            _minimapTex?.Dispose(); _minimapTex = null;

            _minimapTex = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
            });
            _minimapRtv = _device.CreateRenderTargetView(_minimapTex);
            _minimapWidth = width;
            _minimapHeight = height;

            SetViewport(width, height);
            return true;
        }

        // cbuffer: Viewport-Rect from Zoom/Pan
        private void UpdateConstantBuffer()
        {
            var cfg = _player.Config.Video;
            var pViewport = cfg.vp.Viewport;

            var x = Math.Clamp(-pViewport.X / pViewport.Width, 0f, 1f);
            var y = Math.Clamp(-pViewport.Y / pViewport.Height, 0f, 1f);
            var w = cfg.vp.ControlWidth / pViewport.Width;
            var h = cfg.vp.ControlHeight / pViewport.Height;

            var cb = new CbViewport
            {
                ViewX = x,
                ViewY = y,
                ViewW = w,
                ViewH = h,
                MapW = ControlWidth,
                MapH = ControlHeight
            };
            var mapped = _context.Map(_cbViewport, 0, MapMode.WriteDiscard, MapFlags.None);
            Marshal.StructureToPtr(cb, mapped.DataPointer, false);
            _context.Unmap(_cbViewport, 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_frameSource != null)
                _frameSource.FrameReady -= OnFrameReady;
            _frameSource?.Dispose();
            _frameSource = null;

            LocalDispose();
        }

        private void LocalDispose()
        {
            IsInitialized = false;

            _minimapRtv?.Dispose(); _minimapRtv = default;
            _minimapTex?.Dispose(); _minimapTex = default;
            _minimapWidth = _minimapHeight = 0;

            _sampler?.Dispose(); _sampler = default;
            _rasterizer?.Dispose(); _rasterizer = default;
            _cbViewport?.Dispose(); _cbViewport = default;
            _blend?.Dispose(); _blend = default;
            _vertexShader?.Dispose(); _vertexShader = default;
            _pixelShader?.Dispose(); _pixelShader = default;
            _pixelShaderWithZoomBox?.Dispose(); _pixelShaderWithZoomBox = default;

            _context = default;
            _device = default;
        }

        internal void UpdateSize(int actualWidth, int actualHeight)
        {
            if (_disposed || actualWidth <= 0 || actualHeight <= 0)
                return;

            Log.Debug($"UpdateSize({actualWidth}, {actualHeight})");
            ControlWidth = actualWidth;
            ControlHeight = actualHeight;
            // _minimapTex is recreated lazily on the next RenderMinimap.
            _minimapWidth = _minimapHeight = 0;
            SetViewport(ControlWidth, ControlHeight);
        }

        private void SetViewport(int width, int height)
        {
            if (width == 0 || height == 0 || _videoWidth == 0 || _videoHeight == 0) return;

            int x, y, newWidth, newHeight, xPixels, yPixels;

            var curRatio = (double)_videoWidth / _videoHeight;
            var fillRatio = (double)width / height;

            SideYPixels = SideXPixels = 0;
            yPixels = xPixels = 0;
            x = y = 0;

            if (curRatio < fillRatio)
            {
                newWidth = (int)(height * curRatio);
                newHeight = height;

                SideXPixels = ((int)(width - (height * curRatio))) & ~1;

                x = SideXPixels / 2;
                xPixels = newWidth - (width - SideXPixels);
            }
            else
            {
                newWidth = width;
                newHeight = (int)(width / curRatio);
                SideYPixels = ((int)(height - (width / curRatio))) & ~1;

                y = SideYPixels / 2;
                yPixels = newHeight - (height - SideYPixels);
            }
            Viewport = new((int)(x - xPixels * 0.5), (int)(y - yPixels * 0.5), (float)newWidth, (float)newHeight);
            VideoViewSizeChanged?.Invoke();
        }
    }
}
