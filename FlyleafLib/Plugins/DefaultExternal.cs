using System.IO;

using FlyleafLib.Plugins.MediaStream;

namespace FlyleafLib.Plugins
{
    public class DefaultExternal : PluginBase, IPluginExternal
    {
        public bool IsPlaylist => false;

        public bool Download(SubtitleStream stream) { return true; }

        public SubtitleStream OpenSubtitles(string url)
        {
            foreach(var stream in SubtitleStreams)
                if (stream.DecoderInput.Url == url || stream.Tag.ToString() == url) return stream;

            SubtitleStreams.Add(new SubtitleStream()
            {
                DecoderInput= new DecoderInput() { Url = url },
                Downloaded  = true,
                Tag         = url // Use it here because of possible convert to Utf8 and rename
            });

            return SubtitleStreams[SubtitleStreams.Count - 1];
        }
        public SubtitleStream OpenSubtitles(SubtitleStream stream)
        {
            foreach(var sstream in SubtitleStreams)
                if (sstream.Tag == stream.Tag) return stream;

            return null;
        }
        public SubtitleStream OpenSubtitles(Language lang) { return null; }

        public VideoStream OpenVideo(Stream stream)
        {
            VideoStreams.Add(new VideoStream() { DecoderInput = new DecoderInput() { Stream = stream } });
            return VideoStreams[VideoStreams.Count - 1];
        }
        public OpenVideoResults OpenVideo() { return null; }
        public VideoStream OpenVideo(VideoStream stream) { return stream; }

        public void Search(Language lang) { }
    }
}
