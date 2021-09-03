# *Flyleaf v3.3*: Media Player .NET Library for WPF/WinForms (based on FFmpeg/DirectX)

![alt text](Images/Flyleafv3.0.png)
---

>Notes<br/>
>1. FlyleafLib's releases will be on [NuGet](https://www.nuget.org/packages?q=flyleaf)
>2. Compiled samples will be on [GitHub releases](https://github.com/SuRGeoNix/Flyleaf/releases)
>3. Documentation will be on [Wiki](https://github.com/SuRGeoNix/Flyleaf/wiki) and [Samples](https://github.com/SuRGeoNix/Flyleaf/tree/master/Samples) within the solution

### [Supported Features]
* ***Light***: for GPU/CPU/RAM as it supports video acceleration, post-processing with pixel shaders and it is coded from scratch
* ***High Performance***: threading implementation and efficient aborting/cancelation allows to achieve fast seeking
* ***Stable***: library which started as a hobby about 2 years ago and finally became a stable (enough) media engine
* ***Formats Support***: All formats and protocols that FFmpeg supports with the additional supported by plugins (currently torrent (bitswarm) / web (youtube-dl) streaming)
* ***Pluggable***: Focusing both on allowing custom inputs (such as a user defined stream) and support for 3rd party plugins (such as scrappers / channels / playlists etc.)
* ***Configurable***: Exposes low level parameters for customization (demuxing/buffering & decoding parameters) 
* ***UI Controls***: Providing both "naked" and embedded functionality controls 
* ***Multiple Instances***: Supports multiple players with different configurations (such as audio devices/video aspect ratio etc.)

### [Extra Features]
* ***HLS Player***: supports live seeking (might the 1st FFmpeg player which does that)
* ***AudioOnly***: supports Audio Player only without the need of Control/Rendering
* ***RTSP***: supports RTSP cameras with low latency
* ***Downloader***: supports also the plugins so you can download any youtube-dl supported url as well
* ***Recorder***: record the currently watching video
* ***Snapshots***: Take a snapshot of the current frame
* ***Fast Forward***: Change playback speed (x1, x2, x3, x4)
* ***Zoom In/Out***: Zoom In/Out the rendering surface
* ***Frame Extractor***: Extract video frames to BMP, JPG, PNG etc. (All, Specific or by Step)

### [Missing Features]
* HDR/HDR.10/HLG support (BT2020 / SMPTE 2084)
* Post Process Filters (Brightness/Sharping etc.)
* Windows OS is required

### [Thanks to]
*Flyleaf wouldn't exist without them!*

* ***[FFmpeg](http://ffmpeg.org/)***
* ***[FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen/)***
* ***[NAudio](https://github.com/naudio/NAudio)***
* ***[Vortice](https://github.com/amerkoleci/Vortice.Windows)***
* Major open source media players ***VLC***, ***Kodi***, ***FFplay***
* For plugins thanks to ***Youtube-DL*** and ***OpenSubtitles.org***