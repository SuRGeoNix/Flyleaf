using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Flyleaf.Controls.WPF;

public class FlyleafVolumeWedge : Slider
{
    const double TrackInset = 1;
    const double LevelInset = 2;
    const double LevelStart = TrackInset + LevelInset;

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(FlyleafVolumeWedge),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OutlineBrushProperty = DependencyProperty.Register(nameof(OutlineBrush), typeof(Brush), typeof(FlyleafVolumeWedge),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LevelBrushProperty = DependencyProperty.Register(nameof(LevelBrush), typeof(Brush), typeof(FlyleafVolumeWedge),
        new FrameworkPropertyMetadata(Brushes.Firebrick, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush OutlineBrush { get => (Brush)GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }
    public Brush LevelBrush { get => (Brush)GetValue(LevelBrushProperty); set => SetValue(LevelBrushProperty, value); }

    Geometry? wedge, levelWedge;
    Size wedgeSize;

    public FlyleafVolumeWedge()
    {
        SetResourceReference(TrackBrushProperty, "FlyleafME.PlaybackTrackBrush");
        SetResourceReference(OutlineBrushProperty, "FlyleafME.BorderBrush");
        IsMoveToPointEnabled = false;
        IsEnabledChanged += (_, _) =>
        {
            if (!IsEnabled && IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
        };
        Unloaded += (_, _) => { if (IsMouseCaptured) ReleaseMouseCapture(); };
    }

    bool Vertical => Orientation == System.Windows.Controls.Orientation.Vertical;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == OrientationProperty) InvalidateVisual();
    }

    Geometry GetWedge(Size size)
    {
        if (wedge != null && wedgeSize == size) return wedge;
        wedgeSize = size;
        double center = size.Height / 2, height = Math.Min(12, center - 1), right = size.Width - TrackInset;
        double tip = Math.Min(3, height);
        levelWedge = tip > LevelInset && size.Width > 2 * LevelStart
            ? CreateWedge(LevelStart, right - LevelInset, center, tip - LevelInset, height - LevelInset)
            : null;
        return wedge = CreateWedge(TrackInset, right, center, tip, height);
    }

    static Geometry CreateWedge(double left, double right, double center, double tip, double height)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left, center - tip), true, true);
            ctx.LineTo(new Point(right, center - height), true, false);
            ctx.LineTo(new Point(right, center + height), true, false);
            ctx.LineTo(new Point(left, center + tip), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RenderSize.Width <= 2 || RenderSize.Height <= 2) return;
        bool vertical = Vertical;
        var size = vertical ? new Size(RenderSize.Height, RenderSize.Width) : RenderSize;
        var geometry = GetWedge(size);
        // Rotate the shared horizontal geometry: minimum at the bottom, maximum at the top.
        if (vertical) dc.PushTransform(new MatrixTransform(0, -1, 1, 0, 0, RenderSize.Height));
        dc.DrawGeometry(TrackBrush, null, geometry);
        if (!IsEnabled) dc.PushOpacity(0.3);
        double fraction = Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
        if (fraction > 0 && levelWedge != null)
        {
            dc.PushClip(new RectangleGeometry(new Rect(LevelStart, 0, (size.Width - 2 * LevelStart) * fraction, size.Height)));
            dc.DrawGeometry(LevelBrush, null, levelWedge);
            dc.Pop();
        }
        dc.DrawGeometry(null, new Pen(OutlineBrush, 1), geometry);
        if (!IsEnabled) dc.Pop();
        if (vertical) dc.Pop();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        InvalidateVisual();
    }

    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        InvalidateVisual();
    }

    void SetVolumeAt(Point point)
    {
        bool vertical = Vertical;
        double length = vertical ? RenderSize.Height : RenderSize.Width;
        double position = vertical ? length - point.Y : point.X;
        double start = length > 2 * LevelStart ? LevelStart : TrackInset;
        double fraction = Math.Clamp((position - start) / Math.Max(1, length - 2 * start), 0, 1);
        SetCurrentValue(ValueProperty, Math.Round(Minimum + (Maximum - Minimum) * fraction));
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!IsEnabled || !CaptureMouse()) return;
        SetVolumeAt(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseMouseCapture(); return; }
        SetVolumeAt(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        SetVolumeAt(e.GetPosition(this));
        ReleaseMouseCapture();
        e.Handled = true;
    }
}
