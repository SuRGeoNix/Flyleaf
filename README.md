# *Flyleaf v3.0*: Video Player .NET Library for WPF/WinForms (based on FFmpeg/SharpDX)

![alt text](Images/Flyleafv3.0.png)
---

>Notes<br/>
>1. FlyleafLib's releases will be on [NuGet](https://www.nuget.org/packages?q=flyleaf)
>2. You can find a compiled sample version on [GitHub releases](https://github.com/SuRGeoNix/Flyleaf/releases)
>3. Start your own custom WPF MVVM based on [Sample2_Custom](https://github.com/SuRGeoNix/Flyleaf/tree/master/Wpf%20Samples)
>4. To create your own custom control only FlyleafLib package is required

### [Supported Features]
* ***Light***: for GPU/CPU/RAM as it supports video acceleration, post-processing with pixel shaders and it is coded from scratch
* ***High Performance***: threading implementation and efficient aborting/cancelation allows to achieve fast seeking
* ***Stable***: library which started as a hobby about 2 years ago and finally became a stable (enough) media engine
* ***Formats Support***: All formats and protocols that FFmpeg supports with the additional supported by plugins (currently torrent (bitswarm) / web (youtube-dl) streaming)
* ***Pluggable***: Focusing both on allowing custom inputs (such as a user defined stream) and support for 3rd party plugins (such as scrappers / channels / playlists etc.)
* ***Configurable***: Exposes low level parameters for customization (demuxing/buffering & decoding parameters) 
* ***UI Controls***: Providing both "naked" and embedded functionality controls 
* ***Multiple Instances***: Supports multiple players with different configurations (such as audio devices/video aspect ratio etc.)
* ***Extra Features***: Record, Download/Remux, Zoom In/Out, HLS Live Seeking (might the **1st** FFmpeg player that does it!)

### [Missing Features]
* HDR support
* Post Process Filters (Brightness/Sharping etc.)
* Speed (Step/Fast Backwards/Forwards)
* Windows OS is required

### [Major changes from v2.6]
* Separating controls and plugins (*bitswarm, youtube-dl, opensubtitles*) from the library
* Recoding MediaRouter (renamed to Player) to support workflows for plugins (a base for later on to support scrappers etc.)
* Recoding AudioPlayer to support any user defined device and different configuration (device,volume,mute) in case of multiple players
* Dropping OSD support from MediaRender and removing Direct2D rendering (was used mainly to achieve transparent overlay content which can be done now with WPF)
* Dropping WinForms embedded functionality support (at least for now, still supports "naked" control) and focusing on WPF/MVVM to achieve better graphics/effects/styles/themes/templates etc.
* Adding new features such as Take a Snapshot and Pan Zoom In/Out
* Creating library's and WPF control's NuGet packages

### [Build Requirements]
* .NET 5 (windows) / .NET Framework 4.7.2

For .net 5 you will need to ensure that you use *__net5.0-windows__* as target framework (change your .csproj file)
```
<TargetFramework>net5.0-windows</TargetFramework>
```

* FFmpeg shared libraries (compatible with FFmpeg.Autogen)
  * By the time of writing (v4.4.79) has a critical issue with HLS protocol, this [patch](https://patchwork.ffmpeg.org/project/ffmpeg/list/?series=1018) has been applied to the repository's libraries to fix it. That means that you will need at least the **avformat-58.dll** (x64) from here.
* Use Master.RegisterFFmpeg(*ffmpeg_path*) to register them (Ensure you use x86 or x64 libraries)
* To use plugins requires at least one time to build the solution in *Release* configuration
* Generaly plugins should exist in *Plugins* output/target directory
* Debugging with BitSwarm plugin should disable NullReference exceptions
* Don't run VS designer in x64
* If you are editing WPF flyleaf's template use style with StaticResource and not DynamicResource

### [Thanks to]
*Flyleaf wouldn't exist without them!*

* ***FFmpeg***
* ***FFmpeg.AutoGen***
* ***NAudio***
* ***SharpDX***
* Major open source media players ***VLC***, ***Kodi***, ***FFplay***
* For plugins thanks to ***Youtube-DL*** and ***OpenSubtitles.org***