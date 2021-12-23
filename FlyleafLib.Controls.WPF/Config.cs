using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Xml.Serialization;

namespace FlyleafLib.Controls.WPF
{
    public class UIConfig
    {
        public string       SelectedTheme   { get; set; }
        public ObservableCollection<UITheme> 
                            UIThemes        { get; set; }

        public static void Load(Flyleaf flyleaf, string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                using (FileStream fs = new FileStream(path, FileMode.Open))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(UIConfig));
                    UIConfig uIConfig =  (UIConfig) xmlSerializer.Deserialize(fs);

                    flyleaf.UIThemes            = uIConfig.UIThemes;
                    flyleaf.SelectedThemeStr    = uIConfig.SelectedTheme;
                }
            } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e.Message); }
        }

        public static void Save(Flyleaf flyleaf, string uiConfigPath, string configPath)
        {
            try
            {
                UIConfig uIConfig = new UIConfig()
                {
                    UIThemes        = flyleaf.UIThemes,
                    SelectedTheme   = flyleaf.SelectedThemeStr
                };

                if (uiConfigPath != null)
                    using (FileStream fs = new FileStream(uiConfigPath, FileMode.Create))
                    {
                        XmlSerializer xmlSerializer = new XmlSerializer(typeof(UIConfig));
                        xmlSerializer.Serialize(fs, uIConfig);
                    }

                if (configPath != null)
                    flyleaf.Config.Save(configPath);
            } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e.Message); }
        }
    }

    public class UITheme : NotifyPropertyChanged
    {
        [XmlIgnore]
        public Flyleaf flyleaf;

        public UITheme() { }

        public UITheme(Flyleaf flyleaf, UITheme baseTheme)
        {
            this.flyleaf = flyleaf;

            if (baseTheme != null)
            {
                _PrimaryColor   = baseTheme.PrimaryColor;
                _SecondaryColor = baseTheme.SecondaryColor;
                _PaperColor     = baseTheme.PaperColor;
                _VideoView      = baseTheme.VideoView;
            }
        }

        public string Name { get; set; }
        public Color PrimaryColor   {
            get => _PrimaryColor;
            set
            {
                if (!Set(ref _PrimaryColor, value)) return;
                if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                ITheme theme = flyleaf.Resources.GetTheme();
                theme.SetPrimaryColor(value);
                flyleaf.Resources.SetTheme(theme);
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

                ITheme theme = flyleaf.Resources.GetTheme();
                theme.SetSecondaryColor(value);
                flyleaf.Resources.SetTheme(theme);
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

                ITheme theme = flyleaf.Resources.GetTheme();
                theme.Paper = value;
                flyleaf.Resources.SetTheme(theme);
                flyleaf.settings.Resources.SetTheme(theme);
            }
        }
        Color _PaperColor;

        public Color VideoView      {
            get => _VideoView;
            set 
            {
                if (!Set(ref _VideoView, value)) return;
                if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                flyleaf.Config.Video.BackgroundColor = value;
            }
        }
        Color _VideoView;
    }
}
