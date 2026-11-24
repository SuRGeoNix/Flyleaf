using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;

using Microsoft.Win32;

using FlyleafLib.MediaPlayer;
using FlyleafLib;
using Flyleaf.Controls.WPF;
using Flyleaf.FFmpeg;

namespace Flyleaf;

public partial class MainWindow : Window
{
    const int WM_EXITSIZEMOVE = 0x0232;
    const int WM_SIZING = 0x0214;
    const int WM_GETMINMAXINFO = 0x0024;
    const int WM_SETTINGCHANGE = 0x001A;
    const int WM_THEMECHANGED = 0x031A;

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hwnd, out RectNative rect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int Size;
        public RectNative Monitor;
        public RectNative Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MinMaxInfo
    {
        public PointNative Reserved;
        public PointNative MaxSize;
        public PointNative MaxPosition;
        public PointNative MinTrackSize;
        public PointNative MaxTrackSize;
    }

    FlyleafTheme theme => MediaElement.UIConfig.Appearance.Theme;
    FlyleafAccent accent => MediaElement.UIConfig.Appearance.Accent;
    bool headerVisible = true;
    bool playbackBarVisible = true;
    bool outlineVisible = true;
    double preferredLandscapeWidth, preferredPortraitHeight;
    RectNative? dockVideoBounds;
    bool dockWasFullArea;
    bool resizedInteractively;
    bool applyingFullScreen;
    RectNative normalWindowBounds;
    WindowState previousWindowState;
    ResizeMode previousResizeMode;
    HwndSource? windowSource;
    WindowResizeFrame? resizeFrame;
    IDisposable? themePopupActivity;
    System.Windows.Media.Brush[]? windowIconBrushes;

    public Player Player { get; }
    internal PlayerProfiles.Session Profile { get; }
    Rect lastNormalBounds = Rect.Empty;
    readonly FlyleafPanelPlacement[]? startupPanels;
    FlyleafPanelPlacement[]? panelLayoutBaseline, closingPanels;
    bool restoringPanelLayout;
    Window? focusSurface, focusOverlay;

    public MainWindow() : this(null, App.Instance.Config.Window.GetStartupPanelLayout()) { }

