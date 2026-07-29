using FlyleafLib.Custom;
using FlyleafLib.MediaFramework.MediaFrame;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;

namespace FlyleafLib.MediaFramework.MediaRenderer;
#nullable enable
public unsafe partial class Renderer
{   
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
    public double ValidateZoom(double zoom)
    {
        if (zoom < InitialZoom && InitialZoom >= 0)
            zoom = InitialZoom;
        if (zoom > MaximalZoom && MaximalZoom >= 0)
            zoom = MaximalZoom;
        return zoom;
    }

    public ID3D11DeviceContext DeviceContext => context;
    public ID3D11VideoDevice VideoDevice => vd;
    public ID3D11VideoProcessorEnumerator VideoEnunerator => ve;

    public IVideoFrameProcessor? VideoFrameProcessor { get; set; }

    private void CheckFrameForTransformation(VideoFrame frame)
    {
        if (frame.IsTransformedFrame)
        {
            Log.Trace($"Transformed frame, vpiv {frame.VPIV != null}, ctrl {ControlWidth}x{ControlWidth}, viewport{ucfg.vp?.Viewport}, d3txtDesc {d3txtDesc.Width}x{d3txtDesc.Height}, vs {scfg.Width}x{scfg.Height}");
            var desc = frame.Texture[0].Description;
            if (d3txtDesc.Width != desc.Width || d3txtDesc.Height != desc.Height)
            {
                d3txtDesc.Width = desc.Width;
                d3txtDesc.Height = desc.Height;
                D3SetViewport(ControlWidth, ControlHeight);
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
