using System.Windows;

using Vortice;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

using static FlyleafLib.Utils.NativeMethods;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace FlyleafLib.MediaFramework.MediaRenderer;

unsafe public partial class Renderer
{
    internal bool           forceViewToControl; // Makes sure that viewport will fill the exact same size with the control (mainly for keep ratio on resize)
    volatile bool           canRenderPresent;   // Don't render / present during minimize (or invalid size)

    ID3D11Texture2D         backBuffer;
    ID3D11RenderTargetView  backBufferRtv;
    IDXGISwapChain1         swapChain;
    IDCompositionDevice     dCompDevice;
    IDCompositionVisual     dCompVisual;
    IDCompositionTarget     dCompTarget;

    const uint              WM_MOVE                     = 0x0003;
    const uint              WM_SIZE                     = 0x0005;
    const uint              WM_DISPLAYCHANGE            = 0x007E;
    const uint              WM_NCDESTROY                = 0x0082;
    const int               WS_EX_NOREDIRECTIONBITMAP   = 0x00200000;
    SubclassWndProc         wndProcDelegate;
    IntPtr                  wndProcDelegatePtr;

    Format                  BGRA_OR_RGBA;
    bool                    hasSubClass;
    nint                    displayHwnd;
    GPUOutput               gpuOutput;

    private SwapChainDescription1 GetSwapChainDesc(int width, int height)
        => new()
        {
            BufferUsage         = Usage.RenderTargetOutput,
            Format              = Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : BGRA_OR_RGBA,
            Width               = (uint)width,
            Height              = (uint)height,
            AlphaMode           = AlphaMode.Premultiplied,  // TBR
            SwapEffect          = SwapEffect.FlipDiscard,   
            Scaling             = Scaling.Stretch,          // DComp can't validate widhth/height?*
            BufferCount         = Math.Max(Config.Video.SwapBuffers, 2),
            SampleDescription   = new SampleDescription(1, 0),
            Flags               = SwapChainFlags.None
        };