    internal MainWindow(Guid? profileId, FlyleafPanelPlacement[]? panelLayout)
    {
        startupPanels = panelLayout;
        var profiles = App.Instance.Profiles;
        Profile = profileId is { } id ? profiles.Open(id) : profiles.Open();
        Player = new Player(Profile.Player);

        InitializeComponent();
        MediaTitle.DataContext = MediaElement;
        QueueSearchButton.DataContext = MediaElement;

        if (App.Instance.UseTransparentWindows)
        {
            // A layered owner has per-pixel transparency. Alpha-zero pixels let mouse input pass through to the desktop.
            var inputBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
            inputBackground.Freeze();
            AllowsTransparency = true;
            Background = inputBackground;
            MediaElement.Host.VideoBackground = inputBackground;
            Player.OpeningVideoStream += PreferTransparentVideoDecoder;
        }
        Width = Math.Max(MinWidth, App.Instance.Config.Window.Width);
        Height = Math.Max(MinHeight, App.Instance.Config.Window.Height);
        WindowOutline.Resources.MergedDictionaries.Add(MediaElement.Resources);
        RestoreWindowPosition();
        MediaElement.SetCurrentValue(FlyleafME.UIConfigProperty, Profile.Presentation);
        DataContext = this;
        BuildAccentMenu();
        MediaElement.MenuBuilding += MediaElement_MenuBuilding;
        MediaElement.OpenInputRequested += MediaElement_OpenInputRequested;
        MediaElement.OpenFilesRequested += MediaElement_OpenFilesRequested;
        MediaElement.Host.OnSurfaceDrop += MediaElement_Drop;
        MediaElement.Host.OnOverlayDrop += MediaElement_Drop;
        InitializePlaylistCommands();
        var customKeybindingDefaults = FlyleafME.CreateDefaultKeybindings().Keys
            .Select(binding => (Id: GetApplicationActionId(binding), Gesture: FlyleafShortcutGesture.From(binding)))
            .Where(binding => binding.Id != null)
            .GroupBy(binding => binding.Id!).ToDictionary(group => group.Key, group => group.First().Gesture);
        customKeybindingDefaults["app.newWindow"] = NewWindowShortcut;
        customKeybindingDefaults["app.closeWindow"] = CloseWindowShortcut;
        MediaElement.RegisterDefaultPanels(settings =>
        {
            settings.CustomKeybindingDefaults = customKeybindingDefaults;
            settings.Pages.Insert(0, new("application", "Application", c => new ApplicationSettings { Context = c }));
            settings.Pages.Insert(1, new("profiles", "Profiles", c => new ProfileSettings(this) { Context = c }));
            settings.Pages.Insert(2, new("file-associations", "File associations", _ => new FileAssociationSettings(App.Instance.Config)));

            void RefreshResetDefaults() =>
                FlyleafSettingsReset.SetDefaults(settings, profiles.GetResetPlayer(Profile));

            RefreshResetDefaults();

            EventHandler profilesChanged = (_, _) => RefreshResetDefaults();
            profiles.MetadataChanged += profilesChanged;
            Closed += (_, _) => profiles.MetadataChanged -= profilesChanged;
        });
        PlaybackBar.MediaElement = MediaElement;
        App.Instance.Profiles.Observe(Profile);
        Activated += PlayerWindow_Activated;
        Closing += (_, e) =>
        {
            if (e.Cancel) return;
            RememberNormalBounds();
            if (CanCapturePanelLayout) closingPanels = ReadPanelLayout();
        };
        MediaElement.RegisterPanel(new FlyleafPanelDefinition("review-notes", "Review notes", ReviewNotesPanel.Create)
        {
            AllowedModes = FlyleafPanelModes.Docked | FlyleafPanelModes.Floating,
            AllowedSides = FlyleafPanelSides.Left,
            DefaultMode = FlyleafPanelMode.Docked,
            DefaultSide = FlyleafPanelSide.Left,
            MinimumWidth = 260, MaximumWidth = 420, PreferredWidth = 290
        });
        SeekWhileDraggingMenuItem.SetBinding(MenuItem.IsCheckedProperty,
            new Binding("UIConfig.PlaybackBar.SeekWhileDragging") { Source = MediaElement, Mode = BindingMode.TwoWay });
        MediaElement.UIConfigChanged += MediaElement_UIConfigChanged;
        MediaElement.RatioChanged += MediaElement_RatioChanged;
        MediaElement.ViewportLayoutChanged += MediaElement_ViewportLayoutChanged;
        MediaElement.ViewportLayoutChanging += MediaElement_ViewportLayoutChanging;
        MediaElement.FullScreenChanged += MediaElement_FullScreenChanged;

        KeepWindowRatioMenuItem.SetBinding(MenuItem.IsCheckedProperty,
            new Binding("Config.Window.KeepWindowRatioOnResize")
            {
                Source = App.Instance,
                Mode = BindingMode.TwoWay
            });

        PinButton.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(Topmost))
        {
            Source = this,
            Mode = BindingMode.TwoWay
        });
        FullscreenButton.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(MediaElement.IsFullScreen))
        {
            Source = MediaElement,
            Mode = BindingMode.TwoWay
        });

        Player.PropertyChanged += Player_PropertyChanged;
        Player.Video.PropertyChanged += Video_PropertyChanged;
        Player.Activity.PropertyChanged += Activity_PropertyChanged;
        if (ThemeButton.ContextMenu is ContextMenu themeMenu)
        {
            themeMenu.Opened += ThemeMenu_Opened;
            themeMenu.Closed += ThemeMenu_Closed;
        }
        UpdateWindowActions();
        UpdateThemeMenu();
        UpdateWindowIcon();
        SourceInitialized += Window_SourceInitialized;
        ContentRendered += Window_FirstContentRendered;
    }

    static void PreferTransparentVideoDecoder(object? sender, Player.OpeningVideoStreamArgs e)
    {
        var stream = e.VideoStream;
        if (stream.Demuxer.Name?.Contains("webm", StringComparison.OrdinalIgnoreCase) != true ||
            !stream.Metadata.Any(entry => entry.Key.Equals("alpha_mode", StringComparison.OrdinalIgnoreCase) && entry.Value == "1"))
            return;

        e.PreferredDecoder = stream.CodecID switch
        {
            AVCodecID.Vp8 => "libvpx",
            AVCodecID.Vp9 => "libvpx-vp9",
            _ => null
        };
    }

    void Window_FirstContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= Window_FirstContentRendered;
        focusSurface = MediaElement.Host.Surface;
        focusOverlay = MediaElement.Host.Overlay;
        if (focusSurface != null) focusSurface.Activated += PlayerWindow_Activated;
        if (focusOverlay != null) focusOverlay.Activated += PlayerWindow_Activated;
        RestoreStartupPanels();
        panelLayoutBaseline = ReadPanelLayout();
        // The native surface/overlay now exist; startup focus can still belong
        // to the owner, which does not receive the host's playback key handlers.
        MediaElement.FocusPlayback();
    }

    bool CanCapturePanelLayout => IsLoaded && panelLayoutBaseline != null && closingPanels == null && !restoringPanelLayout &&
        App.Instance.Config.Window.StartupPanelLayout == StartupPanelLayoutMode.LastUsed;

    FlyleafPanelPlacement[] ReadPanelLayout()
    {
        var snapshot = MediaElement.CapturePanelLayout().ToDictionary(panel => panel.Id, StringComparer.Ordinal);
        foreach (var panel in MediaElement.RegisteredPanels)
            snapshot.TryAdd(panel.Id, new(panel.Id, panel.DefaultMode, panel.DefaultSide, panel.PreferredWidth, IsOpen: false));
        return snapshot.Values.ToArray();
    }

    internal FlyleafPanelPlacement[]? CapturePanelLayout()
    {
        if (!CanCapturePanelLayout) return null;
        var snapshot = ReadPanelLayout();
        SavePanelLayout(snapshot);
        return snapshot;
    }

    void SavePanelLayout(FlyleafPanelPlacement[] snapshot, bool saveOpenState = false)
    {
        if (panelLayoutBaseline != null)
            App.Instance.Config.Window.MergePanelLayout(panelLayoutBaseline, snapshot, saveOpenState);
        panelLayoutBaseline = snapshot;
    }

    internal void SaveCurrentPanelLayout()
    {
        App.Instance.Config.Window.SaveCurrentPanelLayout(ReadPanelLayout());
        MediaElement.ShowOSD("Panel layout saved");
    }

    internal void ResetPanelLayout()
    {
        var (left, right) = MediaElement.GetDockInsets(MediaElement.ActualWidth);
        dockVideoBounds = null;
        restoringPanelLayout = true;
        try
        {
            MediaElement.ResetPanelLayout();
            if (left + right > 0 && WindowState == WindowState.Normal && !MediaElement.IsFullScreen &&
                windowSource is { IsDisposed: false } source && GetWindowRect(source.Handle, out var window))
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                int leftPixels = (int)Math.Round(left * dpi.DpiScaleX);
                int rightPixels = (int)Math.Round(right * dpi.DpiScaleX);
                SetWindowPos(source.Handle, IntPtr.Zero, window.Left + leftPixels, window.Top,
                    window.Right - window.Left - leftPixels - rightPixels, window.Bottom - window.Top, 0x0004 | 0x0010);
            }
        }
        finally { restoringPanelLayout = false; }

        panelLayoutBaseline = ReadPanelLayout();
        App.Instance.Config.Window.StartupPanelLayout = StartupPanelLayoutMode.Default;
        MediaElement.FocusPlayback();
    }

    void RestoreStartupPanels()
    {
        if (startupPanels is not { Length: > 0 }) return;
        restoringPanelLayout = true;
        try
        {
            MediaElement.RestorePanelLayout(startupPanels);
            var (left, right) = MediaElement.GetRequestedDockInsets();
            if (left + right <= 0 || windowSource == null || !GetWindowRect(windowSource.Handle, out var window)) return;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            int leftPixels = (int)Math.Round(left * dpi.DpiScaleX);
            int rightPixels = (int)Math.Round(right * dpi.DpiScaleX);
            var bounds = WindowRatioSizing.AroundVideo(window.Left, window.Top, window.Right - window.Left,
                window.Bottom - window.Top, leftPixels, 0, rightPixels, 0);
            SetWindowPos(windowSource.Handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0004 | 0x0010);
        }
        finally { restoringPanelLayout = false; }
    }

    void MediaElement_UIConfigChanged(object? sender, EventArgs e)
    {
        UpdateThemeMenu();
        UpdateWindowIcon();
        UpdatePlaybackBarVisibility();
    }

    void UpdateWindowIcon()
    {
        System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)MediaElement.FindResource(key);
        var brushes = new[] { Brush("FlyleafME.LogoMarkBrush"), Brush("FlyleafME.LogoFoldBrush"), Brush("FlyleafME.LogoPlayBrush"), Brush("FlyleafME.LogoOutlineBrush") };
        if (windowIconBrushes?.SequenceEqual(brushes) == true) return;
        windowIconBrushes = brushes;
        Icon = FlyleafLogo.CreateImageSource(brushes[0], brushes[1], brushes[2], brushes[3]);
        App.Instance.RefreshTrayAppearance(this);
    }

    void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || MediaElement.IsFullScreen)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
        }
    }

    void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Player.CanPlay) or nameof(Player.Status))
            UpdateHeaderVisibility();
    }

    void Video_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Video.IsOpened))
            Dispatcher.BeginInvoke(new Action(ApplyVideoWindowRatio));
    }

    void Activity_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Player.Activity.Mode))
            UpdateHeaderVisibility();
    }

    void ThemeMenu_Opened(object sender, RoutedEventArgs e)
        => themePopupActivity ??= FlyleafPopupActivity.Hold(Player);

    void ThemeMenu_Closed(object sender, RoutedEventArgs e)
    {
        themePopupActivity?.Dispose();
        themePopupActivity = null;
    }

    void UpdateHeaderVisibility()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateHeaderVisibility);
            return;
        }

        bool show = MediaElement.HasDockedPanels || (!Player.CanPlay && Player.Status != Status.Opening) || Player.Activity.Mode == ActivityMode.FullActive;
        if (show != headerVisible)
        {
            headerVisible = show;
            HeaderBar.IsHitTestVisible = show;
            HeaderBar.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0,
                TimeSpan.FromMilliseconds(show ? 160 : 350)));
        }
        UpdatePlaybackBarVisibility();
        UpdateWindowOutlineVisibility();
    }

    void UpdatePlaybackBarVisibility()
    {
        if (PlaybackBar == null)
            return;

        bool show = MediaElement.UIConfig.PlaybackBar.Visibility switch
        {
            FlyleafVisibilityPolicy.Always => true,
            FlyleafVisibilityPolicy.Never => false,
            _ => (!Player.CanPlay && Player.Status != Status.Opening) || Player.Activity.Mode == ActivityMode.FullActive
        };
        if (show != playbackBarVisible)
        {
            playbackBarVisible = show;
            PlaybackBar.IsHitTestVisible = show;
            PlaybackBar.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0,
                TimeSpan.FromMilliseconds(show ? 160 : 350)));
        }
        UpdateWindowOutlineVisibility();
    }

    void UpdateWindowOutlineVisibility(DpiScale? dpiOverride = null)
    {
        if (WindowOutline == null)
            return;

        bool framed = FullscreenButton.IsChecked != true && WindowState == WindowState.Normal;
        // Use identical whole-device-pixel widths for the outline and HWND inset.
        // A fractional DIP inset can round the child's position and size
        // differently, leaving the right-hand outline partially covered.
        var dpi = dpiOverride ?? System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double x = Math.Max(1, Math.Round(1.5 * dpi.DpiScaleX, MidpointRounding.AwayFromZero)) / dpi.DpiScaleX;
        double y = Math.Max(1, Math.Round(1.5 * dpi.DpiScaleY, MidpointRounding.AwayFromZero)) / dpi.DpiScaleY;
        // The native frame already contributes one physical bottom pixel. Count
        // it within the outline/inset budget instead of adding another pixel.
        double bottom = AllowsTransparency ? y : Math.Max(0, y - WindowResizeFrame.NativeBottomFramePixels / dpi.DpiScaleY);
        var inset = framed ? new Thickness(x, y, x, bottom) : new Thickness(0);
        MediaElement.Margin = inset;
        WindowOutline.BorderThickness = inset;
        bool show = framed &&
            ((!Player.CanPlay && Player.Status != Status.Opening) || Player.Activity.Mode == ActivityMode.FullActive);
        if (show == outlineVisible)
            return;

        outlineVisible = show;
        WindowOutline.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 0.38 : 0,
            TimeSpan.FromMilliseconds(show ? 160 : 350)));
    }

    void Fullscreen_Changed(object sender, RoutedEventArgs e)
    {
        UpdateWindowActions();
        Dispatcher.BeginInvoke(new Action(ApplyVideoWindowRatio));
    }

    void UpdateWindowActions()
    {
        if (FullscreenButton == null) return;
        UpdateWindowOutlineVisibility();
    }

    void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        MediaElement.UIConfig.Appearance.Theme = FlyleafThemeManager.Resolve(theme) == FlyleafTheme.Dark
            ? FlyleafTheme.Light : FlyleafTheme.Dark;
    }

    void ThemeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string value } && Enum.TryParse(value, out FlyleafTheme selected))
        {
            MediaElement.UIConfig.Appearance.Theme = selected;
        }
    }

    void AccentMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string value } && Enum.TryParse(value, out FlyleafAccent selected))
        {
            MediaElement.UIConfig.Appearance.Accent = selected;
        }
    }

    void RememberNormalBounds(bool enteringFullscreen = false)
    {
        if (dockVideoBounds != null || MediaElement.IsFullScreen && !enteringFullscreen) return;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (!UsableSize(bounds) || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y)) return;
        // Retained docks can be hidden by a full-area panel. Fit them against
        // the normal bounds to recover the base window even in that state.
        double frameWidth = Math.Max(0, ActualWidth - MediaElement.ActualWidth);
        var (left, right) = MediaElement.GetDockInsets(bounds.Width - frameWidth);
        var baseBounds = WindowRatioSizing.WithoutDocks(bounds.Left, bounds.Width, left, right);
        lastNormalBounds = new Rect(baseBounds.Left, bounds.Top, Math.Max(MinWidth, baseBounds.Width), bounds.Height);
    }

    bool UsableSize(Rect bounds) => !bounds.IsEmpty && double.IsFinite(bounds.Width) && bounds.Width >= MinWidth
        && double.IsFinite(bounds.Height) && bounds.Height >= MinHeight;

    internal void CaptureWindowDefaults(bool closing = false)
    {
        if (!closing) RememberNormalBounds();
        if (lastNormalBounds.IsEmpty) return;
        var settings = App.Instance.Config.Window;
        settings.Width = lastNormalBounds.Width;
        settings.Height = lastNormalBounds.Height;
        settings.Left = lastNormalBounds.X;
        settings.Top = lastNormalBounds.Y;
    }

    internal void ResetWindowBounds()
    {
        WindowState = WindowState.Normal;
        var (left, right) = MediaElement.GetRequestedDockInsets();
        double baseWidth = Math.Max(MinWidth, App.Instance.Config.Window.Width);
        Width = baseWidth + left + right;
        Height = Math.Max(MinHeight, App.Instance.Config.Window.Height);
        var work = SystemParameters.WorkArea;
        SetStartupPosition(work.Left + Math.Max(0, (work.Width - baseWidth) / 2) - left,
            work.Top + Math.Max(0, (work.Height - Height) / 2));
    }

    void RestoreWindowPosition()
    {
        var settings = App.Instance.Config.Window;
        if (settings.Left is { } left && settings.Top is { } top) SetStartupPosition(left, top);
    }

    internal void OffsetFrom(MainWindow source)
    {
        if (!source.lastNormalBounds.IsEmpty)
            SetStartupPosition(source.lastNormalBounds.X + 32, source.lastNormalBounds.Y + 32);
    }

    void SetStartupPosition(double left, double top)
    {
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var title = Rect.Intersect(desktop, new Rect(left, top, Width, 32));
        if (title.IsEmpty || title.Width < 80 || title.Height < 16) return;
        Left = left;
        Top = top;
    }

    internal void ApplyProfilePresentation()
    {
        if (!ReferenceEquals(MediaElement.UIConfig, Profile.Presentation))
            MediaElement.SetCurrentValue(FlyleafME.UIConfigProperty, Profile.Presentation);
    }

    void ApplyTheme()
    {
        MediaElement.RefreshAppearance();
        UpdateThemeMenu();
    }

    void UpdateThemeMenu()
    {
        DarkThemeMenuItem.IsChecked = theme == FlyleafTheme.Dark;
        LightThemeMenuItem.IsChecked = theme == FlyleafTheme.Light;
        SystemThemeMenuItem.IsChecked = theme == FlyleafTheme.System;
        var currentTheme = FlyleafThemeManager.Resolve(theme);
        foreach (MenuItem item in AccentMenu.Items)
        {
            var value = Enum.Parse<FlyleafAccent>((string)item.Tag);
            item.IsChecked = accent == value;
            var swatch = ((StackPanel)item.Header).Children.OfType<Border>().Single();
            swatch.Background = new System.Windows.Media.SolidColorBrush(FlyleafThemeManager.GetAccentColor(currentTheme, value));
        }
    }

    void BuildAccentMenu()
    {
        foreach (var value in Enum.GetValues<FlyleafAccent>())
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock { Text = value.ToString(), VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = header, Tag = value.ToString(), IsCheckable = true };
            item.SetResourceReference(StyleProperty, "FlyleafME.ThemeMenuItemStyle"); item.Click += AccentMenu_Click;
            AccentMenu.Items.Add(item);
        }
    }

    void Window_DpiChanged(object sender, DpiChangedEventArgs e)
    {
        if (e.OriginalSource == this) UpdateWindowOutlineVisibility(e.NewDpi);
    }

    void Window_SourceInitialized(object? sender, EventArgs e)
    {
        windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        windowSource?.AddHook(WindowMessageHook);
        UpdateWindowOutlineVisibility();
        if (windowSource != null) resizeFrame = new WindowResizeFrame(this, MediaElement, windowSource);
        Dispatcher.BeginInvoke(new Action(ApplyVideoWindowRatio));
    }

    IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZING) resizedInteractively = true;
        if (msg == WM_EXITSIZEMOVE && resizedInteractively)
        {
            resizedInteractively = false;
            RememberWindowSize(hwnd);
        }

        if (msg == WM_SIZING && lParam != IntPtr.Zero &&
            ConstrainWindowRatio(hwnd, (int)wParam, lParam))
        {
            handled = true;
            return new IntPtr(1);
        }

        if (msg == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, 2);
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
            {
                var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                bounds.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
                bounds.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
                bounds.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
                bounds.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;

                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                bounds.MinTrackSize.X = Math.Max(bounds.MinTrackSize.X, (int)Math.Ceiling(MinWidth * dpi.DpiScaleX));
                bounds.MinTrackSize.Y = Math.Max(bounds.MinTrackSize.Y, (int)Math.Ceiling(MinHeight * dpi.DpiScaleY));
                Marshal.StructureToPtr(bounds, lParam, false);
                handled = true;
            }
        }

        if (theme == FlyleafTheme.System && (msg == WM_SETTINGCHANGE || msg == WM_THEMECHANGED))
            Dispatcher.BeginInvoke(new Action(ApplyTheme));

        return IntPtr.Zero;
    }

    void MediaElement_RatioChanged(object? sender, EventArgs e) => ApplyVideoWindowRatio();

    void KeepWindowRatio_Checked(object sender, RoutedEventArgs e)
    {
        // Let the two-way binding update AppConfig before applying the option.
        Dispatcher.BeginInvoke(new Action(ApplyVideoWindowRatio));
    }

    void RememberWindowSize(IntPtr hwnd)
    {
        if (WindowState != WindowState.Normal || MediaElement.IsFullScreen ||
            !GetClientRect(hwnd, out var client)) return;
        if (MediaElement.CurResizeRatio >= 1)
            preferredLandscapeWidth = MediaElement.Host.ActualWidth;
        else preferredPortraitHeight = MediaElement.Host.ActualHeight;
    }

    void ApplyVideoWindowRatio()
    {
        if (!App.Instance.Config.Window.KeepWindowRatioOnResize ||
            WindowState != WindowState.Normal || MediaElement.IsFullScreen || !Player.Video.IsOpened ||
            windowSource == null) return;
        double ratio = MediaElement.CurResizeRatio;
        if (!double.IsFinite(ratio) || ratio <= 0) return;
        IntPtr hwnd = windowSource.Handle;
        if (!GetWindowRect(hwnd, out var window) || !GetClientRect(hwnd, out var client)) return;
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitorInfo)) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        if (preferredLandscapeWidth == 0) preferredLandscapeWidth = MediaElement.Host.ActualWidth;
        if (preferredPortraitHeight == 0) preferredPortraitHeight = MediaElement.Host.ActualHeight;
        var (frameWidth, frameHeight) = GetRatioFrameSize(window, client, dpi);
        var work = monitorInfo.Work;
        var size = WindowRatioSizing.Constrain(
            (int)Math.Round(preferredLandscapeWidth * dpi.DpiScaleX),
            (int)Math.Round(preferredPortraitHeight * dpi.DpiScaleY), ratio >= 1 ? 2 : 6, ratio,
            Math.Max((int)Math.Ceiling(400 * dpi.DpiScaleX), (int)Math.Ceiling(MinWidth * dpi.DpiScaleX) - frameWidth),
            Math.Max(1, (int)Math.Ceiling(MinHeight * dpi.DpiScaleY) - frameHeight),
            Math.Max(1, (int)Math.Min(work.Right - work.Left, MaxWidth * dpi.DpiScaleX) - frameWidth),
            Math.Max(1, (int)Math.Min(work.Bottom - work.Top, MaxHeight * dpi.DpiScaleY) - frameHeight));
        int width = size.Width + frameWidth, height = size.Height + frameHeight;
        var position = WindowRatioSizing.Place(window.Left, window.Top, window.Right, window.Bottom,
            width, height, work.Left, work.Top, work.Right, work.Bottom);
        SetWindowPos(hwnd, IntPtr.Zero, position.Left, position.Top, width, height, 0x0004 | 0x0010);
    }

    bool ConstrainWindowRatio(IntPtr hwnd, int edge, IntPtr rectangle)
    {
        if (!App.Instance.Config.Window.KeepWindowRatioOnResize ||
            WindowState != WindowState.Normal || MediaElement.IsFullScreen ||
            !Player.Video.IsOpened || edge < 1 || edge > 8)
            return false;

        // FlyleafHost receives renderer ratio changes, including crop and rotation.
        double ratio = MediaElement.CurResizeRatio;
        if (!double.IsFinite(ratio) || ratio <= 0 ||
            !GetWindowRect(hwnd, out var window) || !GetClientRect(hwnd, out var client))
            return false;

        var bounds = Marshal.PtrToStructure<RectNative>(rectangle);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitorInfo))
            return false;

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var (frameWidth, frameHeight) = GetRatioFrameSize(window, client, dpi);
        bool moveLeft = edge is 1 or 4 or 7;
        bool moveTop = edge is 3 or 4 or 5;
        int maxWidth = moveLeft ? bounds.Right - monitorInfo.Work.Left : monitorInfo.Work.Right - bounds.Left;
        int maxHeight = moveTop ? bounds.Bottom - monitorInfo.Work.Top : monitorInfo.Work.Bottom - bounds.Top;
        maxWidth = Math.Min(maxWidth, (int)Math.Min(int.MaxValue, MaxWidth * dpi.DpiScaleX));
        maxHeight = Math.Min(maxHeight, (int)Math.Min(int.MaxValue, MaxHeight * dpi.DpiScaleY));
        var size = WindowRatioSizing.Constrain(
            bounds.Right - bounds.Left - frameWidth, bounds.Bottom - bounds.Top - frameHeight, edge, ratio,
            Math.Max((int)Math.Ceiling(400 * dpi.DpiScaleX), (int)Math.Ceiling(MinWidth * dpi.DpiScaleX) - frameWidth),
            Math.Max(1, (int)Math.Ceiling(MinHeight * dpi.DpiScaleY) - frameHeight),
            Math.Max(1, maxWidth - frameWidth), Math.Max(1, maxHeight - frameHeight));

        if (moveLeft) bounds.Left = bounds.Right - size.Width - frameWidth;
        else bounds.Right = bounds.Left + size.Width + frameWidth;
        if (moveTop) bounds.Top = bounds.Bottom - size.Height - frameHeight;
        else bounds.Bottom = bounds.Top + size.Height + frameHeight;
        Marshal.StructureToPtr(bounds, rectangle, false);
        return true;
    }

    (int Width, int Height) GetRatioFrameSize(RectNative window, RectNative client, DpiScale dpi)
    {
        var (leftDock, rightDock) = MediaElement.GetRequestedDockPixelInsets(dpi.DpiScaleX);
        int nonClientWidth = Math.Max(0, window.Right - window.Left - (client.Right - client.Left));
        int nonClientHeight = Math.Max(0, window.Bottom - window.Top - (client.Bottom - client.Top));
        int insetWidth = (int)Math.Round((WindowLayout.ActualWidth - MediaElement.ActualWidth) * dpi.DpiScaleX);
        int insetHeight = (int)Math.Round((MediaElement.HeaderHeight + WindowLayout.ActualHeight - MediaElement.ActualHeight) * dpi.DpiScaleY);
        return (nonClientWidth + insetWidth + leftDock + rightDock, nonClientHeight + insetHeight);
    }

    void MediaElement_ViewportLayoutChanging(object? sender, EventArgs e)
    {
        if (restoringPanelLayout || dockVideoBounds != null || windowSource == null || WindowState != WindowState.Normal || MediaElement.IsFullScreen) return;
        RememberNormalBounds();
        if (MediaElement.Host.SurfaceHandle != IntPtr.Zero && GetWindowRect(MediaElement.Host.SurfaceHandle, out var video) && video.Right > video.Left && video.Bottom > video.Top)
        { dockVideoBounds = video; dockWasFullArea = MediaElement.HasFullAreaPanel; }
    }

    void MediaElement_ViewportLayoutChanged(object? sender, EventArgs e)
    {
        var video = dockVideoBounds; dockVideoBounds = null;
        UpdateHeaderVisibility();
        if (video is { } saved && windowSource != null && WindowState == WindowState.Normal && !MediaElement.IsFullScreen &&
            !dockWasFullArea && !MediaElement.HasFullAreaPanel && GetWindowRect(windowSource.Handle, out var window))
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var origin = MediaElement.PointToScreen(new Point());
            var (leftDock, rightDock) = MediaElement.GetRequestedDockPixelInsets(dpi.DpiScaleX);
            int leftInset = (int)Math.Round(origin.X - window.Left) + leftDock;
            int topInset = (int)Math.Round(origin.Y - window.Top + MediaElement.HeaderHeight * dpi.DpiScaleY);
            int rightInset = (int)Math.Round(window.Right - origin.X - MediaElement.ActualWidth * dpi.DpiScaleX) + rightDock;
            int bottomInset = (int)Math.Round(window.Bottom - origin.Y - MediaElement.ActualHeight * dpi.DpiScaleY);
            var bounds = WindowRatioSizing.AroundVideo(saved.Left, saved.Top, saved.Right - saved.Left, saved.Bottom - saved.Top,
                leftInset, topInset, rightInset, bottomInset);
            if (bounds.Left != window.Left || bounds.Top != window.Top || bounds.Width != window.Right - window.Left || bounds.Height != window.Bottom - window.Top)
                SetWindowPos(windowSource.Handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0004 | 0x0010);
        }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsLoaded && windowSource is { IsDisposed: false }) RememberWindowSize(windowSource.Handle);
        }));
    }

    void MediaElement_FullScreenChanged(object? sender, EventArgs e)
    {
        if (applyingFullScreen || windowSource == null) return;
        applyingFullScreen = true;
        try
        {
            if (MediaElement.IsFullScreen)
            {
                RememberNormalBounds(enteringFullscreen: true);
                GetWindowRect(windowSource.Handle, out normalWindowBounds);
                previousWindowState = WindowState;
                previousResizeMode = ResizeMode;
                var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(MonitorFromWindow(windowSource.Handle, 2), ref monitorInfo))
                {
                    MediaElement.SetCurrentValue(FlyleafME.IsFullScreenProperty, false);
                    return;
                }
                WindowState = WindowState.Normal;
                ResizeMode = ResizeMode.NoResize;
                var bounds = monitorInfo.Monitor;
                SetWindowPos(windowSource.Handle, IntPtr.Zero, bounds.Left, bounds.Top,
                    bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0004 | 0x0010);
            }
            else
            {
                ResizeMode = previousResizeMode;
                SetWindowPos(windowSource.Handle, IntPtr.Zero, normalWindowBounds.Left, normalWindowBounds.Top,
                    normalWindowBounds.Right - normalWindowBounds.Left, normalWindowBounds.Bottom - normalWindowBounds.Top, 0x0004 | 0x0010);
                WindowState = previousWindowState;
            }
            UpdateWindowActions();
            Dispatcher.BeginInvoke(new Action(ApplyVideoWindowRatio));
        }
        finally { applyingFullScreen = false; }
    }

    async void MediaElement_OpenInputRequested(object? sender, FlyleafOpenInputEventArgs e)
    {
        if (Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(e.Input))) return;
        e.Handled = true;
        await RunPlaylistOperation(() => OpenQueueInputs([e.Input]));
    }

    async void MediaElement_OpenFilesRequested(object? sender, IReadOnlyList<string> files)
    {
        var inputs = GetQueueMediaFiles(files);
        if (inputs.Length > 0) await RunPlaylistOperation(() => OpenQueueInputs(inputs));
    }

    static string[] GetQueueMediaFiles(IReadOnlyList<string> files) =>
        files.Where(file => !Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(file))).ToArray();

    async void MediaElement_Drop(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        if (e.Data.GetDataPresent(DataFormats.Text) && e.Data.GetData(DataFormats.Text, false) is string text && !string.IsNullOrWhiteSpace(text))
        {
            MediaElement.OpenInput(text.Trim()); e.Handled = true; return;
        }
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop, false) is not string[] files) return;
        var inputs = GetQueueMediaFiles(files);
        if (inputs.Length == 0) return;
        e.Handled = true;
        await RunPlaylistOperation(() => OpenQueueInputs(inputs));
    }
    void MediaElement_MenuBuilding(object? sender, FlyleafMenuBuildingEventArgs e)
    {
        var window = new MenuItem { Header = "Window" };
        window.SetResourceReference(StyleProperty, "FlyleafME.ThemeMenuItemStyle");
        void Add(string caption, Action action, bool selected = false, string? shortcut = null)
        {
            var item = new MenuItem { Header = caption, IsChecked = selected };
            if (shortcut != null && Player.Config.Playback.KeyBindings.Keys.FirstOrDefault(binding =>
                binding.Key != Key.None && GetApplicationActionId(binding) == shortcut) is { } binding)
                item.InputGestureText = FlyleafShortcutGesture.From(binding).ToString();
            item.Click += (_, _) => action(); window.Items.Add(item);
        }
        void Separator()
        {
            var separator = new Separator();
            separator.SetResourceReference(StyleProperty, "FlyleafME.ThemeMenuSeparatorStyle");
            window.Items.Add(separator);
        }

        Add("New window", OpenNewPlayerWindow, shortcut: "app.newWindow");
        Separator();
        Add("Always on top", () => Topmost = !Topmost, Topmost);
        Add("Keep window aspect ratio", () =>
        {
            App.Instance.Config.Window.KeepWindowRatioOnResize = !App.Instance.Config.Window.KeepWindowRatioOnResize;
            if (App.Instance.Config.Window.KeepWindowRatioOnResize) ApplyVideoWindowRatio();
        }, App.Instance.Config.Window.KeepWindowRatioOnResize);
        Add("Save current layout", SaveCurrentPanelLayout);
        Separator();
        Add("Close window", ClosePlayerWindow, shortcut: "app.closeWindow");
        e.Menu.Items.Add(window);
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    void ToggleMaximize()
    {
        if (MediaElement.IsFullScreen)
        {
            MediaElement.IsFullScreen = false;
            WindowState = WindowState.Maximized;
            return;
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton == null)
            return;

        Dispatcher.BeginInvoke(new Action(ApplyVideoWindowRatio));
        bool maximized = WindowState == WindowState.Maximized;
        UpdateWindowActions();
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    internal void ActivateFromTray()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        MediaElement.FocusPlayback();
    }

    void QueueSearch_Click(object sender, RoutedEventArgs e) => MediaElement.ShowQueueSearch();
    void QueueSearch_ToolTipOpening(object sender, ToolTipEventArgs e) => QueueSearchButton.ToolTip =
        MediaElement.QueueSearchShortcut is { } shortcut ? $"Search queue ({shortcut})" : "Search queue";

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void PlayerWindow_Activated(object? sender, EventArgs e) => App.Instance.ActivatePlayerWindow(this);

    internal void CaptureProfile()
    {
        if (Player.IsDisposed) return;
        try { App.Instance.Profiles.Capture(Profile); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Text.Json.JsonException or NotSupportedException)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
            if (IsLoaded) MediaElement.ShowOSD("Could not retain profile changes.");
        }
    }

    void Window_Closed(object? sender, EventArgs e)
    {
        CaptureProfile();
        CaptureWindowDefaults(closing: true);
        if (closingPanels != null && App.Instance.Config.Window.StartupPanelLayout == StartupPanelLayoutMode.LastUsed)
            SavePanelLayout(closingPanels, saveOpenState: true);
        App.Instance.PlayerWindowClosed(this);
        Activated -= PlayerWindow_Activated;
        if (focusSurface != null) focusSurface.Activated -= PlayerWindow_Activated;
        if (focusOverlay != null) focusOverlay.Activated -= PlayerWindow_Activated;
        DisposePlaylistCommands();
        PlaybackBar.MediaElement = null;
        MediaElement.MenuBuilding -= MediaElement_MenuBuilding;
        MediaElement.OpenInputRequested -= MediaElement_OpenInputRequested;
        MediaElement.OpenFilesRequested -= MediaElement_OpenFilesRequested;
        MediaElement.Host.OnSurfaceDrop -= MediaElement_Drop;
        MediaElement.Host.OnOverlayDrop -= MediaElement_Drop;
        resizeFrame?.Dispose();
        windowSource?.RemoveHook(WindowMessageHook);
        SourceInitialized -= Window_SourceInitialized;
        ContentRendered -= Window_FirstContentRendered;
        Player.OpeningVideoStream -= PreferTransparentVideoDecoder;
        Player.PropertyChanged -= Player_PropertyChanged;
        Player.Video.PropertyChanged -= Video_PropertyChanged;
        Player.Activity.PropertyChanged -= Activity_PropertyChanged;
        MediaElement.UIConfigChanged -= MediaElement_UIConfigChanged;
        MediaElement.RatioChanged -= MediaElement_RatioChanged;
        MediaElement.ViewportLayoutChanged -= MediaElement_ViewportLayoutChanged;
        MediaElement.ViewportLayoutChanging -= MediaElement_ViewportLayoutChanging;
        MediaElement.FullScreenChanged -= MediaElement_FullScreenChanged;
        if (ThemeButton.ContextMenu is ContextMenu themeMenu)
        {
            themeMenu.Opened -= ThemeMenu_Opened;
            themeMenu.Closed -= ThemeMenu_Closed;
        }
        themePopupActivity?.Dispose();
        MediaElement.Player = null;
        MediaElement.Dispose();
        Player.Dispose();
    }
}
