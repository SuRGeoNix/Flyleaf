using FlyleafLib.Custom;
using FlyleafLib.MediaFramework.MediaFrame;
using System.Diagnostics;
using Vortice.Direct2D1;

namespace FlyleafLib.MediaFramework.MediaRenderer;
#nullable enable
public unsafe partial class Renderer
{
    SwsContext*     swsCustomCtx;
    AVFrame* customFrame;
    // ZoomOverviewRenderer fields
    public IntPtr SharedTextureHandle { get; set; }
    internal IntPtr lastSharedHandle  = IntPtr.Zero;

    public event Action? CustomProcessRequests;
    public event Action? CustomSetSize;
    public event Action<VideoFrame>? RenderChild;
    public EventHandler<ID2D1DeviceContext>? Overview2DInitialized;
    public EventHandler<ID2D1DeviceContext>? Overview2DDisposing;
    public EventHandler<ID2D1DeviceContext>? Overview2DDraw;

    // Renderer? ParentRenderer {  set; get; }
    public double InitialZoom { get; set; } = 1.0;
    public double MaximalZoom { get; set; } = 50.0;
    double ICustomRenderer.ValidateZoom(double zoom)
    {
        if (zoom < InitialZoom && InitialZoom >= 0)
            zoom = InitialZoom;
        if (zoom > MaximalZoom && MaximalZoom >= 0)
            zoom = MaximalZoom;
        return zoom;
    }
    public void CustomFillPlanesAction(VideoFrame frame)
    {
        if (player is not ICustomPlayer custom)
            return;
        long startTime = Stopwatch.GetTimestamp();
        try
        {
            if (!custom.CustomHandlerEnabled)
                return;

            if (VideoDecoder.VideoAccelerated)
                CustomFillPlanesHW(custom, frame);
            else
                CustomFillPlanesSWS(custom, frame);
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
        }
        TimeSpan elapsedTime = Stopwatch.GetElapsedTime(startTime);
        Log.Debug($"[CP] CustomFillPlanesAction, elapsed time {elapsedTime.TotalMicroseconds / (double) 1000} ms");
    }

    private void CustomFillPlanesHW(ICustomPlayer custom, VideoFrame frame)
    {
        var sw_frame   = av_frame_alloc();
        int ret     = av_hwframe_transfer_data(sw_frame, frame.AVFrame, 0);

        if (swsCustomCtx == null || ContextChanged(sw_frame))
            CustomSwsInit(sw_frame->width, sw_frame->height, sw_frame->format);

        ret = sws_scale(swsCustomCtx,
                sw_frame->data.ToRawArray(),
                sw_frame->linesize.ToArray(),
                0,
                sw_frame->height,
                customFrame->data.ToRawArray(),
                customFrame->linesize.ToArray());

        if (ret > 0)
        {
            var mFrame = new VideoFrame()
            {
                AVFrame = customFrame,
                Timestamp = frame.Timestamp,
            };
            custom.FillCustomPlanes(this, mFrame, out var transformed);

            mFrame.AVFrame = null;
            mFrame.Dispose();
            av_frame_free(&sw_frame);
        }
    }

    private void CustomFillPlanesSWS(ICustomPlayer custom, VideoFrame frame)
    {
        if (swsCustomCtx == null || ContextChanged(frame.AVFrame))
            CustomSwsInit(frame.AVFrame->width, frame.AVFrame->height, frame.AVFrame->format);

        int ret = sws_scale(swsCustomCtx,
                        frame.AVFrame->data.ToRawArray(),
                        frame.AVFrame->linesize.ToArray(),
                        0,
                        frame.AVFrame->height,
                        customFrame->data.ToRawArray(),
                        customFrame->linesize.ToArray());
        if (ret > 0)
        {
            var mFrame = new VideoFrame()
            {
                AVFrame = customFrame,
                Timestamp = frame.Timestamp,
            };
            custom.FillCustomPlanes(this, mFrame, out var transformed);
            mFrame.AVFrame = null;
            mFrame.Dispose();
        }
    }
    private bool ContextChanged(AVFrame*  frame) => customFrame == null ? true : customFrame->width != frame->width || customFrame->height != frame->height || customFrame->format != frame->format;

    private void CustomSwsInit(int width, int height, int pxFormat)
    {
        CustomSwsDispose();

        swsCustomCtx = sws_getContext(
                    width, height,
                    (AVPixelFormat)pxFormat,
                    width, height,
                    AVPixelFormat.Bgra, SwsFlags.None, null, null, null);

        AllocateSwsFrame(width, height);
    }
    private void AllocateSwsFrame(int width, int height)
    {
        customFrame = av_frame_alloc();
        customFrame->format = (int)AVPixelFormat.Bgra;
        customFrame->width = width;
        customFrame->height = height;
        _ = av_frame_get_buffer(customFrame, 0);
    }

    private void CustomSwsDispose()
    {
        if (customFrame != null)
        {
            av_frame_free(ref customFrame);
            swsFrame = null;
        }
        if (swsCustomCtx != null)
        {
            sws_freeContext(swsCustomCtx);
            swsCustomCtx = null;
        }
    }
}
#nullable disable
