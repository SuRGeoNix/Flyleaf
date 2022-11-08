using System;
using System.Windows;

using FlyleafLib;

namespace WpfFlyleafHost
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Engine.Start(new EngineConfig()
            {
                FFmpegPath = ":FFmpeg",
                LogOutput = ":debug",
                LogLevel = LogLevel.Debug
            });
        }
    }
}
