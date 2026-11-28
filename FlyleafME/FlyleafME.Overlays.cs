using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;

namespace Flyleaf.Controls.WPF;
public partial class FlyleafME
{
    readonly FlyleafSubtitleText subtitleText = new();
    readonly FlyleafOSDOverlay osd = new(), statusOsd = new();
    Player? overlayPlayer;
    Renderer? overlayRenderer;
    int subtitleRefreshQueued;
    bool subtitleBarLayoutObserved;
    int textPlayerGeneration;
    void InitializeTextOverlays()
    {
        subtitleText.SetBinding(FlyleafSubtitleText.OptionsProperty, new Binding("UIConfig.Subtitles") { Source = this });
        subtitleText.DisplayScaleChanged += UpdateSubtitleRenderStyle;
        osd.SetBinding(FlyleafOSDOverlay.OptionsProperty, new Binding("UIConfig.OSD") { Source = this });
        statusOsd.SetBinding(FlyleafOSDOverlay.OptionsProperty, new Binding("UIConfig.OSD") { Source = this });
        playbackDisplay.SetBinding(FlyleafPlaybackStatusOverlay.OptionsProperty, new Binding("UIConfig.OSD") { Source = this });
        SetZIndex(subtitleText, -1); overlayRoot.Children.Add(subtitleText);
        playbackDisplay.AttachMessages(osd, statusOsd);
        SetZIndex(playbackDisplay, 1); overlayRoot.Children.Add(playbackDisplay);
        playbackDisplay.ErrorDetailsRequested += (_, _) => { if (panelDefinitions.ContainsKey("information")) ShowPanel("information"); };
        overlayRoot.SizeChanged += (_, _) => UpdateOSDMargin();
        InitializeOSDBarObservation();
        UpdateOSDMargin();
    }

