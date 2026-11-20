using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace Flyleaf.Controls.WPF;

public class FlyleafSeekBar : FrameworkElement
{
    static DependencyProperty RegisterBrush(string name, Brush fallback) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = RegisterBrush(nameof(TrackBrush), Brushes.DimGray);
    public static readonly DependencyProperty BufferedBrushProperty = RegisterBrush(nameof(BufferedBrush), Brushes.Gray);
    public static readonly DependencyProperty PlayedBrushProperty = RegisterBrush(nameof(PlayedBrush), Brushes.CornflowerBlue);
    public static readonly DependencyProperty ChapterBrushProperty = RegisterBrush(nameof(ChapterBrush), Brushes.LightGray);
    public static readonly DependencyProperty ThumbOutlineBrushProperty = RegisterBrush(nameof(ThumbOutlineBrush), Brushes.White);

    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush BufferedBrush { get => (Brush)GetValue(BufferedBrushProperty); set => SetValue(BufferedBrushProperty, value); }
    public Brush PlayedBrush { get => (Brush)GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }
    public Brush ChapterBrush { get => (Brush)GetValue(ChapterBrushProperty); set => SetValue(ChapterBrushProperty, value); }
    public Brush ThumbOutlineBrush { get => (Brush)GetValue(ThumbOutlineBrushProperty); set => SetValue(ThumbOutlineBrushProperty, value); }

    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(nameof(Player), typeof(Player), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(null, OnPlayerChanged));

    public Player? Player
    {
        get => (Player?)GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public static readonly DependencyProperty SeekWhileDraggingProperty =
        DependencyProperty.Register(nameof(SeekWhileDragging), typeof(bool), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(true));

    public bool SeekWhileDragging
    {
        get => (bool)GetValue(SeekWhileDraggingProperty);
        set => SetValue(SeekWhileDraggingProperty, value);
    }

    long? lastSeekMilliseconds;
    bool dragging;
    long dragTime;
    long hoverTime;
    bool hovering;
    ToolTip? preview;

    public FlyleafSeekBar()
    {
        SetResourceReference(TrackBrushProperty, "FlyleafME.OverlayBorderBrush");
        SetResourceReference(BufferedBrushProperty, "FlyleafME.OverlaySecondaryTextBrush");
        SetResourceReference(PlayedBrushProperty, "FlyleafME.AccentBrush");
        SetResourceReference(ChapterBrushProperty, "FlyleafME.OverlayTextBrush");
        SetResourceReference(ThumbOutlineBrushProperty, "FlyleafME.OverlayTextBrush");

        MinHeight = 24;
        Cursor = Cursors.Hand;
        Unloaded += (_, _) =>
        {
            HidePreview();
            EndDrag(false);
        };
    }

    static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var seekBar = (FlyleafSeekBar)d;
        if (e.OldValue is Player oldPlayer)
            oldPlayer.PropertyChanged -= seekBar.Player_PropertyChanged;
        if (e.NewValue is Player newPlayer)
            newPlayer.PropertyChanged += seekBar.Player_PropertyChanged;

        seekBar.HidePreview();
        seekBar.EndDrag(false);
        seekBar.InvalidateVisual();
    }

