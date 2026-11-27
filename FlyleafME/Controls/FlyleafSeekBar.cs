using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace Flyleaf.Controls.WPF;

public class FlyleafSeekBar : FrameworkElement
{
    const double ModernFillInset = 2;
    const double PreviewGap = 6, ViewEdgeInset = 6, ImageLabelGap = 4;
    const int MaxPreviewBitmapEdge = 640;
    static readonly Thickness PreviewPadding = new(6, 6, 6, 5);

    static DependencyProperty RegisterBrush(string name, Brush fallback) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = RegisterBrush(nameof(TrackBrush), Brushes.DimGray);
    public static readonly DependencyProperty BufferedBrushProperty = RegisterBrush(nameof(BufferedBrush), Brushes.Gray);
    public static readonly DependencyProperty PlayedBrushProperty = RegisterBrush(nameof(PlayedBrush), Brushes.CornflowerBlue);
    public static readonly DependencyProperty ChapterBrushProperty = RegisterBrush(nameof(ChapterBrush), Brushes.LightGray);
    public static readonly DependencyProperty ThumbOutlineBrushProperty = RegisterBrush(nameof(ThumbOutlineBrush), Brushes.White);
    public static readonly DependencyProperty OutlineBrushProperty = RegisterBrush(nameof(OutlineBrush), Brushes.DimGray);

    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush BufferedBrush { get => (Brush)GetValue(BufferedBrushProperty); set => SetValue(BufferedBrushProperty, value); }
    public Brush PlayedBrush { get => (Brush)GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }
    public Brush ChapterBrush { get => (Brush)GetValue(ChapterBrushProperty); set => SetValue(ChapterBrushProperty, value); }
    public Brush ThumbOutlineBrush { get => (Brush)GetValue(ThumbOutlineBrushProperty); set => SetValue(ThumbOutlineBrushProperty, value); }
    public Brush OutlineBrush { get => (Brush)GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }

    public static readonly DependencyProperty IsModernProperty =
        DependencyProperty.Register(nameof(IsModern), typeof(bool), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsModern { get => (bool)GetValue(IsModernProperty); set => SetValue(IsModernProperty, value); }

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
            new FrameworkPropertyMetadata(true, OnPreviewOptionChanged));

    public bool SeekWhileDragging
    {
        get => (bool)GetValue(SeekWhileDraggingProperty);
        set => SetValue(SeekWhileDraggingProperty, value);
    }

    public static readonly DependencyProperty SeekPreviewProperty =
        DependencyProperty.Register(nameof(SeekPreview), typeof(FlyleafSeekPreviewMode), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(FlyleafSeekPreviewMode.Keyframe, OnPreviewOptionChanged));

    public FlyleafSeekPreviewMode SeekPreview
    {
        get => (FlyleafSeekPreviewMode)GetValue(SeekPreviewProperty);
        set => SetValue(SeekPreviewProperty, value);
    }

    public static readonly DependencyProperty SeekPreviewInputTypesProperty =
        DependencyProperty.Register(nameof(SeekPreviewInputTypes), typeof(FlyleafSeekPreviewInputTypes), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(FlyleafPlaybackBarOptions.DefaultSeekPreviewInputTypes, OnPreviewOptionChanged));

    public FlyleafSeekPreviewInputTypes SeekPreviewInputTypes
    {
        get => (FlyleafSeekPreviewInputTypes)GetValue(SeekPreviewInputTypesProperty);
        set => SetValue(SeekPreviewInputTypesProperty, value);
    }

    public static readonly DependencyProperty SeekPreviewSizeProperty =
        DependencyProperty.Register(nameof(SeekPreviewSize), typeof(FlyleafSeekPreviewSize), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(FlyleafSeekPreviewSize.Small, OnPreviewSizeChanged));

    public FlyleafSeekPreviewSize SeekPreviewSize
    {
        get => (FlyleafSeekPreviewSize)GetValue(SeekPreviewSizeProperty);
        set => SetValue(SeekPreviewSizeProperty, value);
    }

    public static readonly DependencyProperty SeekPreviewPercentageProperty =
        DependencyProperty.Register(nameof(SeekPreviewPercentage), typeof(int), typeof(FlyleafSeekBar),
            new FrameworkPropertyMetadata(FlyleafPlaybackBarOptions.DefaultSeekPreviewPercentage, OnPreviewSizeChanged),
            value => value is int percentage && percentage is >= 1 and <= 100);