    /// <summary>Display a transient message using this control's OSD preferences. Safe from a worker thread.</summary>
    public void ShowOSD(string message)
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowOSD(message))); return; }
        if (!disposed && IsLoaded) osd.ShowMessage(message);
    }
    /// <summary>Display a transient error using this control's OSD preferences and report it to the Information panel. Safe from a worker thread.</summary>
    public void ShowError(string message)
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowError(message))); return; }
        if (!disposed && IsLoaded) ReportPlaybackError(message, blocking: false);
    }
    void ObserveTextPlayer(Player? target)
    {
        Interlocked.Increment(ref textPlayerGeneration);
        ObservePlaybackFeedback(false); playbackDisplay.Reset();
        if (overlayPlayer != null)
        {
            overlayPlayer.PropertyChanged -= TextPlayerChanged;
            overlayPlayer.Audio.PropertyChanged -= TextAudioChanged;
            overlayPlayer.Video.PropertyChanged -= TextVideoChanged;
            overlayPlayer.Subtitles.PropertyChanged -= TextSubtitlesChanged;
        }
        ObserveTextRenderer(null); overlayPlayer = target; osd.Clear(); statusOsd.Clear(); subtitleText.Text = "";
        if (target != null)
        {
            target.PropertyChanged += TextPlayerChanged;
            target.Audio.PropertyChanged += TextAudioChanged;
            target.Video.PropertyChanged += TextVideoChanged;
            target.Subtitles.PropertyChanged += TextSubtitlesChanged;
            UpdateSubtitleRenderStyle();
            ObserveTextRenderer(target.Renderer);
            ObservePlaybackFeedback(true);
        }
        RefreshSubtitleText();
        UpdatePlaybackDisplay();
    }

    void UpdateSubtitleRenderStyle()
    {
        if (overlayPlayer is not { IsDisposed: false } target) return;
        var options = UIConfig.Subtitles;
        target.Subtitles.RenderStyle = new SubtitlesRenderStyle(options.FontFamily, options.FontSize,
            options.Bold, options.Italic, ParseColor(options.Color), ParseColor(options.OutlineColor),
            options.OutlineThickness, options.BottomOffset, options.WithinVideo)
        {
            PixelsPerDip = System.Windows.Media.VisualTreeHelper.GetDpi(subtitleText).PixelsPerDip
        };

        static System.Drawing.Color ParseColor(string value)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
    void ObserveTextRenderer(Renderer? target)
    {
        if (overlayRenderer == target) return;
        if (overlayRenderer != null)
        {
            overlayRenderer.PropertyChanged -= TextRendererChanged;
            overlayRenderer.ViewportChanged -= TextViewportChanged;
            overlayRenderer.SetBitmapSubtitleObstruction(default);
        }
        overlayRenderer = target;
        if (target != null) { target.PropertyChanged += TextRendererChanged; target.ViewportChanged += TextViewportChanged; }
    }
    void TextPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        DisplayPlayerProperty(sender, e);
        if (e.PropertyName is nameof(Player.Renderer) or nameof(Player.IsDisposed)) QueueSubtitleText();
        if (e.PropertyName == nameof(Player.Speed)) Feedback(sender, () => $"Speed ×{overlayPlayer!.Speed:0.###}");
        if (e.PropertyName == nameof(Player.IsRecording)) Feedback(sender, () => overlayPlayer!.IsRecording ? "Recording started" : "Recording stopped");
    }
    void TextAudioChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Audio.Enabled): Feedback(sender, () => overlayPlayer!.Audio.Enabled ? "Audio on" : "Audio off", playbackOnly: true); break;
            case nameof(Audio.Volume): Feedback(sender, () => $"Volume {overlayPlayer!.Audio.Volume}%"); break;
            case nameof(Audio.Mute): Feedback(sender, () => overlayPlayer!.Audio.Mute ? "Muted" : $"Unmuted · {overlayPlayer.Audio.Volume}%"); break;
            case nameof(Audio.Delay): Feedback(sender, () => $"Audio delay {overlayPlayer!.Audio.Delay / (double)TimeSpan.TicksPerMillisecond:0.###} ms", playbackOnly: true); break;
        }
    }
    void TextSubtitlesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Subtitles.SubsText) or nameof(Subtitles.Enabled) or nameof(Subtitles.IsOpened) or nameof(Subtitles.SubsRenderer))
            QueueSubtitleText();
        if (e.PropertyName == nameof(Subtitles.Delay)) Feedback(sender, () => $"Subtitle delay {overlayPlayer!.Subtitles.Delay / (double)TimeSpan.TicksPerMillisecond:0.###} ms", playbackOnly: true);
        if (e.PropertyName == nameof(Subtitles.Enabled)) Feedback(sender, () => overlayPlayer!.Subtitles.Enabled ? "Subtitles on" : "Subtitles off", playbackOnly: true);
    }
    void TextVideoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Video.Stream)) QueueSubtitleText();
    }
    void TextRendererChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Renderer.Rotation)) Feedback(sender, () => $"Rotation {overlayRenderer!.Rotation}°", playbackOnly: true);
        if (e.PropertyName == nameof(Renderer.Zoom)) Feedback(sender, () => $"Zoom {overlayRenderer!.Zoom:0.###}%", playbackOnly: true);
        if (e.PropertyName == nameof(Renderer.AspectRatio)) Feedback(sender, () => $"Aspect ratio · {overlayRenderer!.AspectRatio}", playbackOnly: true);
        if (e.PropertyName == nameof(Renderer.HFlip)) Feedback(sender, () => overlayRenderer!.HFlip ? "Horizontal flip on" : "Horizontal flip off", playbackOnly: true);
        if (e.PropertyName == nameof(Renderer.VFlip)) Feedback(sender, () => overlayRenderer!.VFlip ? "Vertical flip on" : "Vertical flip off", playbackOnly: true);
    }
    void TextViewportChanged(object? sender, EventArgs e) => QueueSubtitleText();
    void Feedback(object? source, Func<string> text, bool playbackOnly = false)
    {
        if (Dispatcher.HasShutdownStarted) return;
        // Capture the owner before queueing; old-player events cannot become new-player messages.
        var expected = overlayPlayer;
        int generation = Volatile.Read(ref textPlayerGeneration);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (disposed || !IsLoaded || generation != Volatile.Read(ref textPlayerGeneration) || expected == null || expected.IsDisposed || expected != overlayPlayer || expected != Player) return;
            if (!ReferenceEquals(source, expected) && !ReferenceEquals(source, expected.Audio) && !ReferenceEquals(source, expected.Subtitles) && !ReferenceEquals(source, overlayRenderer)) return;
            if (playbackOnly && (!expected.CanPlay || expected.Status == Status.Opening)) return;
            osd.ShowMessage(text());
        }));
    }
    void QueueSubtitleText()
    {
        if (Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref subtitleRefreshQueued, 1) != 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            Interlocked.Exchange(ref subtitleRefreshQueued, 0);
            if (!disposed && IsLoaded) RefreshSubtitleText();
        }));
    }
    void RefreshSubtitleText()
    {
        var player = overlayPlayer is { IsDisposed: false } ? overlayPlayer : null;
        ObserveTextRenderer(player?.Renderer);
        subtitleText.VideoViewport = overlayRenderer is { } renderer && player?.Video.Stream is { } stream
            ? renderer.GetVideoViewport(stream) : Rect.Empty;
        subtitleText.Text = player?.Subtitles is { Enabled: true, IsOpened: true } subtitles ? subtitles.SubsText ?? "" : "";
        UpdateSubtitleBarObservation();
    }

    void UpdateSubtitleBarObservation()
    {
        bool observe = osdBarsObserved && osdBarVisible &&
            (!string.IsNullOrWhiteSpace(subtitleText.Text) ||
                overlayPlayer is { IsDisposed: false, Subtitles: { Enabled: true, IsOpened: true, SubsRenderer.Mode: SubtitlesRenderMode.Overlay or SubtitlesRenderMode.Bitmap } });
        if (subtitleBarLayoutObserved != observe)
        {
            subtitleBarLayoutObserved = observe;
            if (observe) overlayRoot.LayoutUpdated += SubtitleBarLayoutUpdated;
            else overlayRoot.LayoutUpdated -= SubtitleBarLayoutUpdated;
        }
        if (observe) SubtitleBarLayoutUpdated(null, EventArgs.Empty);
        else ApplySubtitleBarBounds(Rect.Empty);
    }

    void SubtitleBarLayoutUpdated(object? sender, EventArgs e)
    {
        var bounds = Rect.Empty;
        if (osdPlaybackBar is FlyleafPlaybackBar bar &&
            bar.RenderedSurface is { IsVisible: true, Opacity: > 0.001, ActualWidth: > 0, ActualHeight: > 0 } surface &&
            overlayRoot.IsAncestorOf(surface))
        {
            bounds = surface.TransformToVisual(subtitleText).TransformBounds(new Rect(surface.RenderSize));
            bounds.Intersect(new Rect(subtitleText.RenderSize));
        }
        ApplySubtitleBarBounds(bounds);
    }

    void ApplySubtitleBarBounds(Rect bounds)
    {
        subtitleText.PlaybackBarBounds = bounds;
        if (overlayRenderer is not { } renderer) return;

        var size = subtitleText.RenderSize;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || size.Width <= 0 || size.Height <= 0)
            renderer.SetBitmapSubtitleObstruction(default);
        else
            renderer.SetBitmapSubtitleObstruction(new System.Drawing.RectangleF((float)(bounds.X / size.Width),
                (float)(bounds.Y / size.Height), (float)(bounds.Width / size.Width), (float)(bounds.Height / size.Height)),
                (float)(FlyleafSubtitleText.PlaybackBarGap / size.Height));
    }
}
