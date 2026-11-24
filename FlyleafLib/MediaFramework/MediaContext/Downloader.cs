using System.Diagnostics;

using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaRemuxer;

/* TODO
 * Don't let audio go further than video (because of large duration without video)?
 *
 */

namespace FlyleafLib.MediaFramework.MediaContext;

public enum DownloadResult
{
    None,
    Completed,
    Stopped,
    Failed
}

/// <summary>
/// Downloads or remuxes to different format any ffmpeg valid input url
/// </summary>
public unsafe class Downloader : RunThreadBase
{
    /// <summary>
    /// The backend demuxer. Access this to enable the required streams for downloading
    /// </summary>
    //public Demuxer      Demuxer             { get; private set; }

    public DecoderContext DecCtx { get; private set; }
    internal Demuxer AudioDemuxer => DecCtx.AudioDemuxer;

    /// <summary>
    /// The backend remuxer. Normally you shouldn't access this
    /// </summary>
    public Remuxer      Remuxer             { get; private set; }

    /// <summary>
    /// The current timestamp of the frame starting from 0 (Ticks)
    /// </summary>
    public long         CurTime             { get => _CurTime; private set => Set(ref _CurTime,  value); }
    long _CurTime;

    /// <summary>
    /// The total duration of the input (Ticks)
    /// </summary>
    public long         Duration            { get => _Duration; private set => Set(ref _Duration, value); }
    long _Duration;

    /// <summary>
    /// The percentage of the current download process (0 for live streams)
    /// </summary>
    public double       DownloadPercentage  { get => _DownloadPercentage;     set => Set(ref _DownloadPercentage,  value); }
    double _DownloadPercentage;
    double downPercentageFactor;

    /// <summary>Terminal result of the current download; None before completion.</summary>
    public DownloadResult Result { get => result; private set => Set(ref result, value); }
    DownloadResult result;
    /// <summary>Error returned by the failing operation, if any.</summary>
    public string LastError { get => lastError; private set => Set(ref lastError, value); }
    string lastError;
    /// <summary>Output path, retained after completion or disposal.</summary>
    public string OutputFilename { get => outputFilename; private set => Set(ref outputFilename, value); }
    string outputFilename;
    /// <summary>Bytes of media packets successfully submitted to the muxer (excludes container overhead).</summary>
    public long BytesWritten { get => bytesWritten; private set => Set(ref bytesWritten, value); }
    long bytesWritten;
    /// <summary>Recent media payload throughput in bytes per second; zero when paused or finished.</summary>
    public double BytesPerSecond { get => bytesPerSecond; private set => Set(ref bytesPerSecond, value); }
    double bytesPerSecond;
    /// <summary>Observed output file size; may lag buffered writes until finalization.</summary>
    public long OutputFileSize { get => outputFileSize; private set => Set(ref outputFileSize, value); }
    long outputFileSize;
    /// <summary>Number of recoverable muxer packet write errors encountered during this download.</summary>
    public long PacketWriteErrors { get => packetWriteErrors; private set => Set(ref packetWriteErrors, value); }
    long packetWriteErrors;
    /// <summary>Maximum consecutive invalid packet errors per stream to tolerate; zero makes any such error fatal.</summary>
    public int MaxConsecutivePacketErrors { get; set; } = 30;
    readonly Dictionary<(bool External, int Stream), int> consecutivePacketErrors = [];

    long writtenBytes, mediaTime;
    long progressOrigin = NoTs;
    int generation;
    bool downloadStarted;
    bool completionScheduled;

    public Downloader(Config config, int uniqueId = -1) : base(uniqueId)
    {
        DecCtx = new DecoderContext(config, UniqueId, false);
        DecCtx.AudioDemuxer.Config = config.Demuxer.Clone(); // We change buffer duration and we need to avoid changing video demuxer's config

        //DecCtx.VideoInputOpened += (o, e) => { if (!e.Success) OnDownloadCompleted(false); };
        Remuxer = new Remuxer(UniqueId);
        threadName = "Downloader";
    }

