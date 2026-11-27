using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

internal sealed class FlyleafSeekPreview
{
    const int CacheCapacity = 40, CacheByteLimit = 12 * 1024 * 1024, IdleMilliseconds = 60_000;

    sealed record Source(DemuxerInput Input, DemuxerInput? SmallerInput, InputType Type,
        int StreamIndex, long Duration, Config.DemuxerConfig DemuxerConfig);
    readonly record struct FrameKey(bool Accurate, long Bucket, (uint Width, uint Height) Size);
    readonly record struct FrameRequest(FrameKey Key, long Time);

    readonly DispatcherTimer timer;
    readonly Action<BitmapSource?> showImage;
    readonly Dictionary<FrameKey, BitmapSource> cache = [];
    readonly Queue<FrameKey> cacheOrder = new();
    PlaylistItem? trackedItem;
    VideoStream? trackedStream;
    Source? source, decoderSource;
    DecoderContext? decoder;
    CancellationTokenSource? operation;
    FrameRequest? latest;
    FrameKey? attempted;
    long? shownTime;
    long nextRequestAt, settleAt, idleAt;
    int failures, cacheBytes;
    bool busy, unavailable;

    internal FlyleafSeekPreview(Dispatcher dispatcher, Action<BitmapSource?> showImage)
    {
        this.showImage = showImage;
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        timer.Tick += Process;
    }

    internal void UpdateSource(Player? player)
    {
        if (player is not { CanPlay: true, IsDisposed: false, IsLive: false, Status: not Status.Opening })
            player = null;
        var item = player?.Playlist.Selected;
        var stream = player?.Video.Stream;
        if (ReferenceEquals(trackedItem, item) && ReferenceEquals(trackedStream, stream))
            return;

        operation?.Cancel();
        trackedItem = item;
        trackedStream = stream;
        source = player != null && item != null && stream != null ? CaptureSource(player, item, stream) : null;
        latest = null;
        attempted = null;
        unavailable = false;
        failures = 0;
        idleAt = nextRequestAt = 0;
        cache.Clear();
        cacheOrder.Clear();
        cacheBytes = 0;
        Publish(null);
        Schedule();
    }

    internal void Request(long time, bool accurate, (uint Width, uint Height) size)
    {
        if (source == null || unavailable)
            return;

        bool local = source.Type == InputType.File;
        long bucketSize = TimeSpan.TicksPerMillisecond * (accurate ? 1L : local ? 750L : 1500L);
        var key = new FrameKey(accurate, time / bucketSize, size);
        var now = Environment.TickCount64;
        if (latest == null || (accurate && latest.Value.Key != key))
            settleAt = now + (accurate ? 170 : 100);
        latest = new(key, time);
        idleAt = now + IdleMilliseconds;

        if (cache.TryGetValue(key, out var image))
            Publish(image, time);
        else if (shownTime is long previous && !IsNearby(source, previous, time))
            Publish(null);

        Schedule();
    }

    internal void Hide(bool interrupt = false)
    {
        latest = null;
        attempted = null;
        if (interrupt)
            operation?.Cancel();
        Publish(null);
        Schedule();
    }

    void Publish(BitmapSource? image, long? time = null)
    {
        shownTime = time;
        showImage(image);
    }

    static bool IsNearby(Source source, long previous, long current)
        => Math.Abs(previous - current) <= Math.Max(3 * TimeSpan.TicksPerSecond, source.Duration / 80);

    bool HasRequest => !unavailable && latest is { } request && attempted != request.Key && !cache.ContainsKey(request.Key);

