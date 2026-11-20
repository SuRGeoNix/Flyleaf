using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Flyleaf.Controls.WPF;

/// <summary>A passive statistics display; does not hold activity or intercept playback gestures.</summary>
public sealed class FlyleafStatisticsOverlay : Border
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(FlyleafMediaInformation), typeof(FlyleafStatisticsOverlay), new PropertyMetadata(null, (d, _) => ((FlyleafStatisticsOverlay)d).UpdateLease()));
    public FlyleafMediaInformation? Source { get => (FlyleafMediaInformation?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    IDisposable? lease;
    FlyleafMediaInformation? acquiredSource;

    public FlyleafStatisticsOverlay()
    {
        IsHitTestVisible = false; Focusable = false;
        Padding = new Thickness(10, 7, 10, 7); CornerRadius = new CornerRadius(6);
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top;
        MaxWidth = 400;
        SetResourceReference(BackgroundProperty, "FlyleafME.OverlayBrush");
        var text = new TextBlock { FontSize = 12, LineHeight = 17, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.OverlayTextBrush");
        text.SetBinding(TextBlock.TextProperty, new Binding("Source.OverlayText") { Source = this, Mode = BindingMode.OneWay });
        Child = text;
        SetBinding(VisibilityProperty, new Binding("Source.ShowStatisticsOverlay") { Source = this, Converter = new FlyleafLib.Controls.WPF.BooleanToVisibilityConverter(), FallbackValue = Visibility.Collapsed });
        Loaded += (_, _) => UpdateLease();
        Unloaded += (_, _) => Release();
        IsVisibleChanged += (_, _) => UpdateLease();
    }
    void UpdateLease()
    {
        if (!IsLoaded || !IsVisible || Source == null) { Release(); return; }
        if (ReferenceEquals(acquiredSource, Source)) return;
        Release(); acquiredSource = Source; lease = Source.Acquire();
    }
    void Release() { lease?.Dispose(); lease = null; acquiredSource = null; }
}
