using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Flyleaf.Controls.WPF;

/// <summary>Shared visual elements for the built-in runtime editors.</summary>
internal static class FlyleafPanelUI
{
    internal static Button Button(string caption)
    {
        var button = new Button { Content = caption, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 0 };
        button.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.ButtonStyle");
        return button;
    }

    internal static Button IconButton(string glyph, string label)
    {
        var button = Button(glyph);
        button.Width = button.Height = button.MinHeight = 28;
        button.Padding = new Thickness(4);
        button.FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets");
        button.FontSize = 14; button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center; button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
    internal static Button ResetButton(string label) => IconButton("", label);

    internal static Border SectionHeading(string title, Button? action = null, double topMargin = 22)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var label = Text(title);
        label.FontWeight = FontWeights.SemiBold;
        label.FontSize = 14;
        label.Margin = new Thickness(0);
        label.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(label);
        if (action != null)
        {
            action.Margin = new Thickness(8, 0, 0, 0);
            header.Children.Add(action);
        }
        var heading = new Border { Child = header, Margin = new Thickness(0, topMargin, 0, 8), Padding = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        heading.SetResourceReference(Border.BorderBrushProperty, "FlyleafME.BorderBrush");
        return heading;
    }
    internal static TextBlock Text(string value, bool secondary = false)
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
        text.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "FlyleafME.SecondaryTextBrush" : "FlyleafME.TextBrush");
        return text;
    }

    internal static TextBlock ValidationMessage()
    {
        var text = Text("");
        text.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.ErrorBrush");
        return text;
    }

    internal static Border Section(string title, params UIElement[] content)
    {
        var body = new StackPanel();
        var heading = Text(title); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 10);
        body.Children.Add(heading);
        foreach (var element in content) body.Children.Add(element);
        return Card(body);
    }

    internal static Border CollapsibleSection(string title, UIElement content, bool expanded = false)
    {
        var heading = Text(title); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0);
        var expander = new Expander { Header = heading, Content = content, IsExpanded = expanded, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        expander.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush");
        return Card(expander);
    }

    internal static (Border Card, Action Refresh) DelaySection(string stream, Func<long> readDelay,
        Action<long> writeDelay, Action decrease, Action increase)
    {
        var label = Text("Delay");
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, 0, 6, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.NoWrap;

        var value = new TextBox
        {
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            ToolTip = $"Enter {stream} delay in milliseconds and press Enter"
        };
        value.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.InputStyle");
        System.Windows.Automation.AutomationProperties.SetName(value, $"{stream} delay in milliseconds");
        var units = Text("ms", true);
        units.Margin = new Thickness(0, 0, 4, 0);
        units.VerticalAlignment = VerticalAlignment.Center;

        var error = ValidationMessage();
        string displayed = "";
        void ShowDelay()
        {
            displayed = (readDelay() / (decimal)TimeSpan.TicksPerMillisecond).ToString("0.####", CultureInfo.CurrentCulture);
            value.Text = displayed;
            error.Visibility = Visibility.Collapsed;
        }
        void Refresh()
        {
            if (!value.IsKeyboardFocusWithin) ShowDelay();
        }
        void Commit()
        {
            if (!value.IsEnabled) return;
            if (value.Text == displayed)
            {
                ShowDelay();
                return;
            }
            if (!decimal.TryParse(value.Text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var milliseconds) ||
                milliseconds is < -86400000 or > 86400000)
            {
                error.Text = "Enter a delay between −86,400,000 and 86,400,000 ms.";
                error.Visibility = Visibility.Visible;
                return;
            }
            writeDelay((long)Math.Round(milliseconds * TimeSpan.TicksPerMillisecond));
            ShowDelay();
        }

        value.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            e.Handled = true;
        };
        value.LostKeyboardFocus += (_, _) => Commit();

        var earlier = IconButton("\uE738", $"Decrease {stream} delay by one step");
        var later = IconButton("\uE710", $"Increase {stream} delay by one step");
        var clear = ResetButton($"Reset {stream} delay");
        clear.Margin = new Thickness(4, 0, 0, 0);
        earlier.Click += (_, _) => { decrease(); ShowDelay(); };
        later.Click += (_, _) => { increase(); ShowDelay(); };
        clear.Click += (_, _) => { writeDelay(0); ShowDelay(); };

        var row = new Grid();
        FrameworkElement[] fields = [label, earlier, value, units, later, clear];
        for (int i = 0; i < fields.Length; i++)
        {
            row.ColumnDefinitions.Add(new() { Width = fields[i] == value ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(fields[i], i);
            row.Children.Add(fields[i]);
        }

        var body = new StackPanel();
        body.Children.Add(row);
        body.Children.Add(error);
        ShowDelay();
        return (Card(body), Refresh);
    }

    static Border Card(UIElement content)
    {
        var border = new Border { Child = content, Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "FlyleafME.RaisedSurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "FlyleafME.BorderBrush");
        return border;
    }
}
