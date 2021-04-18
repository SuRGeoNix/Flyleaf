using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;

using FlyleafLib.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.MediaFramework
{
    public unsafe class DecoderContext
    {
        public Status   status;
        public Decoder  aDecoder, vDecoder, sDecoder;
        public Demuxer  demuxer, aDemuxer, sDemuxer;
        public VideoAcceleration va;

        public const int    SCALING_HQ     = SWS_ACCURATE_RND | SWS_BITEXACT | SWS_LANCZOS | SWS_FULL_CHR_H_INT | SWS_FULL_CHR_H_INP;
        public const int    SCALING_LQ     = SWS_BICUBIC;

        //public bool Finished    { get { if (vDecoder != null && vDecoder.status == Status.Ended) return true; else return false; } }
        public bool isRunning   { get { if (vDecoder.status == Status.Playing || aDecoder.status == Status.Playing || sDecoder.status == Status.Playing) return true; else return false;} }
        public bool isPlaying   => status == Status.Playing;

        internal Player         player;
        internal AudioPlayer    audioPlayer;
        internal Renderer       renderer;
        internal Config         cfg;
        internal IntPtr         decCtxPtr;
        public   int            interrupt;

        public DecoderContext(Player player, AudioPlayer audioPlayer)
        {
            this.player      = player;
            this.audioPlayer = audioPlayer;
        }
        public void Init(Renderer renderer, Config cfg)
        {   
            this.renderer   = renderer;
            this.cfg        = cfg;
            status          = Status.None;

            Utils.FFmpeg.RegisterFFmpeg();

            vDecoder    = new Decoder(MediaType.Video, this);
            aDecoder    = new Decoder(MediaType.Audio, this);
            sDecoder    = new Decoder(MediaType.Subs , this);

            demuxer     = new Demuxer(MediaType.Video, this);
            aDemuxer    = new Demuxer(MediaType.Audio, this);
            sDemuxer    = new Demuxer(MediaType.Subs , this);

            va = new VideoAcceleration();
            if (cfg.decoder.HWAcceleration) va.Init(renderer.device);

            GCHandle decCtxHandle = GCHandle.Alloc(this);
            decCtxPtr = (IntPtr) decCtxHandle;
        }

        public int Open(Stream stream, bool doAudio = false, bool doSubs = false)
        {
            Pause();

            int ret;

            ret = demuxer.Open(null, doAudio, doSubs, stream); // No subs, we choose later
            if (ret != 0) return ret;

            return 0;
        }
        public int Open(string url, bool doAudio = false, bool doSubs = false)
        {
            Pause();

            int ret;

            ret = demuxer.Open(url, doAudio, doSubs); // No subs, we choose later
            if (ret != 0) return ret;

            return 0;
        }

        public int ReOpen()
        {
            int ret;
            int sStreamIndex = sDecoder.isEmbedded && sDecoder.status != Status.None && sDecoder.st != null ? sDecoder.st->index : -1;
            int aStreamIndex = aDecoder.isEmbedded && aDecoder.status != Status.None && aDecoder.st != null ? aDecoder.st->index : -1;

            ret = demuxer.Open(demuxer.url, false, false, demuxer.ioStream, false);
            if (ret != 0) return ret;

            if (aStreamIndex != -1 && cfg.audio.Enabled) OpenAudio(aStreamIndex);
            if (sStreamIndex != -1 && cfg.subs.Enabled)  OpenSubs(sStreamIndex);
            if (aStreamIndex == -1 && aDemuxer.status != Status.None && cfg.audio.Enabled) OpenAudio(aDemuxer.url);
            // Where is sDemuxer?

            return 0;
        }

        public int OpenVideo(int streamIndex, bool doAudio = false)
        {
            if (demuxer.status == Status.None) return - 1;
            if (streamIndex < 0 || streamIndex >= demuxer.fmtCtx->nb_streams) return -1;

            if (vDecoder.Open(demuxer, demuxer.fmtCtx->streams[streamIndex]) < 0) return -1;

            if (doAudio)
            {
                int aStreamIndex = av_find_best_stream(demuxer.fmtCtx, AVMEDIA_TYPE_AUDIO, -1, streamIndex, null, 0);
                if (aStreamIndex >= 0) OpenAudio(aStreamIndex);
            }

            if (isRunning) vDecoder.decodeARE.Set();

            return 0;
        }
        public int OpenAudio(int streamIndex)//, long ms = -1)
        {
            int ret = -1;

            if (demuxer.status == Status.None) return ret;
            if (streamIndex < 0 || streamIndex >= demuxer.fmtCtx->nb_streams) return -1;

            StopAudio();
            ret = aDecoder.Open(demuxer, demuxer.fmtCtx->streams[streamIndex]);
            if (isRunning) aDecoder.decodeARE.Set();

            return ret;
        }
        public int OpenAudio(string url, long ms = -1, bool addDelay = false)
        {
            int ret = -1;
            if (demuxer.status == Status.None) return ret;

            StopAudio();
            long openElapsedTicks = DateTime.UtcNow.Ticks;
            ret = aDemuxer.Open(url);
            if (ms != -1) aDemuxer.ReSync(!addDelay ? ms : ms + ((DateTime.UtcNow.Ticks - openElapsedTicks)/10000));

            return ret;
        }
        public int OpenSubs(int streamIndex)
        {
            int ret = -1;

            if (demuxer.status == Status.None) return -1;
            if (streamIndex < 0 || streamIndex >= demuxer.fmtCtx->nb_streams) return -1;

            StopSubs();

            ret = sDecoder.Open(demuxer, demuxer.fmtCtx->streams[streamIndex]);
            if (isRunning) sDecoder.decodeARE.Set();

            return ret;
        }
        public int OpenSubs(string url, long ms)
        {
            int ret = -1;

            if (demuxer.status == Status.None) return ret;

            StopSubs();

            ret = sDemuxer.Open(url);
            sDemuxer.ReSync(ms);

            return ret;
        }

        public void Pause()
        {
            status = Status.Paused;
            interrupt = 1;
            int loops = 0;
            while (demuxer.status == Status.Playing || aDemuxer.status == Status.Playing || sDemuxer.status == Status.Playing || aDecoder.status == Status.Playing || vDecoder.status == Status.Playing || sDecoder.status == Status.Playing)
            {
                loops++;
                Thread.Sleep(10);

                if (loops > 10) demuxer.RestartDemuxThread();
            }
            interrupt = 0;
        }

        public void Stop()
        {
            Pause();
            demuxer.Close();
        }
        public void StopAudio()
        {
            if (aDecoder.isEmbedded)
                aDecoder.Close();
            else
                { aDemuxer.Pause(); aDemuxer.Close(); }
        }
        public void StopSubs()
        {
            if (sDecoder.isEmbedded)
                sDecoder.Close();
            else
                { sDemuxer.Pause(); sDemuxer.Close(); }
        }

        public void Play()
        {
            if (demuxer.status != Status.Paused && demuxer.status != Status.Ended) return;

            status = Status.Playing;
            aDecoder.decodeARE.Set();
            vDecoder.decodeARE.Set();
            sDecoder.decodeARE.Set();

            while (status == Status.Playing && demuxer.status != Status.Playing && demuxer.status != Status.Ended) Thread.Sleep(1);
        }

        public int Seek(long ms, bool foreward = false)//, bool seek2any = false)
        {
            int ret = 0;

            // Seeks at AVS Key Frame -> Seeks Externals at Key Frame time backwards (ensures audio/subs external frames before key frame)
            Log($"[SEEK] {(foreward ? "F" : "B")} | {Utils.TicksToTime(ms * 10000)} (Request)");
            Pause();
            //if (demuxer.streams[vDecoder.st->index].DurationTicks == 0) { Log("[SEEK] Live Stream!"); return 0; }
            ret = demuxer.Seek(ms, foreward);
            if (ret < 0) { Log("[SEEK] Failed 1"); return ret; }
            vDecoder.Flush();
            if (aDecoder.isEmbedded) aDecoder.Flush();
            if (sDecoder.isEmbedded) sDecoder.Flush();

            long ts = GetVideoFrame();
            if (ts < 0) { Log("[SEEK] Failed 2"); return -1; }
            Log("[SEEK] " + Utils.TicksToTime(ts) + " (Actual)");

            if (!aDecoder.isEmbedded)
            {
                aDemuxer.Seek(ts / 10000);
                aDecoder.Flush();
            }
                
            if (!sDecoder.isEmbedded)
            {
                sDemuxer.Seek(ts / 10000);
                sDecoder.Flush();
            }

            return ret;
        }

        public long GetVideoFrame()
        {
            int ret;
            long firstTs = -1;

            while (interrupt != 1)
            {
                AVPacket* pkt = av_packet_alloc();
                ret = av_read_frame(demuxer.fmtCtx, pkt);
                if (ret != 0) return -1;

                if (!demuxer.enabledStreams.Contains(pkt->stream_index))
                {
                    av_packet_free(&pkt);   
                    continue;
                }

                switch (demuxer.fmtCtx->streams[pkt->stream_index]->codecpar->codec_type)
                {
                    case AVMEDIA_TYPE_AUDIO:
                        aDecoder.packets.Enqueue((IntPtr)pkt);

                        break;

                    case AVMEDIA_TYPE_VIDEO:
                        ret = avcodec_send_packet(vDecoder.codecCtx, pkt);
                        av_packet_free(&pkt);

                        if (ret != 0) return -1;
                        
                        while (interrupt != 1)
                        {
                            AVFrame* frame = av_frame_alloc();
                            ret = avcodec_receive_frame(vDecoder.codecCtx, frame);
                            
                            if (ret == 0)
                            {
                                MediaFrame mFrame = new MediaFrame();
                                mFrame.pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                                mFrame.timestamp = ((long)(mFrame.pts * vDecoder.info.Timebase) - vDecoder.info.StartTime) + cfg.audio.LatencyTicks;

                                if (mFrame.pts == AV_NOPTS_VALUE || frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                                {
                                    if (frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I) Log($"Invalid Seek to Keyframe, skip... {frame->pict_type} | {frame->key_frame.ToString()}");
                                    av_frame_free(&frame);
                                    continue;
                                }

                                if (firstTs == -1)
                                {
                                    if (vDecoder.hwAccelSuccess && frame->hw_frames_ctx == null)
                                    {
                                        Log("HW Acceleration Failed 2");
                                        vDecoder.hwAccelSuccess = false;
                                        renderer.FrameResized();
                                    }
                                    if (vDecoder.hwAccelSuccess && frame->hw_frames_ctx == null) vDecoder.hwAccelSuccess = false;
                                    firstTs = mFrame.timestamp;
                                }

                                if (MediaFrame.ProcessVideoFrame(vDecoder, mFrame, frame) != 0) mFrame = null;
                                if (mFrame != null) vDecoder.frames.Enqueue(mFrame);

                                //Log(Utils.TicksToTime((long)(mFrame.pts * avs.streams[video.st->index].timebase)));

                                av_frame_free(&frame);
                                continue;
                            }

                            av_frame_free(&frame);
                            break;
                        }

                        break;

                    case AVMEDIA_TYPE_SUBTITLE:
                        sDecoder.packets.Enqueue((IntPtr)pkt);

                        break;

                    default:
                        av_packet_free(&pkt);
                        break;
                }

                if (firstTs != -1) break;
            }
            
            return firstTs;
        }

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{player.PlayerId}] [DecoderContext] {msg}"); }
    }
}