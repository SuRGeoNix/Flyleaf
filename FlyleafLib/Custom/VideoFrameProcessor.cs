using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using System.Diagnostics;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;

namespace FlyleafLib.Custom;
#nullable enable
public unsafe class VideoFrameProcessor : IVideoFrameProcessor, IDisposable
{
    private readonly ICustomPlayer? _player;
    private readonly FlyleafGpuInjector _gpuInjector;
    private SwsConverter? _swsConverter;
    private AVFrame* swsFrame;
    private Renderer? _renderer;    
    internal LogHandler? Log;

    public VideoFrameProcessor(Player player)
    {
        if (player is ICustomPlayer customPlayer)
        {
            _player = customPlayer;
            _renderer = player.Renderer;
            Log = new(("[#" + player.PlayerId + "]").PadRight(8, ' ') + " [FrameProcessor] ");
        }
        else
            _player = null;

        _gpuInjector = new FlyleafGpuInjector();
    }
    public void Dispose()
    {
        SwsDispose();
    }

    public bool Process(Renderer renderer, VideoFrame frame)
    {
        bool ret = false;
        if (renderer != _renderer)
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

                var toSkip = !custom.FillCustomPlanes(renderer, mFrame, out var transformed);
                Log?.Trace($"toSkip {toSkip}, transformed {transformed is not null}, size {transformed?.Width}x{transformed?.Height}");
                try
                {   
                    lock (renderer.lockDevice)
                    {
                        var device = renderer.Device;
                        var context = renderer.DeviceContext;
                        var vd = renderer.VideoDevice;                        
                        var ve = renderer.VideoEnumerator;

                        if (transformed is System.Drawing.Bitmap bitmap && device != null && context != null)
                        {
                            if (frame.VPIV != null && vd != null && ve != null)
                            {
                                _gpuInjector.InjectBitmapToNv12Texture(
                                    device,
                                    context,
                                    vd, ve,
                                    bitmap,
                                    frame
                                    );
                            }
                            else if (frame.SRV.Length > 0)
                            {
                                _gpuInjector.InjectBitmapToVideoFrameAsShadowResource(
                                    device,
                                    bitmap,
                                    frame
                                    );
                            }
                        }
                    }
                }
                finally
                {
                    transformed?.Dispose();
                    mFrame.AVFrame = null;
                    mFrame.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Error(ex.Message);
        }
        TimeSpan elapsedTime = Stopwatch.GetElapsedTime(startTime);
        Log?.Trace($"[CP] CustomFillPlanesAction, elapsed time {elapsedTime.TotalMicroseconds / (double)1000} ms");

        return ret;
    }

    private bool CopyDataHW(VideoFrame frame)
    {
        var sw_frame   = av_frame_alloc();
        int ret     = av_hwframe_transfer_data(sw_frame, frame.AVFrame, 0);
        try
        {
            if (ret != 0)
                return false;

            if (ContextChanged(sw_frame))
                SwsInit(sw_frame->width, sw_frame->height, sw_frame->format);

            if (_swsConverter is not SwsConverter converter)
                return false;

            ret = converter.Convert(sw_frame, 0, swsFrame->data.ToRawArray(),swsFrame->linesize.ToArray());

        }
        finally
        {
            av_frame_free(ref sw_frame);
        }
        return ret > 0;
    }

    private bool CopyDataSW(VideoFrame frame)
    {
        if (ContextChanged(frame.AVFrame))
            SwsInit(frame.AVFrame->width, frame.AVFrame->height, frame.AVFrame->format);

        if (_swsConverter is not SwsConverter converter)
            return false;

       return converter.Convert(frame.AVFrame, 0, swsFrame->data.ToRawArray(),swsFrame->linesize.ToArray()) > 0;
    }

    private bool ContextChanged(AVFrame* frame)=> _swsConverter == null || swsFrame == null ? true : swsFrame->width != frame->width || swsFrame->height != frame->height || swsFrame->format != frame->format;

    private void SwsInit(int width, int height, int pxFormat)
    {
        SwsDispose();

        _swsConverter = new (
                    width, height,
                    (AVPixelFormat)pxFormat,
                    width, height,
                    AVPixelFormat.Bgra);

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
        _swsConverter?.Dispose();
        _swsConverter = null;
    }
}
#nullable disable
