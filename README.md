# *Flyleaf v3.9*: Media Player .NET Library for WinUI 3/WPF/WinForms (based on FFmpeg/DirectX)

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
- *HLS Live Seeking <sub>Might the 1st FFmpeg player which does that</sub>*
- *Pached for [HLS](https://patchwork.ffmpeg.org/project/ffmpeg/list/?series=1018) and [.NET](https://developercommunity.microsoft.com/t/Proper-handling-of-MS_VC_EXCEPTION-0x40/10961029) issues <sub>Use recommended FFmpeg libraries which can be found on GitHub releases</sub>*
- *Capture Devices <sub>Pass the format, input and options with a single Url eg. fmt://gdigrab?desktop&framerate=30</sub>*
- *Supports FFmpeg v7.1 and v8.0 <sub>(use Flyleaf.FFmpeg.Bindings v8 at your project)</sub>*

### **Playback**
- *Open / Play / Pause / Stop*
- *Speed / Reverse / Zero-Low Latency*
- *Seek Backward / Forward <sub>(Short / Large Step)</sub>*
- *Seek to Time / Seek to Frame / Seek to Chapter / Frame Stepping*

### **Video**
- *Enable / Disable*
- *Device Preference*
- *Aspect Ratio <sub>(Keep / Fill / Custom)</sub>*
- *Deinterlace <sub>(Supports double rate, D3D11VP only)</sub>*
- *HDR to SDR <sub>(Aces / Hable / Reinhard - FlyleafVP only)</sub>*
- *Pan Move / Zoom / Rotate / HFlip-VFlip / Cropping <sub>~~(Replica Renderer/Interactive Zoom)~~</sub>*
- *Record / Snapshot*
- *Super Resolution <sub>(Nvidia / Intel - D3D11VP only)</sub>*
- *Video Acceleration*
- *Video Filters <sub>(Brightness / Contrast / Hue / Saturation)</sub>*
- *Video Processors <sub>(FlyleafVP / D3D11VP)</sub>*
- *VSync*
- *Zero-Copy <sub>(Crops with vertex shader)</sub>*

### **Audio**
- *Enable / Disable*
- *Device Preference*
- *Add / Remove Delay <sub>(Short / Large Step)</sub>*
- *Volume <sub>(Up / Down / Mute)</sub>*
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

### **UI Control (FlyleafME)** <sub>*WPF Only*</sub>
- *Flyleaf Bar Control / Slider*
- *Flyleaf Popup Menu*
- *Flyleaf Settings Dialog*
- *Color Themes <sub>Based on Material Design in XAML</sub>*
- *Style / Control Template Customization*

### **Plugins**
- *OpenSubtitlesOrg <sub>Search & Download for online Subtitles</sub>*
- *SubtitlesConverter <sub>Detect & Convert the input's charset to UTF-8</sub>*
- *TorrentBitSwarm <sub>Play a media from torrent without the need to download it completely</sub>*
- *YoutubeDL <sub>Play web media that are not accessible directly with HTTP(s)</sub>*

### Misc.
- *Mouse & Key Bindings <sub>All the implementation supports customizable mouse & key bindings which can be assigned to an embedded or a custom actions (find defaults [here](https://github.com/SuRGeoNix/Flyleaf/wiki/Player-(Key-&-Mouse-Bindings)))</sub>*
- *Audio Player <sub>Can be used as an audio player only without the need of UI Control</sub>*
- *Downloader / Remuxer <sub>The library can be used also for downloading & remuxing</sub>*
- *Extractor <sub>The library can be used also for extracting video frames (supports also by X frames Step)</sub>*

# [Thanks to]

*Flyleaf wouldn't exist without them!*

* *For the Core*
  * ***[FFmpeg](http://ffmpeg.org/)*** / ***[FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen/)*** / ***[Flyleaf.FFmpeg.Bindings](https://github.com/SuRGeoNix/Flyleaf.FFmpeg.Generator)***
  * ***[Vortice](https://github.com/amerkoleci/Vortice.Windows)***
  * *Major open source media players* ***[VLC](https://github.com/videolan/vlc)***, ***[Kodi](https://github.com/xbmc/xbmc)***, ***[MPV](https://github.com/mpv-player/mpv)***, ***[MPC-BE](https://github.com/Aleksoid1978/MPC-BE)***, ***[FFplay](https://github.com/FFmpeg/FFmpeg/blob/master/fftools/ffplay.c)***

* *For the UI*
  * ***[Dragablz](https://github.com/ButchersBoy/Dragablz)***
  * ***[MaterialDesign Colors & Themes](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/)***

* *For the Plugins*
  * ***[BitSwarm](https://github.com/SuRGeoNix/BitSwarm)***
  * ***[OpenSubtitles.org](https://www.opensubtitles.org/)***
  * ***[YT-DLP](https://github.com/yt-dlp/yt-dlp)***