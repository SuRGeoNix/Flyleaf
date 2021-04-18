using System;
using System.IO;

using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins.MediaStream;

namespace FlyleafLib.Plugins
{
    public class ExternalSubs : PluginBase, IPluginExternalSubtitles
    {
        public bool Download(SubtitleStream stream) { return true; }

        public SubtitleStream OpenSubtitles(string url)
        {
            foreach(var stream in SubtitleStreams)
                if (stream.DecoderInput.Url == url || stream.Tag.ToString() == url) return stream;

            SubtitleStreams.Add(new SubtitleStream()
            {
                DecoderInput = new DecoderInput() { Url = url },
                Downloaded = true,
                Tag = url // Use it here because of possible convert to Utf8 and rename
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

        public void Search(Language lang) { }
    }
    public unsafe class Default : PluginBase, IPluginVideo, IPluginAudio, IPluginSubtitles
    {
        public bool IsPlaylist => false;

        Session Session => Player.Session;
        //Movie SingleMovie => Player.Session.SingleMovie;

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
            foreach(var stream in Player.decoder.demuxer.streams)
                if (stream.Type == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    AudioStreams.Add(new AudioStream()
                    {
                        DecoderInput = new DecoderInput() { StreamIndex = stream.StreamIndex },
                        BitRate = stream.BitRate,
                        Language = Language.Get(stream.Language),

                        SampleFormat = stream.SampleFormatStr,
                        SampleRate = stream.SampleRate,
                        Channels = stream.Channels,
                        Bits = stream.Bits
                    });
                }
                else if (stream.Type == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    VideoStream videoStream = new VideoStream();
                    VideoStream ptrVideoStream = stream.StreamIndex == Player.decoder.vDecoder.st->index ? defaultVideo : videoStream;

                    ptrVideoStream.DecoderInput = new DecoderInput() { StreamIndex = stream.StreamIndex };
                    ptrVideoStream.BitRate = stream.BitRate;
                    ptrVideoStream.Language = Language.Get(stream.Language);

                    ptrVideoStream.PixelFormat = stream.PixelFormatStr;
                    ptrVideoStream.Width = stream.Width;
                    ptrVideoStream.Height = stream.Height;
                    ptrVideoStream.FPS = stream.FPS;
                    VideoStreams.Add(ptrVideoStream);
                }
                else if (stream.Type == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                {
                    SubtitleStreams.Add(new SubtitleStream()
                    {
                        DecoderInput = new DecoderInput() { StreamIndex = stream.StreamIndex },
                        BitRate = stream.BitRate,
                        Language = Language.Get(stream.Language),

                        Downloaded = true,
                        Converted = true
                    });
                }

            defaultVideo.InUse = true;
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
                    Session.SingleMovie.Title = Session.InitialUrl;
                    Session.SingleMovie.Folder = Path.GetTempPath();
                }
            }
            catch (Exception)
            {
                Session.SingleMovie.UrlType = UrlType.Other;
            }

            defaultVideo.DecoderInput.Url = Session.InitialUrl;
            return new OpenVideoResults(defaultVideo);
        }

        public VideoStream OpenVideo(VideoStream stream) { return stream; }

        public AudioStream OpenAudio(AudioStream stream) 
        {
            if (stream.DecoderInput.StreamIndex == -1) return null;

            foreach(var astream in AudioStreams)
                if (astream.DecoderInput.StreamIndex == stream.DecoderInput.StreamIndex) return astream;

            return null;
        }

        public AudioStream OpenAudio()
        {
            if (AudioStreams.Count == 0) return null;

            foreach(var lang in Player.Config.audio.Languages)
                foreach(var stream in AudioStreams)
                    if (stream.Language == lang) return stream;

            // Fall-back to FFmpeg's default
            int ret = FFmpeg.AutoGen.ffmpeg.av_find_best_stream(Player.decoder.demuxer.fmtCtx, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO, -1, Player.decoder.vDecoder.st->index, null, 0);
            foreach(var stream in AudioStreams) if (stream.DecoderInput.StreamIndex == ret) return stream;

            return null;
        }

        public void Search(Language lang) { }

        public bool Download(SubtitleStream stream) { return true; }

        public SubtitleStream OpenSubtitles(Language lang)
        {
            foreach(var stream in SubtitleStreams)
                if (lang == stream.Language) return stream;

            return null;
        }

        public SubtitleStream OpenSubtitles(SubtitleStream stream)
        {
            if (stream.DecoderInput.StreamIndex == -1) return null;

            foreach(var sstream in SubtitleStreams)
                if (sstream.DecoderInput.StreamIndex == stream.DecoderInput.StreamIndex) return sstream;

            return null;
        }
    }    
}