## **C# Video Demuxing & Hardware(GPU) Decoding / Processing Acceleration Tutorial**
_(Based on FFmpeg.Autogen bindings for FFmpeg &amp; SharpDX bindings for DirectX)_

Lately, I&#39;ve decided to develop a Video Player for educational purposes as I&#39;ve never messed with this field before and I found it very interesting (especially for its difficulty and complexity). That&#39;s the reason for writing this tutorial, that hopefully will help others to cross those obstacles (on this tutorial focusing on Video Acceleration only).

The very first problem that I came across was the programming language choice that I&#39;ve made (C# .NET) and that&#39;s because there are not any official FFmpeg (for Decoding/Demuxing) or Microsoft DirectX (for Hardware/GPU Acceleration) Libraries. The only solution that I&#39;ve found was to use the FFmpeg.Autogen C# bindings for FFmpeg and SharpDX (deprecated) for Microsoft DirectX.

The second problem was the lack of documentation for both these Libraries. So many years of their existence and their writings are unfortunately still poor. The only way to understand how they work is by reading low-level large open source code (from projects such as VLC, Kodi etc.).

The main process that this Project/Tutorial follows is the following :-

* Creating a Direct3D 11 Device (both for Rendering & Decoding)
* Parsing our Direct3D 11 Device to FFmpeg and initializing FFmpeg's HW Device Context
* Parsing the created HW Device Context to the desired Video Codec (during Opening Codec)
* FFmpeg Demuxing packets & HW Decoding Video Frames (NV12 | P010)
* Copying from FFmpeg's Texture2D Pool array the current decoded Video Frame (CopySubresourceRegion)
* DirectX Video Processing (VideoProcessorBlt) HW texture (Directly to Backbuffer)
* Rendering the Backbuffer (PresentFrame)

With the above process we manage to perform both Video Decoding but also Video Proccessing (NV12->RGB) without "touching" our CPU/RAM.

**Required Libraries**

- _[SharpDX](http://sharpdx.org/)_ (Latest) – NuGet Package
- _[FFmpeg.Autogen](https://github.com/Ruslan-B/FFmpeg.AutoGen)_ (Latest) – NuGet Package
- _[FFmpeg](https://github.com/BtbN/FFmpeg-Builds/releases)_ (Compatible with FFmpeg.Autogen – GPL shared)
- Check FFmpeg.RegisterFFmpegBinaries() to link FFmpeg .dll libraries