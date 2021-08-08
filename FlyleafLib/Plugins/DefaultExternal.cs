using System.IO;

using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins
{
    public class DefaultExternal : PluginBase, IPluginExternal
    {
        public bool IsPlaylist => false;

        public bool Download(SubtitlesStream stream) { return true; }

        public SubtitlesStream OpenSubtitles(string url)
        {
            foreach(var stream in SubtitlesStreams)
                if (stream.Url == url || stream.Tag.ToString() == url) return stream;

            SubtitlesStreams.Add(new SubtitlesStream()
            {
                Url = url,
                Downloaded  = true,
                Tag         = url // Use it here because of possible convert to Utf8 and rename
            });

            return SubtitlesStreams[SubtitlesStreams.Count - 1];
        }
        public SubtitlesStream OpenSubtitles(SubtitlesStream stream)
        {
            foreach(var sstream in SubtitlesStreams)
                if (sstream.Tag == stream.Tag) return stream;

            return null;
        }
        public SubtitlesStream OpenSubtitles(Language lang) { return null; }

        public VideoStream OpenVideo(Stream stream)
        {
            VideoStreams.Add(new VideoStream() { Stream = stream });
            return VideoStreams[VideoStreams.Count - 1];
        }
        public OpenVideoResults OpenVideo() { return null; }
        public VideoStream OpenVideo(VideoStream stream) { return stream; }

        public void Search(Language lang) { }
    }
}