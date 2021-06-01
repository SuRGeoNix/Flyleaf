using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaContext
{
    public unsafe class DecoderContext
    {
        public AudioDemuxer         AudioDemuxer        { get; private set; }
        public VideoDemuxer         VideoDemuxer        { get; private set; }
        public SubtitlesDemuxer     SubtitlesDemuxer    { get; private set; }

        public VideoDecoder         VideoDecoder        { get; private set; }
        public AudioDecoder         AudioDecoder        { get; private set; }
        public SubtitlesDecoder     SubtitlesDecoder    { get; private set; }
        public DecoderBase GetDecoderPtr(MediaType type) { return type == MediaType.Audio ? (DecoderBase)AudioDecoder : (type == MediaType.Video ? (DecoderBase)VideoDecoder : (DecoderBase)SubtitlesDecoder); }


        internal Player player;
        internal Config cfg => player.Config;

        public DecoderContext(Player player)
        {
            Master.RegisterFFmpeg();
            this.player = player;

            AudioDemuxer        = new AudioDemuxer(cfg, player.PlayerId);
            VideoDemuxer        = new VideoDemuxer(cfg, player.PlayerId);
            SubtitlesDemuxer    = new SubtitlesDemuxer(cfg, player.PlayerId);

            VideoDecoder        = new VideoDecoder(this);
            AudioDecoder        = new AudioDecoder(this);
            SubtitlesDecoder    = new SubtitlesDecoder(this);
        }

        public int Open(string url)
        {
            return VideoDemuxer.Open(url);
        }
        public int Open(Stream stream)
        {
            return VideoDemuxer.Open(stream);
        }
        public int Open(StreamBase stream)
        {
            Log($"[{stream.Type} #{stream.StreamIndex}] Opening on {stream.Demuxer.Type}");

            var decoderPtr = GetDecoderPtr(stream.Type);
            int ret = decoderPtr.Open(stream);
            if (ret == 0 && player.IsPlaying) decoderPtr.Start();

            return ret;
        }
        public int OpenAudio(string url, long ms = -1)
        {
            int ret = -1;

            if (VideoDemuxer.Status == MediaDemuxer.Status.Stopped) return ret;

            long openElapsedTicks = DateTime.UtcNow.Ticks;
            AudioDecoder.Stop();

            ret = AudioDemuxer.Open(url);
            if (ret != 0) return ret;
            if (AudioDemuxer.AudioStreams.Count == 0) return -1;

            ret = AudioDecoder.Open(AudioDemuxer.AudioStreams[0]);
            if (ret != 0) return ret;

            if (ms != -1) SeekAudio(player.IsPlaying ? ms + ((DateTime.UtcNow.Ticks - openElapsedTicks)/10000) : ms);

            return ret;
        }
        public int OpenSubs(string url, long ms = -1)
        {
            int ret = -1;

            if (VideoDemuxer.Status == MediaDemuxer.Status.Stopped) return ret;

            long openElapsedTicks = DateTime.UtcNow.Ticks;
            SubtitlesDecoder.Stop();

            ret = SubtitlesDemuxer.Open(url);
            if (ret != 0) return ret;
            if (SubtitlesDemuxer.SubtitlesStreams.Count == 0) return -1;

            ret = SubtitlesDecoder.Open(SubtitlesDemuxer.SubtitlesStreams[0]);
            if (ret != 0) return ret;

            if (ms != -1) SeekSubtitles(player.IsPlaying ? ms + ((DateTime.UtcNow.Ticks - openElapsedTicks)/10000) : ms);

            return ret;
        }
        

        public int Seek(long ms, bool foreward = false)
        {
            int ret;

            //Log($"[SEEK({(foreward ? "->" : "<-")})] Requested at {new TimeSpan(ms * (long)10000)}");

            lock (VideoDecoder.lockCodecCtx)
            lock (AudioDecoder.lockCodecCtx)
            lock (SubtitlesDecoder.lockCodecCtx)
            {
                ret = VideoDemuxer.Seek(CalcSeekTimestamp(VideoDemuxer, ms, ref foreward), foreward);
                VideoDecoder.Flush();
                if (AudioDecoder.OnVideoDemuxer) AudioDecoder.Flush();
                if (SubtitlesDecoder.OnVideoDemuxer) SubtitlesDecoder.Flush();
            }

            return ret;
        }
        public int SeekAudio(long ms, bool foreward = false)
        {
            int ret = -1;

            if (AudioDemuxer.Status == MediaDemuxer.Status.Stopped || AudioDecoder.Status == MediaDecoder.Status.Stopped || AudioDecoder.OnVideoDemuxer) return ret;

            lock (AudioDecoder.lockCodecCtx)
            {
                ret = AudioDemuxer.Seek(CalcSeekTimestamp(AudioDemuxer, ms, ref foreward), foreward);
                AudioDecoder.Flush();
            }

            if (player.IsPlaying)
            {
                AudioDemuxer.Start();
                AudioDecoder.Start();
            }

            return ret;
        }
        public int SeekSubtitles(long ms, bool foreward = false)
        {
            int ret = -1;

            if (SubtitlesDemuxer.Status == MediaDemuxer.Status.Stopped || SubtitlesDecoder.Status == MediaDecoder.Status.Stopped || SubtitlesDecoder.OnVideoDemuxer) return ret;

            lock (SubtitlesDecoder.lockCodecCtx)
            {
                ret = SubtitlesDemuxer.Seek(CalcSeekTimestamp(SubtitlesDemuxer, ms, ref foreward), foreward);
                SubtitlesDecoder.Flush();
            }

            if (player.IsPlaying)
            {
                SubtitlesDemuxer.Start();
                SubtitlesDecoder.Start();
            }

            return ret;
        }

        public void Pause()
        {
            // Start pausing all
            if (VideoDecoder.IsRunning) VideoDecoder.Status = MediaDecoder.Status.Pausing;
            if (AudioDecoder.IsRunning) AudioDecoder.Status = MediaDecoder.Status.Pausing;
            if (SubtitlesDecoder.IsRunning) SubtitlesDecoder.Status = MediaDecoder.Status.Pausing;
            if (VideoDemuxer.IsRunning) VideoDemuxer.Status = MediaDemuxer.Status.Pausing;
            if (AudioDemuxer.IsRunning) AudioDemuxer.Status = MediaDemuxer.Status.Pausing;
            if (SubtitlesDemuxer.IsRunning) SubtitlesDemuxer.Status = MediaDemuxer.Status.Pausing;

            VideoDecoder.Pause();
            AudioDecoder.Pause();
            SubtitlesDecoder.Pause();

            VideoDemuxer.Pause();
            AudioDemuxer.Pause();
            SubtitlesDemuxer.Pause();
        }
        public void Play()
        {
            VideoDemuxer.Start();
            AudioDemuxer.Start();
            SubtitlesDemuxer.Start();

            VideoDecoder.Start();
            AudioDecoder.Start();
            SubtitlesDecoder.Start();
        }
        public void Stop()
        {
            VideoDemuxer.DemuxInterrupt = 1;
            AudioDemuxer.DemuxInterrupt = 1;
            SubtitlesDemuxer.DemuxInterrupt = 1;

            VideoDemuxer.Stop();
            AudioDemuxer.Stop();
            SubtitlesDemuxer.Stop();
            VideoDecoder.Stop();
            AudioDecoder.Stop();
            SubtitlesDecoder.Stop();

            VideoDemuxer.DemuxInterrupt = 0;
            AudioDemuxer.DemuxInterrupt = 0;
            SubtitlesDemuxer.DemuxInterrupt = 0;
        }

        public long CalcSeekTimestamp(DemuxerBase demuxer, long ms, ref bool foreward)
        {
            long ticks = ((ms * 10000) + demuxer.StartTime);

            if (demuxer.Type == MediaType.Audio) ticks -= (cfg.audio.DelayTicks + cfg.audio.LatencyTicks);
            if (demuxer.Type == MediaType.Subs ) ticks -=  cfg.subs. DelayTicks;

            if (ticks < demuxer.StartTime) 
            {
                ticks = demuxer.StartTime;
                foreward = true;
            }
            else if (ticks >= demuxer.StartTime + VideoDemuxer.Duration) 
            {
                ticks = demuxer.StartTime + VideoDemuxer.Duration;
                foreward = false;
            }

            return ticks;
        }
        public long GetVideoFrame()
        {
            int ret;
            AVPacket* packet = av_packet_alloc();
            AVFrame* frame = av_frame_alloc();

            while (VideoDemuxer.Status != MediaDemuxer.Status.Stopped)
            {
                ret = av_read_frame(VideoDemuxer.FormatContext, packet);
                if (ret != 0) return -1;

                if (!VideoDemuxer.EnabledStreams.Contains(packet->stream_index)) { av_packet_unref(packet); continue; }

                switch (VideoDemuxer.FormatContext->streams[packet->stream_index]->codecpar->codec_type)
                {
                    case AVMEDIA_TYPE_AUDIO:
                        VideoDemuxer.AudioPackets.Enqueue((IntPtr)packet);
                        packet = av_packet_alloc();

                        continue;

                    case AVMEDIA_TYPE_SUBTITLE:
                        VideoDemuxer.SubtitlesPackets.Enqueue((IntPtr)packet);
                        packet = av_packet_alloc();

                        continue;

                    case AVMEDIA_TYPE_VIDEO:
                        ret = avcodec_send_packet(VideoDecoder.CodecCtx, packet);
                        av_packet_free(&packet);
                        packet = av_packet_alloc();

                        if (ret != 0) return -1;
                        
                        while (VideoDecoder.Status != MediaDecoder.Status.Stopped)
                        {
                            ret = avcodec_receive_frame(VideoDecoder.CodecCtx, frame);
                            if (ret != 0) { av_frame_unref(frame); break; }

                            if (frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                            {
                                Log($"Invalid Seek to Keyframe, skip... {frame->pict_type} | {frame->key_frame}");
                            }
                            else
                            {
                                VideoFrame mFrame = VideoDecoder.ProcessVideoFrame(frame);
                                if (mFrame != null)
                                {
                                    Log(Utils.TicksToTime((long)(mFrame.pts * VideoDecoder.VideoStream.Timebase)) + " | pts -> " + mFrame.pts);
                                    VideoDecoder.Frames.Enqueue(mFrame);
                                    av_frame_unref(frame);
                                    VideoDecoder.keyFrameRequired = false;

                                    return mFrame.timestamp;
                                }
                            }

                            av_frame_unref(frame);
                        }

                        break; // Switch break

                } // Switch

            } // While

            av_packet_free(&packet);
            return -1;
        }

        public void PrintStats()
        {
            string dump = "\r\n-===== Streams / Packets / Frames =====-\r\n";
            dump += $"\r\n AudioPackets      ({VideoDemuxer.AudioStreams.Count}): {VideoDemuxer.AudioPackets.Count}";
            dump += $"\r\n VideoPackets      ({VideoDemuxer.VideoStreams.Count}): {VideoDemuxer.VideoPackets.Count}";
            dump += $"\r\n SubtitlesPackets  ({VideoDemuxer.SubtitlesStreams.Count}): {VideoDemuxer.SubtitlesPackets.Count}";
            dump += $"\r\n AudioPackets      ({AudioDemuxer.AudioStreams.Count}): {AudioDemuxer.AudioPackets.Count} (AudioDemuxer)";
            dump += $"\r\n SubtitlesPackets  ({SubtitlesDemuxer.SubtitlesStreams.Count}): {SubtitlesDemuxer.SubtitlesPackets.Count} (SubtitlesDemuxer)";

            dump += $"\r\n Video Frames         : {VideoDecoder.Frames.Count}";
            dump += $"\r\n Audio Frames         : {AudioDecoder.Frames.Count}";
            dump += $"\r\n Subtitles Frames     : {SubtitlesDecoder.Frames.Count}";

            Log(dump);
        }

        public void Dispose()
        {
            VideoDecoder.VideoAcceleration.Dispose();
        }

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{player.PlayerId}] [DecoderContext] {msg}"); }
    }
}