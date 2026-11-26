using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Flyleaf.Controls.WPF;

/// <summary>Passive, wrapped, outlined text. Uses geometry only when text, style or layout changes.</summary>
public sealed class FlyleafSubtitleText : FrameworkElement
{
    const double PlaybackBarGap = 4;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(FlyleafSubtitleText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender, Invalidate));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(nameof(Options), typeof(FlyleafSubtitleOptions), typeof(FlyleafSubtitleText), new PropertyMetadata(null, OptionsChanged));
    public FlyleafSubtitleOptions? Options { get => (FlyleafSubtitleOptions?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    /// <summary>Video rectangle normalized to the playback surface, before clipping to the visible area.</summary>
    public static readonly DependencyProperty VideoViewportProperty = DependencyProperty.Register(nameof(VideoViewport), typeof(Rect), typeof(FlyleafSubtitleText), new FrameworkPropertyMetadata(new Rect(0, 0, 1, 1), FrameworkPropertyMetadataOptions.AffectsRender, Invalidate));
    public Rect VideoViewport { get => (Rect)GetValue(VideoViewportProperty); set => SetValue(VideoViewportProperty, value); }
    static readonly FlyleafSubtitleOptions defaults = new();
    Geometry? geometry;
    Brush fill = Brushes.White;
    Pen? pen;
    Point origin;
    Rect clip, inkBounds;
    Rect playbackBarBounds = Rect.Empty;

    internal Rect PlaybackBarBounds
    {
        get => playbackBarBounds;
        set
        {
            if (playbackBarBounds == value) return;
            playbackBarBounds = value;
            InvalidateVisual();
        }
    }

    public FlyleafSubtitleText() { IsHitTestVisible = false; Focusable = false; }
    static void Invalidate(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((FlyleafSubtitleText)d).Reset();
    static void OptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var text = (FlyleafSubtitleText)d;
        if (e.OldValue is FlyleafSubtitleOptions old) PropertyChangedEventManager.RemoveHandler(old, text.StyleChanged, "");
        if (e.NewValue is FlyleafSubtitleOptions current) PropertyChangedEventManager.AddHandler(current, text.StyleChanged, "");
        text.Reset();
    }
    void StyleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) Reset(); else Dispatcher.BeginInvoke(new Action(Reset));
    }
    void Reset() { geometry = null; InvalidateVisual(); }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); Reset(); }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); Reset(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (string.IsNullOrWhiteSpace(Text) || ActualWidth < 2 || ActualHeight < 2) return;
        if (geometry == null) Build();
        if (geometry == null) return;
        dc.PushClip(new RectangleGeometry(clip));
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y - PlaybackBarLift()));
        // Draw outline behind fill; the fill covers the inward half of the stroke.
        dc.DrawGeometry(fill, pen, geometry); dc.DrawGeometry(fill, null, geometry);
        dc.Pop(); dc.Pop();
    }

    double PlaybackBarLift()
    {
        if (playbackBarBounds.IsEmpty || inkBounds.IsEmpty) return 0;
        var positioned = inkBounds;
        positioned.Offset(origin.X, origin.Y);
        var visible = Rect.Intersect(positioned, clip);
        var overlap = Rect.Intersect(visible, playbackBarBounds);
        if (overlap.IsEmpty || overlap.Width <= 0 || overlap.Height <= 0) return 0;

        // Preserve the chosen viewport even when the space above the bar is too small.
        return Math.Min(visible.Top - clip.Top, positioned.Bottom - playbackBarBounds.Top + PlaybackBarGap);
    }

    void Build()
    {
        var o = Options ?? defaults;
        var area = new Rect(0, 0, ActualWidth, ActualHeight);
        var viewport = VideoViewport;
        if (o.WithinVideo && !viewport.IsEmpty && double.IsFinite(viewport.X) && double.IsFinite(viewport.Y) && double.IsFinite(viewport.Width) && double.IsFinite(viewport.Height))
        {
            var video = new Rect(viewport.X * ActualWidth, viewport.Y * ActualHeight, viewport.Width * ActualWidth, viewport.Height * ActualHeight);
            area.Intersect(video);
        }
        if (area.IsEmpty || area.Width < 2 || area.Height < 2) return;
        double scale = Math.Clamp(0.2 + 0.8 * ActualWidth / 1920, 0.25, 4);
        double stroke = o.OutlineThickness * scale, padding = Math.Max(8, stroke + 2);
        var text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(new FontFamily(o.FontFamily), o.Italic ? FontStyles.Italic : FontStyles.Normal, o.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            o.FontSize * scale, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { TextAlignment = TextAlignment.Center, MaxTextWidth = Math.Max(1, area.Width - 2 * padding), MaxTextHeight = Math.Max(1, area.Height - 2 * padding), Trimming = TextTrimming.CharacterEllipsis };
        geometry = text.BuildGeometry(new Point()); geometry.Freeze();
        fill = FlyleafTextValues.Brush(o.Color);
        pen = stroke > 0 ? new Pen(FlyleafTextValues.Brush(o.OutlineColor), stroke * 2) { LineJoin = PenLineJoin.Round } : null;
        pen?.Freeze();
        inkBounds = pen == null ? geometry.Bounds : geometry.GetRenderBounds(pen);
        clip = area;
        origin = new Point(area.Left + padding, Math.Max(area.Top + padding, area.Bottom - Math.Max(padding, o.BottomOffset * scale) - text.Height));
    }
}
