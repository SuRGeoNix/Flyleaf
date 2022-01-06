using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using WpfColorFontDialog;
using MaterialDesignThemes.Wpf;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls.WPF
{
    public partial class Flyleaf : UserControl, INotifyPropertyChanged, IVideoView
    {
        #region Properties
        public string       UIConfigPath        { get; set; }
        public string       ConfigPath          { get; set; }

        public Player       Player
        { 
            get => _Player;
            set
            {
                var oldPlayer = _Player;

                Set(ref _Player, value);
                Raise("Config");
                InitializePlayer(oldPlayer);
            } 
        }
        Player _Player;

        public Config       Config              => Player?.Config;

        public AudioMaster  AudioMaster         => Master.AudioMaster;

        public SerializableDictionary<string, SerializableDictionary<string, string>>
                            PluginsConfig       => Config?.Plugins;

        public ObservableCollection<UITheme> 
                            UIThemes            { get; set; } = new ObservableCollection<UITheme>();

        public string       ErrorMsg            { get => _ErrorMsg; set => Set(ref _ErrorMsg, value); }
        string _ErrorMsg;

        public bool         ShowDebug           { get => _ShowDebug; set { Set(ref _ShowDebug, value); Config.Player.Stats = value; } }
        bool _ShowDebug;

        public bool         CanPaste            { get => _CanPaste; set => Set(ref _CanPaste, value); }
        bool _CanPaste;

        public string       SubtitlesFontDesc   { get => _SubtitlesFontDesc; set => Set(ref _SubtitlesFontDesc, value); }
        string _SubtitlesFontDesc;

        public Brush        SubtitlesFontColor  { get => _SubtitlesFontColor; set => Set(ref _SubtitlesFontColor, value); }
        Brush _SubtitlesFontColor;

        public TextBlock    Subtitles           { get; set; }
        public int          UniqueId            { get; set; }

        public string SelectedThemeStr
        {
            get => _SelectedTheme?.Name;
            set
            {
                if (_SelectedThemeStr == value || value == null) return;
                Set(ref _SelectedThemeStr, value);

                foreach (var uitheme in UIThemes)
                    if (uitheme.Name == value) SelectedTheme = uitheme;
            }
        }
        string _SelectedThemeStr;

        public UITheme SelectedTheme
        {
            get => _SelectedTheme;
            set
            {
                if (_SelectedTheme != null && _SelectedTheme.Name == value.Name) return;
                Set(ref _SelectedTheme, value);
                ITheme theme = Resources.GetTheme();
                theme.SetPrimaryColor(value.PrimaryColor);
                theme.SetSecondaryColor(value.SecondaryColor);
                theme.Paper = value.PaperColor;
                Resources.SetTheme(theme);
                settings?.Resources.SetTheme(theme);

                if (Config != null && Config.Video != null)
                    Config.Video.BackgroundColor = value.VideoView;
            }
        }
        UITheme _SelectedTheme;

        public Color SelectedColor  { 
            get => _SelectedColor;
            set
            {
                if (_SelectedColor == value) return;

                Set(ref _SelectedColor, value);

                switch (selectedColor)
                {
                    case "Primary":
                        ((UITheme)settings.cmbThemes.SelectedItem).PrimaryColor = value;
                        break;

                    case "Secondary":
                        ((UITheme)settings.cmbThemes.SelectedItem).SecondaryColor = value;
                        break;

                    case "Paper":
                        ((UITheme)settings.cmbThemes.SelectedItem).PaperColor = value;
                        break;

                    case "VideoView":
                        ((UITheme)settings.cmbThemes.SelectedItem).VideoView = value;
                        break;
                }
            }
        }
        Color _SelectedColor;
        Color  selectedColorPrev;
        string selectedColor;

        public IEnumerable<KeyValuePair<String, Color>> NamedColors { get; private set; }
        private IEnumerable<KeyValuePair<String, Color>> GetColors()
        {
            return typeof(Colors)
                .GetProperties()
                .Where(prop =>
                    typeof(Color).IsAssignableFrom(prop.PropertyType))
                .Select(prop =>
                    new KeyValuePair<String, Color>(prop.Name, (Color)prop.GetValue(null)));
        }
        
        #endregion

        #region Initialize
        internal Settings
                    settings;
        UITheme     defaultTheme;

        ContextMenu popUpMenu, popUpMenuSubtitles, popUpMenuVideo;
        MenuItem    popUpAspectRatio;
        MenuItem    popUpKeepAspectRatio;
        MenuItem    popUpCustomAspectRatio;
        MenuItem    popUpCustomAspectRatioSet;
        string      dialogSettingsIdentifier;

        Thickness   subsInitialMargin;

        bool        isDesignMode = (bool) DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;
        bool        prevActivityMode;
        bool        disposed;

        static Flyleaf()
        {
            Master.UIRefresh = true; // Allow UI Refresh for Activity Mode, Buffered Duration on Pause & Stats

            Player.SwapCompleted += (o, e) =>
            {
                var flyleaf1 = (Flyleaf)e.Player1.Tag;
                var flyleaf2 = (Flyleaf)e.Player2.Tag;

                var saveMargin  = flyleaf1.Subtitles.Margin;
                var saveFontSize= flyleaf1.Subtitles.FontSize;

                flyleaf1.Subtitles.Margin   = flyleaf2.Subtitles.Margin;
                flyleaf1.Subtitles.FontSize = flyleaf2.Subtitles.FontSize;

                flyleaf2.Subtitles.Margin   = saveMargin;
                flyleaf2.Subtitles.FontSize = saveFontSize;

                flyleaf1.Player = e.Player2;
                flyleaf2.Player = e.Player1;
            };
        }
        public Flyleaf()
        {
            InitializeComponent();
            if (isDesignMode) return;

            DataContext = this;
        }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (isDesignMode) return;

            Initialize();
        }
        private void Initialize()
        {
            NamedColors = GetColors();

            popUpMenu           = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner", this))?.ContextMenu;
            popUpMenuSubtitles  = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner_Subtitles", this))?.ContextMenu;
            popUpMenuVideo      = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner_Video", this))?.ContextMenu;
            Subtitles           = (TextBlock) Template.FindName("PART_Subtitles", this);

            var dialogSettings  = (DialogHost)Template.FindName("PART_DialogSettings", this);
            if (dialogSettings != null)
            {
                dialogSettingsIdentifier = $"DialogSettings_{Guid.NewGuid()}";
                dialogSettings.Identifier = dialogSettingsIdentifier;
            }

            if (popUpMenu != null)
                popUpMenu.PlacementTarget = this;

            if (popUpMenuSubtitles != null)
            {
                popUpMenuSubtitles.PlacementTarget = this;
                popUpMenuSubtitles.Opened += (o, e) => { prevActivityMode = Config.Player.ActivityMode; Config.Player.ActivityMode = false; };
                popUpMenuSubtitles.Closed += (o, e) => { Config.Player.ActivityMode = prevActivityMode == true; };
            }

            if (popUpMenuVideo != null)
            {
                popUpMenuVideo.PlacementTarget = this;
                popUpMenuVideo.Opened += (o, e) => { prevActivityMode = Config.Player.ActivityMode; Config.Player.ActivityMode = false; };
                popUpMenuVideo.Closed += (o, e) => { Config.Player.ActivityMode = prevActivityMode == true; };
            }

            if (popUpMenu != null)
            {
                var videoItem = from object item in popUpMenu.Items where item is MenuItem && ((MenuItem)item).Header != null && ((MenuItem)item).Header.ToString() == "Video" select item;
                var aspectRatioItem = from object item in ((MenuItem)videoItem.ToArray()[0]).Items where ((MenuItem)item).Header != null && ((MenuItem)item).Header.ToString() == "Aspect Ratio" select item;
                popUpAspectRatio = (MenuItem)aspectRatioItem.ToArray()[0];
            }

            if (popUpAspectRatio != null)
            {
                foreach (var aspectRatio in AspectRatio.AspectRatios)
                {
                    if (aspectRatio == AspectRatio.Custom) continue;
                    popUpAspectRatio.Items.Add(new MenuItem() { Header = aspectRatio, IsCheckable = true });
                    if (aspectRatio == AspectRatio.Keep) popUpKeepAspectRatio = (MenuItem)popUpAspectRatio.Items[popUpAspectRatio.Items.Count - 1];
                }

                popUpCustomAspectRatio = new MenuItem() { IsCheckable = true };
                popUpCustomAspectRatioSet = new MenuItem() { Header = "Set Custom..." };
                popUpCustomAspectRatioSet.Click += (n1, n2) => { DialogAspectRatio(); };

                popUpAspectRatio.Items.Add(popUpCustomAspectRatio);
                popUpAspectRatio.Items.Add(popUpCustomAspectRatioSet);

                popUpMenu.Closed += (o, e) =>
                {
                    if (!prevActivityMode)
                        return;

                    // Workaround to re-enable activity mode
                    if (!Player.IsOpenFileDialogOpen)
                        Config.Player.ActivityMode = true;
                    else
                    {
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            while (Player.IsOpenFileDialogOpen)
                                Thread.Sleep(50);

                            Config.Player.ActivityMode = true;
                        });
                    }
                };

                popUpMenu.Opened += (o, e) =>
                {
                    prevActivityMode = Config.Player.ActivityMode; Config.Player.ActivityMode = false;
                    CanPaste = String.IsNullOrEmpty(Clipboard.GetText()) ? false : true;
                    popUpCustomAspectRatio.Header = $"Custom ({Config.Video.CustomAspectRatio})";
                    FixMenuSingleCheck(popUpAspectRatio, Config.Video.AspectRatio.ToString());
                    if (Config.Video.AspectRatio == AspectRatio.Custom)
                        popUpCustomAspectRatio.IsChecked = true;
                    else if (Config.Video.AspectRatio == AspectRatio.Keep)
                        popUpKeepAspectRatio.IsChecked = true;
                };

                KeyUp += (o, e) =>
                {
                    if (e.Key == Key.Escape && dialogSettingsIdentifier != null && DialogHost.IsDialogOpen(dialogSettingsIdentifier))
                        DialogHost.Close(dialogSettingsIdentifier, "cancel");
                };
            }

            RegisterCommands();

            if (Subtitles != null)
            {
                subsInitialMargin   = Subtitles.Margin;
                SubtitlesFontDesc   = $"{Subtitles.FontFamily} ({Subtitles.FontWeight}), {Subtitles.FontSize}";
                SubtitlesFontColor  = Subtitles.Foreground;
            }

            ITheme theme = Resources.GetTheme();
            defaultTheme = new UITheme(this, defaultTheme) { Name = "Default", PrimaryColor = theme.PrimaryMid.Color, SecondaryColor = theme.SecondaryMid.Color, PaperColor = theme.Paper, VideoView = Config != null && Config.Video != null ? Config.Video.BackgroundColor : Colors.Black};

            if (UIConfigPath != null)
                UIConfig.Load(this, UIConfigPath);

            if (UIThemes == null || UIThemes.Count == 0)
            {
                UIThemes.Add(new UITheme(this, defaultTheme) { Name= "Black & White",       PrimaryColor = Colors.White, SecondaryColor = Colors.White, PaperColor = Colors.Black, VideoView = Colors.Black });
                UIThemes.Add(new UITheme(this, defaultTheme) { Name= "Blue & Red",          PrimaryColor = Colors.DodgerBlue, SecondaryColor = (Color)ColorConverter.ConvertFromString("#e00000"), PaperColor = Colors.Black, VideoView = Colors.Black });
                UIThemes.Add(new UITheme(this, defaultTheme) { Name= "Orange",              PrimaryColor = (Color)ColorConverter.ConvertFromString("#ff8300"), SecondaryColor = Colors.White, PaperColor = Colors.Black, VideoView = Colors.Black });
                UIThemes.Add(new UITheme(this, defaultTheme) { Name= "Firebrick",           PrimaryColor = Colors.Firebrick, SecondaryColor = Colors.White, PaperColor = Colors.Black, VideoView = Colors.Black });
                UIThemes.Add(new UITheme(this, defaultTheme) { Name= "Fuchia,Lime & Blue",  PrimaryColor = (Color)ColorConverter.ConvertFromString("#e615e6"), SecondaryColor = Colors.Lime, PaperColor =(Color)ColorConverter.ConvertFromString("#0f1034"), VideoView = (Color)ColorConverter.ConvertFromString("#0f1034") });
                UIThemes.Add(new UITheme(this, defaultTheme) { Name= "Gold & Chocolate",    PrimaryColor = (Color)ColorConverter.ConvertFromString("#ffc73b"), SecondaryColor = Colors.Chocolate, PaperColor = (Color)ColorConverter.ConvertFromString("#3b1212"), VideoView = (Color)ColorConverter.ConvertFromString("#3b1212") });
                UIThemes.Add(new UITheme(this, defaultTheme) { Name= "Green & Brown",       PrimaryColor = (Color)ColorConverter.ConvertFromString("#24b03b"), SecondaryColor = (Color)ColorConverter.ConvertFromString("#e66102"), PaperColor = Colors.Black, VideoView = Colors.Black });
                UIThemes.Add(new UITheme(this, defaultTheme) { Name= "Custom",              PrimaryColor = Colors.Orange, SecondaryColor = Colors.White, VideoView = Colors.Black });
            }

            if (string.IsNullOrEmpty(SelectedThemeStr))
                SelectedTheme = UIThemes[3];
            
            Raise(null);
            settings?.Raise(null);
        }
        private void InitializePlayer(Player oldPlayer = null)
        {
            // Updates the key binding actions with the new instances in case of swap or initial load
            bool SubsYUp = false, SubsYDown = false, SubsFontIncrease = false, SubsFontDecrease = false;
            Action aSubsYUp =           () => { Thickness t = Subtitles.Margin; t.Bottom += 2; Subtitles.Margin = t; Raise(nameof(Subtitles)); };
            Action aSubsYDown =         () => { Thickness t = Subtitles.Margin; t.Bottom -= 2; Subtitles.Margin = t; Raise(nameof(Subtitles)); };
            Action aSubsFontIncrease =  () => { Subtitles.FontSize += 2; };
            Action aSubsFontDecrease =  () => { Subtitles.FontSize -= 2; };

            Config.Player.ActivityMode = true; // To allow Idle mode on flyleafBar
            Config.Player.KeyBindings.FlyleafWindow = true; // To allow keybindings also on front window

            if (Config.Player.KeyBindings.Enabled)
            {
                // Update Actions if loaded from Config file (TBR: possible also on swap?)
                foreach (var binding in Config.Player.KeyBindings.Keys)
                {
                    if (binding.Action != KeyBindingAction.Custom || string.IsNullOrEmpty(binding.ActionName))
                        continue; 

                    switch (binding.ActionName)
                    {
                        case "SubsYUp":
                            binding.SetAction(aSubsYUp, false);
                            SubsYUp = true;
                            break;

                        case "SubsYDown":
                            binding.SetAction(aSubsYDown, false);
                            SubsYDown = true;
                            break;

                        case "SubsFontIncrease":
                            binding.SetAction(aSubsFontIncrease, false);
                            SubsFontIncrease = true;
                            break;

                        case "SubsFontDecrease":
                            binding.SetAction(aSubsFontDecrease, false);
                            SubsFontDecrease = true;
                            break;
                    }
                }
            }

            Player.Tag = this;

            if (oldPlayer != null)
            {
                Log($"Assigning {Player.PlayerId} | {(oldPlayer != null ? $"Old {oldPlayer.PlayerId}" : "")}"); 
                return;
            }

            UniqueId = Player.PlayerId;
            Log($"Assigning {Player.PlayerId} | {(oldPlayer != null ? $"Old {oldPlayer.PlayerId}" : "")}"); 

            Unloaded += (o, e) => { Dispose(); };
            Player.Control.MouseClick   += (o, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Right & popUpMenu != null) popUpMenu.IsOpen = true; };
            MouseDown += (o, e) => { Player?.Activity.ForceFullActive(); };
            MouseMove += (o, e) => { Player?.Activity.ForceFullActive(); };

            if (defaultTheme != null)
                defaultTheme.VideoView = Config.Video.BackgroundColor;

            if (SelectedTheme != null)
                Config.Video.BackgroundColor = SelectedTheme.VideoView;

            // Additional Key Bindings for Subtitles
            if (Config.Player.KeyBindings.Enabled && !Config.Loaded)
            {
                if (!SubsYUp)
                    Config.Player.KeyBindings.AddCustom(Key.Up,     false, aSubsYUp,           "SubsYUp",          true);
                if (!SubsYDown)
                    Config.Player.KeyBindings.AddCustom(Key.Down,   false, aSubsYDown,         "SubsYDown",        true);
                if (!SubsFontIncrease)
                    Config.Player.KeyBindings.AddCustom(Key.Right,  false, aSubsFontIncrease,  "SubsFontIncrease", true);
                if (!SubsFontDecrease)
                    Config.Player.KeyBindings.AddCustom(Key.Left,   false, aSubsFontDecrease,  "SubsFontDecrease", true);
            }

            Raise(null);
            settings?.Raise(null);
        }
        public void UnsubscribePlayer()
        {
            if (Player == null)
                return;
        }
        public void Dispose()
        {
            lock (this)
            {
                // TBR: Possible leak with Dragablz?
                if (disposed) return;

                UnsubscribePlayer();

                //VideoView?.WindowFront?.Close();
                Player?.Dispose();
                settings?.Dispose();
                settings = null;

                popUpMenu?.Resources?.MergedDictionaries.Clear();
                popUpMenu?.Resources?.Clear();
                popUpMenu = null;

                popUpMenuVideo?.Resources?.MergedDictionaries.Clear();
                popUpMenuVideo?.Resources?.Clear();
                popUpMenuVideo = null;

                popUpMenuSubtitles?.Resources?.MergedDictionaries.Clear();
                popUpMenuSubtitles?.Resources?.Clear();
                popUpMenuSubtitles = null;

                Resources.MergedDictionaries.Clear();
                Resources.Clear();
                Template.Resources.MergedDictionaries.Clear();
                Content = null;
                DataContext = null;
                disposed = true;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                Raise(null);
            }
        }
        #endregion

        #region ICommands
        void RegisterCommands()
        {
            OpenSettings        = new RelayCommand(OpenSettingsAction);
            OpenColorPicker     = new RelayCommand(OpenColorPickerAction);
            ChangeAspectRatio   = new RelayCommand(ChangeAspectRatioAction);
            SetSubtitlesFont    = new RelayCommand(SetSubtitlesFontAction);
            ExitApplication     = new RelayCommand(ExitApplicationAction);
            SetSubsPositionY    = new RelayCommand(SetSubsPositionYAction);
            ResetSubsPositionY  = new RelayCommand(ResetSubsPositionYAction);
            ShowSubtitlesMenu   = new RelayCommand(ShowSubtitlesMenuAction);
            ShowVideoMenu       = new RelayCommand(ShowVideoMenuAction);
        }

        public ICommand ExitApplication { get ; set; }
        public void ExitApplicationAction(object obj = null) { Application.Current.Shutdown(); }
        public ICommand ShowSubtitlesMenu { get; set; }
        public void ShowSubtitlesMenuAction(object obj = null) { popUpMenuSubtitles.IsOpen = true; }

        public ICommand ShowVideoMenu { get; set; }
        public void ShowVideoMenuAction(object obj = null) { popUpMenuVideo.IsOpen = true; }
        public ICommand OpenSettings        { get; set; }
        public async void OpenSettingsAction(object obj = null)
        {
            if (Config == null || dialogSettingsIdentifier == null)
                return;

            if (DialogHost.IsDialogOpen(dialogSettingsIdentifier))
            {
                DialogHost.Close(dialogSettingsIdentifier, "cancel");
                return;
            }

            if (settings == null)
                settings = new Settings(this);

            Config.Player.ActivityMode = false;
            Config.Player.KeyBindings.Enabled = false;

            var prevConfig = Config.Video.Clone();
            var result = await DialogHost.Show(settings, dialogSettingsIdentifier);

            Config.Player.ActivityMode = true;
            Config.Player.KeyBindings.Enabled = true;

            if (result == null) return;

            if (result.ToString() == "cancel")
            {
                Config.Video.HDRtoSDRMethod  = prevConfig.HDRtoSDRMethod;
                Config.Video.HDRtoSDRTone    = prevConfig.HDRtoSDRTone;
                Config.Video.Contrast        = prevConfig.Contrast;
                Config.Video.Brightness      = prevConfig.Brightness;
            }
            else
            {
                settings.ApplySettings();
                if (result.ToString() == "save")
                    UIConfig.Save(this, UIConfigPath, ConfigPath);
            }
        }

        public ICommand OpenColorPicker { get; set; }
        public async void OpenColorPickerAction(object curColor)
        {
            selectedColor = curColor.ToString(); 

            if (selectedColor == "Primary")
                SelectedColor = ((UITheme)settings.cmbThemes.SelectedItem).PrimaryColor;
            else if (selectedColor == "Secondary")
                SelectedColor = ((UITheme)settings.cmbThemes.SelectedItem).SecondaryColor;
            else if (selectedColor == "Paper")
                SelectedColor = ((UITheme)settings.cmbThemes.SelectedItem).PaperColor;
            else if (selectedColor == "VideoView")
                SelectedColor = ((UITheme)settings.cmbThemes.SelectedItem).VideoView;

            selectedColorPrev = SelectedColor;
            var result = await DialogHost.Show(settings.ColorPickerDialog.DialogContent, "ColorPickerDialog");
            if (result != null && result.ToString() == "cancel")
                SelectedColor = selectedColorPrev;
        }

        public ICommand ChangeAspectRatio { get; set; }
        public void ChangeAspectRatioAction(object obj = null)
        {
            MenuItem mi = ((MenuItem)obj);

            if (Regex.IsMatch(mi.Header.ToString(), "Custom"))
            {
                if (Regex.IsMatch(mi.Header.ToString(), "Set")) return;
                Config.Video.AspectRatio = AspectRatio.Custom;
                return;
            }
            else if (Regex.IsMatch(mi.Header.ToString(), "Keep"))
                Config.Video.AspectRatio = AspectRatio.Keep;
            else
                Config.Video.AspectRatio = mi.Header.ToString();
        }

        public ICommand ResetSubsPositionY { get; set; }
        public void ResetSubsPositionYAction(object obj = null) { Subtitles.Margin = subsInitialMargin; }

        public ICommand SetSubsPositionY { get; set; }
        public void SetSubsPositionYAction(object y) { Thickness t = Subtitles.Margin; t.Bottom += int.Parse(y.ToString()); Subtitles.Margin = t; Raise(nameof(Subtitles)); }

        public ICommand SetSubtitlesFont    { get; set; }
        public void SetSubtitlesFontAction(object obj = null)
        {
            ColorFontDialog dialog  = new ColorFontDialog();
            dialog.Font = new FontInfo(Subtitles.FontFamily, Subtitles.FontSize, Subtitles.FontStyle, Subtitles.FontStretch, Subtitles.FontWeight, (SolidColorBrush) Subtitles.Foreground);

            if (dialog.ShowDialog() == true && dialog.Font != null)
            {
                Subtitles.FontFamily    = dialog.Font.Family;
                Subtitles.FontSize      = dialog.Font.Size;
                Subtitles.FontWeight    = dialog.Font.Weight;
                Subtitles.FontStretch   = dialog.Font.Stretch;
                Subtitles.FontStyle     = dialog.Font.Style;
                Subtitles.Foreground    = dialog.Font.BrushColor;

                SubtitlesFontDesc       = $"{Subtitles.FontFamily} ({Subtitles.FontWeight}), {Subtitles.FontSize}";
                SubtitlesFontColor      = Subtitles.Foreground;
            }
        }
        #endregion

        #region TODO
        private async void DialogAspectRatio()
        {
            if (dialogSettingsIdentifier == null) return;
            if (DialogHost.IsDialogOpen(dialogSettingsIdentifier)) return;

            var stackVertical    = new StackPanel() { Height=100, Orientation = Orientation.Vertical };
            var stackHorizontal1 = new StackPanel() { Margin = new Thickness(10), Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center};
            var stackHorizontal2 = new StackPanel() { Margin = new Thickness(10), Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };

            var textBox = new TextBox() { VerticalAlignment = VerticalAlignment.Center, Width = 70, Margin = new Thickness(10, 0, 0, 0), Text=Config.Video.CustomAspectRatio.ToString()};
            textBox.PreviewTextInput += (n1, n2) => { n2.Handled = !Regex.IsMatch(n2.Text, @"^[0-9\.\,\/\:]+$"); };

            var buttonOK = new Button() { Content = "OK" };
            var buttonCancel = new Button() { Margin = new Thickness(10, 0, 0, 0), Content = "Cancel" };

            buttonOK.Click +=       (n1, n2) => { if (textBox.Text != AspectRatio.Invalid) Config.Video.CustomAspectRatio = textBox.Text; DialogHost.Close(dialogSettingsIdentifier); };
            buttonCancel.Click +=   (n1, n2) => { DialogHost.Close(dialogSettingsIdentifier); };

            stackHorizontal1.Children.Add(new TextBlock() { VerticalAlignment = VerticalAlignment.Center, Text="Set Custom Ratio: "});
            stackHorizontal1.Children.Add(textBox);
            stackHorizontal2.Children.Add(buttonOK);
            stackHorizontal2.Children.Add(buttonCancel);

            stackVertical.Children.Add(stackHorizontal1);
            stackVertical.Children.Add(stackHorizontal2);

            stackVertical.Orientation = Orientation.Vertical;
            stackVertical.Resources = Resources;
            stackVertical.Resources.SetTheme(Resources.GetTheme());
            var result = await DialogHost.Show(stackVertical, dialogSettingsIdentifier);
        }
        private void FixMenuSingleCheck(MenuItem mi, string checkedItem = null)
        {
            foreach (var item in mi.Items)
            {
                if (checkedItem != null && ((MenuItem)item).Header.ToString() == checkedItem)
                    ((MenuItem)item).IsChecked = true;
                else
                    ((MenuItem)item).IsChecked = false;
            }
        }
        #endregion

        #region Property Change
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
        #endregion

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Player] {msg}"); }
    }
}