using System;
using System.IO;
using System.Windows.Forms;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaFramework.MediaContext
{
    public unsafe class DecoderContext
    {
        public int                  UniqueId            { get; set; }
        public Config               Config              { get; private set; }

        // Demuxers
        public Demuxer              AudioDemuxer        { get; private set; }
        public Demuxer              VideoDemuxer        { get; private set; }
        public Demuxer              SubtitlesDemuxer    { get; private set; }

        // Decoders
        public VideoDecoder         VideoDecoder        { get; private set; }
        public AudioDecoder         AudioDecoder        { get; private set; }
        public SubtitlesDecoder     SubtitlesDecoder    { get; private set; }
        public DecoderBase GetDecoderPtr(MediaType type){ return type == MediaType.Audio ? (DecoderBase)AudioDecoder : (type == MediaType.Video ? (DecoderBase)VideoDecoder : (DecoderBase)SubtitlesDecoder); }

        public DecoderContext(Config config = null, Control control = null, int uniqueId = -1)
        {
            Master.RegisterFFmpeg();
            Config  = config == null ? new Config() : config;
            UniqueId= uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;

            AudioDemuxer        = new Demuxer(Config.demuxer, MediaType.Audio, uniqueId);
            VideoDemuxer        = new Demuxer(Config.demuxer, MediaType.Video, uniqueId);
            SubtitlesDemuxer    = new Demuxer(Config.demuxer, MediaType.Subs,  uniqueId);

            VideoDecoder        = new VideoDecoder(Config, control, uniqueId);
            AudioDecoder        = new AudioDecoder(Config, uniqueId);
            SubtitlesDecoder    = new SubtitlesDecoder(Config, uniqueId);
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

            bool wasRunning = VideoDecoder.IsRunning;

            var decoderPtr = GetDecoderPtr(stream.Type);
            int ret = decoderPtr.Open(stream);

            if (wasRunning)
            {
                //decoderPtr.Demuxer.Start();
                decoderPtr.Start();
            }

            return ret;
        }
        public int OpenAudio(string url, long ms = -1)
        {
            int ret = -1;

            if (VideoDemuxer.Disposed) return ret;

            long openElapsedTicks = DateTime.UtcNow.Ticks;
            AudioDecoder.Stop();

            ret = AudioDemuxer.Open(url);
            if (ret != 0) return ret;
            if (AudioDemuxer.AudioStreams.Count == 0) return -1;

            ret = AudioDecoder.Open(AudioDemuxer.AudioStreams[0]);
            if (ret != 0) return ret;

            if (ms != -1) SeekAudio(VideoDecoder.IsRunning ? ms + ((DateTime.UtcNow.Ticks - openElapsedTicks)/10000) : ms);

            return ret;
        }
        public int OpenSubs(string url, long ms = -1)
        {
            int ret = -1;

            if (VideoDemuxer.Disposed) return ret;

            long openElapsedTicks = DateTime.UtcNow.Ticks;
            SubtitlesDecoder.Stop();

            ret = SubtitlesDemuxer.Open(url);
            if (ret != 0) return ret;
            if (SubtitlesDemuxer.SubtitlesStreams.Count == 0) return -1;

            ret = SubtitlesDecoder.Open(SubtitlesDemuxer.SubtitlesStreams[0]);
            if (ret != 0) return ret;

            if (ms != -1) SeekSubtitles(VideoDecoder.IsRunning ? ms + ((DateTime.UtcNow.Ticks - openElapsedTicks)/10000) : ms);

            return ret;
        }
        
        public void Flush()
        {
            bool wasRunning = VideoDecoder.IsRunning;
            Pause();
            
            VideoDemuxer.DisposePackets();
            AudioDemuxer.DisposePackets();
            SubtitlesDemuxer.DisposePackets();

            VideoDecoder.Flush();
            AudioDecoder.Flush();
            SubtitlesDecoder.Flush();

            if (wasRunning) Play();
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

            if (AudioDemuxer.Disposed || AudioDecoder.OnVideoDemuxer) return ret;

            lock (AudioDecoder.lockCodecCtx)
            {
                ret = AudioDemuxer.Seek(CalcSeekTimestamp(AudioDemuxer, ms, ref foreward), foreward);
                AudioDecoder.Flush();
            }

            if (VideoDecoder.IsRunning)
            {
                AudioDemuxer.Start();
                AudioDecoder.Start();
            }

            return ret;
        }
        public int SeekSubtitles(long ms, bool foreward = false)
        {
            int ret = -1;

            if (SubtitlesDemuxer.Disposed || SubtitlesDecoder.OnVideoDemuxer) return ret;

            lock (SubtitlesDecoder.lockCodecCtx)
            {
                ret = SubtitlesDemuxer.Seek(CalcSeekTimestamp(SubtitlesDemuxer, ms, ref foreward), foreward);
                SubtitlesDecoder.Flush();
            }

            if (VideoDecoder.IsRunning)
            {
                SubtitlesDemuxer.Start();
                SubtitlesDecoder.Start();
            }

            return ret;
        }

        public void Pause()
        {
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
            VideoDemuxer.Interrupter.ForceInterrupt = 1;
            AudioDemuxer.Interrupter.ForceInterrupt = 1;
            SubtitlesDemuxer.Interrupter.ForceInterrupt = 1;

            VideoDecoder.Dispose();
            AudioDecoder.Dispose();
            SubtitlesDecoder.Dispose();
            AudioDemuxer.Dispose();
            SubtitlesDemuxer.Dispose();
            VideoDemuxer.Dispose();

            VideoDemuxer.Interrupter.ForceInterrupt = 0;
            AudioDemuxer.Interrupter.ForceInterrupt = 0;
            SubtitlesDemuxer.Interrupter.ForceInterrupt = 0;
        }

        public long CalcSeekTimestamp(Demuxer demuxer, long ms, ref bool foreward)
        {
            long startTime = demuxer.hlsCtx == null ? demuxer.StartTime : demuxer.hlsCtx->first_timestamp * 10;
            long ticks = (ms * 10000) + startTime;

            if (demuxer.Type == MediaType.Audio) ticks -= Config.audio.DelayTicks + Config.audio.LatencyTicks;
            if (demuxer.Type == MediaType.Subs ) ticks -= Config.subs. DelayTicks;

            if (ticks < startTime) 
            {
                ticks = startTime;
                foreward = true;
            }
            else if (ticks >= startTime + VideoDemuxer.Duration) 
            {
                ticks = startTime + demuxer.Duration - 100;
                foreward = false;
            }

            return ticks;
        }
        public long GetVideoFrame()
        {
            int ret;
            AVPacket* packet = av_packet_alloc();
            AVFrame* frame = av_frame_alloc();

            while (!VideoDemuxer.Disposed)
            {
                VideoDemuxer.Interrupter.Request(Requester.Read);
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
                        
                        while (!VideoDemuxer.Disposed)
                        {
                            ret = avcodec_receive_frame(VideoDecoder.CodecCtx, frame);
                            if (ret != 0) { av_frame_unref(frame); break; }

                            if (frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                            {
                                Log($"Invalid Seek to Keyframe, skip... {frame->pict_type} | {frame->key_frame}");
                                av_frame_unref(frame);
                                continue;
                            }
                            VideoFrame mFrame = VideoDecoder.ProcessVideoFrame(frame);
                            if (mFrame != null)
                            {
                                Log(Utils.TicksToTime((long)(mFrame.pts * VideoDecoder.VideoStream.Timebase)) + " | pts -> " + mFrame.pts);
                                VideoDecoder.Frames.Enqueue(mFrame);
                                VideoDecoder.keyFrameRequired = false;

                                av_frame_free(&frame);
                                return mFrame.timestamp;
                            }
                        }

                        break; // Switch break

                } // Switch

            } // While

            av_packet_free(&packet);
            av_frame_free(&frame);
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
            VideoDecoder.Dispose();
            VideoDecoder.DisposeVA();
            AudioDecoder.Dispose();
            SubtitlesDecoder.Dispose();
            AudioDemuxer.Dispose();
            SubtitlesDemuxer.Dispose();
            VideoDemuxer.Dispose();
        }

        private void Log(string msg) { System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [DecoderContext] {msg}"); }
    }
}