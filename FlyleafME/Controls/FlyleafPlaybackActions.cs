using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

/// <summary>Shared playback choices and operation-specific availability.</summary>
internal static class FlyleafPlaybackActions
{
    internal static readonly double[] SpeedPresets = [0.5, 0.75, 1, 1.25, 1.5, 2];
    internal static bool CanPlay(Player? player) => player is { IsDisposed: false, CanPlay: true };
    internal static bool CanSeek(Player? player) => CanPlay(player) && player!.Duration > 0;
    internal static bool CanReverse(Player? player) => CanPlay(player) && player!.Video.IsOpened && !player.IsLive;
    internal static bool CanStep(Player? player) => CanPlay(player) && player!.Video.IsOpened && player.VideoDemuxer?.IsHLSLive == false;
}