    void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Player.CurTime) or nameof(Player.Duration) or
            nameof(Player.BufferedDuration) or nameof(Player.CanPlay) or nameof(Player.IsLive))
        {
            if (Dispatcher.CheckAccess())
                InvalidateVisual();
            else
                Dispatcher.BeginInvoke(InvalidateVisual);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 24);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = RenderSize.Width;
        if (width <= 0)
            return;

        var neutral = TrackBrush;
        var cached = BufferedBrush;
        var played = PlayedBrush;

        long duration = Player?.Duration ?? 0;
        long current = Math.Clamp(dragging ? dragTime : (Player?.CurTime ?? 0), 0, Math.Max(0, duration));
        double y = (RenderSize.Height - 6) / 2;
        dc.DrawRoundedRectangle(neutral, null, new Rect(0, y, width, 6), 3, 3);

        if (duration <= 0 || Player?.CanPlay != true)
            return;

        double playedWidth = width * ((double)current / duration);
        long bufferedTicks = Math.Max(0, Player.BufferedDuration);
        double cachedWidth = width * Math.Clamp(((double)current + bufferedTicks) / duration, 0, 1);
        if (cachedWidth > playedWidth)
            dc.DrawRoundedRectangle(cached, null, new Rect(playedWidth, y, cachedWidth - playedWidth, 6), 2, 2);
        if (playedWidth > 0)
            dc.DrawRoundedRectangle(played, null, new Rect(0, y, playedWidth, 6), 3, 3);

        if (Player.Chapters is { Count: > 0 } chapters)
        {
            var markerPen = new Pen(ChapterBrush, 1.5);
            foreach (Demuxer.Chapter chapter in chapters)
            {
                if (chapter.StartTime <= 0 || chapter.StartTime >= duration)
                    continue;
                double x = width * ((double)chapter.StartTime / duration);
                dc.DrawLine(markerPen, new Point(x, y - 2), new Point(x, y + 8));
            }
        }

        double thumbX = Math.Clamp(playedWidth, 0, width);
        dc.DrawEllipse(played, new Pen(ThumbOutlineBrush, 1), new Point(thumbX, y + 3), dragging || hovering ? 6 : 4, dragging || hovering ? 6 : 4);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        hovering = true;
        UpdatePreview(e.GetPosition(this).X);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        hovering = false;
        if (!dragging)
            HidePreview();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Player is not { CanPlay: true, Duration: > 0 })
            return;

        UpdatePreview(e.GetPosition(this).X);
        if (dragging)
        {
            dragTime = hoverTime;
            if (SeekWhileDragging) SeekDragPosition();
        }
        InvalidateVisual();
    }

    void UpdatePreview(double x)
    {
        if (Player is not { CanPlay: true, Duration: > 0 } player)
        {
            HidePreview();
            return;
        }

        hoverTime = TimeAt(x);
        string chapterName = "";
        if (player.Chapters is { Count: > 0 } chapters)
        {
            foreach (Demuxer.Chapter chapter in chapters)
            {
                if (hoverTime >= chapter.StartTime && (chapter.EndTime <= chapter.StartTime || hoverTime < chapter.EndTime))
                {
                    chapterName = chapter.Title ?? "";
                    break;
                }
            }
        }

        if (preview == null)
        {
            preview = new ToolTip
            {
                PlacementTarget = this,
                Placement = PlacementMode.Relative,
                VerticalOffset = -38,
                IsHitTestVisible = false,
                StaysOpen = true
            };
            if (TryFindResource("FlyleafME.ToolTipStyle") is Style style)
                preview.Style = style;
        }

        preview.Content = string.IsNullOrWhiteSpace(chapterName)
            ? FlyleafPlaybackBar.FormatTime(hoverTime)
            : $"{FlyleafPlaybackBar.FormatTime(hoverTime)}  ·  {chapterName}";
        double previewWidth = preview.ActualWidth > 0 ? preview.ActualWidth : 78;
        preview.HorizontalOffset = Math.Clamp(x - previewWidth / 2, 0, Math.Max(0, RenderSize.Width - previewWidth));
        preview.IsOpen = true;
    }

    void HidePreview()
    {
        if (preview != null)
            preview.IsOpen = false;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Player?.CanPlay != true || Player.Duration <= 0)
            return;

        dragging = true;
        lastSeekMilliseconds = null;
        UpdatePreview(e.GetPosition(this).X);
        dragTime = hoverTime;
        if (!CaptureMouse())
        {
            EndDrag(false);
            return;
        }
        if (SeekWhileDragging) SeekDragPosition();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!dragging)
            return;

        dragTime = TimeAt(e.GetPosition(this).X);
        EndDrag(true);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag(false);
    }

    void EndDrag(bool commit)
    {
        if (!dragging)
            return;

        dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (commit) SeekDragPosition();
        lastSeekMilliseconds = null;
        if (!IsMouseOver)
            HidePreview();
        InvalidateVisual();
    }

    void SeekDragPosition()
    {
        if (Player is not { CanPlay: true, Duration: > 0 } player) return;
        long target = Math.Clamp(dragTime, 0, player.Duration);
        long milliseconds = target / TimeSpan.TicksPerMillisecond;
        if (lastSeekMilliseconds == milliseconds) return;
        // Use the engine's normal asynchronous seek path and configured accuracy,
        // just like v1's two-way CurTime slider. Do not pause/resume playback here.
        player.CurTime = target;
        lastSeekMilliseconds = milliseconds;
    }

    long TimeAt(double x)
    {
        if (Player is not { Duration: > 0 } player || RenderSize.Width <= 0)
            return 0;
        return (long)(Math.Clamp(x / RenderSize.Width, 0, 1) * player.Duration);
    }
}
