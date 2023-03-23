using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer__WinUI_;

public sealed partial class MainWindow : Window
{
    public Player Player { get; set; }

    public MainWindow()
    {
        // Initializes Engine (Specifies FFmpeg libraries path which is required)
        Engine.Start(new EngineConfig()
        {
            #if DEBUG
            LogOutput       = ":debug",
            LogLevel        = LogLevel.Debug,
            FFmpegLogLevel  = FFmpegLogLevel.Warning,
            #endif

            UIRefresh       = false, // For Activity Mode usage
            PluginsPath     = ":Plugins",
            FFmpegPath      = ":FFmpeg",
        });

        Player = new Player();

        InitializeComponent();
        rootGrid.DataContext = this;
    }

}
