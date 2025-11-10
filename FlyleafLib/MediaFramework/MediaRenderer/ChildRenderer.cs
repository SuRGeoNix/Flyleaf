using System.Windows;

using Vortice;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

using FlyleafLib.MediaFramework.MediaFrame;

using static FlyleafLib.Utils.NativeMethods;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

using FlyleafLib;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>
/// Child renderer that shares the D3D11 device with the main renderer and presents frames to its own swap chain
/// Supports individual pan, zoom, and rotation settings without affecting the main renderer
/// </summary>
public class ChildRenderer : IDisposable
{
    #region Properties
    public int              UniqueId        { get; private set; }
    public int              ControlWidth    { get; private set; }
    public int              ControlHeight   { get; private set; }
    public bool             Disposed        { get; private set; } = true;
    public bool             IsActive        { get; private set; }

    public Viewport         GetViewport     { get; private set; }
    public event EventHandler ViewportChanged;

    // Pan/Zoom/Rotation support (independent from main renderer)
    public int              PanXOffset      { get => panXOffset;                set => SetPanX(value); }
    int panXOffset;

    public int              PanYOffset      { get => panYOffset;                set => SetPanY(value); }
    int panYOffset;

    public double           Zoom            { get => zoom;                      set => SetZoom(value); }
    double zoom = 1;

    public Point            ZoomCenter      { get => zoomCenter;                set => SetZoomCenter(value); }
    Point zoomCenter = new(0.5, 0.5);

    public uint             Rotation        { get => rotation;                  set => UpdateRotation(value); }
    uint rotation;

    public bool             HFlip           { get => hFlip;                     set { hFlip = value; UpdateRotation(rotation); } }
    bool hFlip;

    public bool             VFlip           { get => vFlip;                     set { vFlip = value; UpdateRotation(rotation); } }
    bool vFlip;

    public CornerRadius     CornerRadius    { get => cornerRadius;              set => UpdateCornerRadius(value); }
    CornerRadius cornerRadius = new(0);
    #endregion

    #region Internal Fields
    internal nint           ControlHandle;
    internal Renderer       ParentRenderer;

    IDXGISwapChain1         swapChain;
    ID3D11Texture2D         backBuffer;
    ID3D11RenderTargetView  backBufferRtv;
    IDCompositionDevice     dCompDevice;
    IDCompositionVisual     dCompVisual;
    IDCompositionTarget     dCompTarget;

    volatile bool           canRenderPresent;
    bool                    needsResize;
    bool                    needsViewport;

    object                  lockRender = new();

    // For viewport calculation
    uint                    visibleWidth;
    uint                    visibleHeight;
    double                  curRatio, keepRatio, fillRatio;
    int                     sideXPixels;
    int                     sideYPixels;
    uint                    textWidth, textHeight;
    CropRect                cropRect;
    VideoProcessorRotation  d3d11vpRotation = VideoProcessorRotation.Identity;
    #endregion

    public ChildRenderer(Renderer parentRenderer, nint handle, int uniqueId = -1)
    {
        ParentRenderer  = parentRenderer;
        ControlHandle   = handle;
        UniqueId        = uniqueId == -1 ? GetUniqueId() : uniqueId;
    }

    static int curUniqueId;
    static int GetUniqueId() => curUniqueId++;

    public void Initialize()
    {
        lock (lockRender)
        {
            if (!Disposed)
                Dispose();

            Disposed = false;

            RECT rect = new();
            GetWindowRect(ControlHandle, ref rect);
            ControlWidth  = rect.Right  - rect.Left;
            ControlHeight = rect.Bottom - rect.Top;

            try
            {
                var swapChainDesc = GetSwapChainDesc(2, 2);
                swapChain = Engine.Video.Factory.CreateSwapChainForComposition(ParentRenderer.Device, swapChainDesc);
                
                DComp.DCompositionCreateDevice(ParentRenderer.dxgiDevice, out dCompDevice).CheckError();
                dCompDevice.CreateTargetForHwnd(ControlHandle, false, out dCompTarget).CheckError();
                dCompDevice.CreateVisual(out dCompVisual).CheckError();
                dCompVisual.SetContent(swapChain).CheckError();
                dCompTarget.SetRoot(dCompVisual).CheckError();
                dCompDevice.Commit().CheckError();

                backBuffer    = swapChain.GetBuffer<ID3D11Texture2D>(0);
                backBufferRtv = ParentRenderer.Device.CreateRenderTargetView(backBuffer);
                
                Engine.Video.Factory.MakeWindowAssociation(ControlHandle, WindowAssociationFlags.IgnoreAll);

                IsActive = true;
                canRenderPresent = true;
                needsResize = true;
            }
            catch (Exception e)
            {
                ParentRenderer.Log?.Error($"[ChildRenderer #{UniqueId}] Initialization failed: {e.Message}");
                Dispose();
                throw;
            }
        }
    }

