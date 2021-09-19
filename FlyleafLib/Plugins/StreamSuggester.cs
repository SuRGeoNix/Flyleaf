using System;
using System.Collections.Generic;
using System.Linq;

using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins
{
    public unsafe class StreamSuggester : PluginBase, ISuggestAudioStream, ISuggestVideoStream, ISuggestSubtitlesStream
    {
        public new int Priority { get; set; } = 3000;

        public AudioStream SuggestAudio(List<AudioStream> streams)
        {
            lock (streams[0].Demuxer.lockActions)
            {
                foreach (var lang in Config.Audio.Languages)
                    foreach (var stream in streams)
                        if (stream.Language == lang)
                        {
                            if (stream.Demuxer.Programs.Length < 2)
                            {
                                Log($"[Audio] based on language");
                                return stream;
                            }

                            for (int i = 0; i < stream.Demuxer.Programs.Length; i++)
                            {
                                bool aExists = false, vExists = false;
                                foreach (int pstream in stream.Demuxer.Programs[i])
                                {
                                    if (pstream == stream.StreamIndex) aExists = true;
                                    else if (pstream == stream.Demuxer.VideoStream?.StreamIndex) vExists = true;
                                }

                                if (aExists && vExists)
                                {
                                    Log($"[Audio] based on language and same program #{i}");
                                    return stream;
                                }
                            }
                        }

                // Fall-back to FFmpeg's default
                int streamIndex;
                lock (streams[0].Demuxer.lockFmtCtx)
                    streamIndex = av_find_best_stream(streams[0].Demuxer.FormatContext, AVMEDIA_TYPE_AUDIO, -1, streams[0].Demuxer.VideoStream != null ? streams[0].Demuxer.VideoStream.StreamIndex : -1, null, 0);

                foreach (var stream in streams)
                    if (stream.StreamIndex == streamIndex)
                    {
                        Log($"[Audio] based on av_find_best_stream");
                        return stream;
                    }

                return null;
            }
        }

        public VideoStream SuggestVideo(List<VideoStream> streams)
        {
            // Try to find best video stream based on current screen resolution
            var iresults =
                from vstream in streams
                where vstream.Type == MediaType.Video && vstream.Height <= Config.Video.MaxVerticalResolution //Decoder.VideoDecoder.Renderer.Info.ScreenBounds.Height
                orderby vstream.Height descending
                select vstream;

            var results = iresults.ToList();

            if (results.Count != 0)
                return iresults.ToList()[0];
            else
            {
                // Fall-back to FFmpeg's default
                int streamIndex;
                lock (streams[0].Demuxer.lockFmtCtx)
                    streamIndex = av_find_best_stream(streams[0].Demuxer.FormatContext, AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
                if (streamIndex < 0) return null;

                foreach (var vstream in streams)
                    if (vstream.StreamIndex == streamIndex)
                        return vstream;
            }

            return null;
        }

        public SubtitlesStream SuggestSubtitles(List<SubtitlesStream> streams, Language lang)
        {
            foreach(var stream in streams)
                if (lang == stream.Language) return stream;

            return null;
        }
    }
}