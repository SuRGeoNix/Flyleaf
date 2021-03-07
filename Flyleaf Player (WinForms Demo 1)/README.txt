============
Flyleaf v2.5
============
A video player with HD & Streaming (Torrent/Web +) support
						by John Stamatakis (aka SuRGeoNix)


NOTES
	- Subtitles non-Unicode	: Converting will be done based on system default (Language Settings -> Administrative -> System locale) - (Opensubtitles default convert to UTF-8 failed as well)
	- File IO Access		: Ensure that Flyleaf will have access on the folder that you will place it (or run as administrator)
	- Firewall				: It is recommended to add an exception for Flyleaf to ensure proper streaming (especially for torrent streaming)

Additions / Enhancements
	- Re-design of Flyleaf's core to achieve better performance and lower resources
	- Seperating Demux with Decode (useful for buffering especially on network streams)
	- Using the same direct3D 11 device both for rendering & decoding (better performance as no extra shared copy is required)
	- Generalize AVIO context to support any Stream type (currently used for Torrent streaming)
	- Better thread handling without abort/start by using reset events (better performance)
	- Making Seek process even more efficient and faster for better user experience
	- Better audio handling by spoting NAudio's issue with threading & better volume / mute control (by handling also app session's device)
	- Adding Subtitles outline and performing more efficient syncing
	- Adding Exit & Media Info on right click pop-up menu
	- Updating dependency libraries (FFmpeg/BitSwarm/APF/Youtube-dl)
	
Issues
	- FFmpeg's seek abort would cause scanning the whole file (on matroska)
	- No more green screens and thread sleeps to avoid them by using the same direct3D 11 device for decoding & rendering
	- Screamer would not scream last video frame
	- Rendering on non-fullscreen was not accurate (aspect ratio issue)
	- Temporarly disabling rendering to boot faster (during settings loading)
	
Changes 2.5.1
	- Finalizing new Seek also for network (slow) streams
	- Fixing a critical issue for all other non-common protocols (rtsp/ftp etc.)
	- Fixing an issue with streams that have start_time != 0
	- Fixing a freeze issue and other crashing issues during open (re-enabling interrupt/abort instead of thread abort)
	- Fixing an issue with OSD clock, was not displaying seek time on non-SeekOnSlide
	- Fixing an issue with BitSwarm with PieceRetries (was dropping peers that shouldn't)
	- Adding http-refer for youtube-dl urls to support more sites
	- Adjusting default torrent streaming timing settings & default timeout for rtsp (10 seconds)

Project	: https://github.com/SuRGeoNix/Flyleaf
License	: LGPL-3.0
<3<3<3<3: FFmpeg, FFmpeg.Autogen, Opensubtitles & SharpDX