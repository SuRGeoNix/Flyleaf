using Vortice.Direct3D11;
using Vortice.Mathematics;

using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>Creates a video post-processor bound to a renderer device lifetime.</summary>
public interface IVideoPostProcessorFactory
{
    IVideoPostProcessor Create(ID3D11Device device);
}

/// <summary>
/// Processes a converted BGRA video surface synchronously inside the renderer's render lock.
/// Implementations must not retain context members, present, flush, dispose renderer-owned
/// resources, or perform blocking waits.
/// </summary>
public interface IVideoPostProcessor : IDisposable
{
    void Process(in VideoPostProcessContext context);
}

/// <summary>Borrowed resources valid only for the duration of <see cref="IVideoPostProcessor.Process"/>.</summary>
public readonly struct VideoPostProcessContext
{
    public ID3D11DeviceContext DeviceContext { get; }
    public ID3D11ShaderResourceView Input { get; }
    public ID3D11RenderTargetView Output { get; }
    public uint OutputWidth { get; }
    public uint OutputHeight { get; }
    public Viewport ContentViewport { get; }
    public bool IsSnapshot { get; }

    internal VideoPostProcessContext(
        ID3D11DeviceContext deviceContext,
        ID3D11ShaderResourceView input,
        ID3D11RenderTargetView output,
        uint outputWidth,
        uint outputHeight,
        Viewport contentViewport,
        bool isSnapshot)
    {
        DeviceContext = deviceContext;
        Input = input;
        Output = output;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        ContentViewport = contentViewport;
        IsSnapshot = isSnapshot;
    }
}
