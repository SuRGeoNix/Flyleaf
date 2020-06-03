# Media Router

<p align="center"><img src="readme1.png" /></p>


## Introduction
The purpose of Media Router is to be a "mediator" between a frontend GUI Audio / Video player and a backend Multimedia Framework. It will be responsible to satisfy frontend's needs such as Open/Play/Pause/Seek/Stop functionalities but also to serve __accurate__ with the right __control flow__ and __synchronized__ the requested media frames such as Audio, Video and Subtitles.

__Accurate__ &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;: Media frames will be served at the exact timestamp that they supposed to.

__Control Flow__ &nbsp;: The incoming flow from Multimedia Framework and outgoing to GUI will be kept low (CPU/GPU/RAM).

__Synchronized__ : Ensures that all time the served media frames will be syncronized between them.

<br/>

## Design

### Layer 1 - Multimedia Framework (FFmpeg.cs)

> <a href="https://www.ffmpeg.org/">FFmpeg 4.2.1</a> library (implemented with C# bindings <a href="https://github.com/Ruslan-B/FFmpeg.AutoGen">FFmpeg.AutoGen</a> 4.2.0)

Demuxes the input file and configures the included media streams. It creates one thread per media stream for decoding. Additionally, it supports hardware acceleration (partially) and accurate seeking by decoding from the previous key/I frame until the requested (in case of B/P frames).

### Layer 2 - Media Router (MediaRouter.cs)

The main implementation is within the "Screamer" method that routes media frames accurately (based on frame timestamp) to the frontend. It supports Audio and Subtitles synchronization with the Video frames. Additionally, it tries to keep the frame queues low so the backend decoder will run only when required (to keep CPU/GPU/RAM low).

### Layer 3 - User Interface (UserInterface.cs)

> <a href="http://www.monogame.net/">Monogame</a> 3.7.1 & <a href="https://github.com/naudio/NAudio">NAudio</a> 1.10.0 library & <a href="https://www.codeproject.com/Tips/1193311/Csharp-Slider-Trackbar-Control-using-Windows-Forms">ColorSlider</a>

A sample GUI has been created to demonstrate Media Router's functionality. It works with both Game Engine (for taking the advantage of GPU and Game Loop) and a classic Windows Form. For subtitles will work with BOM specified, UTF-8 formats otherwise with the default system codepage (lazy support for ASS/SSA). For audio it simple runs with the NAudio library. It currently supports :- 


| Keys                  | Action                     |
| :-------------:       |:-------------:             |
| Drag & Drop           | Open                       |
| P or Space            | Pause / Play               |
| S                     | Stop                       |
| R                     | Keep Ratio                 |
| F or Escape           | Fullscreen                 |
| Left / Right Arrows   | Seeking                    |
| Up / Down Arrows      | Volume Adjustment          |
| [ / ]                 | Audio Adjustment           |
| ; / '                 | Subtitles Adjustment       |
| H                     | Video Acceleration (On/Off)|

<br/>

## Versioning | Changes
#### v1.2.2 - 3/6/2020
>__Additions__

* Implementing (with SharpDX) Direct3D 11 Video Decoding & Acceleration Support (NV12 Pixel Formats)
* Better Image Quality & CPU/RAM Performace (Video Frames lifecycle only within the GPU)
* Faster Torrent Streaming (By New Request Piece Algorithm & Disabling Embedded Subtitles)
* 'H' Key for Video Acceleration (On/Off) [Requires re-opening the input]
* FFmpeg Libraries Update to latest (except swscale)

>__Issues__

* Subtitles Issues
* Performance Issues with the UI

#### v1.2 - 23/5/2020
>__Additions__

* Torrent files / Magnet links (Drag & Drop) for torrent streaming (by merging with my other project  <a href="https://github.com/SuRGeoNix/TorSwarm">TorSwarm</a>) and creating the new __MediaStreamer__ class for general use later on
* Re-design the main Screamer's implementation and Syncing, using seperate threads (Audio/Video/Subs) Screamers and screaming each stream's frame at exact timestamp of each one
* Support for more media formats (the most common, still will not handle correctly ts/vob etc)
* Fast seeking with better FFmpeg implementation and threading so it will not hang the UI
* Audio stereo (2 channels) output for any input and sample rate (it was supporting only 1 channel and 48Khz rate)
* NAudio package updated to 1.10, FFmpeg can be updated to latest (for low quality, use the previous swscale-5.dll)

>__Issues__
* Audio/Subtitles seeking issues (it was going though the whole file) on matroska formats (avformat_seek_file can't seek with other stream but the video stream)
* NAudio was running at the main UI thread and it was hanging
* Performance issue fixed that was hanging the UI (high cpu/gpu) and was breaking the audio and syncing
* Changed Queues to ConcurrentQueues as they had issues with threading 

#### v1.1 - 7/11/2019 (First Release)
<br/>

## Remarks
I have worked on this project for education, fun and programming exercise and I've made it available to the public in case you will find it useful for similar reasons. It's always fun as programmers to have our own media player and play around. Any suggestions are always welcome!

<p align="center"><img src="readme2.png" /></p>