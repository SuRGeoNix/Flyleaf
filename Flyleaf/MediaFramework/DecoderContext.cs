using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.AVCodecID;

using SharpDX.Direct3D11;

namespace SuRGeoNix.Flyleaf.MediaFramework
{
    public class Options
    {
        public Demuxer  demuxer = new Demuxer();
        public Video    video   = new Video();
        public Audio    audio   = new Audio();
        public Subs     subs    = new Subs();

        public class Demuxer
        {
            public int              MinQueueSize    { get; set; } = 10;
            public int              MaxQueueSize    { get; set; } = 100;
        }

        public class Video
        {
            public AVPixelFormat    PixelFormat     { get; set; } = AVPixelFormat.AV_PIX_FMT_RGBA;
            public bool             HWAcceleration  { get; set; } = true;
            public int              DecoderThreads  { get; set; } = Environment.ProcessorCount;
            public bool             SwsHighQuality  { get; set; } = false;
        }
        public class Audio
        {
            public bool             Enabled         { get; set; } = true;
            public long             LatencyTicks    { get; set; } = AudioPlayer.NAUDIO_DELAY_MS * 10000;
            public long             DelayTicks      { get; set; } = 0;
            public AVSampleFormat   SampleFormat    { get; internal set; } = AVSampleFormat.AV_SAMPLE_FMT_FLT;
            public int              SampleRate      { get; internal set; }
            public int              ChannelLayout   { get; internal set; } = AV_CH_LAYOUT_STEREO;
            public int              Channels        { get; internal set; }
            public int              Bits            { get; internal set; }
        }

        public class Subs
        {
            public bool             Enabled         { get; set; } = true;
            public long             DelayTicks      { get; set; } = 0;
        }
    }
    public enum Status
    {
        NOTSET,
        READY,
        PAUSE,
        PLAY,
        END,
        STOP
    }

    public enum Type
    {
        Video,
        Audio,
        Subs
    }
    public unsafe class DecoderContext
    {
        public const int    SCALING_HQ     = SWS_ACCURATE_RND | SWS_BITEXACT | SWS_LANCZOS | SWS_FULL_CHR_H_INT | SWS_FULL_CHR_H_INP;
        public const int    SCALING_LQ     = SWS_BICUBIC;

        public bool hasSubs     { get { if (sDecoder != null && sDecoder.status != Status.NOTSET) return true; else return false; } }
        public bool hasAudio    { get { if (aDecoder != null && aDecoder.status != Status.NOTSET) return true; else return false; } }
        public bool Finished    { get { if (vDecoder != null && vDecoder.status == Status.END) return true; else return false; } }
        public bool isRunning   { get { if (vDecoder.status == Status.PLAY || aDecoder.status == Status.PLAY || sDecoder.status == Status.PLAY) return true; else return false;} }

        public string Referer   { get; set; } // Temporary to allow more Youtube-dl urls

        public StreamInfo vStreamInfo
        {
            get
            {
                if (demuxer == null || demuxer.streams == null || vDecoder.st == null || demuxer.streams.Length < vDecoder.st->index) return new StreamInfo();
                return demuxer.streams[vDecoder.st->index];
            }
        }

        public StreamInfo aStreamInfo
        {
            get
            {
                if (demuxer == null || demuxer.streams == null || aDecoder.st == null || demuxer.streams.Length < aDecoder.st->index) return new StreamInfo();
                return demuxer.streams[aDecoder.st->index];
            }
        }

        public Options opt;
        public Device device;
        public Status status;
        public Decoder aDecoder, vDecoder, sDecoder;
        public Demuxer demuxer, aDemuxer, sDemuxer;
        public VideoAcceleration va;

        internal int interrupt;

        public DecoderContext(Options opt =  null) { this.opt = opt != null ? opt : new Options(); }

        public IntPtr decCtxPtr;
        public void Init(Device device)
        {
            Utils.RegisterFFmpegBinaries();
            //av_log_set_level(AV_LOG_MAX_OFFSET);
            av_log_set_level(AV_LOG_WARNING);
            av_log_set_callback(Utils.ffmpegLogCallback);

            vDecoder    = new Decoder(Type.Video, this);
            aDecoder    = new Decoder(Type.Audio, this);
            sDecoder    = new Decoder(Type.Subs , this);

            demuxer     = new Demuxer(Type.Video, this);
            aDemuxer    = new Demuxer(Type.Audio, this);
            sDemuxer    = new Demuxer(Type.Subs , this);

            va = new VideoAcceleration();
            if (opt.video.HWAcceleration) va.Init(device);
            this.device = device;

            status = Status.NOTSET;

            GCHandle decCtxHandle = GCHandle.Alloc(this);
            decCtxPtr = (IntPtr) decCtxHandle;

            var t1 = av_cpu_count();
        }

        public int Open(Stream stream)
        {
            Pause();

            int ret;

            ret = demuxer.Open(null, opt.audio.Enabled, false, stream); // No subs, we choose later
            if (ret != 0) return ret;

            return 0;
        }
        public int Open(string url)
        {
            Pause();

            int ret;

            ret = demuxer.Open(url, opt.audio.Enabled, false); // No subs, we choose later
            if (ret != 0) return ret;

            return 0;
        }

