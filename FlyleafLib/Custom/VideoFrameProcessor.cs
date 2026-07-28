using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;

namespace FlyleafLib.Custom;

public unsafe class VideoFrameProcessor : IVideoFrameProcessor, IDisposable
{
    private readonly ICustomPlayer? _player;
    private SwsContext*     swsCtx;
    private AVFrame* swsFrame;
    private Renderer _renderer; 
    private Vortice.Direct3D11.ID3D11Texture2D _transformedTexture;
    private uint _transformedWidth;
    private uint _transformedHeight;
    private FlyleafGpuInjector _gpuInjector;
    internal LogHandler? Log;

    public VideoFrameProcessor(Player player)
    {
        if (player is ICustomPlayer customPlayer)
        {
            _player = customPlayer;
            Log = new(("[#" + player.PlayerId + "]").PadRight(8, ' ') + " [FrameProcessor] ");
        }
        else
            _player = null;
    }
    public void Dispose()
    {
        SwsDispose();
        TransformDispose();
    }

    public bool Process(Renderer renderer, VideoFrame frame)
    {
        bool ret = false;
        _renderer = renderer;

        if (_player is not ICustomPlayer custom || !custom.CustomHandlerEnabled)
            return ret;
        
        long startTime = Stopwatch.GetTimestamp();
        try
        {
            if (renderer.VideoDecoder.VideoAccelerated)
                ret = CopyDataHW(frame);
            else
                ret = CopyDataSW(frame);

            if (ret)
            {
                var mFrame = new VideoFrame()
                {
                    AVFrame = swsFrame,
                    Timestamp = frame.Timestamp,
                };
                custom.FillCustomPlanes(renderer, mFrame, out var transformed);

                var device = renderer.Device;
                var context = renderer.DeviceContext;
                var vd = renderer.VideoDevice;
                var ve = renderer.VideoEnunerator;

                if (transformed is System.Drawing.Bitmap bitmap && device != null && context != null)
                {
                    uint width = (uint)bitmap.Width;
                    uint height = (uint)bitmap.Height;

                    if (TransformContextChanged(width, height))
                        TransformInit(width, height);

                    if (renderer.VideoDecoder.VideoAccelerated && vd != null && ve != null)
                    {
                        _gpuInjector?.InjectBitmapToNv12Texture(
                            device,
                            context,
                            vd,ve,
                            bitmap,
                            frame
                            );
                    }
                    else
                        _gpuInjector?.InjectBitmapToVideoFrameAsShadowResource(
                            device,
                            bitmap,
                            frame
                            );

                    Log.Trace("bitmap injected");
                }

                mFrame.AVFrame = null;
                mFrame.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
        }
        TimeSpan elapsedTime = Stopwatch.GetElapsedTime(startTime);
        Log.Debug($"[CP] CustomFillPlanesAction, elapsed time {elapsedTime.TotalMicroseconds / (double)1000} ms");

        return false;
    }

    private bool CopyDataHW(VideoFrame frame)
    {
        var sw_frame   = av_frame_alloc();
        int ret     = av_hwframe_transfer_data(sw_frame, frame.AVFrame, 0);

        if (swsCtx == null || ContextChanged(sw_frame))
            SwsInit(sw_frame->width, sw_frame->height, sw_frame->format);

        ret = sws_scale(swsCtx,
                sw_frame->data.ToRawArray(),
                sw_frame->linesize.ToArray(),
                0,
                sw_frame->height,
                swsFrame->data.ToRawArray(),
                swsFrame->linesize.ToArray());


        return ret > 0;
    }

    private bool CopyDataSW(VideoFrame frame)
    {
        if (swsCtx == null || ContextChanged(frame.AVFrame))
            SwsInit(frame.AVFrame->width, frame.AVFrame->height, frame.AVFrame->format);

        int ret = sws_scale(swsCtx,
                        frame.AVFrame->data.ToRawArray(),
                        frame.AVFrame->linesize.ToArray(),
                        0,
                        frame.AVFrame->height,
                        swsFrame->data.ToRawArray(),
                        swsFrame->linesize.ToArray());
        return ret > 0;
    }

    private bool ContextChanged(AVFrame* frame) => swsFrame == null ? true : swsFrame->width != frame->width || swsFrame->height != frame->height || swsFrame->format != frame->format;

    private bool TransformContextChanged(uint width, uint height)
    {
        var ret = _gpuInjector is null ? true : _transformedTexture == null ? true : false;

        return ret || _transformedWidth != width || _transformedHeight != height;
    }
    private void SwsInit(int width, int height, int pxFormat)
    {
        SwsDispose();

        swsCtx = sws_getContext(
                    width, height,
                    (AVPixelFormat)pxFormat,
                    width, height,
                    AVPixelFormat.Bgra, SwsFlags.None, null, null, null);

        AllocateSwsFrame(width, height);
    }
    private void AllocateSwsFrame(int width, int height)
    {
        swsFrame = av_frame_alloc();
        swsFrame->format = (int)AVPixelFormat.Bgra;
        swsFrame->width = width;
        swsFrame->height = height;
        _ = av_frame_get_buffer(swsFrame, 0);
    }

    private void SwsDispose()
    {
        if (swsFrame != null)
        {
            av_frame_free(ref swsFrame);
            swsFrame = null;
        }
        if (swsCtx != null)
        {
            sws_freeContext(swsCtx);
            swsCtx = null;
        }
    }

    private void TransformInit(uint width, uint height)
    {
        TransformDispose();

        var texDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };
        _transformedTexture = _renderer.Device.CreateTexture2D(texDesc);

        _gpuInjector = new FlyleafGpuInjector();
        _transformedWidth = width;
        _transformedHeight = height;
    }

    private void TransformDispose()
    {
        _transformedWidth = 0;
        _transformedHeight = 0;
        _transformedTexture?.Dispose();
        _gpuInjector?.Dispose();
    }
}
