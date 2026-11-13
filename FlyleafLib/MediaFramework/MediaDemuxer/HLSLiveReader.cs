using System.Runtime.CompilerServices;

namespace FlyleafLib.MediaFramework.MediaDemuxer;

// Direct reads from FFmpeg's private HLS data. Valid only for a known layout.
internal unsafe sealed class HLSLiveReader
{
    internal static readonly HLSLiveReader Instance = Create();

    readonly HLSLayout layout;

    HLSLiveReader(HLSLayout layout) => this.layout = layout;

    static HLSLiveReader Create()
    {
        if (!Environment.Is64BitProcess)
            return null;

        var layout = HLSLayouts.Get(avformat_version());
        return layout.HasValue ? new(layout.Value) : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetPlaylistCount(void* ctx)
        => *(int*)((byte*)ctx + layout.ContextPlaylistCount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void** GetPlaylists(void* ctx)
        => *(void***)((byte*)ctx + layout.ContextPlaylists);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal long GetFirstTimestamp(void* ctx)
        => *(long*)((byte*)ctx + layout.ContextFirstTimestamp);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetMainStreamCount(void* playlist)
        => *(int*)((byte*)playlist + layout.PlaylistMainStreamCount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal AVStream** GetMainStreams(void* playlist)
        => *(AVStream***)((byte*)playlist + layout.PlaylistMainStreams);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HLSWindow GetWindow(void* playlist)
        => new(
            *(long*)((byte*)playlist + layout.PlaylistCurSeqNo),
            *(long*)((byte*)playlist + layout.PlaylistStartSeqNo),
            *(int*)((byte*)playlist + layout.PlaylistSegmentCount),
            *(void***)((byte*)playlist + layout.PlaylistSegments));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal long GetSegmentDuration(void* segment)
        => *(long*)((byte*)segment + layout.SegmentDuration);
}

internal readonly unsafe struct HLSWindow(long curSeqNo, long startSeqNo, int segmentCount, void** segments)
{
    internal readonly long     CurSeqNo      = curSeqNo;
    internal readonly long     StartSeqNo    = startSeqNo;
    internal readonly int      SegmentCount = segmentCount;
    internal readonly void**   Segments     = segments;
}
