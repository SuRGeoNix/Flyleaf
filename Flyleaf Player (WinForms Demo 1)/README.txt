============
Flyleaf v2.4
============
A video player with HD & Streaming (Torrent/Web +) support
						by John Stamatakis (aka SuRGeoNix)

NOTES
- Subtitles non-Unicode	: Converting will be done based on system default (Language Settings -> Administrative -> System locale) - (Opensubtitles default convert to UTF-8 failed as well)
- File IO Access		: Ensure that Flyleaf will have access on the folder that you will place it (or run as administrator)
- Firewall				: It is recommended to add an exception for Flyleaf to ensure proper streaming (especially for torrent streaming)

Additions
- Torrent Sessions	: With the new version of BitSwarm it will continue downloading from the previously used sessions
- History/Recent	: Adding 'Recent' on right click floating menu for fast resume on previously watched videos (It will continue playing with the same settings as it was before)
- Organized Folders	: Fixing temporary files/folders etc and exposing folders in settings for customization

Enhancements
- Audio		: Changing by default to 32-bit float for better quality
- Audio		: Better resample implementation ready to support filters & exposed as settings for user customization (bits, channels/layout, rate)
- DirectX	: Compatibility with older Windows versions
- Libraries	: Updating BitSwarm, FFmpeg, Youtube-DL, misc versions
- Subtitles	: Better sorting and selection (giving match priority to moviehash instead of rating)
- Youtube-DL: Better implementation ready to support more audio/video formats and be exposed as settings for user customization

Issues
- Fixing a minor memory leak issue on demuxing (thanks to pubpy2015)
- Fixing other minor issues


Project	: https://github.com/SuRGeoNix/Flyleaf
License	: LGPL-3.0
<3<3<3<3: FFmpeg, FFmpeg.Autogen, Opensubtitles & SharpDX