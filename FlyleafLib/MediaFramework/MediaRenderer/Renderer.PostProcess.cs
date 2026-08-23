using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    IVideoPostProcessor postProcessor;
    PostProcessSurface livePostProcessSurface;
    ID3D11PixelShader postProcessCopyShader;

    bool PostProcessEnabled => postProcessor != null;

    void PostProcessSetup()
    {
        var factory = ucfg.PostProcessorFactory;
        if (factory == null)
            return;

        try
        {
            postProcessor = factory.Create(device);
            if (postProcessor != null)
                postProcessCopyShader = ShaderCompiler.CompilePS(device, "post-process-copy", "color = float4(Texture1.Sample(Sampler, input.Texture).rgba);");
        }
        catch (Exception e)
        {
            postProcessor = null;
            postProcessCopyShader?.Dispose();
            postProcessCopyShader = null;
            Log.Error($"[PostProcess] Creation failed ({e.Message})");
        }
    }

    void PostProcessDispose()
    {
        livePostProcessSurface?.Dispose();
        livePostProcessSurface = null;
        postProcessCopyShader?.Dispose();
        postProcessCopyShader = null;

        try { postProcessor?.Dispose(); }
        catch (Exception e) { Log.Error($"[PostProcess] Disposal failed ({e.Message})"); }
        postProcessor = null;
    }

    PostProcessSurface GetLivePostProcessSurface()
    {
        uint width = (uint)ControlWidth;
        uint height = (uint)ControlHeight;
        if (livePostProcessSurface == null || livePostProcessSurface.Width != width || livePostProcessSurface.Height != height)
        {
            livePostProcessSurface?.Dispose();
            livePostProcessSurface = new(device, vd, ve, width, height);
        }
        return livePostProcessSurface;
    }

    void RunPostProcessor(PostProcessSurface input, ID3D11RenderTargetView output, uint width, uint height, Viewport contentViewport, bool isSnapshot)
    {
        var fullViewport = new Viewport(width, height);
        try
        {
            postProcessor.Process(new(context, input.SRV, output, width, height, contentViewport, isSnapshot));
        }
        catch (Exception e)
        {
            Log.Error($"[PostProcess] Frame failed; using unprocessed frame ({e.Message})");
            // The extension can use any graphics-pipeline slot. Clear everything before the
            // fallback so stale blend/scissor/shader state cannot suppress or corrupt the copy.
            context.ClearState();
            context.OMSetRenderTargets(output);
            context.RSSetViewport(fullViewport);
            context.IASetVertexBuffer(0, vertexBuffer, sizeof(float) * 5);
            context.IASetInputLayout(inputLayout);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.OMSetBlendState(null);
            context.RSSetState(null);
            context.VSSetShader(vsSimple);
            context.PSSetShader(postProcessCopyShader);
            context.PSSetSampler(0, samplerLinear);
            context.PSSetShaderResource(0, input.SRV);
            context.Draw(6, 0);
        }
        finally
        {
            // Unbind every borrowed resource, including slots/stages chosen by the processor.
            // This also prevents a resized intermediate from remaining alive through a context binding.
            context.ClearState();
            context.OMSetRenderTargets(output);
            context.RSSetViewport(contentViewport);
            context.IASetVertexBuffer(0, vertexBuffer, sizeof(float) * 5);
            context.IASetInputLayout(inputLayout);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetConstantBuffer(0, vsBuffer);
            context.PSSetConstantBuffer(0, psBuffer);
            context.PSSetConstantBuffer(1, panoBuffer);
            context.VSSetShader(vsMain);
            context.PSSetSampler(0, samplerLinear);
            context.OMSetBlendState(null);
            context.RSSetState(ucfg.hflip || vflip ? rsStateHVFlip : null);
            if (psId != null && psShader.TryGetValue(psId, out var flyleafPixelShader))
                context.PSSetShader(flyleafPixelShader);
        }
    }
}

sealed class PostProcessSurface : IDisposable
{
    public uint Width { get; }
    public uint Height { get; }
    public ID3D11Texture2D Texture { get; }
    public ID3D11RenderTargetView RTV { get; }
    public ID3D11ShaderResourceView SRV { get; }
    public ID3D11VideoProcessorOutputView VPOV { get; }

    public PostProcessSurface(ID3D11Device device, ID3D11VideoDevice videoDevice,
        ID3D11VideoProcessorEnumerator videoEnumerator, uint width, uint height)
    {
        Width = width;
        Height = height;
        Texture = device.CreateTexture2D(new Texture2DDescription
        {
            Usage = ResourceUsage.Default,
            Format = Format.B8G8R8A8_UNorm,
            ArraySize = 1,
            MipLevels = 1,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            SampleDescription = new(1, 0),
            Width = width,
            Height = height
        });
        RTV = device.CreateRenderTargetView(Texture);
        SRV = device.CreateShaderResourceView(Texture);
        if (videoDevice != null && videoEnumerator != null)
            VPOV = videoDevice.CreateVideoProcessorOutputView(Texture, videoEnumerator,
                new() { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });
    }

    public void Dispose()
    {
        VPOV?.Dispose();
        SRV.Dispose();
        RTV.Dispose();
        Texture.Dispose();
    }
}
