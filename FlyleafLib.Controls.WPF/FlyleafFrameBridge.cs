using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Wpf;
using FlyleafLib.MediaPlayer;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using Format = Vortice.DXGI.Format;

namespace FlyleafLib.Controls.WPF;

/// <summary>
/// Feeds the current player frame into a <see cref="DrawingSurface"/>'s
/// <c>ColorTexture</c>. The player renders to its composition swap-chain on the
/// render adapter; each frame's backbuffer is grabbed there and delivered to the
/// DrawingSurface device (which lives on the compositor adapter).
///
/// When both are the same physical GPU the frame is handed over GPU-side via a
/// shared texture (zero CPU). When the render GPU differs from the compositor
/// GPU (forced discrete card / headless GPU / Optimus dGPU) the frame is carried
/// over through a CPU staging readback, because legacy D3D11/D3D9 sharing and
/// WPF's D3DImage compositor are single-adapter.
/// </summary>
internal sealed unsafe class FlyleafFrameBridge : IDisposable
{
    private readonly object sync = new();

    private Player player;

    // Render adapter identity
    private long renderLuid;
    private uint renderVendorId;
    private uint renderDeviceId;

    // Compositor (DrawingSurface) device
    private ID3D11Device1 compositorDevice;
    private bool compositorSet;
    private bool crossAdapter;

    private int controlWidth;
    private int controlHeight;
    private bool isDisposed;

    // Render-side frame texture (Shared when same-adapter, Staging when cross)
    private ID3D11Texture2D frameTexture;
    private int frameWidth;
    private int frameHeight;
    private nint sharedHandle;

    // Cross-adapter CPU carry buffer
    private nint cpuBuffer;
    private int cpuBufferLen;
    private int cpuRowPitch;
    private int cpuWidth;
    private int cpuHeight;
    private bool hasCpuFrame;

    // Compositor-side opened shared texture (same-adapter path)
    private ID3D11Texture2D openedTexture;
    private nint openedHandle;

    private int acquireCount;
    private int drawCount;

    public void Initialize(Player player, int controlWidth, int controlHeight)
    {
        this.player = player;
        this.controlWidth = controlWidth;
        this.controlHeight = controlHeight;

        var adapter = player.Renderer.GPUAdapter;
        renderLuid = adapter?.Luid ?? 0;
        renderVendorId = (uint)(adapter?.Vendor ?? 0);
        renderDeviceId = adapter?.Id ?? 0;

        DebugLogger.Print($"[FLB] Initialize control={controlWidth}x{controlHeight} renderLuid={renderLuid}");

        player.Renderer.SwapChain.RegisterBeforePresentCallback(OnBeforePresent);
        player.Renderer.SwapChain.SetupWinUI(OnSwapChainUpdated);
    }

    /// <summary>Called from DrawingSurface.LoadContent with the surface's device.</summary>
    public void SetCompositorDevice(ID3D11Device1 device)
    {
        lock (sync)
        {
            compositorDevice = device;
            crossAdapter = DetectCrossAdapter(device);
            compositorSet = true;
            ResetOpenedTexture();
            DropFrameTexture();
        }

        DebugLogger.Print($"[FLB] SetCompositorDevice crossAdapter={crossAdapter}");
    }