    /// <summary>
    /// Fires on download completed or failed
    /// </summary>
    public event EventHandler<bool> DownloadCompleted;
    protected virtual void OnDownloadCompleted(bool success)
    {
        int completedGeneration = generation;
        Task.Run(() =>
        {
            lock (lockActions)
            {
                if (completedGeneration != generation) return;
                if (thread != null && thread.IsAlive) thread.Join();
                DisposeResources();
            }
            // Do not publish completion for an input which has already been replaced.
            if (completedGeneration == Volatile.Read(ref generation)) DownloadCompleted?.Invoke(this, success);
        });
    }

    /// <summary>
    /// Opens a new media file (audio/video) and prepares it for download (blocking)
    /// </summary>
    /// <param name="url">Media file's url</param>
    /// <param name="defaultPlaylistItem">Whether to open the default input (in case of multiple inputs eg. from bitswarm/youtube-dl, you might want to choose yours)</param>
    /// <param name="defaultVideo">Whether to open the default video stream from plugin suggestions</param>
    /// <param name="defaultAudio">Whether to open the default audio stream from plugin suggestions</param>
    /// <returns></returns>
    public string Open(string url, bool defaultPlaylistItem = true, bool defaultVideo = true, bool defaultAudio = true) => Open((object) url, defaultPlaylistItem, defaultVideo, defaultAudio);

    /// <summary>
    /// Opens a new media file (audio/video) and prepares it for download (blocking)
    /// </summary>
    /// <param name="stream">Media Stream</param>
    /// <param name="defaultPlaylistItem">Whether to open the default input (in case of multiple inputs eg. from bitswarm/youtube-dl, you might want to choose yours)</param>
    /// <param name="defaultVideo">Whether to open the default video stream from plugin suggestions</param>
    /// <param name="defaultAudio">Whether to open the default audio stream from plugin suggestions</param>
    /// <returns></returns>
    public string Open(Stream stream, bool defaultPlaylistItem = true, bool defaultVideo = true, bool defaultAudio = true) => Open((object) stream, defaultPlaylistItem, defaultVideo, defaultAudio);

    internal string Open(object url, bool defaultPlaylistItem = true, bool defaultVideo = true, bool defaultAudio = true)
    {
        lock (lockActions)
        {
            Dispose();
            generation++;
            downloadStarted = completionScheduled = false;
            Result = DownloadResult.None;
            LastError = OutputFilename = null;
            writtenBytes = mediaTime = 0;
            progressOrigin = NoTs;
            BytesWritten = OutputFileSize = 0;
            PacketWriteErrors = 0;
            consecutivePacketErrors.Clear();
            BytesPerSecond = 0;
            Duration = CurTime = 0;
            DownloadPercentage = 0;

            Disposed= false;
            Status  = Status.Opening;
            DecCtx.shouldDispose = false; // TBR (We shouldn't re-use the same DecCtx?)
            var ret = DecCtx.Open(url, defaultPlaylistItem, defaultVideo, defaultAudio, false);
            if (ret != null && ret.Error != null)
            {
                LastError = ret.Error;
                Result = DownloadResult.Failed;
                DisposeResources();
                return ret.Error;
            }

            CurTime = 0;
            DownloadPercentage = 0;

            var Demuxer = !DecCtx.VideoDemuxer.Disposed ? DecCtx.VideoDemuxer : DecCtx.AudioDemuxer;
            Duration = Demuxer.IsLive ? 0 : Demuxer.Duration;
            downPercentageFactor = Duration / 100.0;

            return null;
        }
    }

