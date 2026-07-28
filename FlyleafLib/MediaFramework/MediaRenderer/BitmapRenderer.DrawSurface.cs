using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Wpf;
using InputElementDescription = Vortice.Direct3D11.InputElementDescription;
using VRect = Vortice.Mathematics.Rect;

namespace FlyleafLib.MediaFramework.MediaRenderer;
#nullable enable
public partial class BitmapRenderer : NotifyPropertyChanged, IDisposable
{
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private IDXGIDevice1? _dxgiDevice;
    //ID2D1 resources
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;
    private ID2D1Bitmap1? _d2dTargetBitmap;
    private ID2D1Bitmap? _d2dSourceBitmap;

    private BitmapProperties1 bitmapProps = new()
    {
        BitmapOptions = BitmapOptions.Target | BitmapOptions.CannotDraw,
        PixelFormat = Vortice.DCommon.PixelFormat.Premultiplied
    };

    private readonly object _sync = new();
    private bool _initialized = false;
    public bool Disposed;


    private byte[]? _pixelBuffer;
    private int _w, _h, _stride;
    private long _flagNewPixels;

    private DrawingSurface _drawSurface;

    public BitmapRenderer(DrawingSurface drawSurface, VPConfig config, int uniqueId = -1)
    {
        ucfg = config;

        _drawSurface = drawSurface;
        drawSurface.LoadContent += OnLoadContent;
        drawSurface.UnloadContent += OnUnloadContext;
        drawSurface.Draw += OnDraw;
        drawSurface.SizeChanged += OnSizeChanged;

        UpdateSize((int)drawSurface.ActualWidth, (int)drawSurface.ActualHeight);

        UniqueId = uniqueId == -1 ? GetUniqueId() : uniqueId;
        Log = new(("[#" + UniqueId + "]").PadRight(8, ' ') + " [BitmapRenderer ] ");
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSize((int) e.NewSize.Width, (int) e.NewSize.Height);
        lock (_sync)
        {
            RecreateD2D1RenderTarget();
        }
        Interlocked.Exchange(ref _flagNewPixels, 1);
    }

