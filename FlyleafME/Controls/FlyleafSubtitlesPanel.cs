using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FlyleafLib.MediaPlayer;
using Microsoft.Win32;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;
using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace Flyleaf.Controls.WPF;

/// <summary>Track selection and synchronization for the current player's subtitles.</summary>
public sealed class FlyleafSubtitlesPanel : StackPanel
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafSubtitlesPanel),
        new PropertyMetadata(null, (d, _) => { if (((FlyleafSubtitlesPanel)d).IsLoaded) ((FlyleafSubtitlesPanel)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }

    readonly CheckBox enabled = new() { Content = "Enable subtitles" };
    readonly Button track = Button("Choose subtitle track  ▾");
    readonly Button load = Button("Load subtitle file…");
    readonly TextBlock details = Text("Open a video to load subtitles.", true);
    readonly TextBlock feedback = Text("", true);
    readonly Border synchronization;
    readonly Action refreshDelay;
    FlyleafPanelContext? observedContext;
    Player? player;
    ContextMenu? openMenu;
    bool refreshQueued;

    public FlyleafSubtitlesPanel()
    {
        enabled.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        load.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(enabled);
        Children.Add(Section("Track", track, details, load, feedback));
        (synchronization, refreshDelay) = DelaySection("subtitle", () => player?.Subtitles.Delay ?? 0,
            ticks => { if (player != null) player.Subtitles.Delay = ticks; },
            () => player?.Subtitles.DelayRemove(), () => player?.Subtitles.DelayAdd());
        Children.Add(synchronization);
        var appearance = new FlyleafSubtitleAppearanceEditor();
        appearance.SetBinding(FlyleafSubtitleAppearanceEditor.OptionsProperty, new Binding("Context.UIConfig.Subtitles") { Source = this });
        var appearanceSection = new Expander { Header = Text("Text appearance"), Content = appearance, Margin = new Thickness(0, 16, 0, 0) };
        appearanceSection.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush"); Children.Add(appearanceSection);
        track.Click += (_, _) => ShowTracks();
        load.Click += (_, _) => LoadFile();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    void Attach()
    {
        Detach(); observedContext = Context;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        AttachPlayer();
    }
    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        observedContext = null; DetachPlayer();
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
            player.PropertyChanged -= PlayerChanged;
            player.Subtitles.PropertyChanged -= SubtitlesChanged;
            player.Video.PropertyChanged -= PlayerChanged;
            player.OpenCompleted -= OpenChanged;
            player.OpenPlaylistItemCompleted -= OpenChanged;
            player.OpenSubtitlesStreamCompleted -= OpenChanged;
            player.OpenExternalSubtitlesStreamCompleted -= OpenChanged;
        }
        BindingOperations.ClearBinding(enabled, ToggleButton.IsCheckedProperty);
        player = null;
    }
    void AttachPlayer()
    {
        DetachPlayer(); player = observedContext?.Player;
        if (player != null)
        {
            player.PropertyChanged += PlayerChanged;
            player.Subtitles.PropertyChanged += SubtitlesChanged;
            player.Video.PropertyChanged += PlayerChanged;
            player.OpenCompleted += OpenChanged;
            player.OpenPlaylistItemCompleted += OpenChanged;
            player.OpenSubtitlesStreamCompleted += OpenChanged;
            player.OpenExternalSubtitlesStreamCompleted += OpenChanged;
            enabled.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Enabled") { Source = player.Subtitles, Mode = BindingMode.TwoWay });
        }
        feedback.Text = ""; Refresh();
    }
    void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Player.Status) or nameof(Player.IsDisposed) or nameof(Video.IsOpened)) QueueRefresh();
    }
    void SubtitlesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Subtitles.SubsText)) QueueRefresh();
    }
    void OpenChanged(object? sender, object e)
    {
        var error = e switch
        {
            FlyleafLib.MediaPlayer.OpenCompletedArgs result when result.IsSubtitles => result.Error,
            OpenSubtitlesStreamCompletedArgs result => result.Error,
            OpenExternalSubtitlesStreamCompletedArgs result => result.Error,
            _ => null
        };
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded || !ReferenceEquals(sender, player)) return;
            if (e is OpenSubtitlesStreamCompletedArgs or OpenExternalSubtitlesStreamCompletedArgs || e is FlyleafLib.MediaPlayer.OpenCompletedArgs { IsSubtitles: true })
                feedback.Text = error ?? "";
            Refresh();
        }));
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
        enabled.IsEnabled = available;
        load.IsEnabled = available && player!.Video.IsOpened;
        track.IsEnabled = available && (player!.Subtitles.Streams.Count > 0 || player.Playlist.Selected?.ExternalSubtitlesStreams.Count > 0);
        synchronization.IsEnabled = available && player!.Subtitles.IsOpened;
        var external = available ? player!.Playlist.Selected?.ExternalSubtitlesStream : null;
        track.Content = available && player!.Subtitles.IsOpened
            ? external?.Title is { Length: > 0 } title ? title + "  ▾" : $"Track {player.Subtitles.StreamIndex + 1} · {player.Subtitles.Codec}  ▾"
            : "Choose subtitle track  ▾";
        details.Text = available && player!.Subtitles.IsOpened ? $"{player.Subtitles.Codec} · {(external == null ? "Embedded subtitles" : "External subtitles")}" :
            load.IsEnabled ? "Choose an available track or load a subtitle file." : "Open a video to load subtitles.";
        refreshDelay();
    }

    void ShowTracks()
    {
        if (player is not { IsDisposed: false } target) return;
        if (openMenu != null) openMenu.IsOpen = false;
        var menu = new ContextMenu { PlacementTarget = track, Placement = PlacementMode.Bottom, MaxHeight = 360 };
        openMenu = menu;
        menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
        IDisposable? hold = null;
        menu.Opened += (_, _) => hold = FlyleafPopupActivity.Hold(target);
        menu.Closed += (_, _) => { hold?.Dispose(); hold = null; };
        FlyleafTrackMenus.AddSubtitleTracks(menu, target, () => ReferenceEquals(player, target), () => feedback.Text = "Opening subtitles…");
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }
    void LoadFile()
    {
        if (player is not { IsDisposed: false } target || !target.Video.IsOpened) return;
        var dialog = new OpenFileDialog { Title = "Load subtitles", Filter = "Subtitle files|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.idx;*.sup;*.ttml|All files|*.*" };
        using var hold = FlyleafPopupActivity.Hold(target);
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || target.IsDisposed || !ReferenceEquals(player, target) || !target.Video.IsOpened) return;
        // Force subtitle dispatch even for extensions not recognized by the normal OpenAsync path.
        feedback.Text = "Opening subtitles…";
        target.OpenAsync(dialog.FileName, forceSubtitles: true);
    }
}
