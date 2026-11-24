using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

internal readonly record struct FlyleafPlaybackDisplay(Status Status, bool CanPlay, ActivityMode Activity, long Time, long Duration, bool Live, bool Recording, bool BarVisible, bool ClockAvailable = true);
/// <summary>Playback operations, errors, recording and keyboard time feedback have independent lifetimes.</summary>
public sealed class FlyleafPlaybackStatusOverlay : Grid
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(nameof(Options), typeof(FlyleafOSDOptions), typeof(FlyleafPlaybackStatusOverlay), new PropertyMetadata(null, OptionsChanged));
    public FlyleafOSDOptions? Options { get => (FlyleafOSDOptions?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    readonly Border card = Surface(), clockSurface = Surface(), recordSurface = Surface(), busySurface = Surface(), queueSurface = Surface();
    readonly TextBlock caption = Label(), clock = Label(), recording = Label(), busyLabel = Label(), queueLabel = Label();
    readonly Grid corners = new() { IsHitTestVisible = false };
    readonly StackPanel[] cornerPanels = new StackPanel[4];
    readonly StackPanel corner = new(), messages = new(), errorActions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Grid timedStatusHost = new();
    readonly DispatcherTimer busyDelay, seekReveal, errorTimeout;
    static readonly FlyleafOSDOptions defaults = new();
    FlyleafPlaybackDisplay display = Empty;
    static FlyleafPlaybackDisplay Empty => new(Status.Stopped, false, ActivityMode.Idle, 0, 0, false, false, false);
    string busy = "", blockingError = "", transientError = "";
    bool busyVisible, buffering, seeking, obscured, dismissedFailure, queueOpening;
    long seekTime;
    public event EventHandler? ErrorDetailsRequested;
    internal string StatusText => caption.Text.Length > 0 ? caption.Text : busyLabel.Text;
    internal string TimeText => clock.Text;
    internal bool TimeVisible => clockSurface.Visibility == Visibility.Visible;
    internal bool StatusVisible => card.Visibility == Visibility.Visible || busySurface.Visibility == Visibility.Visible;
    public FlyleafPlaybackStatusOverlay()
    {
        Focusable = false;
        card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center; card.MaxWidth = 460; card.Margin = new Thickness(24);
        var body = new StackPanel(); caption.TextAlignment = TextAlignment.Center; caption.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.ErrorBrush"); body.Children.Add(caption);
        var details = FlyleafPanelUI.Button("Error details"); details.Click += (_, _) => ErrorDetailsRequested?.Invoke(this, EventArgs.Empty);
        var dismiss = FlyleafPanelUI.Button("Dismiss"); dismiss.Margin = new Thickness(8, 0, 0, 0); dismiss.Click += (_, _) => { blockingError = ""; dismissedFailure = true; Refresh(); };
        errorActions.Margin = new Thickness(0, 12, 0, 0); errorActions.Children.Add(details); errorActions.Children.Add(dismiss); body.Children.Add(errorActions); card.Child = body;
        // The corner layout respects bars; the error card stays centered on the entire surface.
        corners.ColumnDefinitions.Add(new ColumnDefinition()); corners.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < cornerPanels.Length; i++)
        {
            bool left = i % 2 == 0;
            var panel = cornerPanels[i] = new StackPanel { HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = i < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom };
            SetColumn(panel, left ? 0 : 1); corners.Children.Add(panel);
        }
        Children.Add(corners); Children.Add(card);
        queueLabel.TextWrapping = TextWrapping.NoWrap; queueLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        queueSurface.Child = queueLabel; queueSurface.MaxWidth = 480; queueSurface.Margin = new Thickness(0, 0, 0, 6); corner.Children.Add(queueSurface);
        Typography.SetNumeralAlignment(clock, FontNumeralAlignment.Tabular);
        clockSurface.Child = clock; clockSurface.Margin = new Thickness(0, 0, 0, 6); corner.Children.Add(clockSurface);
        recordSurface.Child = recording; recordSurface.Margin = new Thickness(0, 0, 0, 6);
        recording.Text = "● REC"; recording.Foreground = Brushes.OrangeRed; corner.Children.Add(recordSurface);
        busySurface.Child = busyLabel; busySurface.Margin = new Thickness(0, 0, 0, 6); busySurface.MaxWidth = 480;
        messages.Children.Add(busySurface); messages.Children.Add(timedStatusHost);
        busyDelay = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
        busyDelay.Tick += (_, _) => { busyDelay.Stop(); busyVisible = true; Refresh(); };
        seekReveal = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        seekReveal.Tick += (_, _) => { seekReveal.Stop(); Refresh(); };
        errorTimeout = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(7) };
        errorTimeout.Tick += (_, _) => { ClearTransientError(); Refresh(); };
        Unloaded += (_, _) => Reset(); Refresh();
    }
    internal void AttachMessages(FlyleafOSDOverlay actions, FlyleafOSDOverlay status)
    {
        status.Margin = new Thickness(0, 0, 0, 6);
        timedStatusHost.Children.Add(status); messages.Children.Add(actions);
        Refresh();
    }
    internal void SetInsets(Thickness insets)
    {
        if (corners.Margin != insets) corners.Margin = insets;
    }
    void PositionCorners(FlyleafOSDOptions o)
    {
        static void Place(StackPanel group, StackPanel target, bool first)
        {
            if (ReferenceEquals(group.Parent, target)) return;
            if (group.Parent is StackPanel old) old.Children.Remove(group);
            if (first) target.Children.Insert(0, group); else target.Children.Add(group);
        }
        var clockCorner = cornerPanels[(int)o.ClockPosition];
        var messageCorner = cornerPanels[(int)o.Position];
        Place(corner, clockCorner, true); Place(messages, messageCorner, false);
        queueSurface.HorizontalAlignment = clockSurface.HorizontalAlignment = recordSurface.HorizontalAlignment = clockCorner.HorizontalAlignment;
        busySurface.HorizontalAlignment = messageCorner.HorizontalAlignment;
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
    internal void UpdateQueue(string text, bool opening)
    {
        bool changed = queueLabel.Text != text || queueOpening != opening;
        queueLabel.Text = text; queueOpening = opening;
        if (changed) Refresh();
    }
    internal void SetBuffering(bool value) { buffering = value; Refresh(); }
    internal void Seek(bool started, int milliseconds)
    {
        seeking = started; seekReveal.Stop(); seekReveal.Start();
        if (started && milliseconds >= 0) seekTime = milliseconds * TimeSpan.TicksPerMillisecond;
        Refresh();
    }
    internal void SetObscured(bool value) { obscured = value; Refresh(); }
    bool HasBlockingError => !dismissedFailure && (blockingError.Length > 0 || display.Status == Status.Failed);
    void ClearTransientError() { errorTimeout.Stop(); transientError = ""; }
    internal void Error(string message, bool blocking = true)
    {
        if (blocking)
        {
            ClearTransientError();
            blockingError = message; dismissedFailure = false;
        }
        else
        {
            if (HasBlockingError || !(Options ?? defaults).ShowErrors) return;
            transientError = message;
            errorTimeout.Stop(); errorTimeout.Start();
        }
        Refresh();
    }
    internal void ClearError() { ClearTransientError(); blockingError = ""; dismissedFailure = false; Refresh(); }
    internal void NewInput() { ClearTransientError(); blockingError = ""; dismissedFailure = false; buffering = seeking = false; seekReveal.Stop(); Refresh(); }
    internal void CancelOperations() { buffering = seeking = false; seekReveal.Stop(); Refresh(); }
    internal void Reset() { busyDelay.Stop(); seekReveal.Stop(); queueLabel.Text = ""; queueOpening = false; ClearTransientError(); busy = blockingError = ""; busyVisible = buffering = seeking = dismissedFailure = false; display = Empty; Refresh(); }
    void Refresh()
    {
        var o = Options ?? defaults;
        string pending = display.Status == Status.Opening ? "Opening…" : seeking ? "Seeking…" : buffering ? "Buffering…" : "";
        if (pending != busy)
        {
            busy = pending; busyVisible = false; busyDelay.Stop();
            if (busy.Length > 0) busyDelay.Start();
        }
        bool failed = o.ShowErrors && HasBlockingError;
        if (transientError.Length > 0 && (HasBlockingError || !o.ShowErrors)) ClearTransientError();
        bool transient = transientError.Length > 0;
        string error = failed ? blockingError.Length > 0 ? blockingError : "Unable to open media." : transientError;
        caption.Text = failed || transient ? error.Length > 220 ? error[..220] + "…" : error : "";
        caption.FontSize = clock.FontSize = recording.FontSize = busyLabel.FontSize = queueLabel.FontSize = o.FontSize;
        card.Visibility = transient || !obscured && failed ? Visibility.Visible : Visibility.Collapsed;
        card.IsHitTestVisible = failed;
        errorActions.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        busyLabel.Text = busy;
        bool showBusy = !obscured && o.ShowStatus && busyVisible && busy.Length > 0;
        busySurface.Visibility = showBusy ? Visibility.Visible : Visibility.Collapsed;
        timedStatusHost.Visibility = showBusy ? Visibility.Collapsed : Visibility.Visible;
        PositionCorners(o);
        bool clockAvailable = display.ClockAvailable && display.Status != Status.Opening && !queueOpening;
        bool showClock = !obscured && clockAvailable && ShowTime(o.TimeDisplay, display.CanPlay, display.Activity, display.BarVisible, seeking || seekReveal.IsEnabled);
        clockSurface.Visibility = showClock ? Visibility.Visible : Visibility.Collapsed;
        clock.Text = clockAvailable && display.CanPlay ? FormatTime(seeking ? seekTime : display.Time, display.Duration, display.Live, o.ShowMilliseconds) : "";
        queueSurface.Visibility = !obscured && queueLabel.Text.Length > 0 && (o.QueueDisplay == FlyleafVisibilityPolicy.Always ||
            o.QueueDisplay == FlyleafVisibilityPolicy.Auto && (display.Activity != ActivityMode.Idle)) ? Visibility.Visible : Visibility.Collapsed;
        recordSurface.Visibility = !obscured && display.Recording ? Visibility.Visible : Visibility.Collapsed;
    }
    internal static bool ShowTime(FlyleafVisibilityPolicy policy, bool canPlay, ActivityMode activity, bool barVisible, bool seek)
        => canPlay && (policy == FlyleafVisibilityPolicy.Always || policy == FlyleafVisibilityPolicy.Auto && !barVisible && (activity != ActivityMode.Idle || seek));
    internal static string FormatTime(long time, long duration, bool live, bool milliseconds = false)
    {
        string Time(long ticks)
        {
            string format = ticks >= TimeSpan.TicksPerDay ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss";
            return TimeSpan.FromTicks(Math.Max(0, ticks)).ToString(milliseconds ? format + @"\.fff" : format);
        }
        return live ? duration > 0 ? $"LIVE · {Time(time)} / {Time(duration)}" : "LIVE" : duration > 0 ? $"{Time(time)} / {Time(duration)}" : Time(time);
    }
}