        public int ReOpen()
        {
            int ret;

            long saveADelay     = opt.audio.DelayTicks;
            long saveSDelay     = opt.subs.DelayTicks;
            bool saveAEnable    = opt.audio.Enabled;
            bool saveSEnable    = opt.subs.Enabled;

            bool savedAStatus   = aDemuxer.status != Status.NOTSET;
            bool savedSStatus   = sDemuxer.status != Status.NOTSET;
            string savedAUrl    = aDemuxer.url;
            string savedSUrl    = sDemuxer.url;
            int savedSEmbedded  = sDecoder.st != null ? sDecoder.st->index : -1;

            List<int> savedStreams = new List<int>();
            foreach (var tmp1 in demuxer.enabledStreams) savedStreams.Add(tmp1);
            ret = demuxer.Open(demuxer.url, opt.audio.Enabled, false, demuxer.ioStream);
            if (ret != 0) return ret;
            demuxer.enabledStreams = savedStreams;
            demuxer.RefreshStreams();

            if (savedAStatus) ret = aDemuxer.Open(savedAUrl);
            if (savedSStatus) 
                ret = sDemuxer.Open(savedSUrl);
            else if (savedSEmbedded != -1)
                OpenSubs(savedSEmbedded);

            opt.audio.DelayTicks= saveADelay;
            opt.subs.DelayTicks = saveSDelay;
            opt.audio.Enabled   = saveAEnable;
            opt.subs.Enabled    = saveSEnable;

            return 0;
        }

        public void OpenAudio(int streamIndex)
        {
            if (demuxer.status == Status.NOTSET) return;
            if (streamIndex < 0 || streamIndex >= demuxer.streams.Length) return;

            aDecoder.Open(demuxer, demuxer.fmtCtx->streams[streamIndex]);
            if (isRunning) aDecoder.decodeARE.Set();
        }
        public void OpenAudio(string url)
        {
            if (demuxer.status == Status.NOTSET) return;

            aDemuxer.Open(url);
        }
        public void OpenSubs(int streamIndex)
        {
            if (demuxer.status == Status.NOTSET) return;
            if (streamIndex < 0 || streamIndex >= demuxer.streams.Length) return;

            if (sDemuxer.status != Status.NOTSET) sDemuxer.Close();

            sDecoder.Open(demuxer, demuxer.fmtCtx->streams[streamIndex]);
            if (isRunning) sDecoder.decodeARE.Set();
        }
        public void OpenSubs(string url, long ms)
        {
            if (demuxer.status == Status.NOTSET) return;

            sDemuxer.Open(url);
            sDemuxer.ReSync(ms);
        }

        public void Pause()
        {
            status = Status.PAUSE;
            interrupt = 1;
            while (demuxer.status == Status.PLAY || aDemuxer.status == Status.PLAY || sDemuxer.status == Status.PLAY || aDecoder.status == Status.PLAY || vDecoder.status == Status.PLAY || sDecoder.status == Status.PLAY) Thread.Sleep(10);
            interrupt = 0;
        }

        public void Stop()
        {
            Pause();
            aDemuxer.Close();
            sDemuxer.Close();
            demuxer.Close();
        }
        public void StopAudio()
        {
            if (!aDecoder.isEmbedded)
                aDemuxer.Close();
            else
                aDecoder.Close();
        }

        public void StopSubs()
        {
            if (!sDecoder.isEmbedded)
                sDemuxer.Close();
            else
                sDecoder.Close();
        }

        public void Play()
        {
            if (demuxer.status != Status.READY) return;

            status = Status.PLAY;
            aDecoder.decodeARE.Set();
            vDecoder.decodeARE.Set();
            sDecoder.decodeARE.Set();
        }

        public void Seek(long ms, bool foreward = false)//, bool seek2any = false)
        {
            // Seeks at AVS Key Frame -> Seeks Externals at Key Frame time backwards (ensures audio/subs external frames before key frame)
            Log("[SEEK] " + Utils.TicksToTime(ms * 10000) + " (Request)");
            Pause();
                
            aDecoder.Flush();
            vDecoder.Flush();
            sDecoder.Flush();
            if (demuxer.Seek(ms, foreward) < 0) return;

            long ts = GetVideoFrame();
            if (ts < 0) { Log("[SEEK] Failed"); return; }
            Log("[SEEK] " + Utils.TicksToTime(ts) + " (Actual)");

            aDemuxer.Seek(ts / 10000);
            sDemuxer.Seek(ts / 10000);
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
                        lock (device)
                        ret = avcodec_send_packet(vDecoder.codecCtx, pkt);
                        av_packet_free(&pkt);

                        if (ret != 0) return -1;
                        
                        while (interrupt != 1)
                        {
                            AVFrame* frame = av_frame_alloc();
                            lock (device)
                            ret = avcodec_receive_frame(vDecoder.codecCtx, frame);

                            if (ret == 0)
                            {
                                MediaFrame mFrame = new MediaFrame();
                                mFrame.pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                                mFrame.timestamp = ((long)(mFrame.pts * vDecoder.info.Timebase) - demuxer.streams[vDecoder.st->index].StartTime) + opt.audio.LatencyTicks;

                                if (mFrame.pts == AV_NOPTS_VALUE)
                                {
                                    av_frame_free(&frame);
                                    continue;
                                }

                                if (firstTs == -1)
                                {
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

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [DecoderContext] {msg}"); }
    }
}
