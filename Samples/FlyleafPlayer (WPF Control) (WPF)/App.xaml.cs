using System;
using System.IO;
using System.Threading;
using System.Windows;

using FlyleafLib;

namespace FlyleafPlayer
{
    public partial class App : Application
    {
        public static string CmdUrl { get; set; } = null;
        public static readonly string EnginePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.Engine.json");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length == 1)
                CmdUrl = e.Args[0];

            // Ensures that we have enough worker threads to avoid the UI from freezing or not updating on time
            ThreadPool.GetMinThreads(out int workers, out int ports);
            ThreadPool.SetMinThreads(workers + 6, ports + 6);

            EngineConfig engineConfig;

            // Engine's Config
            #if RELEASE
            if (File.Exists(EnginePath))
                try { engineConfig = EngineConfig.Load(EnginePath); } catch { engineConfig = DefaultEngineConfig(); }
            else
                engineConfig = DefaultEngineConfig();
            #else
            engineConfig = DefaultEngineConfig();
            #endif

            Engine.StartAsync(engineConfig);
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
}