    /// <summary>
    /// Downloads the currently configured AVS streams
    /// </summary>
    /// <param name="filename">The filename for the downloaded video. The file extension will let the demuxer to choose the output format (eg. mp4). If you useRecommendedExtension will be updated with the extension.</param>
    /// <param name="useRecommendedExtension">Will try to match the output container with the input container</param>
    public void Download(ref string filename, bool useRecommendedExtension = true)
    {
        lock (lockActions)
        {
            if (Status != Status.Opening || Disposed || downloadStarted)
                throw new InvalidOperationException("Open an input before starting a new download.");

            downloadStarted = true;

            if (useRecommendedExtension)
                filename = $"{filename}.{(!DecCtx.VideoDemuxer.Disposed ? DecCtx.VideoDemuxer.Extension : DecCtx.AudioDemuxer.Extension)}";

            OutputFilename = filename;
            try
            {
                int ret = Remuxer.Open(filename);
                if (ret != 0)
                    { FinishDownload(DownloadResult.Failed, FormatError("Opening output", ret)); return; }

                ret = AddStreams(DecCtx.VideoDemuxer);
                if (ret == 0) ret = AddStreams(DecCtx.AudioDemuxer);
                if (ret != 0)
                    { FinishDownload(DownloadResult.Failed, FormatError("Adding output stream", ret)); return; }

                if (!Remuxer.HasStreams)
                    { FinishDownload(DownloadResult.Failed, "No audio or video streams selected"); return; }
                ret = Remuxer.WriteHeader();
                if (ret != 0)
                    { FinishDownload(DownloadResult.Failed, FormatError("Writing output header", ret)); return; }

                Start();
            }
            catch (Exception ex) { FinishDownload(DownloadResult.Failed, ex.Message); }
        }
    }

    private int AddStreams(Demuxer demuxer)
    {
        if (demuxer.Disposed) return 0;
        for(int i=0; i<demuxer.EnabledStreams.Count; i++)
        {
            var stream = demuxer.AVStreamToStream[demuxer.EnabledStreams[i]];
            if (stream.Type is not (MediaType.Audio or MediaType.Video)) continue;
            int ret = Remuxer.AddStream(stream.AVStream, demuxer.Type == MediaType.Audio);
            if (ret != 0) return ret;
        }
        return 0;
    }

    static string FormatError(string operation, int error) => $"{operation}: {FFmpegEngine.ErrorCodeToMsg(error)} ({error})";

    /// <summary>Stops a download and finalizes the partial output, including when paused.</summary>
    public override void Stop()
    {
        lock (lockActions)
        {
            if (Disposed || !downloadStarted) return;
            base.Stop();
            if (thread != null && thread.IsAlive && thread != Thread.CurrentThread) thread.Join();
            if (Result == DownloadResult.None) FinishDownload(DownloadResult.Stopped);
            DisposeResources();
        }
    }

    /// <summary>
    /// Stops and disposes the downloader
    /// </summary>
    public void Dispose()
    {
        if (Disposed) return;

        lock (lockActions)
        {
            if (Disposed) return;

            Stop();

            DisposeResources();
        }
    }

    void DisposeResources()
    {
        if (Disposed) return;

        DecCtx.Dispose();
        Remuxer.Dispose();

        Status = Status.Stopped;
        Disposed = true;
    }

    public long AudioBufferDurationWithVideo = TimeSpan.FromSeconds(2).Ticks;

