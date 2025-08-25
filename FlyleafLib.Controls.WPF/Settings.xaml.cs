using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using MaterialDesignThemes.Wpf;

namespace FlyleafLib.Controls.WPF;

public partial class Settings : UserControl, INotifyPropertyChanged
{
    FlyleafME flyleaf;

    public Settings(FlyleafME flyleaf)
    {
        InitializeComponent();
        this.flyleaf = flyleaf;
        DataContext = flyleaf;
        Resources.SetTheme(flyleaf.Overlay.Resources.GetTheme());
    }

    public void Dispose()
    {
        tabRoot.Resources.MergedDictionaries.Clear();
        tabRoot.Resources.Clear();
        tabRoot = null;

        ColorPicker.Resources.MergedDictionaries.Clear();
        ColorPicker.Resources.Clear();
        ColorPicker = null;
        DataContext = null;

        Resources.MergedDictionaries.Clear();
        Resources.Clear();
        Resources = null;

        Content = null;
        Raise(null);
    }

    public void ApplySettings() { ApplySettingsRec(tabRoot); }
    public void ApplySettingsRec(Visual parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            Visual visual  = (Visual)VisualTreeHelper.GetChild(parent, i);

            if (visual == null) break;
            if (visual is FrameworkElement)
            {
                var tag = ((FrameworkElement)visual).Tag;
                if (tag != null && tag.ToString() == "_save")
                {
                    switch (visual)
                    {
                        case System.Windows.Controls.Primitives.ToggleButton b:
                            b.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty).UpdateSource();
                            break;
                        case TextBox t:
                            t.GetBindingExpression(TextBox.TextProperty).UpdateSource();
                            break;
                        case ComboBox c:
                            c.GetBindingExpression(ComboBox.SelectedItemProperty).UpdateSource();
                            break;
                    }
                }
            }

            ApplySettingsRec(visual);
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    internal void Raise([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new(propertyName));
    internal void Set<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "")
    {
        if (!check || (field == null && value != null) || (field != null && !field.Equals(value)))
        {
            field = value;
            PropertyChanged?.Invoke(this, new(propertyName));
        }
    }

    private void PluginValueChanged(object sender, RoutedEventArgs e)
        => flyleaf.Config.Plugins[cmbPlugins.Text][((TextBlock)((Panel)((FrameworkElement)sender).Parent).Children[0]).Text] = ((TextBox)sender).Text;

    private void NamedColors_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ColorPicker.Color = ((KeyValuePair<string, Color>)NamedColors.SelectedItem).Value;

    public static IEnumerable<KeyValuePair<string, Color>> AllNamedColors => typeof(Colors).GetProperties()
            .Where(prop => typeof(Color).IsAssignableFrom(prop.PropertyType))
            .Select(prop => new KeyValuePair<string, Color>(prop.Name, (Color)prop.GetValue(null)));

    private void ValidationHex              (object sender, TextCompositionEventArgs e) => e.Handled = !RegHex().IsMatch(e.Text);
    private void ValidationNumericPositive  (object sender, TextCompositionEventArgs e) => e.Handled = !RegNumPositive().IsMatch(e.Text);
    private void ValidationNumeric          (object sender, TextCompositionEventArgs e) => e.Handled = !RegNum().IsMatch(e.Text);
    private void ValidationRatio            (object sender, TextCompositionEventArgs e) => e.Handled = !RegRatio().IsMatch(e.Text);

    [GeneratedRegex(@"^[0-9a-f]+$", RegexOptions.IgnoreCase, "en-150")]
    private static partial Regex RegHex();
    [GeneratedRegex(@"^[0-9]+$")]
    private static partial Regex RegNumPositive();
    [GeneratedRegex(@"^-?[0-9]*$")]
    private static partial Regex RegNum();
    [GeneratedRegex(@"^[0-9\.\,\/\:]+$")]
    private static partial Regex RegRatio();
}

public class ColorHexRule : ValidationRule
{
    public ColorHexRule() { }

    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        if (value != null && Regex.IsMatch(value.ToString(), @"^[0-9a-f]{6}$", RegexOptions.IgnoreCase))
            return new(true, null);

        return new(false, "Invalid");
    }

}
