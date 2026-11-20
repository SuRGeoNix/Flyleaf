# FlyleafME

FlyleafME is a reusable WPF media player control powered by FlyleafLib. It combines a video surface with customizable playback controls, a queue and slideshow, subtitles, on-screen messages, and dockable panels.

FlyleafME is the successor to the `FlyleafLib.Controls.WPF` package. The new package ID and assembly are `FlyleafME`, and the public namespace is `Flyleaf.Controls.WPF`.

## Install

Use a WPF project targeting `net10.0-windows`.

```shell
dotnet add package FlyleafME --prerelease
```

Omit `--prerelease` when installing a stable release. The package brings in the managed FlyleafLib dependency. Put the compatible native FFmpeg DLLs in an `FFmpeg` folder beside your executable and choose a project platform that matches the DLLs, such as x64. [Flyleaf downloads](https://github.com/SuRGeoNix/Flyleaf/releases)

## Quick start

In this example, the WPF project is named `PlayerDemo` and `App.xaml` has `StartupUri="MainWindow.xaml"`. Use your project's namespace in the XAML and code.

### Start the engine in App.xaml.cs

Initialize the engine once on the UI thread, before creating any players.

```csharp
using System;
using System.IO;
using System.Windows;
using FlyleafLib;

namespace PlayerDemo;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Engine.Start(new EngineConfig
        {
            FFmpegPath = Path.Combine(AppContext.BaseDirectory, "FFmpeg")
        });

        base.OnStartup(e);
    }
}
```

### Add the control in MainWindow.xaml

```xml
<Window x:Class="PlayerDemo.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:me="clr-namespace:Flyleaf.Controls.WPF;assembly=FlyleafME"
        Title="FlyleafME Demo" Width="800" Height="450">
    <DockPanel>
        <Button DockPanel.Dock="Top" Content="Open file" Click="OpenFile_Click"/>
        <me:FlyleafME x:Name="Media">
            <me:FlyleafME.OverlayContent>
                <me:FlyleafOverlayLayout>
                    <me:FlyleafOverlayLayout.BottomContent>
                        <me:FlyleafPlaybackBar x:Name="PlaybackBar"/>
                    </me:FlyleafOverlayLayout.BottomContent>
                </me:FlyleafOverlayLayout>
            </me:FlyleafME.OverlayContent>
        </me:FlyleafME>
    </DockPanel>
</Window>
```

### Connect the player in MainWindow.xaml.cs

```csharp
using System;
using System.Windows;
using FlyleafLib.MediaPlayer;

namespace PlayerDemo;

public partial class MainWindow : Window
{
    private readonly Player player;

    public MainWindow()
    {
        InitializeComponent();

        player = new Player();
        Media.Player = player;
        Media.RegisterDefaultPanels();
        PlaybackBar.MediaElement = Media;
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e) => Media.OpenFile();

    protected override void OnClosed(EventArgs e)
    {
        PlaybackBar.MediaElement = null;
        Media.Player = null;
        Media.Dispose();
        player.Dispose();
        base.OnClosed(e);
    }
}
```

Open a local file with the button. Playback starts automatically with the default player settings. To open a path or URL from code, call `Media.OpenInput(pathOrUrl)` on the UI thread.

## Customize the control

- Use `UIConfig` to change appearance, playback bar behavior, subtitle styling, on-screen messages, and mouse gestures
- Use `HeaderContent` and `OverlayContent` to compose your own player UI
- Use `RegisterDefaultPanels()` for the built-in panels and `RegisterPanel(...)` to supply your own
- Use `Media.Queue` for queue operations and `Media.Queue.AutoAdvance = true` to enable automatic progression and slideshow playback

Your application owns the player lifecycle and decides when to save settings and panel layouts. Use `FlyleafUIConfigSerializer` to serialize the control preferences and `CapturePanelLayout()` and `RestorePanelLayout(...)` to save and restore panel placements.

See the [Flyleaf desktop player source](https://github.com/SuRGeoNix/Flyleaf/tree/master/Flyleaf) for an example with multiple windows, profiles, and playlist management.

## License

[LGPL-3.0-or-later](https://github.com/SuRGeoNix/Flyleaf/blob/master/LICENSE.txt)
