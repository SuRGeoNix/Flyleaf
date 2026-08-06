using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

using FlyleafLib.MediaPlayer;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using Format = Vortice.DXGI.Format;

namespace FlyleafLib.Controls.WPF;

/// <summary>
/// Acquires the current player frame on the render adapter and exposes it as CPU
/// pixels for software presentation via a WPF <see cref="System.Windows.Media.Imaging.WriteableBitmap"/>.
///
/// The player renders to its composition swap-chain (any adapter, incl. a forced
/// discrete GPU); each frame's backbuffer is copied into a CPU-readable staging
/// texture and read back into a buffer. A CPU/software presentation path is used
/// (rather than D3DImage) so rendering works regardless of adapter topology and
/// in environments with no D3D front buffer (RDP, headless, locked sessions).
/// </summary>
internal sealed unsafe class FlyleafFrameBridge : IDisposable
{
    private readonly object sync = new();

    private Player player;
    private int controlWidth;
    private int controlHeight;
    private bool isDisposed;

    // Render-side CPU-readable staging texture
    private ID3D11Texture2D stagingTexture;
    private int texWidth;
    private int texHeight;

    // CPU frame buffer (BGRA)
    private nint cpuBuffer;
    private int cpuBufferLen;
    private int cpuRowPitch;
    private int cpuWidth;
    private int cpuHeight;
    private bool hasCpuFrame;

    private long frameGeneration;
    private long drawnGeneration;

    private int acquireCount;

    /// <summary>True when a newly acquired frame has not yet been presented.</summary>
    public bool HasPendingFrame
    {
        get { lock (sync) return !isDisposed && hasCpuFrame && frameGeneration != drawnGeneration; }
    }

    public void Initialize(Player player, int controlWidth, int controlHeight)
    {
        this.player = player;
        this.controlWidth = controlWidth;
        this.controlHeight = controlHeight;

        DebugLogger.Print($"[FLB] Initialize control={controlWidth}x{controlHeight} renderLuid={player.Renderer.GPUAdapter?.Luid}");

        player.Renderer.SwapChain.RegisterBeforePresentCallback(OnBeforePresent);
        player.Renderer.SwapChain.SetupWinUI(OnSwapChainUpdated);
    }

    private void OnSwapChainUpdated(IDXGISwapChain2 swapChain)
    {
        try
        {
            lock (sync)
                DropStaging();

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
            if (isDisposed)
                return;

            var swapChain = player?.Renderer?.SwapChain;
            if (swapChain == null || swapChain.Disposed)
                return;

            if (!EnsureStaging(controlWidth, controlHeight))
                return;

            if (!swapChain.CopyBackBufferTo(stagingTexture))
                return;

            if (!ReadbackToCpu())
                return;

            frameGeneration++;

            int n = ++acquireCount;
            if (n <= 3 || n % 120 == 0)
                DebugLogger.Print($"[FLB] acquire #{n} {texWidth}x{texHeight}");
        }
    }

    private bool ReadbackToCpu()
    {
        try
        {
            var context = player.Renderer.Device.ImmediateContext;
            var map = context.Map(stagingTexture, 0);
            try
            {
                int len = (int)map.RowPitch * texHeight;
                EnsureCpuBuffer(len);
                Buffer.MemoryCopy((void*)map.DataPointer, (void*)cpuBuffer, len, len);
                cpuRowPitch = (int)map.RowPitch;
                cpuWidth = texWidth;
                cpuHeight = texHeight;
                hasCpuFrame = true;
                return true;
            }
            finally
            {
                context.Unmap(stagingTexture, 0);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Print($"[FLB] ReadbackToCpu failed: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Copies the latest frame into a locked WriteableBitmap back buffer. Returns
    /// true if a new frame was written (caller should then AddDirtyRect). Must be
    /// called on the UI thread with the bitmap locked.
    /// </summary>
    public bool CopyLatestFrameInto(nint dest, int destStride, int destWidth, int destHeight)
    {
        lock (sync)
        {
            if (isDisposed || !hasCpuFrame || frameGeneration == drawnGeneration)
                return false;

            if (cpuWidth != destWidth || cpuHeight != destHeight || dest == IntPtr.Zero)
                return false;

            int rowBytes = Math.Min(cpuRowPitch, destStride);
            byte* src = (byte*)cpuBuffer;
            byte* dst = (byte*)dest;
            for (int y = 0; y < destHeight; y++)
                Buffer.MemoryCopy(src + (long)y * cpuRowPitch, dst + (long)y * destStride, destStride, rowBytes);

            drawnGeneration = frameGeneration;
            return true;
        }
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
            DropStaging();
        }

        player?.Renderer?.SwapChain?.Resize(width, height);
    }

    private bool EnsureStaging(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return false;

        if (stagingTexture != null && texWidth == width && texHeight == height)
            return true;

        DropStaging();

        stagingTexture = player.Renderer.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });

        texWidth = width;
        texHeight = height;
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

    private void DropStaging()
    {
        stagingTexture?.Dispose();
        stagingTexture = null;
        texWidth = 0;
        texHeight = 0;
        hasCpuFrame = false;
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
            DropStaging();
            if (cpuBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(cpuBuffer);
                cpuBuffer = IntPtr.Zero;
                cpuBufferLen = 0;
            }
        }
    }
}
