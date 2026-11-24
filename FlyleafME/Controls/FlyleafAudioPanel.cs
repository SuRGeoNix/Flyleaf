using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using Microsoft.Win32;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>Reusable current-player audio adjustments, independent of panel presentation.</summary>
public sealed class FlyleafAudioPanel : StackPanel
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafAudioPanel),
        new PropertyMetadata(null, (d, _) => { if (((FlyleafAudioPanel)d).IsLoaded) ((FlyleafAudioPanel)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }

    readonly CheckBox enabled = new() { Content = "Enable audio" };
    readonly CheckBox mute = new() { Content = "Mute" };
    readonly Button capture = Button("Choose capture device  ▾");
    readonly Button track = Button("No audio track");
    readonly Button device = Button("Default output");
    readonly Button load = Button("Load audio file…");
    readonly Slider volume = new() { Minimum = 0, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 10, 0, 8) };
    readonly TextBlock details = Text("Open media to choose an audio track.", true);
    readonly TextBlock feedback = Text("", true);
    readonly Border synchronization;
    readonly Action refreshDelay;
    FlyleafPanelContext? observedContext;
    Player? player;
    ContextMenu? openMenu;
    bool refreshQueued;

    public FlyleafAudioPanel()
    {
        enabled.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        mute.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        volume.SetResourceReference(StyleProperty, "FlyleafME.PlaybackVolumeStyle");
        volume.Focusable = true;
        Children.Add(enabled);
        load.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(Section("Track", track, details, load, feedback));
        Children.Add(Section("Capture input", capture));
        Children.Add(Section("Output", device));
        (synchronization, refreshDelay) = DelaySection("audio", () => player?.Audio.Delay ?? 0,
            ticks => { if (player != null) player.Audio.Delay = ticks; },
            () => player?.Audio.DelayRemove(), () => player?.Audio.DelayAdd());
        Children.Add(synchronization);

        var level = Text("");
        level.SetBinding(TextBlock.TextProperty, new Binding("Context.Player.Audio.Volume") { Source = this, StringFormat = "{0}%" });
        var volumeHeader = new DockPanel();
        DockPanel.SetDock(level, Dock.Right);
        volumeHeader.Children.Add(level);
        volumeHeader.Children.Add(mute);
        Children.Add(Section("Volume", volumeHeader, volume));

        track.Click += (_, _) => ShowTracks();
        capture.Click += (_, _) => ShowCaptureDevices();
        device.Click += (_, _) => ShowDevices();
        load.Click += (_, _) => LoadFile();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    void Attach()
    {
        Detach();
        observedContext = Context;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        AttachPlayer();
    }

    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        observedContext = null;
        DetachPlayer();
    }

    void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(player, observedContext?.Player)) AttachPlayer();
    }

    void DetachPlayer()
    {
        if (openMenu != null) openMenu.IsOpen = false;
        openMenu = null;
        if (player != null)
        {
            player.Audio.PropertyChanged -= AudioChanged;
            player.PropertyChanged -= PlayerChanged;
            player.OpenCompleted -= OpenChanged;
            player.OpenPlaylistItemCompleted -= OpenChanged;
            player.OpenAudioStreamCompleted -= OpenChanged;
            player.OpenExternalAudioStreamCompleted -= OpenChanged;
        }
        foreach (var control in new DependencyObject[] { enabled, mute, volume }) BindingOperations.ClearAllBindings(control);
        player = null;
    }

    void AttachPlayer()
    {
        DetachPlayer();
        player = observedContext?.Player;
        if (player != null)
        {
            player.Audio.PropertyChanged += AudioChanged;
            player.PropertyChanged += PlayerChanged;
            player.OpenCompleted += OpenChanged;
            player.OpenPlaylistItemCompleted += OpenChanged;
            player.OpenAudioStreamCompleted += OpenChanged;
            player.OpenExternalAudioStreamCompleted += OpenChanged;
            enabled.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Enabled") { Source = player.Audio, Mode = BindingMode.TwoWay });
            mute.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Mute") { Source = player.Audio, Mode = BindingMode.TwoWay });
            volume.SetBinding(RangeBase.MaximumProperty, new Binding("VolumeMax") { Source = player.Config.Audio });
            volume.SetBinding(RangeBase.ValueProperty, new Binding("Volume") { Source = player.Audio, Mode = BindingMode.TwoWay });
        }
        feedback.Text = "";
        Refresh();
    }

    void AudioChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Audio.FramesDisplayed) or nameof(Audio.FramesDropped) or nameof(Audio.BitRate))) QueueRefresh();
    }
    void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Player.Status) or nameof(Player.IsDisposed)) QueueRefresh();
    }
    void OpenChanged(object? sender, object e)
    {
        var error = e switch
        {
            FlyleafLib.MediaFramework.MediaContext.DecoderContext.OpenAudioStreamCompletedArgs result => result.Error,
            FlyleafLib.MediaFramework.MediaContext.DecoderContext.OpenExternalAudioStreamCompletedArgs result => result.Error,
            _ => null
        };
        if (e is FlyleafLib.MediaFramework.MediaContext.DecoderContext.OpenAudioStreamCompletedArgs or FlyleafLib.MediaFramework.MediaContext.DecoderContext.OpenExternalAudioStreamCompletedArgs)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsLoaded && ReferenceEquals(sender, player)) feedback.Text = error ?? "";
            }));
        QueueRefresh();
    }
    void QueueRefresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(QueueRefresh)); return; }
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { refreshQueued = false; if (IsLoaded) Refresh(); }));
    }

    void Refresh()
    {
        var available = player is { IsDisposed: false };
        enabled.IsEnabled = mute.IsEnabled = volume.IsEnabled = device.IsEnabled = available;
        load.IsEnabled = available && player!.CanPlay && player.Playlist.Selected != null;
        capture.IsEnabled = available && Context?.MediaElement != null;
        track.IsEnabled = available && (player!.Audio.Streams.Count > 0 || player.Playlist.Selected?.ExternalAudioStreams.Count > 0);
        synchronization.IsEnabled = available && player!.Audio.IsOpened;
        device.Content = available ? player!.Audio.Device?.Name ?? "Default output" : "No player";
        var external = available ? player!.Playlist.Selected?.ExternalAudioStream : null;
        track.Content = available && player!.Audio.IsOpened
            ? $"{(external != null ? "External" : "Track")} {player.Audio.StreamIndex + 1} · {player.Audio.Codec}  ▾"
            : "Choose audio track  ▾";
        details.Text = available && player!.Audio.IsOpened
            ? $"{player.Audio.Codec} · {player.Audio.Channels} channels · {player.Audio.SampleRate:N0} Hz"
            : "No audio stream is open. Volume, mute and output can be set before playback.";
        refreshDelay();
    }

    ContextMenu Menu(Button anchor)
    {
        if (openMenu != null) openMenu.IsOpen = false;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MaxHeight = 360 };
        openMenu = menu;
        menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
        IDisposable? hold = null;
        menu.Opened += (_, _) => hold = FlyleafPopupActivity.Hold(player);
        menu.Closed += (_, _) => { hold?.Dispose(); hold = null; };
        return menu;
    }

    static void Add(ContextMenu menu, string title, bool selected, Action action)
    {
        var item = new MenuItem { Header = title, IsChecked = selected };
        item.SetResourceReference(StyleProperty, "FlyleafME.ThemeMenuItemStyle");
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    void ShowCaptureDevices()
    {
        if (player is not { IsDisposed: false } target || Context?.MediaElement is not { } owner) return;
        var menu = Menu(capture);
        owner.PopulateCaptureDeviceMenu(menu, target, video: false);
        menu.IsOpen = true;
    }
    void ShowTracks()
    {
        if (player is not { IsDisposed: false } target) return;
        var menu = Menu(track);
        FlyleafTrackMenus.AddAudioTracks(menu, target, () => ReferenceEquals(player, target), () => feedback.Text = "Opening audio…");
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    void ShowDevices()
    {
        if (player is not { IsDisposed: false } target) return;
        var menu = Menu(device);
        foreach (var endpoint in Engine.Audio.Devices)
            Add(menu, endpoint.Name, endpoint.Id == target.Audio.Device?.Id,
                () => { if (!target.IsDisposed && ReferenceEquals(player, target)) target.Audio.Device = endpoint; });
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }

    void LoadFile()
    {
        if (player is not { IsDisposed: false, CanPlay: true } target || target.Playlist.Selected is not { } selected) return;
        var dialog = new OpenFileDialog { Title = "Load external audio", Filter = "Audio files|*.aac;*.ac3;*.dts;*.flac;*.m4a;*.mp3;*.ogg;*.opus;*.wav;*.wma|All files|*.*" };
        using var hold = FlyleafPopupActivity.Hold(target);
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || target.IsDisposed || !ReferenceEquals(player, target) || !ReferenceEquals(selected, target.Playlist.Selected) || !target.CanPlay) return;
        var stream = new ExternalAudioStream { Url = dialog.FileName };
        PlaylistItem.AddExternalStream(stream, selected, "FlyleafME");
        feedback.Text = "Opening audio…";
        target.OpenAsync(stream);
    }
}
