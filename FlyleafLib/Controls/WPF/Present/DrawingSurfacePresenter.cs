using System.Windows;
using Vortice.Wpf;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11Device1 = Vortice.Direct3D11.ID3D11Device1;

namespace FlyleafLib.Controls.WPF.Present;

/// <summary>
/// GPU present path: draws the frame into a Vortice.Wpf <see cref="DrawingSurface"/>'s
/// ColorTexture on the compositor device. Same-adapter uses a zero-copy shared
/// texture; cross-adapter uploads the CPU frame via UpdateSubresource. Requires a
/// live D3D front buffer (unavailable over RDP/headless/locked sessions).
/// </summary>
internal sealed class DrawingSurfacePresenter : IVideoPresenter
{
    private readonly DrawingSurface _surface;
    private IVideoFrameProvider _provider;
    private ID3D11Device1 _compositorDevice;

    // Compositor-side opened shared texture cache (same-adapter path)
    private ID3D11Texture2D _openedTexture;
    private nint _openedHandle;

    public DrawingSurfacePresenter()
    {
        _surface = new DrawingSurface { AlwaysRefresh = false };
        _surface.LoadContent += OnLoadContent;
        _surface.UnloadContent += OnUnloadContent;
        _surface.Draw += OnDraw;
    }

    public FrameworkElement Host => _surface;
    public bool IsHardware => true;

    /// <summary>Null until the surface has loaded its D3D content.</summary>
    public bool IsLoaded { get; private set; }

    public bool IsFrontBufferAvailable
        => (_surface.Source as D3D11ImageSource)?.IsFrontBufferAvailable ?? false;

    public void Attach(IVideoFrameProvider provider)
    {
        _provider = provider;
        if (_compositorDevice != null)
            provider.SetCompositorDevice(_compositorDevice);
    }

    public void Resize(int pixelWidth, int pixelHeight)
    {
        // DrawingSurface sizes its ColorTexture from layout; the provider's swap-chain
        // is resized by the coordinator. Nothing device-side to do here.
    }

    public void Pump()
    {
        if (_provider != null && IsFrontBufferAvailable && _provider.HasPendingFrame)
            _surface.Invalidate();
    }

    public void Dispose()
    {
        _surface.LoadContent -= OnLoadContent;
        _surface.UnloadContent -= OnUnloadContent;
        _surface.Draw -= OnDraw;
        ResetOpenedTexture();
        (_surface.Source as D3D11ImageSource)?.Dispose();
    }

    private void OnLoadContent(object sender, DrawingSurfaceEventArgs e)
    {
        _compositorDevice = e.Device;
        IsLoaded = true;
        _provider?.SetCompositorDevice(_compositorDevice);
        DebugLogger.Print("[FLB] DrawingSurface LoadContent");
    }

    private void OnUnloadContent(object sender, DrawingSurfaceEventArgs e)
    {
        IsLoaded = false;
        ResetOpenedTexture();
        _compositorDevice = null;
    }

    private void OnDraw(object sender, DrawEventArgs args)
    {
        if (_provider == null)
            return;

        var colorTexture = args.Surface.ColorTexture;
        if (colorTexture == null)
            return;

        var ctDesc = colorTexture.Description;

        // Same-adapter GPU: open the render-device shared texture on the compositor device and copy.
        bool presented = _provider.TryPresentShared(handle =>
        {
            if (_compositorDevice == null)
                return false;

            if (_openedTexture == null || _openedHandle != handle)
            {
                ResetOpenedTexture();
                _openedTexture = _compositorDevice.OpenSharedResource<ID3D11Texture2D>(handle);
                _openedHandle = handle;
            }

            var src = _openedTexture.Description;
            if (src.Width != ctDesc.Width || src.Height != ctDesc.Height)
                return false;

            args.Context.CopyResource(colorTexture, _openedTexture);
            return true;
        });

        // Cross-adapter: upload the CPU frame into the ColorTexture.
        if (!presented)
        {
            presented = _provider.TryPresentCpu((ptr, rowPitch, w, h) =>
            {
                if (w != (int)ctDesc.Width || h != (int)ctDesc.Height)
                    return false;

                args.Context.UpdateSubresource(colorTexture, 0, null, ptr, (uint)rowPitch, 0);
                return true;
            });
        }

        if (presented)
            args.InvalidateSurface();
    }

    private void ResetOpenedTexture()
    {
        _openedTexture?.Dispose();
        _openedTexture = null;
        _openedHandle = IntPtr.Zero;
    }
}
