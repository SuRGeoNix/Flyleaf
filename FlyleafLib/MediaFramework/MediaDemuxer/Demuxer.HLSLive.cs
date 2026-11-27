namespace FlyleafLib.MediaFramework.MediaDemuxer;

public unsafe partial class Demuxer
{
    // Audio/Video Stream's HLS playlist
    internal void*          HLSPlaylist             { get; private set; }
    internal void*          hlsCtx;
    HLSLiveReader           hlsReader;

    internal long           hlsStartTime            = NoTs;
    long                    hlsCurDuration;
    long                    hlsPrevSeqNo            = NoTs;
    void*                   hlsPrevPlaylist;
    long                    hlsPrevStartSeqNo       = NoTs;
    int                     hlsPrevSegments         = -1;
    sealed record HLSLiveWindow(int StreamIndex, long StartSequence, long[] SegmentEnds);
    HLSLiveWindow hlsLiveWindow;

    // Publish a managed snapshot so the seekbar never waits on the demuxer or reads FFmpeg pointers.
    HLSLiveWindow CurrentHLSLiveWindow
    {
        get
        {
            var window = Volatile.Read(ref hlsLiveWindow);
            return IsHLSLive && window != null && window.StreamIndex == VideoStream?.StreamIndex ? window : null;
        }
    }

    /// <summary>Gets the HLS video media sequence at a seekbar position. Times are in ticks.</summary>
    public long? GetHLSLiveSegment(long position, long minimumWindowDuration = 0, long timelineDuration = 0)
    {
        if (CurrentHLSLiveWindow is not { } window)
            return null;

        var ends = window.SegmentEnds;
        long duration = ends[^1];
        if (duration <= Math.Max(0, minimumWindowDuration) || position < 0)
            return null;

        // Playback may still have buffered segments that the live playlist has dropped.
        if (timelineDuration > 0)
        {
            if (timelineDuration <= minimumWindowDuration || position > timelineDuration)
                return null;
            position = duration - (timelineDuration - position);
        }
        if (position < 0 || position > duration)
            return null;

        position = Math.Min(position, duration - 1);
        for (int i = 0; i < ends.Length; i++)
            if (position < ends[i])
                return window.StartSequence + i;

        return null;
    }

    /// <summary>Checks whether the current HLS video window contains a media sequence.</summary>
    public bool ContainsHLSLiveSegment(long sequence) => TryGetHLSLiveSegmentOffset(sequence, out _);

    internal bool TryGetHLSLiveSegmentOffset(long sequence, out long offset)
    {
        offset = 0;
        if (CurrentHLSLiveWindow is not { } window)
            return false;

        long index = sequence - window.StartSequence;
        if (index < 0 || index >= window.SegmentEnds.Length)
            return false;

        long start = index == 0 ? 0 : window.SegmentEnds[index - 1];
        long duration = window.SegmentEnds[index] - start;
        offset = start + duration / 2;
        return duration > 0;
    }

    internal bool IsCurrentHLSLiveSegment(long sequence)
        => HLSPlaylist != null && hlsReader.GetWindow(HLSPlaylist).CurSeqNo == sequence;

    // CurPackets Callbacks
    internal void SetHLSDuration(long duration)
    {
        Duration = duration;
        NotifyHLSDuration();
    }

    void NotifyHLSDuration()
    {
        long duration = Duration;
        if (Interlocked.Exchange(ref hlsNotifiedDuration, duration) != duration)
            HLSDurationChanged?.Invoke(duration);
    }

    internal void SetHLSCurTime(long curTime)
    {
        if (curTime != _hlsCurTime)
        {
            _hlsCurTime = curTime;
            HLSCurTimeChanged?.Invoke(curTime);
        }
    }

    internal long HLSFirstTimestamp
    {
        get
        {
            if (hlsCtx == null)
                return NoTs;

            long timestamp = hlsReader.GetFirstTimestamp(hlsCtx);
            return timestamp == NoTs ? NoTs : timestamp * 10;
        }
    }

    long hlsNotifiedDuration;
    long _hlsCurTime; // only to check changes
    public Action<long> HLSDurationChanged;
    public Action<long> HLSCurTimeChanged; // Includes BufferedDurationChanged if possible?*

