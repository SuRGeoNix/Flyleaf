using FlyleafLib.Controls.WPF.Present;
using Vortice.DXGI;

using FlyleafLib.MediaPlayer;

using ID3D11Device = Vortice.Direct3D11.ID3D11Device;

namespace FlyleafLib.Controls.WPF;

/// <summary>
/// <see cref="IVideoFrameProvider"/> sourced from a <see cref="Player"/>'s composition
/// swap-chain: each present, the render-adapter backbuffer is copied into the base
/// frame texture and delivered (GPU shared / CPU readback) per present mode.
/// </summary>
public sealed class FlyleafFrameBridge : VideoFrameProviderBase
{
    private Player _player;

    protected override ID3D11Device RenderDevice => _player.Renderer.Device;

    public void Initialize(Player player, int controlWidth, int controlHeight)
    {
        _player = player;
        SetControlSize(controlWidth, controlHeight);
        SetRenderLuid(player.Renderer.GPUAdapter?.Luid ?? 0);

        DebugLogger.Print($"[FLB] Initialize control={controlWidth}x{controlHeight} renderLuid={player.Renderer.GPUAdapter?.Luid}");

        player.Renderer.SwapChain.RegisterBeforePresentCallback(OnBeforePresent);
        player.Renderer.SwapChain.SetupWinUI(OnSwapChainUpdated);
    }

    private void OnBeforePresent()
        => PublishFrame(tex => _player.Renderer.SwapChain.CopyBackBufferTo(tex));

    private void OnSwapChainUpdated(IDXGISwapChain2 swapChain)
    {
        try
        {
            ResetFrames();
            if (swapChain != null)
                _player?.Renderer?.SwapChain?.Resize(_controlWidth, _controlHeight);
        }
        finally
        {
            swapChain?.Dispose();
        }
    }

    protected override void OnResize(int width, int height)
        => _player?.Renderer?.SwapChain?.Resize(width, height);

    protected override void DisposeCore()
    {
        if (_player?.Renderer?.SwapChain != null)
        {
            _player.Renderer.SwapChain.UnregisterBeforePresentCallback(OnBeforePresent);
            _player.Renderer.SwapChain.Dispose(rendererFrame: false);
        }
    }
}
