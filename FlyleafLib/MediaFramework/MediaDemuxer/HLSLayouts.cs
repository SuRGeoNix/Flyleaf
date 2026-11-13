namespace FlyleafLib.MediaFramework.MediaDemuxer;

// Private FFmpeg HLS layouts (Windows x64). Offsets are relative to each native struct.
internal readonly record struct HLSLayout(
    int ContextPlaylistCount,
    int ContextPlaylists,
    int ContextFirstTimestamp,
    int PlaylistMainStreams,
    int PlaylistMainStreamCount,
    int PlaylistStartSeqNo,
    int PlaylistCurSeqNo,
    int PlaylistSegmentCount,
    int PlaylistSegments,
    int SegmentDuration);

internal static class HLSLayouts
{
    const int PlaylistUrlSize      = 4096; // MAX_URL_SIZE
    const int AVIOContextSize      = 200;
    const int FFIOContextExtraSize = 72;
    const int PlaylistData         = PlaylistUrlSize + AVIOContextSize + FFIOContextExtraSize;

    // Offsets after playlist.main_streams, unchanged in the inspected revisions.
    const int MainStreamCount      = 8;
    const int StartSeqNo           = 32;
    const int SegmentCount         = 56;
    const int Segments             = 64;
    const int CurSeqNo             = 80;

    const uint Version900          = (63u << 16) | (1u  << 8) | 100u;
    const uint Version90Branch     = (63u << 16) | (1u  << 8) | 102u;
    const uint VersionNoReadBuffer = (63u << 16) | (7u  << 8) | 101u;
    const uint VersionByteRange    = (63u << 16) | (8u  << 8) | 100u;
    const uint VersionRenditions   = (63u << 16) | (11u << 8) | 100u;
    const uint Version91Branch     = (63u << 16) | (12u << 8) | 100u;
    const uint VersionAfterWebVTT  = (63u << 16) | (18u << 8) | 100u; // current master

    static HLSLayout Create(int contextFirstTimestamp, int playlistPrefix)
    {
        int main = PlaylistData + playlistPrefix;
        return new(
            ContextPlaylistCount:     32,
            ContextPlaylists:         40,
            ContextFirstTimestamp:    contextFirstTimestamp,
            PlaylistMainStreams:      main,
            PlaylistMainStreamCount:  main + MainStreamCount,
            PlaylistStartSeqNo:       main + StartSeqNo,
            PlaylistCurSeqNo:         main + CurSeqNo,
            PlaylistSegmentCount:     main + SegmentCount,
            PlaylistSegments:         main + Segments,
            SegmentDuration:         0);
    }

    // Playlist prefixes describe the fields between FFIOContext and main_streams.
    internal static readonly HLSLayout FFmpeg90     = Create(88, 80);  // n9.0/n9.0.1, 63.6.100
    internal static readonly HLSLayout NoReadBuffer = Create(88, 72);  // master after 2026-09-29
    internal static readonly HLSLayout ByteRange    = Create(88, 88);  // master after 2026-10-11, before context changes
    internal static readonly HLSLayout Release90    = Create(88, 96);  // release/9.0 after 2026-10-11 backport
    internal static readonly HLSLayout Renditions   = Create(120, 88); // master with rendition groups, before EXT-X-START flag
    internal static readonly HLSLayout Release91    = Create(128, 88); // release/9.1, master before WebVTT fields
    internal static readonly HLSLayout Master       = Create(128, 136);// master after 2026-11-04 WebVTT cues

    // Best-effort ranges for upstream mainline builds; no guarantees for patched
    // binaries or commits with identical avformat_version() but different hls.c.
    internal static HLSLayout? Get(uint avformatVersion)
    {
        if ((avformatVersion >> 16) != 63 || avformatVersion < Version900)
            return null;

        // 63.1.102 is ambiguous: FFmpeg n9.0.2 used FFmpeg90, while a later
        // release/9.0 backport introduced Release90 without a version bump.
        // Prefer the earlier tagged release unless the exact build is known.
        if (avformatVersion == Version90Branch)
            return FFmpeg90;

        if (avformatVersion < VersionNoReadBuffer)
            return FFmpeg90;
        if (avformatVersion < VersionByteRange)
            return NoReadBuffer;
        if (avformatVersion < VersionRenditions)
            return ByteRange;
        if (avformatVersion < Version91Branch)
            return Renditions;
        if (avformatVersion < VersionAfterWebVTT)
            return Release91;

        // Retain the last known layout for later libavformat 63 builds.
        return Master;
    }

    internal static string FormatVersion(uint version)
        => $"{version >> 16}.{(version >> 8) & 0xff}.{version & 0xff}";
}
