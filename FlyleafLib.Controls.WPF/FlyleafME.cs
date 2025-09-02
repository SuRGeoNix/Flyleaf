using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using WpfColorFontDialog;
using MaterialDesignThemes.Wpf;

using FlyleafLib.MediaPlayer;

using static FlyleafLib.Utils;

namespace FlyleafLib.Controls.WPF;

/* TODO
 *
 * Parse user's content into a new PART_grid within the main Grid
 */

public class FlyleafME : FlyleafHost, INotifyPropertyChanged
{
    static FlyleafME()
        => DefaultStyleKeyProperty.OverrideMetadata(typeof(FlyleafME), new FrameworkPropertyMetadata(typeof(FlyleafME)));

    public FlyleafME()
        => Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri("pack://application:,,,/FlyleafLib.Controls.WPF;component/Resources/MaterialDesignColors.xaml") }); // Design Only? (Will affect detached content)

    public FlyleafME(Window standAlone) : base(standAlone) { }

    #region Properties
    public string       UIConfigPath        { get; set; }
    public string       ConfigPath          { get; set; }
    public string       EnginePath          { get; set; }

    public Config       Config              => Player?.Config;

    UIConfig _UIConfig;
    public UIConfig     UIConfig            { get => _UIConfig; set { if (_UIConfig == value) return; _UIConfig = value; Raise(nameof(UIConfig)); } }


    public AudioEngine  AudioEngine         => Engine.Audio;
    public EngineConfig ConfigEngine        => Engine.Config;

    public Dictionary<string, ObservableDictionary<string, string>>
                        PluginsConfig       => Config?.Plugins;

    public bool         ShowDebug           { get => _ShowDebug; set { Set(ref _ShowDebug, value); Config.Player.Stats = value; } }
    bool _ShowDebug;

    public bool         CanPaste            { get => _CanPaste; set => Set(ref _CanPaste, value); }
    bool _CanPaste;

    public UITheme SelectedTheme
    {
        get => _SelectedTheme;
        set
        {
            if (_SelectedTheme != null && _SelectedTheme.Name == value.Name)
                return;

            Set(ref _SelectedTheme, value, false);

            Theme theme;
            var tmp = Overlay.Resources.MergedDictionaries.FirstOrDefault(x => x is IMaterialDesignThemeDictionary);
            if (tmp == null) // Set tmp theme to avoid missing theme keys
            {
                var bndl = new BundledTheme
                {
                    PrimaryColor    = MaterialDesignColors.PrimaryColor.Red,
                    SecondaryColor  = MaterialDesignColors.SecondaryColor.Green,
                    BaseTheme       = BaseTheme.Dark
                };
                theme = bndl.GetTheme();
                Overlay.Resources.MergedDictionaries.Add(bndl);
            }
            else
                theme = Overlay.Resources.GetTheme();

            theme.SetPrimaryColor(value.PrimaryColor);
            theme.SetSecondaryColor(value.SecondaryColor);
            theme.Background = value.BackgroundColor;
            Overlay.Resources.SetTheme(theme);
            settings?.Resources.SetTheme(theme);

            UIConfig.SelectedTheme = value.Name;
            if (Config != null && Config.Video != null)
                Config.Video.BackgroundColor = value.SurfaceColor;
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

                case "Background":
                    ((UITheme)settings.cmbThemes.SelectedItem).BackgroundColor = value;
                    break;

                case "Surface":
                    ((UITheme)settings.cmbThemes.SelectedItem).SurfaceColor = value;
                    break;
            }
        }
    }
    Color _SelectedColor;
    Color  selectedColorPrev;
    string selectedColor;

    public event EventHandler ThemeLoaded;
    #endregion

    bool initialized;
    internal Settings
                settings;
    ContextMenu popUpMenu;
    MenuItem    popUpAspectRatio;
    MenuItem    popUpKeepAspectRatio;
    MenuItem    popUpCustomAspectRatio;
    MenuItem    popUpCustomAspectRatioSet;
    string      dialogSettingsIdentifier;

    Thickness   subsInitialMargin;

    bool        isDesignMode = (bool) DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;

    public override void SetPlayer(Player oldPlayer)
    {
        if (oldPlayer != null)
            oldPlayer.renderer.ViewportChanged -= ViewportChanged;

        base.SetPlayer(oldPlayer);

        if (Player == null || isDesignMode)
            return;

        if (SelectedTheme != null)
            Player.Config.Video.BackgroundColor = SelectedTheme.SurfaceColor;

        // Updates the key binding actions with the new instances in case of swap or initial load
        bool SubsYUp = false, SubsYDown = false, SubsFontIncrease = false, SubsFontDecrease = false;
        void aSubsYUp()         { Thickness t = UIConfig.SubsMargin; t.Bottom += 2; UIConfig.SubsMargin = t; }
        void aSubsYDown()       { Thickness t = UIConfig.SubsMargin; t.Bottom -= 2; UIConfig.SubsMargin = t; }
        void aSubsFontIncrease() => UIConfig.SubsFontSize += 2;
        void aSubsFontDecrease() => UIConfig.SubsFontSize -= 2;

        // Additional Key Bindings for Subtitles
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

        if (!Config.Loaded)
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

        Player.renderer.ViewportChanged += ViewportChanged;
        ViewportChanged(null, null);
        //Raise(null);
        //settings?.Raise(null);

        // TBR
        //Unloaded += (o, e) => { Dispose(); };
    }

    private void ViewportChanged(object sender, EventArgs e) => UIConfig?.UpdateSubsMargin();

    public override void SetOverlay()
    {
        base.SetOverlay();

        if (isDesignMode || Overlay == null)
            return;

        if (Overlay.IsLoaded && !initialized)
        {
            initialized = true;
            Initialize();
        }
        else
        {
            Overlay.Loaded += (o, e) =>
            {
                if (!initialized)
                {
                    initialized = true;
                    Initialize();
                }
            };
        }
    }

    private void Initialize()
    {
        // TODO: Allow template without pop-ups

        // Allow UI Refresh for Activity Mode, Buffered Duration on Pause & Stats
        if (Engine.IsLoaded)
            Engine.Config.UIRefresh = true;
        else
            Engine.Loaded += (o, e) => Engine.Config.UIRefresh = true;

        Overlay.Resources.MergedDictionaries.Add(new ResourceDictionary() {
            Source = new Uri("pack://application:,,,/FlyleafLib.Controls.WPF;component/Resources/MaterialDesignColors.xaml") });

        DialogHost dialogSettings;
        if (Overlay.Content != null)
        {
            popUpMenu       = ((FrameworkElement)LogicalTreeHelper.FindLogicalNode((FrameworkElement)Overlay.Content, "PART_ContextMenuOwner"))?.ContextMenu;
            dialogSettings  = (DialogHost)LogicalTreeHelper.FindLogicalNode((FrameworkElement)Overlay.Content, "PART_DialogSettings");
        }
        else
        {
            popUpMenu       = ((FrameworkElement)Overlay.Template.FindName("PART_ContextMenuOwner", Overlay))?.ContextMenu;
            dialogSettings  = (DialogHost)Overlay.Template.FindName("PART_DialogSettings", Overlay);
        }

        if (dialogSettings != null)
        {
            dialogSettingsIdentifier = $"DialogSettings_{Guid.NewGuid()}";
            dialogSettings.Identifier = dialogSettingsIdentifier;
        }

        InitializePopUps();
        RegisterCommands();

        if (UIConfigPath != null)
        {
            try
            {
                if (System.IO.File.Exists(UIConfigPath))
                    UIConfig.Load(this, UIConfigPath);
            } catch { }
        }

        if (UIConfig == null || !UIConfig.Loaded)
        {
            UIConfig UIConfig = new(this)
            {
                SubsMargin          = new(0, 0, 0, 48),
                SubsFontFamily      = "Segoe UI",
                SubsFontWeight      = FontWeights.Bold.ToString(),
                SubsFontStyle       = FontStyles.Normal.ToString(),
                SubsFontStretch     = FontStretches.Normal.ToString(),
                SubsFontSize        = 48,
                SubsStrokeThickness = 3,
                SubsFontColor       = Colors.White,
                SubsWithinViewport  = true
            };

            var theme = Overlay.Resources.GetTheme();
            var defaultTheme = new UITheme(this, null) { Name = "Default", PrimaryColor = theme.PrimaryMid.Color, SecondaryColor = theme.SecondaryMid.Color, BackgroundColor = theme.Background, SurfaceColor = Config != null && Config.Video != null ? Config.Video.BackgroundColor : Colors.Black};
            UIConfig.Themes = 
            [
                new(this, defaultTheme) { Name = "Black & White", PrimaryColor = Colors.White, SecondaryColor = Colors.White, BackgroundColor = Colors.Black, SurfaceColor = Colors.Black },
                new(this, defaultTheme) { Name = "Blue & Red", PrimaryColor = Colors.DodgerBlue, SecondaryColor = (Color)ColorConverter.ConvertFromString("#e00000"), BackgroundColor = Colors.Black, SurfaceColor = Colors.Black },
                new(this, defaultTheme) { Name = "Orange", PrimaryColor = (Color)ColorConverter.ConvertFromString("#ff8300"), SecondaryColor = Colors.White, BackgroundColor = Colors.Black, SurfaceColor = Colors.Black },
                new(this, defaultTheme) { Name = "Firebrick", PrimaryColor = Colors.Firebrick, SecondaryColor = Colors.White, BackgroundColor = Colors.Black, SurfaceColor = Colors.Black },
                new(this, defaultTheme) { Name = "Fuchia,Lime & Blue", PrimaryColor = (Color)ColorConverter.ConvertFromString("#e615e6"), SecondaryColor = Colors.Lime, BackgroundColor = (Color)ColorConverter.ConvertFromString("#0f1034"), SurfaceColor = (Color)ColorConverter.ConvertFromString("#0f1034") },
                new(this, defaultTheme) { Name = "Gold & Chocolate", PrimaryColor = (Color)ColorConverter.ConvertFromString("#ffc73b"), SecondaryColor = Colors.Chocolate, BackgroundColor = (Color)ColorConverter.ConvertFromString("#3b1212"), SurfaceColor = (Color)ColorConverter.ConvertFromString("#3b1212") },
                new(this, defaultTheme) { Name = "Green & Brown", PrimaryColor = (Color)ColorConverter.ConvertFromString("#24b03b"), SecondaryColor = (Color)ColorConverter.ConvertFromString("#e66102"), BackgroundColor = Colors.Black, SurfaceColor = Colors.Black },
                new(this, defaultTheme) { Name = "Custom", PrimaryColor = Colors.Orange, SecondaryColor = Colors.White, SurfaceColor = Colors.Black }
            ];

            UIConfig.SelectedTheme = "Firebrick";

            this.UIConfig = UIConfig;
            if (!string.IsNullOrEmpty(UIConfigPath))
                UIConfig.Save(this, UIConfigPath, ConfigPath, EnginePath);
        }

        foreach (var uitheme in UIConfig.Themes)
            if (uitheme.Name == UIConfig.SelectedTheme)
                SelectedTheme = uitheme;

        subsInitialMargin   = UIConfig.SubsMargin;

        if (popUpMenu != null)
            Surface.MouseRightButtonUp += (o, e) => {
                popUpMenu.PlacementTarget = Overlay; popUpMenu.DataContext = this; popUpMenu.IsOpen = true; };

        Overlay.KeyUp += (o, e) =>
        {
            if (e.Key == Key.Escape && dialogSettingsIdentifier != null && DialogHost.IsDialogOpen(dialogSettingsIdentifier))
                DialogHost.Close(dialogSettingsIdentifier, "cancel");
        };
        Overlay.AddHandler(FlyleafBar.OpenSettingsEvent, new RoutedEventHandler(OpenSettingsFired));

        ThemeLoaded?.Invoke(this, new EventArgs());
    }
    private void InitializePopUps()
    {
        if (popUpMenu != null)
            popUpMenu.PlacementTarget = this;

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

            popUpCustomAspectRatio      = new() { IsCheckable = true };
            popUpCustomAspectRatioSet   = new() { Header = "Set Custom..." };
            popUpCustomAspectRatioSet.Click += (n1, n2) => { DialogAspectRatio(); };

            popUpAspectRatio.Items.Add(popUpCustomAspectRatio);
            popUpAspectRatio.Items.Add(popUpCustomAspectRatioSet);

            popUpMenu.Closed += (o, e) => { if (Player != null) Player.Activity.IsEnabled = true; };

            popUpMenu.Opened += (o, e) =>
            {
                if (Player != null) Player.Activity.IsEnabled = false;
                CanPaste = !string.IsNullOrEmpty(Clipboard.GetText());
                popUpCustomAspectRatio.Header = $"Custom ({Config.Video.CustomAspectRatio})";
                FixMenuSingleCheck(popUpAspectRatio, Config.Video.AspectRatio.ToString());
                if (Config.Video.AspectRatio == AspectRatio.Custom)
                    popUpCustomAspectRatio.IsChecked = true;
                else if (Config.Video.AspectRatio == AspectRatio.Keep)
                    popUpKeepAspectRatio.IsChecked = true;
            };
        }
    }

    #region ICommands
    void RegisterCommands()
    {
        OpenFileDialog      = new RelayCommand(OpenFileDialogAction);
        OpenSettings        = new RelayCommand(OpenSettingsAction);
        OpenColorPicker     = new RelayCommand(OpenColorPickerAction);
        ChangeAspectRatio   = new RelayCommand(ChangeAspectRatioAction);
        SetSubtitlesFont    = new RelayCommand(SetSubtitlesFontAction);
        ExitApplication     = new RelayCommand(ExitApplicationAction);
        SetSubsPositionY    = new RelayCommand(SetSubsPositionYAction);
        ResetSubsPositionY  = new RelayCommand(ResetSubsPositionYAction);
        ResetFilters        = new RelayCommandSimple(ResetFiltersAction);
    }

    public ICommand OpenFileDialog      { get; set; }
    public void OpenFileDialogAction(object obj = null) => Player.OpenFromFileDialog();

    public ICommand ExitApplication     { get ; set; }
    public void ExitApplicationAction(object obj = null) { Application.Current.Shutdown(); }

    public ICommand OpenSettings        { get; set; }
    public async void OpenSettingsAction(object obj = null)
    {
        if (Config == null || dialogSettingsIdentifier == null) // || Player == null ?
            return;

        if (DialogHost.IsDialogOpen(dialogSettingsIdentifier))
        {
            DialogHost.Close(dialogSettingsIdentifier, "cancel");
            return;
        }

        if (settings == null)
            settings = new(this);

        Player.Activity.IsEnabled = false;

        var prevKeys = KeyBindings;
        KeyBindings = AvailableWindows.None;

        Dictionary<VideoFilters, int> saveFilterValues = [];
        Dictionary<VideoFilters, int> saveD3FilterValues = [];
        foreach(var filter in Config.Video.Filters.Values)
            saveFilterValues.Add(filter.Filter, filter.Value);

        foreach(var filter in Config.Video.D3Filters.Values)
            saveD3FilterValues.Add(filter.Filter, filter.Value);

        var wasOnTop    = DetachedTopMost;
        DetachedTopMost = false;
        var prevConfig  = Config.Video.Clone();
        var result      = await DialogHost.Show(settings, dialogSettingsIdentifier);
        DetachedTopMost = wasOnTop;
        Player.Activity.IsEnabled
                        = true;
        KeyBindings     = prevKeys;

        if (result == null)
            return;

        if (result.ToString() == "cancel")
        {
            Config.Video.HDRtoSDRMethod = prevConfig.HDRtoSDRMethod;
            Config.Video.SDRDisplayNits = prevConfig.SDRDisplayNits;

            foreach(var filter in saveFilterValues)
                Config.Video.Filters[filter.Key].Value = filter.Value;

            foreach(var filter in saveD3FilterValues)
                Config.Video.D3Filters[filter.Key].Value = filter.Value;
        }
        else
        {
            settings.ApplySettings();
            if (result.ToString() == "save")
            {
                subsInitialMargin = UIConfig.SubsMargin;

                try { UIConfig.Save(this, UIConfigPath, ConfigPath, EnginePath); } catch (Exception e) { MessageBox.Show(e.Message, "Error"); }
            }
        }
    }
    private void OpenSettingsFired(object sender, RoutedEventArgs e) => OpenSettingsAction();

    public ICommand OpenColorPicker     { get; set; }
    public async void OpenColorPickerAction(object curColor)
    {
        selectedColor = curColor.ToString();

        if (selectedColor == "Primary")
            SelectedColor = ((UITheme)settings.cmbThemes.SelectedItem).PrimaryColor;
        else if (selectedColor == "Secondary")
            SelectedColor = ((UITheme)settings.cmbThemes.SelectedItem).SecondaryColor;
        else if (selectedColor == "Background")
            SelectedColor = ((UITheme)settings.cmbThemes.SelectedItem).BackgroundColor;
        else if (selectedColor == "Surface")
            SelectedColor = ((UITheme)settings.cmbThemes.SelectedItem).SurfaceColor;

        selectedColorPrev = SelectedColor;
        var result = await DialogHost.Show(settings.ColorPickerDialog.DialogContent, "ColorPickerDialog");
        if (result != null && result.ToString() == "cancel")
            SelectedColor = selectedColorPrev;
    }

    public ICommand ChangeAspectRatio   { get; set; }
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

    public ICommand ResetSubsPositionY  { get; set; }
    public void ResetSubsPositionYAction(object obj = null) => UIConfig.SubsMargin = subsInitialMargin;

    public ICommand ResetFilters        { get; set; }
    public void ResetFiltersAction()
    {
        var vp = Config.Video.VideoProcessor == VideoProcessors.Auto ? Player.renderer.VideoProcessor : Config.Video.VideoProcessor;

        if (vp == VideoProcessors.Flyleaf)
            foreach (var kv in Config.Video.Filters)
                kv.Value.Value = kv.Value.Default;
        else if (vp == VideoProcessors.D3D11)
            foreach (var kv in Config.Video.D3Filters)
                kv.Value.Value = kv.Value.Default;

            Config.Video.SDRDisplayNits = Engine.Video.RecommendedLuminance;
    }

    public ICommand SetSubsPositionY    { get; set; }
    public void SetSubsPositionYAction(object y) { Thickness t = UIConfig.SubsMargin; t.Bottom += int.Parse(y.ToString()); UIConfig.SubsMargin = t; }

    public ICommand SetSubtitlesFont    { get; set; }
    static FontWeightConverter  fontWeightConv  = new();
    static FontStyleConverter   fontStyleConv   = new();
    static FontStretchConverter fontStretchConv = new();
    public void SetSubtitlesFontAction(object obj = null)
    {
        ColorFontDialog dialog = new()
        {
            Font = new(new(UIConfig.SubsFontFamily), UIConfig.SubsFontSize, (FontStyle)fontStyleConv.ConvertFromString(UIConfig.SubsFontStyle), (FontStretch)fontStretchConv.ConvertFromString(UIConfig.SubsFontStretch), (FontWeight)fontWeightConv.ConvertFromString(UIConfig.SubsFontWeight), new SolidColorBrush(UIConfig.SubsFontColor))
        };

        if (dialog.ShowDialog() == true && dialog.Font != null)
        {
            UIConfig.SubsFontFamily = dialog.Font.Family.ToString();
            UIConfig.SubsFontSize   = dialog.Font.Size;
            UIConfig.SubsFontWeight = dialog.Font.Weight.ToString();
            UIConfig.SubsFontStretch= dialog.Font.Stretch.ToString();
            UIConfig.SubsFontStyle  = dialog.Font.Style.ToString();
            UIConfig.SubsFontColor  = dialog.Font.BrushColor.Color;
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
        stackVertical.Resources = Overlay.Resources;
        stackVertical.Resources.SetTheme(Overlay.Resources.GetTheme());
        var result = await DialogHost.Show(stackVertical, dialogSettingsIdentifier);
    }
    private void FixMenuSingleCheck(MenuItem mi, string checkedItem = null)
    {
        foreach (var item in mi.Items)
        {
            if (checkedItem != null && ((MenuItem)item).Header != null && ((MenuItem)item).Header.ToString() == checkedItem)
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
}