    void Schedule()
    {
        timer.Stop();
        if (busy)
            return;

        long now = Environment.TickCount64;
        long wakeAt = long.MaxValue;
        if (decoder != null)
            wakeAt = ReferenceEquals(decoderSource, source) && !unavailable ? idleAt : now;
        if (HasRequest)
            wakeAt = Math.Min(wakeAt, Math.Max(nextRequestAt, settleAt));
        if (wakeAt == long.MaxValue)
            return;

        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, wakeAt - now));
        timer.Start();
    }

    async void Process(object? sender, EventArgs e)
    {
        timer.Stop();
        busy = true;
        try
        {
            var now = Environment.TickCount64;
            if (decoder is { } old && (!ReferenceEquals(decoderSource, source) || unavailable || now >= idleAt))
            {
                decoder = null;
                decoderSource = null;
                await Task.Run(old.Dispose);
            }

            if (source is not { } current || latest is not { } request || !HasRequest ||
                Environment.TickCount64 < Math.Max(nextRequestAt, settleAt))
                return;

            attempted = request.Key;
            nextRequestAt = Environment.TickCount64 + (current.Type == InputType.File ? 125 : 275);
            var context = decoder;
            using var cancellation = new CancellationTokenSource();
            operation = cancellation;
            try
            {
                var result = await Task.Run(() => Decode(current, context, request, cancellation.Token));
                decoder = result.Context;
                decoderSource = current;
                if (!ReferenceEquals(source, current) || cancellation.IsCancellationRequested)
                    return;

                idleAt = Environment.TickCount64 + IdleMilliseconds;
                if (result.Image is not { } image)
                {
                    if (decoder == null || (!request.Key.Accurate && ++failures >= 2))
                        unavailable = true;
                    if (unavailable || latest?.Key == request.Key)
                        Publish(null);
                    return;
                }

                failures = 0;
                CacheImage(request.Key, image);
                if (latest is { } target && target.Key.Size == request.Key.Size && (target.Key == request.Key ||
                    (!target.Key.Accurate && !request.Key.Accurate && IsNearby(current, request.Time, target.Time))))
                    Publish(image, request.Time);
            }
            finally
            {
                operation = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Seek preview worker failed ({ex.GetType().Name})");
            if (ReferenceEquals(decoderSource, source))
            {
                unavailable = true;
                Publish(null);
            }
        }
        finally
        {
            busy = false;
            Schedule();
        }
    }

    void CacheImage(FrameKey key, BitmapSource image)
    {
        static int ByteCount(BitmapSource bitmap) => bitmap.PixelWidth * bitmap.PixelHeight * 4;
        int bytes = ByteCount(image);
        while (cache.Count > 0 && (cache.Count >= CacheCapacity || cacheBytes + bytes > CacheByteLimit))
        {
            if (cache.Remove(cacheOrder.Dequeue(), out var old))
                cacheBytes -= ByteCount(old);
        }

        cache.Add(key, image);
        cacheOrder.Enqueue(key);
        cacheBytes += bytes;
    }

    static (DecoderContext? Context, BitmapSource? Image) Decode(Source source, DecoderContext? context,
        FrameRequest request, CancellationToken cancellation)
    {
        try
        {
            if (context == null)
            {
                using var open = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                open.CancelAfter(source.Type == InputType.File ? 1500 : 2500);
                if (source.SmallerInput != null)
                {
                    context = DecoderContext.OpenVideoPreview(source.SmallerInput, source.Type, -1, source.DemuxerConfig, open.Token);
                    if (context != null && Math.Abs(context.VideoDemuxer.Duration - source.Duration) > 2 * TimeSpan.TicksPerSecond)
                    {
                        context.Dispose();
                        context = null;
                    }
                }
                context ??= DecoderContext.OpenVideoPreview(source.Input, source.Type, source.StreamIndex, source.DemuxerConfig, open.Token);
            }
            if (context == null || cancellation.IsCancellationRequested)
                return (context, null);

            using var seek = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            seek.CancelAfter(source.Type == InputType.File ? 450 : 800);
            var (width, height) = request.Key.Size;
            var result = context.GetVideoPreviewFrame(request.Time, request.Key.Accurate, width, height, seek.Token);
            return (context, result?.Image);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Seek preview failed ({ex.GetType().Name})");
            return (context, null);
        }
    }

    static Source? CaptureSource(Player player, PlaylistItem item, VideoStream stream)
    {
        if (stream.Demuxer.Duration <= 0 ||
            (stream.TotalFrames <= 1 && FlyleafQueueProgression.IsImageFormat(stream.Demuxer.Name)))
            return null;

        var external = stream.ExternalStream as ExternalVideoStream;
        DemuxerInput input = external ?? (DemuxerInput)item;
        if (input.IOStream != null || input.Url is not { Length: > 0 } url)
            return null;

        InputType type;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
                type = uri.IsUnc ? InputType.UNC : InputType.File;
            else if (uri.Scheme is "http" or "https")
                type = InputType.Web;
            else
                return null;
        }
        else if (player.Playlist.InputType is not InputType.Torrent && !url.Contains(':'))
            type = InputType.File;
        else
            return null;

        DemuxerInput? smaller = null;
        if (type == InputType.Web && external != null && stream.Height > 360)
        {
            double ratio = (double)stream.Width / stream.Height;
            BindingOperations.AccessCollection(item.ExternalVideoStreams, () =>
            {
                var candidate = item.ExternalVideoStreams
                    .Where(s => s.Height is > 0 and <= 360 && s.IOStream == null &&
                        (s.Width <= 0 || ratio <= 0 || Math.Abs((double)s.Width / s.Height - ratio) < 0.05) &&
                        Uri.TryCreate(s.Url, UriKind.Absolute, out var address) && address.Scheme is "http" or "https")
                    .OrderByDescending(s => s.Height)
                    .ThenBy(s => s.HasAudio)
                    .ThenBy(s => s.BitRate)
                    .FirstOrDefault();
                if (candidate != null)
                    smaller = CopyInput(candidate);
            }, false);
        }

        var demuxerConfig = player.Config.Demuxer.Clone();
        demuxerConfig.ExtraHTTPQueryParamsToUnderlying = CopyOptions(demuxerConfig.ExtraHTTPQueryParamsToUnderlying) ?? [];
        return new(CopyInput(input), smaller, type, stream.StreamIndex, stream.Demuxer.Duration, demuxerConfig);
    }

    static DemuxerInput CopyInput(DemuxerInput input) => new()
    {
        Url = input.Url, UrlFallback = input.UrlFallback, Referrer = input.Referrer,
        Cookies = input.Cookies, CookiesFallback = input.CookiesFallback,
        FormatOpt = CopyOptions(input.FormatOpt), HTTPHeaders = CopyOptions(input.HTTPHeaders)
    };

    static Dictionary<string, string>? CopyOptions(Dictionary<string, string>? options) =>
        options == null ? null : new(options, options.Comparer);
}
