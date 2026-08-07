using FlyleafLib.Controls.WPF.Present;
using FlyleafLib.MediaPlayer;

using ID3D11Device = Vortice.Direct3D11.ID3D11Device;

namespace FlyleafLib.Zoom;

/// <summary>
/// <see cref="IVideoFrameProvider"/> sourced from the zoom-overview minimap. The
/// minimap is rendered on the player's render device (sampling the decoded frame
/// directly — no cross-adapter shared handle), then delivered through the shared
/// present machinery (GPU shared / CPU readback / software) exactly like the main
/// video, so it renders across adapters and over RDP/headless.
/// </summary>
public sealed class ZoomFrameProvider : VideoFrameProviderBase
{
    private Player _player;
    private ZoomOverviewRenderer _renderer;

    public ZoomOverviewRenderer Renderer => _renderer;

    protected override ID3D11Device RenderDevice => _player.Renderer.Device;

    public void Initialize(Player player, int controlWidth, int controlHeight)
    {
        _player = player;
        SetControlSize(controlWidth, controlHeight);
        SetRenderLuid(player.Renderer.GPUAdapter?.Luid ?? 0);

        _renderer = new ZoomOverviewRenderer(player, controlWidth, controlHeight);
        _renderer.Initialize();
        _renderer.FrameReady += OnFrameReady;
    }

    // Render thread: draw the minimap, then copy it into the base frame texture.
    private void OnFrameReady()
    {
        var minimap = _renderer?.RenderMinimap();
        if (minimap == null)
            return;

        PublishFrame(target =>
        {
            var td = target.Description;
            var sd = minimap.Description;
            if (td.Width != sd.Width || td.Height != sd.Height)
                return false;

            _player.Renderer.DeviceContext.CopyResource(target, minimap);
            return true;
        });
    }

    protected override void OnResize(int width, int height)
        => _renderer?.UpdateSize(width, height);

    protected override void DisposeCore()
    {
        if (_renderer != null)
        {
            _renderer.FrameReady -= OnFrameReady;
            _renderer.Dispose();
            _renderer = null;
        }
    }
}
