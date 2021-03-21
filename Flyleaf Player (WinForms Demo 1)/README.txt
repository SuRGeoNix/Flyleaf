============
Flyleaf v2.6
============
A video player with HD & Streaming (Torrent/Web +) support
						by John Stamatakis (aka SuRGeoNix)


NOTES
	- Subtitles non-Unicode	: Converting will be done based on system default (Language Settings -> Administrative -> System locale) - (Opensubtitles default convert to UTF-8 failed as well)
	- File IO Access		: Ensure that Flyleaf will have access on the folder that you will place it (or run as administrator)
	- Firewall				: It is recommended to add an exception for Flyleaf to ensure proper streaming (especially for torrent streaming)

Additions / Enhancements
	- Re-coding MediaRenderer
	- Hardware Frames (NV12/P010 Semi-Planar): Direct Convert to RGB with PixelShaders (No more VideoProcessorBlt)
	- Software Frames (8-bit YUV Planar/Packed): PixelShaders support for most of them (Planar/Packed)
	- Rest Frames (RGB & YUV > 8-bit): Still using fallback to "heavy" SwsScale (however improvement also here with direct copy)
	- ColorSpace / ColorRange support (BT601/BT709 Full/Limited)
	- Better Multi-threading SwapChain implementation and more BackBuffers in use
	- Audio auto re-sync in case of de-sync
	
Issues
	- Fixing an issue while ffmpeg fails to seek on keyframe we skip until it finds one (was showing broken frames)
	- Fixing an issue with Audio noise during pause/play
	
Project	: https://github.com/SuRGeoNix/Flyleaf
License	: LGPL-3.0
<3<3<3<3: FFmpeg, FFmpeg.Autogen, Opensubtitles & SharpDX