## **C# Video Demuxing & Hardware(GPU) Decoding / Processing Acceleration Tutorial**
_(Based on FFmpeg.Autogen bindings for FFmpeg &amp; SharpDX bindings for DirectX)_

Lately, I&#39;ve decided to develop a Video Player for educational purposes as I&#39;ve never messed with this field before and I found it very interesting (especially for its difficulty and complexity). That&#39;s the reason for writing this tutorial, that hopefully will help others to cross those obstacles (on this tutorial focusing on Video Acceleration only).

The very first problem that I came across was the programming language choice that I&#39;ve made (C# .NET) and that&#39;s because there are not any official FFmpeg (for Decoding/Demuxing) or Microsoft DirectX (for Hardware/GPU Acceleration) Libraries. The only solution that I&#39;ve found was to use the FFmpeg.Autogen C# bindings for FFmpeg and SharpDX (deprecated) for Microsoft DirectX.

The second problem was the lack of documentation for both these Libraries. So many years of their existence and their writings are unfortunately still poor. The only way to understand how they work is by reading low-level large open source code (from projects such as VLC, Kodi etc.).

Finally, this tutorial/project implements GPU Video Decoding with FFmpeg (which uses DirectX) and GPU Video Processing with DirectX by copying FFmpeg &#39;s decoded texture subresource (from FFmpeg &#39;s Direct3D device) to a shared texture which then we use from our Direct3D device to perform GPU Video Processing (VideoProcessorBlt) to convert NV12 to RGBA and present it. I have included comments within the code to explain the important steps (see FFmpeg.GetFrame and DirectX.PresentFrame).

For this project Install (Restore) the required NuGet Packages and include them in the project. Add FFmpeg Libraries to &lt;ProjectDir&gt;/Libraries/x86 and &lt;ProjectDir&gt;/Libraries/x64.

**Required Libraries**

- _[FFmpeg.Autogen](https://github.com/Ruslan-B/FFmpeg.AutoGen)_ (Latest) – NuGet Package
- _[FFmpeg](https://ffmpeg.zeranoe.com/builds/)_ (Compatible with FFmpeg.Autogen – Linking -&gt; Shared)
- _[SharpDX](http://sharpdx.org/)_ (Latest) – NuGet Package

**Create a new Project**

- Create a New Project -> Visual Studio C# Windows Forms App (.NET Framework)
- From NuGet Package Manager Install _FFmpeg.Autogen &amp; SharpDX (DXGI &amp; Direct3D11)_
- Copy FFmpeg Libraries (\*.dll) to your Project Target Directory (eg. Bin/Debug)