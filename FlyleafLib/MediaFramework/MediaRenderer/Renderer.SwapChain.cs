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

    private const Int32 WM_NCDESTROY= 0x0082;
    private const Int32 WM_SIZE     = 0x0005;
    SubclassWndProc wndProcDelegate;
    IntPtr wndProcDelegatePtr;

    internal void InitializeSwapChain(IntPtr handle)
    {
        if (cornerRadius != zeroCornerRadius && Device.FeatureLevel >= FeatureLevel.Level_10_0 && (string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) || Config.Video.GPUAdapter.ToUpper() != "WARP"))
        {
            InitializeCompositionSwapChain(handle);
            return;
        }

        lock (lockDevice)
        {
            if (!SCDisposed)
                DisposeSwapChain();

            if (Disposed)
                Initialize(false);

            SwapChainDescription1 swapChainDescription = new()
            {
                Format      = Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm,
                Width       = ControlWidth,
                Height      = ControlHeight,
                AlphaMode   = AlphaMode.Ignore,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0)
            };

            if (Device.FeatureLevel < FeatureLevel.Level_10_0 || (!string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) && Config.Video.GPUAdapter.ToUpper() == "WARP"))
            {
                swapChainDescription.BufferCount= 1;
                swapChainDescription.SwapEffect = SwapEffect.Discard;
                swapChainDescription.Scaling    = Scaling.Stretch;
            }
            else
            {
                swapChainDescription.BufferCount= Config.Video.SwapBuffers; // TBR: for hdr output or >=60fps maybe use 6
                swapChainDescription.SwapEffect = SwapEffect.FlipDiscard;
                swapChainDescription.Scaling    = Scaling.None;
            }

            try
            {
                Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} swap chain with {Config.Video.SwapBuffers} buffers [Handle: {handle}]");
                swapChain = Engine.Video.Factory.CreateSwapChainForHwnd(Device, handle, swapChainDescription, new SwapChainFullscreenDescription() { Windowed = true });
            } catch (Exception e)
            {
                if (string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) || Config.Video.GPUAdapter.ToUpper() != "WARP")
                {
                    try { if (Device != null) Log.Warn($"Device Remove Reason = {Device.DeviceRemovedReason.Description}"); } catch { } // For troubleshooting
                        
                    Log.Warn($"[SwapChain] Initialization failed ({e.Message}). Failling back to WARP device.");
                    Config.Video.GPUAdapter = "WARP";
                    ControlHandle = handle;
                    Flush();
                }
                else
                {
                    ControlHandle = IntPtr.Zero;
                    Log.Error($"[SwapChain] Initialization failed ({e.Message})");
                }

                return;
            }
                
            SCDisposed = false;
            ControlHandle = handle;
            backBuffer   = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv= Device.CreateRenderTargetView(backBuffer);

            SetWindowSubclass(ControlHandle, wndProcDelegatePtr, UIntPtr.Zero, UIntPtr.Zero);

            RECT rect = new();
            GetWindowRect(ControlHandle, ref rect);
            ResizeBuffers(rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }
    internal void InitializeWinUISwapChain()
    {
        lock (lockDevice)
        {
            if (!SCDisposed)
                DisposeSwapChain();

            if (Disposed)
                Initialize(false);

            SwapChainDescription1 swapChainDescription = new()
            {
                Format      = Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm,
                Width       = 1,
                Height      = 1,
                AlphaMode   = AlphaMode.Ignore,
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect  = SwapEffect.FlipSequential,
                Scaling     = Scaling.Stretch,
                BufferCount = Config.Video.SwapBuffers,
                SampleDescription = new SampleDescription(1, 0)
            };
            Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} swap chain with {Config.Video.SwapBuffers} buffers");

            try
            {
                swapChain = Engine.Video.Factory.CreateSwapChainForComposition(Device, swapChainDescription);
            } catch (Exception e)
            {
                Log.Error($"Initialization failed [{e.Message}]"); 

                // TODO fallback to WARP?

                SwapChainWinUIClbk?.Invoke(null);
                return;
            }

            backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv = Device.CreateRenderTargetView(backBuffer);
            SCDisposed = false;
            ResizeBuffers(1, 1);

            SwapChainWinUIClbk?.Invoke(swapChain.QueryInterface<IDXGISwapChain2>());
        }
            
    }
    private void InitializeCompositionSwapChain(IntPtr handle)
    {
        lock (lockDevice)
        {
            if (!SCDisposed)
                DisposeSwapChain();

            if (Disposed)
                Initialize(false);

            RECT rect = new();
            GetWindowRect(handle, ref rect);
            ControlWidth = rect.Right - rect.Left;
            ControlHeight = rect.Bottom - rect.Top;

            SwapChainDescription1 swapChainDescription = new()
            {
                Format      = Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm,
                Width       = ControlWidth,
                Height      = ControlHeight,
                AlphaMode   = AlphaMode.Premultiplied,
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect  = SwapEffect.FlipSequential,
                Scaling     = Scaling.Stretch,
                BufferCount = Config.Video.SwapBuffers,
                SampleDescription = new SampleDescription(1, 0),
            };
                
            try
            {
                Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} composition swap chain with {Config.Video.SwapBuffers} buffers [Handle: {handle}]");
                swapChain = Engine.Video.Factory.CreateSwapChainForComposition(Device, swapChainDescription);
                dCompDevice.CreateTargetForHwnd(handle, true, out dCompTarget).CheckError();
                dCompDevice.CreateVisual(out dCompVisual).CheckError();
                dCompVisual.SetContent(swapChain).CheckError();
                dCompTarget.SetRoot(dCompVisual).CheckError();
                dCompDevice.Commit().CheckError();
            }
            catch (Exception e)
            {
                if (string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) || Config.Video.GPUAdapter.ToUpper() != "WARP")
                {
                    try { if (Device != null) Log.Warn($"Device Remove Reason = {Device.DeviceRemovedReason.Description}"); } catch { } // For troubleshooting

                    Log.Warn($"[SwapChain] Initialization failed ({e.Message}). Failling back to WARP device.");
                    Config.Video.GPUAdapter = "WARP";
                    ControlHandle = handle;
                    Flush();
                }
                else
                {
                    ControlHandle = IntPtr.Zero;
                    Log.Error($"[SwapChain] Initialization failed ({e.Message})");
                }

                return;
            }

            SCDisposed = false;
            ControlHandle = handle;
            backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv = Device.CreateRenderTargetView(backBuffer);

            int styleEx = GetWindowLong(handle, (int)WindowLongFlags.GWL_EXSTYLE).ToInt32();
            styleEx |= 0x00200000; // WS_EX_NOREDIRECTIONBITMAP 
            SetWindowLong(handle, (int)WindowLongFlags.GWL_EXSTYLE, new IntPtr(styleEx));
            SetWindowSubclass(ControlHandle, wndProcDelegatePtr, UIntPtr.Zero, UIntPtr.Zero);

            ResizeBuffers(ControlWidth, ControlHeight);
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
                    if (dCompVisual == null)
                    {
                        context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                    }
                    else
                    {
                        context.ClearRenderTargetView(backBufferRtv, new Color4(0, 0, 0, 0));
                        swapChain.Present(Config.Video.VSync, PresentFlags.None);
                        dCompDevice.Commit();
                    }
                }
                catch { }
            }

            Log.Info($"Destroying swap chain [Handle: {ControlHandle}]");

            // Unassign renderer's WndProc if still there and re-assign the old one
            if (ControlHandle != IntPtr.Zero)
            {
                RemoveWindowSubclass(ControlHandle, wndProcDelegatePtr, UIntPtr.Zero);
                ControlHandle = IntPtr.Zero;
            }

            SwapChainWinUIClbk = null;
                
            dCompVisual?.Dispose();
            dCompTarget?.Dispose();
            dCompVisual = null;
            dCompTarget = null;

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

        if (_RotationAngle == 90 || _RotationAngle == 270)
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
        if (dCompVisual == null)
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
