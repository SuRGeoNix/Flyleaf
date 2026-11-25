using System.Windows;
using System.Windows.Controls;
using FlyleafLib;
using Flyleaf.Controls.WPF;
using FlyleafLib.MediaPlayer;
using Microsoft.Win32;

namespace Flyleaf;

/// <summary>Sample-owned settings contribution; no application types enter the reusable control.</summary>
public sealed class ApplicationSettings : FlyleafSettingsEditor
{
    protected override NotifyPropertyChanged SelectConfig(Player target) => App.Instance.Config;

    protected override void Build()
    {
        BeginPage("Application");
        if (Context?.UIConfig is not { } ui) return;
        Observe(ui.Appearance); Observe(ui.PlaybackBar); Observe(ui.Queue); Observe(App.Instance.Config.Window);
        var appearance = Group("Appearance", () =>
        {
            var defaults = new FlyleafAppearanceOptions();
            ui.Appearance.Theme = defaults.Theme;
            ui.Appearance.Accent = defaults.Accent;
        });
        Choice(appearance, "Theme", () => ui.Appearance.Theme, v => ui.Appearance.Theme = v,
            Enum.GetValues<FlyleafTheme>().Select(v => (v, v.ToString())).ToArray());
        Choice(appearance, "Accent", () => ui.Appearance.Accent, v => ui.Appearance.Accent = v,
            Enum.GetValues<FlyleafAccent>().Select(v => (v, v.ToString())).ToArray());
        var playbackBar = Group("Playback bar", () =>
        {
            var defaults = new FlyleafPlaybackBarOptions();
            ui.PlaybackBar.Visibility = FlyleafVisibilityPolicy.Auto;
            ui.PlaybackBar.Design = defaults.Design;
            ui.PlaybackBar.Mode = defaults.Mode;
            ui.PlaybackBar.Floating = defaults.Floating;
            ui.PlaybackBar.ClassicFloating = defaults.ClassicFloating;
            ui.PlaybackBar.DynamicPinned = defaults.DynamicPinned;
            ui.PlaybackBar.SeekWhileDragging = true;
            ui.PlaybackBar.AutoHideDelayMilliseconds = 2800;
        });
        Choice(playbackBar, "Design", () => ui.PlaybackBar.Design, v => ui.PlaybackBar.Design = v,
            Enum.GetValues<FlyleafPlaybackBarDesign>().Select(v => (v, v.ToString())).ToArray());
        bool IsModern() => ui.PlaybackBar.Design == FlyleafPlaybackBarDesign.Modern;
        var modernMode = new StackPanel();
        playbackBar.Children.Add(modernMode);
        Choice(modernMode, "Modern mode", () => ui.PlaybackBar.Mode, v => ui.PlaybackBar.Mode = v,
            Enum.GetValues<FlyleafPlaybackBarMode>().Select(v => (v, v.ToString())).ToArray());
        RefreshWith(() => modernMode.Visibility = IsModern() ? Visibility.Visible : Visibility.Collapsed);

        Toggle(playbackBar, "Floating bar", "Inset the playback bar and round its corners.",
            () => IsModern() ? ui.PlaybackBar.Floating : ui.PlaybackBar.ClassicFloating, v =>
            {
                if (IsModern()) ui.PlaybackBar.Floating = v;
                else ui.PlaybackBar.ClassicFloating = v;
            });
        Choice(playbackBar, "Visibility", () => ui.PlaybackBar.Visibility, v => ui.PlaybackBar.Visibility = v,
            Enum.GetValues<FlyleafVisibilityPolicy>().Select(v => (v, v.ToString())).ToArray());
        Toggle(playbackBar, "Seek while dragging", "Seek continuously while dragging the timeline; turn off to seek once on release.", () => ui.PlaybackBar.SeekWhileDragging, v => ui.PlaybackBar.SeekWhileDragging = v);
        Number(playbackBar, "Auto-hide delay", "seconds · 0 = never", () => ui.PlaybackBar.AutoHideDelayMilliseconds / 1000.0,
            value => ui.PlaybackBar.AutoHideDelayMilliseconds = (int)Math.Round(value * 1000), 0, 600,
            value => value > 0 && value < 0.001 ? "Use 0 or at least 0.001 seconds." : null);
        var app = App.Instance.Config;
        var queue = Group("Queue", () => app.ConfirmUnsavedQueueChanges = true);
        Toggle(queue, "Confirm unsaved queue changes", "Ask before closing or replacing an edited queue with multiple items or a modified saved playlist. Applies to all player windows.",
            () => app.ConfirmUnsavedQueueChanges, value => app.ConfirmUnsavedQueueChanges = value);
        var downloads = Group("Downloads", () => app.DownloadFolder = FlyleafDownloadManager.DefaultDownloadFolder);
        Folder(downloads, "Download folder", () => app.DownloadFolder, value => app.DownloadFolder = value);
        var slideShow = Group("Slide show", () => ui.Queue.SlideShowIntervalMilliseconds = 3000);
        Number(slideShow, "Image display interval", "seconds", () => ui.Queue.SlideShowIntervalMilliseconds / 1000.0,
            value => ui.Queue.SlideShowIntervalMilliseconds = (int)Math.Round(value * 1000), 0.1, 3600);
        var slideHelp = new System.Windows.Controls.TextBlock
        {
            Text = "With Auto-play next item enabled, zero-time images stay visible for this interval. Animations and videos finish playing before advancing.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
        slideHelp.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        slideShow.Children.Add(slideHelp);
        var osd = new FlyleafOSDOptionsEditor { ShowResetButton = false };
        var messages = Group("On-screen display", osd.Reset);
        osd.SetBinding(FlyleafOSDOptionsEditor.OptionsProperty, new System.Windows.Data.Binding("Context.UIConfig.OSD") { Source = this });
        osd.PreviewRequested += (_, _) => Context?.MediaElement?.ShowOSD("On-screen message preview · Volume 50%");
        messages.Children.Add(osd);
        var window = Group("Window", () =>
        {
            var current = App.Instance.Config.Window;
            var defaults = new WindowOptions();
            current.KeepWindowRatioOnResize = defaults.KeepWindowRatioOnResize;
            current.Width = defaults.Width;
            current.Height = defaults.Height;
            current.Left = current.Top = null;
            Context?.MediaElement?.SetCurrentValue(FlyleafME.IsFullScreenProperty, false);
            if (Context?.MediaElement != null && System.Windows.Window.GetWindow(Context.MediaElement) is MainWindow main)
                main.ResetWindowBounds();
        });
        Toggle(window, "Keep video ratio when resizing", "Constrains interactive resizing of a normal player window while video is open.",
            () => App.Instance.Config.Window.KeepWindowRatioOnResize, v => App.Instance.Config.Window.KeepWindowRatioOnResize = v);
        MainWindow? PlayerWindow() => Context?.MediaElement is { } view ? Window.GetWindow(view) as MainWindow : null;
        var layout = Group("Panel layout", () => PlayerWindow()?.ResetPanelLayout());
        var windowOptions = App.Instance.Config.Window;
        var saveLayout = new Button { Content = "Save current", Margin = new Thickness(8, 0, 0, 0) };
        saveLayout.SetResourceReference(StyleProperty, "FlyleafME.ButtonStyle");
        saveLayout.Click += (_, _) => Run(() => PlayerWindow()?.SaveCurrentPanelLayout());
        Choice(layout, "Startup panel layout", () => windowOptions.StartupPanelLayout, value => windowOptions.StartupPanelLayout = value,
            [(StartupPanelLayoutMode.Default, "Default layout"), (StartupPanelLayoutMode.LastUsed, "Last used layout"),
             (StartupPanelLayoutMode.Saved, "Saved layout")], saveLayout);
        var startupOptions = App.Instance.Config.Startup;
        Observe(startupOptions);
        var startup = Group("Startup", () =>
        {
            var defaults = new StartupOptions();
            startupOptions.SingleInstance = defaults.SingleInstance;
            startupOptions.KeepInTray = defaults.KeepInTray;
            startupOptions.AllowTransparency = defaults.AllowTransparency;
        });
        Toggle(startup, "Single instance", "Send later launches to the running app. Takes effect after restarting Flyleaf.",
            () => startupOptions.SingleInstance, value => startupOptions.SingleInstance = value);
        Toggle(startup, "Keep Flyleaf in the tray", "Keep the app running after the last player window closes.",
            () => startupOptions.KeepInTray, value => startupOptions.KeepInTray = value);
        Toggle(startup, "Allow transparency", "Show the desktop through transparent video. Restart Flyleaf to apply. For transparent backgrounds and letterboxing, set Video background to #00000000.",
            () => startupOptions.AllowTransparency, value => startupOptions.AllowTransparency = value);
        BuildEngineSettings();
        Refresh();
    }
    void BuildEngineSettings()
    {
        var engine = Engine.Config;
        var group = Group("Engine · all windows", ResetEngine);
        Toggle(group, "Disable audio", "Takes effect after restarting Flyleaf.", () => engine.DisableAudio, value => engine.DisableAudio = value);
        var browse = new System.Windows.Controls.Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        browse.SetResourceReference(StyleProperty, "FlyleafME.ButtonStyle");
        void SetOutput(string value)
        {
            var output = App.NormalizeEngineLogOutput(value);
            if (engine.LogOutput != output) engine.LogOutput = output;
        }
        var outputField = TextEntry(group, "Log output", () => engine.LogOutput ?? "", SetOutput, browseButton: browse);
        outputField.ToolTip = "Enter a log file path. Leave empty to disable output.";
        #if DEBUG
        outputField.ToolTip = "Enter a log file path, :console, or :debug. Leave empty to disable output.";
        #endif
        browse.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Title = "Select log file", DefaultExt = ".log",
                Filter = "Log files|*.log|All files|*.*", FileName = "Flyleaf.log" };
            if (engine.LogOutput is { Length: > 0 } output && !output.StartsWith(':'))
                dialog.FileName = output;
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Run(() => SetOutput(dialog.FileName));
        };
        Choice(group, "Log level", () => engine.LogLevel, value => engine.LogLevel = value,
            Enum.GetValues<LogLevel>().Select(value => (value, value.ToString())).ToArray());
        Choice(group, "FFmpeg log level", () => engine.FFmpegLogLevel, value => engine.FFmpegLogLevel = value,
            Enum.GetValues<Flyleaf.FFmpeg.LogLevel>().Select(value => (value, value.ToString())).ToArray());
    }
    static void ResetEngine()
    {
        var engine = Engine.Config;
        var defaults = App.Instance.FactoryEngine;
        engine.DisableAudio = defaults.DisableAudio;
        engine.FFmpegPath = defaults.FFmpegPath;
        engine.FFmpegLoadProfile = defaults.FFmpegLoadProfile;
        engine.FFmpegHLSLiveSeek = defaults.FFmpegHLSLiveSeek;
        engine.FFmpegLogLevel = defaults.FFmpegLogLevel;
        engine.PluginsPath = defaults.PluginsPath;
        engine.LogLevel = defaults.LogLevel;
        engine.LogAppend = defaults.LogAppend;
        engine.LogCachedLines = defaults.LogCachedLines;
        engine.LogRollMaxFileSize = defaults.LogRollMaxFileSize;
        engine.LogRollMaxFiles = defaults.LogRollMaxFiles;
        engine.LogDateTimeFormat = defaults.LogDateTimeFormat;
        if (engine.LogOutput != defaults.LogOutput) engine.LogOutput = defaults.LogOutput;
        engine.UIRefreshInterval = defaults.UIRefreshInterval;
        engine.UIRefresh = defaults.UIRefresh;
        engine.KeepDisplayActive = defaults.KeepDisplayActive;
    }
}
