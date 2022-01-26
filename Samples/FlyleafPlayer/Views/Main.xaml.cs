/* This is Sample is under development */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafPlayer.Commands;
using MaterialDesignThemes.Wpf;

namespace FlyleafPlayer.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Main : Window
    {
        public Player Player { get; set; }
        public Config Config { get; set; }
        public FrameworkElement Paok123 { get; set; }
        public MainCommands Commands { get; set; } = new MainCommands();
        public ObservableCollection<UITheme> 
                            UIThemes            { get; set; } = new ObservableCollection<UITheme>();

        public Main()
        {
            // Ensures that we have enough worker threads to avoid the UI from freezing or not updating on time
            ThreadPool.GetMinThreads(out int workers, out int ports);
            ThreadPool.SetMinThreads(workers + 6, ports + 6);

            // Initializes Engine (Specifies FFmpeg libraries path which is required)
            Engine.Start(new EngineConfig()
            {
                #if DEBUG
                LogOutput           = ":debug",
                LogLevel            = LogLevel.Debug,
                FFmpegLogLevel      = FFmpegLogLevel.Warning,
                #endif
                
                PluginsPath         = ":Plugins",
                FFmpegPath          = ":FFmpeg",
                HighPerformaceTimers= false,
                UIRefresh           = true
            });

            Config = new Config();
            Config.Player.ActivityMode = true;
            Config.Player.MouseBindings.Enabled = true;
            //Config.Player.MouseBindings.WinMoveOnDrag = true;
            //Config.Player.KeyBindings.FlyleafWindow = true;
            Player = new Player(Config);
            DataContext = this;

            InitializeComponent();

            Paok123 = TestName;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Player.VideoView.WinFormsHost.MouseDown += (o, e) =>
            {
                System.Diagnostics.Debug.WriteLine("WinFormsHost");
            };

            Player.VideoView.WinFormsHost.Child.MouseDown += (o, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Child");
            };

            Player.VideoView.WindowFront.MouseDown += (o, e) =>
            {
                System.Diagnostics.Debug.WriteLine("WindowFront");
            };

            MouseDown += (o, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Me");
            };

            
            ITheme theme = paletteHelper.GetTheme();
            defaultTheme = new UITheme(paletteHelper, defaultTheme) { Name = "Default", PrimaryColor = theme.PrimaryMid.Color, SecondaryColor = theme.SecondaryMid.Color, PaperColor = theme.Paper, VideoView = Config != null && Config.Video != null ? Config.Video.BackgroundColor : Colors.Black};
            UIThemes.Add(new UITheme(paletteHelper, defaultTheme) { Name= "Black & White",       PrimaryColor = Colors.White, SecondaryColor = Colors.White, PaperColor = Colors.Black, VideoView = Colors.Black });
            UIThemes.Add(new UITheme(paletteHelper, defaultTheme) { Name= "Blue & Red",          PrimaryColor = Colors.DodgerBlue, SecondaryColor = (Color)ColorConverter.ConvertFromString("#e00000"), PaperColor = Colors.Black, VideoView = Colors.Black });
            UIThemes.Add(new UITheme(paletteHelper, defaultTheme) { Name= "Orange",              PrimaryColor = (Color)ColorConverter.ConvertFromString("#ff8300"), SecondaryColor = Colors.White, PaperColor = Colors.Black, VideoView = Colors.Black });
            UIThemes.Add(new UITheme(paletteHelper, defaultTheme) { Name= "Firebrick",           PrimaryColor = Colors.Firebrick, SecondaryColor = Colors.White, PaperColor = Colors.Black, VideoView = Colors.Black });
            UIThemes.Add(new UITheme(paletteHelper, defaultTheme) { Name= "Fuchia,Lime & Blue",  PrimaryColor = (Color)ColorConverter.ConvertFromString("#e615e6"), SecondaryColor = Colors.Lime, PaperColor =(Color)ColorConverter.ConvertFromString("#0f1034"), VideoView = (Color)ColorConverter.ConvertFromString("#0f1034") });
            UIThemes.Add(new UITheme(paletteHelper, defaultTheme) { Name= "Gold & Chocolate",    PrimaryColor = (Color)ColorConverter.ConvertFromString("#ffc73b"), SecondaryColor = Colors.Chocolate, PaperColor = (Color)ColorConverter.ConvertFromString("#3b1212"), VideoView = (Color)ColorConverter.ConvertFromString("#3b1212") });
            UIThemes.Add(new UITheme(paletteHelper, defaultTheme) { Name= "Green & Brown",       PrimaryColor = (Color)ColorConverter.ConvertFromString("#24b03b"), SecondaryColor = (Color)ColorConverter.ConvertFromString("#e66102"), PaperColor = Colors.Black, VideoView = Colors.Black });
            UIThemes.Add(new UITheme(paletteHelper, defaultTheme) { Name= "Custom",              PrimaryColor = Colors.Orange, SecondaryColor = Colors.White, VideoView = Colors.Black });

            SelectedTheme = UIThemes[5];
        }
        PaletteHelper paletteHelper = new PaletteHelper();
        UITheme defaultTheme;
        public UITheme SelectedTheme
        {
            get => _SelectedTheme;
            set
            {
                if (_SelectedTheme != null && _SelectedTheme.Name == value.Name)
                    return;
                _SelectedTheme = value;
                //Set(ref _SelectedTheme, value);
                ITheme theme = paletteHelper.GetTheme();
                theme.SetPrimaryColor(value.PrimaryColor);
                theme.SetSecondaryColor(value.SecondaryColor);
                theme.Paper = value.PaperColor;
                paletteHelper.SetTheme(theme);

                if (Config != null && Config.Video != null)
                    Config.Video.BackgroundColor = value.VideoView;
            }
        }
        UITheme _SelectedTheme;

        public class UITheme : NotifyPropertyChanged
    {
        public PaletteHelper flyleaf;

        public UITheme() { }

        public UITheme(PaletteHelper flyleaf, UITheme baseTheme)
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
                //if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                ITheme theme = flyleaf.GetTheme();
                theme.SetPrimaryColor(value);
                flyleaf.SetTheme(theme);
            }
        }
        Color _PrimaryColor;

        public Color SecondaryColor { 
            get => _SecondaryColor;
            set
            {
                if (!Set(ref _SecondaryColor, value)) return;
                //if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                ITheme theme = flyleaf.GetTheme();
                theme.SetSecondaryColor(value);
                flyleaf.SetTheme(theme);
            }
        }
        Color _SecondaryColor;

        public Color PaperColor { 
            get => _PaperColor;
            set
            {
                if (!Set(ref _PaperColor, value)) return;
                //if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                ITheme theme = flyleaf.GetTheme();
                theme.Paper = value;
                flyleaf.SetTheme(theme);
            }
        }
        Color _PaperColor;

        public Color VideoView      {
            get => _VideoView;
            set 
            {
                if (!Set(ref _VideoView, value)) return;
                //if (flyleaf == null || flyleaf.SelectedTheme == null || flyleaf.SelectedTheme.Name != Name) return;

                //flyleaf.Config.Video.BackgroundColor = value;
            }
        }
        Color _VideoView;
    }
    }
}
