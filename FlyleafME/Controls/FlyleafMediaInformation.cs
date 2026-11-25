using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows.Threading;
using Flyleaf.FFmpeg;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaRenderer;
using DecoderContext = FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace Flyleaf.Controls.WPF;

/// <summary>Shared current-player information for Media Information and the Debug Overlay.</summary>
public sealed class FlyleafMediaInformation : NotifyPropertyChanged, IDisposable
{
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    Player? player;
    Player? observedDetails;
    Renderer? renderer;
    PlaylistItem? selectedItem;
    DispatcherOperation? pendingRefresh;
    Sections pendingSections;
    long position;
    int viewers;
    bool disposed, buffering, showDebugOverlay, enabledViewerStatistics;
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
    internal DetailSection AudioOutputSection { get; } = new("No audio output.");
    internal DetailSection SubtitleSection { get; } = new("No subtitle stream.");
    internal DetailSection DataSection { get; } = new("No data stream.");
    internal DetailSection ProcessingSection { get; } = new("No renderer.");
    internal DetailSection StatisticsSection { get; } = new("Statistics collection is off.");
    internal DetailSection PlaybackStatisticsSection { get; } = new("No playback statistics.");
    internal DetailSection VideoStatisticsSection { get; } = new("No video stream.");
    internal DetailSection AudioStatisticsSection { get; } = new("No audio stream.");
    string errorDetails = "No reported error.";

    [Flags]
    enum Sections
    {
        None = 0,
        Source = 1,
        Playback = 2,
        Position = 4,
        Video = 8,
        Audio = 16,
        Subtitles = 32,
        Data = 64,
        Processing = 128,
        Statistics = 256,
        Diagnostics = 512,
        All = Source | Playback | Position | Video | Audio | Subtitles | Data | Processing | Statistics | Diagnostics
    }

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

