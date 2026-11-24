using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FlyleafLib;
using Flyleaf.Controls.WPF;

namespace Flyleaf;

public partial class App : Application
{
    public static App Instance => (App)Current;
    public AppConfig Config => store.Application;
    static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flyleaf");
    public string SettingsDirectory => AppDataDirectory;
    ConfigStore store = null!;
    MainWindow? activePlayerWindow;
    SingleInstance? instance;
    TrayIcon? tray;
    bool initialized;
    TaskCompletionSource? trayCloseCompletion;
    volatile bool exiting;
    internal PlayerProfiles Profiles => store.Profiles;
    internal EngineConfig FactoryEngine { get; private set; } = null!;
    internal bool UseTransparentWindows { get; private set; }

    internal MainWindow OpenWindow(MainWindow? source = null)
    {
        source?.CaptureProfile();
        source?.CaptureWindowDefaults();
        var settings = Config.Window;
        var panelLayout = (settings.StartupPanelLayout == StartupPanelLayoutMode.LastUsed ? source?.CapturePanelLayout() : null)
            ?? settings.GetStartupPanelLayout();
        var window = new MainWindow(source?.Profile.Id, panelLayout);
        if (source != null) window.OffsetFrom(source);
        window.Show();
        return window;
    }

    internal void ActivatePlayerWindow(MainWindow window)
    {
        if (activePlayerWindow == window) return;
        activePlayerWindow?.CaptureProfile();
        activePlayerWindow?.CapturePanelLayout();
        activePlayerWindow = window;
        RefreshTrayAppearance(window);
    }

    internal void PlayerWindowClosed(MainWindow window)
    {
        if (activePlayerWindow == window) activePlayerWindow = null;
    }

    internal void RefreshTrayAppearance(MainWindow window)
    {
        if (window == activePlayerWindow) tray?.ApplyAppearance(window.Profile.Presentation.Appearance);
    }