    public int SeekPreviewPercentage
    {
        get => (int)GetValue(SeekPreviewPercentageProperty);
        set => SetValue(SeekPreviewPercentageProperty, value);
    }

    long? lastSeekMilliseconds;
    bool dragging;
    long dragTime;
    long hoverTime;
    bool hovering;
    ToolTip? preview;
    TextBlock? previewLabel;
    Image? previewImage;
    FrameworkElement? previewViewport;
    Size previewBounds;
    FlyleafSeekPreview? thumbnail;
    double previewX;

    public FlyleafSeekBar()
    {

        MinHeight = 24;
        Cursor = Cursors.Hand;
        Loaded += (_, _) =>
        {
            AttachPlayer(Player);
            // FlyleafHost's overlay window contains the video view, including in fullscreen.
            var window = Window.GetWindow(this);
            previewViewport = window?.Content as FrameworkElement ?? window;
            if (previewViewport != null)
                previewViewport.SizeChanged += PreviewViewport_SizeChanged;
        };
        Unloaded += (_, _) =>
        {
            if (previewViewport != null)
                previewViewport.SizeChanged -= PreviewViewport_SizeChanged;
            previewViewport = null;
            DetachPlayer(Player);
            HidePreview();
            EndDrag(false);
            thumbnail?.UpdateSource(null);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            HidePreview();
            thumbnail?.UpdateSource(null);
        };
    }

    static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var seekBar = (FlyleafSeekBar)d;
        seekBar.DetachPlayer(e.OldValue as Player);
        if (seekBar.IsLoaded) seekBar.AttachPlayer(e.NewValue as Player);

