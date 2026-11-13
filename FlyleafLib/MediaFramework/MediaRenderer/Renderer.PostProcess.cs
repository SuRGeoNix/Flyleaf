using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

using ID3D11Device      = Vortice.Direct3D11.ID3D11Device;
using ID3D11Texture2D   = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    ID3D11DeviceContext1    postContext1;
    ID3DDeviceContextState  postState;
    IVideoPostProcessor     postProcessor;
    PostProcessSurface      postLiveSurface, postSnapshotSurface;
    bool postProcessFaulted;

    void PostProcessSetup()
    {
        if (ucfg.PostProcessorFactory == null)
            return;

        try
        {
            using var device1   = device.QueryInterface<ID3D11Device1>();
            postContext1        = context.QueryInterface<ID3D11DeviceContext1>();
            postState           = device1.CreateDeviceContextState<ID3D11Device1>(0, [FeatureLevel], out _);
            postProcessor       = ucfg.PostProcessorFactory.Create(device);
            if (postProcessor == null)
                PostProcessDispose();
        }
        catch (Exception e)
        {
            Log.Warn($"[PostProcess] D3D11.1 unavailable or initialization failed; disabled ({e.Message})");
            PostProcessDispose();
        }
    }

    bool CanPostProcess(bool snapshot)
    {
        if (postProcessor == null || postProcessFaulted || postContext1 == null || postState == null)
            return false;
        if ((!snapshot || !SwapChain.Disposed) &&
            (SwapChain.BackBuffer == null || SwapChain.BackBuffer.Description.Format != Format.B8G8R8A8_UNorm))
            return false;
        try { return postProcessor.IsEnabled; }
        catch (Exception e)
        {
            PostProcessFault(e);
            return false;
        }
    }

    PostProcessSurface GetPostProcessSurface(bool snapshot, uint width, uint height)
    {
        if (width == 0 || height == 0 || !CanPostProcess(snapshot))
            return null;

        try
        {
            var current = snapshot ? postSnapshotSurface : postLiveSurface;
            bool needsVideoView = VideoProcessor == VideoProcessors.D3D11;
            if (current == null || current.Width != width || current.Height != height ||
                (needsVideoView && current.VPOV == null))
            {
                if (snapshot) postSnapshotSurface = null;
                else          postLiveSurface = null;
                current?.Dispose();
                current = new PostProcessSurface(device, needsVideoView ? vd : null, ve, width, height);
                if (snapshot) postSnapshotSurface = current;
                else          postLiveSurface = current;
            }
            return current;
        }
        catch (Exception e)
        {
            PostProcessFault(e);
            return null;
        }
    }

    void PostProcessFault(Exception e)
    {
        if (!postProcessFaulted)
            Log.Error($"[PostProcess] Disabled after error ({e.Message})");
        postProcessFaulted = true;
    }

    bool RunPostProcessor(PostProcessSurface input, ID3D11RenderTargetView output,
        uint width, uint height, Viewport viewport, bool snapshot)
    {
        ID3DDeviceContextState savedState = null;
        try
        {
            savedState = postContext1.SwapDeviceContextState(postState);
            postProcessor.Process(new VideoPostProcessContext(
                context, input.SRV, output, width, height, viewport, snapshot));
            return true;
        }
        catch (Exception e)
        {
            PostProcessFault(e);
            return false; // Caller re-renders the original frame after restoration.
        }
        finally
        {
            if (savedState != null)
            {
                // Clear only the processor state, then restore every Flyleaf graphics binding.
                try { context.ClearState(); }
                finally
                {
                    using var displaced = postContext1.SwapDeviceContextState(savedState);
                    savedState.Dispose();
                }
            }
        }
    }

    void PostProcessDispose()
    {
        postLiveSurface?.    Dispose(); postLiveSurface     = null;
        postSnapshotSurface?.Dispose(); postSnapshotSurface = null;
        try { postProcessor?.Dispose(); }
        catch (Exception e) { Log.Warn($"[PostProcess] Disposal failed ({e.Message})"); }
        postProcessor       = null;
        postState?.          Dispose();  postState = null;
        postContext1?.       Dispose();  postContext1 = null;
        postProcessFaulted  = false;
    }

    sealed class PostProcessSurface : IDisposable
    {
        public uint                             Width       { get; }
        public uint                             Height      { get; }
        public ID3D11Texture2D                  Texture     { get; }
        public ID3D11RenderTargetView           RTV         { get; }
        public ID3D11ShaderResourceView         SRV         { get; }
        public ID3D11VideoProcessorOutputView   VPOV        { get; }

        public PostProcessSurface(ID3D11Device device, ID3D11VideoDevice vd,
            ID3D11VideoProcessorEnumerator ve, uint width, uint height)
        {
            Width = width;
            Height = height;
            try
            {
                Texture = device.CreateTexture2D(new Texture2DDescription
                {
                    Usage       = ResourceUsage.Default,
                    Format      = Format.B8G8R8A8_UNorm,
                    Width       = width,
                    Height      = height,
                    ArraySize   = 1,
                    MipLevels   = 1,
                    BindFlags   = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    SampleDescription = new(1, 0)
                });
                RTV = device.CreateRenderTargetView(Texture);
                SRV = device.CreateShaderResourceView(Texture);
                if (vd != null && ve != null)
                    VPOV = vd.CreateVideoProcessorOutputView(Texture, ve,
                        new() { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            VPOV?.   Dispose();
            SRV?.    Dispose();
            RTV?.    Dispose();
            Texture?.Dispose();
        }
    }
}
