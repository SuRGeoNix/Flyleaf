using System;

namespace FlyleafLib.Plugins.MediaStream
{
    public class VideoStream : StreamBase
    {
        /// <summary>
        /// Stream's Movie info from the current plugin's Playlist
        /// </summary>
        public Movie    Movie       { get; set; } = new Movie();
        public string   PixelFormat { get; set; }
        public int      Width       { get; set; }
        public int      Height      { get; set; }
        public double   FPS         { get; set; }

        public virtual string GetDump() { return $"[Video] {CodecName} {Width}x{Height} @ {FPS.ToString("#.###")} | {BitRate}"; }
    }
}