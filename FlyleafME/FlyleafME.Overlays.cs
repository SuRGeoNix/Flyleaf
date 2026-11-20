using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    readonly FlyleafSubtitleText subtitleText = new();
    readonly FlyleafOSDOverlay osd = new();
    static readonly Rect fullViewport = new(0, 0, 1, 1);
    Player? overlayPlayer;
    Renderer? overlayRenderer;
    bool subtitleRefreshQueued;
    int textPlayerGeneration;

    void InitializeTextOverlays()
    {
        subtitleText.SetBinding(FlyleafSubtitleText.OptionsProperty, new Binding("UIConfig.Subtitles") { Source = this });
        osd.SetBinding(FlyleafOSDOverlay.OptionsProperty, new Binding("UIConfig.OSD") { Source = this });
        playbackDisplay.SetBinding(FlyleafPlaybackStatusOverlay.OptionsProperty, new Binding("UIConfig.OSD") { Source = this });
        SetZIndex(subtitleText, -1); overlayRoot.Children.Add(subtitleText);
        // Messages remain visible over the Settings preview, while never intercepting input.
        SetZIndex(osd, 30); overlayRoot.Children.Add(osd);
        SetZIndex(playbackDisplay, 31); overlayRoot.Children.Add(playbackDisplay);
        playbackDisplay.ErrorDetailsRequested += (_, _) => { if (panelDefinitions.ContainsKey("information")) ShowPanel("information"); };
        overlayRoot.SizeChanged += (_, e) =>
        {
            UpdateOSDMargin();
            UpdateStatisticsViewport(e.NewSize);
        };
        UpdateOSDMargin();
    }

    void UpdateOSDMargin()
    {
        double header = overlayHeader.Content != null ? overlayHeader.ActualHeight : 0;
        osd.Margin = playbackDisplay.Margin = new Thickness(12, header + 12, 12, 80);
    }

    void UpdateStatisticsViewport(Size surface)
    {
        var viewport = subtitleText.VideoViewport;
        var picture = new Rect(viewport.X * surface.Width, viewport.Y * surface.Height,
            viewport.Width * surface.Width, viewport.Height * surface.Height);
        picture.Intersect(new Rect(surface));
        if (picture.IsEmpty) picture = new Rect();

        Canvas.SetLeft(statisticsViewport, picture.X);
        Canvas.SetTop(statisticsViewport, picture.Y);
        statisticsViewport.Width = picture.Width;
        statisticsViewport.Height = picture.Height;
    }

    internal void UpdateVideoViewport(Rect viewport)
    {
        subtitleText.VideoViewport = viewport;
        UpdateStatisticsViewport(overlayRoot.RenderSize);
    }

    static bool TryGetVideoViewport(Renderer renderer, out Rect viewport)
    {
        var pixels = renderer.Viewport;
        int width = renderer.ControlWidth, height = renderer.ControlHeight;
        if (width <= 0 || height <= 0 || pixels.Width <= 0 || pixels.Height <= 0)
        {
            viewport = default;
            return false;
        }
        viewport = new Rect(pixels.X / (double)width, pixels.Y / (double)height,
            pixels.Width / (double)width, pixels.Height / (double)height);
        return true;
    }

    /// <summary>Display a transient message using this control's OSD preferences. Safe from a worker thread.</summary>
    public void ShowOSD(string message)
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowOSD(message))); return; }
        if (!disposed && IsLoaded) osd.ShowMessage(message);
    }

    void ObserveTextPlayer(Player? target)
    {
        Interlocked.Increment(ref textPlayerGeneration);
        ObservePlaybackFeedback(false); playbackDisplay.Reset();
        if (overlayPlayer != null)
        {
            overlayPlayer.PropertyChanged -= TextPlayerChanged;
            overlayPlayer.Audio.PropertyChanged -= TextAudioChanged;
            overlayPlayer.Subtitles.PropertyChanged -= TextSubtitlesChanged;
        }
        ObserveTextRenderer(null); overlayPlayer = target; osd.Clear(); subtitleText.Text = "";
        if (target != null)
        {
            target.PropertyChanged += TextPlayerChanged;
            target.Audio.PropertyChanged += TextAudioChanged;
            target.Subtitles.PropertyChanged += TextSubtitlesChanged;
            ObserveTextRenderer(target.Renderer);
            ObservePlaybackFeedback(true);
        }
        RefreshSubtitleText();
        UpdatePlaybackDisplay();
    }
    void ObserveTextRenderer(Renderer? target)
    {
        if (overlayRenderer == target) return;
        if (overlayRenderer != null) { overlayRenderer.PropertyChanged -= TextRendererChanged; overlayRenderer.ViewportChanged -= TextViewportChanged; }
        overlayRenderer = target;
        if (target != null) { target.PropertyChanged += TextRendererChanged; target.ViewportChanged += TextViewportChanged; }
        UpdateVideoViewport(target != null && TryGetVideoViewport(target, out var viewport) ? viewport : fullViewport);
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
        if (e.PropertyName is nameof(Subtitles.SubsText) or nameof(Subtitles.Enabled) or nameof(Subtitles.IsOpened)) QueueSubtitleText();
        if (e.PropertyName == nameof(Subtitles.Delay)) Feedback(sender, () => $"Subtitle delay {overlayPlayer!.Subtitles.Delay / (double)TimeSpan.TicksPerMillisecond:0.###} ms", playbackOnly: true);
        if (e.PropertyName == nameof(Subtitles.Enabled)) Feedback(sender, () => overlayPlayer!.Subtitles.Enabled ? "Subtitles on" : "Subtitles off", playbackOnly: true);
    }
    void TextRendererChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Renderer.Rotation)) Feedback(sender, () => $"Rotation {overlayRenderer!.Rotation}°", playbackOnly: true);
        if (e.PropertyName == nameof(Renderer.Zoom)) Feedback(sender, () => $"Zoom {overlayRenderer!.Zoom:0.###}%", playbackOnly: true);
        if (e.PropertyName == nameof(Renderer.AspectRatio)) Feedback(sender, () => $"Aspect ratio · {overlayRenderer!.AspectRatio}", playbackOnly: true);
        if (e.PropertyName == nameof(Renderer.HFlip)) Feedback(sender, () => overlayRenderer!.HFlip ? "Horizontal flip on" : "Horizontal flip off", playbackOnly: true);
        if (e.PropertyName == nameof(Renderer.VFlip)) Feedback(sender, () => overlayRenderer!.VFlip ? "Vertical flip on" : "Vertical flip off", playbackOnly: true);
    }
    void TextViewportChanged(object? sender, EventArgs e)
    {
        // Capture the picture and its matching surface size in the renderer callback.
        if (Dispatcher.HasShutdownStarted || sender is not Renderer renderer || !TryGetVideoViewport(renderer, out var viewport)) return;
        int generation = Volatile.Read(ref textPlayerGeneration);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!disposed && generation == Volatile.Read(ref textPlayerGeneration) && ReferenceEquals(renderer, overlayRenderer))
                UpdateVideoViewport(viewport);
        }));
    }
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
        if (Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(QueueSubtitleText)); return; }
        if (subtitleRefreshQueued) return; subtitleRefreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { subtitleRefreshQueued = false; if (!disposed && IsLoaded) RefreshSubtitleText(); }));
    }
    void RefreshSubtitleText()
    {
        ObserveTextRenderer(overlayPlayer is { IsDisposed: false } ? overlayPlayer.Renderer : null);
        subtitleText.Text = overlayPlayer is { IsDisposed: false, Subtitles: { Enabled: true, IsOpened: true } subtitles } ? subtitles.SubsText ?? "" : "";
        UpdateOSDMargin();
    }
}
