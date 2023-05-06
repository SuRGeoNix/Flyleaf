using System;

using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

using static FlyleafLib.Utils.NativeMethods;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public partial class Renderer
{
    ID3D11Texture2D                         backBuffer;
    ID3D11RenderTargetView                  backBufferRtv;
    IDXGISwapChain1                         swapChain;
    IDCompositionDevice                     dCompDevice;
    IDCompositionVisual                     dCompVisual;
    IDCompositionTarget                     dCompTarget;

    const Int32         WM_NCDESTROY= 0x0082;
    const Int32         WM_SIZE     = 0x0005;
    const Int32         WS_EX_NOREDIRECTIONBITMAP
                                    = 0x00200000;
    SubclassWndProc     wndProcDelegate;
    IntPtr              wndProcDelegatePtr;

    // Support Windows 8+
    private SwapChainDescription1 GetSwapChainDesc(int width, int height, bool isComp = false, bool alpha = false)
    {
        if (Device.FeatureLevel < FeatureLevel.Level_10_0 || (!string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) && Config.Video.GPUAdapter.ToUpper() == "WARP"))
        {
            return new()
            {
                BufferUsage = Usage.RenderTargetOutput,
                Format      = Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm,
                Width       = width,
                Height      = height,
                AlphaMode   = AlphaMode.Ignore,
                SwapEffect  = isComp ? SwapEffect.FlipSequential : SwapEffect.Discard, // will this work for warp?
                Scaling     = Scaling.Stretch,
                BufferCount = 1,
                SampleDescription = new SampleDescription(1, 0)
            };
        }
        else
        {
            SwapEffect swapEffect = isComp ? SwapEffect.FlipSequential : Environment.OSVersion.Version.Major >= 10 ? SwapEffect.FlipDiscard : SwapEffect.FlipSequential;

            return new()
            {
                BufferUsage = Usage.RenderTargetOutput,
                Format      = Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : (Config.Video.SwapForceR8G8B8A8 ? Format.R8G8B8A8_UNorm : Format.B8G8R8A8_UNorm),
                Width       = width,
                Height      = height,
                AlphaMode   = alpha  ? AlphaMode.Premultiplied : AlphaMode.Ignore,
                SwapEffect  = swapEffect,
                Scaling     = isComp ? Scaling.Stretch : Scaling.None,
                BufferCount = swapEffect == SwapEffect.FlipDiscard ? Math.Min(Config.Video.SwapBuffers, 2) : Config.Video.SwapBuffers,
                SampleDescription = new SampleDescription(1, 0),
            };
        }
    }

    internal void InitializeSwapChain(IntPtr handle)
    {
        lock (lockDevice)
        {
            if (!SCDisposed)
                DisposeSwapChain();

            if (Disposed && myReplica == null)
                Initialize(false);
            
            ControlHandle   = handle;
            RECT rect       = new();
            GetWindowRect(ControlHandle,ref rect);
            ControlWidth    = rect.Right  - rect.Left;
            ControlHeight   = rect.Bottom - rect.Top;

            try
            {
                if (cornerRadius == zeroCornerRadius)
                {
                    Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} swap chain with {Config.Video.SwapBuffers} buffers [Handle: {handle}]");
                    swapChain = Engine.Video.Factory.CreateSwapChainForHwnd(Device, handle, GetSwapChainDesc(ControlWidth, ControlHeight));
                }
                else
                {
                    Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} composition swap chain with {Config.Video.SwapBuffers} buffers [Handle: {handle}]");
                    swapChain = Engine.Video.Factory.CreateSwapChainForComposition(Device, GetSwapChainDesc(ControlWidth, ControlHeight, true, true));
                    using (var dxgiDevice = Device.QueryInterface<IDXGIDevice>())
                    dCompDevice = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
                    dCompDevice.CreateTargetForHwnd(handle, false, out dCompTarget).CheckError();
                    dCompDevice.CreateVisual(out dCompVisual).CheckError();
                    dCompVisual.SetContent(swapChain).CheckError();
                    dCompTarget.SetRoot(dCompVisual).CheckError();
                    dCompDevice.Commit().CheckError();

                    int styleEx = GetWindowLong(handle, (int)WindowLongFlags.GWL_EXSTYLE).ToInt32() | WS_EX_NOREDIRECTIONBITMAP;
                    SetWindowLong(handle, (int)WindowLongFlags.GWL_EXSTYLE, new IntPtr(styleEx));
                }
            }
            catch (Exception e)
            {
                if (string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) || Config.Video.GPUAdapter.ToUpper() != "WARP")
                {
                    try { if (Device != null) Log.Warn($"Device Remove Reason = {Device.DeviceRemovedReason.Description}"); } catch { } // For troubleshooting
                        
                    Log.Warn($"[SwapChain] Initialization failed ({e.Message}). Failling back to WARP device.");
                    Config.Video.GPUAdapter = "WARP";
                    Flush();
                }
                else
                {
                    ControlHandle = IntPtr.Zero;
                    Log.Error($"[SwapChain] Initialization failed ({e.Message})");
                }

                return;
            }
            
            backBuffer      = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv   = Device.CreateRenderTargetView(backBuffer);
            SCDisposed      = false;

            if (!isFlushing) // avoid calling UI thread during Player.Stop
            {
                // SetWindowSubclass seems to require UI thread when RemoveWindowSubclass does not (docs are not mentioning this?)
                if (System.Threading.Thread.CurrentThread.ManagedThreadId == System.Windows.Application.Current.Dispatcher.Thread.ManagedThreadId)
                    SetWindowSubclass(ControlHandle, wndProcDelegatePtr, UIntPtr.Zero, UIntPtr.Zero);
                else
                    Utils.UI(() => SetWindowSubclass(ControlHandle, wndProcDelegatePtr, UIntPtr.Zero, UIntPtr.Zero));
            }

            Engine.Video.Factory.MakeWindowAssociation(ControlHandle, WindowAssociationFlags.IgnoreAll);

            ResizeBuffers(ControlWidth, ControlHeight); // maybe not required (only for vp)?
        }
    }
    internal void InitializeWinUISwapChain() // TODO: width/height directly here
    {
        lock (lockDevice)
        {
            if (!SCDisposed)
                DisposeSwapChain();

            if (Disposed)
                Initialize(false);

            Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} swap chain with {Config.Video.SwapBuffers} buffers");

            try
            {
                swapChain = Engine.Video.Factory.CreateSwapChainForComposition(Device, GetSwapChainDesc(1, 1, true));
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

            // Clear Screan
            if (!Disposed && swapChain != null)
            {
                try
                {
                    context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                    swapChain.Present(Config.Video.VSync, PresentFlags.None);
                }
                catch { }
            }

            Log.Info($"Destroying swap chain [Handle: {ControlHandle}]");

            // Unassign renderer's WndProc if still there and re-assign the old one
            if (ControlHandle != IntPtr.Zero)
            {
                if (!isFlushing) // SetWindowSubclass requires UI thread so avoid calling it on flush (Player.Stop)
                    RemoveWindowSubclass(ControlHandle, wndProcDelegatePtr, UIntPtr.Zero);
                ControlHandle = IntPtr.Zero;
            }

            SwapChainWinUIClbk = null;

            dCompVisual?.Dispose();
            dCompTarget?.Dispose();
            dCompDevice?.Dispose();
            dCompVisual = null;
            dCompTarget = null;
            dCompDevice = null;

            vpov?.Dispose();
            backBufferRtv?.Dispose();
            backBuffer?.Dispose();
            swapChain?.Dispose();

            if (Device != null)
                context?.Flush();
        }
    }

    public void SetViewport(bool refresh = true)
    {
        float ratio;

        if (Config.Video.AspectRatio == AspectRatio.Keep)
            ratio = curRatio;
        else ratio = Config.Video.AspectRatio == AspectRatio.Fill
            ? ControlWidth / (float)ControlHeight
            : Config.Video.AspectRatio == AspectRatio.Custom ? Config.Video.CustomAspectRatio.Value : Config.Video.AspectRatio.Value;

        if (ratio <= 0) ratio = 1;

        if (actualRotation == 90 || actualRotation == 270)
            ratio = 1 / ratio;

        if (ratio < ControlWidth / (float)ControlHeight)
        {
            int yZoomPixels = (int)(ControlHeight * zoom/100.0) - ControlHeight;
            int Height = ControlHeight + yZoomPixels;
            GetViewport = new Viewport(((ControlWidth - (ControlHeight * ratio)) / 2) - (yZoomPixels / 2 * ratio) + PanXOffset, 0 - (yZoomPixels / 2) + PanYOffset, Height * ratio, Height, 0.0f, 1.0f);
        }
        else
        {
            int xZoomPixels = (int)(ControlWidth * zoom/100.0) - ControlWidth;
            int Width  = ControlWidth + xZoomPixels;
            GetViewport = new Viewport(0 - (xZoomPixels / 2) + PanXOffset, ((ControlHeight - (ControlWidth / ratio)) / 2) - (xZoomPixels / 2 / ratio) + PanYOffset, Width, Width / ratio, 0.0f, 1.0f);
        }

        if (videoProcessor == VideoProcessors.D3D11)
        {
            RawRect src, dst;

            if (GetViewport.Width < 1 || GetViewport.X + GetViewport.Width <= 0 || GetViewport.X >= ControlWidth || GetViewport.Y + GetViewport.Height <= 0 || GetViewport.Y >= ControlHeight)
            { // Out of screen
                src = new RawRect();
                dst = new RawRect();
            }
            else
            {
                int cropLeft    = GetViewport.X < 0 ? (int) GetViewport.X * -1 : 0;
                int cropRight   = GetViewport.X + GetViewport.Width > ControlWidth ? (int) (GetViewport.X + GetViewport.Width - ControlWidth) : 0;
                int cropTop     = GetViewport.Y < 0 ? (int) GetViewport.Y * -1 : 0;
                int cropBottom  = GetViewport.Y + GetViewport.Height > ControlHeight ? (int) (GetViewport.Y + GetViewport.Height - ControlHeight) : 0;

                dst = new RawRect(Math.Max((int)GetViewport.X, 0), Math.Max((int)GetViewport.Y, 0), Math.Min((int)GetViewport.Width + (int)GetViewport.X, ControlWidth), Math.Min((int)GetViewport.Height + (int)GetViewport.Y, ControlHeight));
                    
                if (_RotationAngle == 90)
                {
                    src = new RawRect(
                        (int) (cropTop * ((float)VideoRect.Right / GetViewport.Height)),
                        (int) (cropRight * ((float)VideoRect.Bottom / GetViewport.Width)),
                        VideoRect.Right - ((int) ((cropBottom) * ((float)VideoRect.Right / GetViewport.Height))),
                        VideoRect.Bottom - ((int) ((cropLeft) * ((float)VideoRect.Bottom / GetViewport.Width))));
                }
                else if (_RotationAngle == 270)
                {
                    src = new RawRect(
                        (int) (cropBottom * ((float)VideoRect.Right / GetViewport.Height)),
                        (int) (cropLeft * ((float)VideoRect.Bottom / GetViewport.Width)),
                        VideoRect.Right - ((int) ((cropTop) * ((float)VideoRect.Right / GetViewport.Height))),
                        VideoRect.Bottom - ((int) ((cropRight) * ((float)VideoRect.Bottom / GetViewport.Width))));
                }
                else if (_RotationAngle == 180)
                {
                    src = new RawRect(
                        (int) (cropRight * ((float)VideoRect.Right / (float)GetViewport.Width)),
                        (int) (cropBottom * ((float)VideoRect.Bottom / (float)GetViewport.Height)),
                        VideoRect.Right - ((int) ((cropLeft) * ((float)VideoRect.Right / (float)GetViewport.Width))),
                        VideoRect.Bottom - ((int) ((cropTop) * ((float)VideoRect.Bottom / (float)GetViewport.Height))));
                }
                else
                {
                    src = new RawRect(
                        (int) (cropLeft * ((float)VideoRect.Right / (float)GetViewport.Width)),
                        (int) (cropTop * ((float)VideoRect.Bottom / (float)GetViewport.Height)),
                        VideoRect.Right - ((int) ((cropRight) * ((float)VideoRect.Right / (float)GetViewport.Width))),
                        VideoRect.Bottom - ((int) ((cropBottom) * ((float)VideoRect.Bottom / (float)GetViewport.Height))));
                }
            }

            vc.VideoProcessorSetStreamSourceRect(vp, 0, true, src);
            vc.VideoProcessorSetStreamDestRect  (vp, 0, true, dst);
            vc.VideoProcessorSetOutputTargetRect(vp, true, new RawRect(0, 0, ControlWidth, ControlHeight));
        }

        if (refresh)
            Present();
    }
    public void ResizeBuffers(int width, int height)
    {
        lock (lockDevice)
        {
            if (SCDisposed)
                return;
                
            ControlWidth = width;
            ControlHeight = height;

            backBufferRtv.Dispose();
            vpov?.Dispose();
            backBuffer.Dispose();
            swapChain.ResizeBuffers(0, ControlWidth, ControlHeight, Format.Unknown, SwapChainFlags.None);
            UpdateCornerRadius();
            backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv = Device.CreateRenderTargetView(backBuffer);
            if (videoProcessor == VideoProcessors.D3D11)
                vd1.CreateVideoProcessorOutputView(backBuffer, vpe, vpovd, out vpov);

            SetViewport();
        }
    }

    internal void UpdateCornerRadius()
    {
        if (dCompDevice == null)
            return;

        dCompDevice.CreateRectangleClip(out var clip).CheckError();
        clip.SetLeft(0);
        clip.SetRight(ControlWidth);
        clip.SetTop(0);
        clip.SetBottom(ControlHeight);
        clip.SetTopLeftRadiusX((float)cornerRadius.TopLeft);
        clip.SetTopLeftRadiusY((float)cornerRadius.TopLeft);
        clip.SetTopRightRadiusX((float)cornerRadius.TopRight);
        clip.SetTopRightRadiusY((float)cornerRadius.TopRight);
        clip.SetBottomLeftRadiusX((float)cornerRadius.BottomLeft);
        clip.SetBottomLeftRadiusY((float)cornerRadius.BottomLeft);
        clip.SetBottomRightRadiusX((float)cornerRadius.BottomRight);
        clip.SetBottomRightRadiusY((float)cornerRadius.BottomRight);
        dCompVisual.SetClip(clip).CheckError();
        clip.Dispose();
        dCompDevice.Commit().CheckError();
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (msg)
        {
            case WM_NCDESTROY:
                if (SCDisposed)
                    RemoveWindowSubclass(ControlHandle, wndProcDelegatePtr, UIntPtr.Zero);
                else
                    DisposeSwapChain();
                break;

            case WM_SIZE:
                ResizeBuffers(SignedLOWORD(lParam), SignedHIWORD(lParam));
                break;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
