using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>Two endpoints on a shared track with exact, culture-aware seconds inputs.</summary>
internal sealed class FlyleafDurationRangeEditor : StackPanel
{
    readonly Func<(double Minimum, double Maximum)> read;
    readonly Action<double, double> write;
    readonly Action<Action> apply;
    readonly bool maximumOff;
    internal TextBox MinimumInput { get; } = Input();
    internal TextBox MaximumInput { get; } = Input();
    readonly FlyleafRangeBar bar = new();
    readonly TextBlock scale = Text("", true), error = ValidationMessage();
    bool dirty, updating;

    internal FlyleafDurationRangeEditor(Func<(double Minimum, double Maximum)> read, Action<double, double> write, Action<Action> apply, bool maximumOff = false)
    {
        this.read = read; this.write = write; this.apply = apply; this.maximumOff = maximumOff;
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new());
        var lower = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; lower.Children.Add(Text("Minimum · seconds", true)); lower.Children.Add(MinimumInput);
        var upper = new StackPanel(); upper.Children.Add(Text(maximumOff ? "Maximum · 0 = off" : "Maximum · seconds", true)); upper.Children.Add(MaximumInput);
        FlyleafSettingsSearch.Label(lower, "Minimum");
        FlyleafSettingsSearch.Label(upper, "Maximum");
        Grid.SetColumn(upper, 1); row.Children.Add(lower); row.Children.Add(upper);
        Children.Add(row); Children.Add(bar); Children.Add(scale); Children.Add(error);
        foreach (var input in new[] { MinimumInput, MaximumInput })
        {
            input.TextChanged += (_, _) => { if (!updating) dirty = true; };
            input.LostKeyboardFocus += (_, _) => CommitText();
            input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitText(); e.Handled = true; } };
        }
        bar.Committed += (_, _) => TryCommit(bar.Lower, bar.Upper);
        bar.PreviewChanged += (_, _) => { dirty = false; error.Text = ""; ShowValues(bar.Lower, bar.Upper); };
        bar.DragCanceled += (_, _) => RefreshValues(true);
        RefreshValues();
    }
    static TextBox Input()
    {
        var input = new TextBox { Width = 88, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 0, 0) };
        input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle"); return input;
    }
    void CommitText()
    {
        if (!dirty) return;
        if (!double.TryParse(MinimumInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var minimum) || !double.TryParse(MaximumInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var maximum))
        { error.Text = "Enter minimum and maximum seconds."; return; }
        TryCommit(minimum, maximum);
    }
    internal bool TryCommit(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum < 0 || maximum < 0 || minimum > 86400 || maximum > 86400)
        { error.Text = "Use finite durations between 0 and 86,400 seconds."; return false; }
        if (minimum > maximum && !(maximumOff && maximum == 0)) { error.Text = "Minimum cannot exceed maximum."; return false; }
        error.Text = ""; dirty = false; apply(() => write(minimum, maximum)); RefreshValues(); return true;
    }
    internal void RefreshValues(bool discardDraft = false)
    {
        if (discardDraft) { dirty = false; error.Text = ""; }
        if (dirty || bar.IsDragging) return;
        var values = read();
        ShowValues(values.Minimum, values.Maximum);
        double extent = Math.Min(86400, Math.Max(10, Math.Ceiling(Math.Max(values.Minimum, values.Maximum) / 5) * 5));
        bool off = maximumOff && values.Maximum == 0;
        bar.IsEnabled = !off;
        bar.SetValues(off ? 0 : values.Minimum, off ? 0 : values.Maximum, extent);
        scale.Text = off ? "Catch-up latency control is off. Set a maximum to enable it." : $"Drag range · 0–{extent:0.###} s · fields allow exact values";
    }
    void ShowValues(double minimum, double maximum)
    {
        updating = true;
        try
        {
            MinimumInput.Text = minimum.ToString("0.#######", CultureInfo.CurrentCulture);
            MaximumInput.Text = maximum.ToString("0.#######", CultureInfo.CurrentCulture);
        }
        finally { updating = false; }
    }
}