    void UpdateProgress()
    {
        BytesWritten = writtenBytes;
        CurTime = Duration > 0 ? Math.Min(mediaTime, Duration) : mediaTime;
        if (Duration > 0) DownloadPercentage = Math.Clamp(CurTime / downPercentageFactor, 0, 99.9);
        if (OutputFilename != null)
            try { OutputFileSize = new FileInfo(OutputFilename).Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
    }

    void FinishDownload(DownloadResult outcome, string error = null)
    {
        if (completionScheduled) return;
        completionScheduled = true;

        if (!Remuxer.Disposed)
        {
            int ret = Remuxer.WriteTrailer();
            if (ret != 0 && outcome != DownloadResult.Failed)
            {
                outcome = DownloadResult.Failed;
                error = FormatError("Finalizing output", ret);
            }
        }

        UpdateProgress();
        BytesPerSecond = 0;
        if (outcome == DownloadResult.Completed && PacketWriteErrors != 0 && writtenBytes == 0)
        {
            outcome = DownloadResult.Failed;
            error = "No media packets could be written to the output.";
        }
        if (PacketWriteErrors != 0 && CanWarn) Log.Warn($"Download finished with {PacketWriteErrors} recoverable packet write error(s)");
        if (outcome == DownloadResult.Completed && Duration > 0)
        {
            CurTime = Duration;
            DownloadPercentage = 100;
        }
        LastError = error;
        Result = outcome;
        // Preserve the sample's distinction between a finalized partial file and a failure.
        OnDownloadCompleted(outcome != DownloadResult.Failed);
    }

    protected override void RunInternal()
    {
        // Don't allow audio to change our duration without video (TBR: requires timestamp of videodemuxer to wait)
        long resetBufferDuration = -1;
        bool hasAVDemuxers = !DecCtx.VideoDemuxer.Disposed && !DecCtx.AudioDemuxer.Disposed;
        if (hasAVDemuxers)
        {
            resetBufferDuration = DecCtx.AudioDemuxer.Config.BufferDuration;
            DecCtx.AudioDemuxer.Config.BufferDuration = AudioBufferDurationWithVideo;
        }

        var Demuxer = !DecCtx.VideoDemuxer.Disposed ? DecCtx.VideoDemuxer : DecCtx.AudioDemuxer;
        var auxiliary = hasAVDemuxers ? AudioDemuxer : null;
        bool mainIsAudio = Demuxer == AudioDemuxer;
        DownloadResult outcome = DownloadResult.Stopped;
        string error = null;
        long lastBytes = writtenBytes;
        var clock = Stopwatch.StartNew();
        bool timerStarted = false;

        try
        {
            if (!Remuxer.HasStreams) throw new InvalidOperationException("No streams have been configured for download");

            Engine.TimeBeginPeriod1();
            timerStarted = true;
            // Only demux the selected A/V inputs; decoding is not required.
            Demuxer.Start();
            auxiliary?.Start();
            Duration = Demuxer.IsLive ? 0 : Demuxer.Duration;
            downPercentageFactor = Duration / 100.0;

            while (Status is Status.Running or Status.QueueEmpty)
            {
                // Wait for a packet or EOF from each input before choosing the next packet.
                // An ended input may still have queued packets, which must be drained.
                var mainStatus = Demuxer.Status;
                var audioStatus = auxiliary?.Status;
                bool mainEmpty = Demuxer.Packets.Count == 0;
                bool audioEmpty = auxiliary == null || auxiliary.Packets.Count == 0;
                bool mainEnded = mainEmpty && mainStatus == Status.Ended;
                bool audioEnded = auxiliary == null || (audioEmpty && audioStatus == Status.Ended);

                if (mainEmpty && !mainEnded && !Demuxer.IsRunning)
                {
                    // The producer may have reached EOF or enqueued its last packet since our snapshot.
                    if (Demuxer.Status == Status.Ended || Demuxer.Packets.Count != 0) continue;
                    outcome = DownloadResult.Failed;
                    error = ReadError(Demuxer, "Input");
                    break;
                }
                if (auxiliary != null && audioEmpty && !audioEnded && !auxiliary.IsRunning)
                {
                    if (auxiliary.Status == Status.Ended || auxiliary.Packets.Count != 0) continue;
                    outcome = DownloadResult.Failed;
                    error = ReadError(auxiliary, "External audio");
                    break;
                }
                if (mainEnded && audioEnded)
                {
                    outcome = DownloadResult.Completed;
                    break;
                }

                if ((mainEmpty && !mainEnded) || (audioEmpty && !audioEnded))
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueEmpty;
                    UpdateRate();
                    Thread.Sleep(10);
                    continue;
                }

                lock (lockStatus)
                {
                    if (Status == Status.QueueEmpty) Status = Status.Running;
                    if (Status != Status.Running) break;
                }

                // Peek under the queue lock because the producer can resize the underlying Queue.
                AVPacket* packet = null;
                AVPacket* packet2 = null;
                if (!mainEmpty) lock (Demuxer.Packets) packet = Demuxer.Packets.Peek();
                if (!audioEmpty) lock (auxiliary.Packets) packet2 = auxiliary.Packets.Peek();
                bool externalAudio = packet == null || (packet2 != null && PacketTime(packet2, auxiliary) <= PacketTime(packet, Demuxer));
                var source = externalAudio ? auxiliary : Demuxer;
                if (source == null) continue;
                packet = source.Packets.Dequeue();
                if (packet == null) continue;

                var stream = source.AVStreamToStream[packet->stream_index];
                if (stream.Type is not (MediaType.Audio or MediaType.Video))
                {
                    av_packet_free(&packet);
                    continue;
                }

                long timestamp = packet->dts != NoTs ? packet->dts : packet->pts;
                long packetEnd = NoTs;
                if (!externalAudio && timestamp != NoTs)
                {
                    long ticks = (long)(timestamp * stream.Timebase);
                    if (progressOrigin == NoTs)
                        progressOrigin = Demuxer.IsLive || Demuxer.StartTime == NoTs ? ticks : Demuxer.StartTime;
                    packetEnd = Math.Max(0, ticks - progressOrigin + (long)(packet->duration * stream.Timebase));
                }
                int size = packet->size;
                var streamKey = (externalAudio || mainIsAudio, packet->stream_index);
                int ret = Remuxer.Write(packet, streamKey.Item1);
                int ioError = Remuxer.OutputError;
                if (ioError != 0 || ret != 0)
                {
                    // A malformed packet (e.g. invalid timestamps) can be rejected without an output I/O failure.
                    // FFmpeg owns the submitted packet after Write; never retry that packet.
                    if (ioError == 0 && ret == AVERROR_EINVAL)
                    {
                        int errors = consecutivePacketErrors.GetValueOrDefault(streamKey) + 1;
                        if (errors <= MaxConsecutivePacketErrors)
                        {
                            consecutivePacketErrors[streamKey] = errors;
                            PacketWriteErrors++;
                            if (CanWarn && (errors == 1 || PacketWriteErrors % 100 == 0))
                                Log.Warn($"Ignoring output packet error (stream #{streamKey.Item2}, external audio: {streamKey.Item1}): {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");
                            if (packetEnd != NoTs) mediaTime = Math.Max(mediaTime, packetEnd);
                            UpdateRate();
                            continue;
                        }
                    }
                    outcome = DownloadResult.Failed;
                    error = FormatError(ioError != 0 ? "Writing output IO" : "Writing output packet", ioError != 0 ? ioError : ret);
                    break;
                }
                consecutivePacketErrors.Remove(streamKey);
                writtenBytes += size;
                if (packetEnd != NoTs) mediaTime = Math.Max(mediaTime, packetEnd);
                UpdateRate();
            }

            if (Status == Status.Pausing && outcome != DownloadResult.Failed && outcome != DownloadResult.Completed)
            {
                DecCtx.Pause();
                UpdateProgress();
                BytesPerSecond = 0;
                return;
            }
        }
        catch (Exception ex)
        {
            outcome = DownloadResult.Failed;
            error = ex.Message;
        }
        finally
        {
            if (timerStarted) Engine.TimeEndPeriod1();
            if (resetBufferDuration != -1) DecCtx.AudioDemuxer.Config.BufferDuration = resetBufferDuration;
        }

        lock (lockStatus)
        {
            if (outcome == DownloadResult.Completed && Status == Status.Stopping) outcome = DownloadResult.Stopped;
            Status = outcome == DownloadResult.Completed ? Status.Ended : Status.Stopping;
        }
        FinishDownload(outcome, error);

        void UpdateRate()
        {
            double seconds = clock.Elapsed.TotalSeconds;
            if (seconds < 1) return;
            BytesPerSecond = (writtenBytes - lastBytes) / seconds;
            lastBytes = writtenBytes;
            clock.Restart();
            UpdateProgress();
        }
    }

    static string ReadError(Demuxer demuxer, string input)
        => demuxer.Interrupter.Timedout ? $"{input} read timed out"
            : demuxer.LastReadError != 0 ? FormatError($"Reading {input.ToLowerInvariant()}", demuxer.LastReadError)
            : $"{input} demuxer stopped unexpectedly ({demuxer.Status})";

    static double PacketTime(AVPacket* packet, Demuxer demuxer)
    {
        long timestamp = packet->dts != NoTs ? packet->dts : packet->pts;
        return timestamp == NoTs ? double.NegativeInfinity : timestamp * demuxer.AVStreamToStream[packet->stream_index].Timebase - demuxer.StartTime;
    }
}
