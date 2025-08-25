using System;
using System.IO;
using System.Windows;

using FlyleafLib;

namespace FlyleafPlayer;

public partial class App : Application
{
    public static AppConfig AppConfig       { get; set; } = AppConfig.Load();
    public static string    CmdUrl          { get; set; } = null;
    public static bool      StartMinimized  { get; set; } = false;
    public static string    EnginePath      { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.Engine.json");

    static System.Windows.Forms.NotifyIcon TrayIcon;

    void App_Startup(object sender, StartupEventArgs e)
    {
        if (AppConfig.General.SingleInstance && !ApplicationActivator.LaunchOrReturn(SingleInstanceExecute, e.Args))
        {
            Shutdown();
            return;
        }

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "/StartMinimized")
                StartMinimized = true;
            else
                CmdUrl ??= e.Args[i];
        }

        EngineConfig engineConfig;
        #if RELEASE
        if (File.Exists(EnginePath))
            try { engineConfig = EngineConfig.Load(EnginePath); } catch { engineConfig = DefaultEngineConfig(); }
        else
            engineConfig = DefaultEngineConfig();
        #else
        engineConfig = DefaultEngineConfig();
        #endif
        Engine.StartAsync(engineConfig);

        if (AppConfig.General.SingleInstance)
        {
            TrayIcon = new()
            {
                Text            = "Flyleaf",
                Icon            = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath),
                Visible         = true,
                ContextMenuStrip= new()
            };

            TrayIcon.ContextMenuStrip.Items.Add("Close All", null, (o, e) => { foreach (var win in Current.Windows) if (win is MainWindow main) main.Close(); });
            TrayIcon.ContextMenuStrip.Items.Add("Exit", null, (o, e) => Current.Shutdown());
            TrayIcon.DoubleClick += TrayIcon_DoubleClick;

            if (!StartMinimized)
                new MainWindow().Show();
        }
        else
            new MainWindow().Show();
    }

    public void SingleInstanceExecute(Payload payload)
    {
        if (payload.CommandLineArguments.Length == 0)
        {
            TrayIcon_DoubleClick(null, EventArgs.Empty);
            return;
        }

        MainWindow main = null;

        if (!payload.OpenInNewWindow)
        {
            foreach (var win in Current.Windows)
                if (win is MainWindow main2 && main2.FlyleafME.Player.Status != FlyleafLib.MediaPlayer.Status.Playing)
                { main = main2; break; }

            if (main == null)
                foreach (var win in Current.Windows)
                    if (win is MainWindow main2)
                    { main = main2; break; }
        }
        
        if (main == null)
        {
            main = new();
            main.Show();
            main.FlyleafME.Player?.OpenAsync(payload.CommandLineArguments[0]);
        }
        else
            main.FlyleafME.Player?.OpenAsync(payload.CommandLineArguments[0]);

        if (main.FlyleafME.IsMinimized)
            main.FlyleafME.IsMinimized = false;

        main.FlyleafME.Surface.Show();
        main.FlyleafME.Surface.ShowInTaskbar = true;

        if (!main.FlyleafME.Surface.Topmost)
        {
            main.FlyleafME.Surface.Topmost = true;
            main.FlyleafME.Surface.Topmost = false;
            main.FlyleafME.Surface.Show();
        }

        main.FlyleafME.Surface.Activate();
    }

    void TrayIcon_DoubleClick(object sender, EventArgs e)
    {
        bool found = false;
        foreach (Window win in Current.Windows)
            if (win is MainWindow main)
            {
                found = true;

                if (main.FlyleafME.IsMinimized)
                    main.FlyleafME.IsMinimized = false;

                main.FlyleafME.Surface.Show();
                main.FlyleafME.Surface.ShowInTaskbar = true;
                main.FlyleafME.Surface.Activate();
            }

        if (!found)
            new MainWindow().Show();
    }

    static EngineConfig DefaultEngineConfig()
    =>  new()
    {
        PluginsPath         = ":Plugins",
        FFmpegPath          = ":FFmpeg",
        FFmpegHLSLiveSeek   = true,
        UIRefresh           = true,
        FFmpegLoadProfile   = Flyleaf.FFmpeg.LoadProfile.All,

        #if RELEASE
        LogOutput           = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.FirstRun.log"),
        LogLevel            = LogLevel.Debug,
        #else
        LogOutput           = ":debug",
        LogLevel            = LogLevel.Debug,
        FFmpegLogLevel      = Flyleaf.FFmpeg.LogLevel.Warn,
        #endif
    };
}