    private bool DetectCrossAdapter(ID3D11Device device)
    {
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            var desc = adapter.Description;

            long compLuid = desc.Luid;
            uint compVendor = desc.VendorId;
            uint compDevice = desc.DeviceId;

            if (compLuid == renderLuid)
                return false;

            // Different LUID but same physical GPU (virtual-display duplicates) is not cross-adapter.
            return !(compVendor == renderVendorId && compDevice == renderDeviceId);
        }
        catch (Exception ex)
        {
            DebugLogger.Print($"[FLB] DetectCrossAdapter failed: {ex.Message}");
            return false;
        }
    }

    private void OnSwapChainUpdated(IDXGISwapChain2 swapChain)
    {
        try
        {
            lock (sync)
                DropFrameTexture();

            if (swapChain != null)
                player?.Renderer?.SwapChain?.Resize(controlWidth, controlHeight);
        }
        finally
        {
            swapChain?.Dispose();
        }
    }

    private void OnBeforePresent()
    {
        lock (sync)
        {
            if (isDisposed || !compositorSet)
                return;

            var swapChain = player?.Renderer?.SwapChain;
            if (swapChain == null || swapChain.Disposed)
                return;

            if (!EnsureFrameTexture(controlWidth, controlHeight))
                return;

            if (!swapChain.CopyBackBufferTo(frameTexture))
                return;

            if (crossAdapter)
                ReadbackToCpu();

            int n = ++acquireCount;
            if (n <= 3 || n % 120 == 0)
                DebugLogger.Print($"[FLB] acquire #{n} {frameWidth}x{frameHeight} cross={crossAdapter}");
        }
    }

    private void ReadbackToCpu()
    {
        try
        {
            var context = player.Renderer.Device.ImmediateContext;
            var map = context.Map(frameTexture, 0);
            try
            {
                int len = (int)map.RowPitch * frameHeight;
                EnsureCpuBuffer(len);
                Buffer.MemoryCopy((void*)map.DataPointer, (void*)cpuBuffer, len, len);
                cpuRowPitch = (int)map.RowPitch;
                cpuWidth = frameWidth;
                cpuHeight = frameHeight;
                hasCpuFrame = true;
            }
            finally
            {
                context.Unmap(frameTexture, 0);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Print($"[FLB] ReadbackToCpu failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>Called from DrawingSurface.Draw on the compositor device.</summary>
    public void CopyLatestFrameTo(DrawEventArgs args)
    {
        try
        {
            var colorTexture = args.Surface.ColorTexture;
            if (colorTexture == null)
                return;

            var ctDesc = colorTexture.Description;

            lock (sync)
            {
                if (isDisposed)
                    return;

                if (crossAdapter)
                {
                    if (!hasCpuFrame || cpuWidth != (int)ctDesc.Width || cpuHeight != (int)ctDesc.Height)
                        return;

                    args.Context.UpdateSubresource(colorTexture, 0, null, cpuBuffer, (uint)cpuRowPitch, 0);
                    LogDraw();
                    return;
                }

                if (sharedHandle == IntPtr.Zero)
                    return;

                if (openedTexture == null || openedHandle != sharedHandle)
                {
                    ResetOpenedTexture();
                    openedTexture = compositorDevice.OpenSharedResource<ID3D11Texture2D>(sharedHandle);
                    openedHandle = sharedHandle;
                }

                var srcDesc = openedTexture.Description;
                if (srcDesc.Width != ctDesc.Width || srcDesc.Height != ctDesc.Height)
                    return;

                args.Context.CopyResource(colorTexture, openedTexture);
                LogDraw();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Print($"[FLB] CopyLatestFrameTo failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    private void LogDraw()
    {
        int n = ++drawCount;
        if (n <= 3 || n % 120 == 0)
            DebugLogger.Print($"[FLB] draw #{n} cross={crossAdapter}");
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        lock (sync)
        {
            if (width == controlWidth && height == controlHeight)
                return;

            controlWidth = width;
            controlHeight = height;
            DropFrameTexture();
        }

        player?.Renderer?.SwapChain?.Resize(width, height);
    }

    private bool EnsureFrameTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return false;

        if (frameTexture != null && frameWidth == width && frameHeight == height)
            return true;

        DropFrameTexture();

        var device = player.Renderer.Device;
        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = crossAdapter ? ResourceUsage.Staging : ResourceUsage.Default,
            BindFlags = crossAdapter ? BindFlags.None : BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = crossAdapter ? CpuAccessFlags.Read : CpuAccessFlags.None,
            MiscFlags = crossAdapter ? ResourceOptionFlags.None : ResourceOptionFlags.Shared
        };

        frameTexture = device.CreateTexture2D(desc);
        frameWidth = width;
        frameHeight = height;

        if (!crossAdapter)
        {
            using var dxgiResource = frameTexture.QueryInterface<IDXGIResource>();
            sharedHandle = dxgiResource.SharedHandle;
        }
        else
        {
            sharedHandle = IntPtr.Zero;
        }

        return true;
    }

    private void EnsureCpuBuffer(int len)
    {
        if (cpuBuffer != IntPtr.Zero && cpuBufferLen >= len)
            return;

        if (cpuBuffer != IntPtr.Zero)
            Marshal.FreeHGlobal(cpuBuffer);

        cpuBuffer = Marshal.AllocHGlobal(len);
        cpuBufferLen = len;
    }

    private void DropFrameTexture()
    {
        frameTexture?.Dispose();
        frameTexture = null;
        frameWidth = 0;
        frameHeight = 0;
        sharedHandle = IntPtr.Zero;
        hasCpuFrame = false;
        ResetOpenedTexture();
    }

    private void ResetOpenedTexture()
    {
        openedTexture?.Dispose();
        openedTexture = null;
        openedHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (isDisposed)
                return;
            isDisposed = true;
        }

        if (player?.Renderer?.SwapChain != null)
        {
            player.Renderer.SwapChain.UnregisterBeforePresentCallback(OnBeforePresent);
            player.Renderer.SwapChain.Dispose(rendererFrame: false);
        }

        lock (sync)
        {
            DropFrameTexture();
            if (cpuBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(cpuBuffer);
                cpuBuffer = IntPtr.Zero;
                cpuBufferLen = 0;
            }
        }
    }
}