    private void OnLoadContent(object? sender, DrawingSurfaceEventArgs e)
    {
        Log.Debug("OnLoadContent");
        InitContext(sender, e.Device, e.Context);
    }
    private void InitContext(object? sender, Vortice.Direct3D11.ID3D11Device device, ID3D11DeviceContext1 context)
    {
        Log.Debug("InitContext");
        InputElementDescription[] inputElements =
        [
            new("POSITION", 0, Format.R32G32B32_Float,  0),
            new("TEXCOORD", 0, Format.R32G32_Float,     0)
        ];

        _vertexShader = device.CreateVertexShader(ShaderCompiler.VSBlob);
        _pixelShader = ShaderCompiler.CompilePS(device, "rgba", "color = float4(Texture1.Sample(Sampler, input.Texture).rgba);");
        _inputLayout = device.CreateInputLayout(inputElements, ShaderCompiler.VSBlob);

        _dxgiDevice = device.QueryInterface<IDXGIDevice1>();
        FormatSupport fs = device.CheckFormatSupport(Format.B8G8R8A8_UNorm);


        if (sender is not DrawingSurface image)
            return;

        _d2dDevice = D2D1.D2D1CreateDevice(_dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext();

        using var surface = image.ColorTexture?.QueryInterface<IDXGISurface>();
        _d2dTargetBitmap = _d2dContext?.CreateBitmapFromDxgiSurface(surface, bitmapProps);
        if (_d2dContext is not null)
            _d2dContext.Target = _d2dTargetBitmap;
        else
            Log.Warn("Bitmap renderer was initialized with empty target");
        _initialized = true;
    }

    private void OnUnloadContext(object? sender, DrawingSurfaceEventArgs e)
    {
        Log.Debug("OnUnloadContext");

        D3ResourceDispose();
        D2DContextDispose();

        Disposed = true;
        _initialized = false;
    }

    public void UpdateFrame(Bitmap bmp)
    {
        Bitmap src = bmp;
        if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
            src = bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format32bppArgb);

        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int w = src.Width;
            int h = src.Height;
            int stride = Math.Abs(data.Stride);
            int size = stride * h;

            lock (_sync)
            {
                if (_pixelBuffer == null || _pixelBuffer.Length < size)
                    _pixelBuffer = new byte[size];

                Marshal.Copy(data.Scan0, _pixelBuffer, 0, size);

                _w = w;
                _h = h;
                _stride = stride;
                Interlocked.Exchange(ref _flagNewPixels, 1);
            }
        }
        finally
        {
            src.UnlockBits(data);
            if (!ReferenceEquals(src, bmp))
                src.Dispose();
            bmp.Dispose();
        }
    }

    private void RecreateD2DBitmap()
    {
        if (_d2dContext == null || _w == 0 || _h == 0 || !_initialized)
            return;

        _d2dSourceBitmap?.Dispose();

        BitmapProperties props = new(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));
        _d2dSourceBitmap = _d2dContext?.CreateBitmap(new SizeI(_w, _h), IntPtr.Zero, (uint)_stride, props);
    }

    private void RecreateD2D1RenderTarget()
    {
        if (!_initialized)
            return;
        D2DContextDispose();

        _d2dDevice = D2D1.D2D1CreateDevice(_dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext();

        using var surface = _drawSurface.ColorTexture?.QueryInterface<IDXGISurface>();

        _d2dTargetBitmap = _d2dContext?.CreateBitmapFromDxgiSurface(surface, bitmapProps);
        if (_d2dContext != null)
            _d2dContext.Target = _d2dTargetBitmap;

        RecreateD2DBitmap();
    }

    private void D2DContextDispose()
    {
        _d2dSourceBitmap?.Dispose();

        if (_d2dContext is not null)
            _d2dContext.Target = null;

        _d2dTargetBitmap?.Dispose();
        _d2dTargetBitmap = null;

        _d2dContext?.Dispose();
        _d2dContext = null;

        _d2dDevice?.Dispose();
        _d2dDevice = null;
    }

    private void D3ResourceDispose()
    {
        _vertexBuffer?.Dispose();
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _inputLayout?.Dispose();

        _d2dSourceBitmap?.Dispose();
        _d2dSourceBitmap = null;
    }

    private void OnDraw(object? sender, DrawEventArgs e)
    {
        if (!_initialized)
        {
            InitContext(sender, e.Device, e.Context);
        }

        e.Context.ClearRenderTargetView(e.Surface.ColorTextureView, Utils.WPFToVorticeColor(ucfg.BackColor));

        if (e.Surface.DepthStencilView != null)
        {
            e.Context.ClearDepthStencilView(e.Surface.DepthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
        }
        e.Context.OMSetBlendState(null);
        e.Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        e.Context.VSSetShader(_vertexShader);
        e.Context.PSSetShader(_pixelShader);

        if (sender is DrawingSurface surface)
            lock (_sync)
            {
                DrawOnSurface(surface);
            }

        e.Context.Draw(3, 0);
    }

    private void DrawOnSurface(DrawingSurface surface)
    {
        if (Interlocked.CompareExchange(ref _flagNewPixels, 0, 1) == 1)
        {
            if (_d2dTargetBitmap is null || ControlWidth != surface.ActualWidth || ControlHeight != surface.ActualWidth)
                RecreateD2D1RenderTarget();
            if (_d2dSourceBitmap is null || _d2dSourceBitmap is not ID2D1Bitmap)
                RecreateD2DBitmap();
            if (_d2dSourceBitmap is null)

                return;

            if (_d2dSourceBitmap?.Size.Width != _w || _d2dSourceBitmap?.Size.Height != _h)
                RecreateD2DBitmap();

            if (_pixelBuffer != null && _d2dSourceBitmap != null)
            {
                _d2dSourceBitmap.CopyFromMemory(_pixelBuffer, (uint)_stride);
            }
        }

        if (_d2dSourceBitmap is null)
            return;

        var bbWidth  = _d2dSourceBitmap.PixelSize.Width;
        var bbHeight = _d2dSourceBitmap.PixelSize.Height;
        _curRatio = bbWidth / bbHeight;

        SetViewport(ControlWidth, ControlHeight);

        var dstRect = GetDestinationRect();        
        var srcRect = GetSourceRect(bbWidth, bbHeight);
        
        if (_d2dContext is not ID2D1RenderTarget ctx)
            return;

        ctx.BeginDraw();
        ctx.DrawBitmap(
            _d2dSourceBitmap,
            dstRect,
            1f,
            BitmapInterpolationMode.Linear,
            srcRect
        );
        ctx.EndDraw();
    }

    public void Dispose()
    {
        if (!Disposed)
        {
            D3ResourceDispose();
            D2DContextDispose();
            Disposed = true;
        }
    }

    private VRect GetSourceRect(int width, int height)
    {
        var vp = Viewport;
                
        var zoom = (float)ValidateZoom(ucfg.zoom);
        var newWidth = width / zoom;
        var newHeight = height / zoom;

        var x = (float)ucfg.zoomCenter.X * width - newWidth / 2;
        var y = (float)ucfg.zoomCenter.Y * height - newHeight / 2;

        return new (x, y, width/zoom, height/zoom);
    }

    private VRect GetDestinationRect()
    {
        var vp = Viewport;

        var width = Math.Min(vp.Width, ControlWidth);
        var height = Math.Min(vp.Height, ControlHeight);
        var x = vp.X >= 0 ? vp.X : 0;
        var y = vp.Y >= 0 ? vp.Y : 0;

        return new(x, y, width, height);
    }
}
#nullable disable
