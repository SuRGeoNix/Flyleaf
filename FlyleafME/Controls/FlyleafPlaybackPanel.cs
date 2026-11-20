using System.ComponentModel;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaPlayer;
using Microsoft.Win32;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>Current-player transport, precision playback and capture controls.</summary>
public sealed class FlyleafPlaybackPanel : StackPanel
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafPlaybackPanel),
        new PropertyMetadata(null, (d, _) => { if (((FlyleafPlaybackPanel)d).IsLoaded) ((FlyleafPlaybackPanel)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }

    readonly TextBlock position = Text("No media open", true);
    readonly TextBlock feedback = Text("", true);
    readonly Button play = Button("Play");
    readonly Button stop = Button("Stop");
    readonly Button backward = Button("Seek back");
    readonly Button forward = Button("Seek forward");
    readonly TextBox jump = Input("00:00:00");
    readonly Button jumpButton = Button("Go");
    readonly TextBox speed = Input("1");
    readonly Button speedSet = Button("Set");
    readonly ComboBox speedPresets = new() { Width = 96, ToolTip = "Common speed multipliers" };
    readonly CheckBox repeat = new() { Content = "Repeat current item" };
    readonly CheckBox reverse = new() { Content = "Reverse video" };
    readonly Button previousFrame = Button("Previous frame");
    readonly Button nextFrame = Button("Next frame");
    readonly StackPanel chapters = new();
    readonly TextBlock noChapters = Text("No chapters in this input.", true);
    readonly Button record = Button("Start recording…");
    readonly Button snapshot = Button("Save snapshot…");
    readonly List<Demuxer.Chapter> shownChapters = [];
    FlyleafPanelContext? observedContext;
    Player? player;
    IDisposable? speedActivity;
    double? shownSpeed;
    long shownPositionSecond = -1;
    bool refreshQueued, busy, refreshing;

    public FlyleafPlaybackPanel()
    {
        repeat.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        reverse.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        reverse.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(feedback);
        Children.Add(Section("Transport", Row(play, stop), position, Row(backward, forward),
            Text("Jump to time · hh:mm:ss", true), Row(jump, jumpButton)));
        var multiplier = Text("Multiplier (0.125–16×)", true);
        multiplier.Margin = new Thickness(0);
        AutomationProperties.SetLabeledBy(speed, multiplier);
        AutomationProperties.SetName(speedPresets, "Common speeds");
        speedPresets.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
        speedPresets.Items.Add(new ComboBoxItem { Content = "Presets", IsEnabled = false });
        foreach (var rate in FlyleafPlaybackActions.SpeedPresets)
            speedPresets.Items.Add(new ComboBoxItem { Content = $"{SpeedText(rate)}×", Tag = rate });
        speedPresets.SelectedIndex = 0;
        Children.Add(Section("Speed", multiplier, Row(speed, speedSet, speedPresets)));
        Children.Add(Section("Playback mode", repeat, reverse));
        Children.Add(Section("Frame stepping", Row(previousFrame, nextFrame)));
        Children.Add(Section("Chapters", noChapters, new ScrollViewer { Content = chapters, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }));
        Children.Add(Section("Capture", Row(record, snapshot)));
        play.Click += (_, _) => Run(p => { p.TogglePlayPause(); return null; });
        stop.Click += (_, _) => Run(p => { p.Stop(); return null; });
        backward.Click += (_, _) => Run(p => { if (FlyleafPlaybackActions.CanSeek(p)) p.SeekBackward(); return null; });
        forward.Click += (_, _) => Run(p => { if (FlyleafPlaybackActions.CanSeek(p)) p.SeekForward(); return null; });
        jumpButton.Click += (_, _) => Jump();
        jump.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Jump(); e.Handled = true; } };
        speedSet.Click += (_, _) => SetSpeed();
        speed.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetSpeed(); e.Handled = true; } };
        speedPresets.SelectionChanged += (_, _) =>
        {
            if (speedPresets.SelectedItem is not ComboBoxItem { Tag: double rate }) return;
            speedPresets.SelectedIndex = 0;
            ApplySpeed(rate);
        };
        speedPresets.DropDownOpened += (_, _) => speedActivity = FlyleafPopupActivity.Hold(player);
        speedPresets.DropDownClosed += (_, _) =>
        {
            speedActivity?.Dispose();
            speedActivity = null;
        };
        repeat.Click += (_, _) => { if (!refreshing && player is { IsDisposed: false } target) target.LoopPlayback = repeat.IsChecked == true; };
        reverse.Click += (_, _) =>
        {
            if (refreshing) return;
            var value = reverse.IsChecked == true;
            Run(p => { if (!value || FlyleafPlaybackActions.CanReverse(p)) p.ReversePlayback = value; return null; });
        };
        previousFrame.Click += (_, _) => Run(p => { if (FlyleafPlaybackActions.CanStep(p)) p.ShowFramePrev(); return null; });
        nextFrame.Click += (_, _) => Run(p => { if (FlyleafPlaybackActions.CanStep(p)) p.ShowFrameNext(); return null; });
        record.Click += (_, _) => Record();
        snapshot.Click += (_, _) => Snapshot();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    static TextBox Input(string value)
    {
        var input = new TextBox { Text = value, MinWidth = 55, VerticalContentAlignment = VerticalAlignment.Center };
        input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        return input;
    }
    static Grid Row(params FrameworkElement[] controls)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < controls.Length; i++)
        {
            row.ColumnDefinitions.Add(new() { Width = controls[i] is TextBox || controls.Length == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            if (i > 0) controls[i].Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(controls[i], i); row.Children.Add(controls[i]);
        }
        return row;
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
        speedPresets.IsDropDownOpen = false;
        shownSpeed = null;
        if (player != null)
        {
            player.PropertyChanged -= PlayerChanged;
            player.Video.PropertyChanged -= VideoChanged;
        }
        player = null;
    }
    void AttachPlayer()
    {
        DetachPlayer(); player = observedContext?.Player;
        if (player != null)
        {
            player.PropertyChanged += PlayerChanged;
            player.Video.PropertyChanged += VideoChanged;
        }
        shownChapters.Clear(); chapters.Children.Clear(); feedback.Text = "";
        jump.Text = "00:00:00"; Refresh();
    }
    void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Player.CurTime))
        {
            if (Dispatcher.CheckAccess())
            {
                if (IsLoaded && ReferenceEquals(sender, player)) RefreshPosition(timeOnly: true);
            }
            else Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsLoaded && ReferenceEquals(sender, player)) RefreshPosition(timeOnly: true);
            }));
            return;
        }
        if (e.PropertyName is nameof(Player.Status) or nameof(Player.CanPlay) or nameof(Player.IsPlaying) or nameof(Player.IsDisposed)
            or nameof(Player.Speed) or nameof(Player.ReversePlayback) or nameof(Player.LoopPlayback) or nameof(Player.IsRecording)
            or nameof(Player.Chapters) or nameof(Player.Duration) or nameof(Player.IsLive)) QueueRefresh();
    }
    void VideoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Video.IsOpened)) QueueRefresh();
    }
    void QueueRefresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(QueueRefresh)); return; }
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { refreshQueued = false; if (IsLoaded) Refresh(); }));
    }
    static string Time(long ticks) => TimeSpan.FromTicks(Math.Max(0, ticks)).ToString(ticks >= TimeSpan.TicksPerDay ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss", CultureInfo.InvariantCulture);
    void RefreshPosition(bool timeOnly = false)
    {
        var target = player;
        var playable = target is { IsDisposed: false, CanPlay: true };
        var ticks = playable ? Math.Max(0, target!.CurTime) : 0;
        var seconds = playable ? ticks / TimeSpan.TicksPerSecond : -1;
        if (timeOnly && seconds == shownPositionSecond) return;
        shownPositionSecond = seconds;
        position.Text = playable
            ? $"{Time(ticks)} / {Time(target!.Duration)}{(target.IsLive ? " · Live" : "")}" : "No playable media";
    }
    void Refresh()
    {
        refreshing = true;
        var available = player is { IsDisposed: false };
        var playable = FlyleafPlaybackActions.CanPlay(player) && !busy;
        play.IsEnabled = playable; play.Content = player?.IsPlaying == true ? "Pause" : "Play";
        stop.IsEnabled = playable && player?.Status != Status.Stopped;
        backward.IsEnabled = forward.IsEnabled = jump.IsEnabled = jumpButton.IsEnabled = FlyleafPlaybackActions.CanSeek(player) && !busy;
        speed.IsEnabled = speedSet.IsEnabled = speedPresets.IsEnabled = playable;
        var rate = available ? player!.Speed : 1;
        if (shownSpeed != rate)
        {
            shownSpeed = rate;
            speed.Text = SpeedText(rate);
        }
        repeat.IsEnabled = available && !busy; repeat.IsChecked = available && player!.LoopPlayback;
        reverse.IsEnabled = !busy && (FlyleafPlaybackActions.CanReverse(player) || available && player!.ReversePlayback);
        reverse.IsChecked = available && player!.ReversePlayback;
        previousFrame.IsEnabled = nextFrame.IsEnabled = FlyleafPlaybackActions.CanStep(player) && !busy;
        record.IsEnabled = !busy && (playable || available && player!.IsRecording);
        record.Content = available && player!.IsRecording ? "Stop recording" : "Start recording…";
        snapshot.IsEnabled = playable && player!.Video.IsOpened && player.Renderer != null;
        chapters.IsEnabled = playable;
        RefreshPosition(); RefreshChapters(); refreshing = false;
    }
    void RefreshChapters()
    {
        var current = player is { IsDisposed: false } ? player.Chapters?.ToArray() ?? [] : [];
        noChapters.Visibility = current.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (shownChapters.SequenceEqual(current)) return;
        chapters.Children.Clear(); shownChapters.Clear(); shownChapters.AddRange(current);
        foreach (var chapter in current)
        {
            var button = Button("");
            button.Content = new TextBlock { Text = $"{Time(chapter.StartTime)} · {chapter.Title}", TextWrapping = TextWrapping.Wrap };
            button.Margin = new Thickness(0, 4, 0, 0);
            button.Click += (_, _) => Run(p => { if (p.Chapters.Contains(chapter)) p.SeekToChapter(chapter); return null; });
            chapters.Children.Add(button);
        }
    }
    static string SpeedText(double rate) => rate.ToString("0.###", CultureInfo.CurrentCulture);

    void SetSpeed()
    {
        if (!(double.TryParse(speed.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var rate)
            || double.TryParse(speed.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rate))
            || !double.IsFinite(rate) || rate < 0.125 || rate > 16)
        {
            feedback.Text = "Enter a multiplier between 0.125 and 16.";
            return;
        }
        ApplySpeed(rate);
    }
    void ApplySpeed(double rate)
    {
        if (busy || !FlyleafPlaybackActions.CanPlay(player)) return;
        if (shownSpeed == Math.Round(rate, 3)) speed.Text = SpeedText(shownSpeed.Value);
        Run(p => { p.Speed = rate; return null; });
    }
    void Jump()
    {
        if (!FlyleafPlaybackActions.CanSeek(player)) return;
        if (!TimeSpan.TryParseExact(jump.Text, [@"h\:mm\:ss", @"hh\:mm\:ss", @"d\.hh\:mm\:ss"], CultureInfo.InvariantCulture, out var time)
            || time < TimeSpan.Zero || time.Ticks > player!.Duration || time.TotalMilliseconds > int.MaxValue)
        { feedback.Text = "Enter a time within the media duration, such as 00:12:30."; return; }
        var milliseconds = (int)time.TotalMilliseconds;
        Run(p => { if (p.Config.Playback.SeekAccurate) p.SeekAccurate(milliseconds); else p.Seek(milliseconds, time.Ticks > p.CurTime); return null; });
    }
    async void Run(Func<Player, string?> action)
    {
        if (busy || player is not { IsDisposed: false } target) return;
        var source = target.Playlist.Selected;
        busy = true; feedback.Text = ""; Refresh();
        using var hold = FlyleafPopupActivity.Hold(target);
        try
        {
            var result = await Task.Run(() => target.IsDisposed || !ReferenceEquals(player, target) || !ReferenceEquals(source, target.Playlist.Selected) ? null : action(target));
            if (IsLoaded && ReferenceEquals(player, target) && ReferenceEquals(source, target.Playlist.Selected)) feedback.Text = result ?? "";
        }
        catch (Exception ex) { if (IsLoaded && ReferenceEquals(player, target)) feedback.Text = ex.Message; }
        finally { busy = false; if (IsLoaded) Refresh(); }
    }
    void Record()
    {
        if (player is not { IsDisposed: false } target) return;
        if (target.IsRecording) { Run(p => { p.StopRecording(); return p.IsRecording ? "Recording could not be stopped." : "Recording stopped."; }); return; }
        if (!target.CanPlay) return;
        var source = target.Playlist.Selected;
        var extension = target.MainDemuxer.Extension;
        if (string.IsNullOrEmpty(extension)) extension = "mkv";
        var dialog = new SaveFileDialog { Title = "Record media", Filter = $"Media recording|*.{extension}|All files|*.*", FileName = "Recording", DefaultExt = "." + extension, InitialDirectory = target.Config.Playback.FolderRecordings };
        using var hold = FlyleafPopupActivity.Hold(target);
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || !ReferenceEquals(player, target) || target.IsDisposed || !ReferenceEquals(source, target.Playlist.Selected)) return;
        var path = dialog.FileName;
        Run(p => { var filename = path; p.StartRecording(ref filename, useRecommendedExtension: false); return p.IsRecording ? $"Recording to {filename}" : "Recording could not be started."; });
    }
    void Snapshot()
    {
        if (player is not { IsDisposed: false, CanPlay: true } target || !target.Video.IsOpened) return;
        var source = target.Playlist.Selected;
        var preferredFormat = target.Config.Playback.SnapshotFormat?.ToLowerInvariant();
        var filterIndex = preferredFormat switch { "jpg" or "jpeg" => 2, "bmp" => 3, _ => 1 };
        var defaultExtension = filterIndex switch { 2 => ".jpg", 3 => ".bmp", _ => ".png" };
        var dialog = new SaveFileDialog { Title = "Save snapshot", Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap image|*.bmp", FilterIndex = filterIndex, DefaultExt = defaultExtension, FileName = "Snapshot", InitialDirectory = target.Config.Playback.FolderSnapshots };
        using var hold = FlyleafPopupActivity.Hold(target);
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || !ReferenceEquals(player, target) || target.IsDisposed || !ReferenceEquals(source, target.Playlist.Selected)) return;
        var filename = dialog.FileName;
        var format = Path.GetExtension(filename).ToLowerInvariant() switch { ".png" => ImageFormat.Png, ".jpg" or ".jpeg" => ImageFormat.Jpeg, ".bmp" => ImageFormat.Bmp, _ => null };
        if (format == null) { feedback.Text = "Choose a PNG, JPEG or BMP filename."; return; }
        Run(p =>
        {
            p.TakeSnapshotToFile(filename);
            return null;
        });
    }
}