internal sealed class FlyleafRangeBar : Canvas
{
    readonly Rectangle track = new() { Height = 3, RadiusX = 1.5, RadiusY = 1.5 };
    readonly Rectangle selected = new() { Height = 3, RadiusX = 1.5, RadiusY = 1.5 };
    readonly Thumb lower = Handle("Minimum"), upper = Handle("Maximum");
    internal double Lower { get; private set; }
    internal double Upper { get; private set; }
    double extent = 10;
    internal bool IsDragging => lower.IsDragging || upper.IsDragging;
    internal event EventHandler? Committed;
    internal event EventHandler? PreviewChanged;
    internal event EventHandler? DragCanceled;

    internal FlyleafRangeBar()
    {
        Height = 28; Margin = new Thickness(0, 10, 0, 0); Background = Brushes.Transparent;
        track.SetResourceReference(Shape.FillProperty, "FlyleafME.BorderBrush"); selected.SetResourceReference(Shape.FillProperty, "FlyleafME.AccentBrush");
        Children.Add(track); Children.Add(selected); Children.Add(lower); Children.Add(upper);
        foreach (var handle in new[] { lower, upper })
        {
            handle.DragDelta += (_, e) => Move(handle, (handle == lower ? Lower : Upper) + e.HorizontalChange / Math.Max(1, ActualWidth - 18) * extent);
            handle.DragCompleted += (_, e) => { if (e.Canceled) DragCanceled?.Invoke(this, EventArgs.Empty); else Committed?.Invoke(this, EventArgs.Empty); };
            handle.KeyDown += (_, e) =>
            {
                double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
                double current = handle == lower ? Lower : Upper;
                double? next = e.Key switch { Key.Left or Key.Down => current - step, Key.Right or Key.Up => current + step, Key.Home => 0, Key.End => extent, _ => null };
                if (next == null) return; Move(handle, next.Value); Committed?.Invoke(this, EventArgs.Empty); e.Handled = true;
            };
        }
        MouseLeftButtonDown += (_, e) =>
        {
            for (var node = e.OriginalSource as DependencyObject; node != null && node != this; node = VisualTreeHelper.GetParent(node)) if (node is Thumb) return;
            double value = Math.Clamp((e.GetPosition(this).X - 9) / Math.Max(1, ActualWidth - 18), 0, 1) * extent;
            var handle = Math.Abs(value - Lower) < Math.Abs(value - Upper) ? lower : upper;
            handle.Focus(); Move(handle, value); Committed?.Invoke(this, EventArgs.Empty); e.Handled = true;
        };
        SizeChanged += (_, _) => ArrangeHandles();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.4;
    }
    static Thumb Handle(string name)
    {
        var thumb = new Thumb { Width = 18, Height = 18, Focusable = true, Cursor = Cursors.SizeWE, ToolTip = name + " · arrow keys adjust; Shift for larger steps" };
        System.Windows.Automation.AutomationProperties.SetName(thumb, name + " duration");
        thumb.SetResourceReference(StyleProperty, "FlyleafME.RangeHandleStyle"); return thumb;
    }
    internal void SetValues(double minimum, double maximum, double maximumExtent)
    { extent = Math.Max(0.001, maximumExtent); Lower = Math.Clamp(minimum, 0, extent); Upper = Math.Clamp(maximum, Lower, extent); ArrangeHandles(); }
    void Move(Thumb handle, double value)
    {
        value = Math.Round(value, 3);
        if (handle == lower) Lower = Math.Clamp(value, 0, Upper); else Upper = Math.Clamp(value, Lower, extent);
        ArrangeHandles();
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }
    void ArrangeHandles()
    {
        double width = Math.Max(0, ActualWidth - 18), left = width * Lower / extent, right = width * Upper / extent;
        track.Width = width; SetLeft(track, 9); SetTop(track, 12);
        selected.Width = Math.Max(0, right - left); SetLeft(selected, left + 9); SetTop(selected, 12);
        SetLeft(lower, left); SetLeft(upper, right); SetTop(lower, 5); SetTop(upper, 5);
    }
}
