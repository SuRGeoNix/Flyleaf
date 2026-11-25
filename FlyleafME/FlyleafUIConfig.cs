using System.ComponentModel;

namespace Flyleaf.Controls.WPF;

public enum FlyleafVisibilityPolicy
{
    Auto,
    Always,
    Never
}

public class FlyleafAppearanceOptions : NotifyPropertyChanged
{
    internal const FlyleafAccent DefaultAccent = FlyleafAccent.Firebrick;
    internal const string DefaultCustomAccentColor = "#4F8CC9";

    FlyleafTheme theme = FlyleafTheme.Dark;
    FlyleafAccent accent = DefaultAccent;
    string customAccentColor = DefaultCustomAccentColor;

    public FlyleafTheme Theme { get => theme; set => Set(ref theme, value); }
    public FlyleafAccent Accent { get => accent; set => Set(ref accent, value); }
    public string CustomAccentColor
    {
        get => customAccentColor;
        set => Set(ref customAccentColor, "#" + FlyleafTextValues.Color(value)[^6..]);
    }

    public FlyleafAppearanceOptions Clone() => new() { Theme = Theme, Accent = Accent, CustomAccentColor = CustomAccentColor };
}

public enum FlyleafPlaybackBarDesign
{
    Classic,
    Modern
}

public enum FlyleafPlaybackBarMode
{
    Compact,
    Full,
    Dynamic
}

public class FlyleafPlaybackBarOptions : NotifyPropertyChanged
{
    FlyleafVisibilityPolicy visibility;
    FlyleafPlaybackBarDesign design = FlyleafPlaybackBarDesign.Modern;
    FlyleafPlaybackBarMode mode = FlyleafPlaybackBarMode.Dynamic;
    bool floating = true;
    bool classicFloating = true;
    bool dynamicPinned = true;
    bool seekWhileDragging = true;
    int autoHideDelayMilliseconds = 2800;

    public FlyleafVisibilityPolicy Visibility { get => visibility; set => Set(ref visibility, value); }
    public FlyleafPlaybackBarDesign Design { get => design; set => Set(ref design, value); }
    public FlyleafPlaybackBarMode Mode { get => mode; set => Set(ref mode, value); }

    /// <summary>Inset the Modern playback bar and round its corners.</summary>
    public bool Floating { get => floating; set => Set(ref floating, value); }

    /// <summary>Inset the Classic playback bar and round its corners.</summary>
    public bool ClassicFloating { get => classicFloating; set => Set(ref classicFloating, value); }

    /// <summary>Keep the controls row expanded in Dynamic mode.</summary>
    public bool DynamicPinned { get => dynamicPinned; set => Set(ref dynamicPinned, value); }

    /// <summary>Seek continuously while dragging; false commits only on release.</summary>
    public bool SeekWhileDragging { get => seekWhileDragging; set => Set(ref seekWhileDragging, value); }

    /// <summary>Shared mouse/keyboard inactivity timeout. Zero keeps the controls active.</summary>
    public int AutoHideDelayMilliseconds
    {
        get => autoHideDelayMilliseconds;
        set
        {
            if (value < 0 || value > 600000) throw new ArgumentOutOfRangeException(nameof(value), "Use a delay from 0 to 600 seconds.");
            Set(ref autoHideDelayMilliseconds, value);
        }
    }

    public FlyleafPlaybackBarOptions Clone() => new() { Visibility = Visibility, Design = Design, Mode = Mode, Floating = Floating, ClassicFloating = ClassicFloating, DynamicPinned = DynamicPinned, SeekWhileDragging = SeekWhileDragging, AutoHideDelayMilliseconds = AutoHideDelayMilliseconds };
}

public class FlyleafQueueOptions : NotifyPropertyChanged
{
    int slideShowIntervalMilliseconds = 3000;
    /// <summary>Minimum display time for zero-duration, zero-clock images.</summary>
    public int SlideShowIntervalMilliseconds
    {
        get => slideShowIntervalMilliseconds;
        set
        {
            if (value < 100 || value > 3600000) throw new ArgumentOutOfRangeException(nameof(value), "Use an interval from 0.1 to 3600 seconds.");
            Set(ref slideShowIntervalMilliseconds, value);
        }
    }
    public FlyleafQueueOptions Clone() => new() { SlideShowIntervalMilliseconds = SlideShowIntervalMilliseconds };
}
/// <summary>UI preferences only; the application owns persistence and sharing.</summary>
public class FlyleafMouseOptions : NotifyPropertyChanged
{
    AvailableWindows bindings = AvailableWindows.Both, pan = AvailableWindows.Surface,
        zoom = AvailableWindows.Surface, rotate = AvailableWindows.Surface, fullscreen = AvailableWindows.Both;
    public AvailableWindows Bindings { get => bindings; set => Set(ref bindings, value); }
    public AvailableWindows PanOnCtrlDrag { get => pan; set => Set(ref pan, value); }
    public AvailableWindows ZoomOnCtrlWheel { get => zoom; set => Set(ref zoom, value); }
    public AvailableWindows RotateOnShiftWheel { get => rotate; set => Set(ref rotate, value); }
    public AvailableWindows FullscreenOnDoubleClick { get => fullscreen; set => Set(ref fullscreen, value); }
    public FlyleafMouseOptions Clone() => new()
    {
        Bindings = Bindings, PanOnCtrlDrag = PanOnCtrlDrag, ZoomOnCtrlWheel = ZoomOnCtrlWheel,
        RotateOnShiftWheel = RotateOnShiftWheel, FullscreenOnDoubleClick = FullscreenOnDoubleClick
    };
}

