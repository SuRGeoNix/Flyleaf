using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    bool    isPresenting;
    long    lastPresentAt       = 0;
    long    lastPresentRequestAt= 0;
    object  lockPresentTask     = new();

    public bool Present(VideoFrame frame, bool forceWait = true, bool secondField = false)
    {
        if (Monitor.TryEnter(lockDevice, frame.timestamp == 0 ? 100 : 5)) // Allow more time for first frame
        {
            try
            {
                var ret = PresentInternal(frame, forceWait, secondField);
                if (frame != LastFrame) // De-interlace (Same AVFrame - Different FieldType)
                {
                    VideoDecoder.DisposeFrame(LastFrame);
                    LastFrame = frame;
                    if (child != null)
                        child.LastFrame = frame;
                }

                return ret;

            }
            catch (SharpGenException e)
            {
                try { VideoDecoder.DisposeFrame(frame); vpiv?.Dispose(); } catch { };
                
                if (e.ResultCode == Vortice.DXGI.ResultCode.DeviceRemoved || e.ResultCode == Vortice.DXGI.ResultCode.DeviceReset)
                {
                    Log.Error($"Device Lost ({e.ResultCode} | {Device.DeviceRemovedReason} | {e.Message})");
                    Thread.Sleep(100);
                    
                    HandleDeviceReset();
                }
                else
                {
                    Log.Warn($"Present frame failed {e.Message} | {Device?.DeviceRemovedReason}");
                    throw; // Force Playback Stop
                }
            }
            catch (Exception e)
            {
                try { VideoDecoder.DisposeFrame(frame); vpiv?.Dispose(); } catch { };
                Log.Warn($"Present frame failed {e.Message} | {Device?.DeviceRemovedReason}");
            }
            finally
            {
                Monitor.Exit(lockDevice);
            }
        }
        else
        {
            try { VideoDecoder.DisposeFrame(frame); vpiv?.Dispose(); } catch { };
            Log.Info("Dropped Frame - Lock timeout");
        }

        return false;
    }
    public void Present()
    {
        if (SCDisposed)
            return;

        lock (lockPresentTask) // NOTE: We don't have TimeBeginPeriod, FpsForIdle will not be accurate
        {
            if ((Config.Player.player == null || !Config.Player.player.requiresBuffering) && VideoDecoder.IsRunning && (VideoStream == null || VideoStream.FPS > 10)) // With slow FPS we need to refresh as fast as possible
                return;

            if (isPresenting)
            {
                lastPresentRequestAt = DateTime.UtcNow.Ticks;
                return;
            }

            isPresenting = true;
        }

        Task.Run(() =>
        {
            long presentingAt;
            do
            {
                long sleepMs = DateTime.UtcNow.Ticks - lastPresentAt;
                sleepMs = sleepMs < (long)(1.0 / Config.Player.IdleFps * 1000 * 10000) ? (long) (1.0 / Config.Player.IdleFps * 1000) : 0;
                if (sleepMs > 2)
                    Thread.Sleep((int)sleepMs);

                presentingAt = DateTime.UtcNow.Ticks;
                RefreshLayout();
                lastPresentAt = DateTime.UtcNow.Ticks;

            } while (lastPresentRequestAt > presentingAt);

            isPresenting = false;
        });
    }
    internal bool PresentInternal(VideoFrame frame, bool forceWait = true, bool secondField = false)
    {
        if (!canRenderPresent)
            return true; // TBR: to avoid increasing drop frames here (is just disabled)

        if (frame.srvs == null) // videoProcessor can be FlyleafVP but the player can send us a cached frame from prev videoProcessor D3D11VP (check frame.srv instead of videoProcessor)
        {
            if (frame.avFrame != null)
            {
                vpivd.Texture2D.ArraySlice = (uint) frame.avFrame->data[1];
                vd1.CreateVideoProcessorInputView(VideoDecoder.textureFFmpeg, vpe, vpivd, out vpiv);
            }
            else
            {
                vpivd.Texture2D.ArraySlice = 0;
                vd1.CreateVideoProcessorInputView(frame.textures[0], vpe, vpivd, out vpiv);
            }

            if (vpiv == null)
                return false;

            /* [Undocumented Deinterlace]
             * Currently we don't use Past/Future frames so we consider only Bob/Weave methods (we also consider that are supported by D3D11VP)
             * For Bob -double rate- we set the second field (InputFrameOrField/OutputIndex)
             * Should review Present (Idle/RefreshLayout) to ignore changing those (to avoid jumping from second to first field)
             */
            vpsa[0].InputSurface= vpiv;
            vpsa[0].OutputIndex = vpsa[0].InputFrameOrField = secondField ? 1u : 0u;
            vc.VideoProcessorBlt(vp, vpov, 0, 1, vpsa);
            vpiv.Dispose();
            forceWait |= _FieldType != VideoFrameFormat.Progressive;
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
            context.RSSetViewport((float) (view.X + (overlayTextureOriginalPosX * ratiox)), (float) (view.Y + (view.Height * ratioy)), (float) (overlayTexture.Description.Width * ratiox), (float) (overlayTexture.Description.Height * ratiox));
            context.PSSetShader(ShaderBGRA);
            context.Draw(6, 0);

            // restore context
            context.PSSetShader(ShaderPS);
            context.OMSetBlendState(VideoStream.PixelFormatDesc->flags.HasFlag(PixFmtFlags.Alpha) ? blendStateAlpha : null);
            context.RSSetViewport(GetViewport);
        }

        var ret = swapChain.Present(Config.Video.VSync, forceWait ? PresentFlags.None : Config.Video.PresentFlags);
        if (ret.Failure) // 1 Retry (for DoNotWait) to avoid frame drop
        {
            if (!forceWait && Config.Video.PresentFlags.HasFlag(PresentFlags.DoNotWait))
            {
                Thread.Sleep(2);
                ret = swapChain.Present(Config.Video.VSync, Config.Video.PresentFlags);
                if (ret.Failure) Log.Info($"Dropped Frame - {ret.Description}");
            }
            else
                Log.Info($"Dropped Frame - {ret.Description}");
        }

        child?.PresentInternal(frame, forceWait, secondField);

        return ret.Success;
    }

    public void ClearOverlayTexture()
    {
        if (overlayTexture == null)
            return;

        overlayTexture?.Dispose();
        overlayTextureSrv?.Dispose();
        overlayTexture      = null;
        overlayTextureSrv   = null;
    }

    SubresourceData subDataOverlay;
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

            overlayTexture?.Dispose();
            overlayTextureSrv?.Dispose();
            overlayTexture          = Device.CreateTexture2D(overlayTextureDesc, [subDataOverlay]);
            overlayTextureSrv       = Device.CreateShaderResourceView(overlayTexture);
            overlayTextureSRVs[0]   = overlayTextureSrv;
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

    public void RefreshLayout()
    {
        if (Monitor.TryEnter(lockDevice, 5))
        {
            try
            {
                if (SCDisposed)
                    return;

                if (LastFrame != null && (LastFrame.textures != null || LastFrame.avFrame != null))
                    PresentInternal(LastFrame);
                else if (Config.Video.ClearScreen)
                {
                    context.OMSetRenderTargets(backBufferRtv);
                    context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                    swapChain.Present(Config.Video.VSync, PresentFlags.None);
                }
            }
            catch (Exception e)
            {
                if (CanWarn) Log.Warn($"Present idle failed {e.Message} | {Device.DeviceRemovedReason}");
            }
            finally
            {
                Monitor.Exit(lockDevice);
            }
        }
    }
    public void ClearScreen()
    {
        ClearOverlayTexture();
        VideoDecoder.DisposeFrame(LastFrame);
        Present();
    }
    public void ClearScreenForce()
    {
        lock (lockDevice)
        {
            if (!canRenderPresent)
                return;

            ClearOverlayTexture();
            VideoDecoder.DisposeFrame(LastFrame);
            context.OMSetRenderTargets(backBufferRtv);
            context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
            swapChain.Present(Config.Video.VSync, PresentFlags.None);
        }
    }
}
