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

    // CurPackets Callbacks
    internal void SetHLSDuration(long duration)
    {
        if (Duration != duration)
        {
            Duration = duration;
            HLSDurationChanged?.Invoke(duration);
        }
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

        if (hlsPrevPlaylist     != playlist ||
            hlsPrevSeqNo        != seqNo ||
            hlsPrevStartSeqNo   != startSeqNo ||
            hlsPrevSegments     != segmentCount)
        {
            long curDuration = 0;
            long duration = 0;

            for (int i = 0; i < segmentCount; i++)
            {
                var segment = window.Segments[i];
                if (segment == null)
                    return;

                long segmentDuration = hlsReader.GetSegmentDuration(segment);
                duration += segmentDuration;
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
    }
}
