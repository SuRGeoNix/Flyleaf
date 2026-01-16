using System.Windows;

using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace CustomDirect2DOverlay;

/// <summary>
/// Drawing D2D Graphics with Vortice above Flyleaf's frames during playback
/// </summary>
public partial class MainWindow : Window
{
    public Player Player { get; set; }

    public MainWindow()
    {
        Engine.Start(new EngineConfig()
        {
            #if DEBUG
            LogOutput       = ":debug",
            LogLevel        = LogLevel.Debug,
            FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Warn,
            #endif

            PluginsPath     = ":Plugins",
            FFmpegPath      = ":FFmpeg",
        });

        InitializeComponent();
        Config config = new();
        config.Video.Use2DGraphics  = true;
        config.Video.D2DInitialized += Video_D2DInitialized;
        config.Video.D2DDisposing   += Video_D2DDisposing;
        config.Video.D2DDraw        += Video_D2DDraw;

        Player = new(config);

        DataContext = this;
    }

    ID2D1SolidColorBrush  brush2d;

    // D2Draw per frame
    private void Video_D2DDraw(object sender, ID2D1DeviceContext context)
    {
        Viewport vp = ((Renderer)sender).Viewport;

        context.BeginDraw();

        // Rectangle (20x20) Centered to Renderer's Viewport
        RawRectF rect1 = new(
            vp.X + (vp.Width  / 2) - 10,
            vp.Y + (vp.Height / 2) - 10,
            vp.X + (vp.Width  / 2) + 10,
            vp.Y + (vp.Height / 2) + 10);

        context.DrawRectangle(rect1, brush2d);

        context.EndDraw();
    }

    // D2D Resource Disposal
    private void Video_D2DDisposing(object sender, ID2D1DeviceContext context)
    {
        brush2d.Dispose();
    }

    // D2D Resource Initialization
    private void Video_D2DInitialized(object sender, ID2D1DeviceContext context)
    {
        brush2d = context.CreateSolidColorBrush(new Color4(1.0f, 0.0f, 0.0f, 1f));
    }
}