    private SwapChainDescription1 GetSwapChainDesc(int width, int height)
        => new()
        {
            BufferUsage         = Usage.RenderTargetOutput,
            Format              = ParentRenderer.Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : (ParentRenderer.Config.Video.SwapForceR8G8B8A8 ? Format.R8G8B8A8_UNorm : Format.B8G8R8A8_UNorm),
            Width               = (uint)width,
            Height              = (uint)height,
            AlphaMode           = AlphaMode.Premultiplied,
            SwapEffect          = SwapEffect.FlipDiscard,
            Scaling             = Scaling.Stretch,
            BufferCount         = Math.Max(ParentRenderer.Config.Video.SwapBuffers, 2),
            SampleDescription   = new SampleDescription(1, 0),
            Flags               = SwapChainFlags.None
        };

    public void Resize(int width, int height)
    {
        lock (lockRender)
        {
            if (Disposed || !IsActive)
                return;

            if (width == ControlWidth && height == ControlHeight)
                return;

            ControlWidth  = width;
            ControlHeight = height;
            needsResize   = true;
            canRenderPresent = width > 0 && height > 0;
        }
    }

    private void ResizeBuffersInternal()
    {
        if (!needsResize)
            return;

        needsResize = false;

        if (ControlWidth <= 0 || ControlHeight <= 0)
        {
            canRenderPresent = false;
            return;
        }

        backBufferRtv?.Dispose();
        backBuffer?.Dispose();

        swapChain.ResizeBuffers(0, (uint)ControlWidth, (uint)ControlHeight, Format.Unknown, SwapChainFlags.None).CheckError();
        
        backBuffer    = swapChain.GetBuffer<ID3D11Texture2D>(0);
        backBufferRtv = ParentRenderer.Device.CreateRenderTargetView(backBuffer);
        
        canRenderPresent = true;
        needsViewport = true;
    }

    #region Pan/Zoom/Rotation Methods
    public void SetPanX(int panX, bool refresh = true)
    {
        panXOffset = panX;
        SetViewport(refresh);
    }

    public void SetPanY(int panY, bool refresh = true)
    {
        panYOffset = panY;
        SetViewport(refresh);
    }

    public void SetZoom(double zoom, bool refresh = true)
    {
        this.zoom = zoom;
        SetViewport(refresh);
    }

    public void SetZoomCenter(Point p, bool refresh = true)
    {
        zoomCenter = p;
        SetViewport(refresh);
    }

    public void SetZoomAndCenter(double zoom, Point p, bool refresh = true)
    {
        this.zoom = zoom;
        zoomCenter = p;
        SetViewport(refresh);
    }

    public void SetPanAll(int panX, int panY, uint rotation, double zoom, Point p, bool refresh = true)
    {
        panXOffset = panX;
        panYOffset = panY;
        this.zoom = zoom;
        zoomCenter = p;
        UpdateRotation(rotation, false);
        SetViewport(refresh);
    }

    private void UpdateRotation(uint angle, bool refresh = true)
    {
        rotation = angle;
        
        // Map rotation to D3D11 VideoProcessor rotation
        d3d11vpRotation = (rotation, hFlip, vFlip) switch
        {
            (0, false, false)   => VideoProcessorRotation.Identity,
            (90, false, false)  => VideoProcessorRotation.Rotation90,
            (180, false, false) => VideoProcessorRotation.Rotation180,
            (270, false, false) => VideoProcessorRotation.Rotation270,
            _ => VideoProcessorRotation.Identity
        };

        if (refresh)
            SetViewport();
    }

    private void UpdateCornerRadius(CornerRadius value)
    {
        cornerRadius = value;
        // Corner radius implementation would go here
    }

    public void SetViewport(bool refresh = true)
    {
        lock (lockRender)
        {
            if (Disposed || !IsActive)
                return;

            needsViewport = true;
        }
    }

