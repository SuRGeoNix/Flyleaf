using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

using ID3D11Device          = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext   = Vortice.Direct3D11.ID3D11DeviceContext;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>Creates one post-processor per Flyleaf renderer device lifetime.</summary>
public interface IVideoPostProcessorFactory
{
    IVideoPostProcessor Create(ID3D11Device device);
}

/// <summary>
/// Optional, synchronous GPU processing of Flyleaf's final SDR video image.
/// No presenting, flushing, blocking, retaining borrowed views, or disposing borrowed resources.
/// The callback runs under Flyleaf's renderer lock; application state read here must be thread-safe.
/// </summary>
public interface IVideoPostProcessor : IDisposable
{
    /// <summary>Return false to bypass both intermediate rendering and the callback.</summary>
    bool IsEnabled => true;

    /// <summary>
    /// Read a complete display-ready SDR BGRA8 frame from Input and write ALL output pixels to Output.
    /// The caller owns the D3D11 context and all supplied views.
    /// </summary>
    void Process(in VideoPostProcessContext context);
}

/// <summary>Resources are borrowed only for the duration of Process().</summary>
public readonly struct VideoPostProcessContext
{
    public ID3D11DeviceContext      DeviceContext       { get; }
    public ID3D11ShaderResourceView Input               { get; }
    public ID3D11RenderTargetView   Output              { get; }
    public Format                   SurfaceFormat       { get; }
    public uint                     OutputWidth         { get; }
    public uint                     OutputHeight        { get; }
    public Viewport                 ContentViewport     { get; }
    public bool                     IsSnapshot          { get; }

    internal VideoPostProcessContext(ID3D11DeviceContext deviceContext,
        ID3D11ShaderResourceView input, ID3D11RenderTargetView output,
        uint width, uint height, Viewport viewport, bool isSnapshot)
    {
        DeviceContext   = deviceContext;
        Input           = input;
        Output          = output;
        SurfaceFormat   = Format.B8G8R8A8_UNorm;
        OutputWidth     = width;
        OutputHeight    = height;
        ContentViewport = viewport;
        IsSnapshot      = isSnapshot;
    }
}
