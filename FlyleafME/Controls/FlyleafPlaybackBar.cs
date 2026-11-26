using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaStream;

namespace Flyleaf.Controls.WPF;

public class FlyleafPlaybackBar : Control, INotifyPropertyChanged
{
    /// <summary>Connect a composed media element to supply the player, queue, options and default panel actions.</summary>
    public static readonly DependencyProperty MediaElementProperty = DependencyProperty.Register(
        nameof(MediaElement), typeof(FlyleafME), typeof(FlyleafPlaybackBar), new PropertyMetadata(null, MediaElementChanged));
    public FlyleafME? MediaElement { get => (FlyleafME?)GetValue(MediaElementProperty); set => SetValue(MediaElementProperty, value); }

    readonly Dictionary<DependencyProperty, BindingExpressionBase> connectedBindings = [];

    static void MediaElementChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var bar = (FlyleafPlaybackBar)sender;
        if (e.OldValue is FlyleafME previousOwner) previousOwner.PanelRegistryChanged -= bar.PanelRegistryChanged;
        var owner = e.NewValue as FlyleafME;
        if (owner != null && bar.IsLoaded) owner.PanelRegistryChanged += bar.PanelRegistryChanged;
        void Connect(DependencyProperty property, string path)
        {
            if (bar.connectedBindings.Remove(property, out var previous) && ReferenceEquals(bar.GetBindingExpression(property), previous))
                BindingOperations.ClearBinding(bar, property);
            if (owner != null && bar.ReadLocalValue(property) == DependencyProperty.UnsetValue)
                bar.connectedBindings.Add(property, bar.SetBinding(property, new Binding(path) { Source = owner, Mode = BindingMode.OneWay }));
        }
        Connect(PlayerProperty, nameof(FlyleafME.Player));
        Connect(QueueSourceProperty, nameof(FlyleafME.Queue));
        Connect(InformationSourceProperty, nameof(FlyleafME.Information));
        Connect(OptionsProperty, "UIConfig.PlaybackBar");
        bar.Notify(nameof(CanOpenSettings));
    }

    public static readonly DependencyProperty QueueSourceProperty = DependencyProperty.Register(
        nameof(QueueSource), typeof(FlyleafQueue), typeof(FlyleafPlaybackBar), new PropertyMetadata(null, (d, e) =>
        {
            var bar = (FlyleafPlaybackBar)d;
            if (e.OldValue is FlyleafQueue oldQueue) oldQueue.PropertyChanged -= bar.QueueChanged;
            if (bar.IsLoaded && e.NewValue is FlyleafQueue queue) queue.PropertyChanged += bar.QueueChanged;
            bar.NotifyAll();
        }));
    public FlyleafQueue? QueueSource { get => (FlyleafQueue?)GetValue(QueueSourceProperty); set => SetValue(QueueSourceProperty, value); }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    void QueueChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FlyleafQueue.CanPrevious): Notify(nameof(HasPrevious)); break;
            case nameof(FlyleafQueue.CanNext): Notify(nameof(HasNext)); break;
            case nameof(FlyleafQueue.TotalItems):
                Notify(nameof(HasPlaylist)); Notify(nameof(CanOpenQueue)); Notify(nameof(ShowAutoNext)); Notify(nameof(ShowItemNavigation));
                UpdateResponsiveLayout();
                break;
            case null or "": NotifyAll(); break;
        }
    }
    void AttachQueue()
    {
        if (QueueSource == null) return;
        QueueSource.PropertyChanged -= QueueChanged; QueueSource.PropertyChanged += QueueChanged; NotifyAll();
    }
    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(nameof(Player), typeof(Player), typeof(FlyleafPlaybackBar),
            new PropertyMetadata(null, OnPlayerChanged));

    public Player? Player
    {
        get => (Player?)GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public static readonly DependencyProperty InformationSourceProperty = DependencyProperty.Register(
        nameof(InformationSource), typeof(FlyleafMediaInformation), typeof(FlyleafPlaybackBar));
    public FlyleafMediaInformation? InformationSource
    {
        get => (FlyleafMediaInformation?)GetValue(InformationSourceProperty);
        set => SetValue(InformationSourceProperty, value);
    }

    public static readonly DependencyProperty OptionsProperty =
        DependencyProperty.Register(nameof(Options), typeof(FlyleafPlaybackBarOptions), typeof(FlyleafPlaybackBar),
            new PropertyMetadata(null, null, (_, value) => value ?? new FlyleafPlaybackBarOptions()));

    public FlyleafPlaybackBarOptions Options
    {
        get => (FlyleafPlaybackBarOptions)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public static readonly DependencyProperty IsDynamicModeProperty = DependencyProperty.Register(
        nameof(IsDynamicMode), typeof(bool), typeof(FlyleafPlaybackBar), new PropertyMetadata(false, (d, e) =>
        {
            var bar = (FlyleafPlaybackBar)d;
            bar.ResetDynamic();
            bar.ClosePlaybackPopups();
            bar.ApplyDynamicPinPreference();
        }));
    public bool IsDynamicMode { get => (bool)GetValue(IsDynamicModeProperty); set => SetValue(IsDynamicModeProperty, value); }

    public static readonly DependencyProperty IsModernFullLayoutProperty = DependencyProperty.Register(
        nameof(IsModernFullLayout), typeof(bool), typeof(FlyleafPlaybackBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) =>
        {
            var bar = (FlyleafPlaybackBar)d;
            if (!bar.IsDynamicMode) bar.ClosePlaybackPopups();
        }));
    public bool IsModernFullLayout { get => (bool)GetValue(IsModernFullLayoutProperty); set => SetValue(IsModernFullLayoutProperty, value); }

    static readonly DependencyProperty DynamicPinPreferenceProperty = DependencyProperty.Register(
        "DynamicPinPreference", typeof(bool), typeof(FlyleafPlaybackBar), new PropertyMetadata(false, (d, _) =>
            ((FlyleafPlaybackBar)d).ApplyDynamicPinPreference()));

    static readonly DependencyPropertyKey DynamicPinnedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DynamicPinned), typeof(bool), typeof(FlyleafPlaybackBar), new PropertyMetadata(false));
    public static readonly DependencyProperty DynamicPinnedProperty = DynamicPinnedPropertyKey.DependencyProperty;
    public bool DynamicPinned => (bool)GetValue(DynamicPinnedProperty);
    public bool DynamicExpanded => dynamicExpanded;

    static readonly DependencyPropertyKey IsNonClientPointerOverDynamicHandleBandPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsNonClientPointerOverDynamicHandleBand), typeof(bool), typeof(FlyleafPlaybackBar), new PropertyMetadata(false));
    public static readonly DependencyProperty IsNonClientPointerOverDynamicHandleBandProperty = IsNonClientPointerOverDynamicHandleBandPropertyKey.DependencyProperty;
    public bool IsNonClientPointerOverDynamicHandleBand => (bool)GetValue(IsNonClientPointerOverDynamicHandleBandProperty);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsRequested
    {
        add { settingsRequested += value; Notify(nameof(CanOpenSettings)); }
        remove { settingsRequested -= value; Notify(nameof(CanOpenSettings)); }
    }
    EventHandler? settingsRequested;
    public event EventHandler? GeneralMenuRequested;
    public event EventHandler<FlyleafPanelRequestedEventArgs>? PanelRequested;

    ButtonBase? videoButton, audioButton, subtitlesButton, playlistButton, speedButton, moreButton, settingsButton, volumeButton;
    Popup? volumePopup;
    IDisposable? volumePopupActivity;
    bool compact, volumeCompact, arranging, modernLayout;
    ButtonBase? dynamicHandle, modernStop, modernAutoNext;
    bool dynamicExpanded;
    TranslateTransform? dynamicHandleOffset;
    double? dynamicHandleLeft;
    DispatcherOperation? dynamicHandleReset;
    IDisposable? dynamicDragActivity;
    readonly DispatcherTimer dynamicCollapse = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly HashSet<ContextMenu> openPlaybackMenus = [];
    const double DynamicHandleEdgeInset = 8;
    const double CompactTimelineTargetWidth = 220;
    const double ModernFullLayoutHeadroom = 24;
    (double Width, bool Navigation, bool AutoNext, bool Live, bool Duration, bool Full, bool TimelineOnly, int CurrentTimeLength, int DurationLength,
        string? Video, string? Audio, string? Subtitles)? lastModernLayout;
    FrameworkElement? controlsRow, transportGroup, sourceGroup, utilityGroup, horizontalVolume;
    FrameworkElement? videoDetail, audioDetail, subtitleDetail, compactTimeline, compactCurrentTime, compactDuration;
    Border? surface;
    FrameworkElement? modernFrame, dynamicHandleBand;

    public string CurrentTimeText => Player?.CanPlay == true ? FormatTime(Player.CurTime) : "--:--:--";
    public string DurationText => Player?.Duration > 0 ? FormatTime(Player.Duration) : "--:--:--";
    public bool ShowDuration => !IsLive || Player?.Duration != 0;
    public bool IsLive => Player is { CanPlay: true, IsLive: true, Status: not Status.Stopped } p && !FlyleafQueueProgression.IsImageFormat(p.MainDemuxer?.Name);
    public bool IsPlaying => Player?.IsPlaying == true;
    public bool CanPlay => Player?.CanPlay == true;
    public bool CanSeek => CanPlay && Player?.Duration > 0;
    public bool CanStop => CanPlay && Player?.Status != Status.Stopped;
    public bool HasVideo => Player?.Video.IsOpened == true;
    public bool HasAudio => Player?.Audio.IsOpened == true;
    public bool CanAdjustVolume => Player is { IsDisposed: false };
    public bool HasSubtitles => Player?.Subtitles.Streams?.Count > 0 || Player?.Subtitles.IsOpened == true;
    public bool HasPlaylist => QueueSource != null ? QueueSource.Sources.Count > 0 : Player?.Playlist.Items?.Count > 1;
    public bool CanOpenQueue => QueueSource != null || HasPlaylist;
    public bool ShowAutoNext => QueueSource?.TotalItems >= 2;
    public bool ShowItemNavigation => QueueSource == null ? HasPlaylist : ShowAutoNext;
    public bool HasPrevious => QueueSource?.CanPrevious ?? (HasPlaylist && Player?.Playlist.PrevItem != null);
    public bool HasNext => QueueSource?.CanNext ?? (HasPlaylist && Player?.Playlist.NextItem != null);
    public bool IsCompact => compact;
    public bool IsVolumeCompact => volumeCompact;
    public string VolumeToolTip => volumeCompact ? "Volume" : "Mute / Unmute";
    public bool CanOpenSettings => settingsRequested != null || MediaElement?.IsPanelRegistered("settings") == true;
    public string SpeedToolTip => $"Playback speed · {Player?.Speed ?? 1:0.###}×";
    public double SpeedNeedleAngle => (Player?.Speed ?? 1) switch { < 1 => -45, > 1 => 45, _ => 0 };

    public string VideoLabel
    {
        get
        {
            if (!HasVideo || Player == null)
                return "";
            var stream = Player.Video.Streams?.FirstOrDefault(s => s.StreamIndex == Player.Video.StreamIndex);
            int height = stream is { Height: > 0 } ? (int)stream.Height : Player.Video.Height;
            int width = stream is { Width: > 0 } ? (int)stream.Width : Player.Video.Width;
            string resolution = height >= 4320 || (width >= 7000 && height >= 3000) ? "8K" :
                height >= 2160 ? "4K" : height >= 1440 ? "1440p" : height >= 1080 ? "1080p" :
                height >= 720 ? "720p" : height > 0 ? $"{height}p" : "Video";
            string codec = Player.Video.Codec?.ToUpperInvariant() ?? "";
            string hdr = stream is { DoviProfile: > 0 }
                ? $"DoVi {stream.DoviProfile}.{stream.DoviCompatibility}"
                : stream?.HDRFormat.ToString() ?? "";
            if (hdr is "" or "None" or "Unknown" or "SDR")
                hdr = "";
            return string.Join(" · ", new[] { resolution, codec, hdr }.Where(s => !string.IsNullOrEmpty(s)));
        }
    }

    public string AudioLabel
    {
        get
        {
            if (!HasAudio || Player == null)
                return "";
            var stream = Player.Audio.Streams?.FirstOrDefault(s => s.StreamIndex == Player.Audio.StreamIndex);
            string lang = LanguageCode(stream?.Language?.OriginalInput);
            string layout = Player.Audio.ChannelLayout ?? "";
            if (string.IsNullOrWhiteSpace(layout) || layout.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                layout = stream?.ChannelLayoutStr ?? "";
            if (string.IsNullOrWhiteSpace(layout))
            {
                int channels = stream is { Channels: > 0 } ? stream.Channels : Player.Audio.Channels;
                layout = channels switch { 1 => "Mono", 2 => "Stereo", > 0 => $"{channels} ch", _ => "" };
            }
            if (layout.Equals("stereo", StringComparison.OrdinalIgnoreCase))
                layout = "Stereo";
            if (layout.Equals("mono", StringComparison.OrdinalIgnoreCase))
                layout = "Mono";
            string label = string.Join(" · ", new[] { lang, layout }.Where(s => !string.IsNullOrEmpty(s)));
            return string.IsNullOrEmpty(label) ? "Audio" : label;
        }
    }

    public string VolumeIcon
    {
        get
        {
            if (Player is not { IsDisposed: false } player)
                return "\uE992";
            if (player.Audio.Mute)
                return "\uE74F";
            double volume = (double)player.Audio.Volume / Math.Max(1, player.Config.Audio.VolumeMax);
            return volume <= 0 ? "\uE992" : volume <= 1.0 / 3 ? "\uE993" :
                volume <= 2.0 / 3 ? "\uE994" : "\uE995";
        }
    }

    public string SubtitlesLabel
    {
        get
        {
            if (Player?.Subtitles.IsOpened != true)
                return "Off";
            var stream = Player.Subtitles.Streams?.FirstOrDefault(s => s.StreamIndex == Player.Subtitles.StreamIndex);
            string lang = LanguageCode(stream?.Language?.OriginalInput);
            return string.IsNullOrEmpty(lang) ? "On" : lang;
        }
    }

    static string LanguageCode(string? code)
        => code is { Length: >= 2 and <= 3 } && code.All(char.IsLetter) ? code.ToUpperInvariant() : "";

    public static string FormatTime(long ticks)
    {
        if (ticks < 0)
            return "--:--:--";
        long seconds = ticks / TimeSpan.TicksPerSecond;
        long hours = seconds / 3600;
        return $"{hours:00}:{(seconds / 60) % 60:00}:{seconds % 60:00}";
    }

    static FlyleafPlaybackBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlyleafPlaybackBar),
            new FrameworkPropertyMetadata(typeof(FlyleafPlaybackBar)));
    }

    public FlyleafPlaybackBar()
    {
        PreviousCommand = new FlyleafLib.Controls.WPF.RelayCommandSimple(() =>
        {
            if (QueueSource != null) QueueSource.Previous(); else if (Player?.Playlist.PrevItem is { } item) Player.OpenAsync(item);
        });
        NextCommand = new FlyleafLib.Controls.WPF.RelayCommandSimple(() =>
        {
            if (QueueSource != null) QueueSource.Next(); else if (Player?.Playlist.NextItem is { } item) Player.OpenAsync(item);
        });
        Loaded += (_, _) =>
        {
            ApplyDynamicPinPreference();
            AttachQueue();
            if (MediaElement is { } owner)
            {
                owner.PanelRegistryChanged -= PanelRegistryChanged;
                owner.PanelRegistryChanged += PanelRegistryChanged;
            }
            Notify(nameof(CanOpenSettings));
        };
        Unloaded += (_, _) =>
        {
            if (QueueSource != null) QueueSource.PropertyChanged -= QueueChanged;
            if (MediaElement is { } owner) owner.PanelRegistryChanged -= PanelRegistryChanged;
        };
        dynamicCollapse.Tick += DynamicCollapse_Tick;
        MouseEnter += (_, _) => dynamicCollapse.Stop();
        MouseLeave += (_, _) =>
        {
            QueueDynamicHandleReset();
            ScheduleDynamicCollapse();
        };
        Unloaded += (_, _) =>
        {
            ResetDynamic();
            ClosePlaybackPopups();
        };
        IsVisibleChanged += DynamicAvailabilityChanged;
        IsHitTestVisibleChanged += DynamicAvailabilityChanged;
        IsMouseCaptureWithinChanged += DynamicCaptureChanged;
        CoerceValue(OptionsProperty);
        SetBinding(DynamicPinPreferenceProperty, new Binding(nameof(Options) + "." + nameof(FlyleafPlaybackBarOptions.DynamicPinned)) { Source = this });
        SizeChanged += (_, _) => { if (!modernLayout) UpdateResponsiveLayout(); };
        Unloaded += (_, _) => Detach(Player);
        Loaded += (_, _) => Attach(Player);
    }

    void PanelRegistryChanged(object? sender, EventArgs e) => Notify(nameof(CanOpenSettings));

    static void OnPlayerChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        var bar = (FlyleafPlaybackBar)obj;
        bar.ClosePlaybackPopups();
        if (!bar.DynamicPinned) bar.SetDynamicExpanded(false);
        bar.Detach(e.OldValue as Player);
        bar.Attach(e.NewValue as Player);
        bar.NotifyAll();
    }

    void Attach(Player? player)
    {
        if (player == null)
            return;
        player.PropertyChanged -= ModelChanged;
        player.PropertyChanged += ModelChanged;
        player.Video.PropertyChanged -= ModelChanged;
        player.Video.PropertyChanged += ModelChanged;
        player.Audio.PropertyChanged -= ModelChanged;
        player.Audio.PropertyChanged += ModelChanged;
        player.Subtitles.PropertyChanged -= ModelChanged;
        player.Subtitles.PropertyChanged += ModelChanged;
        if (player.Playlist is INotifyPropertyChanged playlist)
        {
            playlist.PropertyChanged -= ModelChanged;
            playlist.PropertyChanged += ModelChanged;
        }
        if (player.Playlist.Items is INotifyCollectionChanged items)
        {
            items.CollectionChanged -= ItemsChanged;
            items.CollectionChanged += ItemsChanged;
        }
    }

    void Detach(Player? player)
    {
        if (player == null)
            return;
        player.PropertyChanged -= ModelChanged;
        player.Video.PropertyChanged -= ModelChanged;
        player.Audio.PropertyChanged -= ModelChanged;
        player.Subtitles.PropertyChanged -= ModelChanged;
        if (player.Playlist is INotifyPropertyChanged playlist)
            playlist.PropertyChanged -= ModelChanged;
        if (player.Playlist.Items is INotifyCollectionChanged items)
            items.CollectionChanged -= ItemsChanged;
    }

    void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyAll();

    void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Runtime toggles can notify on their worker thread. Marshal before
        // reading Player (or any other dependency property), not just before Notify.
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => ModelChanged(sender, e)));
            return;
        }
        if (sender == Player && e.PropertyName == nameof(Player.CurTime))
        {
            Notify(nameof(CurrentTimeText));
            return;
        }

        if (sender == Player && e.PropertyName == nameof(Player.Speed))
        {
            Notify(nameof(SpeedToolTip));
            Notify(nameof(SpeedNeedleAngle));
            return;
        }

        if (sender == Player && e.PropertyName == nameof(Player.BufferedDuration))
            return;

        if (sender == Player && e.PropertyName == nameof(Player.IsDisposed) && !CanAdjustVolume && volumePopup != null)
            volumePopup.IsOpen = false;

        if (sender == Player?.Subtitles && e.PropertyName == nameof(Subtitles.SubsText))
            return;

        if (sender == Player?.Audio && (e.PropertyName == nameof(Audio.Volume) || e.PropertyName == nameof(Audio.Mute)))
        {
            Notify(nameof(VolumeIcon));
            return;
        }

        if (sender == Player?.Video && (e.PropertyName == nameof(Video.FPSCurrent) ||
            e.PropertyName == nameof(Video.FramesDisplayed) || e.PropertyName == nameof(Video.FramesDropped)))
            return;

        NotifyAll();
    }

    void NotifyAll()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(NotifyAll);
            return;
        }
        foreach (string property in new[]
        {
            nameof(CurrentTimeText), nameof(DurationText), nameof(ShowDuration), nameof(IsLive), nameof(IsPlaying), nameof(CanPlay),
            nameof(CanSeek), nameof(CanStop), nameof(CanAdjustVolume),
            nameof(HasVideo), nameof(HasAudio), nameof(HasSubtitles), nameof(HasPlaylist), nameof(HasPrevious),
            nameof(HasNext), nameof(CanOpenQueue), nameof(ShowAutoNext), nameof(ShowItemNavigation),
            nameof(VideoLabel), nameof(AudioLabel), nameof(SubtitlesLabel), nameof(VolumeIcon), nameof(SpeedToolTip), nameof(SpeedNeedleAngle)
        })
            Notify(property);
        UpdateResponsiveLayout();
    }

    void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public override void OnApplyTemplate()
    {
        Unhook();
        base.OnApplyTemplate();
        videoButton = GetTemplateChild("PART_Video") as ButtonBase;
        audioButton = GetTemplateChild("PART_Audio") as ButtonBase;
        subtitlesButton = GetTemplateChild("PART_Subtitles") as ButtonBase;
        playlistButton = GetTemplateChild("PART_Playlist") as ButtonBase;
        speedButton = GetTemplateChild("PART_Speed") as ButtonBase;
        moreButton = GetTemplateChild("PART_More") as ButtonBase;
        settingsButton = GetTemplateChild("PART_Settings") as ButtonBase;
        volumeButton = GetTemplateChild("PART_Volume") as ButtonBase;
        volumePopup = GetTemplateChild("PART_VolumePopup") as Popup;

        if (volumeButton != null)
        {
            volumeButton.SetBinding(ContentControl.ContentProperty, new Binding(nameof(VolumeIcon)) { Source = this });
            volumeButton.Click += Volume_Click;
        }
        if (volumePopup != null)
        {
            volumePopup.PlacementTarget = volumeButton;
            volumePopup.Opened += VolumePopup_Opened;
            volumePopup.Closed += VolumePopup_Closed;
        }

        if (videoButton != null) videoButton.Click += Video_Click;
        if (audioButton != null) audioButton.Click += Audio_Click;
        if (subtitlesButton != null) subtitlesButton.Click += Subtitles_Click;
        if (playlistButton != null) playlistButton.Click += Playlist_Click;
        if (speedButton != null) speedButton.Click += Speed_Click;
        if (moreButton != null) moreButton.Click += More_Click;
        if (settingsButton != null) settingsButton.Click += Settings_Click;
        modernLayout = GetTemplateChild("PART_Seek") is FlyleafSeekBar { IsModern: true };
        lastModernLayout = null;
        surface = GetTemplateChild("PART_Surface") as Border;
        modernFrame = GetTemplateChild("PART_ModernFrame") as FrameworkElement;
        dynamicHandle = GetTemplateChild("PART_DynamicHandle") as ButtonBase;
        dynamicHandleBand = GetTemplateChild("PART_DynamicHandleBand") as FrameworkElement;
        modernStop = modernLayout ? GetTemplateChild("PART_Stop") as ButtonBase : null;
        modernAutoNext = modernLayout ? GetTemplateChild("PART_AutoNext") as ButtonBase : null;
        dynamicHandleOffset = null;
        if (dynamicHandle != null)
        {
            dynamicHandleOffset = new TranslateTransform();
            dynamicHandle.SetCurrentValue(RenderTransformProperty, dynamicHandleOffset);
            dynamicHandle.MouseEnter += DynamicHandle_MouseEnter;
            dynamicHandle.Click += DynamicHandle_Click;
        }
        if (dynamicHandleBand != null)
        {
            dynamicHandleBand.MouseEnter += DynamicHandleBand_MouseEnter;
            dynamicHandleBand.MouseLeave += DynamicHandleBand_MouseLeave;
            dynamicHandleBand.SizeChanged += DynamicHandleBand_SizeChanged;
        }
        controlsRow = GetTemplateChild("PART_ControlsRow") as FrameworkElement;
        transportGroup = GetTemplateChild("PART_TransportGroup") as FrameworkElement;
        sourceGroup = GetTemplateChild("PART_SourceGroup") as FrameworkElement;
        compactTimeline = GetTemplateChild("PART_CompactTimeline") as FrameworkElement;
        compactCurrentTime = GetTemplateChild("PART_CompactCurrentTime") as FrameworkElement;
        compactDuration = GetTemplateChild("PART_CompactDuration") as FrameworkElement;
        utilityGroup = GetTemplateChild("PART_UtilityGroup") as FrameworkElement;
        horizontalVolume = GetTemplateChild("PART_HorizontalVolume") as FrameworkElement;
        videoDetail = GetTemplateChild("PART_VideoDetail") as FrameworkElement;
        audioDetail = GetTemplateChild("PART_AudioDetail") as FrameworkElement;
        subtitleDetail = GetTemplateChild("PART_SubtitleDetail") as FrameworkElement;
        if (!modernLayout && controlsRow != null) controlsRow.SizeChanged += ControlsRow_SizeChanged;
        ApplyDynamicPinPreference();
        Notify(nameof(CanOpenSettings));
        UpdateResponsiveLayout();
    }
    /// <summary>Tests the Dynamic handle against a point in physical screen coordinates.</summary>
    public bool IsOverDynamicHandle(Point screenPoint) =>
        IsDynamicMode && (dynamicHandleLeft.HasValue || dynamicHandle?.IsKeyboardFocused == true) &&
        ContainsScreenPoint(dynamicHandle, screenPoint);

    /// <summary>Updates handle visibility while the pointer crosses the host's resize border. Pass null when it leaves.</summary>
    public bool UpdateNonClientDynamicHandleHover(Point? screenPoint)
    {
        bool overBand = false;
        if (IsDynamicMode && dynamicHandleBand is { } band && screenPoint is { } point && ContainsScreenPoint(band, point))
        {
            overBand = true;
            PositionDynamicHandle(band.PointFromScreen(point).X);
        }
        if (IsNonClientPointerOverDynamicHandleBand != overBand)
        {
            SetValue(IsNonClientPointerOverDynamicHandleBandPropertyKey, overBand);
            if (!overBand) QueueDynamicHandleReset();
            ScheduleDynamicCollapse();
        }
        return overBand;
    }

    static bool ContainsScreenPoint(FrameworkElement? element, Point screenPoint) =>
        element is { IsVisible: true, IsHitTestVisible: true } && PresentationSource.FromVisual(element) != null &&
        new Rect(element.RenderSize).Contains(element.PointFromScreen(screenPoint));

    void DynamicHandleBand_MouseEnter(object sender, MouseEventArgs e) =>
        PositionDynamicHandle(e.GetPosition((FrameworkElement)sender).X);

    void DynamicHandleBand_MouseLeave(object sender, MouseEventArgs e) => QueueDynamicHandleReset();

    void DynamicHandleBand_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) UpdateDynamicHandleOffset();
    }

    void PositionDynamicHandle(double pointerX)
    {
        if (!IsDynamicMode || dynamicHandleLeft.HasValue ||
            dynamicHandleBand is not { } band || dynamicHandle is not { } handle ||
            pointerX < 0 || pointerX > band.ActualWidth)
            return;

        const double gap = 8;
        double right = pointerX + gap;
        dynamicHandleLeft = right + handle.ActualWidth + DynamicHandleEdgeInset <= band.ActualWidth
            ? right
            : pointerX - gap - handle.ActualWidth;
        UpdateDynamicHandleOffset();
    }

    void UpdateDynamicHandleOffset()
    {
        if (dynamicHandleOffset is not { } offset || dynamicHandleBand is not { } band ||
            dynamicHandle is not { } handle || dynamicHandleLeft is not { } left)
            return;

        double maxLeft = Math.Max(0, band.ActualWidth - handle.ActualWidth);
        // Keep the handle clear of the docked window's corner resize targets.
        double inset = Math.Min(DynamicHandleEdgeInset, maxLeft / 2);
        offset.X = Math.Clamp(left, inset, maxLeft - inset) - maxLeft / 2;
    }

    void QueueDynamicHandleReset()
    {
        if (!dynamicHandleLeft.HasValue || dynamicHandleReset != null) return;

        // Client and non-client leave messages can arrive during the same visit to the strip.
        dynamicHandleReset = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            dynamicHandleReset = null;
            if (dynamicHandleBand is not { } band || band.IsMouseOver || IsNonClientPointerOverDynamicHandleBand ||
                (dynamicExpanded && (!DynamicPinned || IsMouseOver)) ||
                new Rect(band.RenderSize).Contains(Mouse.GetPosition(band)))
                return;

            ResetDynamicHandlePosition();
        }));
    }

    void ResetDynamicHandlePosition()
    {
        dynamicHandleReset?.Abort();
        dynamicHandleReset = null;
        dynamicHandleLeft = null;
        if (dynamicHandleOffset != null) dynamicHandleOffset.X = 0;
    }

    void DynamicHandle_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!IsDynamicMode || dynamicHandleBand is not { } band) return;
        PositionDynamicHandle(e.GetPosition(band).X);

        // The first enter event can still target the handle's previous position.
        var handle = (ButtonBase)sender;
        if (!DynamicInteractionActive && new Rect(handle.RenderSize).Contains(e.GetPosition(handle)))
            SetDynamicExpanded(true);
    }

    void DynamicHandle_Click(object sender, RoutedEventArgs e)
    {
        if (IsDynamicMode) Options.DynamicPinned = !DynamicPinned;
    }

    void ApplyDynamicPinPreference()
    {
        bool pinned = IsDynamicMode && (bool)GetValue(DynamicPinPreferenceProperty);
        SetDynamicPinned(pinned);
        if (pinned) SetDynamicExpanded(true);
        ScheduleDynamicCollapse();
    }

    void SetDynamicPinned(bool pinned)
    {
        if (DynamicPinned == pinned) return;
        SetValue(DynamicPinnedPropertyKey, pinned);
        Notify(nameof(DynamicPinned));
    }

    bool DynamicInteractionActive => IsMouseCaptureWithin || volumePopup?.IsOpen == true || openPlaybackMenus.Count != 0;
    bool CanCollapseDynamic => dynamicExpanded && !DynamicPinned && !IsMouseOver &&
        !IsNonClientPointerOverDynamicHandleBand && !DynamicInteractionActive;

    void DynamicCaptureChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsDynamicMode && IsMouseCaptureWithin && Player is { } player && Mouse.Captured is FlyleafSeekBar or FlyleafVolumeWedge)
            dynamicDragActivity ??= FlyleafPopupActivity.Hold(player);
        else
        {
            dynamicDragActivity?.Dispose();
            dynamicDragActivity = null;
        }
        ScheduleDynamicCollapse();
    }

    void DynamicAvailabilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && IsHitTestVisible) return;
        ResetDynamicHandlePosition();
        UpdateNonClientDynamicHandleHover(null);
        if (!DynamicPinned && !DynamicInteractionActive) SetDynamicExpanded(false);
    }

    void SetDynamicExpanded(bool expanded)
    {
        dynamicCollapse.Stop();
        if (dynamicExpanded == expanded) return;
        dynamicExpanded = expanded;
        Notify(nameof(DynamicExpanded));
        if (expanded) ScheduleDynamicCollapse();
        else QueueDynamicHandleReset();
    }

    void ScheduleDynamicCollapse()
    {
        dynamicCollapse.Stop();
        if (CanCollapseDynamic) dynamicCollapse.Start();
    }

    void DynamicCollapse_Tick(object? sender, EventArgs e)
    {
        dynamicCollapse.Stop();
        if (CanCollapseDynamic) SetDynamicExpanded(false);
    }

    void ResetDynamic()
    {
        ResetDynamicHandlePosition();
        UpdateNonClientDynamicHandleHover(null);
        SetDynamicPinned(false);
        SetDynamicExpanded(false);
    }

    void ClosePlaybackPopups()
    {
        foreach (var menu in openPlaybackMenus)
        {
            menu.Closed -= PlaybackMenu_Closed;
            menu.IsOpen = false;
        }
        openPlaybackMenus.Clear();
        if (volumePopup != null) volumePopup.IsOpen = false;
        volumePopupActivity?.Dispose();
        volumePopupActivity = null;
        dynamicDragActivity?.Dispose();
        dynamicDragActivity = null;
    }

    void ControlsRow_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();
    protected override Size MeasureOverride(Size availableSize)
    {
        if (modernLayout) UpdateResponsiveLayout(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }
    void UpdateResponsiveLayout(double? measureWidth = null)
    {
        if (arranging || controlsRow == null || transportGroup == null || (!UsesCompactLayout && sourceGroup == null) || utilityGroup == null) return;
        if (modernLayout && !measureWidth.HasValue)
        {
            // Decide which controls fit before the Grid's Auto columns can enlarge its arranged width.
            InvalidateMeasure();
            return;
        }
        if (!modernLayout && controlsRow.ActualWidth <= 0) return;
        double available = surface == null ? measureWidth ?? controlsRow.ActualWidth : (measureWidth ?? ActualWidth) - surface.Margin.Left - surface.Margin.Right -
            surface.Padding.Left - surface.Padding.Right - surface.BorderThickness.Left - surface.BorderThickness.Right;
        if (modernFrame != null) available -= modernFrame.Margin.Left + modernFrame.Margin.Right;
        if (modernLayout)
        {
            // Text bindings may cause a measure on every time update. Only re-evaluate when the
            // available width or the content affecting the natural widths actually changes.
            var layout = (available, ShowItemNavigation, ShowAutoNext, IsLive, ShowDuration, IsModernFullLayout, UsesTimelineOnlyLayout,
                UsesCompactLayout ? (compactCurrentTime as TextBlock)?.Text.Length ?? 0 : 0,
                UsesCompactLayout ? (compactDuration as TextBlock)?.Text.Length ?? 0 : 0,
                (videoDetail as TextBlock)?.Text, (audioDetail as TextBlock)?.Text, (subtitleDetail as TextBlock)?.Text);
            if (lastModernLayout == layout) return;
            lastModernLayout = layout;
        }
        void VisibilityOf(FrameworkElement? element, Visibility value)
        {
            if (element == null || element.Visibility == value) return;
            element.Visibility = value;
            // Visibility changes queue ancestor measurement; resolve it now before
            // measuring another candidate layout during this same resize pass.
            for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(element); parent is UIElement visual;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
            {
                visual.InvalidateMeasure();
                if (ReferenceEquals(parent, controlsRow)) break;
            }
        }
        arranging = true;
        try
        {
            VisibilityOf(compactCurrentTime, Visibility.Visible);
            VisibilityOf(compactDuration, ShowDuration ? Visibility.Visible : Visibility.Collapsed);
            if (UsesTimelineOnlyLayout)
            {
                if (!compact) { compact = true; Notify(nameof(IsCompact)); }
                return;
            }

            bool singleRow = UsesCompactLayout;
            if (compactTimeline != null)
            {
                VisibilityOf(modernAutoNext, !singleRow && ShowAutoNext ? Visibility.Visible : Visibility.Collapsed);
                VisibilityOf(speedButton, singleRow ? Visibility.Collapsed : Visibility.Visible);
            }
            if (!singleRow)
            {
                foreach (var detail in new[] { videoDetail, audioDetail, subtitleDetail })
                    VisibilityOf(detail, Visibility.Visible);
                foreach (var action in new[] { settingsButton, videoButton, audioButton, subtitlesButton, playlistButton, speedButton })
                    VisibilityOf(action, Visibility.Visible);
            }
            else
            {
                foreach (var action in new[] { settingsButton, playlistButton })
                    VisibilityOf(action, Visibility.Visible);
            }
            VisibilityOf(horizontalVolume, Visibility.Visible);
            bool Fits()
            {
                var size = new Size(double.PositiveInfinity, 36);
                var middle = singleRow ? compactTimeline! : sourceGroup!;
                transportGroup.Measure(size); middle.Measure(size); utilityGroup.Measure(size);
                // The seek bar has zero natural width. Reserve a useful timeline before dropping optional controls.
                double reserve = singleRow ? (modernLayout ? CompactTimelineTargetWidth : 56) : (modernLayout ? ModernFullLayoutHeadroom : 0);
                return transportGroup.DesiredSize.Width + middle.DesiredSize.Width + utilityGroup.DesiredSize.Width + reserve <= available + 0.5;
            }
            if (!singleRow)
            {
                foreach (var detail in new[] { videoDetail, audioDetail, subtitleDetail })
                {
                    if (Fits()) break;
                    VisibilityOf(detail, Visibility.Collapsed);
                }
                if (!Fits()) VisibilityOf(speedButton, Visibility.Collapsed);
            }
            bool nextVolumeCompact = (modernLayout && volumePopup?.IsOpen == true) || !Fits();
            if (nextVolumeCompact) VisibilityOf(horizontalVolume, Visibility.Collapsed);
            if (singleRow && !Fits())
            {
                VisibilityOf(compactCurrentTime, Visibility.Collapsed);
                VisibilityOf(compactDuration, Visibility.Collapsed);
            }
            foreach (var action in singleRow ? new[] { settingsButton, playlistButton } : new[] { settingsButton, videoButton, audioButton, subtitlesButton, playlistButton })
            {
                if (Fits()) break;
                VisibilityOf(action, Visibility.Collapsed);
            }
            bool nextCompact = singleRow || videoDetail?.Visibility == Visibility.Collapsed;
            if (compact != nextCompact) { compact = nextCompact; Notify(nameof(IsCompact)); }
            if (volumeCompact != nextVolumeCompact)
            {
                volumeCompact = nextVolumeCompact; Notify(nameof(IsVolumeCompact)); Notify(nameof(VolumeToolTip));
                if (!volumeCompact && volumePopup != null) volumePopup.IsOpen = false;
            }
        }
        finally { arranging = false; }
    }

    void Unhook()
    {
        ResetDynamic();
        if (dynamicHandle != null)
        {
            dynamicHandle.MouseEnter -= DynamicHandle_MouseEnter;
            dynamicHandle.Click -= DynamicHandle_Click;
        }
        if (dynamicHandleBand != null)
        {
            dynamicHandleBand.MouseEnter -= DynamicHandleBand_MouseEnter;
            dynamicHandleBand.MouseLeave -= DynamicHandleBand_MouseLeave;
            dynamicHandleBand.SizeChanged -= DynamicHandleBand_SizeChanged;
        }
        ClosePlaybackPopups();
        if (!modernLayout && controlsRow != null) controlsRow.SizeChanged -= ControlsRow_SizeChanged;
        if (volumeButton != null) volumeButton.Click -= Volume_Click;
        if (volumePopup != null)
        {
            volumePopup.Opened -= VolumePopup_Opened;
            volumePopup.Closed -= VolumePopup_Closed;
        }

        if (videoButton != null) videoButton.Click -= Video_Click;
        if (audioButton != null) audioButton.Click -= Audio_Click;
        if (subtitlesButton != null) subtitlesButton.Click -= Subtitles_Click;
        if (playlistButton != null) playlistButton.Click -= Playlist_Click;
        if (speedButton != null) speedButton.Click -= Speed_Click;
        if (moreButton != null) moreButton.Click -= More_Click;
        if (settingsButton != null) settingsButton.Click -= Settings_Click;
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (settingsRequested is { } handler) handler(this, EventArgs.Empty);
        else ToggleOwnerPanel("settings");
    }

    bool CanRequestPanel(string id) => PanelRequested != null || MediaElement?.IsPanelRegistered(id) == true;

    void RequestPanel(string id)
    {
        if (PanelRequested is { } handler) handler(this, new(id));
        else ToggleOwnerPanel(id);
    }

    void ToggleOwnerPanel(string id)
    {
        if (MediaElement is not { IsLoaded: true } owner || !owner.IsPanelRegistered(id)) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(MediaElement, owner) && owner.IsLoaded && owner.IsPanelRegistered(id))
                owner.TogglePanel(id);
        }));
    }

    void Volume_Click(object sender, RoutedEventArgs e)
    {
        if (Player is not { IsDisposed: false } player) return;
        if (volumeCompact)
        {
            if (volumePopup != null)
                volumePopup.IsOpen = !volumePopup.IsOpen;
        }
        else
            player.Commands.ToggleMute.Execute(null);
    }

    void VolumePopup_Opened(object? sender, EventArgs e)
    {
        volumePopupActivity ??= FlyleafPopupActivity.Hold(Player);
        dynamicCollapse.Stop();
    }

    void VolumePopup_Closed(object? sender, EventArgs e)
    {
        volumePopupActivity?.Dispose();
        volumePopupActivity = null;
        if (modernLayout)
        {
            lastModernLayout = null;
            InvalidateMeasure();
        }
        ScheduleDynamicCollapse();
    }

    bool UsesTimelineOnlyLayout => compactTimeline != null && IsDynamicMode && !IsModernFullLayout;
    bool UsesCompactLayout => compactTimeline != null && !IsDynamicMode && !IsModernFullLayout;
    bool ActionVisible(ButtonBase? button) => button?.Visibility == Visibility.Visible && (!UsesCompactLayout || button.IsVisible);

    internal void TrackOpenPlaybackMenu(ContextMenu menu)
    {
        if (!openPlaybackMenus.Add(menu)) return;
        menu.Closed += PlaybackMenu_Closed;
        dynamicCollapse.Stop();
    }

    void PlaybackMenu_Closed(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        menu.Closed -= PlaybackMenu_Closed;
        openPlaybackMenus.Remove(menu);
        ScheduleDynamicCollapse();
    }

    ContextMenu NewMenu(ButtonBase anchor)
    {
        if (!ActionVisible(anchor) && moreButton != null) anchor = moreButton;
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Top,
            MinWidth = 185
        };
        if (TryFindResource("FlyleafME.ThemeContextMenuStyle") is Style style)
            menu.Style = style;
        if (TryFindResource("FlyleafME.ThemeMenuItemStyle") is Style itemStyle)
            menu.ItemContainerStyle = itemStyle;
        IDisposable? activity = null;
        menu.Opened += (_, _) =>
        {
            activity ??= FlyleafPopupActivity.Hold(Player);
            TrackOpenPlaybackMenu(menu);
        };
        menu.Closed += (_, _) =>
        {
            activity?.Dispose();
            activity = null;
        };
        return menu;
    }

    void AddItem(ContextMenu menu, string header, Action? action, bool selected = false)
    {
        // The custom template renders selection; don't let WPF allocate its native check gutter.
        var item = new MenuItem { Header = header, IsCheckable = false, IsChecked = selected, IsEnabled = action != null };
        if (TryFindResource("FlyleafME.ThemeMenuItemStyle") is Style style)
            item.Style = style;
        if (action != null)
            item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    void AddPanelItem(ContextMenu menu, string id, string title)
    {
        if (!CanRequestPanel(id)) return;
        if (menu.Items.Count > 0)
        {
            var separator = new Separator();
            separator.SetResourceReference(StyleProperty, "FlyleafME.ThemeMenuSeparatorStyle");
            menu.Items.Add(separator);
        }
        AddItem(menu, title, () => RequestPanel(id));
    }

    void Video_Click(object sender, RoutedEventArgs e)
    {
        if (Player?.Video.Streams == null || videoButton == null)
            return;
        var menu = NewMenu(videoButton);
        var target = Player;
        AddItem(menu, "Enabled", () => { if (ReferenceEquals(Player, target) && !target.IsDisposed) target.Video.Toggle(); }, target.Video.Enabled);
        FlyleafTrackMenus.AddVideoTracks(menu, target, () => ReferenceEquals(Player, target));
        AddPanelItem(menu, "video", "Open video panel…");
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    void Audio_Click(object sender, RoutedEventArgs e)
    {
        if (Player?.Audio.Streams == null || audioButton == null)
            return;
        var menu = NewMenu(audioButton);
        AddItem(menu, "Enabled", () => Player.Audio.Toggle(), Player.Audio.Enabled);
        var target = Player;
        FlyleafTrackMenus.AddAudioTracks(menu, target, () => ReferenceEquals(Player, target));
        AddPanelItem(menu, "audio", "Open audio panel…");
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    void Subtitles_Click(object sender, RoutedEventArgs e)
    {
        if (Player == null || subtitlesButton == null)
            return;
        var menu = NewMenu(subtitlesButton);
        AddItem(menu, "Enabled", () => Player.Subtitles.Toggle(), Player.Subtitles.Enabled);
        var target = Player;
        FlyleafTrackMenus.AddSubtitleTracks(menu, target, () => ReferenceEquals(Player, target));
        AddPanelItem(menu, "subtitles", "Open subtitles panel…");
        menu.IsOpen = true;
    }

    void Playlist_Click(object sender, RoutedEventArgs e)
    {
        if (QueueSource != null && CanRequestPanel("queue")) { RequestPanel("queue"); return; }
        if (Player == null || playlistButton == null)
            return;
        var menu = NewMenu(playlistButton);
        foreach (var item in Player.Playlist.Items)
        {
            var current = item;
            AddItem(menu, item.Title ?? "Untitled", () => Player.Commands.Reopen.Execute(current), current == Player.Playlist.Selected);
        }
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    void AddOverflowActions(ContextMenu menu)
    {
        foreach (var (button, label) in new[] { (settingsButton, "Settings…"), (videoButton, "Video streams…"),
            (audioButton, "Audio streams…"), (subtitlesButton, "Subtitles…"), (playlistButton, "Queue…") })
        {
            if (button == null || (!UsesCompactLayout ? button.Visibility != Visibility.Collapsed : ActionVisible(button))) continue;
            if (ReferenceEquals(button, playlistButton) && QueueSource != null && CanRequestPanel("queue")) continue;
            var action = button;
            AddItem(menu, label, action.IsEnabled ? () => action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)) : null);
        }
    }

    void Speed_Click(object sender, RoutedEventArgs e)
    {
        if (speedButton == null || Player is not { } target || !FlyleafPlaybackActions.CanPlay(target)) return;
        var menu = NewMenu(speedButton);
        FlyleafMenuItems.Speed(menu, target, () => ReferenceEquals(Player, target));
        if (CanRequestPanel("playback"))
        {
            FlyleafMenuItems.Separator(menu);
            AddItem(menu, "Playback panel…", () => RequestPanel("playback"));
        }
        menu.IsOpen = true;
    }

    void More_Click(object sender, RoutedEventArgs e)
    {
        if (moreButton == null) return;
        var menu = BuildMoreMenu(moreButton);
        if (menu.HasItems) menu.IsOpen = true;
    }

    ContextMenu BuildMoreMenu(ButtonBase anchor)
    {
        var menu = NewMenu(anchor);
        AddOverflowActions(menu);
        var target = Player;
        if (UsesCompactLayout)
        {
            bool addStop = !ActionVisible(modernStop), addAutoNext = !ActionVisible(modernAutoNext) && ShowAutoNext;
            if ((addStop || addAutoNext) && menu.HasItems) FlyleafMenuItems.Separator(menu);
            if (addStop) AddItem(menu, "Stop", CanStop && target != null ? () => target.Commands.Stop.Execute(null) : null);
            if (addAutoNext && QueueSource is { } queue)
                AddItem(menu, "Auto-next", () => queue.AutoAdvance = !queue.AutoAdvance, queue.AutoAdvance);
        }
        if (target != null)
        {
            if (menu.Items.Count > 0) FlyleafMenuItems.Separator(menu);
            if (!ActionVisible(speedButton))
            {
                var speeds = FlyleafMenuItems.Group(menu, "Speed");
                speeds.IsEnabled = FlyleafPlaybackActions.CanPlay(target);
                FlyleafMenuItems.Speed(speeds, target, () => ReferenceEquals(Player, target));
            }
            FlyleafMenuItems.Modes(menu, target, () => ReferenceEquals(Player, target));
        }
        var panels = new[] { ("playback", "Playback"), ("video", "Video adjustments"), ("audio", "Audio"),
            ("subtitles", "Subtitles"), ("queue", "Queue"), ("information", "Media information") };
        var availablePanels = panels.Where(p => CanRequestPanel(p.Item1)).ToArray();
        if (availablePanels.Length > 0)
        {
            if (menu.Items.Count > 0) FlyleafMenuItems.Separator(menu);
            var group = FlyleafMenuItems.Group(menu, "Panels");
            foreach (var (id, label) in availablePanels)
                FlyleafMenuItems.Add(group, label, () => RequestPanel(id));
        }
        if (MediaElement is { CanOfferFolderImport: true } folderOwner)
        {
            if (menu.HasItems) FlyleafMenuItems.Separator(menu);
            folderOwner.AddFolderMenuItem(menu);
        }
        if (InformationSource is { } information)
        {
            if (menu.Items.Count > 0) FlyleafMenuItems.Separator(menu);
            AddItem(menu, "Show debug overlay", () =>
            {
                if (ReferenceEquals(InformationSource, information)) information.ShowDebugOverlay = !information.ShowDebugOverlay;
            }, information.ShowDebugOverlay);
        }
        if (GeneralMenuRequested != null || MediaElement != null)
        {
            if (menu.Items.Count > 0) FlyleafMenuItems.Separator(menu);
            AddItem(menu, "General menu…", () =>
            {
                if (GeneralMenuRequested is { } handler) handler(this, EventArgs.Empty);
                else if (MediaElement is { } owner)
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (IsLoaded && ReferenceEquals(anchor, moreButton) && ReferenceEquals(owner, MediaElement) && ReferenceEquals(target, Player))
                            owner.ShowContextMenu(anchor, PlacementMode.Top, this);
                    }));
            });
        }
        return menu;
    }
}
