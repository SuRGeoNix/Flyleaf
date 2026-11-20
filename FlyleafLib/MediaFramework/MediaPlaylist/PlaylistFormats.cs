using System.Linq;

namespace FlyleafLib.MediaFramework.MediaPlaylist;

/// <summary>Distinguishes HLS media manifests from editable lists without relying on the filename.</summary>
public static class PlaylistFormats
{
    public static bool IsHlsManifest(string text) => text.Split('\n').Any(line =>
        line.TrimStart('\uFEFF', ' ', '\t').StartsWith("#EXT-X-", StringComparison.OrdinalIgnoreCase));
}
