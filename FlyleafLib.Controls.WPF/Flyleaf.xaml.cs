using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;

using Dragablz;
using WpfColorFontDialog;
using MaterialDesignThemes.Wpf;
using MaterialDesignColors;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.Controls.WPF
{
    public partial class Flyleaf : UserControl, INotifyPropertyChanged, IVideoView
    {
        #region Properties
        private bool            IsDesignMode=> (bool) DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;

        public Player           Player      { get => _Player; set { _Player = value; InitializePlayer(); } }
        Player _Player;

        public Demuxer          VideoDemuxer => Player?.decoder?.VideoDemuxer;
        public VideoDecoder     VideoDecoder => Player?.decoder?.VideoDecoder;
        public AudioDecoder     AudioDecoder => Player?.decoder?.AudioDecoder;

        public AudioPlayer      AudioPlayer => Player?.audioPlayer;
        public AudioMaster      AudioMaster => Master.AudioMaster;
        public FlyleafWindow    WindowFront => VideoView?.WindowFront;
        public WindowsFormsHost WinFormsHost=> VideoView?.WinFormsHost;
        public VideoView        VideoView   => Player?.VideoView;
        
        public Session          Session     => Player?.Session;
        public Config           Config      => Player?.Config;
        public Config.Audio     Audio       => Config?.audio;
        public Config.Subs      Subs        => Config?.subs;
        public Config.Video     Video       => Config?.video;
        public Config.Decoder   Decoder     => Config?.decoder;
        public Config.Demuxer   Demuxer     => Config?.demuxer;

        bool _ShowDebug;
        public bool ShowDebug
        {
            get => _ShowDebug;
            set => Set(ref _ShowDebug, value);
        }

        bool _IsRecording;
        public bool IsRecording
        {
            get => _IsRecording;
            set => ToggleRecord();
        }

        bool _IsFullscreen;
        public bool IsFullscreen
        {
            get => _IsFullscreen;
            private set => Set(ref _IsFullscreen, value);
        }
        #endregion

        #region Settings
        public bool EnableKeyBindings { get; set; } = true;
        public bool EnableMouseEvents { get; set; } = true;
        int _IdleTimeout = 6000;
        public int IdleTimeout
        {
            get => _IdleTimeout;
            set { _IdleTimeout = value; IdleTimeoutChanged(); }
        }
        #endregion

        #region Initialize
        public TextBlock Subtitles { get; set; }
        Thickness subsInitialMargin;

        ContextMenu popUpMenu, popUpMenuSubtitles, popUpMenuVideo;
        MenuItem    popUpAspectRatio;
        MenuItem    popUpKeepAspectRatio;
        MenuItem    popUpCustomAspectRatio;
        MenuItem    popUpCustomAspectRatioSet;
        string      dialogSettingsIdentifier;
        bool        playerInitialized;

        int snapshotCounter = 1;
        int recordCounter = 1;

        public Flyleaf()
        {
            // TODO: fix assemblies to load here
            var a1 = new Card();
            var a2 = new Hue("Dummy", Colors.Black, Colors.White);
            var a3 = DragablzColors.WindowBaseColor.ToString();

            InitializeComponent();
            if (IsDesignMode) return;

            DataContext = this;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (IsDesignMode) return;

            Initialize();
        }

        public void Dispose()
        {
            lock (this)
            {
                if (disposed) return;

                //System.Diagnostics.Debug.WriteLine("Flyleaf_WPF_Dispose");
                WindowFront?.Close();
                Player?.Dispose();

                _IdleTimeout = -1;
                Resources.MergedDictionaries.Clear();
                Resources.Clear();
                Template.Resources.MergedDictionaries.Clear();
                Content = null;
                DataContext = null;
                disposed = true;
                //System.Diagnostics.Debug.WriteLine("Flyleaf_WPF_Disposed");

                
            }
        }
        bool disposed;

        private void Initialize()
        {
            popUpMenu           = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner", this))?.ContextMenu;
            popUpMenuSubtitles  = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner_Subtitles", this))?.ContextMenu;
            popUpMenuVideo      = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner_Video", this))?.ContextMenu;
            Subtitles           = (TextBlock) Template.FindName("PART_Subtitles", this);

            var dialogSettings  = ((DialogHost)Template.FindName("PART_DialogSettings", this));
            if (dialogSettings != null)
            {
                dialogSettingsIdentifier = $"DialogSettings_{Guid.NewGuid()}";
                dialogSettings.Identifier = dialogSettingsIdentifier;
            }

            if (popUpMenu           != null) popUpMenu.PlacementTarget          = this;
            if (popUpMenuSubtitles  != null) popUpMenuSubtitles.PlacementTarget = this;
            if (popUpMenuVideo      != null) popUpMenuVideo.PlacementTarget     = this;

            if (popUpMenu != null)
            {
                var videoItem = from object item in popUpMenu.Items where item is MenuItem && ((MenuItem)item).Header.ToString() == "Video" select item;
                var aspectRatioItem = from object item in ((MenuItem)videoItem.ToArray()[0]).Items where ((MenuItem)item).Header.ToString() == "Aspect Ratio" select item;
                popUpAspectRatio = (MenuItem)aspectRatioItem.ToArray()[0];
                popUpMenu.MouseMove += (o, e) => { lastMouseActivity = DateTime.UtcNow.Ticks; };
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

                popUpMenu.Opened += (o, e) =>
                {
                    CanPaste = String.IsNullOrEmpty(Clipboard.GetText()) ? false : true;
                    popUpCustomAspectRatio.Header = $"Custom ({Video.CustomAspectRatio})";
                    //popUpKeepAspectRatio.Header = $"Keep ({Player.VideoInfo.AspectRatio})";

                    FixMenuSingleCheck(popUpAspectRatio, Video.AspectRatio.ToString());
                    if (Video.AspectRatio == AspectRatio.Custom)
                        popUpCustomAspectRatio.IsChecked = true;
                    else if (Video.AspectRatio == AspectRatio.Keep)
                        popUpKeepAspectRatio.IsChecked = true;
                };
            }

            RegisterCommands();
            if (Subtitles != null)
            {
                subsInitialMargin   = Subtitles.Margin;
                SubtitlesFontDesc   = $"{Subtitles.FontFamily} ({Subtitles.FontWeight}), {Subtitles.FontSize}";
                SubtitlesFontColor  = Subtitles.Foreground;
            }
            Raise(null);

            lastMouseActivity = lastKeyboardActivity = DateTime.UtcNow.Ticks;

            if (_IdleTimeout > 0 && (idleThread == null || !idleThread.IsAlive))
            {
                idleThread = new Thread(() => { IdleThread(); } );
                idleThread.IsBackground = true;
                idleThread.Start();
            }
            
        }
        private void InitializePlayer()
        {
            if (playerInitialized) return;
            playerInitialized = true;

            VideoView.Resources   = Resources;
            VideoView.FontFamily  = FontFamily;
            VideoView.FontSize    = FontSize;

            this.Unloaded += (o, e) => { if (!Master.PreventAutoDispose) Dispose(); };

            // Keys (WFH will work for backwindow's key events) | both WPF
            if (EnableKeyBindings)
            {
                WindowFront.KeyDown += Flyleaf_KeyDown;
                WinFormsHost.KeyDown+= Flyleaf_KeyDown;
                WindowFront.KeyUp   += Flyleaf_KeyUp;
                WinFormsHost.KeyUp  += Flyleaf_KeyUp;
            }

            // Mouse (For back window we can only use Player.Control to catch mouse events) | WinFroms + WPF :(
            if (EnableMouseEvents)
            {
                Player.Control.DoubleClick  += (o, e) => { ToggleFullscreenAction(); };
                Player.Control.MouseClick   += (o, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Right & popUpMenu != null) popUpMenu.IsOpen = true; };

                WindowFront.MouseMove       += (o, e) => { lastMouseActivity = DateTime.UtcNow.Ticks; };
                Player.Control.MouseMove    += (o, e) => { lastMouseActivity = DateTime.UtcNow.Ticks; };

                Player.Control.MouseWheel   += (o, e) => { Flyleaf_MouseWheel(e.Delta); };
                WindowFront.MouseWheel      += (o, e) => { Flyleaf_MouseWheel(e.Delta); };
            }

            // Drag & Drop
            Player.Control.AllowDrop    = true;
            Player.Control.DragEnter    += Flyleaf_DragEnter;
            Player.Control.DragDrop     += Flyleaf_DragDrop;

            Player.OpenCompleted        += Player_OpenCompleted;
        }
        #endregion

        #region ICommands
        void RegisterCommands()
        {
            TogglePlayPause     = new RelayCommand(TogglePlayPauseAction); //, p => Session.CanPlay);
            ToggleFullscreen    = new RelayCommand(ToggleFullscreenAction);
            ToggleMute          = new RelayCommand(ToggleMuteAction);
            OpenSettings        = new RelayCommand(OpenSettingsAction);
            ChangeAspectRatio   = new RelayCommand(ChangeAspectRatioAction);
            SetSubtitlesFont    = new RelayCommand(SetSubtitlesFontAction);

            OpenFromFileDialog  = new RelayCommand(OpenFromFileDialogAction);
            OpenFromPaste       = new RelayCommand(OpenFromPasteAction);
            ExitApplication     = new RelayCommand(ExitApplicationAction);
            SetSubsDelayMs      = new RelayCommand(SetSubsDelayMsAction);
            SetAudioDelayMs     = new RelayCommand(SetAudioDelayMsAction);
            SetSubsPositionY    = new RelayCommand(SetSubsPositionYAction);

            ResetSubsPositionY  = new RelayCommand(ResetSubsPositionYAction);
            ResetSubsDelayMs    = new RelayCommand(ResetSubsDelayMsAction);
            ResetAudioDelayMs   = new RelayCommand(ResetAudioDelayMsAction);
            OpenStream          = new RelayCommand(OpenStreamAction);

            ShowSubtitlesMenu   = new RelayCommand(ShowSubtitlesMenuAction);
            ShowVideoMenu       = new RelayCommand(ShowVideoMenuAction);
            TakeSnapshot        = new RelayCommand(TakeSnapshotAction);
            ZoomReset           = new RelayCommand(ZoomResetAction);
            Zoom                = new RelayCommand(ZoomAction);
        }

        public ICommand ZoomReset { get; set; }
        public void ZoomResetAction(object obj = null) { Player.renderer.Zoom = 0;  }

        public ICommand Zoom { get; set; }
        public void ZoomAction(object offset) { Player.renderer.Zoom += int.Parse(offset.ToString()); }

        public ICommand TakeSnapshot { get; set; }
        public void TakeSnapshotAction(object obj = null)
        {
            if (!Session.CanPlay) return;

            Player.renderer.TakeSnapshot(System.IO.Path.Combine(Environment.CurrentDirectory, $"Snapshot{snapshotCounter}.bmp"));
            snapshotCounter++;
        }

        public ICommand ShowSubtitlesMenu { get; set; }
        public void ShowSubtitlesMenuAction(object obj = null) { popUpMenuSubtitles.IsOpen = true; }

        public ICommand ShowVideoMenu { get; set; }
        public void ShowVideoMenuAction(object obj = null) { popUpMenuVideo.IsOpen = true; }

        public ICommand OpenStream { get; set; }
        public void OpenStreamAction(object stream) { Player.Open((StreamBase)stream); }

        public ICommand ResetSubsPositionY { get; set; }
        public void ResetSubsPositionYAction(object obj = null) { Subtitles.Margin = subsInitialMargin; }

        public ICommand ResetSubsDelayMs { get; set; }
        public void ResetSubsDelayMsAction(object obj = null) { Subs.DelayTicks = 0; }

        public ICommand ResetAudioDelayMs { get; set; }
        public void ResetAudioDelayMsAction(object obj = null) { Audio.DelayTicks = 0; }

        public ICommand SetSubsPositionY { get; set; }
        public void SetSubsPositionYAction(object y) { Thickness t = Subtitles.Margin; t.Bottom += int.Parse(y.ToString()); Subtitles.Margin = t; Raise("Subtitles"); }

        public ICommand SetAudioDelayMs { get; set; }
        public void SetAudioDelayMsAction(object delay) { Audio.DelayTicks += (int.Parse(delay.ToString())) * (long)10000; }

        public ICommand SetSubsDelayMs { get; set; }
        public void SetSubsDelayMsAction(object delay) { Subs.DelayTicks += (int.Parse(delay.ToString())) * (long)10000; }

        public ICommand OpenFromFileDialog  { get; set; }
        public void OpenFromFileDialogAction(object obj = null)
        {
            System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
            if(openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) Open(openFileDialog.FileName);
        }

        public ICommand OpenFromPaste  { get; set; }
        public void OpenFromPasteAction(object obj = null) { Open(Clipboard.GetText()); }

        public ICommand ExitApplication { get ; set; }
        public void ExitApplicationAction(object obj = null) { Application.Current.Shutdown(); }


        bool _CanPaste;
        public bool         CanPaste        {
            get => _CanPaste;
            set => Set(ref _CanPaste, value);
        }
        public ICommand ChangeAspectRatio { get; set; }
        public void ChangeAspectRatioAction(object obj = null)
        {
            MenuItem mi = ((MenuItem)obj);

            if (Regex.IsMatch(mi.Header.ToString(), "Custom"))
            {
                if (Regex.IsMatch(mi.Header.ToString(), "Set")) return;
                Video.AspectRatio = AspectRatio.Custom;
                return;
            }
            else if (Regex.IsMatch(mi.Header.ToString(), "Keep"))
                Video.AspectRatio = AspectRatio.Keep;
            else
                Video.AspectRatio = mi.Header.ToString();
        }

        string _SubtitlesFontDesc;
        public string         SubtitlesFontDesc        {
            get => _SubtitlesFontDesc;
            set => Set(ref _SubtitlesFontDesc, value);
        }

        Brush _SubtitlesFontColor;
        public Brush         SubtitlesFontColor        {
            get => _SubtitlesFontColor;
            set => Set(ref _SubtitlesFontColor, value);
        }

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

        public ICommand OpenSettings        { get; set; }
        public async void OpenSettingsAction(object obj = null)
        {
            if (dialogSettingsIdentifier == null) return;

            if (DialogHost.IsDialogOpen(dialogSettingsIdentifier))
            {
                DialogHost.Close(dialogSettingsIdentifier, "cancel");
                return;
            }

            var view = new Settings();//Session);
            view.DataContext = this;
            var result = await DialogHost.Show(view, dialogSettingsIdentifier, view.Closing);
            view.Closed(result);
        }

        public ICommand ToggleMute          { get; set; }
        public void ToggleMuteAction(object obj = null)
        {
            AudioPlayer.Mute = !AudioPlayer.Mute;
        }

        public ICommand TogglePlayPause     { get; set; }
        public void TogglePlayPauseAction(object obj = null)
        {
            if (Player.IsPlaying)
                Player.Pause();
            else
                Player.Play();
        }
        
        public ICommand ToggleFullscreen    { get; set; }
        public void ToggleFullscreenAction(object obj = null)
        {
            if (VideoView.IsFullScreen)
                VideoView.NormalScreen();
            else
                VideoView.FullScreen();

            IsFullscreen = VideoView.IsFullScreen;
        }
        #endregion

        #region TODO
        public void Open(string url)
        {
            if (String.IsNullOrEmpty(url)) return;

            Player.Open(url);
        }
        public void Player_OpenCompleted(object sender, Player.OpenCompletedArgs e)
        {
            Raise(null);

            switch (e.type)
            {
                case MediaType.Video:
                    if (!e.success) return;

                    Player.Play();
                    break;
            }
        }
        private async void DialogAspectRatio()
        {
            if (dialogSettingsIdentifier == null) return;

            var stackVertical    = new StackPanel() { Height=100, Orientation = Orientation.Vertical };
            var stackHorizontal1 = new StackPanel() { Margin = new Thickness(10), Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center};
            var stackHorizontal2 = new StackPanel() { Margin = new Thickness(10), Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };

            var textBox = new TextBox() { VerticalAlignment = VerticalAlignment.Center, Width = 70, Margin = new Thickness(10, 0, 0, 0), Text=Video.CustomAspectRatio.ToString()};
            textBox.PreviewTextInput += (n1, n2) => { n2.Handled = !Regex.IsMatch(n2.Text, @"^[0-9\.\,\/\:]+$"); };

            var buttonOK = new Button() { Content = "OK" };
            var buttonCancel = new Button() { Margin = new Thickness(10, 0, 0, 0), Content = "Cancel" };

            buttonOK.Click +=       (n1, n2) => { if (textBox.Text != AspectRatio.Invalid) Video.CustomAspectRatio = textBox.Text; DialogHost.Close(dialogSettingsIdentifier); };
            buttonCancel.Click +=   (n1, n2) => { DialogHost.Close(dialogSettingsIdentifier); };

            stackHorizontal1.Children.Add(new TextBlock() { VerticalAlignment = VerticalAlignment.Center, Text="Set Custom Ratio: "});
            stackHorizontal1.Children.Add(textBox);
            stackHorizontal2.Children.Add(buttonOK);
            stackHorizontal2.Children.Add(buttonCancel);

            stackVertical.Children.Add(stackHorizontal1);
            stackVertical.Children.Add(stackHorizontal2);

            stackVertical.Orientation = Orientation.Vertical;

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

        private void ToggleRecord()
        {
            if (!Session.CanPlay)
            {
                Set(ref _IsRecording, false);
                return;
            }

            if (Player.decoder.VideoDemuxer.IsRecording)
            {
                Player.decoder.VideoDemuxer.StopRecording();
                Set(ref _IsRecording, false);
            }
            else
            {
                Player.decoder.VideoDemuxer.StartRecording($"Record{recordCounter}.mp4");
                Set(ref _IsRecording, true);
                recordCounter++;
            }
        }
        #endregion

        #region Activity Mode
        Thread idleThread;
        long lastKeyboardActivity, lastMouseActivity;
        public enum ActivityMode
        {
            Idle,
            Active,
            FullActive
        }

        public void IdleTimeoutChanged()
        {
            if (_IdleTimeout > 0 && (idleThread == null || !idleThread.IsAlive))
            {
                idleThread = new Thread(() => { IdleThread(); } );
                idleThread.IsBackground = true;
                idleThread.Start();
            }
                
            CurrentMode = ActivityMode.FullActive;
        }

        ActivityMode _CurrentMode = ActivityMode.FullActive;
        public ActivityMode         CurrentMode        {
            get => _CurrentMode;
            set => Set(ref _CurrentMode, value);
        }

        private ActivityMode GetCurrentActivityMode()
        {
            if ((DateTime.UtcNow.Ticks - lastMouseActivity   ) / 10000 < _IdleTimeout) return ActivityMode.FullActive;
            if ((DateTime.UtcNow.Ticks - lastKeyboardActivity) / 10000 < _IdleTimeout) return ActivityMode.Active;

            return ActivityMode.Idle;
        }

        bool _ShowGPUUsage;
        public bool ShowGPUUsage
        {   get => _ShowGPUUsage;
            set { Set(ref _ShowGPUUsage, value); if (value) StartGPUUsage(); else GPUUsage = ""; }
        }
        
        string _GPUUsage;
        public string GPUUsage
        {   get => _GPUUsage;
            set => Set(ref _GPUUsage, value);
        }
        Thread gpuThread;
        private void StartGPUUsage()
        {
            if (gpuThread != null && gpuThread.IsAlive) { ShowGPUUsage = false; return; }
            gpuThread = new Thread(() =>
            {
                while (_ShowGPUUsage) GPUUsage = Utils.GetGPUUsage().ToString("0.00") + "%";
                ShowGPUUsage = false;
            });
            gpuThread.IsBackground = true; gpuThread.Start();
        }

        private void IdleThread()
        {
            while (_IdleTimeout > 0)
            {
                Thread.Sleep(500);

                var newMode = GetCurrentActivityMode();
                if (newMode != CurrentMode)
                {
                    if (newMode == ActivityMode.Idle && IsFullscreen)
                        Dispatcher.Invoke(() => { while (ShowCursor(false) >= 0) { } isCursorHidden = true;});

                    if (isCursorHidden && newMode == ActivityMode.FullActive)
                        Dispatcher.Invoke(() => { while (ShowCursor(true)   < 0) { } });

                    CurrentMode = newMode;
                }
            }
        }

        bool isCursorHidden;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);
        #endregion

        #region Events
        private void Flyleaf_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4) { return; }

            Thickness t;

            if (dialogSettingsIdentifier != null && DialogHost.IsDialogOpen(dialogSettingsIdentifier)) return;

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                switch (e.Key)
                {
                    case Key.OemOpenBrackets:
                        Audio.DelayTicks -= 1000 * 10000;
                        break;

                    case Key.OemCloseBrackets:
                        Audio.DelayTicks += 1000 * 10000;
                        break;

                    case Key.OemSemicolon:
                        Subs.DelayTicks -= 1000 * 10000;
                        break;

                    case Key.OemQuotes:
                        Subs.DelayTicks += 1000 * 10000;
                        break;

                    case Key.Up:
                        t = Subtitles.Margin; t.Bottom += 2; Subtitles.Margin = t; Raise("Subtitles");
                        break;

                    case Key.Down:
                        t = Subtitles.Margin; t.Bottom -= 2; Subtitles.Margin = t; Raise("Subtitles");
                        break;

                    case Key.C:
                        if (Session == null | Session.InitialUrl == null) break;

                        Clipboard.SetText(Session.InitialUrl);
                        break;

                    case Key.R:
                        ToggleRecord();
                        
                        break;

                    case Key.S:
                        TakeSnapshotAction();
                        break;

                    case Key.V:
                        Open(Clipboard.GetText());
                        break;

                    case Key.X:
                        Player.decoder.Flush();
                        break;
                }

                e.Handled = true;
                return;
            }
            
            switch (e.Key)
            {
                case Key.Up:
                    if (AudioPlayer.Volume == 150) return;
                    AudioPlayer.Volume += AudioPlayer.Volume + 5 > 150 ? 0 : 5;

                    break;

                case Key.Down:
                    if (AudioPlayer.Volume == 0) return;
                    AudioPlayer.Volume -= AudioPlayer.Volume - 5 < 0 ? 0 : 5;
                    
                    break;

                case Key.Right:
                    int ms = (int) ((Session.CurTime + (5 * 1000 * (long)10000)) / 10000);
                    Player.Seek(ms, true);
                    break;

                case Key.Left:
                    ms = (int) ((Session.CurTime - (5 * 1000 * (long)10000)) / 10000);
                    Player.Seek(ms);
                    break;


                case Key.OemOpenBrackets:
                    Audio.DelayTicks -= 100 * 10000;
                    break;

                case Key.OemCloseBrackets:
                    Audio.DelayTicks += 100 * 10000;
                    break;

                case Key.OemSemicolon:
                    Subs.DelayTicks -= 100 * 10000;
                    break;

                case Key.OemQuotes:
                    Subs.DelayTicks += 100 * 10000;
                    break;
            }

            e.Handled = true;
        }
        private void Flyleaf_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.System && e.SystemKey == Key.F4) { return; }

            lastKeyboardActivity = DateTime.UtcNow.Ticks;

            if (dialogSettingsIdentifier != null && DialogHost.IsDialogOpen(dialogSettingsIdentifier) && e.Key == Key.Escape) { DialogHost.Close(dialogSettingsIdentifier, "cancel"); return; }

            switch (e.Key)
            {
                case Key.I:
                    lastKeyboardActivity = 0; lastMouseActivity = 0;
                    return;

                case Key.F:
                    ToggleFullscreenAction();
                    break;

                case Key.Space:
                    TogglePlayPauseAction(null);
                    break;

                case Key.Escape:
                    if (IsFullscreen) ToggleFullscreenAction();
                    break;
            }

            e.Handled = true;
        }
        private void Flyleaf_DragEnter(object sender, System.Windows.Forms.DragEventArgs e) { e.Effect = System.Windows.Forms.DragDropEffects.All; }
        private void Flyleaf_DragDrop(object sender, System.Windows.Forms.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string filename = ((string[])e.Data.GetData(DataFormats.FileDrop, false))[0];
                Open(filename);
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                string text = e.Data.GetData(DataFormats.Text, false).ToString();
                if (text.Length > 0) Open(text);
            }
        }
        private void Flyleaf_MouseWheel(int delta)
        {
            if (delta == 0) return;

            Player.renderer.Zoom += delta > 0 ? 50 : -50;
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
}