/// <summary>UI preferences only; the application owns persistence and sharing.</summary>
public class FlyleafUIConfig : NotifyPropertyChanged
{
    FlyleafAppearanceOptions appearance = new();
    FlyleafPlaybackBarOptions playbackBar = new();
    FlyleafMouseOptions mouse = new();
    FlyleafQueueOptions queue = new();
    FlyleafSubtitleOptions subtitles = new();
    FlyleafOSDOptions osd = new();
    string? selectedPlugin;

    public string? SelectedPlugin { get => selectedPlugin; set => Set(ref selectedPlugin, value); }

    public FlyleafUIConfig()
    {
        PropertyChangedEventManager.AddHandler(appearance, AppearanceChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(playbackBar, PlaybackBarChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(mouse, MouseChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(queue, QueueChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(subtitles, SubtitlesChanged, string.Empty);
        PropertyChangedEventManager.AddHandler(osd, OSDChanged, string.Empty);
    }

    public FlyleafAppearanceOptions Appearance
    {
        get => appearance;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(appearance, value)) return;
            PropertyChangedEventManager.RemoveHandler(appearance, AppearanceChanged, string.Empty);
            appearance = value;
            PropertyChangedEventManager.AddHandler(appearance, AppearanceChanged, string.Empty);
            Raise();
        }
    }

    public FlyleafPlaybackBarOptions PlaybackBar
    {
        get => playbackBar;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(playbackBar, value)) return;
            PropertyChangedEventManager.RemoveHandler(playbackBar, PlaybackBarChanged, string.Empty);
            playbackBar = value;
            PropertyChangedEventManager.AddHandler(playbackBar, PlaybackBarChanged, string.Empty);
            Raise();
        }
    }

    public FlyleafQueueOptions Queue
    {
        get => queue;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(queue, value)) return;
            PropertyChangedEventManager.RemoveHandler(queue, QueueChanged, string.Empty);
            queue = value;
            PropertyChangedEventManager.AddHandler(queue, QueueChanged, string.Empty); Raise();
        }
    }
    void QueueChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(Queue));
    void AppearanceChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(Appearance));
    void PlaybackBarChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(PlaybackBar));
    void MouseChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(Mouse));
    void SubtitlesChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(Subtitles));
    void OSDChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(OSD));

    public FlyleafSubtitleOptions Subtitles
    {
        get => subtitles;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(subtitles, value)) return;
            PropertyChangedEventManager.RemoveHandler(subtitles, SubtitlesChanged, string.Empty);
            subtitles = value;
            PropertyChangedEventManager.AddHandler(subtitles, SubtitlesChanged, string.Empty); Raise();
        }
    }
    public FlyleafOSDOptions OSD
    {
        get => osd;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(osd, value)) return;
            PropertyChangedEventManager.RemoveHandler(osd, OSDChanged, string.Empty);
            osd = value;
            PropertyChangedEventManager.AddHandler(osd, OSDChanged, string.Empty); Raise();
        }
    }

    public FlyleafMouseOptions Mouse
    {
        get => mouse;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(mouse, value)) return;
            PropertyChangedEventManager.RemoveHandler(mouse, MouseChanged, string.Empty);
            mouse = value;
            PropertyChangedEventManager.AddHandler(mouse, MouseChanged, string.Empty);
            Raise();
        }
    }

    /// <summary>Copies preferences into independent objects without copying event subscribers.</summary>
    public FlyleafUIConfig Clone() => new() { Appearance = Appearance.Clone(), PlaybackBar = PlaybackBar.Clone(), Mouse = Mouse.Clone(), Queue = Queue.Clone(), Subtitles = Subtitles.Clone(), OSD = OSD.Clone(), SelectedPlugin = SelectedPlugin };
}
