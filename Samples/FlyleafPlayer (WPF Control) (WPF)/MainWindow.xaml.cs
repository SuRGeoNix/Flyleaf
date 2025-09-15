using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using static FlyleafLib.Utils;
using static FlyleafPlayer.AppConfig;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer;

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
    /// Flyleaf Player binded to FlyleafME (This can be swapped and will not belong to this window)
    /// </summary>
    public Player           Player              { get; set; }

    /// <summary>
    /// FlyleafME Media Element Control
    /// </summary>
    public FlyleafME        FlyleafME           { get; set; }

    public ICommand         OpenWindow          { get; set; }
    public ICommand         CloseWindow         { get; set; }

    public SlideShowConfig  SlideShowConfig     { get; set; } = App.AppConfig.SlideShow;
    public GeneralConfig    GeneralConfig       { get; set; } = App.AppConfig.General;

    static bool     runOnce;
    Config          playerConfig;
    bool            ReversePlaybackChecked;
    static int      openedWindows;
    bool            closed = false;
    static object   lockTray = new();

    public MainWindow()
    {
        lock (lockTray) openedWindows++;

        OpenWindow      = new RelayCommandSimple(() => CreateNewWindow(this));
        CloseWindow     = new RelayCommandSimple(() => BtnClose_Click(null, null));
        ShowPrevImage   = new RelayCommandSimple(() => SlideShowGoTo(SlidePosition.Prev));
        ShowNextImage   = new RelayCommandSimple(() => SlideShowGoTo(SlidePosition.Next));
        SlideShowToggle = new RelayCommandSimple(SlideShowToggleAction);
        SlideShowRestart= new RelayCommandSimple(() => { SlideShowStart(false, false); SlideShowGoTo(SlidePosition.Start); }); // Note: When we start SlideShow with non-current ImageIndex use Start(notask) and GoTo to start the task
            
        FlyleafME = new(this)
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
            PreferredPortraitHeight = 600,

            // Allow Flyleaf WPF Control to Load UIConfig and Save both Config & UIConfig (Save button will be available in settings)
            EnginePath    = App.EnginePath,
            ConfigPath    = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.Config.json"),
            UIConfigPath  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.UIConfig.json")
        };

        InitializeComponent();
        DataContext = FlyleafME; // Allowing FlyleafHost to access our Player
    }
    void X_Closing(object sender, CancelEventArgs e)
    {
        // NOTE: FlyleafME.Player is our Player in case of Swap and not Player

        if (!GeneralConfig.SingleInstance)
            return;

        lock (lockTray)
        {
            if (closed)
                return;

            if (openedWindows == 1)
            {
                e.Cancel = true;
                FlyleafME.CloseCanceled();
                FlyleafME.Player.Stop();

                if (mediaViewer != MediaViewer.Video)
                    MediaViewer = MediaViewer.Video;

                FlyleafME.Surface.ShowInTaskbar = false;
                FlyleafME.Surface.Hide();
            }
            else
            {
                FlyleafME.Player.Dispose();
                closed = true;
                openedWindows--;
            }
        }
    }

    void BtnMinimize_Click(object sender, RoutedEventArgs e) => FlyleafME.IsMinimized = true;
    void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    public static MainWindow GetWindowFromPlayer(Player player)
    {
        FlyleafHost flhost = null;
        MainWindow mw = null;

        UIInvokeIfRequired(() =>
        {
            flhost  = (FlyleafHost) player.Host;
            mw      = (MainWindow) flhost.Overlay;
        });

        return mw;
    }
    static void CreateNewWindow(MainWindow mw)
    {
        MainWindow mwNew = new()
        {
            Width   = mw.Width,
            Height  = mw.Height,
        };

        mwNew.Show();
    }

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Engine.IsLoaded)
            WindowAndEngineLoaded();
        else
            Engine.Loaded += (o, e) => WindowAndEngineLoaded();
    }

    void WindowAndEngineLoaded()
    {
        LoadPlayer();

        UIInvokeIfRequired(() =>
        {
            FlyleafME.Player = Player;

            if (GeneralConfig.SingleInstance)
            {
                FlyleafME.Overlay.Closing += X_Closing;
                FlyleafME.Surface.Closing += X_Closing;
            }
        });

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
                AddFirewallRule();
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

    void LoadPlayer()
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

        userClearScreen = playerConfig.Video.ClearScreen; // slide show disable this

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

        // Testing misc
        //playerConfig.Demuxer.AllowFindStreamInfo = false;
        #endif
        
        // Initializes the Player
        Player = new Player(playerConfig);

        Player.Opening          += Player_Opening;
        Player.OpenCompleted    += Player_OpenCompleted;

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
        keys.AddCustom(Key.N, true, () => CreateNewWindow(GetWindowFromPlayer(Player)), "New Window", false, true, false);
        keys.AddCustom(Key.W, true, () => GetWindowFromPlayer(Player).BtnClose_Click(null, null), "Close Window", false, true, false);

        // We might saved the tmp keys (restore them)
        if (Player.Config.Loaded && keys.Exists("tmp01"))
        {
            SlideKeysRemove();
            VideoKeysAdd();
        }
    }

    static Config DefaultConfig()
    {
        Config config = new();
        config.Demuxer.FormatOptToUnderlying= true;     // Mainly for HLS to pass the original query which might includes session keys
        config.Audio.FiltersEnabled         = true;     // To allow embedded atempo filter for speed
        config.Video.GPUAdapter             = "";       // Set it empty so it will include it when we save it
        config.Subtitles.SearchLocal        = true;
        return config;
    }

    #region Photo Viewer / Slide Show
    /* TODO
        * Sorting (+based on explorer folder settings / numerical*)
        * Shuffle
        * Include / Exclude  (SubFolder/Image/Animation/Video *Regex)
        * Prepare next? (for faster/better transition)
        * Preview Film/Slider/Gallery?
        * Config / Keys** Save?
        */

    public ICommand     ShowNextImage   { get; set; }
    public ICommand     ShowPrevImage   { get; set; }
    public ICommand     SlideShowToggle { get; set; }
    public ICommand     SlideShowRestart{ get; set; }

    public bool         CanNextImage    { get => canNextImage;  set { if (canNextImage == value) return;    canNextImage = value;   OnPropertyChanged(nameof(CanNextImage)); } }
    bool canNextImage;

    public bool         CanPrevImage    { get => canPrevImage;  set { if (canPrevImage == value) return;    canPrevImage = value;   OnPropertyChanged(nameof(CanPrevImage)); } }
    bool canPrevImage;

    public MediaViewer  MediaViewer     { get => mediaViewer;   set { if (mediaViewer == value) return;     prevMediaViewer = mediaViewer; mediaViewer = value; SwitchMediaViewer(); OnPropertyChanged(nameof(MediaViewer)); } }
    MediaViewer mediaViewer = MediaViewer.Video; MediaViewer prevMediaViewer;

    public string       ImageTitle      { get => imageTitle;    set { if (imageTitle == value) return;      imageTitle = value;     OnPropertyChanged(nameof(ImageTitle)); } }
    string imageTitle;

    public List<string> ImageFiles      { get; set; } = [];
    public int          ImageIndex      { get => imageIndex;    set { if (imageIndex == value) return;      imageIndex = value; UIImageIndex = imageIndex + 1; OnPropertyChanged(nameof(UIImageIndex)); } }
    int imageIndex = -1;
    public int          UIImageIndex    { get; set; }

    public bool         SlideShow       { get => slideShow;     set { if (slideShow == value) return;       slideShow = value;      OnPropertyChanged(nameof(SlideShow)); } }
    bool slideShow;

    public int          TotalFiles      { get => totalFiles;    set { if (totalFiles == value) return;      totalFiles = value;     OnPropertyChanged(nameof(TotalFiles)); } }
    int totalFiles;

    CancellationTokenSource
                        slideShowCancel = new();
    object              slideShowLock   = new();
    bool                slideShowOpening;           // avoid rechecks
    int                 slideShowElapsedMs;         // For Pause/Play to keep the remaining ms
    long                slideShowStartedAt;         // For Pause/Play to keep the remaining ms
    FileSystemWatcher   slideShowWatcher;           // Monitor ImageFolder for changes
    bool                userKeepRatioOnResize;
    string              imageFolder;
    int                 folderId;

    [GeneratedRegex("\\.(apng|avif|bmp|jpg|jpeg|gif|ico|png|svg|tiff|webp|jfif)$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex RegexImages();

    void Player_Opening(object sender, OpeningArgs e)
    {
        if (slideShowOpening)
            ImageTitle = ImageFiles[ImageIndex];
        else if (!RegexImages().IsMatch(e.Url))
        {
            if (mediaViewer != MediaViewer.Video)
                MediaViewer = MediaViewer.Video;
        }
        else
        {
            try
            {
                var url = e.Url.AsSpan();
                string curFolder = null;
                bool folderFound = false;

                for (int i = url.Length - 1; i >= 0; i--)
                    if (url[i] == '/' || url[i] == '\\')
                    {
                        ImageTitle  = url[(i + 1)..].ToString();
                        curFolder   = url[..i].ToString();
                        folderFound = true;
                        break;
                    }

                if (!folderFound)
                    MediaViewer = MediaViewer.Image;
                else if (curFolder.Equals(imageFolder, StringComparison.OrdinalIgnoreCase) && mediaViewer == MediaViewer.Slide) // Mainly manually opening from the same folder
                {
                    lock (slideShowLock)
                    {
                        int imageIndex = -1;
                        for (int i = 0; i < ImageFiles.Count; i++)
                            if (ImageFiles[i].Equals(ImageTitle, StringComparison.OrdinalIgnoreCase))
                            { imageIndex = i; break; }

                        // if (imageIndex == -1) // should never happen*?

                        ImageIndex  = imageIndex;
                        CanPrevImage= imageIndex > 0;
                        CanNextImage= imageIndex < ImageFiles.Count - 1;
                    }
                }
                else
                {
                    imageFolder = curFolder;
                    RefreshFolder(true);
                }
            }
            catch
            {
                MediaViewer = MediaViewer.Image;
            }
        }
    }
    void Player_OpenCompleted(object sender, OpenCompletedArgs e)
    {
        if (mediaViewer == MediaViewer.Video || e.IsSubtitles)
            return;
        else if (mediaViewer == MediaViewer.Image && imageTitle == null)
            ImageTitle = Player.Playlist.Selected.Title;
        else
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

    bool userClearScreen;
    void SwitchMediaViewer()
    {
        var keys = playerConfig.Player.KeyBindings;

        switch (prevMediaViewer)
        {
            case MediaViewer.Video:
                userKeepRatioOnResize = FlyleafME.KeepRatioOnResize;
                break;

            case MediaViewer.Image:
                ImageKeysRemove();
                break;

            case MediaViewer.Slide:
                SlideShowDeInit();
                SlideKeysRemove();

                break;
        }

        switch (MediaViewer)
        {
            case MediaViewer.Video:
                imageFolder = null;
                Player.Config.Video.ClearScreen = userClearScreen;

                FlyleafME.ToggleFullScreenOnDoubleClick = AvailableWindows.Both;
                FlyleafME.KeyBindings = AvailableWindows.Both;
                FlyleafME.KeepRatioOnResize = userKeepRatioOnResize;

                VideoKeysAdd();

                break;

            case MediaViewer.Image:
                Player.Config.Video.ClearScreen = userClearScreen;

                FlyleafME.ToggleFullScreenOnDoubleClick = AvailableWindows.Surface;
                FlyleafME.KeyBindings = AvailableWindows.Surface;
                FlyleafME.KeepRatioOnResize = userKeepRatioOnResize;

                ImageKeysAdd();
                break;

            case MediaViewer.Slide:
                userClearScreen = Player.Config.Video.ClearScreen;
                Player.Config.Video.ClearScreen = false;
                FlyleafME.ToggleFullScreenOnDoubleClick = AvailableWindows.Surface;
                FlyleafME.KeyBindings = AvailableWindows.Both;
                FlyleafME.KeepRatioOnResize = false;

                SlideKeysAdd();
                break;
        }
    }

    private void DialogHost_DialogOpened(object sender, MaterialDesignThemes.Wpf.DialogOpenedEventArgs e)
        { if (MediaViewer == MediaViewer.Slide) FlyleafME.KeyBindings = AvailableWindows.Surface; }
    private void DialogHost_DialogClosed(object sender, MaterialDesignThemes.Wpf.DialogClosedEventArgs e)
    {
        if (MediaViewer == MediaViewer.Slide)
        {
            FlyleafME.KeyBindings = AvailableWindows.Both;
            if ((string)e.Parameter == "Save")
                App.AppConfig.Save();
        }
    }

    [DllImport("shlwapi.dll", EntryPoint = "StrCmpLogicalW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int StrCmpLogical(string psz1, string psz2);

    void RefreshFolder(bool init)
    {
        Task.Run(() =>
        {
            int curId = Interlocked.Increment(ref folderId);

            var files = Directory.EnumerateFiles(imageFolder);
            List<string> imageFiles = [];
            foreach (var file in files)
            {
                if (curId != folderId)
                    return;

                if (!RegexImages().IsMatch(file))
                    continue;

                imageFiles.Add(Path.GetFileName(file));
                if (imageFiles.Count > SlideShowConfig.MaxFiles)
                    break;
            }

            imageFiles.Sort(StrCmpLogical);

            UIInvokeIfRequired(() =>
            {
                lock (slideShowLock)
                {
                    if (curId != folderId)
                        return;

                    if (imageFiles.Count == 0)
                        MediaViewer = MediaViewer.Video;
                    else if (imageFiles.Count == 1)
                    {
                        if (!init)
                            Player.OpenAsync(imageFolder + "\\" + imageFiles[0]); // probably from our delete
                        MediaViewer = MediaViewer.Image;
                    }
                    else
                    {
                        if (init)
                            MediaViewer = MediaViewer.Slide; // resets
                            
                        int imageIndex = -1;

                        for (int i = 0; i < imageFiles.Count; i++)
                            if (imageFiles[i].Equals(ImageTitle, StringComparison.OrdinalIgnoreCase))
                                { imageIndex = i; break; }

                        ImageFiles  = imageFiles;
                        TotalFiles  = imageFiles.Count;
                        CanPrevImage= imageIndex > 0;
                        CanNextImage= imageIndex < ImageFiles.Count - 1;

                        if (SlideShow && !CanNextImage)
                            SlideShow = false;

                        if (init)
                        {
                            slideShowWatcher = new(imageFolder)
                            {
                                EnableRaisingEvents = true,
                                NotifyFilter = NotifyFilters.FileName
                            };

                            slideShowWatcher.Renamed += (o, e) =>
                            {
                                if (RegexImages().IsMatch(e.Name))
                                    RefreshFolder(false);
                            };

                            slideShowWatcher.Deleted += (o, e) =>
                            {
                                if (RegexImages().IsMatch(e.Name))
                                    RefreshFolder(false);
                            };

                            slideShowWatcher.Created += (o, e) =>
                            {
                                if (RegexImages().IsMatch(e.Name))
                                    RefreshFolder(false);
                            };
                        }

                        if (imageIndex == -1) // Mainly from our Delete
                            SlideShowGoTo(SlidePosition.Refresh);
                        else
                            ImageIndex = imageIndex;
                    }
                }
            });
        });
    }

    void SlideShowDeInit()
    {
        //UIInvokeIfRequired(() =>
        //{
            lock (slideShowLock)
            {
                slideShowCancel.Cancel();
                slideShowCancel     = new();
                imageFolder         = null;
                ImageFiles.Clear();
                ImageIndex          = -1;
                TotalFiles          = 0;
                slideShowElapsedMs  = 0;
                SlideShow           = CanPrevImage = CanNextImage = false;
                slideShowWatcher.Dispose();
            }
        //});
    }
    enum SlidePosition
    {
        Start,
        Prev,
        Next,
        Refresh, // Same Index
        PrevPage,
        NextPage,
        End
    }
    void SlideShowGoTo(SlidePosition pos, bool fromSlideShow = false)
    {
        lock (slideShowLock)
        {
            if (MediaViewer != MediaViewer.Slide)
                return;

            if (SlideShow && !fromSlideShow)
            {
                slideShowCancel.Cancel();
                slideShowCancel = new();
            }

            if ((ImageIndex < 1                     && (pos == SlidePosition.Prev || pos == SlidePosition.PrevPage)) ||
                (ImageIndex >= ImageFiles.Count - 1 && (pos == SlidePosition.Next || pos == SlidePosition.NextPage)))
                return; // out of bounds

            if (pos == SlidePosition.Start || ImageFiles.Count < 2)
                ImageIndex = 0;
            else if (pos == SlidePosition.End)
                ImageIndex = ImageFiles.Count - 1;
            else if (pos == SlidePosition.Prev)
                ImageIndex--;
            else if (pos == SlidePosition.Next)
                ImageIndex++;
            else if (pos == SlidePosition.PrevPage)
                ImageIndex = Math.Max(0, ImageIndex - SlideShowConfig.PageStep);
            else if (pos == SlidePosition.NextPage)
                ImageIndex = Math.Min(ImageFiles.Count - 1, ImageIndex + SlideShowConfig.PageStep);
            
            CanPrevImage = ImageIndex > 0;
            CanNextImage = ImageIndex < ImageFiles.Count - 1;

            if (SlideShow && !CanNextImage)
                SlideShow = false;

            slideShowElapsedMs  = 0;
            slideShowOpening    = true;
            Player.OpenAsync(imageFolder + "\\" + ImageFiles[ImageIndex]);
            slideShowOpening    = false;
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
        if (Player.CanPlay && slideShowElapsedMs < SlideShowConfig.SlideShowTimer)
            await Task.Delay(SlideShowConfig.SlideShowTimer - slideShowElapsedMs, slideShowCancel.Token);

        while (Player.IsPlaying)
            await Task.Delay(100, slideShowCancel.Token);

        if (slideShowCancel.IsCancellationRequested || !SlideShow)
            return;

        UI(() => SlideShowGoTo(SlidePosition.Next, true));
    }

    void ImageKeysRemove()
    {
        var keys = playerConfig.Player.KeyBindings;
        keys.Remove(Key.Delete);
        keys.Remove(Key.C, false, true);
        keys.Remove(Key.X, false, true);
        keys.Remove(Key.Q);
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
        keys.Remove(Key.PageDown);
        keys.Remove(Key.PageUp);

        ImageKeysRemove();
    }

    void ImageKeysAdd()
    {
        var keys = playerConfig.Player.KeyBindings;
        keys.AddCustom(Key.Delete,  true,   ImageDelete, "tmp07");
        keys.AddCustom(Key.C,       true,   () => ImageCutCopy(), "tmp08", false, true, false);
        keys.AddCustom(Key.X,       true,   () => ImageCutCopy(false), "tmp09", false, true, false);
        keys.AddCustom(Key.Q,       true,   () => { Player.Stop(); MediaViewer = MediaViewer.Video; }, "tmp12", false, true, false);
    }

    void SlideKeysAdd()
    {
        var keys = playerConfig.Player.KeyBindings;
        keys.AddCustom(Key.Left,    false,  () => SlideShowGoTo(SlidePosition.Prev), "tmp01");
        keys.AddCustom(Key.Right,   false,  () => SlideShowGoTo(SlidePosition.Next), "tmp02");
        keys.AddCustom(Key.Home,    true,   () => SlideShowGoTo(SlidePosition.Start), "tmp03");
        keys.AddCustom(Key.End,     true,   () => SlideShowGoTo(SlidePosition.End), "tmp04");
        keys.AddCustom(Key.Space,   true,   SlideShowToggleAction, "tmp05");
        keys.AddCustom(Key.F5,      true,   () => SlideShowStart(true), "tmp06");
        keys.AddCustom(Key.PageDown,false,  () => SlideShowGoTo(SlidePosition.NextPage), "tmp10");
        keys.AddCustom(Key.PageUp,  false,  () => SlideShowGoTo(SlidePosition.PrevPage), "tmp11");

        ImageKeysAdd();
    }
    void VideoKeysAdd()
    {
        var keys = playerConfig.Player.KeyBindings;
        keys.Add(Key.Left,  KeyBindingAction.SeekBackward);
        keys.Add(Key.Right, KeyBindingAction.SeekForward);
        keys.Add(Key.Space, KeyBindingAction.TogglePlayPause);
        keys.Add(Key.C,     KeyBindingAction.CopyToClipboard, false, true);
        keys.Add(Key.Q,     KeyBindingAction.Stop, false, true);
    }
    void ImageDelete()
    {
        if (SlideShowConfig.DeleteConfirmation && MessageBox.Show("Are you sure to delete this image?", "Delete Confirmation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        Player.Stop();
        try { File.Delete(imageFolder + "\\" + ImageFiles[ImageIndex]); } catch(Exception) { return; }
    }
    void ImageCutCopy(bool copy = true)
    {
        try
        {
            var filePath = ImageFiles.Count > 0 ? imageFolder + "\\" + ImageFiles[ImageIndex] : Player.Playlist.Selected.Url;
            var droplist = new System.Collections.Specialized.StringCollection { filePath };
            var data = new DataObject();
            data.SetFileDropList(droplist);
            data.SetData("Preferred DropEffect", new MemoryStream(copy ? DragDropEffectsCopyBytes : DragDropEffectsMoveBytes));
            Clipboard.SetDataObject(data);

            // This way we close the image so it can be pasted (not great for the viewer)* similar to deleting a file when is already opened (ideally we close the input and we keep only the frame)
            if (!copy)
            {
                if (ImageFiles.Count == 1 || MediaViewer == MediaViewer.Image)
                {
                    Player.Stop();
                    MediaViewer = MediaViewer.Video;
                }
                else
                    SlideShowGoTo(canNextImage ? SlidePosition.Next : (canPrevImage ? SlidePosition.Prev : SlidePosition.Start));
            }
                    
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
    async Task FadeOutMsg()
    {
        await Task.Delay(MSG_TIMEOUT, cancelMsgToken.Token);
        UIInvoke(() => { msg = ""; PropertyChanged?.Invoke(this, new(nameof(Msg))); });
    }
    #endregion
}

public enum MediaViewer
{
    Image,
    Video,
    Slide
}
