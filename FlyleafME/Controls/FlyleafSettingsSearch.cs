using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Flyleaf.Controls.WPF;

internal static class FlyleafSettingsSearch
{
    static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached("Title", typeof(string), typeof(FlyleafSettingsSearch));
    static readonly DependencyProperty SectionProperty = DependencyProperty.RegisterAttached("Section", typeof(string), typeof(FlyleafSettingsSearch));
    static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached("Reveal", typeof(Action), typeof(FlyleafSettingsSearch));

    internal sealed record Location(string Title, string Section);

    internal static T Label<T>(T element, string title) where T : FrameworkElement
    {
        element.SetValue(TitleProperty, title);
        return element;
    }

    internal static void Section(FrameworkElement element, string name, Action? reveal = null)
    {
        element.SetValue(SectionProperty, name);
        if (reveal != null) element.SetValue(RevealProperty, reveal);
    }

    internal static IEnumerable<(Location Location, FrameworkElement Target)> Find(FrameworkElement element, string section = "")
    {
        if (element.Visibility != Visibility.Visible && element.GetValue(RevealProperty) == null) yield break;
        var heading = (string?)element.GetValue(SectionProperty) ?? ((element as Expander)?.Header switch
        {
            TextBlock text => text.Text,
            string value => value,
            _ => null
        });
        if (!string.IsNullOrWhiteSpace(heading))
        {
            var target = element is StackPanel { Children.Count: > 0 } panel && panel.Children[0] is Border header ? header : element;
            yield return (new(heading, section), target);
            section = section.Length == 0 ? heading : $"{section} › {heading}";
        }
        if (element.GetValue(TitleProperty) is string title)
        {
            yield return (new(title, section), element);
            yield break;
        }
        var label = element switch
        {
            CheckBox { Content: string choice } => choice,
            Button { Content: string action } when !action.EndsWith('▾') && action.Any(char.IsLetterOrDigit) => action,
            ComboBox or TextBox => AutomationProperties.GetName(element),
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(label)) yield return (new(label, section), element);
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<FrameworkElement>())
            foreach (var option in Find(child, section)) yield return option;
    }

    internal static void Reveal(FrameworkElement target)
    {
        for (DependencyObject? element = target; element != null; element = LogicalTreeHelper.GetParent(element))
        {
            if (element is Expander expander) expander.SetCurrentValue(Expander.IsExpandedProperty, true);
            if (element.GetValue(RevealProperty) is Action reveal) reveal();
        }
    }

    internal sealed class Highlight(FrameworkElement target) : Adorner(target)
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            var brush = target.TryFindResource("FlyleafME.AccentBrush") as Brush ?? SystemColors.HighlightBrush;
            var bounds = new Rect(new Point(), target.RenderSize);
            bounds.Inflate(2, 1);
            drawingContext.DrawRoundedRectangle(null, new Pen(brush, 1.5), bounds, 4, 4);
        }
    }
}
