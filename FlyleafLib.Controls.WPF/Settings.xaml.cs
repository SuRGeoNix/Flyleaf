using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FlyleafLib.Controls.WPF
{
    public partial class Settings : UserControl, INotifyPropertyChanged
    {
        //public Session          Session         { get; set; }
        //public Config.Audio     Audio           => Session.audio;
        //public Config.Subs      Subs            => Session.subs;
        //public Config.Video     Video           => Session.video;
        //public Config.Decoder   Decoder         => Session.decoder;
        //public Config.Demuxer   Demuxer         => Session.demuxer;

        public Settings()//(Session session)
        {
            //Session = session;
            InitializeComponent();
            //DataContext = this;
        }

        public void Closing(object sender, DialogClosingEventArgs eventArgs) { }
        public void Closed(object result) { if (result != null && result.ToString() == "apply") SaveSettings(); }
        public void SaveSettings() { SaveSettingsRec(tabRoot); }
        public void SaveSettingsRec(Visual parent)
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
                                (b.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)).UpdateSource();
                                break;
                            case TextBox t:
                                (t.GetBindingExpression(TextBox.TextProperty)).UpdateSource();
                                break;
                            case ComboBox c:
                                (c.GetBindingExpression(ComboBox.SelectedItemProperty)).UpdateSource();
                                break;
                        } 
                    }
                }

                SaveSettingsRec(visual);
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected void Set<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "")
        {
            if (!check || (field == null && value != null) || (field != null && !field.Equals(value)))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void ValidationNumericPositive(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$"); }
        private void ValidationNumeric(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = !Regex.IsMatch(e.Text, @"^-?[0-9]*$"); }
        private void ValidationRatio(object sender, System.Windows.Input.TextCompositionEventArgs e) { e.Handled = !Regex.IsMatch(e.Text, @"^[0-9\.\,\/\:]+$"); }
    }
}
