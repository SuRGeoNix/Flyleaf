using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

using FlyleafLib.Controls.WPF; // DebugLogger

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11Device1 = Vortice.Direct3D11.ID3D11Device1;
using Format = Vortice.DXGI.Format;

namespace FlyleafLib.Controls.WPF.Present;

/// <summary>
/// Shared frame-delivery logic for <see cref="IVideoFrameProvider"/>: owns the
/// render-device frame texture (Shared for the same-adapter GPU fast path, Staging
/// for readback), the CPU carry buffer, cross-adapter detection, present-mode
/// selection, and generation gating. Subclasses only supply the render device and
/// a way to fill the frame texture with the current source (swap-chain backbuffer,
/// zoom-overview minimap, ...).
/// </summary>
public abstract unsafe class VideoFrameProviderBase : IVideoFrameProvider
{
    protected readonly object _sync = new();

    private long _renderLuid;
    private bool _modeSet;
    private PresentKind _presentKind = PresentKind.Software;
    private bool _compositorSet;
    private bool _crossAdapter;
    private bool _needsReadback = true;

    protected int _controlWidth;
    protected int _controlHeight;
    private bool _isDisposed;

    private ID3D11Texture2D _frameTexture;
    private int _frameWidth;
    private int _frameHeight;
    private nint _sharedHandle;

    private nint _cpuBuffer;
    private int _cpuBufferLen;
    private int _cpuRowPitch;
    private int _cpuWidth;
    private int _cpuHeight;
    private bool _hasCpuFrame;

    private long _frameGeneration;
    private long _drawnGeneration;
    private int _acquireCount;

    /// <summary>The device the frames are produced on (the player's render adapter).</summary>
    protected abstract ID3D11Device RenderDevice { get; }

    protected void SetRenderLuid(long luid) => _renderLuid = luid;

    protected void SetControlSize(int width, int height)
    {
        _controlWidth = width;
        _controlHeight = height;
    }

    public bool IsCrossAdapter { get { lock (_sync) return _crossAdapter; } }

    public bool HasPendingFrame
    {
        get { lock (_sync) return !_isDisposed && _frameGeneration != _drawnGeneration; }
    }

    public void SetPresentMode(PresentKind kind)
    {
        lock (_sync)
        {
            _presentKind = kind;
            _modeSet = true;
            Recompute();
            DropFrameTexture();
        }
        DebugLogger.Print($"[FLB] SetPresentMode {kind} needsReadback={_needsReadback}");
    }

    public void SetCompositorDevice(ID3D11Device1 device)
    {
        lock (_sync)
        {
            _crossAdapter = DetectCrossAdapter(device);
            _compositorSet = true;
            Recompute();
            DropFrameTexture();
        }
        DebugLogger.Print($"[FLB] SetCompositorDevice crossAdapter={_crossAdapter}");
    }

    private void Recompute()
        => _needsReadback = _presentKind == PresentKind.Software || (_presentKind == PresentKind.Hardware && _crossAdapter);

