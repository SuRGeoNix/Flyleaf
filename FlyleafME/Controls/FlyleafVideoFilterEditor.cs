using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace Flyleaf.Controls.WPF;

/// <summary>Reusable native-level filter editor, shared by quick panels and settings pages.</summary>
public sealed class FlyleafVideoFilterEditor : StackPanel
{
    public static readonly DependencyProperty FilterProperty = DependencyProperty.Register(
        nameof(Filter), typeof(object), typeof(FlyleafVideoFilterEditor),
        new PropertyMetadata(null, (d, _) => ((FlyleafVideoFilterEditor)d).Build()),
        value => value == null || value is FLFilter or D3Filter);
    public object? Filter { get => GetValue(FilterProperty); set => SetValue(FilterProperty, value); }
    TextBox? level;
    (int Minimum, int Maximum, int Default, float Scale) displayedRange;

    internal bool Matches(object filter)
    {
        if (!ReferenceEquals(Filter, filter)) return false;
        return filter switch
        {
            FLFilter fl => displayedRange == (fl.Minimum, fl.Maximum, fl.Default, 1f),
            D3Filter d3 => displayedRange == (d3.Minimum, d3.Maximum, d3.Default, d3.Step),
            _ => false
        };
    }

    public void Reset()
    {
        if (Filter is FLFilter fl) fl.Reset();
        else if (Filter is D3Filter { Available: true } d3) d3.Reset();
        level?.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    void Build()
    {
        BindingOperations.ClearBinding(this, IsEnabledProperty);
        Children.Clear(); level = null;
        if (Filter == null) return;
        int min, max, neutral;
        string name;
        float multiplier = 1;
        if (Filter is FLFilter fl) { min = fl.Minimum; max = fl.Maximum; neutral = fl.Default; name = fl.Filter.ToString(); }
        else { var d3 = (D3Filter)Filter; min = d3.Minimum; max = d3.Maximum; neutral = d3.Default; name = d3.Filter.ToString(); multiplier = d3.Step; }
        displayedRange = (min, max, neutral, multiplier);
        Margin = new Thickness(0, 0, 0, 8);
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock { Text = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush");
        header.Children.Add(caption);
        level = new TextBox { Width = 62, Margin = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var valueBinding = new Binding("Value")
        {
            Source = Filter, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            ValidatesOnExceptions = true
        };
        valueBinding.ValidationRules.Add(new LevelRule(min, max));
        level.SetBinding(TextBox.TextProperty, valueBinding);
        level.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        level.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { level.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); e.Handled = true; } };
        Grid.SetColumn(level, 2); header.Children.Add(level);
        var reset = FlyleafPanelUI.ResetButton("Reset " + name);
        reset.Click += (_, _) => Reset();
        Grid.SetColumn(reset, 3); header.Children.Add(reset);
        Children.Add(header);
        var slider = new Slider
        {
            Minimum = min, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true,
            SmallChange = 1, LargeChange = Math.Max(1, (max - (double)min) / 10), VerticalAlignment = VerticalAlignment.Center
        };
        slider.SetBinding(Slider.ValueProperty, new Binding("Value")
        { Source = Filter, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        slider.SetResourceReference(StyleProperty, "FlyleafME.PlaybackVolumeStyle");
        Grid.SetColumn(slider, 1); header.Children.Add(slider);
        var range = new TextBlock
        {
            Text = $"Levels {min} … {max} · Default {neutral}" +
                (Filter is D3Filter && multiplier != 1 ? $" · Scale ×{multiplier:G}" : ""),
            TextWrapping = TextWrapping.Wrap, FontSize = 11
        };
        range.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        header.ToolTip = name + " · " + range.Text; slider.ToolTip = range.Text;
        SetBinding(IsEnabledProperty, new Binding("Available") { Source = Filter });
    }

    sealed class LevelRule(int min, int max) : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
            int.TryParse(value?.ToString(), NumberStyles.Integer, cultureInfo, out int result) && result >= min && result <= max
                ? ValidationResult.ValidResult : new ValidationResult(false, $"Enter a whole level from {min} to {max}.");
    }
}
