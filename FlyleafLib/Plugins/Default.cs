using System;
using System.IO;
using System.Linq;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaStream;

namespace FlyleafLib.Plugins
{
    public unsafe class Default : PluginBase, IPluginVideo, IPluginAudio, IPluginSubtitles
    {
        public bool IsPlaylist => false;
        Session Session => Player.Session;

        public override void OnInitialized()
        {
            base.OnInitialized();
            defaultVideo = new VideoStream();
        }
        public override void OnInitializingSwitch()
        {
            base.OnInitializingSwitch();
            base.OnInitialized();
        }

        VideoStream defaultVideo;

        public override void OnVideoOpened()
        {
            // TODO add also external demuxers (add range)
            AudioStreams = Player.decoder.VideoDemuxer.AudioStreams;
            VideoStreams = Player.decoder.VideoDemuxer.VideoStreams;
            SubtitlesStreams = Player.decoder.VideoDemuxer.SubtitlesStreams;

            // if (!player.HasVideo) ... possible allow plugins to select embedded video?

            // Try to find best video stream based on current screen resolution
            var iresults =
                from    vstream in VideoStreams
                where   vstream.Type == MediaType.Video && vstream.Height <= Player.renderer.Info.ScreenBounds.Height
                orderby vstream.Height descending
                select  vstream;

            var results = iresults.ToList();

            if (results.Count != 0)
                Player.decoder.VideoDecoder.Open(iresults.ToList()[0]);
            else
            {
                // Fall-back to FFmpeg's default
                if (Player == null || Player.decoder == null || Player.decoder.VideoDemuxer.FormatContext == null) return; // Proper lock on format context*
                lock (Player.decoder.VideoDemuxer.lockFmtCtx)
                {
                    if (Player == null || Player.decoder == null || Player.decoder.VideoDemuxer.FormatContext == null) return; // Proper lock on format context*

                    // Let FFmpeg decide
                    int vstreamIndex = av_find_best_stream(Player.decoder.VideoDemuxer.FormatContext, AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
                    if (vstreamIndex < 0) return;

                    foreach(var vstream in VideoStreams)
                        if (vstream.StreamIndex == vstreamIndex)
                            { Player.decoder.VideoDecoder.Open(vstream); break; }
                }
            }
        }
        public OpenVideoResults OpenVideo()
        {
            // Fill Basic Info & Return the provided url

            try
            {
                Uri uri = new Uri(Session.InitialUrl);
                //Session.UrlScheme = uri.Scheme.ToLower();
                Session.SingleMovie.Url = Session.InitialUrl;

                if (File.Exists(Session.InitialUrl))
                {
                    Session.SingleMovie.UrlType = UrlType.File;

                    var fi = new FileInfo(Session.InitialUrl);
                    Session.SingleMovie.Title   = fi.Name;
                    Session.SingleMovie.Folder  = fi.DirectoryName;
                    Session.SingleMovie.FileSize= fi.Length;
                }
                else
                {
                    Session.SingleMovie.UrlType = UrlType.Other;
                    Session.SingleMovie.Title   = Session.InitialUrl;
                    Session.SingleMovie.Folder  = Path.GetTempPath();
                }
            }
            catch (Exception)
            {
                Session.SingleMovie.UrlType = UrlType.Other;
            }

            defaultVideo.Url = Session.InitialUrl;
            return new OpenVideoResults(defaultVideo);
        }
        public VideoStream OpenVideo(VideoStream stream) { return stream; }

        public AudioStream OpenAudio(AudioStream stream) 
        {
            if (stream.StreamIndex == -1) return null;

            foreach(var astream in AudioStreams)
                if (astream.StreamIndex == stream.StreamIndex) return astream;

            return null;
        }
        public AudioStream OpenAudio()
        {
            try
            {
                if (AudioStreams.Count == 0) return null;

                foreach(var lang in Player.Config.audio.Languages)
                    foreach(var stream in AudioStreams)
                        if (stream.Language == lang) return stream;

                // Fall-back to FFmpeg's default
                if (Player == null || Player.decoder == null || Player.decoder.VideoDemuxer.FormatContext == null) return null; // Proper lock on format context*
                lock (Player.decoder.VideoDemuxer.lockFmtCtx)
                {
                    if (Player == null || Player.decoder == null || Player.decoder.VideoDemuxer.FormatContext == null) return null; // Proper lock on format context*

                    int ret = av_find_best_stream(Player.decoder.VideoDemuxer.FormatContext, AVMEDIA_TYPE_AUDIO, -1, Player.decoder.VideoDecoder.Stream.StreamIndex, null, 0);
                    foreach(var stream in AudioStreams) if (stream.StreamIndex == ret) return stream;
                }
            } catch (Exception) { }

            return null;
        }

        public void Search(Language lang) { }
        public bool Download(SubtitlesStream stream) { return true; }
        public SubtitlesStream OpenSubtitles(Language lang)
        {
            foreach(var stream in SubtitlesStreams)
                if (lang == stream.Language) return stream;

            return null;
        }
        public SubtitlesStream OpenSubtitles(SubtitlesStream stream)
        {
            if (stream.StreamIndex == -1) return null;

            foreach(var sstream in SubtitlesStreams)
                if (sstream.StreamIndex == stream.StreamIndex) return sstream;

            return null;
        }
    }    
}