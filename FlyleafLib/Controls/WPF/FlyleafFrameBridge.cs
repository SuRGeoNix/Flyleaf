using FlyleafLib.Controls.WPF.Present;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

using FlyleafLib.MediaPlayer;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11Device1 = Vortice.Direct3D11.ID3D11Device1;
using Format = Vortice.DXGI.Format;

namespace FlyleafLib.Controls.WPF;

/// <summary>
/// Acquires the current player frame on the render adapter and exposes it to a
/// presenter. The player renders to its composition swap-chain (any adapter,
/// incl. a forced discrete GPU); each frame's backbuffer is copied into a frame
/// texture and, when needed, read back to a CPU buffer.
///
/// Frame delivery depends on the active present mode / adapter topology:
/// - Hardware, same adapter: a Shared render-device texture handed over GPU-side
///   (zero CPU) via <see cref="TryPresentShared"/>.
/// - Hardware, cross adapter: a Staging texture read back to CPU and uploaded to
///   the compositor via <see cref="TryPresentCpu"/>.
/// - Software: a Staging texture read back to CPU and blitted into a
///   WriteableBitmap via <see cref="TryCopyCpuInto"/> (adapter-agnostic, needs no
///   D3D front buffer — works over RDP/headless/locked sessions).
/// </summary>
public sealed unsafe class FlyleafFrameBridge : IVideoFrameProvider
{
    private readonly object _sync = new();

    private Player _player;

    // Render adapter identity
    private long _renderLuid;

    private bool _modeSet;
    private PresentKind _presentKind = PresentKind.Software;
    private bool _compositorSet;
    private bool _crossAdapter;
    private bool _needsReadback = true;

    private int _controlWidth;
    private int _controlHeight;
    private bool _isDisposed;

    // Render-side frame texture (Shared when GPU same-adapter, Staging when readback)
    private ID3D11Texture2D _frameTexture;
    private int _frameWidth;
    private int _frameHeight;
    private nint _sharedHandle;

    // CPU carry buffer (readback paths)
    private nint _cpuBuffer;
    private int _cpuBufferLen;
    private int _cpuRowPitch;
    private int _cpuWidth;
    private int _cpuHeight;
    private bool _hasCpuFrame;

    private long _frameGeneration;
    private long _drawnGeneration;
    private int _acquireCount;

    public bool IsCrossAdapter { get { lock (_sync) return _crossAdapter; } }

    public bool HasPendingFrame
    {
        get { lock (_sync) return !_isDisposed && _frameGeneration != _drawnGeneration; }
    }

    public void Initialize(Player player, int controlWidth, int controlHeight)
    {
        _player = player;
        _controlWidth = controlWidth;
        _controlHeight = controlHeight;

        _renderLuid = player.Renderer.GPUAdapter?.Luid ?? 0;

        DebugLogger.Print($"[FLB] Initialize control={controlWidth}x{controlHeight} renderLuid={_renderLuid}");

        player.Renderer.SwapChain.RegisterBeforePresentCallback(OnBeforePresent);
        player.Renderer.SwapChain.SetupWinUI(OnSwapChainUpdated);
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
            // LUID is the unique adapter identity; two identical-model discrete GPUs
            // differ by LUID, so LUID equality is the only safe same-adapter test.
            return compLuid != _renderLuid;
        }
        catch (Exception ex)
        {
            DebugLogger.Print($"[FLB] DetectCrossAdapter failed, using CPU path: {ex.Message}");
            return true;
        }
    }

    private void OnSwapChainUpdated(IDXGISwapChain2 swapChain)
    {
        try
        {
            lock (_sync)
                DropFrameTexture();

            if (swapChain != null)
                _player?.Renderer?.SwapChain?.Resize(_controlWidth, _controlHeight);
        }
        finally
        {
            swapChain?.Dispose();
        }
    }

    private void OnBeforePresent()
    {
        lock (_sync)
        {
            if (_isDisposed || !_modeSet)
                return;

            if (_presentKind == PresentKind.Hardware && !_compositorSet)
                return;

            var swapChain = _player?.Renderer?.SwapChain;
            if (swapChain == null || swapChain.Disposed)
                return;

            if (!EnsureFrameTexture(_controlWidth, _controlHeight))
                return;

            if (!swapChain.CopyBackBufferTo(_frameTexture))
                return;

            if (_needsReadback && !ReadbackToCpu())
                return;

            _frameGeneration++;

            int n = ++_acquireCount;
            if (n <= 3 || n % 120 == 0)
                DebugLogger.Print($"[FLB] acquire #{n} {_frameWidth}x{_frameHeight} readback={_needsReadback}");
        }
    }

    private bool ReadbackToCpu()
    {
        try
        {
            var context = _player.Renderer.Device.ImmediateContext;
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

        _player?.Renderer?.SwapChain?.Resize(width, height);
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

        _frameTexture = _player.Renderer.Device.CreateTexture2D(desc);
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

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
        }

        if (_player?.Renderer?.SwapChain != null)
        {
            _player.Renderer.SwapChain.UnregisterBeforePresentCallback(OnBeforePresent);
            _player.Renderer.SwapChain.Dispose(rendererFrame: false);
        }

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