    void InitializeHLSLive()
    {
        if (!Engine.Config.FFmpegHLSLiveSeek || Duration != 0 || Name != "hls")
            return;

        var reader = HLSLiveReader.Instance;
        if (reader == null)
        {
            Log.Warn($"HLS live seeking unavailable for libavformat {HLSLayouts.FormatVersion(avformat_version())}");
            return;
        }

        if (fmtCtx->priv_data == null)
            return;

        hlsReader = reader;
        hlsCtx = fmtCtx->priv_data;
        StartTime = 0;
    }

    void ResetHLSLive()
    {
        HLSPlaylist         = null;
        hlsCtx              = null;
        hlsReader           = null;
        hlsPrevPlaylist     = null;
        hlsPrevSeqNo        = NoTs;
        hlsPrevStartSeqNo   = NoTs;
        hlsPrevSegments     = -1;
        hlsStartTime        = NoTs;
        hlsCurDuration      = 0;
        hlsNotifiedDuration = 0;
        Volatile.Write(ref hlsLiveWindow, null);
    }

    internal void* FindHLSPlaylist(int streamIndex)
    {
        if (hlsCtx == null)
            return null;

        int count = hlsReader.GetPlaylistCount(hlsCtx);
        if (count <= 0 || count > 4096)
            return null;

        var playlists = hlsReader.GetPlaylists(hlsCtx);
        if (playlists == null)
            return null;

        for (int i = 0; i < count; i++)
        {
            void* playlist = playlists[i];
            if (playlist == null)
                continue;

            int streamsCount = hlsReader.GetMainStreamCount(playlist);
            if (streamsCount <= 0 || streamsCount > 4096)
                continue;

            var streams = hlsReader.GetMainStreams(playlist);
            if (streams == null)
                continue;

            for (int l = 0; l < streamsCount; l++)
                if (streams[l] != null && streams[l]->index == streamIndex)
                {
                    Log.Debug($"Stream #{streamIndex} Found in playlist {i}");
                    return playlist;
                }
        }

        return null;
    }

    internal void UpdateHLSTime()
    {
        var playlist = HLSPlaylist;
        if (playlist == null)
            return;

        var  window         = hlsReader.GetWindow(playlist);
        long seqNo          = window.CurSeqNo;
        long startSeqNo     = window.StartSeqNo;
        int  segmentCount   = window.SegmentCount;
        long index          = seqNo - startSeqNo;

        // Reject invalid or temporarily inconsistent playlist windows
        if (segmentCount <= 0 || index < 0 || index > segmentCount || window.Segments == null)
            return;

        bool windowChanged = hlsPrevPlaylist != playlist || hlsPrevStartSeqNo != startSeqNo || hlsPrevSegments != segmentCount;
        int videoIndex = VideoStream?.StreamIndex ?? -1;
        bool snapshotChanged = videoIndex >= 0 && (windowChanged || hlsLiveWindow?.StreamIndex != videoIndex);
        bool timingChanged = windowChanged || snapshotChanged || hlsPrevSeqNo != seqNo;
        if (timingChanged)
        {
            long[] segmentEnds = snapshotChanged ? new long[segmentCount] : null;
            long curDuration = 0;
            long duration = 0;

            for (int i = 0; i < segmentCount; i++)
            {
                var segment = window.Segments[i];
                if (segment == null)
                    return;

                long segmentDuration = hlsReader.GetSegmentDuration(segment);
                duration += segmentDuration;
                if (segmentEnds != null)
                    segmentEnds[i] = duration * 10;
                if (i < index)
                    curDuration += segmentDuration;
            }

            long newCurDuration = curDuration * 10;

            if (hlsPrevPlaylist != playlist ||
                hlsPrevSeqNo != seqNo ||
                hlsPrevStartSeqNo != startSeqNo ||
                hlsCurDuration != newCurDuration)
                hlsStartTime = NoTs;

            hlsCurDuration      = newCurDuration;
            Duration            = duration * 10;
            if (segmentEnds != null)
                Volatile.Write(ref hlsLiveWindow, new(videoIndex, startSeqNo, segmentEnds));

            hlsPrevPlaylist     = playlist;
            hlsPrevSeqNo        = seqNo;
            hlsPrevStartSeqNo   = startSeqNo;
            hlsPrevSegments     = segmentCount;
        }

        if (hlsStartTime == NoTs && CurPackets.LastTimestamp != NoTs)
        {
            hlsStartTime = CurPackets.LastTimestamp - hlsCurDuration;
            CurPackets.UpdateCurTime();
        }

        if (timingChanged)
            NotifyHLSDuration();
    }
}
