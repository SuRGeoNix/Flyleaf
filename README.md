# <img src="Images/Flyleaf_TitleBar_128.png" width="32">*lyleaf*: Media Player .NET Library

![alt text](Images/Flyleafv3.12.png)

---

>Notes<br/>
>1. FlyleafLib's releases will be on [NuGet](https://www.nuget.org/packages?q=flyleaflib)
>2. Compiled samples will be on [GitHub releases](https://github.com/SuRGeoNix/Flyleaf/releases)
>3. Documentation will be on [Wiki](https://github.com/SuRGeoNix/Flyleaf/wiki) (outdated!) and [Samples](https://github.com/SuRGeoNix/Flyleaf/tree/master/Samples) within the solution

> **Repository History Update (November 2026):**
> Git history was rewritten to remove obsolete binaries and media assets, reducing repository storage by approximately 98.5%. Existing contributors are encouraged to perform a fresh clone. Forks and branches based on the previous history may require rebasing or recreation before contributing.

# [Overview]

✅ **Play Everything** <sub>(Audio, Videos, Images, Playlists over any Protocol)</sub>

- *Extends FFmpeg's supported protocols and formats with additional plugins <sub>(YoutubeDL, TorrentBitSwarm)</sub>*
- *Accepts Custom I/O Streams and Plugins to handle non-standard protocols / formats*
	
✅ **Play it Smoothly** <sub>(Even with high resolutions 8K / HDR)</sub>

- *Coded from scratch to gain the best possible performance with FFmpeg & DirectX using video acceleration and custom pixel shaders*
- *Threading implementation with efficient cancellation which allows fast open, play, pause, stop, seek and stream switching*
	
✅ **Develop it Easy**

- *Provides a DPI aware, hardware accelerated Direct3D Surface (FlyleafHost) which can be hosted as normal control to your application and easily develop above it your own transparent overlay content*
- *All the implementation uses UI notifications (PropertyChanged / ObservableCollection etc.) so you can use it as a ViewModel directly*    
- *For WPF, FlyleafME provides a composable playback UI with customizable controls and panels. [FlyleafME quick start](FlyleafME/README.md)*

# [Features]

### **FFmpeg**
- *HLS Live Seeking <sub>Supports seeking in live HLS streams</sub>*
- *Custom FFmpeg Builds <sub>Based on FFmpeg master (post-9.1 Gauss), with Flyleaf-specific optimizations and patches</sub>*
- *Additional Patches <sub>HLS persistent HTTP/HTTPS connections, Windows Schannel TLS 1.3, and MPEG-TS HEVC seeking</sub>*
- *Capture Devices <sub>Pass the format, input and options with a single URL, e.g. fmt://gdigrab?desktop&framerate=30</sub>*
- *FFmpeg ABI <sub>Uses [Flyleaf.FFmpeg.ABI](https://github.com/SuRGeoNix/Flyleaf.FFmpeg.ABI) bindings; use a compatible version for your FFmpeg binaries</sub>*
- *Recommended binaries and patch details: [Flyleaf FFmpeg Releases](https://github.com/SuRGeoNix/FFmpeg/releases)*

### **Playback**
- *Open / Play / Pause / Stop*
- *Speed / Reverse / Zero-Low Latency*
- *Seek Backward / Forward <sub>(Short / Medium / Large Step)</sub>*
- *Seek to Time / Seek to Frame / Seek to Chapter / Frame Stepping*

### **Video**
- *Enable / Disable*
- *Device Preference*
- *Aspect Ratio <sub>(Keep / Fill / Custom)</sub>*
- *Deinterlace <sub>(D3D11VP only - Supports double rate)</sub>*
- *Advanced HDR10 / HDR10+ / HLG / Dolby Vision <sub>(FlyleafVP only - All Dovi Profiles including L1/L3, L2/L8 trims, 7 FEL)</sub>*
- *Embedded ICC Profiles <sub>(FlyleafVP only)</sub>*
- *Pan Move / Zoom / Rotate / HFlip-VFlip / Cropping*
- *Record / Snapshot*
- *Super Resolution <sub>(D3D11VP only - Nvidia / Intel)</sub>*
- *360° Panoramic <sub>(FlyleafVP only)</sub>*
- *Video Acceleration*
- *Video Filters <sub>(Brightness / Contrast / Hue / Saturation)</sub>*
- *Video Processors <sub>(FlyleafVP / D3D11VP / SwsScale)</sub>*
- *VSync*
- *Zero-Copy <sub>(Crops with vertex shader)</sub>*
- *Split-Frame / Alpha Packing <sub>(FlyleafVP only - Special technique to support alpha with hardware acceleration)</sub>*

### **Audio**
- *Enable / Disable*
- *Device Preference*
- *Add / Remove Delay <sub>(Short / Large Step)</sub>*
- *Volume <sub>(Up / Down / Mute)</sub>*
- *Native Audio Formats <sub>(Sample Formats / Sample Rates / Channel Layouts)</sub>*
- *Languages support <sub>System's default languages as priorities for audio streams</sub>*

### **Subtitles**
- *Enable / Disable*
- *Add / Remove Delay <sub>(Short / Large Step)</sub>*
- *Bitmap Subtitles support*
- *Advanced Character Detection and Convert to UTF-8 <sub>SubtitlesConverter plugin</sub>*
- *Languages support <sub>System's default languages as priorities for subtitles streams</sub>*

### **UI Control (FlyleafHost)** <sub>*WPF / WinUI &amp; WinForms (Partially)*</sub>
- *Attach / Detach*
- *Activity / Idle Mode*
- *Drag Move <sub>(Self / Owner)</sub>*
- *Drag & Drop Swap*
- *Drag & Drop Open*
- *Full / Normal Screen*
- *Resize / Resize & Keep Ratio*
- *Z-Order*

### **UI Control (FlyleafME)** <sub>*WPF / .NET 10*</sub>

`FlyleafME` package · [Quick start](FlyleafME/README.md)

- *Customizable Playback Bar / Seek Bar / Context Menu*
- *Media Queue / Slideshow*
- *Docked / Floating / Overlay Panels <sub>(Playback, Video, Audio, Subtitles, Media Information, Queue, Settings)</sub>*
- *Customizable Subtitle Text / On-Screen Display*
- *Dark / Light / System Themes with Accent Colors*

### **UI Control (FlyleafME Legacy)** <sub>*WPF / .NET 8 and .NET 10*</sub>

`FlyleafLib.Controls.WPF` package

- *Flyleaf Bar Control / Slider*
- *Flyleaf Popup Menu*
- *Flyleaf Settings Dialog*
- *Color Themes <sub>Based on Material Design in XAML</sub>*
- *Style / Control Template Customization*

### **Plugins**
- *OpenSubtitlesOrg <sub>Search & Download for online Subtitles</sub>*
- *SubtitlesConverter <sub>Detect & Convert the input's charset to UTF-8</sub>*
- *TorrentBitSwarm <sub>Play a media from torrent without the need to download it completely</sub>*
- *YTDLP <sub>Play web media that are not accessible directly with HTTP(s)</sub>*

### Misc.
- *Mouse & Key Bindings <sub>All the implementation supports customizable mouse & key bindings which can be assigned to an embedded or a custom actions (find defaults [here](https://github.com/SuRGeoNix/Flyleaf/wiki/Player-(Key-&-Mouse-Bindings)))</sub>*
- *Audio Player <sub>Can be used as an audio player only without the need of UI Control</sub>*
- *Downloader / Remuxer <sub>The library can be used also for downloading & remuxing</sub>*
- *Extractor <sub>The library can be used also for extracting video frames (supports also by X frames Step)</sub>*

# [Building from Source]

FFmpeg native binaries are no longer included in the repository. They are distributed separately through [Flyleaf FFmpeg releases](https://github.com/SuRGeoNix/FFmpeg/releases).

After cloning the repository, run `Download-FFmpeg.bat` from the repository root to download the recommended FFmpeg build into the `FFmpeg/` directory.

```powershell
git clone https://github.com/SuRGeoNix/Flyleaf.git
cd Flyleaf
.\Download-FFmpeg.bat
```

The script downloads a pinned Windows x64 LGPL shared build, ensuring consistent FFmpeg versions across development environments. Running it again replaces the existing FFmpeg binaries.

Once downloaded, open `FlyleafLib.sln` in Visual Studio to build the library and samples.

# [Thanks to]

*Flyleaf wouldn't exist without them!*

* *For the Core*
  * ***[FFmpeg](http://ffmpeg.org/)*** / ***[Flyleaf.FFmpeg.ABI](https://github.com/SuRGeoNix/Flyleaf.FFmpeg.ABI)***
  * DirectX / ***[Vortice](https://github.com/amerkoleci/Vortice.Windows)***
  * *Major open source media players* ***[FFplay](https://github.com/FFmpeg/FFmpeg/blob/master/fftools/ffplay.c)***, ***[Kodi](https://github.com/xbmc/xbmc)***, ***[Libplacebo](https://github.com/haasn/libplacebo)***, ***[MPCVR](https://github.com/Aleksoid1978/VideoRenderer/)***, ***[MPV](https://github.com/mpv-player/mpv)***, ***[VLC](https://github.com/videolan/vlc)***

* *For the Plugins*
  * ***[BitSwarm](https://github.com/SuRGeoNix/BitSwarm)***
  * ***[OpenSubtitles.org](https://www.opensubtitles.org/)***
  * ***[YT-DLP](https://github.com/yt-dlp/yt-dlp)*** / ***[Flyleaf.Plugins.YTDLP](https://github.com/SuRGeoNix/Flyleaf.Plugins.YTDLP)***