        seekBar.HidePreview();
        seekBar.EndDrag(false);
        seekBar.thumbnail?.UpdateSource(null);
        seekBar.InvalidateVisual();
    }

    static void OnPreviewOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var seekBar = (FlyleafSeekBar)d;
        seekBar.thumbnail?.Hide(interrupt: true);
        if (seekBar.SeekPreview == FlyleafSeekPreviewMode.Disabled || e.Property == SeekPreviewInputTypesProperty)
            seekBar.thumbnail?.UpdateSource(null);
        seekBar.RefreshOpenPreview();
    }

    static void OnPreviewSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((FlyleafSeekBar)d).RefreshOpenPreview();

    void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshOpenPreview();

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RefreshOpenPreview();
    }

    void RefreshOpenPreview()
    {
        if (preview?.IsOpen == true)
            UpdatePreview(previewX);
    }

    void AttachPlayer(Player? player)
    {
        if (player == null) return;
        DetachPlayer(player);
        player.PropertyChanged += Player_PropertyChanged;
        player.Video.PropertyChanged += Video_PropertyChanged;
        InvalidateVisual();
    }

    void DetachPlayer(Player? player)
    {
        if (player == null) return;
        player.PropertyChanged -= Player_PropertyChanged;
        player.Video.PropertyChanged -= Video_PropertyChanged;
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
        if (e.PropertyName is nameof(Player.Status) or nameof(Player.CanPlay) or nameof(Player.IsLive) or nameof(Player.IsDisposed))
            RefreshPreview(sender as Player);
        else if (e.PropertyName is nameof(Player.CurTime) or nameof(Player.Duration))
            RefreshLivePreview(sender as Player, e.PropertyName == nameof(Player.Duration));
    }

    void Video_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Video.Stream) && ReferenceEquals(sender, Player?.Video))
            RefreshPreview(Player);
    }

    void RefreshPreview(Player? player)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RefreshPreview(player));
            return;
        }
        if (!IsLoaded || !IsVisible || !ReferenceEquals(player, Player))
            return;
        if (SeekPreview != FlyleafSeekPreviewMode.Disabled)
            thumbnail?.UpdateSource(player, SeekPreviewInputTypes);
        RefreshOpenPreview();
    }

    void RefreshLivePreview(Player? player, bool durationChanged)
    {
        if (player?.IsLive != true)
            return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RefreshLivePreview(player, durationChanged));
            return;
        }
        if (!ReferenceEquals(player, Player) || SeekPreview == FlyleafSeekPreviewMode.Disabled ||
            !SeekPreviewInputTypes.HasFlag(FlyleafSeekPreviewInputTypes.Live) || !(hovering || dragging))
            return;

        if (durationChanged || preview?.IsOpen != true)
            UpdatePreview(Mouse.GetPosition(this).X);
        else if (!dragging || !SeekWhileDragging)
            thumbnail?.Request(hoverTime, player.Duration, false, GetPreviewPixelBounds());
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 24);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = RenderSize.Width;
        if (width <= 0)
            return;

        if (IsModern)
        {
            RenderModern(dc, width);
            return;
        }

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

    void RenderModern(DrawingContext dc, double width)
    {
        double height = Math.Min(22, RenderSize.Height);
        if (height <= 0) return;
        double y = (RenderSize.Height - height) / 2;
        var bounds = new Rect(0, y, width, height);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, RenderSize.Height));
        dc.DrawRoundedRectangle(TrackBrush, null, bounds, 4, 4);
        if (width > 1 && height > 1)
            dc.DrawRoundedRectangle(null, new Pen(OutlineBrush, 1), new Rect(0.5, y + 0.5, width - 1, height - 1), 3.5, 3.5);

        long duration = Player?.Duration ?? 0;
        if (Player is not { CanPlay: true } player || duration <= 0 ||
            width <= 2 * ModernFillInset || height <= 2 * ModernFillInset)
            return;

        bounds.Inflate(-ModernFillInset, -ModernFillInset);
        long actual = Math.Clamp(player.CurTime, 0, duration);
        long current = Math.Clamp(dragging ? dragTime : actual, 0, duration);
        double playedEnd = bounds.Width * ((double)current / duration);
        double actualX = bounds.Width * ((double)actual / duration);
        double bufferedEnd = bounds.Width * Math.Clamp(((double)actual + Math.Max(0, player.BufferedDuration)) / duration, 0, 1);

        void DrawRegion(Brush brush, double start, double end)
        {
            if (end <= start) return;
            dc.DrawRectangle(brush, null, new Rect(bounds.Left + start, bounds.Top, end - start, bounds.Height));
        }

        dc.PushClip(new RectangleGeometry(bounds, 2, 2));
        // Overlap the played region to avoid gaps at the boundary. When
        // previewing a backward seek, retain the actual start of buffered data.
        if (bufferedEnd > Math.Max(playedEnd, actualX))
            DrawRegion(BufferedBrush, playedEnd >= actualX ? 0 : actualX, bufferedEnd);
        DrawRegion(PlayedBrush, 0, playedEnd);
        if (player.Chapters is { Count: > 0 } chapters)
        {
            var markerPen = new Pen(ChapterBrush, 1);
            foreach (Demuxer.Chapter chapter in chapters)
            {
                if (chapter.StartTime <= 0 || chapter.StartTime >= duration) continue;
                double x = bounds.Left + bounds.Width * ((double)chapter.StartTime / duration);
                dc.DrawLine(markerPen, new Point(x, y + 4), new Point(x, y + height - 4));
            }
        }
        dc.Pop();
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
        if (!IsLoaded || !IsVisible || Player is not { CanPlay: true, Duration: > 0, IsDisposed: false, Status: not Status.Opening } player)
        {
            HidePreview();
            return;
        }

        previewX = x;
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
            previewImage = new Image
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 0, ImageLabelGap),
                Visibility = Visibility.Collapsed
            };
            previewLabel = new TextBlock
            {
                MaxWidth = 320,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var content = new StackPanel();
            content.Children.Add(previewImage);
            content.Children.Add(previewLabel);
            preview = new ToolTip
            {
                PlacementTarget = this,
                Placement = PlacementMode.Top,
                IsHitTestVisible = false,
                StaysOpen = true,
                Padding = PreviewPadding,
                Content = content
            };
            preview.SizeChanged += (_, _) => PositionPreview();
            if (TryFindResource("FlyleafME.ToolTipStyle") is Style style)
                preview.Style = style;
        }

        previewLabel!.Text = string.IsNullOrWhiteSpace(chapterName)
            ? FlyleafPlaybackBar.FormatTime(hoverTime)
            : $"{FlyleafPlaybackBar.FormatTime(hoverTime)}  ·  {chapterName}";
        previewBounds = GetPreviewBounds();
        FitThumbnail();
        PositionPreview();
        preview.IsOpen = true;

        if (SeekPreview == FlyleafSeekPreviewMode.Disabled || (dragging && SeekWhileDragging))
        {
            thumbnail?.Hide(interrupt: true);
            return;
        }
        thumbnail ??= new(Dispatcher, ShowThumbnail);
        thumbnail.UpdateSource(player, SeekPreviewInputTypes);
        thumbnail.Request(hoverTime, player.Duration, SeekPreview == FlyleafSeekPreviewMode.Accurate, GetPreviewPixelBounds());
    }

    Size PreviewViewSize => previewViewport is { ActualWidth: > 0, ActualHeight: > 0 } view
        ? view.RenderSize : new Size(RenderSize.Width, 360);

    double AvailablePreviewWidth()
        => Math.Max(1, PreviewViewSize.Width - 2 * ViewEdgeInset - PreviewPadding.Left - PreviewPadding.Right - 2);

    Size GetPreviewBounds()
    {
        var view = PreviewViewSize;
        double percentage = SeekPreviewPercentage / 100.0;
        Size preferred = SeekPreviewSize switch
        {
            FlyleafSeekPreviewSize.Medium => new(240, 135),
            FlyleafSeekPreviewSize.Large => new(320, 180),
            FlyleafSeekPreviewSize.Custom => new(view.Width * percentage, view.Height * percentage),
            _ => new(208, 117)
        };
        double above = previewViewport != null ? TranslatePoint(default, previewViewport).Y : view.Height;
        double below = view.Height - above - RenderSize.Height;
        double captionHeight = Math.Ceiling(previewLabel!.FontSize * 1.3);
        double availableHeight = Math.Max(above, below) - PreviewGap - ViewEdgeInset -
            PreviewPadding.Top - PreviewPadding.Bottom - 2 - ImageLabelGap - captionHeight;
        return new(Math.Max(1, Math.Min(preferred.Width, AvailablePreviewWidth())),
            Math.Max(1, Math.Min(preferred.Height, availableHeight)));
    }

    (uint Width, uint Height) GetPreviewPixelBounds()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double width = previewBounds.Width * dpi.DpiScaleX;
        double height = previewBounds.Height * dpi.DpiScaleY;
        double scale = Math.Min(1, MaxPreviewBitmapEdge / Math.Max(width, height));
        return ((uint)Math.Clamp(Math.Ceiling(width * scale), 1, MaxPreviewBitmapEdge),
            (uint)Math.Clamp(Math.Ceiling(height * scale), 1, MaxPreviewBitmapEdge));
    }

    void PositionPreview()
    {
        if (preview == null)
            return;
        double width = preview.ActualWidth > 0 ? preview.ActualWidth : 78;
        double viewLeft = previewViewport?.TranslatePoint(default, this).X ?? 0;
        double minLeft = viewLeft + ViewEdgeInset;
        double maxLeft = Math.Max(minLeft, viewLeft + PreviewViewSize.Width - ViewEdgeInset - width);
        double left = Math.Clamp(previewX - width / 2, minLeft, maxLeft);
        preview.PlacementRectangle = new Rect(left, -PreviewGap, width, RenderSize.Height + 2 * PreviewGap);
    }

    void ShowThumbnail(BitmapSource? bitmap)
    {
        if (previewImage == null)
            return;
        previewImage.Source = bitmap;
        previewImage.Visibility = bitmap == null ? Visibility.Collapsed : Visibility.Visible;
        FitThumbnail();
    }

    void FitThumbnail()
    {
        double labelWidth = 320;
        if (previewImage is { Source: BitmapSource bitmap })
        {
            double ratio = bitmap.PixelWidth / (double)bitmap.PixelHeight;
            previewImage.Width = Math.Min(previewBounds.Width, previewBounds.Height * ratio);
            previewImage.Height = previewImage.Width / ratio;
            labelWidth = Math.Max(110, previewImage.Width);
        }
        previewLabel!.MaxWidth = Math.Min(labelWidth, AvailablePreviewWidth());
    }

    void HidePreview()
    {
        thumbnail?.Hide();
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
        else if (preview?.IsOpen == true && SeekWhileDragging)
            UpdatePreview(Mouse.GetPosition(this).X);
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
        double width = RenderSize.Width;
        double inset = IsModern && width > 2 * ModernFillInset ? ModernFillInset : 0;
        return (long)(Math.Clamp((x - inset) / (width - 2 * inset), 0, 1) * player.Duration);
    }
}
