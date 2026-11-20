using System.Windows;
using System.Windows.Controls;

namespace Flyleaf.Controls.WPF;

/// <summary>
/// Composes overlay UI without changing the FlyleafHost rendering viewport.
/// Slots can be supplied by the application or by reusable player controls.
/// </summary>
public class FlyleafOverlayLayout : Control
{
    public static readonly DependencyProperty TopContentProperty =
        DependencyProperty.Register(nameof(TopContent), typeof(object), typeof(FlyleafOverlayLayout));

    public static readonly DependencyProperty CenterContentProperty =
        DependencyProperty.Register(nameof(CenterContent), typeof(object), typeof(FlyleafOverlayLayout));

    public static readonly DependencyProperty BottomContentProperty =
        DependencyProperty.Register(nameof(BottomContent), typeof(object), typeof(FlyleafOverlayLayout));

    public static readonly DependencyProperty LeftContentProperty =
        DependencyProperty.Register(nameof(LeftContent), typeof(object), typeof(FlyleafOverlayLayout));

    public static readonly DependencyProperty RightContentProperty =
        DependencyProperty.Register(nameof(RightContent), typeof(object), typeof(FlyleafOverlayLayout));

    public object? TopContent
    {
        get => GetValue(TopContentProperty);
        set => SetValue(TopContentProperty, value);
    }

    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public object? BottomContent
    {
        get => GetValue(BottomContentProperty);
        set => SetValue(BottomContentProperty, value);
    }

    public object? LeftContent
    {
        get => GetValue(LeftContentProperty);
        set => SetValue(LeftContentProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    static FlyleafOverlayLayout()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlyleafOverlayLayout),
            new FrameworkPropertyMetadata(typeof(FlyleafOverlayLayout)));
    }
}
