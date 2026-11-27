using System.Linq;
using System.Windows.Media.Imaging;

using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaContext;

public unsafe partial class DecoderContext
{
    const int PreviewSourceHeight = 360;

    /// <summary>
    /// Opens an independent, video-only decoder from an already resolved file or media URL.
    /// Returns null if the input cannot provide a seekable video timeline. The caller owns
    /// the context and must serialize frame requests and disposal off the UI thread.
    /// </summary>
    public static DecoderContext OpenVideoPreview(DemuxerInput input, InputType inputType,
        int streamIndex, Config.DemuxerConfig demuxerConfig, CancellationToken cancellationToken = default)
    {
        if (input?.Url == null || input.IOStream != null || cancellationToken.IsCancellationRequested ||
            inputType is not (InputType.File or InputType.UNC or InputType.Web))
            return null;

        var config = new Config(forVideoPreview: true) { Demuxer = demuxerConfig?.Clone() ?? new() };
        config.Demuxer.config = config;
        config.Demuxer.AllowInterrupts = true;
        config.Demuxer.AllowReadInterrupts = true;
        config.Demuxer.AllowTimeouts = true;
        config.Demuxer.CloseTimeout = TimeSpan.TicksPerSecond;
        config.Video.MaxVerticalResolutionCustom = PreviewSourceHeight;
        config.Video.MaxFrames = 1;
        config.Video.DecoderThreads = Math.Min(2, Environment.ProcessorCount);

        var preview = new DecoderContext(config, -1, true, null, loadPlugins: false)
        {
            AudioEnabled = false,
            SubtitlesEnabled = false,
            DataEnabled = false
        };
        bool opened = false;

        try
        {
            preview.Playlist.InputType = inputType;
            var demuxer = preview.VideoDemuxer;
            demuxer.Interrupter.OperationCancellation = cancellationToken;
            if (preview.OpenDemuxerInput(demuxer, input) != null || cancellationToken.IsCancellationRequested ||
                demuxer.IsLive || demuxer.Duration <= 0)
                return null;

            bool adaptive = demuxer.Name is "hls" or "dash";
            if (inputType == InputType.Web && !adaptive && demuxer.FormatContext->pb != null &&
                demuxer.FormatContext->pb->seekable == 0)
                return null;

            int preferredHeight = config.Video.MaxVerticalResolution;
            var stream = demuxer.VideoStreams.FirstOrDefault(s => s.StreamIndex == streamIndex);
            if (!adaptive && streamIndex >= 0 && stream == null)
                return null;

            if (adaptive && stream != null && stream.Height > preferredHeight && stream.Width > 0)
            {
                double ratio = (double)stream.Width / stream.Height;
                stream = demuxer.VideoStreams
                    .Where(s => s.Height > 0 && s.Height <= preferredHeight &&
                        Math.Abs((double)s.Width / s.Height - ratio) < 0.05)
                    .MaxBy(s => s.Height) ?? stream;
            }
            stream ??= demuxer.VideoStreams.Where(s => s.Height > 0 && s.Height <= preferredHeight).MaxBy(s => s.Height)
                ?? demuxer.VideoStreams.FirstOrDefault();

            opened = stream != null && preview.Open(stream, defaultAudio: false).Success && !cancellationToken.IsCancellationRequested;
            return opened ? preview : null;
        }
        finally
        {
            preview.VideoDemuxer.Interrupter.OperationCancellation = default;
            if (!opened)
                preview.Dispose();
        }
    }

    /// <summary>
    /// Seeks a context opened by <see cref="OpenVideoPreview"/> and returns one frozen bitmap
    /// and its actual media timestamp in ticks, or null if no frame was decoded.
    /// </summary>
    public (BitmapSource Image, long Timestamp)? GetVideoPreviewFrame(long timestamp, bool accurate,
        uint maxWidth, uint maxHeight, CancellationToken cancellationToken = default)
    {
        if (player != null || VideoStream == null || VideoDecoder.IsRunning || VideoDemuxer.IsRunning ||
            maxWidth == 0 || maxHeight == 0 || cancellationToken.IsCancellationRequested)
            return null;

        long frameDuration = Math.Max(VideoStream.FrameDuration, TimeSpan.TicksPerMillisecond);
        long duration = VideoStream.Duration > 0 ? Math.Min(VideoStream.Duration, VideoDemuxer.Duration) : VideoDemuxer.Duration;
        long lastTimestamp = Math.Max(0, duration - frameDuration);
        timestamp = Math.Clamp(timestamp, 0, lastTimestamp);
        long ms = timestamp / TimeSpan.TicksPerMillisecond;
        var interrupter = VideoDemuxer.Interrupter;
        interrupter.OperationCancellation = cancellationToken;

        try
        {
            long target = accurate ? timestamp : -1;
            var frame = DecodePreviewFrame(ms, target, cancellationToken);
            // Some demuxers can land after the target even on a backward seek.
            if (accurate && ms > 0 && !cancellationToken.IsCancellationRequested &&
                (frame == null || frame.Timestamp - timestamp > frameDuration))
                frame = DecodePreviewFrame(Math.Max(0, ms - 3000), target, cancellationToken);

            var renderer = VideoDecoder.Renderer;
            if (frame == null || cancellationToken.IsCancellationRequested)
                return null;

            var ratio = renderer.curRatio;
            if (!double.IsFinite(ratio) || ratio <= 0)
                ratio = (double)maxWidth / maxHeight;

            uint width = (uint)Math.Clamp(Math.Round(maxHeight * ratio), 1, maxWidth);
            uint height = (uint)Math.Clamp(Math.Round(width / ratio), 1, maxHeight);
            renderer.RenderRequest(frame);
            var image = renderer.TakeSnapshotBitmapSource(width, height);
            return image != null && !cancellationToken.IsCancellationRequested ? (image, frame.Timestamp) : null;
        }
        finally
        {
            interrupter.OperationCancellation = default;
        }
    }

    VideoFrame DecodePreviewFrame(long ms, long target, CancellationToken cancellationToken)
    {
        if (Seek(ms, seekInQueue: false) < 0 || cancellationToken.IsCancellationRequested)
            return null;

        GetVideoFrame(target, cancellationToken);
        return VideoDecoder.Renderer.Frames.TryDequeue(out var frame) ? frame : null;
    }
}
