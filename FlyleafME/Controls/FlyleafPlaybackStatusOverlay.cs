using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

internal readonly record struct FlyleafPlaybackDisplay(Status Status, bool CanPlay, ActivityMode Activity, long Time, long Duration, bool Live, bool Recording, bool BarVisible);

/// <summary>Playback operations, blocking errors, recording and keyboard time feedback have independent lifetimes.</summary>
public sealed class FlyleafPlaybackStatusOverlay : Grid
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(nameof(Options), typeof(FlyleafOSDOptions), typeof(FlyleafPlaybackStatusOverlay), new PropertyMetadata(null, OptionsChanged));
    public FlyleafOSDOptions? Options { get => (FlyleafOSDOptions?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    readonly Border card = Surface(), clockSurface = Surface();
    readonly TextBlock caption = Label(), clock = Label(), recording = Label();
    readonly StackPanel corner = new(), errorActions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    readonly DispatcherTimer busyDelay, seekReveal;
    static readonly FlyleafOSDOptions defaults = new();
    FlyleafPlaybackDisplay display = Empty;
    static FlyleafPlaybackDisplay Empty => new(Status.Stopped, false, ActivityMode.Idle, 0, 0, false, false, false);
    string busy = "", blockingError = "";
    bool busyVisible, buffering, seeking, obscured, dismissedFailure;
    long seekTime;
    public event EventHandler? ErrorDetailsRequested;
    internal string StatusText => caption.Text;
    internal string TimeText => clock.Text;
    internal bool TimeVisible => clockSurface.Visibility == Visibility.Visible;
    internal bool StatusVisible => card.Visibility == Visibility.Visible;
    public FlyleafPlaybackStatusOverlay()
    {
        Focusable = false;
        card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center; card.MaxWidth = 460; card.Margin = new Thickness(24);
        var body = new StackPanel(); caption.TextAlignment = TextAlignment.Center; body.Children.Add(caption);
        var details = FlyleafPanelUI.Button("Error details"); details.Click += (_, _) => ErrorDetailsRequested?.Invoke(this, EventArgs.Empty);
        var dismiss = FlyleafPanelUI.Button("Dismiss"); dismiss.Margin = new Thickness(8, 0, 0, 0); dismiss.Click += (_, _) => { blockingError = ""; dismissedFailure = true; Refresh(); };
        errorActions.Margin = new Thickness(0, 12, 0, 0); errorActions.Children.Add(details); errorActions.Children.Add(dismiss); body.Children.Add(errorActions); card.Child = body;
        Children.Add(card);
        corner.IsHitTestVisible = false; corner.HorizontalAlignment = HorizontalAlignment.Left; corner.VerticalAlignment = VerticalAlignment.Top;
        clockSurface.Child = clock; corner.Children.Add(clockSurface);
        var recordSurface = Surface(); recordSurface.Margin = new Thickness(0, 6, 0, 0); recordSurface.Child = recording;
        recording.Text = "● REC"; recording.Foreground = Brushes.OrangeRed; corner.Children.Add(recordSurface); Children.Add(corner);
        busyDelay = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
        busyDelay.Tick += (_, _) => { busyDelay.Stop(); busyVisible = true; Refresh(); };
        seekReveal = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        seekReveal.Tick += (_, _) => { seekReveal.Stop(); Refresh(); };
        Unloaded += (_, _) => Reset(); Refresh();
    }
    static Border Surface()
    {
        var result = new Border { Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(4) };
        result.SetResourceReference(Border.BackgroundProperty, "FlyleafME.OverlayBrush"); return result;
    }
    static TextBlock Label()
    {
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        result.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.OverlayTextBrush"); return result;
    }
    static void OptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (FlyleafPlaybackStatusOverlay)d;
        if (e.OldValue is FlyleafOSDOptions old) PropertyChangedEventManager.RemoveHandler(old, overlay.Changed, "");
        if (e.NewValue is FlyleafOSDOptions current) PropertyChangedEventManager.AddHandler(current, overlay.Changed, "");
        overlay.Refresh();
    }
    void Changed(object? sender, PropertyChangedEventArgs e) { if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(new Action(Refresh)); }
    internal void Update(FlyleafPlaybackDisplay value) { display = value; Refresh(); }
    internal void SetBuffering(bool value) { buffering = value; Refresh(); }
    internal void Seek(bool started, int milliseconds)
    {
        seeking = started; seekReveal.Stop(); seekReveal.Start();
        if (started && milliseconds >= 0) seekTime = milliseconds * TimeSpan.TicksPerMillisecond;
        Refresh();
    }
    internal void SetObscured(bool value) { obscured = value; Refresh(); }
    internal void Error(string message) { blockingError = message; dismissedFailure = false; busyDelay.Stop(); Refresh(); }
    internal void NewInput() { blockingError = ""; dismissedFailure = false; buffering = seeking = false; seekReveal.Stop(); Refresh(); }
    internal void CancelOperations() { buffering = seeking = false; seekReveal.Stop(); Refresh(); }
    internal void Reset() { busyDelay.Stop(); seekReveal.Stop(); busy = blockingError = ""; busyVisible = buffering = seeking = dismissedFailure = false; display = Empty; Refresh(); }
    void Refresh()
    {
        var o = Options ?? defaults;
        string pending = display.Status == Status.Opening ? "Opening…" : seeking ? "Seeking…" : buffering ? "Buffering…" : "";
        if (pending != busy)
        {
            busy = pending; busyVisible = false; busyDelay.Stop();
            if (busy.Length > 0) busyDelay.Start();
        }
        bool failed = o.ShowErrors && !dismissedFailure && (blockingError.Length > 0 || display.Status == Status.Failed);
        caption.Text = failed ? blockingError.Length > 0 ? (blockingError.Length > 220 ? blockingError[..220] + "…" : blockingError) : "Unable to open media." : busy;
        caption.SetResourceReference(TextBlock.ForegroundProperty, failed ? "FlyleafME.ErrorBrush" : "FlyleafME.OverlayTextBrush");
        caption.FontSize = clock.FontSize = o.FontSize;
        card.Visibility = !obscured && (failed || o.ShowStatus && busyVisible && busy.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
        errorActions.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        // Keep time opposite top-corner action messages. Bottom-corner messages leave time at top left.
        corner.HorizontalAlignment = o.Position == FlyleafOSDPosition.TopLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        clock.Text = FormatTime(seeking ? seekTime : display.Time, display.Duration, display.Live);
        clockSurface.Visibility = !obscured && ShowTime(o.TimeDisplay, display.CanPlay, display.Activity, display.BarVisible, seeking || seekReveal.IsEnabled) ? Visibility.Visible : Visibility.Collapsed;
        ((Border)corner.Children[1]).Visibility = !obscured && display.Recording ? Visibility.Visible : Visibility.Collapsed;
    }
    internal static bool ShowTime(FlyleafVisibilityPolicy policy, bool canPlay, ActivityMode activity, bool barVisible, bool seek)
        => canPlay && (policy == FlyleafVisibilityPolicy.Always || policy == FlyleafVisibilityPolicy.Auto && !barVisible && (activity == ActivityMode.Active || seek));
    internal static string FormatTime(long time, long duration, bool live)
    {
        static string Time(long ticks) => TimeSpan.FromTicks(Math.Max(0, ticks)).ToString(ticks >= TimeSpan.TicksPerDay ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss");
        return live ? duration > 0 ? $"LIVE · {Time(time)} / {Time(duration)}" : "LIVE" : duration > 0 ? $"{Time(time)} / {Time(duration)}" : Time(time);
    }
}