    private void SetViewportInternal()
    {
        if (!needsViewport)
            return;

        needsViewport = false;

        // Get video dimensions from parent renderer
        visibleWidth  = ParentRenderer.VisibleWidth;
        visibleHeight = ParentRenderer.VisibleHeight;

        if (visibleWidth == 0 || visibleHeight == 0)
        {
            GetViewport = new Viewport(0, 0, ControlWidth, ControlHeight);
            ViewportChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Calculate aspect ratios
        curRatio  = (double)visibleWidth / visibleHeight;
        keepRatio = (double)ControlWidth / ControlHeight;
        fillRatio = keepRatio;

        int x, y, newWidth, newHeight, xZoomPixels, yZoomPixels;

        if (curRatio < fillRatio)
        {
            newHeight = ControlHeight;
            newWidth  = (int)(visibleWidth * ControlHeight / visibleHeight);
            sideXPixels = ControlWidth - newWidth;
            sideYPixels = 0;
        }
        else
        {
            newWidth  = ControlWidth;
            newHeight = (int)(visibleHeight * ControlWidth / visibleWidth);
            sideXPixels = 0;
            sideYPixels = ControlHeight - newHeight;
        }

        // Apply zoom
        xZoomPixels = (int)((newWidth  * zoom) - newWidth);
        yZoomPixels = (int)((newHeight * zoom) - newHeight);

        newWidth  += xZoomPixels;
        newHeight += yZoomPixels;

        // Apply zoom center
        x = (int)((sideXPixels / 2) - (xZoomPixels * zoomCenter.X));
        y = (int)((sideYPixels / 2) - (yZoomPixels * zoomCenter.Y));

        // Apply pan offsets
        x += panXOffset;
        y += panYOffset;

        GetViewport = new Viewport(x, y, newWidth, newHeight);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }
    #endregion

    /// <summary>
    /// Renders the current frame from the parent renderer to this child's swap chain
    /// Should be called by the parent renderer during its render loop
    /// </summary>
    internal void RenderFrame(VideoFrame frame, bool secondField)
    {
        lock (lockRender)
        {
            if (Disposed || !IsActive || !canRenderPresent)
                return;

            try
            {
                ResizeBuffersInternal();
                SetViewportInternal();

                if (!canRenderPresent)
                    return;

                var context = ParentRenderer.context;

                // Set our render target
                context.OMSetRenderTargets(backBufferRtv);
                context.ClearRenderTargetView(backBufferRtv, ParentRenderer.Config.Video._BackgroundColor);

                // Set our viewport
                context.RSSetViewport(GetViewport);

                // If frame uses Flyleaf shaders (has shader resource views)
                if (frame.srvs != null)
                {
                    // Use shader resource views directly - this is the efficient path
                    context.PSSetShaderResources(0, frame.srvs);
                    context.Draw(6, 0);
                }
                else
                {
                    // For D3D11 Video Processor output, we need the parent to render to backBuffer first
                    // Then we copy from parent's backBuffer to ours
                    // This is less efficient but necessary for D3D11VP path
                    // The parent renderer will already have rendered to its backBuffer
                    // We just need to copy it with our viewport
                    
                    // Note: The parent's backBuffer should already have the rendered frame
                    // We'll create an SRV from parent's backBuffer and render it to ours
                    using var parentBackBufferSrv = ParentRenderer.Device.CreateShaderResourceView(ParentRenderer.backBuffer);
                    context.PSSetShaderResources(0, new[] { parentBackBufferSrv });
                    context.Draw(6, 0);
                }

                // Restore parent's viewport
                context.RSSetViewport(ParentRenderer.GetViewport);
            }
            catch (Exception e)
            {
                ParentRenderer.Log?.Error($"[ChildRenderer #{UniqueId}] RenderFrame failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Presents the rendered frame to the screen
    /// Should be called by the parent renderer after all child renderers have rendered
    /// </summary>
    internal void Present()
    {
        lock (lockRender)
        {
            if (Disposed || !IsActive || !canRenderPresent)
                return;

            try
            {
                swapChain.Present(ParentRenderer.Config.Video.VSync, PresentFlags.None).CheckError();
            }
            catch (Exception e)
            {
                ParentRenderer.Log?.Error($"[ChildRenderer #{UniqueId}] Present failed: {e.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (lockRender)
        {
            if (Disposed)
                return;

            Disposed = true;
            IsActive = false;

            backBufferRtv?.Dispose();
            backBuffer?.Dispose();
            swapChain?.Dispose();
            
            if (dCompDevice != null)
            {
                dCompDevice.Dispose();
                dCompDevice = null;
            }

            dCompVisual?.Dispose();
            dCompTarget?.Dispose();
        }
    }
}
