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

In this example, the WPF project is named `PlayerDemo`. Use your project's namespace in the XAML and code.

### Load the control templates in App.xaml

Merge the FlyleafME resource dictionaries so WPF can render the playback bar and overlay layout.

```xml
<Application x:Class="PlayerDemo.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="pack://application:,,,/FlyleafME;component/Themes/Dark.xaml"/>
                <ResourceDictionary Source="pack://application:,,,/FlyleafME;component/Themes/Controls.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

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
        <Button x:Name="OpenFileButton" DockPanel.Dock="Top" Content="Open file" Click="OpenFile_Click"/>
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
using Flyleaf.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace PlayerDemo;

public partial class MainWindow : Window
{
    private readonly Player player;
    private WindowState previousWindowState;

    public MainWindow()
    {
        InitializeComponent();

        player = new Player();
        Media.Player = player;
        Media.RegisterDefaultPanels();
        Media.UIConfig.PlaybackBar.Visibility = FlyleafVisibilityPolicy.Auto;
        Media.FullScreenChanged += Media_FullScreenChanged;
        PlaybackBar.MediaElement = Media;
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e) => Media.OpenFile();

    private void Media_FullScreenChanged(object? sender, EventArgs e)
    {
        if (Media.IsFullScreen)
        {
            previousWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            OpenFileButton.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            OpenFileButton.Visibility = Visibility.Visible;
            WindowState = previousWindowState;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Media.FullScreenChanged -= Media_FullScreenChanged;
        PlaybackBar.MediaElement = null;
        Media.Player = null;
        Media.Dispose();
        player.Dispose();
        base.OnClosed(e);
    }
}
```

Open a local file with the button. Playback starts automatically with the default player settings. To open a path or URL from code, call `Media.OpenInput(pathOrUrl)` on the UI thread.

Double-click the video or use the fullscreen button to toggle the borderless maximized window. Press Esc to return to the previous window state. FlyleafME raises `FullScreenChanged` when its state changes, and the host window applies the window changes.

## Common tasks

The examples use `Media` from the quick start. Call them from the UI thread, for example in a button click handler.

### Configure playback bar visibility

In `Auto`, the bar stays visible before media is opened and hides after mouse inactivity once media is open. Moving the mouse over the video brings it back. Idle detection uses the engine's `UIRefresh` setting, which is enabled by default.

```csharp
Media.UIConfig.PlaybackBar.Visibility = FlyleafVisibilityPolicy.Auto;
Media.UIConfig.PlaybackBar.AutoHideDelayMilliseconds = 3000;
```

Choose `Always` or `Never` for fixed visibility. Leave the bar's WPF `Visibility` property unset to use the built-in policy.

### Queue files and play them in order

```csharp
var first = Media.Queue.Add(@"C:\Media\first.mp4");
Media.Queue.Add(@"C:\Media\second.mp4");
Media.Queue.AutoAdvance = true;
Media.Queue.Open(first);
```

The queue also advances through still images as a slideshow. Set `Media.UIConfig.Queue.SlideShowIntervalMilliseconds` to control how long each still image is shown.

### Dock the queue panel

After the control has loaded, open a panel registered by `RegisterDefaultPanels()`.

```csharp
Media.ShowPanel("queue", FlyleafPanelMode.Docked, FlyleafPanelSide.Right);
```

### Change the theme and show a message

```csharp
Media.UIConfig.Appearance.Theme = FlyleafTheme.Light;
Media.UIConfig.Appearance.Accent = FlyleafAccent.Teal;
Media.ShowOSD("Playback ready");
```

## Customize the control

- Use `UIConfig` to change appearance, playback bar behavior, subtitle styling, on-screen messages, and mouse gestures
- Use `HeaderContent` and `OverlayContent` to compose your own player UI
- Use `RegisterDefaultPanels()` for the built-in panels and `RegisterPanel(...)` to supply your own
- Use `Media.Queue` for queue operations and `Media.Queue.AutoAdvance = true` to enable automatic progression and slideshow playback

Your application owns the player lifecycle and decides when to save settings and panel layouts. Use `FlyleafUIConfigSerializer` to serialize the control preferences and `CapturePanelLayout()` and `RestorePanelLayout(...)` to save and restore panel placements.

See the [Flyleaf desktop player source](https://github.com/SuRGeoNix/Flyleaf/tree/master/Flyleaf) for an example with multiple windows, profiles, and playlist management.

## License

[LGPL-3.0-or-later](https://github.com/SuRGeoNix/Flyleaf/blob/master/LICENSE.txt)
