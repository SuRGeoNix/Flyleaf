using System.ComponentModel;
using System.Windows.Threading;
using FlyleafLib.MediaPlayer;
using DecoderContext = FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace Flyleaf.Controls.WPF;

/// <summary>Shared current-player information for detailed and compact views. Refreshes only while viewed.</summary>
public sealed class FlyleafMediaInformation : NotifyPropertyChanged, IDisposable
{
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer timer;
    Player? player;
    int viewers;
    bool disposed, buffering, showStatisticsOverlay, enabledViewerStatistics;
    string operationError = "";
    string captureDetails = "No snapshot saved.";
    public string CaptureDetails => captureDetails;
    string recordingDetails = "No recording saved.";
    public string RecordingDetails => recordingDetails;
    internal void ReportCapture(string filename) => Set(ref captureDetails, filename, propertyName: nameof(CaptureDetails));
    internal DetailSection SourceSection { get; } = new("No media open.");
    internal DetailSection PlaybackSection { get; } = new("No player.");
    internal DetailSection VideoSection { get; } = new("No video stream.");
    internal DetailSection AudioSection { get; } = new("No audio stream.");
    internal DetailSection SubtitleSection { get; } = new("No subtitle stream.");
    internal DetailSection DataSection { get; } = new("No data stream.");
    internal DetailSection ProcessingSection { get; } = new("No renderer.");
    internal DetailSection StatisticsSection { get; } = new("Statistics collection is off.");
    string errorDetails = "No reported error.", overlayText = "No media open.";

    internal sealed class DetailRow(string label, string value) : NotifyPropertyChanged
    {
        string text = value;
        public string Label { get; } = label;
        public string Value { get => text; internal set => Set(ref text, value); }
    }

    internal sealed class DetailSection(string emptyText) : NotifyPropertyChanged
    {
        public DetailRow[] Rows { get; private set; } = [new("", emptyText)];

        internal bool Update(DetailRow[] rows)
        {
            if (rows.Length == 0) rows = [new("", emptyText)];
            if (Rows.Select(row => row.Label).SequenceEqual(rows.Select(row => row.Label)))
            {
                bool changed = false;
                for (int i = 0; i < rows.Length; i++)
                {
                    if (Rows[i].Value == rows[i].Value) continue;
                    Rows[i].Value = rows[i].Value;
                    changed = true;
                }
                return changed;
            }
            Rows = rows;
            Raise(nameof(Rows));
            return true;
        }

        internal string GetText() => string.Join("\n", Rows.Select(row =>
            row.Label.Length == 0 ? row.Value : $"{row.Label}: {row.Value}"));
    }

    void UpdateDetails(DetailSection section, string propertyName, params DetailRow[] rows)
    {
        if (section.Update(rows)) Raise(propertyName);
    }

    public Player? Player => player;
    public string SourceDetails => SourceSection.GetText();
    public string PlaybackDetails => PlaybackSection.GetText();
    public string VideoDetails => VideoSection.GetText();
    public string AudioDetails => AudioSection.GetText();
    public string SubtitleDetails => SubtitleSection.GetText();
    public string DataDetails => DataSection.GetText();
    public string ProcessingDetails => ProcessingSection.GetText();
    public string StatisticsDetails => StatisticsSection.GetText();
    public string ErrorDetails => errorDetails;
    public string OverlayText => overlayText;
    public bool HasPlayer => player is { IsDisposed: false };
    public bool StatisticsEnabled => HasPlayer && player!.Stats;
    public bool ShowStatisticsOverlay
    {
        get => showStatisticsOverlay;
        set { dispatcher.VerifyAccess(); Set(ref showStatisticsOverlay, value); }
    }
    public string Summary => string.Join("\n\n", new[]
    {
        "Source\n" + SourceDetails, "Playback\n" + PlaybackDetails, "Video\n" + VideoDetails,
        "Audio\n" + AudioDetails, "Subtitles\n" + SubtitleDetails, "Data\n" + DataDetails,
        "Processing\n" + ProcessingDetails, "Statistics\n" + StatisticsDetails, "Errors\n" + ErrorDetails, "Latest snapshot\n" + CaptureDetails,
        "Latest recording\n" + RecordingDetails
    });

