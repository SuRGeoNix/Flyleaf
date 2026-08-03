using FlyleafLib.Custom;
using FlyleafLib.MediaFramework.MediaFrame;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;

namespace FlyleafLib.MediaFramework.MediaRenderer;
#nullable enable
public unsafe partial class Renderer
{   
    // ZoomOverviewRenderer fields
    public IntPtr SharedTextureHandle { get; set; }
    internal IntPtr lastSharedHandle  = IntPtr.Zero;
    private ZoomParameters _zoomParameters = new(1.0, 50.0);
    private bool _transformedStream;
    private int _transformedWidth;
    private int _transformedHeight;
        
    public event Action? CustomSetSize;
    public event Action<VideoFrame>? RenderChild;
    public EventHandler<ID2D1DeviceContext>? Overview2DInitialized;
    public EventHandler<ID2D1DeviceContext>? Overview2DDisposing;
    public EventHandler<ID2D1DeviceContext>? Overview2DDraw;


    // Renderer? ParentRenderer {  set; get; }
    public double InitialZoom
    {
        get => _zoomParameters.InitialZoom;
        set => _zoomParameters.InitialZoom = value;
    }
    public double MaximalZoom
    {
        get => _zoomParameters.MaximalZoom;
        set => _zoomParameters.MaximalZoom = value;
    }
    public double ValidateZoom(double zoom) => _zoomParameters.ValidateZoom(zoom);

    internal ID3D11DeviceContext DeviceContext => context;
    internal ID3D11VideoDevice VideoDevice => vd;
    internal ID3D11VideoProcessorEnumerator VideoEnumerator => ve;

    public IVideoFrameProcessor? VideoFrameProcessor { get; set; }

    void D3SetViewport(int width, int height, int transformedWidth, int transformedHeight)
    {
        // if (transformedHeight == _transformedHeight && transformedWidth == _transformedWidth && _transformedStream)
        //    return;

        _transformedStream = true;
        _transformedWidth = transformedWidth;
        _transformedHeight = transformedHeight;

        SetViewport(width, height);

        Viewport view = Viewport;

        if (!ucfg.SuperResolution)
            DisableSuperRes();
        else
        {
            if (scfg.PixelComp0Depth <= 8 && // Seems it crashes with 10-bit?
               (((rotation == 0 || rotation == 180) && view.Width > VisibleWidth && view.Height > VisibleHeight) ||
                ((rotation == 90 || rotation == 270) && view.Width > VisibleHeight && view.Height > VisibleWidth)))
                EnableSuperRes();
            else
                DisableSuperRes();
        }

        int right   = (int)(view.X + view.Width);
        int bottom  = (int)(view.Y + view.Height);

        if (view.Width < 1 || view.Y >= height || view.X >= width || bottom <= 0 || right <= 0)
        {
            d3CanPresent = false;
            return;
        }

        d3CanPresent = true;

        RawRect dst = new(
                Math.Max((int)view.X, 0),
                Math.Max((int)view.Y, 0),
                Math.Min(right      , width),
                Math.Min(bottom     , height));

        double croppedWidth     = _transformedWidth   - crop.Width;
        double croppedHeight    = _transformedHeight  - crop.Height;
        int dstWidth            = dst.Right  - dst.Left;
        int dstHeight           = dst.Bottom - dst.Top;

        int     cropLeft,   cropTop,    cropRight,  cropBottom;
        int     srcLeft,    srcTop,     srcRight,   srcBottom;
        double  scaleX,     scaleY,     scaleXRot,  scaleYRot;

        if (rotation == 0)
        {
            cropLeft = view.X < 0 ? (int)(-view.X) : 0;
            cropTop = view.Y < 0 ? (int)(-view.Y) : 0;

            scaleX = croppedWidth / view.Width;
            scaleY = croppedHeight / view.Height;

            srcLeft = (int)(crop.Left + cropLeft * scaleX);
            srcTop = (int)(crop.Top + cropTop * scaleY);
            srcRight = srcLeft + (int)(dstWidth * scaleX);
            srcBottom = srcTop + (int)(dstHeight * scaleY);
        }
        else if (rotation == 180)
        {
            cropRight = right > width ? right - width : 0;
            cropBottom = bottom > height ? bottom - height : 0;

            scaleX = croppedWidth / view.Width;
            scaleY = croppedHeight / view.Height;

            srcLeft = (int)(crop.Left + cropRight * scaleX);
            srcTop = (int)(crop.Top + cropBottom * scaleY);
            srcRight = srcLeft + (int)(dstWidth * scaleX);
            srcBottom = srcTop + (int)(dstHeight * scaleY);
        }
        else if (rotation == 90)
        {
            cropTop = view.Y < 0 ? (int)(-view.Y) : 0;
            cropRight = right > width ? right - width : 0;

            scaleXRot = croppedWidth / view.Height;
            scaleYRot = croppedHeight / view.Width;

            srcLeft = (int)(crop.Left + cropTop * scaleXRot);
            srcTop = (int)(crop.Top + cropRight * scaleYRot);
            srcRight = srcLeft + (int)(dstHeight * scaleXRot);
            srcBottom = srcTop + (int)(dstWidth * scaleYRot);
        }
        else if (rotation == 270)
        {
            cropLeft = view.X < 0 ? (int)(-view.X) : 0;
            cropBottom = bottom > height ? bottom - height : 0;

            scaleXRot = croppedWidth / view.Height;
            scaleYRot = croppedHeight / view.Width;

            srcLeft = (int)(crop.Left + cropBottom * scaleXRot);
            srcTop = (int)(crop.Top + cropLeft * scaleYRot);
            srcRight = srcLeft + (int)(dstHeight * scaleXRot);
            srcBottom = srcTop + (int)(dstWidth * scaleYRot);
        }
        else
            srcLeft = srcTop = srcRight = srcBottom = 0;

        RawRect src = new(
            Math.Max(srcLeft    , 0),
            Math.Max(srcTop     , 0),
            Math.Min(srcRight   , _transformedWidth),
            Math.Min(srcBottom  , _transformedHeight));

        vc.VideoProcessorSetStreamSourceRect(vp, 0, true, src);
        vc.VideoProcessorSetStreamDestRect(vp, 0, true, dst);
    }

    private void CheckFrameForTransformation(VideoFrame frame)
    {
        if (frame.IsTransformedFrame && frame.Texture?.Length > 0 && VideoProcessor is VideoProcessors.D3D11)
        {
            Log.Trace($"Transformed frame, vpiv {frame.VPIV != null}, ctrl {ControlWidth}x{ControlHeight}, viewport{ucfg.vp?.Viewport}, d3txtDesc {d3txtDesc.Width}x{d3txtDesc.Height}, vs {scfg.Width}x{scfg.Height}");
            var desc = frame.Texture[0].Description;
            D3SetViewport(ControlWidth, ControlHeight, (int)desc.Width, (int)desc.Height);
        }
        else
        {
            if (frame.IsTransformedFrame && VideoProcessor is VideoProcessors.Flyleaf)
            {
                FLSetViewport();
                context.PSSetShader(psShader["rgba"]);
            }
        }
    }

    private void CustomDispose()
    {
        if (VideoFrameProcessor is IDisposable processor)
        {
            processor.Dispose();
        }
    }
}
#nullable disable