    internal void InitializeSwapChain(nint handle)
    {
        lock (lockDevice)
        {
            if (!SCDisposed)
                DisposeSwapChain();

            if (Disposed)
                Initialize(false);

            SCDisposed      = false;
            ControlHandle   = handle;
            RECT rect       = new();
            GetWindowRect(ControlHandle, ref rect);
            ControlWidth    = rect.Right  - rect.Left;
            ControlHeight   = rect.Bottom - rect.Top;

            // WS_EX_NOREDIRECTIONBITMAP: Prevent DWM from creating a redirection surface (offscreen bitmap)
            SetWindowLong(handle, (int)WindowLongFlags.GWL_EXSTYLE, GetWindowLong(handle, (int)WindowLongFlags.GWL_EXSTYLE).ToInt32() | WS_EX_NOREDIRECTIONBITMAP);

            try
            {
                Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} swap chain [Handle: {handle}, Buffers: {Config.Video.SwapBuffers}, Format: {(Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : BGRA_OR_RGBA)}]");
                swapChain = Engine.Video.Factory.CreateSwapChainForComposition(Device, GetSwapChainDesc(ControlWidth, ControlHeight));
                DComp.DCompositionCreateDevice(dxgiDevice, out dCompDevice).CheckError();
                dCompDevice.CreateTargetForHwnd(handle, false, out dCompTarget).CheckError();
                dCompDevice.CreateVisual(out dCompVisual).CheckError();
                dCompVisual.SetContent(swapChain).CheckError();
                dCompTarget.SetRoot(dCompVisual).CheckError();
                dCompDevice.Commit().CheckError();
            }
            catch (Exception e) // Should handle device lost etc..
            {
                if (!gpuForceWarp)
                {
                    gpuForceWarp = true;
                    Log.Error($"SwapChain Initialization failed ({e.Message}). Failling back to WARP device.");
                    Flush();
                }
                else
                {
                    ControlHandle = 0;
                    Log.Error($"SwapChain Initialization failed ({e.Message})");
                }

                return;
            }

            backBuffer      = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv   = Device.CreateRenderTargetView(backBuffer);
            Engine.Video.Factory.MakeWindowAssociation(ControlHandle, WindowAssociationFlags.IgnoreAll);
            AddSubClass();
            fillRatio = ControlWidth / (double)ControlHeight; // ResizeBuffers will not trigger it if same width/height as before
            ResizeBuffers(ControlWidth, ControlHeight);
            UpdateDisplay(true); // don't force if we let WndProc run without our swapchain
        }
    }
    internal void InitializeWinUISwapChain()
    {   // TODO: width/height directly here
        lock (lockDevice)
        {
            if (!SCDisposed)
                DisposeSwapChain();

            if (Disposed)
                Initialize(false);

            Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} swap chain with {Config.Video.SwapBuffers} buffers");

            try
            {
                swapChain = Engine.Video.Factory.CreateSwapChainForComposition(Device, GetSwapChainDesc(1, 1));
            }
            catch (Exception e)
            {
                Log.Error($"Initialization failed [{e.Message}]");

                // TODO fallback to WARP?

                SwapChainWinUIClbk?.Invoke(null);
                return;
            }

            backBuffer      = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv   = Device.CreateRenderTargetView(backBuffer);
            SCDisposed      = false;
            ResizeBuffers(1, 1);
            UpdateDisplay(true); // TODO: Parent Handle for WndProc and changes? (currently get's default monitor)
            SwapChainWinUIClbk?.Invoke(swapChain.QueryInterface<IDXGISwapChain2>());
        }
    }

    public void DisposeSwapChain()
    {
        lock (lockDevice)
        {
            if (SCDisposed)
                return;

            SCDisposed = true;

            lock (lockRenderLoops)
            {
                canRenderPresent= false;
                needsResize     = needsViewport = false;
            }

            while(isIdlePresenting) Thread.Sleep(1);
            // StopPlayer (if it does not do it already before?*)

            // TBR: Clear Screan (Disposing dCompTarget will do it)
            //try
            //{
            //    context.OMSetRenderTargets(backBufferRtv);
            //    context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
            //    swapChain.Present(1, PresentFlags.None);
            //}
            //catch { }

            Log.Info($"Destroying swap chain [Handle: {ControlHandle}]");

            if (ControlHandle != 0)
            {
                if (!isFlushing) // Avoid UI Remove/Add subclass during flush
                    RemoveSubClass();
                ControlHandle = 0;
            }

            if (dCompVisual != null)
            {
                dCompVisual.SetContent(null);
                dCompVisual.Dispose();
                dCompVisual = null;
            }

            if (dCompTarget != null)
            {
                dCompTarget.SetRoot(null);
                dCompTarget.Dispose();
                dCompTarget = null;
            }

            if (SwapChainWinUIClbk != null)
            {
                SwapChainWinUIClbk.Invoke(null);
                SwapChainWinUIClbk = null;
                swapChain?.Release(); // TBR: SwapChainPanel (SCP) should be disposed and create new instance instead (currently used from Template)
            }

            vpov?.          Dispose();
            backBufferRtv?. Dispose();
            backBuffer?.    Dispose();
            swapChain?.     Dispose();

            if (dCompDevice != null)
            {
                dCompDevice.Dispose();
                dCompDevice = null;
            }

            if (Device != null)
                context?.Flush();
        }
    }

    public void RemoveViewportOffsets(ref Point p)
    {
        p.X -= (SideXPixels / 2 + PanXOffset);
        p.Y -= (SideYPixels / 2 + PanYOffset);
    }
    
    public static double GetCenterPoint(double zoom, double offset)
        => zoom == 1 ? offset : offset / (zoom - 1); // possible bug when zoom = 1 (noticed in out of bounds zoom out)

    /// <summary>
    /// Zooms in a way that the specified point before zoom will be at the same position after zoom
    /// </summary>
    /// <param name="p"></param>
    /// <param name="zoom"></param>
    public void ZoomWithCenterPoint(Point p, double zoom)
    {
        /* Notes
         *
         * Zoomed Point (ZP)    // the current point in a -possible- zoomed viewport
         * Zoom (Z)
         * Unzoomed Point (UP)  // the actual pixel of the current point
         * Viewport Point (VP)
         * Center Point (CP)
         *
         * UP = (VP + ZP) / Z =>
         * ZP = (UP * Z) - VP
         * CP = VP / (ZP - 1) (when UP = ZP)
         */

        Viewport view = GetViewport;

        if (!(p.X >= view.X && p.X < view.X + view.Width && p.Y >= view.Y && p.Y < view.Y + view.Height)) // Point out of view
        {
            SetZoom(zoom);
            return;
        }

        Point viewport = new(view.X, view.Y);
        RemoveViewportOffsets(ref viewport);
        RemoveViewportOffsets(ref p);

        // Finds the required center point so that p will have the same pixel after zoom
        Point zoomCenter = new(
            GetCenterPoint(zoom, ((p.X - viewport.X) / (this.zoom / zoom)) - p.X) / (view.Width / this.zoom),
            GetCenterPoint(zoom, ((p.Y - viewport.Y) / (this.zoom / zoom)) - p.Y) / (view.Height / this.zoom));

        SetZoomAndCenter(zoom, zoomCenter);
    }

    bool needsViewport;
    public void SetViewport(bool refresh = true)
    {
        lock (lockRenderLoops)
        {
            if (SCDisposed)
                return;

            needsViewport   = true;
            canRenderPresent= true; // TBR: should be re-calculated

            if (refresh)
                RenderRequest();
        }
    }
    public void SetViewportInternal()
    {
        if (!needsViewport)
            return;

        needsViewport = false;

        int x, y, newWidth, newHeight, xZoomPixels, yZoomPixels;

        var shouldFill = player?.Host?.Player_HandlesRatioResize(ControlWidth, ControlHeight);

        if (curRatio < fillRatio)
        {
            newHeight   = (int)(ControlHeight * zoom);
            newWidth    = (shouldFill.HasValue && shouldFill.Value) ? (int)(ControlWidth * zoom) : (int)(newHeight * curRatio);

            SideXPixels = ((int) (ControlWidth - (ControlHeight * curRatio))) & ~1;
            SideYPixels = 0;

            y = PanYOffset;
            x = PanXOffset + SideXPixels / 2;

            yZoomPixels = newHeight - ControlHeight;
            xZoomPixels = newWidth - (ControlWidth - SideXPixels);
        }
        else
        {
            newWidth    = (int)(ControlWidth * zoom);
            newHeight   = (shouldFill.HasValue && shouldFill.Value) || curRatio == fillRatio ? (int)(ControlHeight * zoom) : (int)(newWidth / curRatio);

            SideYPixels = ((int) (ControlHeight - (ControlWidth / curRatio))) & ~1;
            SideXPixels = 0;

            x = PanXOffset;
            y = PanYOffset + SideYPixels / 2;

            xZoomPixels = newWidth - ControlWidth;
            yZoomPixels = newHeight - (ControlHeight - SideYPixels);
        }

        GetViewport = new((int)(x - xZoomPixels * (float)zoomCenter.X), (int)(y - yZoomPixels * (float)zoomCenter.Y), newWidth, newHeight);
        
        if (videoProcessor == VideoProcessors.D3D11)
        {
            Viewport view = GetViewport;

            if (!Config.Video.SuperResolution)
                DisableSuperRes();
            else
            {
                if (((_RotationAngle ==  0 || _RotationAngle == 180) && view.Width > VisibleWidth  && view.Height > VisibleHeight) ||
                    ((_RotationAngle == 90 || _RotationAngle == 270) && view.Width > VisibleHeight && view.Height > VisibleWidth))
                    EnableSuperRes();
                else
                    DisableSuperRes();
            }

            int right   = (int)(view.X + view.Width);
            int bottom  = (int)(view.Y + view.Height);

            if (view.Width < 1 || view.Y >= ControlHeight || view.X >= ControlWidth || bottom <= 0 || right <= 0)
            {
                canRenderPresent = false;
                return;
            }

            RawRect dst = new(
                    Math.Max((int)view.X, 0),
                    Math.Max((int)view.Y, 0),
                    Math.Min(right, ControlWidth),
                    Math.Min(bottom, ControlHeight));
            
            double croppedWidth     = VideoRect.Right   - (cropRect.Right  + cropRect.Left);
            double croppedHeight    = VideoRect.Bottom  - (cropRect.Bottom + cropRect.Top);
            int dstWidth    = dst.Right  - dst.Left;
            int dstHeight   = dst.Bottom - dst.Top;

            int     cropLeft,   cropTop,    cropRight,  cropBottom;
            int     srcLeft,    srcTop,     srcRight,   srcBottom;
            double  scaleX,     scaleY,     scaleXRot,  scaleYRot;

            if (_RotationAngle == 0)
            {
                cropLeft    = view.X < 0 ? (int)(-view.X) : 0;
                cropTop     = view.Y < 0 ? (int)(-view.Y) : 0;

                scaleX      = croppedWidth  / view.Width;
                scaleY      = croppedHeight / view.Height;

                srcLeft     = (int)(cropRect.Left + cropLeft * scaleX);
                srcTop      = (int)(cropRect.Top  + cropTop  * scaleY);
                srcRight    = srcLeft + (int)(dstWidth  * scaleX);
                srcBottom   = srcTop  + (int)(dstHeight * scaleY);
            }
            else if (_RotationAngle == 180)
            {
                cropRight   = right  > ControlWidth  ? right  - ControlWidth  : 0;
                cropBottom  = bottom > ControlHeight ? bottom - ControlHeight : 0;

                scaleX      = croppedWidth  / view.Width;
                scaleY      = croppedHeight / view.Height;
                
                srcLeft     = (int)(cropRect.Left + cropRight  * scaleX);
                srcTop      = (int)(cropRect.Top  + cropBottom * scaleY);
                srcRight    = srcLeft + (int)(dstWidth  * scaleX);
                srcBottom   = srcTop  + (int)(dstHeight * scaleY);
            }
            else if (_RotationAngle == 90)
            {
                cropTop     = view.Y < 0 ? (int)(-view.Y) : 0;
                cropRight   = right > ControlWidth ? right - ControlWidth : 0;

                scaleXRot   = croppedWidth  / view.Height;
                scaleYRot   = croppedHeight / view.Width;
                
                srcLeft     = (int)(cropRect.Left + cropTop    * scaleXRot);
                srcTop      = (int)(cropRect.Top  + cropRight  * scaleYRot);
                srcRight    = srcLeft + (int)(dstHeight * scaleXRot);
                srcBottom   = srcTop  + (int)(dstWidth  * scaleYRot);
            }
            else if (_RotationAngle == 270)
            {
                cropLeft    = view.X < 0 ? (int)(-view.X) : 0;
                cropBottom  = bottom > ControlHeight ? bottom - ControlHeight : 0;

                scaleXRot   = croppedWidth  / view.Height;
                scaleYRot   = croppedHeight / view.Width;
                
                srcLeft     = (int)(cropRect.Left + cropBottom * scaleXRot);
                srcTop      = (int)(cropRect.Top  + cropLeft   * scaleYRot);
                srcRight    = srcLeft + (int)(dstHeight * scaleXRot);
                srcBottom   = srcTop  + (int)(dstWidth  * scaleYRot);
            }
            else
                srcLeft = srcTop = srcRight = srcBottom = 0;
            
            RawRect src = new(
                Math.Max(srcLeft, 0),
                Math.Max(srcTop , 0),
                Math.Min(srcRight , VideoRect.Right),
                Math.Min(srcBottom, VideoRect.Bottom));
            
            vc.VideoProcessorSetStreamSourceRect(vp, 0, true, src);
            vc.VideoProcessorSetStreamDestRect  (vp, 0, true, dst);
            vc.VideoProcessorSetOutputTargetRect(vp, true, new(0, 0, ControlWidth, ControlHeight));
        }
        else
            context.RSSetViewport(GetViewport);

        canRenderPresent = true;
        ViewportChanged?.Invoke(this, new());
    }

    bool needsResize;
    public void ResizeBuffers(int width, int height)
    {   // TBR: Fast way from resize (no locks? to avoid possible delay)
        lock (lockRenderLoops)
        {
            if (SCDisposed || width <= 0 || height <= 0)
            {
                canRenderPresent = false;
                return;
            }
            else if (ControlWidth == width && ControlHeight == height)
            {
                // Re-calculate of canRenderPresent
                if (videoProcessor == VideoProcessors.D3D11)
                {
                    Viewport view   = GetViewport;
                    int right       = (int)(view.X + view.Width);
                    int bottom      = (int)(view.Y + view.Height);

                    if (view.Width < 1 || view.Y >= ControlHeight || view.X >= ControlWidth || bottom <= 0 || right <= 0)
                        canRenderPresent = false;
                    else
                        canRenderPresent = true;
                }
                else
                    canRenderPresent = true;

                //RenderRequest(); // We don't refresh as we consider same view
                return;
            }
            
            ControlWidth    = width;
            ControlHeight   = height;
            needsResize     = true;

            RenderRequest();
        }
    }
    void ResizeBuffersInternal()
    {
        if (!needsResize)
            return;

        needsResize     = false;
        needsViewport   = true;

        if (use2d)
        {
            context2d.Target = null;
            bitmap2d?.Dispose();
            bitmap2d = null;
        }

        fillRatio = ControlWidth / (double)ControlHeight;
        if (Config.Video.AspectRatio == AspectRatio.Fill)
            curRatio = fillRatio;

        backBufferRtv.Dispose();
        vpov        ?.Dispose();
        backBuffer   .Dispose();

        swapChain.ResizeBuffers(0, (uint)ControlWidth, (uint)ControlHeight, Format.Unknown, SwapChainFlags.None);

        if (cornerRadiusNeedsUpdate)
            UpdateCornerRadiusInternal();

        backBuffer      = swapChain.GetBuffer<ID3D11Texture2D>(0);
        backBufferRtv   = Device.CreateRenderTargetView(backBuffer);
        if (videoProcessor == VideoProcessors.D3D11)
            vd1.CreateVideoProcessorOutputView(backBuffer, vpe, vpovd, out vpov);

        if (use2d)
        {
            using var surface = backBuffer.QueryInterface<IDXGISurface>();
            bitmap2d = context2d.CreateBitmapFromDxgiSurface(surface, bitmapProps2d);
            context2d.Target = bitmap2d;
        }
    }

    void UpdateCornerRadius(CornerRadius cornerRadius)
    {
        lock (lockDevice)
        {
            if (this.cornerRadius == cornerRadius)
                return;

            this.cornerRadius = cornerRadius;
            cornerRadiusNeedsUpdate = true;

            if (!SCDisposed)
                UpdateCornerRadiusInternal();
        }
    }
    void UpdateCornerRadiusInternal()
    {
        try
        {
            dCompDevice.CreateRectangleClip(out var clip).CheckError();
            clip.SetLeft                (0);
            clip.SetRight               (ControlWidth);
            clip.SetTop                 (0);
            clip.SetBottom              (ControlHeight);
            clip.SetTopLeftRadiusX      ((float)cornerRadius.TopLeft);
            clip.SetTopLeftRadiusY      ((float)cornerRadius.TopLeft);
            clip.SetTopRightRadiusX     ((float)cornerRadius.TopRight);
            clip.SetTopRightRadiusY     ((float)cornerRadius.TopRight);
            clip.SetBottomLeftRadiusX   ((float)cornerRadius.BottomLeft);
            clip.SetBottomLeftRadiusY   ((float)cornerRadius.BottomLeft);
            clip.SetBottomRightRadiusX  ((float)cornerRadius.BottomRight);
            clip.SetBottomRightRadiusY  ((float)cornerRadius.BottomRight);
            dCompVisual.SetClip(clip).CheckError();
            clip.Dispose();
            dCompDevice.Commit().CheckError();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to set CornerRadius = {cornerRadius} ({e.Message})");
        }
    }

    void UpdateDisplay(bool force = false)
    {
        nint newDisplayHwnd = MonitorFromWindow(ControlHandle, MonitorOptions.MONITOR_DEFAULTTONEAREST);
        if (displayHwnd == newDisplayHwnd && !force)
            return;

        displayHwnd = newDisplayHwnd;

        var displays = Engine.Video.GetGPUOutputs(dxgiAdapter);
        foreach(var display in displays)
            if (displayHwnd == display.Hwnd)
            {
                gpuOutput = display;
                Config.Video.MaxVerticalResolutionAuto  = display.Height;
                Config.Video.SDRDisplayNitsAuto         = display.MaxLuminance;

                // currently not used (int accurate instead of double)
                //refreshRateTicks = (int)((1.0 / display.RefreshRate) * 1000 * 10000);
                if (CanDebug) Log.Debug($"{display}");
                return;
            }
    }

    void AddSubClass()
    {
        if (!hasSubClass)
        {
            hasSubClass = true;
            if (Environment.CurrentManagedThreadId == Application.Current.Dispatcher.Thread.ManagedThreadId)
                SetWindowSubclass(ControlHandle, wndProcDelegatePtr, 0, 0);
            else
                UI(() => SetWindowSubclass(ControlHandle, wndProcDelegatePtr, 0, 0));
        }
    }
    void RemoveSubClass()
    {
        if (hasSubClass)
        {
            hasSubClass = false;
            if (Environment.CurrentManagedThreadId == Application.Current.Dispatcher.Thread.ManagedThreadId)
                RemoveWindowSubclass(ControlHandle, wndProcDelegatePtr, 0);
            else
                UI(() => RemoveWindowSubclass(ControlHandle, wndProcDelegatePtr, 0));
        }
    }
    IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (msg)
        {
            case WM_NCDESTROY:
                if (SCDisposed)
                    RemoveSubClass();
                else
                    DisposeSwapChain();
                break;

            // TODO: currently disabled for performance (we only change recommeded resolution/sdrnits) | when more added (Dpi/HDR native etc.)
            //case WM_MOVE:
            //    UpdateDisplay();
            //    break;

            case WM_DISPLAYCHANGE: // top-level window only (any display) - should refresh all and check if current changed
                UpdateDisplay(true);
                break;

            case WM_SIZE:
                ResizeBuffers(SignedLOWORD(lParam), SignedHIWORD(lParam));
                break;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
