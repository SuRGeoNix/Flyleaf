using System.Windows;
using System.Windows.Media;

namespace Flyleaf.Controls.WPF;

/// <summary>Scalable interpretation of the Flyleaf F/play mark, with independently themed brushes.</summary>
public sealed class FlyleafLogo : FrameworkElement
{
    static readonly Geometry stem = Shape("M15,87 C24,71 36,65 53,62 L53,85 C51,104 36,115 16,120 C12,107 13,97 15,87 Z");
    static readonly Geometry leaf = Shape("M15,85 C14,65 17,46 24,33 C32,19 44,18 61,18 L100,18 C110,18 118,17 124,14 C122,31 117,43 101,46 C85,49 69,46 58,47 C53,47 52,51 52,58 L52,63 C36,61 24,69 15,85 Z");
    static readonly Geometry play = Shape("M70,51 C66,49 62,52 62,57 L62,90 C62,95 66,97 70,94 L100,77 C105,74 105,71 100,68 Z");

    public static readonly DependencyProperty MarkBrushProperty = BrushProperty(nameof(MarkBrush), ColorBrush(255, 48, 40));
    public static readonly DependencyProperty FoldBrushProperty = BrushProperty(nameof(FoldBrush), ColorBrush(205, 19, 37));
    public static readonly DependencyProperty PlayBrushProperty = BrushProperty(nameof(PlayBrush), Brushes.White);
    public static readonly DependencyProperty OutlineBrushProperty = BrushProperty(nameof(OutlineBrush), ColorBrush(32, 43, 57));
    public Brush MarkBrush { get => (Brush)GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }
    public Brush FoldBrush { get => (Brush)GetValue(FoldBrushProperty); set => SetValue(FoldBrushProperty, value); }
    public Brush PlayBrush { get => (Brush)GetValue(PlayBrushProperty); set => SetValue(PlayBrushProperty, value); }
    public Brush OutlineBrush { get => (Brush)GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }

    public FlyleafLogo()
    {
        IsHitTestVisible = false;
        SetResourceReference(MarkBrushProperty, "FlyleafME.LogoMarkBrush");
        SetResourceReference(FoldBrushProperty, "FlyleafME.LogoFoldBrush");
        SetResourceReference(PlayBrushProperty, "FlyleafME.LogoPlayBrush");
        SetResourceReference(OutlineBrushProperty, "FlyleafME.LogoOutlineBrush");
    }

    static DependencyProperty BrushProperty(string name, Brush fallback) => DependencyProperty.Register(name, typeof(Brush), typeof(FlyleafLogo),
        new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    static Geometry Shape(string data) { var shape = Geometry.Parse(data); shape.Freeze(); return shape; }
    static Brush ColorBrush(byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(128, availableSize.Width), Math.Min(128, availableSize.Height));

    protected override void OnRender(DrawingContext context)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 128;
        if (scale <= 0) return;
        context.PushTransform(new TranslateTransform((ActualWidth - 128 * scale) / 2, (ActualHeight - 128 * scale) / 2));
        context.PushTransform(new ScaleTransform(scale, scale));
        Draw(context, MarkBrush, FoldBrush, PlayBrush, OutlineBrush);
        context.Pop(); context.Pop();
    }

    static void Draw(DrawingContext context, Brush mark, Brush fold, Brush symbol, Brush outline)
    {
        context.DrawGeometry(fold, null, stem);
        context.DrawGeometry(mark, null, leaf);
        context.DrawGeometry(symbol, new Pen(outline, 2) { LineJoin = PenLineJoin.Round }, play);
    }

    /// <summary>Vector ImageSource for WPF Window.Icon; Windows rasterizes at the requested native icon size.</summary>
    public static DrawingImage CreateImageSource(Brush mark, Brush fold, Brush symbol, Brush outline)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 128, 128));
            Draw(context, mark, fold, symbol, outline);
        }
        return new DrawingImage(drawing);
    }
}