    void StartupOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartupOptions.KeepInTray)) UpdateTray();
    }

    void UpdateTray()
    {
        if (Config.Startup.KeepInTray)
        {
            tray ??= new TrayIcon(
                () => Dispatcher.BeginInvoke(new Action(() => OpenLaunch(new([], false, false)))),
                () => Dispatcher.BeginInvoke(new Action(() => CloseTrayWindows(exit: false))),
                () => Dispatcher.BeginInvoke(new Action(() => CloseTrayWindows(exit: true))));
            tray.ApplyAppearance(activePlayerWindow?.Profile.Presentation.Appearance ?? Profiles.SelectedPresentation.Appearance);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        else
        {
            tray?.Dispose();
            tray = null;
            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
    }

    async void CloseTrayWindows(bool exit)
    {
        if (trayCloseCompletion != null) return;
        var completion = trayCloseCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (exit) exiting = true;
        bool shutdown = false;
        try
        {
            foreach (var window in Windows.OfType<MainWindow>().ToArray())
                if (!await window.CloseFromTrayAsync()) return;
            if (exit)
            {
                shutdown = true;
                Shutdown();
            }
        }
        finally
        {
            if (exit && !shutdown) exiting = false;
            trayCloseCompletion = null;
            completion.SetResult();
        }
    }

    bool QueueLaunch(LaunchRequest request)
    {
        if (exiting || Dispatcher.HasShutdownStarted) return false;
        Dispatcher.BeginInvoke(new Action(() => OpenLaunch(request)));
        return true;
    }

    async void OpenLaunch(LaunchRequest request)
    {
        while (trayCloseCompletion != null) await trayCloseCompletion.Task;
        if (exiting) return;
        if (request.StartMinimized && request.Inputs.Length == 0 && tray != null) return;
        var available = Windows.OfType<MainWindow>().ToArray();
        var previous = activePlayerWindow ?? available.LastOrDefault();
        var target = request.NewWindow ? null : request.Inputs.Length > 0
            ? available.FirstOrDefault(window => !window.Player.IsPlaying) ?? previous
            : previous;
        if (!request.NewWindow && request.Inputs is [var input] && Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(input)))
            target = previous is { Player.Video.IsOpened: true } ? previous
                : available.FirstOrDefault(window => window.Player.Video.IsOpened) ?? previous;
        target ??= OpenWindow(request.NewWindow ? previous : null);
        if (request.StartMinimized && request.Inputs.Length == 0)
            target.WindowState = WindowState.Minimized;
        else
            target.ActivateFromTray();
        if (request.Inputs.Length > 0) await target.OpenLaunchInputsAsync(request.Inputs);
    }

    static Config CreateDefaultPlayerConfig()
    {
        var config = new Config();
        config.Demuxer.FormatOpt            = DefaultFormatOpt();
        config.Demuxer.AudioFormatOpt       = DefaultFormatOpt();
        config.Demuxer.SubtitlesFormatOpt   = DefaultFormatOpt();
        config.Playback.KeyBindings         = FlyleafME.CreateDefaultKeybindings();
        return config;
    }

    static Dictionary<string, string> DefaultFormatOpt()
    {
        var opts = FlyleafLib.Config.DemuxerConfig.DefaultFormatOpt();

        opts["probesize"]           = $"{50L * 1024 * 1024}";
        opts["analyzeduration"]     = $"{TimeSpan.FromSeconds(10).TotalMicroseconds}";
        opts["extension_picky"]     = "0";
        opts["rtsp_transport"]      = "tcp";

        // HLS
        opts["http_persistent"]     = "0"; // Temp fix for FFmpeg bug crash https://code.ffmpeg.org/FFmpeg/FFmpeg/issues/23798

        return opts;
    }

    internal static EngineConfig CreateDefaultEngineConfig() => new()
    {
        FFmpegHLSLiveSeek = true,
    };

    internal static string NormalizeEngineLogOutput(string? value)
    {
        var output = value?.Trim() ?? "";
        if (output.Length == 0 || output == ":custom") return output;
        #if DEBUG
        if (output is ":debug" or ":console") return output;
        #endif
        if (output.StartsWith(':'))
            throw new ArgumentException("Enter a log file path. :console and :debug are available in DEBUG builds.");

        var path = Path.GetFullPath(output);
        if (Path.EndsInDirectorySeparator(path) || Directory.Exists(path))
            throw new ArgumentException("Specify a file name for the log.");
        return path;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        FactoryEngine = CreateDefaultEngineConfig();
        store = new(SettingsDirectory);
        store.LoadBeforeEngine(CreateDefaultEngineConfig);
        UseTransparentWindows = Config.Startup.AllowTransparency;

        LaunchRequest launch;
        try
        {
            launch = LaunchRequest.Parse(e.Args, Environment.CurrentDirectory,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            MessageBox.Show(ex.Message, "Open media", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        #if DEBUG
        store.Application.Startup.SingleInstance = false;
        store.Application.Startup.KeepInTray = false;

        store.Engine.FFmpegPath     = ":FFmpeg";
        store.Engine.FFmpegLogLevel = FFmpeg.LogLevel.Warning;
        store.Engine.LogLevel       = LogLevel.Debug;
        store.Engine.LogOutput      = ":debug";
        #endif

        if (Config.Startup.SingleInstance)
        {
            instance = SingleInstance.ForCurrentSession();
            if (!instance.TryAcquire())
            {
                if (instance.ForwardAsync(launch).GetAwaiter().GetResult()) { Shutdown(); return; }
                if (!instance.TryAcquire())
                {
                    MessageBox.Show("The running Flyleaf instance did not respond.", "Flyleaf", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Shutdown(1);
                    return;
                }
            }
            instance.Listen(QueueLaunch);
        }

        // Launch inputs have already been resolved against the original working directory.
        // Resolve application-relative resources from the executable directory instead.
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        try { store.Engine.LogOutput = NormalizeEngineLogOutput(store.Engine.LogOutput); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            store.Engine.LogOutput = FactoryEngine.LogOutput;
        }

        try { Engine.Start(store.Engine); }
        catch (Exception ex)
        {
            MessageBox.Show($"Flyleaf could not initialize its engine.\n\n{ex.Message}\n\nApplication directory: {AppContext.BaseDirectory}",
                "Flyleaf startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        store.LoadProfiles(CreateDefaultPlayerConfig());
        initialized = true;
        Config.Startup.PropertyChanged += StartupOptionsChanged;
        UpdateTray();
        base.OnStartup(e);
        OpenLaunch(launch);
        if (store.LoadError != null)
            MessageBox.Show(MainWindow, $"Flyleaf is using defaults for settings that could not be loaded.\n\n{store.LoadError}",
                "Settings load failed", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        try
        {
            if (initialized)
            {
                Config.Startup.PropertyChanged -= StartupOptionsChanged;
                tray?.Dispose();
                #if !DEBUG
                try { store.Save(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
                {
                    MessageBox.Show(ex.Message, "Settings save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                #endif
            }
            // Engine's Exit handler changes runtime refresh settings as it disposes players.
            base.OnExit(e);
        }
        finally { instance?.Dispose(); }
    }
}