        internal bool UpdateValue(string label, string value)
        {
            foreach (var row in Rows)
            {
                if (row.Label != label) continue;
                if (row.Value == value) return false;
                row.Value = value;
                return true;
            }
            return false;
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
    public string AudioOutputDetails => AudioOutputSection.GetText();
    public string SubtitleDetails => SubtitleSection.GetText();
    public string DataDetails => DataSection.GetText();
    public string ProcessingDetails => ProcessingSection.GetText();
    public string StatisticsDetails => StatisticsSection.GetText();
    public string ErrorDetails => errorDetails;
    public bool ShowDebugOverlay
    {
        get => showDebugOverlay;
        set
        {
            dispatcher.VerifyAccess();
            Set(ref showDebugOverlay, value);
        }
    }
    public string Summary => string.Join("\n\n", new[]
    {
        "Source\n" + SourceDetails, "Playback\n" + PlaybackDetails, "Video\n" + VideoDetails,
        "Audio\n" + AudioDetails, "Audio output\n" + AudioOutputDetails, "Subtitles\n" + SubtitleDetails, "Data\n" + DataDetails,
        "Processing\n" + ProcessingDetails, "Statistics\n" + StatisticsDetails, "Errors\n" + ErrorDetails, "Latest snapshot\n" + CaptureDetails,
        "Latest recording\n" + RecordingDetails
    });

    internal FlyleafMediaInformation() { }

    internal void SetPlayer(Player? value)
    {
        dispatcher.VerifyAccess();
        if (disposed || ReferenceEquals(player, value)) return;
        StopStatistics();
        ObserveDetails(null);
        CancelRefresh();
        Observe(false); player = value; buffering = false; operationError = ""; Observe(true);
        position = player?.CurTime ?? 0;
        if (viewers > 0 && player is { IsDisposed: false } target)
        {
            ObserveDetails(target);
            StartStatistics();
        }
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
    void ObserveDetails(Player? target)
    {
        if (ReferenceEquals(observedDetails, target)) return;
        if (observedDetails is { } old)
        {
            old.Video.PropertyChanged -= DetailChanged;
            old.Audio.PropertyChanged -= DetailChanged;
            old.Subtitles.PropertyChanged -= DetailChanged;
            old.Data.PropertyChanged -= DetailChanged;
            old.Data.Streams.CollectionChanged -= DataStreamsChanged;
            old.Playlist.PropertyChanged -= DetailChanged;
            Engine.Config.PropertyChanged -= DetailChanged;
        }
        observedDetails = target;
        ObserveRenderer(target?.Renderer);
        ObserveSelectedItem(target?.Playlist.Selected);
        if (target == null) return;
        target.Video.PropertyChanged += DetailChanged;
        target.Audio.PropertyChanged += DetailChanged;
        target.Subtitles.PropertyChanged += DetailChanged;
        target.Data.PropertyChanged += DetailChanged;
        target.Data.Streams.CollectionChanged += DataStreamsChanged;
        target.Playlist.PropertyChanged += DetailChanged;
        Engine.Config.PropertyChanged += DetailChanged;
    }

    void ObserveRenderer(Renderer? target)
    {
        if (ReferenceEquals(renderer, target)) return;
        if (renderer != null)
        {
            renderer.PropertyChanged -= DetailChanged;
            renderer.Pano360.PropertyChanged -= DetailChanged;
            renderer.ViewportChanged -= ViewportChanged;
        }
        renderer = target;
        if (renderer == null) return;
        renderer.PropertyChanged += DetailChanged;
        renderer.Pano360.PropertyChanged += DetailChanged;
        renderer.ViewportChanged += ViewportChanged;
    }

    void ObserveSelectedItem(PlaylistItem? target)
    {
        if (ReferenceEquals(selectedItem, target)) return;
        if (selectedItem != null) selectedItem.PropertyChanged -= DetailChanged;
        selectedItem = target;
        if (selectedItem != null) selectedItem.PropertyChanged += DetailChanged;
    }

    void OnDispatcher(Action action)
    {
        if (dispatcher.CheckAccess()) action();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(DispatcherPriority.Background, action);
    }

    void QueueRefresh(Sections sections)
    {
        if (sections == Sections.None || viewers == 0 || disposed || dispatcher.HasShutdownStarted) return;
        pendingSections |= sections;
        pendingRefresh ??= dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var refresh = pendingSections;
            pendingSections = Sections.None;
            pendingRefresh = null;
            if (viewers > 0) Refresh(refresh);
        }));
    }

    void CancelRefresh()
    {
        pendingRefresh?.Abort();
        pendingRefresh = null;
        pendingSections = Sections.None;
    }

    void Post(object? sender, Action update, Sections sections)
    {
        if (dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (disposed || !ReferenceEquals(sender, player)) return;
            update();
            QueueRefresh(sections);
        }));
    }
    void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Player target) return;
        var name = e.PropertyName;
        bool lifetimeChanged = name is null or "" or nameof(Player.IsDisposed);
        if (!lifetimeChanged && Volatile.Read(ref viewers) == 0) return;
        var sections = name switch
        {
            null or "" or nameof(Player.IsDisposed) => Sections.All,
            nameof(Player.CurTime) => Sections.Position,
            nameof(Player.Status) or nameof(Player.CanPlay) or nameof(Player.IsLive) or nameof(Player.Duration) or
                nameof(Player.BufferedDuration) or nameof(Player.Speed) or nameof(Player.LoopPlayback) or
                nameof(Player.ReversePlayback) => Sections.Playback,
            nameof(Player.IsRecording) => Sections.Playback | Sections.Diagnostics,
            nameof(Player.LastError) => Sections.Diagnostics,
            nameof(Player.BitRate) or nameof(Player.Stats) => Sections.Statistics,
            nameof(Player.Renderer) => Sections.Video | Sections.Processing,
            _ => Sections.None
        };
        if (sections == Sections.None) return;
        // Capture on notification. Other updates must not sample the live position between notifications.
        long? notifiedPosition = (sections & Sections.Position) != 0 ? target.CurTime : null;
        OnDispatcher(() =>
        {
            if (disposed || !ReferenceEquals(player, target)) return;
            if (notifiedPosition is long value) position = value;
            if (observedDetails != null && (name is null or "" or nameof(Player.Renderer)))
                ObserveRenderer(target.IsDisposed ? null : target.Renderer);
            if (lifetimeChanged && target.IsDisposed)
            {
                ObserveDetails(null);
                Refresh();
                return;
            }
            QueueRefresh(sections);
        });
    }

    void DetailChanged(object? sender, PropertyChangedEventArgs e) => OnDispatcher(() =>
    {
        if (observedDetails is not { IsDisposed: false } target) return;
        var name = e.PropertyName;
        Sections sections;
        if (ReferenceEquals(sender, target.Video))
            sections = name switch
            {
                nameof(Video.BufferedDuration) or nameof(Video.BitRate) or nameof(Video.FPSCurrent) or
                    nameof(Video.FramesDisplayed) or nameof(Video.FramesDropped) => Sections.Statistics,
                nameof(Video.IsOpened) or nameof(Video.Stream) => Sections.Source | Sections.Video | Sections.Processing | Sections.Statistics,
                _ => Sections.Video | Sections.Processing | Sections.Statistics
            };
        else if (ReferenceEquals(sender, target.Audio))
            sections = name switch
            {
                nameof(Audio.BufferedDuration) or nameof(Audio.BitRate) or nameof(Audio.FramesDisplayed) or
                    nameof(Audio.FramesDropped) => Sections.Statistics,
                nameof(Audio.Volume) or nameof(Audio.Mute) or nameof(Audio.Delay) or nameof(Audio.Device) or
                    nameof(Audio.SourceVoiceFormat) or nameof(Audio.MasteringVoiceFormat) => Sections.Audio,
                nameof(Audio.IsOpened) or nameof(Audio.Stream) => Sections.Source | Sections.Audio | Sections.Statistics,
                _ => Sections.Audio | Sections.Statistics
            };
        else if (ReferenceEquals(sender, target.Subtitles))
            sections = name == nameof(Subtitles.SubsText) ? Sections.None : Sections.Subtitles;
        else if (ReferenceEquals(sender, target.Data))
            sections = Sections.Data;
        else if (ReferenceEquals(sender, target.Playlist))
        {
            if (name is null or "" or nameof(Playlist.Selected))
                ObserveSelectedItem(target.Playlist.Selected);
            sections = name is null or "" or nameof(Playlist.Selected) or nameof(Playlist.Title) or nameof(Playlist.Url)
                ? Sections.Source : Sections.None;
        }
        else if (sender != null && ReferenceEquals(sender, selectedItem))
            sections = name is null or "" or nameof(PlaylistItem.Title) ? Sections.Source : Sections.None;
        else if (sender != null && ReferenceEquals(sender, renderer))
            sections = name is null or "" or nameof(Renderer.DynamicRange)
                ? Sections.Video | Sections.Processing : Sections.Processing;
        else if (sender != null && ReferenceEquals(sender, renderer?.Pano360))
            sections = name is null or "" or nameof(Pano360Config.Enabled) ? Sections.Processing : Sections.None;
        else if (ReferenceEquals(sender, Engine.Config))
            sections = name is null or "" or nameof(EngineConfig.UIRefresh) ? Sections.Statistics : Sections.None;
        else
            return;
        QueueRefresh(sections);
    });

    void ViewportChanged(object? sender, EventArgs e) => OnDispatcher(() =>
    {
        if (ReferenceEquals(sender, renderer)) QueueRefresh(Sections.Processing);
    });

    void DataStreamsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnDispatcher(() =>
    {
        if (observedDetails is { IsDisposed: false } target && ReferenceEquals(sender, target.Data.Streams))
            QueueRefresh(Sections.Data);
    });

    void Opening(object? sender, OpeningArgs e) => Post(sender, () =>
    {
        if (!e.IsSubtitles) { buffering = false; operationError = ""; }
    }, Sections.Source | Sections.Playback | Sections.Diagnostics);
    void BufferingStarted(object? sender, EventArgs e) => Post(sender, () => buffering = true, Sections.Playback);
    void BufferingCompleted(object? sender, BufferingCompletedArgs e) => Post(sender, () =>
    {
        buffering = false;
        if (e.Error != null) operationError = e.Error;
    }, Sections.Playback | Sections.Diagnostics);
    void PlaybackStopped(object? sender, PlaybackStoppedArgs e) => Post(sender, () =>
    {
        buffering = false;
        if (e.Error != null) operationError = e.Error;
    }, Sections.Playback | Sections.Diagnostics);
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
        Post(sender, () => operationError = error ?? "", Sections.All);
    }
    internal void ReportOperationError(string error)
    {
        dispatcher.VerifyAccess(); operationError = error;
        QueueRefresh(Sections.Diagnostics);
    }
    internal IDisposable Acquire()
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (++viewers == 1)
        {
            position = player?.CurTime ?? 0;
            ObserveDetails(player is { IsDisposed: false } target ? target : null);
            StartStatistics();
            Refresh();
        }
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
                source.CancelRefresh();
                source.ObserveDetails(null);
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
    static string AudioFormat(AudioVoiceFormat? format)
    {
        if (format == null) return "—";
        var description = $"{format.SampleRate:N0} Hz · {Value(format.ChannelLayout)}";
        return format.BitsPerSample is int bits
            ? $"{description} · {bits}-bit {(format.FloatingPoint == true ? "float" : "PCM")}"
            : description;
    }
    static string DisplaySource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Stream input";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.IsFile) return uri?.IsFile == true ? uri.LocalPath : value;
        // Source information is also copied into diagnostics: omit URL credentials and session query data.
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped);
    }
    internal void Refresh() => Refresh(Sections.All);

    void Refresh(Sections sections)
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        if (player is not { IsDisposed: false } p)
        {
            UpdateDetails(SourceSection, nameof(SourceDetails));
            UpdateDetails(PlaybackSection, nameof(PlaybackDetails));
            UpdateDetails(VideoSection, nameof(VideoDetails));
            UpdateDetails(AudioSection, nameof(AudioDetails));
            UpdateDetails(AudioOutputSection, nameof(AudioOutputDetails));
            UpdateDetails(SubtitleSection, nameof(SubtitleDetails));
            UpdateDetails(DataSection, nameof(DataDetails));
            UpdateDetails(ProcessingSection, nameof(ProcessingDetails));
            UpdateDetails(StatisticsSection, nameof(StatisticsDetails));
            PlaybackStatisticsSection.Update([]);
            VideoStatisticsSection.Update([]);
            AudioStatisticsSection.Update([]);
            Set(ref recordingDetails, "No recording saved.", propertyName: nameof(RecordingDetails));
            Set(ref errorDetails, "No reported error.", propertyName: nameof(ErrorDetails));
            return;
        }

        if ((sections & Sections.Source) != 0)
        {
            var title = p.Playlist.Selected?.Title ?? p.Playlist.Title;
            var source = p.Playlist.Url == null && p.Playlist.IOStream == null ? "No input" : DisplaySource(p.Playlist.Url);
            UpdateDetails(SourceSection, nameof(SourceDetails),
                new("Title", Value(title)), new("Location", source),
                new("Container", Value(p.MainDemuxer?.LongName)), new("Opener", Value(p.decoder.OpenedPlugin?.Name)),
                new("Player", $"{p.PlayerId} · {p.Usage}"));
        }
        if ((sections & Sections.Playback) != 0)
            UpdateDetails(PlaybackSection, nameof(PlaybackDetails),
                new("Status", $"{p.Status}{(buffering ? " · Buffering" : "")}"),
                new("Position", FormatPosition(p)),
                new("Buffered duration", p.CanPlay ? Time(p.BufferedDuration) : "—"),
                new("Speed", $"{p.Speed:0.###}×"), new("Repeat", OnOff(p.LoopPlayback)),
                new("Reverse", OnOff(p.ReversePlayback)), new("Recording", OnOff(p.IsRecording)));
        else if ((sections & Sections.Position) != 0 && PlaybackSection.UpdateValue("Position", FormatPosition(p)))
            Raise(nameof(PlaybackDetails));

        if ((sections & Sections.Video) != 0) RefreshVideo(p);
        if ((sections & Sections.Audio) != 0) RefreshAudio(p);
        if ((sections & Sections.Subtitles) != 0)
        {
            var subtitles = p.Subtitles.IsOpened ? p.decoder.SubtitlesStream : null;
            UpdateDetails(SubtitleSection, nameof(SubtitleDetails), p.Subtitles.IsOpened ?
            [
                new("Codec", Value(p.Subtitles.Codec)), new("Track", $"#{p.Subtitles.StreamIndex} · {Value(subtitles?.Language?.ToString())}"),
                new("Delay", Delay(p.Subtitles.Delay))
            ] : [new("", StreamStatus(p.Subtitles.Enabled))]);
        }
        if ((sections & Sections.Data) != 0)
            UpdateDetails(DataSection, nameof(DataDetails),
                new("Status", p.Data.IsOpened ? "Open" : StreamStatus(p.Data.Enabled)),
                new("Available streams", p.Data.Streams.Count.ToString()),
                new("Codec", Value(p.decoder.DataStream?.Codec)));
        if ((sections & Sections.Processing) != 0) RefreshProcessing(p);

        if ((sections & Sections.Statistics) != 0) RefreshStatistics(p);

        if ((sections & Sections.Diagnostics) != 0)
        {
            Set(ref recordingDetails, p.LastRecordingFilename ?? "No recording saved.", propertyName: nameof(RecordingDetails));
            var reportedError = string.IsNullOrWhiteSpace(operationError) ? p.LastError : operationError;
            Set(ref errorDetails, string.IsNullOrWhiteSpace(reportedError) ? "No reported error." : reportedError, propertyName: nameof(ErrorDetails));
        }
    }

    string FormatPosition(Player p) => $"{Time(position)} / {Time(p.Duration)}{(p.CanPlay && p.IsLive ? " · Live" : "")}";

    void RefreshVideo(Player p)
    {
        var video = p.Video.Stream;
        var transfer = video?.ColorTransfer switch
        {
            AVColorTransferCharacteristic.SMPTE2084 => "PQ (ST 2084)",
            AVColorTransferCharacteristic.ARIBStdB67 => "HLG",
            AVColorTransferCharacteristic.BT709 => "BT.709",
            null or AVColorTransferCharacteristic.Unspecified => "—",
            _ => video?.ColorTransfer.ToString() ?? "—"
        };
        UpdateDetails(VideoSection, nameof(VideoDetails), video != null ?
        [
            new("Track", $"#{video.StreamIndex}"), new("Codec", Value(video.Codec)),
            new("Resolution", $"{video.Width} × {video.Height}"),
            new("Aspect ratio", video.GetDAR().ToString()),
            new("Frame rate", $"{p.Video.FPS:0.###} fps"),
            new("Total frames", p.Video.FramesTotal > 0 ? $"{p.Video.FramesTotal:N0}" : "—"),
            new("Pixel format", Value(p.Video.PixelFormat)),
            new("Bit depth", video.PixelComp0Depth > 0 ? $"{video.PixelComp0Depth} bits" : "—"),
            new("Color space", video.ColorSpace == ColorSpace.None ? "—" : video.ColorSpace.ToString()),
            new("Transfer", transfer),
            new("Range", video.ColorRange == ColorRange.None ? "—" : video.ColorRange.ToString()),
            new("HDR format", video.DoviRpuPresent ? Value(p.Renderer?.DynamicRange) :
                video.HDRFormat == HDRFormat.None ? "SDR" : video.HDRFormat.ToString())
        ] : [new("", StreamStatus(p.Video.Enabled))]);
    }

    void RefreshAudio(Player p)
    {
        var audio = p.Audio.Stream;
        UpdateDetails(AudioSection, nameof(AudioDetails), p.Audio.IsOpened ?
        [
            new("Track", $"#{p.Audio.StreamIndex} · {Value(audio?.Language?.ToString())}"),
            new("Source", p.AudioDecoder.OnVideoDemuxer ? "Embedded" : "Separate audio input"),
            new("Codec", Value(p.Audio.Codec)), new("Channels", $"{p.Audio.Channels} · {Value(p.Audio.ChannelLayout)}"),
            new("Sample rate", $"{p.Audio.SampleRate:N0} Hz"), new("Bit depth", $"{p.Audio.Bits} bits"),
            new("Sample format", Value(p.Audio.SampleFormat))
        ] : [new("", StreamStatus(p.Audio.Enabled))]);

        UpdateDetails(AudioOutputSection, nameof(AudioOutputDetails), p.Audio.IsOpened ?
        [
            new("Device", Value(p.Audio.Device?.Name)),
            new("XAudio2 source voice", AudioFormat(p.Audio.SourceVoiceFormat)),
            new("XAudio2 mastering voice", AudioFormat(p.Audio.MasteringVoiceFormat)),
            new("Volume", $"{p.Audio.Volume}%{(p.Audio.Mute ? " · Muted" : "")}"),
            new("Delay", Delay(p.Audio.Delay))
        ] : []);
    }

    void RefreshProcessing(Player p)
    {
        var renderer = p.Renderer;
        var output = renderer?.SwapChainOutput;
        UpdateDetails(ProcessingSection, nameof(ProcessingDetails), renderer == null || !p.Video.IsOpened ? [] :
        [
            new("Decoder", p.Video.HWAcceleration ? $"Hardware · {Value(p.Video.Codec)}" : $"Software · {Value(p.Video.Codec)}"),
            new("GPU", Value(renderer.GPUAdapter?.Description)),
            new("Preferred processor", renderer.PreferredVideoProcessor.ToString()),
            new("Active processor", p.Video.IsOpened ? renderer.VideoProcessor.ToString() : "—"),
            new("Dynamic range", p.Video.IsOpened ? $"{Value(renderer.DynamicRange)} → {Value(renderer.TargetDynamicRange)}" : "—"),
            new("Video viewport", renderer.Viewport.Width > 0 && renderer.Viewport.Height > 0
                ? $"{renderer.Viewport.Width:0} × {renderer.Viewport.Height:0}" : "—"),
            new("Swap chain format", Value(output?.Format.ToString())),
            new("Output color space", Value(output?.ColorSpace?.ToString())),
            new("HDR output", output != null ? OnOff(output.HDR) : "—"),
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
    }

    void RefreshStatistics(Player p)
    {
        string? statsReason = null;
        if (!p.Stats) statsReason = "Statistics collection is off.";
        else if (!Engine.Config.UIRefresh) statsReason = "Engine UI refresh is off.";
        bool statsAvailable = statsReason == null;
        bool videoOpen = p.Video.Stream != null;
        bool audioOpen = p.Audio.Stream != null;
        DetailRow[] playbackStatistics = statsReason == null ? [new("Total bitrate", $"{p.BitRate:0.##} kb/s")] : [new("", statsReason)];
        DetailRow[] videoStatistics = videoOpen && statsAvailable ?
        [
            new("Video bitrate", $"{p.Video.BitRate:0.##} kb/s"),
            new("Presented rate", $"{p.Video.FPSCurrent:0.###} fps"),
            new("Video displayed", $"{p.Video.FramesDisplayed:N0}"),
            new("Video dropped", $"{p.Video.FramesDropped:N0}")
        ] : [];
        DetailRow[] audioStatistics = audioOpen && statsAvailable ?
        [
            new("Audio bitrate", $"{p.Audio.BitRate:0.##} kb/s"),
            new("Audio submitted", $"{p.Audio.FramesDisplayed:N0}"),
            new("Audio dropped", $"{p.Audio.FramesDropped:N0}")
        ] : [];
        DetailRow[] unavailableStatistics = statsReason == null ? [] : [new("", statsReason)];
        UpdateDetails(StatisticsSection, nameof(StatisticsDetails), [..playbackStatistics, ..videoStatistics, ..audioStatistics]);
        PlaybackStatisticsSection.Update(playbackStatistics);
        VideoStatisticsSection.Update(videoOpen ?
        [
            new("Buffered duration", Time(p.Video.BufferedDuration)),
            .. videoStatistics, .. unavailableStatistics
        ] : []);
        AudioStatisticsSection.Update(audioOpen ?
        [
            new("Buffered duration", Time(p.Audio.BufferedDuration)),
            .. audioStatistics, .. unavailableStatistics
        ] : []);
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess(); if (disposed) return;
        ShowDebugOverlay = false;
        CancelRefresh(); StopStatistics(); ObserveDetails(null); Observe(false); player = null; disposed = true;
    }
}
