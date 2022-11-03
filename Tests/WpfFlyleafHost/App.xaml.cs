using FlyleafLib;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WpfFlyleafHost
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
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