    internal FlyleafMediaInformation()
    {
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Refresh();
    }
    internal void SetPlayer(Player? value)
    {
        dispatcher.VerifyAccess();
        if (disposed || ReferenceEquals(player, value)) return;
        StopStatistics();
        Observe(false); player = value; buffering = false; operationError = ""; Observe(true);
        if (viewers > 0) StartStatistics();
        Raise(nameof(Player)); Refresh();
    }
    void Observe(bool attach)
    {
        if (player == null) return;
        if (attach)
        {
            player.PropertyChanged += PlayerChanged;
            player.Opening += Opening;
            player.OpenCompleted += OpenCompleted;
            player.OpenPlaylistItemCompleted += OpenCompleted;
            player.OpenSessionCompleted += OpenCompleted;
            player.OpenAudioStreamCompleted += OpenCompleted;
            player.OpenVideoStreamCompleted += OpenCompleted;
            player.OpenSubtitlesStreamCompleted += OpenCompleted;
            player.OpenDataStreamCompleted += OpenCompleted;
            player.OpenExternalAudioStreamCompleted += OpenCompleted;
            player.OpenExternalVideoStreamCompleted += OpenCompleted;
            player.OpenExternalSubtitlesStreamCompleted += OpenCompleted;
            player.BufferingStarted += BufferingStarted;
            player.BufferingCompleted += BufferingCompleted;
            player.PlaybackStopped += PlaybackStopped;
        }
        else
        {
            player.PropertyChanged -= PlayerChanged;
            player.Opening -= Opening;
            player.OpenCompleted -= OpenCompleted;
            player.OpenPlaylistItemCompleted -= OpenCompleted;
            player.OpenSessionCompleted -= OpenCompleted;
            player.OpenAudioStreamCompleted -= OpenCompleted;
            player.OpenVideoStreamCompleted -= OpenCompleted;
            player.OpenSubtitlesStreamCompleted -= OpenCompleted;
            player.OpenDataStreamCompleted -= OpenCompleted;
            player.OpenExternalAudioStreamCompleted -= OpenCompleted;
            player.OpenExternalVideoStreamCompleted -= OpenCompleted;
            player.OpenExternalSubtitlesStreamCompleted -= OpenCompleted;
            player.BufferingStarted -= BufferingStarted;
            player.BufferingCompleted -= BufferingCompleted;
            player.PlaybackStopped -= PlaybackStopped;
        }
    }
    void Post(object? sender, Action update)
    {
        if (dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (disposed || !ReferenceEquals(sender, player)) return;
            update(); if (viewers > 0) Refresh();
        }));
    }
    void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlyleafLib.MediaPlayer.Player.Stats) or nameof(FlyleafLib.MediaPlayer.Player.IsDisposed)) Post(sender, Refresh);
    }
    void Opening(object? sender, OpeningArgs e) => Post(sender, () => { if (!e.IsSubtitles) { buffering = false; operationError = ""; } });
    void BufferingStarted(object? sender, EventArgs e) => Post(sender, () => buffering = true);
    void BufferingCompleted(object? sender, BufferingCompletedArgs e) => Post(sender, () => { buffering = false; if (e.Error != null) operationError = e.Error; });
    void PlaybackStopped(object? sender, PlaybackStoppedArgs e) => Post(sender, () => { buffering = false; if (e.Error != null) operationError = e.Error; });
    void OpenCompleted(object? sender, object e)
    {
        var error = e switch
        {
            OpenCompletedArgs result => result.Error,
            DecoderContext.OpenPlaylistItemCompletedArgs result => result.Error,
            DecoderContext.OpenSessionCompletedArgs result => result.Error,
            DecoderContext.StreamOpenedArgs result => result.Error,
            DecoderContext.ExternalStreamOpenedArgs result => result.Error,
            _ => null
        };
        Post(sender, () => operationError = error ?? "");
    }
    internal void ReportOperationError(string error)
    {
        dispatcher.VerifyAccess(); operationError = error;
        if (viewers > 0) Refresh();
    }
    internal IDisposable Acquire()
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (++viewers == 1) { StartStatistics(); Refresh(); timer.Start(); }
        return new Lease(this);
    }
    void StartStatistics()
    {
        if (player is not { IsDisposed: false, Stats: false } target) return;
        enabledViewerStatistics = true;
        target.Stats = true;
    }

    void StopStatistics()
    {
        if (!enabledViewerStatistics) return;
        enabledViewerStatistics = false;
        if (player is { IsDisposed: false } target) target.Stats = false;
    }

    sealed class Lease(FlyleafMediaInformation owner) : IDisposable
    {
        FlyleafMediaInformation? source = owner;
        public void Dispose()
        {
            if (source == null) return;
            source.dispatcher.VerifyAccess();
            if (--source.viewers == 0)
            {
                source.timer.Stop();
                source.StopStatistics();
            }
            source = null;
        }
    }
    static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    internal static string Time(long ticks) => TimeSpan.FromTicks(Math.Max(0, ticks))
        .ToString(ticks >= TimeSpan.TicksPerDay ? @"d\.hh\:mm\:ss\.fff" : @"hh\:mm\:ss\.fff");
    static string StreamStatus(bool enabled) => enabled ? "No stream open." : "Disabled";
    static string OnOff(bool value) => value ? "On" : "Off";
    static string Delay(long ticks) => $"{ticks / (double)TimeSpan.TicksPerMillisecond:0.###} ms";
    static string DisplaySource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Stream input";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.IsFile) return uri?.IsFile == true ? uri.LocalPath : value;
        // Source information is also copied into diagnostics: omit URL credentials and session query data.
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped);
    }
    internal void Refresh()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        Raise(nameof(HasPlayer)); Raise(nameof(StatisticsEnabled));
        Set(ref recordingDetails, player?.LastRecordingFilename ?? "No recording saved.", propertyName: nameof(RecordingDetails));
        if (player is not { IsDisposed: false } p)
        {
            UpdateDetails(SourceSection, nameof(SourceDetails));
            UpdateDetails(PlaybackSection, nameof(PlaybackDetails));
            UpdateDetails(VideoSection, nameof(VideoDetails));
            UpdateDetails(AudioSection, nameof(AudioDetails));
            UpdateDetails(SubtitleSection, nameof(SubtitleDetails));
            UpdateDetails(DataSection, nameof(DataDetails));
            UpdateDetails(ProcessingSection, nameof(ProcessingDetails));
            UpdateDetails(StatisticsSection, nameof(StatisticsDetails));
            Set(ref errorDetails, "No reported error.", propertyName: nameof(ErrorDetails));
            Set(ref overlayText, "No player.", propertyName: nameof(OverlayText));
            return;
        }
        var item = p.Playlist.Selected;
        var title = item?.Title ?? p.Playlist.Title;
        var source = p.Playlist.Url == null && p.Playlist.IOStream == null ? "No input" : DisplaySource(p.Playlist.Url);
        UpdateDetails(SourceSection, nameof(SourceDetails),
            new("Title", Value(title)), new("Location", source),
            new("Container", Value(p.MainDemuxer?.LongName)), new("Player", $"{p.PlayerId} · {p.Usage}"));
        UpdateDetails(PlaybackSection, nameof(PlaybackDetails),
            new("Status", $"{p.Status}{(buffering ? " · Buffering" : "")}"),
            new("Position", $"{Time(p.CurTime)} / {Time(p.Duration)}{(p.CanPlay && p.IsLive ? " · Live" : "")}"),
            new("Buffered", p.CanPlay ? Time(p.BufferedDuration) : "—"),
            new("Speed", $"{p.Speed:0.###}×"), new("Repeat", OnOff(p.LoopPlayback)),
            new("Reverse", OnOff(p.ReversePlayback)), new("Recording", OnOff(p.IsRecording)));

        var renderer = p.Renderer;
        var sourceAspect = p.Video.IsOpened ? p.decoder.VideoStream?.GetDAR() : null;
        UpdateDetails(VideoSection, nameof(VideoDetails), p.Video.IsOpened ?
        [
            new("Codec", Value(p.Video.Codec)), new("Size", $"{p.Video.Width} × {p.Video.Height}"),
            new("Aspect ratio", sourceAspect?.ToString() ?? "—"),
            new("Frame rate", $"{p.Video.FPS:0.###} fps"),
            new("Total frames", p.Video.FramesTotal > 0 ? $"{p.Video.FramesTotal:N0}" : "—"),
            new("Pixel format", Value(p.Video.PixelFormat)),
            new("Color format", Value(p.Video.ColorFormat)),
            new("Hardware decoding", p.Video.HWAcceleration ? "Active" : "Inactive")
        ] : [new("", StreamStatus(p.Video.Enabled))]);

        UpdateDetails(AudioSection, nameof(AudioDetails), p.Audio.IsOpened ?
        [
            new("Codec", Value(p.Audio.Codec)), new("Channels", $"{p.Audio.Channels} · {Value(p.Audio.ChannelLayout)}"),
            new("Sample rate", $"{p.Audio.SampleRate:N0} Hz"), new("Bit depth", $"{p.Audio.Bits} bits"),
            new("Sample format", Value(p.Audio.SampleFormat)), new("Output", Value(p.Audio.Device?.Name)),
            new("Volume", $"{p.Audio.Volume}%{(p.Audio.Mute ? " · Muted" : "")}"),
            new("Delay", Delay(p.Audio.Delay))
        ] : [new("", StreamStatus(p.Audio.Enabled))]);

        UpdateDetails(SubtitleSection, nameof(SubtitleDetails), p.Subtitles.IsOpened ?
        [
            new("Codec", Value(p.Subtitles.Codec)), new("Track", $"{p.Subtitles.StreamIndex + 1}"),
            new("Delay", Delay(p.Subtitles.Delay))
        ] : [new("", StreamStatus(p.Subtitles.Enabled))]);

        UpdateDetails(DataSection, nameof(DataDetails),
            new("Status", p.Data.IsOpened ? "Open" : StreamStatus(p.Data.Enabled)),
            new("Available streams", p.Data.Streams.Count.ToString()),
            new("Codec", Value(p.decoder.DataStream?.Codec)));

        UpdateDetails(ProcessingSection, nameof(ProcessingDetails), renderer == null ? [] :
        [
            new("GPU", Value(renderer.GPUAdapter?.Description)),
            new("Preferred processor", renderer.PreferredVideoProcessor.ToString()),
            new("Active processor", p.Video.IsOpened ? renderer.VideoProcessor.ToString() : "—"),
            new("Dynamic range", p.Video.IsOpened ? $"{Value(renderer.DynamicRange)} → {Value(renderer.TargetDynamicRange)}" : "—"),
            new("HDR output", p.Video.IsOpened ? OnOff(renderer.SwapChain?.HDR == true) : "—"),
            new("Aspect ratio", renderer.AspectRatio.ToString()), new("Zoom", $"{renderer.Zoom:0.###}%"),
            new("Pan X", $"{renderer.PanXOffset:0.###}"), new("Pan Y", $"{renderer.PanYOffset:0.###}"),
            new("Rotation", $"{renderer.Rotation}°"), new("360°", OnOff(renderer.Pano360.Enabled)),
            new("Deinterlace", p.Video.IsOpened ? ((DeInterlace)renderer.FieldType) switch
            {
                DeInterlace.TopField => "Top field first",
                DeInterlace.BottomField => "Bottom field first",
                _ => "Off"
            } : "—")
        ]);

        var statsAvailable = p.Stats && Engine.Config.UIRefresh;
        var statsReason = !p.Stats ? "Statistics collection is off." : "Engine UI refresh is off.";
        UpdateDetails(StatisticsSection, nameof(StatisticsDetails), statsAvailable ?
        [
            new("Total bitrate", $"{p.BitRate:0.##} kb/s"),
            .. (p.Video.IsOpened ? new DetailRow[]
            {
                new("Video bitrate", $"{p.Video.BitRate:0.##} kb/s"),
                new("Presented rate", $"{p.Video.FPSCurrent:0.###} fps"),
                new("Video displayed", $"{p.Video.FramesDisplayed:N0}"),
                new("Video dropped", $"{p.Video.FramesDropped:N0}")
            } : []),
            .. (p.Audio.IsOpened ? new DetailRow[]
            {
                new("Audio bitrate", $"{p.Audio.BitRate:0.##} kb/s"),
                new("Audio displayed", $"{p.Audio.FramesDisplayed:N0}"),
                new("Audio dropped", $"{p.Audio.FramesDropped:N0}")
            } : [])
        ] : [new("", statsReason)]);
        var reportedError = string.IsNullOrWhiteSpace(operationError) ? p.LastError : operationError;
        Set(ref errorDetails, string.IsNullOrWhiteSpace(reportedError) ? "No reported error." : reportedError, propertyName: nameof(ErrorDetails));
        var compact = new List<string>(4);
        if (p.Video.IsOpened)
            compact.Add($"Video  {p.Video.Width} × {p.Video.Height} · {p.Video.FPS:0.###} fps");
        if (p.Audio.IsOpened)
            compact.Add($"Audio  {p.Audio.Codec} · {p.Audio.SampleRate:N0} Hz · {p.Audio.Channels} ch");
        if (compact.Count == 0) compact.Add("No audio or video open.");
        else if (statsAvailable)
        {
            compact.Add($"Total bitrate  {p.BitRate:N0} kb/s");
            if (p.Video.IsOpened)
                compact.Add($"{p.Video.FPSCurrent:0.##} fps presented · {p.Video.FramesDropped:N0} video dropped");
            else
                compact.Add($"{p.Audio.FramesDropped:N0} audio dropped");
        }
        else compact.Add(statsReason);
        Set(ref overlayText, string.Join("\n", compact), propertyName: nameof(OverlayText));
    }
    public void Dispose()
    {
        dispatcher.VerifyAccess(); if (disposed) return;
        ShowStatisticsOverlay = false;
        timer.Stop(); StopStatistics(); Observe(false); player = null; disposed = true;
    }
}
