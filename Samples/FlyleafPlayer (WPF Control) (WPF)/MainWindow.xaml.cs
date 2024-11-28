using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer
{
    // TODO: Popup Menu Playlist will not resize the size?
    //       Add Play Next/Prev for Playlists (Page Up/Down?) this goes down to Player

    /// <summary>
    /// <para>FlyleafPlayer Sample</para>
    /// <para>A stand-alone Overlay which uses a customization of FlyleafME control</para>
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new(propertyName));
        public static string FlyleafLibVer => "FlyleafLib v" + System.Reflection.Assembly.GetAssembly(typeof(Engine)).GetName().Version;
        
        /// <summary>
        /// Flyleaf Player binded to FlyleafME (This can be swapped and will nto belong to this window)
        /// </summary>
        public Player       Player      { get; set; }

        /// <summary>
        /// FlyleafME Media Element Control
        /// </summary>
        public FlyleafME    FlyleafME   { get; set; }

        public ICommand     OpenWindow  { get; set; }
        public ICommand     CloseWindow { get; set; }
        
        static bool runOnce;
        Config playerConfig;
        bool ReversePlaybackChecked;

        public MainWindow()
        {
            OpenWindow      = new RelayCommandSimple(() => new MainWindow() { Width = Width, Height = Height }.Show());
            CloseWindow     = new RelayCommandSimple(Close);
            ShowPrevImage   = new RelayCommandSimple(() => SlideShowGoTo(ImageIndex - 1));
            ShowNextImage   = new RelayCommandSimple(() => SlideShowGoTo(ImageIndex + 1));
            SlideShowToggle = new RelayCommandSimple(SlideShowToggleAction);
            SlideShowRestart= new RelayCommandSimple(() => { SlideShowStart(false, false); SlideShowGoTo(0); }); // Note: When we start SlideShow with non-current ImageIndex use Start(notask) and GoTo to start the task

            FlyleafME = new FlyleafME(this)
            {
                Tag = this,
                ActivityTimeout     = MSG_TIMEOUT,
                KeyBindings         = AvailableWindows.Both,
                DetachedResize      = AvailableWindows.Overlay,
                DetachedDragMove    = AvailableWindows.Both,
                ToggleFullScreenOnDoubleClick 
                                    = AvailableWindows.Both,
                KeepRatioOnResize   = true,
                OpenOnDrop          = AvailableWindows.Both,

                PreferredLandscapeWidth = 800,
                PreferredPortraitHeight = 600
            };

            // Allow Flyleaf WPF Control to Load UIConfig and Save both Config & UIConfig (Save button will be available in settings)
            FlyleafME.ConfigPath    = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.Config.json");
            FlyleafME.EnginePath    = App.EnginePath;
            FlyleafME.UIConfigPath  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.UIConfig.json");
            
            InitializeComponent();

            // Allowing FlyleafHost to access our Player
            DataContext = FlyleafME;
        }

        private Config DefaultConfig()
        {
            Config config = new Config();
            config.Demuxer.FormatOptToUnderlying= true;     // Mainly for HLS to pass the original query which might includes session keys
            config.Audio.FiltersEnabled         = true;     // To allow embedded atempo filter for speed
            config.Video.GPUAdapter             = "";       // Set it empty so it will include it when we save it
            config.Subtitles.SearchLocal        = true;
            return config;
        }

        private void LoadPlayer()
        {
            // NOTE: Loads/Saves configs only in RELEASE mode

            // Player's Config (Cannot be initialized before Engine's initialization)
            #if RELEASE
            // Load Player's Config
            if (File.Exists(FlyleafME.ConfigPath))
                try { playerConfig = Config.Load(FlyleafME.ConfigPath); } catch { playerConfig = DefaultConfig(); }
            else
                playerConfig = DefaultConfig();
            #else
                playerConfig = DefaultConfig();
            #endif

            #if DEBUG
            // Testing audio filters
            //playerConfig.Audio.Filters = new()
            //{
              ////new() { Name = "loudnorm", Args = "I=-24:LRA=7:TP=-2", Id = "loudnorm1" },
              ////new() { Name = "dynaudnorm", Args = "f=4150", Id = "dynaudnorm1" },
              ////new() { Name ="afftfilt", Args = "real='hypot(re,im)*sin(0)':imag='hypot(re,im)*cos(0)':win_size=512:overlap=0.75" }, // robot
              ////new() { Name ="tremolo", Args="f=5:d=0.5" },
              ////new() { Name ="vibrato", Args="f=10:d=0.5" },
              ////new() { Name ="rubberband", Args="pitch=1.5" }
            //};
            #endif
            
            // Initializes the Player
            Player = new Player(playerConfig);

            // Dispose Player on Window Close (the possible swapped player from FlyleafMe that actually belongs to us)
            Closing += (o, e) => FlyleafME.Player?.Dispose();

            Player.Opening      += Player_Opening;
            Player.OpenCompleted+= Player_OpenCompleted;

            // If the user requests reverse playback allocate more frames once
            Player.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == "ReversePlayback" && !GetWindowFromPlayer(Player).ReversePlaybackChecked)
                {
                    if (playerConfig.Decoder.MaxVideoFrames < 80)
                        playerConfig.Decoder.MaxVideoFrames = 80;

                    GetWindowFromPlayer(Player).ReversePlaybackChecked = true;
                }
                else if (e.PropertyName == nameof(Player.Rotation))
                    GetWindowFromPlayer(Player).Msg = $"Rotation {Player.Rotation}°";
                else if (e.PropertyName == nameof(Player.Speed))
                    GetWindowFromPlayer(Player).Msg = $"Speed x{Player.Speed}";
                else if (e.PropertyName == nameof(Player.Zoom))
                    GetWindowFromPlayer(Player).Msg = $"Zoom {Player.Zoom}%";
                else if (e.PropertyName == nameof(Player.Status) && Player.Activity.Mode == ActivityMode.Idle)
                    Player.Activity.ForceActive();
            };

            Player.Audio.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Audio.Volume))
                    GetWindowFromPlayer(Player).Msg = $"Volume {Player.Audio.Volume}%";
                else if (e.PropertyName == nameof(Player.Audio.Mute))
                    GetWindowFromPlayer(Player).Msg = Player.Audio.Mute ? "Muted" : "Unmuted";
            };

            Player.Config.Audio.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Config.Audio.Delay))
                    GetWindowFromPlayer(Player).Msg = $"Audio Delay {Player.Config.Audio.Delay / 10000}ms";
            };

            Player.Config.Subtitles.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(Player.Config.Subtitles.Delay))
                    GetWindowFromPlayer(Player).Msg = $"Subs Delay {Player.Config.Subtitles.Delay / 10000}ms";
            };

            // Ctrl+ N / Ctrl + W (Open New/Close Window)
            var keys = playerConfig.Player.KeyBindings;
            keys.AddCustom(Key.N, true, () => CreateNewWindow(Player), "New Window", false, true, false);
            keys.AddCustom(Key.W, true, () => GetWindowFromPlayer(Player).Close(), "Close Window", false, true, false);
            
            // We might saved the tmp keys (restore them)
            if (Player.Config.Loaded && keys.Exists("tmp01"))
            {
                SlideKeysRemove();
                VideoKeysRestore();
            }
        }

        private static MainWindow GetWindowFromPlayer(Player player)
        {
            FlyleafHost flhost = null;
            MainWindow mw = null;

            Utils.UIInvokeIfRequired(() =>
            {
                flhost  = (FlyleafHost) player.Host;
                mw      = (MainWindow) flhost.Overlay;
            });

            return mw;
        }
        private static void CreateNewWindow(Player player) 
        {
            var mw = GetWindowFromPlayer(player);

            MainWindow mwNew = new()
            {
                Width   = mw.Width,
                Height  = mw.Height,
            };

            mwNew.Show();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Engine.IsLoaded)
            {
                LoadPlayer();
                FlyleafME.Player = Player;
            }
            else
            {
                Engine.Loaded += (o, e) =>
                {
                    LoadPlayer();
                    Utils.UIInvokeIfRequired(() => FlyleafME.Player = Player);
                };
            }

            if (runOnce)
                return;

            runOnce = true;

            if (App.CmdUrl != null)
                Player.OpenAsync(App.CmdUrl);

            #if RELEASE
            // Save Player's Config (First Run)
            // Ensures that the Control's handle has been created and the renderer has been fully initialized (so we can save also the filters parsed by the library)
            if (!playerConfig.Loaded)
            {
                try
                {
                    Utils.AddFirewallRule();
                    playerConfig.Save(FlyleafME.ConfigPath);
                } catch { }
            }

            // Stops Logging (First Run)
            if (!Engine.Config.Loaded)
            {
                Engine.Config.LogOutput      = null;
                Engine.Config.LogLevel       = LogLevel.Quiet;
                //Engine.Config.FFmpegDevices  = false;

                try { Engine.Config.Save(App.EnginePath); } catch { }
            }
            #endif
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => FlyleafME.IsMinimized = true;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        #region Photo Viewer / Slide Show
        /* TODO
         * Sorting (+based on explorer folder settings / numerical*)
         * Shuffle
         * Include / Exclude  (SubFolder/Image/Animation/Video *Regex)
         * Prepare next? (for faster/better transition)
         * Preview Film/Slider/Gallery?
         * Config / Keys** Save?
         */

        #region Slide Show Config
        public int          SlideShowTimer      { get => slideShowTimer;        set { if (slideShowTimer == value) return;      slideShowTimer = value;     OnPropertyChanged(nameof(SlideShowTimer)); } }
        int slideShowTimer = 3000;
        public int          PageStep            { get => pageStep;              set { if (pageStep == value) return;            pageStep = value;           OnPropertyChanged(nameof(PageStep)); } }
        int pageStep = 10;
        public int          MaxFiles            { get => maxFiles;              set { if (maxFiles == value) return;            maxFiles = value;           OnPropertyChanged(nameof(MaxFiles)); } }
        int maxFiles = 5000;
        public bool         DeleteConfirmation  { get => deleteConfirmation;    set { if (deleteConfirmation == value) return;  deleteConfirmation = value; OnPropertyChanged(nameof(DeleteConfirmation)); } }
        bool deleteConfirmation = true;
        #endregion

        public ICommand     ShowNextImage   { get; set; }
        public ICommand     ShowPrevImage   { get; set; }
        public ICommand     SlideShowToggle { get; set; }
        public ICommand     SlideShowRestart{ get; set; }

        public bool         CanNextImage    { get => canNextImage;  set { if (canNextImage == value) return;    canNextImage = value;   Utils.UI(() => OnPropertyChanged(nameof(CanNextImage))); } }
        bool canNextImage;

        public bool         CanPrevImage    { get => canPrevImage;  set { if (canPrevImage == value) return;    canPrevImage = value;   Utils.UI(() => OnPropertyChanged(nameof(CanPrevImage))); } }
        bool canPrevImage;

        public MediaViewer  MediaViewer     { get => mediaViewer;   set { if (mediaViewer == value) return;     prevMediaViewer = mediaViewer; mediaViewer = value; SwitchMediaViewer(); Utils.UI(() => OnPropertyChanged(nameof(MediaViewer))); } }
        MediaViewer mediaViewer = MediaViewer.Video; MediaViewer prevMediaViewer;

        public string       ImageTitle      { get => imageTitle;    set { if (imageTitle == value) return;      imageTitle = value;     Utils.UI(() => OnPropertyChanged(nameof(ImageTitle))); } }
        string imageTitle;

        public FileInfo     ImageInfo       { get => imageInfo;     set { if (imageInfo == value) return;       imageInfo = value; ImageTitle = value?.Name; } }
        FileInfo imageInfo;

        public List<string> ImageFiles      { get; set; } = [];
        public int          ImageIndex      { get => imageIndex;    set { if (imageIndex == value) return;      imageIndex = value; UIImageIndex = imageIndex + 1; Utils.UI(() => OnPropertyChanged(nameof(UIImageIndex))); } }
        int imageIndex = -1;
        public int          UIImageIndex    { get; set; }

        public string       ImageFolder     { get => imageFolder;   set { prevImageFolder = imageFolder;        imageFolder = value; } }
        string imageFolder, prevImageFolder;

        public bool         SlideShow       { get => slideShow;     set { if (slideShow == value) return;       slideShow = value;      Utils.UI(() => OnPropertyChanged(nameof(SlideShow))); } }
        bool slideShow;

        public int          TotalFiles      { get => totalFiles;    set { if (totalFiles == value) return;      totalFiles = value;     Utils.UI(() => OnPropertyChanged(nameof(TotalFiles))); } }
        int totalFiles;

        CancellationTokenSource
                            slideShowCancel = new();
        object              slideShowLock   = new();
        int                 slideShowElapsedMs;         // For Pause/Play to keep the remaining ms
        long                slideShowStartedAt;         // For Pause/Play to keep the remaining ms
        FileSystemWatcher   slideShowWatcher;           // Monitor ImageFolder for changes
        bool                imageFilesChanged;
        bool                userKeepRatioOnResize;

        [GeneratedRegex("\\.(apng|avif|bmp|jpg|jpeg|gif|ico|png|svg|tiff|webp|jfif)$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex RegexImages();

        void Player_Opening(object sender, OpeningArgs e)
        {
            if (e.IsSubtitles || e.Url == null)
                return;

            var url = e.Url;

            if (!RegexImages().IsMatch(url))
                MediaViewer = MediaViewer.Video;
            else
            {
                try
                {
                    ImageInfo = new(url);
                    ImageFolder = ImageInfo.Exists ? Path.GetDirectoryName(ImageInfo.FullName) : null;
                    SlideShowCheck();
                }
                catch (Exception)
                {
                    if (ImageInfo == null)
                        ImageTitle = url;

                    ImageFolder = null;
                    MediaViewer = MediaViewer.Image;
                }
            }
        }
        void Player_OpenCompleted(object sender, OpenCompletedArgs e)
        {
            if (e.IsSubtitles)
                return;

            lock (slideShowLock)
                if (SlideShow)
                {
                    slideShowCancel.Cancel();
                    slideShowCancel = new();
                    slideShowStartedAt = DateTime.UtcNow.Ticks;
                    slideShowElapsedMs = 0;
                    Task.Run(SlideShowTask, slideShowCancel.Token);
                }
        }

        void SwitchMediaViewer()
        {
            var keys = playerConfig.Player.KeyBindings;

            switch (prevMediaViewer)
            {
                case MediaViewer.Video:
                    userKeepRatioOnResize = FlyleafME.KeepRatioOnResize;
                    break;

                case MediaViewer.Image:
                    break;

                case MediaViewer.Slide:
                    SlideShowDeInit();
                    SlideKeysRemove();

                    break;
            }

            switch (MediaViewer)
            {
                case MediaViewer.Video:
                    ImageInfo = null;
                    ImageFolder = null;

                    FlyleafME.ToggleFullScreenOnDoubleClick = AvailableWindows.Both;
                    FlyleafME.KeyBindings = AvailableWindows.Both;
                    FlyleafME.KeepRatioOnResize = userKeepRatioOnResize;

                    VideoKeysRestore();

                    break;

                case MediaViewer.Image:
                    FlyleafME.ToggleFullScreenOnDoubleClick = AvailableWindows.Surface;
                    FlyleafME.KeyBindings = AvailableWindows.Surface;
                    FlyleafME.KeepRatioOnResize = userKeepRatioOnResize;

                    break;

                case MediaViewer.Slide:
                    FlyleafME.ToggleFullScreenOnDoubleClick = AvailableWindows.Surface;
                    FlyleafME.KeyBindings = AvailableWindows.Both;
                    FlyleafME.KeepRatioOnResize = false;

                    SlideKeysAdd();
                    break;
            }
        }

        private void DialogHost_DialogOpened(object sender, MaterialDesignThemes.Wpf.DialogOpenedEventArgs eventArgs)
            { if (MediaViewer == MediaViewer.Slide) FlyleafME.KeyBindings = AvailableWindows.Surface; }
        private void DialogHost_DialogClosed(object sender, MaterialDesignThemes.Wpf.DialogClosedEventArgs eventArgs)
            { if (MediaViewer == MediaViewer.Slide) FlyleafME.KeyBindings = AvailableWindows.Both; }

        void SlideShowCheck()
        {
            if (prevImageFolder == imageFolder)
            {
                if (imageFolder == null)
                    MediaViewer = MediaViewer.Image;
                else if (MediaViewer != MediaViewer.Slide)
                    return;
                else if (ImageFiles[ImageIndex] != ImageInfo.FullName)
                {
                    int foundIndex = -1;

                    for (int i = 0; i < ImageFiles.Count; i++)
                        if (ImageFiles[i] == ImageInfo.FullName)
                            { foundIndex = i; break; }

                    if (foundIndex == -1)
                        MediaViewer = MediaViewer.Image; // Maybe recalculate files?
                    else
                    {
                        if (SlideShow)
                        {
                            slideShowCancel.Cancel();
                            slideShowCancel = new();
                        }

                        ImageIndex = foundIndex;
                        CanPrevImage = ImageIndex > 0;
                        CanNextImage = ImageIndex < ImageFiles.Count - 1;

                        if (SlideShow && !CanNextImage)
                            SlideShow = false;
                    }
                }
            }
            else if (imageFolder == null)
                MediaViewer = MediaViewer.Image;
            else
            {
                var files = Directory.GetFiles(imageFolder, $"*.*");
                if (files.Length < 2)
                    MediaViewer = MediaViewer.Image;
                else
                {
                    List<string> imagefiles = [];
                    int imageIndex = -1;
                    for (int i = 0; i < files.Length; i++)
                    {
                        var file = files[i];
                        if (!RegexImages().IsMatch(file))
                            continue;

                        if (file.Equals(imageInfo.FullName, StringComparison.OrdinalIgnoreCase))
                            imageIndex = imagefiles.Count;

                        imagefiles.Add(file);
                        if (imagefiles.Count > maxFiles)
                            break;
                    }

                    if (imagefiles.Count < 2)
                        MediaViewer = MediaViewer.Image;
                    else
                    {
                        lock (slideShowLock)
                        {
                            slideShowWatcher = new(imageFolder);
                            slideShowWatcher.EnableRaisingEvents = true;
                            slideShowWatcher.NotifyFilter = NotifyFilters.FileName;
                            
                            slideShowWatcher.Renamed += (o, e) =>
                            {
                                if (!imageFilesChanged && RegexImages().IsMatch(e.Name))
                                    imageFilesChanged = true;
                            };

                            slideShowWatcher.Deleted += (o, e) =>
                            {
                                if (!imageFilesChanged && RegexImages().IsMatch(e.Name))
                                    imageFilesChanged = true;
                            };

                            slideShowWatcher.Created += (o, e) =>
                            {
                                if (!imageFilesChanged && RegexImages().IsMatch(e.Name))
                                    imageFilesChanged = true;
                            };

                            slideShowCancel.Cancel();
                            slideShowCancel = new();

                            ImageFiles  = imagefiles;
                            TotalFiles  = imagefiles.Count;
                            ImageIndex  = imageIndex == -1 ? 0 : imageIndex;
                            CanPrevImage= ImageIndex > 0;
                            CanNextImage= ImageIndex < ImageFiles.Count - 1;
                            MediaViewer = MediaViewer.Slide;
                        }
                    }
                }
            }
        }
        void SlideShowReCheck(ref int index)
        {
            if (!imageFilesChanged)
                return;

            imageFilesChanged = false;

            var files = Directory.GetFiles(imageFolder, $"*.*");
            if (files.Length < 2)
                MediaViewer = MediaViewer.Image;
            else
            {
                List<string> imagefiles = [];
                int imageIndex = -1;
                for (int i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    if (!RegexImages().IsMatch(file))
                        continue;

                    if (file.Equals(imageInfo.FullName, StringComparison.OrdinalIgnoreCase))
                        imageIndex = imagefiles.Count;

                    imagefiles.Add(file);
                    if (imagefiles.Count > maxFiles)
                        break;
                }

                if (imagefiles.Count < 2)
                    MediaViewer = MediaViewer.Image;
                else
                {
                    if (index == 0)
                        index = 0;
                    else if (index == ImageFiles.Count - 1)
                        index = imagefiles.Count - 1;
                    else if (imageIndex != -1)
                    {
                        if (index - ImageIndex == 1)
                            index = imageIndex + 1;
                        else if (ImageIndex - index == 1)
                            index = imageIndex - 1;
                    }
                    else if (index > imagefiles.Count - 1)
                        index = imagefiles.Count - 1;

                    ImageFiles = imagefiles;
                    TotalFiles = imagefiles.Count;
                }
            }
        }
        void SlideShowDeInit()
        {
            lock (slideShowLock)
            {
                slideShowCancel.Cancel();
                slideShowCancel = new();
                ImageFiles.Clear();
                ImageIndex = -1;
                TotalFiles = 0;
                slideShowElapsedMs = 0;
                SlideShow = CanPrevImage = CanNextImage = false;
                slideShowWatcher.Dispose();
            }
        }
        void SlideShowGoTo(int index, bool fromSlideShow = false, bool restart = false)
        {
            lock (slideShowLock)
            {
                SlideShowReCheck(ref index);

                if (MediaViewer != MediaViewer.Slide || index < 0 || index > ImageFiles.Count - 1)
                    return;

                if (SlideShow && !fromSlideShow)
                {
                    slideShowCancel.Cancel();
                    slideShowCancel = new();
                }                
                
                ImageIndex = index;
                CanPrevImage = ImageIndex > 0;
                CanNextImage = ImageIndex < ImageFiles.Count - 1;

                if (SlideShow && !CanNextImage)
                    SlideShow = false;

                slideShowElapsedMs = 0;
                Player.OpenAsync(ImageFiles[ImageIndex]);
            }
        }
        void SlideShowStop()
        {
            lock (slideShowLock)
            {
                slideShowCancel.Cancel();
                slideShowCancel = new();
                SlideShow = false;
                slideShowElapsedMs += (int) (DateTime.UtcNow.Ticks - slideShowStartedAt) / 10000;
            }
        }
        void SlideShowStart(bool inFullScreen = false, bool andTask = true)
        {
            lock (slideShowLock)
            {
                slideShowCancel.Cancel();
                slideShowCancel = new();
                SlideShow = true;
                if (inFullScreen)
                    Player.FullScreen();

                Player.Activity.ForceIdle();
                if (andTask)
                {
                    slideShowStartedAt = DateTime.UtcNow.Ticks;
                    Task.Run(SlideShowTask, slideShowCancel.Token);
                }
            }
        }
        void SlideShowToggleAction()
        {
            if (SlideShow)
                SlideShowStop();
            else if (canNextImage)
                SlideShowStart();
        }

        async Task SlideShowTask()
        {
            if (Player.CanPlay && slideShowElapsedMs < slideShowTimer)
                await Task.Delay(SlideShowTimer - slideShowElapsedMs, slideShowCancel.Token);

            while (Player.IsPlaying)
                await Task.Delay(100, slideShowCancel.Token);

            if (slideShowCancel.IsCancellationRequested || !SlideShow)
                return;

            SlideShowGoTo(ImageIndex + 1, true);
        }

        void SlideKeysRemove()
        {
            var keys = playerConfig.Player.KeyBindings;

            keys.Remove(Key.Left);
            keys.Remove(Key.Right);
            keys.Remove(Key.Home);
            keys.Remove(Key.End);
            keys.Remove(Key.Space);
            keys.Remove(Key.F5);
            keys.Remove(Key.Delete);
            keys.Remove(Key.C, false, true);
            keys.Remove(Key.X, false, true);
            keys.Remove(Key.PageDown);
            keys.Remove(Key.PageUp);
        }
        void SlideKeysAdd()
        {
            var keys = playerConfig.Player.KeyBindings;
            keys.AddCustom(Key.Left,    false,  () => SlideShowGoTo(ImageIndex - 1), "tmp01");
            keys.AddCustom(Key.Right,   false,  () => SlideShowGoTo(ImageIndex + 1), "tmp02");
            keys.AddCustom(Key.Home,    true,   () => SlideShowGoTo(0), "tmp03");
            keys.AddCustom(Key.End,     true,   () => SlideShowGoTo(ImageFiles.Count -1), "tmp04");
            keys.AddCustom(Key.Space,   true,   SlideShowToggleAction, "tmp05");
            keys.AddCustom(Key.F5,      true,   () => SlideShowStart(true), "tmp06");
            keys.AddCustom(Key.Delete,  true,   ImageDelete, "tmp07");
            keys.AddCustom(Key.C,       true,   () => ImageCutCopy(), "tmp08", false, true, false);
            keys.AddCustom(Key.X,       true,   () => ImageCutCopy(false), "tmp09", false, true, false);
            keys.AddCustom(Key.PageDown,false,  () => SlideShowGoTo(Math.Min(ImageFiles.Count - 1, ImageIndex + PageStep)), "tmp10");
            keys.AddCustom(Key.PageUp,  false,  () => SlideShowGoTo(Math.Max(0, ImageIndex - PageStep)), "tmp11");
        }
        void VideoKeysRestore()
        {
            var keys = playerConfig.Player.KeyBindings;
            keys.Add(Key.Left,  KeyBindingAction.SeekBackward);
            keys.Add(Key.Right, KeyBindingAction.SeekForward);
            keys.Add(Key.Space, KeyBindingAction.TogglePlayPause);
            keys.Add(Key.C,     KeyBindingAction.CopyToClipboard, false, true);
        }
        void ImageDelete()
        {
            if (deleteConfirmation && MessageBox.Show("Are you sure to delete this image?", "Delete Confirmation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            
            Player.Stop();
            try { File.Delete(ImageInfo.FullName); imageFilesChanged = true; } catch(Exception) { return; }
            SlideShowGoTo(ImageIndex); //canNextImage ? ImageIndex + 1 : (canPrevImage ? ImageIndex - 1 : 0));
        }
        void ImageCutCopy(bool copy = true)
        {
            try
            {
                var droplist = new System.Collections.Specialized.StringCollection { imageInfo.FullName };
                var data = new DataObject();
                data.SetFileDropList(droplist);
                data.SetData("Preferred DropEffect", new MemoryStream(copy ? DragDropEffectsCopyBytes : DragDropEffectsMoveBytes));
                Clipboard.SetDataObject(data);

                if (!copy) // This way we close the image so it can be paste (not great for the viewer)* similar to deleting a file when is already opened (ideally we close the input and we keep only the frame)
                    SlideShowGoTo(canNextImage ? ImageIndex + 1 : (canPrevImage ? ImageIndex - 1 : 0));
            } catch (Exception) {}
        }
        static byte[] DragDropEffectsCopyBytes = BitConverter.GetBytes((int)DragDropEffects.Copy);
        static byte[] DragDropEffectsMoveBytes = BitConverter.GetBytes((int)DragDropEffects.Move);
        #endregion

        #region OSD Msg
        const int MSG_TIMEOUT = 3500;
        CancellationTokenSource cancelMsgToken = new();
        public string Msg { get => msg; set { cancelMsgToken.Cancel(); msg = value; OnPropertyChanged(nameof(Msg)); cancelMsgToken = new(); Task.Run(FadeOutMsg, cancelMsgToken.Token); } }
        string msg;
        private async Task FadeOutMsg()
        {
            await Task.Delay(MSG_TIMEOUT, cancelMsgToken.Token);
            Utils.UIInvoke(() => { msg = ""; PropertyChanged?.Invoke(this, new(nameof(Msg))); });
        }
        #endregion
    }

    public enum MediaViewer
    {
        Image,
        Video,
        Slide
    }
}
