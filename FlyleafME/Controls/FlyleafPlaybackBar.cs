using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

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
    bool compact, volumeCompact, arranging;
    FrameworkElement? controlsRow, transportGroup, sourceGroup, utilityGroup, horizontalVolume;
    FrameworkElement? videoDetail, audioDetail, subtitleDetail;
    Border? surface;

    public string CurrentTimeText => Player?.CanPlay == true ? FormatTime(Player.CurTime) : "--:--:--";
    public string DurationText => Player?.Duration > 0 ? FormatTime(Player.Duration) : "--:--:--";
    public bool IsLive => Player is { IsLive: true } p && !FlyleafQueueProgression.IsImageFormat(p.MainDemuxer?.Name);
    public bool IsPlaying => Player?.IsPlaying == true;
    public bool CanPlay => Player?.CanPlay == true;
    public bool CanSeek => CanPlay && Player?.Duration > 0;
    public bool CanStop => CanPlay && Player?.Status != Status.Stopped;
    public bool HasVideo => Player?.Video.IsOpened == true;
    public bool HasAudio => Player?.Audio.IsOpened == true;
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
            if (Player == null)
                return "\uE992";
            if (Player.Audio.Mute)
                return "\uE74F";
            double volume = (double)Player.Audio.Volume / Math.Max(1, Player.Config.Audio.VolumeMax);
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
        CoerceValue(OptionsProperty);
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Unloaded += (_, _) => Detach(Player);
        Loaded += (_, _) => Attach(Player);
    }

    void PanelRegistryChanged(object? sender, EventArgs e) => Notify(nameof(CanOpenSettings));

    static void OnPlayerChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        var bar = (FlyleafPlaybackBar)obj;
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
            nameof(CurrentTimeText), nameof(DurationText), nameof(IsLive), nameof(IsPlaying), nameof(CanPlay),
            nameof(CanSeek), nameof(CanStop),
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
        surface = GetTemplateChild("PART_Surface") as Border;
        controlsRow = GetTemplateChild("PART_ControlsRow") as FrameworkElement;
        transportGroup = GetTemplateChild("PART_TransportGroup") as FrameworkElement;
        sourceGroup = GetTemplateChild("PART_SourceGroup") as FrameworkElement;
        utilityGroup = GetTemplateChild("PART_UtilityGroup") as FrameworkElement;
        horizontalVolume = GetTemplateChild("PART_HorizontalVolume") as FrameworkElement;
        videoDetail = GetTemplateChild("PART_VideoDetail") as FrameworkElement;
        audioDetail = GetTemplateChild("PART_AudioDetail") as FrameworkElement;
        subtitleDetail = GetTemplateChild("PART_SubtitleDetail") as FrameworkElement;
        if (controlsRow != null) controlsRow.SizeChanged += ControlsRow_SizeChanged;
        Notify(nameof(CanOpenSettings));
        UpdateResponsiveLayout();
    }
    void ControlsRow_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();
    void UpdateResponsiveLayout()
    {
        if (arranging || controlsRow == null || transportGroup == null || sourceGroup == null || utilityGroup == null ||
            controlsRow.ActualWidth <= 0) return;
        double available = surface == null ? controlsRow.ActualWidth : ActualWidth - surface.Margin.Left - surface.Margin.Right -
            surface.Padding.Left - surface.Padding.Right - surface.BorderThickness.Left - surface.BorderThickness.Right;
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
            foreach (var detail in new[] { videoDetail, audioDetail, subtitleDetail })
                VisibilityOf(detail, Visibility.Visible);
            foreach (var action in new[] { settingsButton, videoButton, audioButton, subtitlesButton, playlistButton, speedButton })
                VisibilityOf(action, Visibility.Visible);
            VisibilityOf(horizontalVolume, Visibility.Visible);
            bool Fits()
            {
                var size = new Size(double.PositiveInfinity, 36);
                transportGroup.Measure(size); sourceGroup.Measure(size); utilityGroup.Measure(size);
                return transportGroup.DesiredSize.Width + sourceGroup.DesiredSize.Width + utilityGroup.DesiredSize.Width <= available + 0.5;
            }
            foreach (var detail in new[] { videoDetail, audioDetail, subtitleDetail })
            {
                if (Fits()) break;
                VisibilityOf(detail, Visibility.Collapsed);
            }
            if (!Fits()) VisibilityOf(speedButton, Visibility.Collapsed);
            bool nextVolumeCompact = !Fits();
            if (nextVolumeCompact) VisibilityOf(horizontalVolume, Visibility.Collapsed);
            foreach (var action in new[] { settingsButton, videoButton, audioButton, subtitlesButton, playlistButton })
            {
                if (Fits()) break;
                VisibilityOf(action, Visibility.Collapsed);
            }
            bool nextCompact = videoDetail?.Visibility == Visibility.Collapsed;
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
        if (controlsRow != null) controlsRow.SizeChanged -= ControlsRow_SizeChanged;
        if (volumeButton != null) volumeButton.Click -= Volume_Click;
        if (volumePopup != null)
        {
            volumePopup.IsOpen = false;
            volumePopup.Opened -= VolumePopup_Opened;
            volumePopup.Closed -= VolumePopup_Closed;
        }
        volumePopupActivity?.Dispose();
        volumePopupActivity = null;

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
        if (volumeCompact)
        {
            if (volumePopup != null)
                volumePopup.IsOpen = !volumePopup.IsOpen;
        }
        else
            Player?.Commands.ToggleMute.Execute(null);
    }

    void VolumePopup_Opened(object? sender, EventArgs e)
        => volumePopupActivity ??= FlyleafPopupActivity.Hold(Player);

    void VolumePopup_Closed(object? sender, EventArgs e)
    {
        volumePopupActivity?.Dispose();
        volumePopupActivity = null;
    }

    ContextMenu NewMenu(ButtonBase anchor)
    {
        if (anchor.Visibility != Visibility.Visible && moreButton != null) anchor = moreButton;
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Top,
            MinWidth = 185,
            MaxHeight = 350
        };
        if (TryFindResource("FlyleafME.ThemeContextMenuStyle") is Style style)
            menu.Style = style;
        if (TryFindResource("FlyleafME.ThemeMenuItemStyle") is Style itemStyle)
            menu.ItemContainerStyle = itemStyle;
        IDisposable? activity = null;
        menu.Opened += (_, _) => activity ??= FlyleafPopupActivity.Hold(Player);
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
            if (button?.Visibility != Visibility.Collapsed) continue;
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
        if (target != null)
        {
            if (menu.Items.Count > 0) FlyleafMenuItems.Separator(menu);
            if (speedButton?.Visibility != Visibility.Visible)
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
            AddItem(menu, "Show statistics overlay", () =>
            {
                if (ReferenceEquals(InformationSource, information)) information.ShowStatisticsOverlay = !information.ShowStatisticsOverlay;
            }, information.ShowStatisticsOverlay);
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
                        if (IsLoaded && ReferenceEquals(owner, MediaElement)) owner.ShowContextMenu(anchor, PlacementMode.Top);
                    }));
            });
        }
        return menu;
    }
}
