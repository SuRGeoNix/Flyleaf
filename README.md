# *Flyleaf v3.6*: Media Player .NET Library for WPF/WinForms (based on FFmpeg/DirectX)

![alt text](Images/Flyleafv3.6.png)
---

>Notes<br/>
>1. FlyleafLib's releases will be on [NuGet](https://www.nuget.org/packages?q=flyleaf)
>2. Compiled samples will be on [GitHub releases](https://github.com/SuRGeoNix/Flyleaf/releases)
>3. Documentation will be on [Wiki](https://github.com/SuRGeoNix/Flyleaf/wiki) and [Samples](https://github.com/SuRGeoNix/Flyleaf/tree/master/Samples) within the solution

### [Supported Features]
* ***Light***: for GPU/CPU/RAM as it supports video acceleration, post-processing with pixel shaders and it is coded from scratch
* ***High Performance***: threading implementation and efficient aborting/cancelation allows to achieve smooth playback and fast seeking
* ***Stable***: library which started as a hobby about 2 years ago and finally became a stable (enough) media engine
* ***Formats Support***: All formats and protocols that FFmpeg supports with the additional supported by plugins (currently torrent (bitswarm) / web (youtube-dl) streaming)
* ***Pluggable***: Focusing both on allowing custom inputs (such as a user defined stream) and support for 3rd party plugins (such as scrappers / channels / playlists etc.)
* ***Configurable***: Exposes low level parameters for customization (demuxing/buffering & decoding parameters). Supports save and load of an existing config file.
* ***UI Controls***: Supports both WinForms and WPF with a large number of embedded functionality (Activity Mode/FullScreen/Mouse & Key Bindings/ICommands/Properties/UI Updates)
* ***Multiple Instances***: Supports multiple players with fast swap to each other and different configurations (such as audio devices/video aspect ratio etc.)

### [Extra Features]
* ***HLS Player***: supports live seeking (might the 1st FFmpeg player which does that)
* ***RTSP***: supports RTSP cameras with low latency
* ***AudioOnly***: supports Audio Player only without the need of Control/Rendering
* ***HDR to SDR***: supports BT2020 / SMPTE 2084 to BT709 with Aces, Hable and Reinhard methods (still in progress, HDR native not supported yet)
* ***Slow/Fast Speed***: Change playback speed (x0.0 - x1.0 and x1 - x16)
* ***Reverse Playback***: Change playback mode to reverse and still keep the same frame rate (speed x0.0 - x1.0)
* ***Recorder***: record the currently watching video
* ***Snapshots***: Take a snapshot of the current frame
* ***Pan Move***: Drag move the rendering surface
* ***Pan Rotation***: Rotate to any angle (0° - 360°) the rendering surface
* ***Pan Zoom***: Zoom In / Out the rendering surface
* ***Deinterlace***: Currently supports bob deinterlacing
* ***Video Filters***: Supports brightness, contrast, hue and saturation filters
* ***Key Bindings***: Assign embedded or custom actions to keys (check [default](https://github.com/SuRGeoNix/Flyleaf/wiki/Player-(Key-&-Mouse-Bindings)))
* ***Themes***: WPF Control is based on [Material Design In XAML](http://materialdesigninxaml.net/) and supports already some basic Color Themes
* ***Downloader***: supports also the plugins so you can download any yt-dlp supported url as well
* ***Frame Extractor***: Extract video frames to Bmp, Jpeg, Png etc. (All, Specific or by Step)

### [[FlyleafHost]](https://github.com/SuRGeoNix/Flyleaf/wiki/FlyleafHost)

A custom implementation to host the hardware accelerated D3D rendering surface within a WinUI application

* ***Attach / Detach***: Detaches the host and allows to move freely
* ***Drag Move (Self / Owner)***: Drag move within the window owner's border
* ***Drag & Drop Swap***: Fast players swap between flyleaf hosts
* ***Drag & Drop Open***: Open new inputs easily by drag & drop
* ***Full / Normal Screen***: Fast switch between normal and full screen
* ***Resize***: Custom resize implementation for transparent non-boarders windows (can respect input's ratio)
* ***Z-order***: Supports z-order betweeen flyleaf hosts within owner's window

### [Thanks to]
*Flyleaf wouldn't exist without them!*

* ***[FFmpeg](http://ffmpeg.org/)***
* ***[FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen/)***
* ***[Vortice](https://github.com/amerkoleci/Vortice.Windows)***
* Major open source media players ***[VLC](https://github.com/videolan/vlc)***, ***[Kodi](https://github.com/xbmc/xbmc)***, ***[MPV](https://github.com/mpv-player/mpv)***, ***[FFplay](https://github.com/FFmpeg/FFmpeg/blob/master/fftools/ffplay.c)***
* For plugins thanks to ***[YT-DLP](https://github.com/yt-dlp/yt-dlp)*** and ***[OpenSubtitles.org](https://www.opensubtitles.org/)***