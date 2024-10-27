# *Flyleaf v3.8*: Media Player .NET Library for WinUI 3/WPF/WinForms (based on FFmpeg/DirectX)

![alt text](Images/Flyleafv3.6.png)

---

>Notes<br/>
>1. FlyleafLib's releases will be on [NuGet](https://www.nuget.org/packages?q=flyleaf)
>2. Compiled samples will be on [GitHub releases](https://github.com/SuRGeoNix/Flyleaf/releases)
>3. Documentation will be on [Wiki](https://github.com/SuRGeoNix/Flyleaf/wiki) and [Samples](https://github.com/SuRGeoNix/Flyleaf/tree/master/Samples) within the solution

# [Overview]

✅ **Play Everything** <sub>(Audio, Videos, Images, Playlists over any Protocol)</sub>

- *Extends FFmpeg's supported protocols and formats with additional plugins <sub>(YoutubeDL, TorrentBitSwarm)</sub>*
- *Accepts Custom I/O Streams and Plugins to handle non-standard protocols / formats*
	
✅ **Play it Smoothly** <sub>(Even with high resolutions 4K / HDR)</sub>

- *Coded from scratch to gain the best possible performance with FFmpeg & DirectX using video acceleration and custom pixel shaders*
- *Threading implementation with efficient cancellation which allows fast open, play, pause, stop, seek and stream switching*
	
✅ **Develop it Easy**

- *Provides a DPI aware, hardware accelerated Direct3D Surface (FlyleafHost) which can be hosted as normal control to your application and easily develop above it your own transparent overlay content*
- *All the implementation uses UI notifications (PropertyChanged / ObservableCollection etc.) so you can use it as a ViewModel directly*    
- *For WPF provides a Control (FlyleafME) with all the basic UI sub-controls (Bar, Settings, Popup menu) and can be customized with style / control template overrides*

# [Features]

### **FFmpeg**
- *HLS Live Seeking* <sub>Might the 1st FFmpeg player which does that</sub>
- *Pached for [HLS issue](https://patchwork.ffmpeg.org/project/ffmpeg/list/?series=1018)* <sub>Use recommended FFmpeg libraries which can be found on GitHub releases</sub>
- *Capture Devices* <sub>Pass the format, input and options with a single Url eg. fmt://gdigrab?desktop&framerate=30</sub>
- *Supports FFmpeg v7.1*

### **Playback**
- *Open / Play / Pause / Stop*
- *Speed / Reverse / Zero-Low Latency*
- *Seek Backward / Forward (Short / Large Step)*
- *Seek to Time / Seek to Frame / Seek to Chapter / Frame Stepping*

### **Video**
- *Enable / Disable*
- *Device Preference*
- *Aspect Ratio (Keep / Fill / Custom)*
- *Deinterlace (Bob)*
- *HDR to SDR (Aces / Hable / Reinhard)* <sub>[BT2020 / SMPTE 2084 to BT709]</sub>
- *Pan Move / Zoom / Rotate / HFlip-VFlip (Replica Renderer/Interactive Zoom)*
- *Record / Snapshot*
- *Video Acceleration*
- *Video Filters (Brightness / Contrast / Hue / Saturation)*
- *Video Processors (Flyleaf / D3D11)*
- *VSync*
- *Zero-Copy*

### **Audio**
- *Enable / Disable*
- *Device Preference*
- *Add / Remove Delay (Short / Large Step)*
- *Volume (Up / Down / Mute)*
- *Languages support* <sub>System's default languages as priorities for audio streams</sub>

### **Subtitles**
- *Enable / Disable*
- *Add / Remove Delay (Short / Large Step)*
- *Advanced Character Detection and Convert to UTF-8* <sub>SubtitlesConverter plugin</sub>
- *Languages support* <sub>System's default languages as priorities for subtitles streams</sub>

### **UI Control (FlyleafHost)**<sub>WPF / WinUI &amp; WinForms (Partially)</sub>
- *Attach / Detach*
- *Activity / Idle Mode*
- *Drag Move (Self / Owner)*
- *Drag & Drop Swap*
- *Drag & Drop Open*
- *Full / Normal Screen*
- *Resize / Resize & Keep Ratio*
- *Z-Order*

### **UI Control (FlyleafME)** <sub>WPF Only</sub>
- *Flyleaf Bar Control / Slider*
- *Flyleaf Popup Menu*
- *Flyleaf Settings Dialog*
- *Color Themes* <sub>Based on Material Design in XAML</sub>
- *Style / Control Template Customization*

### **Plugins**
- *OpenSubtitlesOrg* <sub>Search & Download for online Subtitles</sub>
- *SubtitlesConverter* <sub>Detect & Convert the input's charset to UTF-8</sub>
- *TorrentBitSwarm* <sub>Play a media from torrent without the need to download it completely</sub>
- *YoutubeDL* <sub>Play web media that are not accessible directly with HTTP(s)</sub>

### Misc.
- *Mouse & Key Bindings* <sub>All the implementation supports customizable mouse & key bindings which can be assigned to an embedded or a custom actions (find defaults [here](https://github.com/SuRGeoNix/Flyleaf/wiki/Player-(Key-&-Mouse-Bindings)))</sub>
- *Audio Player* <sub>Can be used as an audio player only without the need of UI Control</sub>
- *Downloader / Remuxer* <sub>The library can be used also for downloading & remuxing</sub>
- *Extractor* <sub>The library can be used also for extracting video frames (supports also by X frames Step)</sub>

# [Thanks to]

*Flyleaf wouldn't exist without them!*

* *For the Core*
  * ***[FFmpeg](http://ffmpeg.org/)*** / ***[FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen/)***
  * ***[Vortice](https://github.com/amerkoleci/Vortice.Windows)***
  * *Major open source media players* ***[VLC](https://github.com/videolan/vlc)***, ***[Kodi](https://github.com/xbmc/xbmc)***, ***[MPV](https://github.com/mpv-player/mpv)***, ***[FFplay](https://github.com/FFmpeg/FFmpeg/blob/master/fftools/ffplay.c)***

* *For the UI*
  * ***[Dragablz](https://github.com/ButchersBoy/Dragablz)***
  * ***[MaterialDesign Colors & Themes](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/)***

* *For the Plugins*
  * ***[BitSwarm](https://github.com/SuRGeoNix/BitSwarm)***
  * ***[OpenSubtitles.org](https://www.opensubtitles.org/)***
  * ***[YT-DLP](https://github.com/yt-dlp/yt-dlp)***