    private bool DetectCrossAdapter(ID3D11Device device)
    {
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            long compLuid = adapter.Description.Luid;
            DebugLogger.Print($"[FLB] adapters renderLuid={_renderLuid} compositorLuid={compLuid}");
            return compLuid != _renderLuid;
        }
        catch (Exception ex)
        {
            DebugLogger.Print($"[FLB] DetectCrossAdapter failed, using CPU path: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// Produces one frame: ensures the frame texture, lets the subclass fill it via
    /// <paramref name="writeInto"/>, reads it back to CPU when required, and bumps the
    /// generation. Call on the render thread.
    /// </summary>
    protected bool PublishFrame(Func<ID3D11Texture2D, bool> writeInto)
    {
        lock (_sync)
        {
            if (_isDisposed || !_modeSet)
                return false;

            if (_presentKind == PresentKind.Hardware && !_compositorSet)
                return false;

            if (!EnsureFrameTexture(_controlWidth, _controlHeight))
                return false;

            if (!writeInto(_frameTexture))
                return false;

            if (_needsReadback && !ReadbackToCpu())
                return false;

            _frameGeneration++;

            int n = ++_acquireCount;
            if (n <= 3 || n % 120 == 0)
                DebugLogger.Print($"[FLB] acquire #{n} {_frameWidth}x{_frameHeight} readback={_needsReadback}");

            return true;
        }
    }

    private bool ReadbackToCpu()
    {
        try
        {
            var context = RenderDevice.ImmediateContext;
            var map = context.Map(_frameTexture, 0);
            try
            {
                int len = (int)map.RowPitch * _frameHeight;
                EnsureCpuBuffer(len);
                Buffer.MemoryCopy((void*)map.DataPointer, (void*)_cpuBuffer, len, len);
                _cpuRowPitch = (int)map.RowPitch;
                _cpuWidth = _frameWidth;
                _cpuHeight = _frameHeight;
                _hasCpuFrame = true;
                return true;
            }
            finally
            {
                context.Unmap(_frameTexture, 0);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Print($"[FLB] ReadbackToCpu failed: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    public bool TryPresentShared(Func<nint, bool> copyFromHandle)
    {
        lock (_sync)
        {
            if (_isDisposed || _needsReadback || _sharedHandle == IntPtr.Zero || _frameGeneration == _drawnGeneration)
                return false;

            bool ok = copyFromHandle(_sharedHandle);
            if (ok)
                _drawnGeneration = _frameGeneration;
            return ok;
        }
    }

    public bool TryPresentCpu(Func<nint, int, int, int, bool> upload)
    {
        lock (_sync)
        {
            if (_isDisposed || !_needsReadback || !_hasCpuFrame || _frameGeneration == _drawnGeneration)
                return false;

            bool ok = upload(_cpuBuffer, _cpuRowPitch, _cpuWidth, _cpuHeight);
            if (ok)
                _drawnGeneration = _frameGeneration;
            return ok;
        }
    }

    public bool TryCopyCpuInto(nint dest, int destStride, int destWidth, int destHeight)
    {
        lock (_sync)
        {
            if (_isDisposed || !_hasCpuFrame || _frameGeneration == _drawnGeneration)
                return false;

            if (_cpuWidth != destWidth || _cpuHeight != destHeight || dest == IntPtr.Zero)
                return false;

            int rowBytes = Math.Min(_cpuRowPitch, destStride);
            byte* src = (byte*)_cpuBuffer;
            byte* dst = (byte*)dest;
            for (int y = 0; y < destHeight; y++)
                Buffer.MemoryCopy(src + (long)y * _cpuRowPitch, dst + (long)y * destStride, destStride, rowBytes);

            _drawnGeneration = _frameGeneration;
            return true;
        }
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        lock (_sync)
        {
            if (width == _controlWidth && height == _controlHeight)
                return;

            _controlWidth = width;
            _controlHeight = height;
            DropFrameTexture();
        }

        OnResize(width, height);
    }

    /// <summary>Subclass hook to resize its frame source (swap-chain / minimap RT).</summary>
    protected virtual void OnResize(int width, int height) { }

    /// <summary>Drops the frame texture under the lock (e.g. on swap-chain re-creation).</summary>
    protected void ResetFrames()
    {
        lock (_sync)
            DropFrameTexture();
    }

    private bool EnsureFrameTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return false;

        if (_frameTexture != null && _frameWidth == width && _frameHeight == height)
            return true;

        DropFrameTexture();

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = _needsReadback ? ResourceUsage.Staging : ResourceUsage.Default,
            BindFlags = _needsReadback ? BindFlags.None : BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = _needsReadback ? CpuAccessFlags.Read : CpuAccessFlags.None,
            MiscFlags = _needsReadback ? ResourceOptionFlags.None : ResourceOptionFlags.Shared
        };

        _frameTexture = RenderDevice.CreateTexture2D(desc);
        _frameWidth = width;
        _frameHeight = height;

        if (!_needsReadback)
        {
            using var dxgiResource = _frameTexture.QueryInterface<IDXGIResource>();
            _sharedHandle = dxgiResource.SharedHandle;
        }
        else
        {
            _sharedHandle = IntPtr.Zero;
        }

        return true;
    }

    private void EnsureCpuBuffer(int len)
    {
        if (_cpuBuffer != IntPtr.Zero && _cpuBufferLen >= len)
            return;

        if (_cpuBuffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_cpuBuffer);

        _cpuBuffer = Marshal.AllocHGlobal(len);
        _cpuBufferLen = len;
    }

    private void DropFrameTexture()
    {
        _frameTexture?.Dispose();
        _frameTexture = null;
        _frameWidth = 0;
        _frameHeight = 0;
        _sharedHandle = IntPtr.Zero;
        _hasCpuFrame = false;
    }

    /// <summary>Subclass hook to release its frame source (unregister callbacks, dispose RT).</summary>
    protected virtual void DisposeCore() { }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
        }

        DisposeCore();

        lock (_sync)
        {
            DropFrameTexture();
            if (_cpuBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_cpuBuffer);
                _cpuBuffer = IntPtr.Zero;
                _cpuBufferLen = 0;
            }
        }
    }
}
