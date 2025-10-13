using SharpGen.Runtime;
using Vortice.DXGI;
using Vortice.Mathematics;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    long            lastPresentAt;
    long            lastPresentRequestAt;
    volatile bool   isPlayerPresenting;
    volatile bool   isIdlePresenting;
    object          lockLastFrame = new();
    
    internal void RenderRequest(VideoFrame frame = null, bool forceClear = false)
    {
        // NOTE: We expect frame only from player's ShowFrameX when is already Paused
        // TODO: We use source Fps to present our updated frames which can be low (e.g. 1-10fps) | Consider passing ts to player and will act also as idle renderer? (with higher fps)
        if (isPlayerPresenting && frame == null)
            return;

        lock (lockLastFrame)
        {
            lastPresentRequestAt = DateTime.UtcNow.Ticks;

            if ((frame != null || forceClear) && frame != LastFrame)
            {
                VideoDecoder.DisposeFrame(LastFrame);
                LastFrame = frame;
            }

            if (!canRenderPresent || isPlayerPresenting || isIdlePresenting)
                return;

            isIdlePresenting = true;
        }

        Task.Run(RenderIdleLoop);
    }
    void RenderIdleLoop()
    {
        int rechecks = 1000; // Awake for ~5sec when Idle
        while (canRenderPresent)
        {
            while (lastPresentRequestAt < lastPresentAt && rechecks-- > 0)
            {
                if (isPlayerPresenting || !canRenderPresent)
                    { rechecks = 0; break; }

                Thread.Sleep(5); // might not TimeBeginPeriod1 (can drop fps or slow down cancelation)
            }

            if (rechecks < 1)
                break;

            RenderIdle();

            rechecks = 500;
        }

        lock (lockLastFrame) // To avoid race condition*?
        {
            isIdlePresenting = false;
            if (lastPresentRequestAt > lastPresentAt && !isPlayerPresenting && canRenderPresent)
                RenderRequest();
        }
    }
    void RenderIdle()
    {
        try
        {
            ResizeBuffersInternal();
            SetViewportInternal();

            lastPresentAt = DateTime.UtcNow.Ticks;

            if (canRenderPresent)
            {
                lock (lockLastFrame)
                    if (LastFrame != null)
                        RenderFrame(LastFrame, false);
                    else
                    {
                        ClearOverlayTexture();
                        context.OMSetRenderTargets(backBufferRtv);
                        context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                    }

                swapChain.Present(1, 0);
            }            
        }
        catch (SharpGenException e)
        {
            Log.Error($"[RenderIdle] Device Lost ({e.ResultCode.NativeApiCode} ({e.ResultCode}) | {Device.DeviceRemovedReason} | {e.Message})");

            if (IsDeviceError(e.ResultCode))
            {   
                lock (lockDevice)
                    HandleDeviceLost();
            }
        }
        catch (Exception e)
        {
            Log.Error($"[RenderIdle] Failed ({e.Message})");
        }
    }

    internal void RenderPlayStart()  { isPlayerPresenting = true; while(isIdlePresenting) Thread.Sleep(1); } // Stop RenderIdle
    internal void RenderPlayStop()  => isPlayerPresenting = false; // Check if last timestamp?* to start idle (we don't update it currently)
    internal bool RenderPlay(VideoFrame frame, bool secondField)
    {
        try
        {
            ResizeBuffersInternal();
            SetViewportInternal();

            lock (LastFrame)
            {
                if (canRenderPresent)
                    RenderFrame(frame, secondField);
                else if (frame != LastFrame)
                {
                    VideoDecoder.DisposeFrame(LastFrame);
                    LastFrame = frame;
                }

                // Don't return false when !canRenderPresent as it will cause restarts (consider as valid)
                return true;
            }
        }
        catch (SharpGenException e)
        {
            if (frame != LastFrame) VideoDecoder.DisposeFrame(frame); vpiv?.Dispose();
            Log.Error($"[RenderPlay] Device Lost ({e.ResultCode.NativeApiCode} ({e.ResultCode}) | {Device.DeviceRemovedReason} | {e.Message})");

            if (IsDeviceError(e.ResultCode))
            {   
                lock (lockDevice)
                    HandleDeviceLost();
            }
        }
        catch (Exception e)
        {
            if (frame != LastFrame) VideoDecoder.DisposeFrame(frame); vpiv?.Dispose();
            Log.Error($"[RenderPlay] Failed ({e.Message})");
        }

        return false;
    }
    internal bool PresentPlay()
    {
        try
        {
            if (canRenderPresent)
                swapChain?.Present(Config.Video.VSync, PresentFlags.None).CheckError();

            return true;
        }
        catch (SharpGenException e)
        {
            if (e.ResultCode == ResultCode.WasStillDrawing) // For DoNotWait (any reason to still support it with Config?)
            {
                Log.Info($"[V] Frame Dropped (GPU)");
                return false;
            }

            Log.Error($"[PresentPlay] {e.ResultCode.NativeApiCode} ({e.ResultCode}) | {Device.DeviceRemovedReason} | {e.Message}");

            if (IsDeviceError(e.ResultCode))
            {   
                lock (lockDevice)
                    HandleDeviceLost();
            }
            else throw; // Force Playback Stop
        }
        catch (Exception e)
        {
            Log.Error($"[PresentPlay] Failed ({e.Message})");
            throw; // Force Playback Stop
        }

        return false;
    }

    void RenderFrame(VideoFrame frame, bool secondField) // From Play or Idle (No lock/try, ensure we don't run both)
    {
        if (frame.srvs == null) // videoProcessor can be FlyleafVP but the player can send us a cached frame from prev videoProcessor D3D11VP (check frame.srv instead of videoProcessor)
        {
            if (frame.avFrame != null)
            {
                vpivd.Texture2D.ArraySlice = (uint)frame.avFrame->data[1];
                vd1.CreateVideoProcessorInputView(VideoDecoder.textureFFmpeg, vpe, vpivd, out vpiv).CheckError();
            }
            else
            {
                vpivd.Texture2D.ArraySlice = 0;
                vd1.CreateVideoProcessorInputView(frame.textures[0], vpe, vpivd, out vpiv).CheckError(); // TBR: frame.textures might be null from player (the dequeued vFrame)
            }

            if (vpiv == null)
                throw new NullReferenceException("RenderFrame: vpiv was null");

            /* [Undocumented Deinterlace]
            * Currently we don't use Past/Future frames so we consider only Bob/Weave methods (we also consider that are supported by D3D11VP)
            * For Bob -double rate- we set the second field (InputFrameOrField/OutputIndex)
            * TODO: Bring secondField to renderer so Render refreshes can work also with it (maybe ShowFrameX too)
            */
            vpsa[0].InputSurface= vpiv;
            vpsa[0].OutputIndex = vpsa[0].InputFrameOrField = secondField ? 1u : 0u;
            vc.VideoProcessorBlt(vp, vpov, 0, 1, vpsa);
            vpiv.Dispose();
        }
        else
        {
            context.OMSetRenderTargets(backBufferRtv);
            context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
            context.PSSetShaderResources(0, frame.srvs);
            context.Draw(6, 0);
        }

        if (use2d)
            Config.Video.OnD2DDraw(this, context2d);

        if (overlayTexture != null) // Bitmap Subs
        {
            Viewport view = GetViewport;

            // TODO: Bad quality for scaling subs (consider using different shader/sampler) | Don't stretch the overlay (reduce height based on ratiox) | Sub's stream size might be different from video size (fix y based on percentage)
            var ratiox = (double)view.Width / overlayTextureOriginalWidth;
            var ratioy = (double)overlayTextureOriginalPosY / overlayTextureOriginalHeight;

            if (videoProcessor == VideoProcessors.D3D11)
                context.OMSetRenderTargets(backBufferRtv);

            context.OMSetBlendState(blendStateAlpha);
            context.PSSetShaderResources(0, overlayTextureSRVs);
            context.RSSetViewport((float)(view.X + (overlayTextureOriginalPosX * ratiox)), (float)(view.Y + (view.Height * ratioy)), (float)(overlayTexture.Description.Width * ratiox), (float)(overlayTexture.Description.Height * ratiox));
            context.PSSetShader(ShaderBGRA);
            context.Draw(6, 0);

            // restore context
            context.PSSetShader(ShaderPS);
            context.OMSetBlendState(VideoStream.PixelFormatDesc->flags.HasFlag(PixFmtFlags.Alpha) ? blendStateAlpha : null);
            context.RSSetViewport(GetViewport);
        }

        if (frame != LastFrame)
        {
            VideoDecoder.DisposeFrame(LastFrame);
            LastFrame = frame;
        }
    }

    public FrameStatistics GetFrameStatistics()
    {
        lock (lockDevice)
        {
            if (SCDisposed)
                return new();

            FrameStatistics stats;
            int retries = 7;
            while(swapChain.GetFrameStatistics(out stats).Failure && retries-- > 0);

            #if DEBUG
            if (retries == 0 && CanDebug) Log.Debug("GetFrameStatistics failed");
            #endif

            return stats;
        }
    }

    public void ClearScreen(bool force = false) { if (Config.Video.ClearScreen || force) RenderRequest(null, true); }
    public void ClearOverlayTexture()
    {
        if (overlayTexture == null)
            return;

        overlayTexture?.    Dispose();
        overlayTextureSrv?. Dispose();
        overlayTexture      = null;
        overlayTextureSrv   = null;
    }
    internal void CreateOverlayTexture(SubtitlesFrame frame, int streamWidth, int streamHeight)
    {
        var rect    = frame.sub.rects[0];
        var stride  = rect->linesize[0] * 4;

        overlayTextureOriginalWidth = streamWidth;
        overlayTextureOriginalHeight= streamHeight;
        overlayTextureOriginalPosX  = rect->x;
        overlayTextureOriginalPosY  = rect->y;
        overlayTextureDesc.Width    = (uint)rect->w;
        overlayTextureDesc.Height   = (uint)rect->h;

        byte[] data = new byte[rect->w * rect->h * 4];

        fixed(byte* ptr = data)
        {
            uint[] colors   = new uint[256];
            var colorsData  = new Span<uint>((byte*)rect->data[1], rect->nb_colors);

            for (int i = 0; i < colorsData.Length; i++)
                colors[i] = colorsData[i];

            ConvertPal(colors, 256, false);

            for (int y = 0; y < rect->h; y++)
            {
                uint* xout =(uint*) (ptr + y * stride);
                byte* xin = ((byte*)rect->data[0]) + y * rect->linesize[0];

                for (int x = 0; x < rect->w; x++)
                    *xout++ = colors[*xin++];
            }

            subDataOverlay.DataPointer = (nint)ptr;
            subDataOverlay.RowPitch    = (uint)stride;

            overlayTexture?.            Dispose();
            overlayTextureSrv?.         Dispose();
            overlayTexture              = Device.CreateTexture2D(overlayTextureDesc, [subDataOverlay]);
            overlayTextureSrv           = Device.CreateShaderResourceView(overlayTexture);
            overlayTextureSRVs[0]       = overlayTextureSrv;
        }
    }
    static void ConvertPal(uint[] colors, int count, bool gray) // subs bitmap (source: mpv)
    {
        for (int n = 0; n < count; n++)
        {
            uint c = colors[n];
            uint b = c & 0xFF;
            uint g = (c >> 8) & 0xFF;
            uint r = (c >> 16) & 0xFF;
            uint a = (c >> 24) & 0xFF;

            if (gray)
                r = g = b = (r + g + b) / 3;

            // from straight to pre-multiplied alpha
            b = b * a / 255;
            g = g * a / 255;
            r = r * a / 255;
            colors[n] = b | (g << 8) | (r << 16) | (a << 24);
        }
    }
    bool IsDeviceError(Result code) => true; // code == ResultCode.DeviceRemoved || code == ResultCode.DeviceRemoved || code == ResultCode.DeviceHung || code == ResultCode.DriverInternalError;
    // TBR: For now all.. ResultCode.InvalidCall ? (Happens also with NativeApiCode: WINCODEC_ERR_INVALIDPARAMETER, Description: The parameter is incorrect, ApiCode: InvalidParameter, Code: -2147024809
}
