using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using MaterialDesignThemes.Wpf;

namespace FlyleafLib.Controls.WPF
{
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
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        internal void Set<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "")
        {
            if (!check || (field == null && value != null) || (field != null && !field.Equals(value)))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void ValidationHex(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = !Regex.IsMatch(e.Text, @"^[0-9a-f]+$", RegexOptions.IgnoreCase); }
        private void ValidationNumericPositive(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$"); }
        private void ValidationNumeric(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = !Regex.IsMatch(e.Text, @"^-?[0-9]*$"); }
        private void ValidationRatio(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = !Regex.IsMatch(e.Text, @"^[0-9\.\,\/\:]+$"); }
        
        private void PluginValueChanged(object sender, RoutedEventArgs e)
        {
            string curPlugin = ((TextBlock)((Panel)((FrameworkElement)sender).Parent).Children[0]).Text;

            flyleaf.Config.Plugins[cmbPlugins.Text][curPlugin] = ((TextBox)sender).Text;
        }

        private void NamedColors_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ColorPicker.Color = ((KeyValuePair<string, Color>)NamedColors.SelectedItem).Value;
        }
    }

    public class ColorHexRule : ValidationRule
    {
        public ColorHexRule() { }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value != null && Regex.IsMatch(value.ToString(), @"^[0-9a-f]{6}$", RegexOptions.IgnoreCase))
                return new ValidationResult(true, null);

            return new ValidationResult(false, "Invalid");
        }

    }
}