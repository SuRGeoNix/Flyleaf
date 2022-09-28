using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;
using System;
using System.IO;

namespace PumpkinSubtitle
{
    public class PumpkinSubtitlePlugin : PluginBase, IOpenSubtitles, IDownloadSubtitles
    {
        public override void OnLoaded()
        {
            base.OnLoaded();
        }
        public override void OnOpen()
        {
            base.OnOpen();
        }

        public override void OnOpenExternalSubtitles()
        {
            base.OnOpenExternalSubtitles();

        }

        bool IDownloadSubtitles.DownloadSubtitles(ExternalSubtitlesStream extStream)
        {
            var url = extStream.Url;

            return true;
        }

        OpenSubtitlesResults IOpenSubtitles.Open(string url)
        {
            if (Selected != null && Selected.ExternalSubtitlesStreams != null)
            {
                foreach (var extStream in Selected.ExternalSubtitlesStreams)
                    if (extStream.Url == url || extStream.DirectUrl == url)
                        return new OpenSubtitlesResults(extStream);
            }
            string title;

            try
            {
                var fi = new FileInfo(Playlist.Url);
                title = fi.Extension == null ? fi.Name : fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
            }
            catch { title = url; }
            ExternalSubtitlesStream newExtStream = new ExternalSubtitlesStream()
            {
                Url = url,
                Title = title,
                Downloaded = true,
            };

            AddExternalStream(newExtStream);
            return new OpenSubtitlesResults(newExtStream);
        }

        OpenSubtitlesResults IOpenSubtitles.Open(Stream iostream)
        {
            return null;
        }
    }
}
