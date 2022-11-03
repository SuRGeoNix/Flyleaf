using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;

using MaterialDesignThemes.Wpf;

namespace FlyleafLib.Controls.WPF
{
    public class UIConfig : NotifyPropertyChanged
    {
        [XmlIgnore]
        public bool         Loaded          { get; private set; }

        public ObservableCollection<UITheme> 
                            Themes          { get; set; }
        public string       SelectedTheme   { get => _SelectedTheme;    set => Set(ref _SelectedTheme, value); }
        string _SelectedTheme;

        public string       SubsFontFamily  { get => _SubsFontFamily;   set => Set(ref _SubsFontFamily, value); }
        string _SubsFontFamily;
        public double       SubsFontSize    { get => _SubsFontSize;     set => Set(ref _SubsFontSize, value); }
        double _SubsFontSize;
        public Color        SubsFontColor   { get => _SubsFontColor;    set => Set(ref _SubsFontColor, value); }
        Color _SubsFontColor;
        public string       SubsFontStretch { get => _SubsFontStretch;  set => Set(ref _SubsFontStretch, value); }
        string _SubsFontStretch;
        public string       SubsFontWeight  { get => _SubsFontWeight;   set => Set(ref _SubsFontWeight, value); }
        string _SubsFontWeight;
        public string       SubsFontStyle   { get => _SubsFontStyle;    set => Set(ref _SubsFontStyle, value); }
        string _SubsFontStyle;
        public Thickness    SubsMargin      { get => _SubsMargin;       set => Set(ref _SubsMargin, value); }
        Thickness _SubsMargin;

        internal FlyleafME flyleaf;

        public UIConfig() { }
        public UIConfig(FlyleafME flyleaf) { this.flyleaf = flyleaf; }

        public static void Load(FlyleafME flyleaf, string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(UIConfig));
                flyleaf.UIConfig = (UIConfig) xmlSerializer.Deserialize(fs);

                flyleaf.UIConfig.Loaded = true;
                flyleaf.UIConfig.flyleaf = flyleaf;
                foreach(var theme in flyleaf.UIConfig.Themes)
                    theme.flyleaf = flyleaf;
            }
        }

        public static void Save(FlyleafME flyleaf, string uiConfigPath, string configPath, string enginePath)
        {
            if (uiConfigPath != null)
                using (FileStream fs = new FileStream(uiConfigPath, FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(UIConfig));
                    xmlSerializer.Serialize(fs, flyleaf.UIConfig);
                }

            if (configPath != null)
                flyleaf.Config.Save(configPath);

            if (enginePath != null)
                flyleaf.ConfigEngine.Save(enginePath);
        }
    }

    public class UITheme : NotifyPropertyChanged
    {
        [XmlIgnore]
        public FlyleafME flyleaf;

        public UITheme() { }

        public UITheme(FlyleafME flyleaf, UITheme baseTheme)
        {
            this.flyleaf = flyleaf;

            if (baseTheme != null)
            {
                _PrimaryColor   = baseTheme.PrimaryColor;
                _SecondaryColor = baseTheme.SecondaryColor;
                _PaperColor     = baseTheme.PaperColor;
                _SurfaceColor   = baseTheme.SurfaceColor;
            }
        }

        public string Name { get; set; }
        public Color PrimaryColor   {
            get => _PrimaryColor;
            set
            {
                if (!Set(ref _PrimaryColor, value)) return;
                if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                ITheme theme = flyleaf.Overlay.Resources.GetTheme();
                theme.SetPrimaryColor(value);
                flyleaf.Overlay.Resources.SetTheme(theme);
                flyleaf.settings.Resources.SetTheme(theme);
            }
        }
        Color _PrimaryColor;

        public Color SecondaryColor { 
            get => _SecondaryColor;
            set
            {
                if (!Set(ref _SecondaryColor, value)) return;
                if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                ITheme theme = flyleaf.Overlay.Resources.GetTheme();
                theme.SetSecondaryColor(value);
                flyleaf.Overlay.Resources.SetTheme(theme);
                flyleaf.settings.Resources.SetTheme(theme);
            }
        }
        Color _SecondaryColor;

        public Color PaperColor { 
            get => _PaperColor;
            set
            {
                if (!Set(ref _PaperColor, value)) return;
                if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                ITheme theme = flyleaf.Overlay.Resources.GetTheme();
                theme.Paper = value;
                flyleaf.Overlay.Resources.SetTheme(theme);
                flyleaf.settings.Resources.SetTheme(theme);
            }
        }
        Color _PaperColor;

        public Color SurfaceColor      {
            get => _SurfaceColor;
            set 
            {
                if (!Set(ref _SurfaceColor, value)) return;
                if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                flyleaf.Config.Video.BackgroundColor = value;
            }
        }
        Color _SurfaceColor;
    }
}
