using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

namespace FlyleafLib.MediaFramework
{
    public unsafe class Demuxer
    {
        public bool                 isPlaying   => status == Status.Playing;
        public bool                 isWaiting   { get; private set; }

        AVPacket* pkt;

        public string               url;
        public Status               status;
        public MediaType            type;
        AVMediaType                 mType;

        public DemuxerInfo          info;
        public DecoderContext       decCtx;
        Decoder                     decoder;

        public AVFormatContext      *fmtCtx;
        public List<int>            enabledStreams;
        public StreamInfo[]         streams;
        //public List<AudioStream>    AudioStreams    { get; set; } = new List<AudioStream>();
        //public List<VideoStream>    VideoStreams    { get; set; } = new List<VideoStream>();
        //public List<SubtitleStream> SubtitleStreams { get; set; } = new List<SubtitleStream>();

        public int                  defaultAudioStream;

        internal Thread                      demuxThread;
        public AutoResetEvent       demuxARE;
        bool                        forcePause;

        List<object>    gcPrevent = new List<object>();
        AVIOContext*    ioCtx;
        public Stream   ioStream;
        const int       ioBufferSize    = 0x200000; // Should be exposed to config as well
        byte[]          ioBuffer;

        avio_alloc_context_read_packet IORead = (opaque, buffer, bufferSize) =>
        {
            GCHandle decCtxHandle = (GCHandle) ((IntPtr) opaque);
            DecoderContext decCtx = (DecoderContext) decCtxHandle.Target;

            //Console.WriteLine($"** R | { decCtx.demuxer.fmtCtx->pb->pos} - {decCtx.demuxer.ioStream.Position} | {decCtx.demuxer.fmtCtx->io_repositioned}");

            // During seek, it will destroy the sesion on Matroska (requires reopen the format input)
            if (decCtx.interrupt == 1) return AVERROR_EXIT;

            //Thread.Sleep(800); // Testing "slow" network streams

            // Check whether is possible direct access from ioBuffer to ioBufferUnmanaged to avoid Marshal.Copy each time
            int ret = decCtx.demuxer.ioStream.Read(decCtx.demuxer.ioBuffer, 0, bufferSize);

            if (ret < 0 || decCtx.interrupt == 1)
            {
                if (ret == -1)
                    Console.WriteLine("[Stream] Cancel");
                else if (ret == -2)
                    Console.WriteLine("[Stream] Error");
                else
                    Console.WriteLine("[Stream] Interrupt 2");

                if (ret > 0) decCtx.demuxer.ioStream.Position -= ret;
                if (decCtx.demuxer.ioStream.Position < 0) decCtx.demuxer.ioStream.Position = 0;

                return AVERROR_EXIT;
            }

            Marshal.Copy(decCtx.demuxer.ioBuffer, 0, (IntPtr) buffer, ret);

            return ret;
        };

        avio_alloc_context_seek IOSeek = (opaque, offset, wehnce) =>
        {
            GCHandle decCtxHandle = (GCHandle) ((IntPtr) opaque);
            DecoderContext decCtx = (DecoderContext) decCtxHandle.Target;

            //Console.WriteLine($"** S | {decCtx.demuxer.fmtCtx->pb->pos} - {decCtx.demuxer.ioStream.Position}");

            if (wehnce == AVSEEK_SIZE) return decCtx.demuxer.ioStream.Length;

            return decCtx.demuxer.ioStream.Seek(offset, (SeekOrigin) wehnce);
        };

        avio_alloc_context_read_packet_func ioread          = new avio_alloc_context_read_packet_func();    
        avio_alloc_context_seek_func        ioseek          = new avio_alloc_context_seek_func();

        AVIOInterruptCB_callback_func       interruptClbk   = new AVIOInterruptCB_callback_func();     
        AVIOInterruptCB_callback InterruptClbk = (p0) =>
        {
            GCHandle decCtxHandle = (GCHandle)((IntPtr)p0);
            DecoderContext decCtx = (DecoderContext)decCtxHandle.Target;

            //if (decCtx.interrupt == 1) Console.WriteLine("----------- InterruptClbk ------------");
            return decCtx.interrupt;
        };

        public Demuxer(MediaType type = MediaType.Video, DecoderContext decCtx = null)
        {
            this.decCtx     = decCtx;
            this.type       = type;
            status          = Status.None;

            switch (type)
            {
                case MediaType.Video:
                    decoder = decCtx.vDecoder;
                    mType = AVMEDIA_TYPE_VIDEO;
                    break;

                case MediaType.Audio:
                    decoder = decCtx.aDecoder;
                    mType = AVMEDIA_TYPE_AUDIO;
                    break;

                case MediaType.Subs:
                    decoder = decCtx.sDecoder;
                    mType = AVMEDIA_TYPE_SUBTITLE;
                    break;
            }

            demuxARE            = new AutoResetEvent(false);
            enabledStreams      = new List<int>();
            defaultAudioStream  = -1;

            interruptClbk.Pointer   = Marshal.GetFunctionPointerForDelegate(InterruptClbk);
            ioread.Pointer          = Marshal.GetFunctionPointerForDelegate(IORead);
            ioseek.Pointer          = Marshal.GetFunctionPointerForDelegate(IOSeek);

            gcPrevent = new List<object>();
            gcPrevent.Add(ioread);
            gcPrevent.Add(ioseek);
            gcPrevent.Add(IORead);
            gcPrevent.Add(IOSeek);
        }

        public string GetDump()
        {
            if (status == Status.None) return null;

            return $"[# Format] {Utils.BytePtrToStringUTF8(fmtCtx->iformat->long_name)}/{Utils.BytePtrToStringUTF8(fmtCtx->iformat->name)} | {Utils.BytePtrToStringUTF8(fmtCtx->iformat->extensions)} | {new TimeSpan(fmtCtx->start_time * 10)}/{new TimeSpan(fmtCtx->duration * 10)}";
        }

        public int Open(string url, bool doAudio = true, bool doSubs = true, Stream stream = null, bool closeExternals = true)
        {
            if (url == null && stream == null) return -1;

            if (type == MediaType.Video && closeExternals)
            {
                decCtx.aDemuxer.Close();
                decCtx.sDemuxer.Close();
            }
            Close(closeExternals);

            int ret;
            this.url = url;
            Log($"Opening {url}");

            AVDictionary *opt = null;
            Dictionary<string, string> optPtr = decCtx.cfg.demuxer.GetFormatOptPtr(type);
            foreach (var t1 in optPtr) av_dict_set(&opt, t1.Key, t1.Value, 0);

            fmtCtx = avformat_alloc_context();
            fmtCtx->interrupt_callback.callback = interruptClbk;
            fmtCtx->interrupt_callback.opaque   = (void*)decCtx.decCtxPtr;
            fmtCtx->flags |= AVFMT_FLAG_DISCARD_CORRUPT;

            if (stream != null)
            {
                if (ioBuffer == null) ioBuffer  = new byte[ioBufferSize]; // NOTE: if we use small buffer ffmpeg might request more than we suggest
                ioCtx           = avio_alloc_context((byte*)av_malloc(ioBufferSize), ioBufferSize, 0, (void*) decCtx.decCtxPtr, ioread, null, ioseek);
                fmtCtx->pb      = ioCtx;
                fmtCtx->flags  |= AVFMT_FLAG_CUSTOM_IO;
                ioStream        = stream;
                ioStream.Seek(0, SeekOrigin.Begin);
            }

            AVFormatContext* fmtCtxPtr = fmtCtx;
            ret = avformat_open_input(&fmtCtxPtr, stream != null ? null : url, null, &opt);
            if (ret < 0) { Log($"[Format] [ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); return ret; }

            // validate that we need this
            av_format_inject_global_side_data(fmtCtx);

            ret = avformat_find_stream_info(fmtCtx, null);
            if (ret < 0) { Log($"[Format] [ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); avformat_close_input(&fmtCtxPtr); return ret; }

            DemuxerInfo.Fill(this);
            Log("\r\n[# Format] " + DemuxerInfo.GetDumpAll(this));

            ret = av_find_best_stream(fmtCtx, mType, -1, -1, null, 0);
            if (ret < 0) { Log($"[Format] [ERROR-3] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); avformat_close_input(&fmtCtxPtr); return ret; }

            ret = decoder.Open(this, fmtCtx->streams[ret]);
            if (ret < 0) { avformat_close_input(&fmtCtxPtr); return ret; }

            switch (mType)
            {
                case AVMEDIA_TYPE_VIDEO:

                    ret = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_AUDIO, -1, decoder.st->index, null, 0);
                    if (ret >= 0) defaultAudioStream = ret;

                    if (doAudio)
                    {
                        if (ret >= 0)
                            decCtx.aDecoder.Open(this, fmtCtx->streams[ret]);
                        else if (ret != AVERROR_STREAM_NOT_FOUND)
                            Log($"[Format] [ERROR-7] [Audio] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                    }

                    if (doSubs)
                    {
                        if ((ret = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_SUBTITLE, -1, decoder.st->index, null, 0)) >= 0)
                            decCtx.sDecoder.Open(this, fmtCtx->streams[ret]);
                        else if (ret != AVERROR_STREAM_NOT_FOUND)
                            Log($"[Format] [ERROR-7] [Subs ] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                    }

                    break;

                case AVMEDIA_TYPE_AUDIO:
                    break;
                case AVMEDIA_TYPE_SUBTITLE:
                    break;
            }

            RefreshStreams();

            if (demuxThread == null || !demuxThread.IsAlive)
            {
                demuxThread = new Thread(() => Demux());
                demuxThread.IsBackground= true;   
                demuxThread.Start();
                while (status != Status.Paused) Thread.Sleep(5); // Wait for thread to come up
            }
            else
                status = Status.Paused;

            pkt = av_packet_alloc();
            //Console.WriteLine($"CP: {decoder.codecCtx->colorspace} | PR: {decoder.codecCtx->color_primaries} | TRC: {decoder.codecCtx->color_trc} | CR: {decoder.codecCtx->color_range}");
            return 0;
        }

        public void RestartDemuxThread()
        {
            Utils.EnsureThreadDone(demuxThread, 20, 3);
            demuxThread = new Thread(() => Demux());
            demuxThread.IsBackground= true;   
            demuxThread.Start();
            while (status != Status.Paused) Thread.Sleep(5);
        }

        public void Close(bool closeExternals = true)
        {
            decoder.Close();

            if (type == MediaType.Video)
            {
                if (decCtx.aDecoder.isEmbedded || closeExternals)
                    decCtx.aDecoder.Close();

                if (decCtx.sDecoder.isEmbedded || closeExternals)
                    decCtx.sDecoder.Close();
            }

            if (demuxThread != null && demuxThread.IsAlive) demuxThread.Abort();

            if (status == Status.None) return;

            if (pkt != null)    fixed (AVPacket** ptr = &pkt) av_packet_free(ptr);
            if (ioCtx != null)  { av_free(ioCtx->buffer); fixed (AVIOContext** ptr = &ioCtx) avio_context_free(ptr); }
            if (fmtCtx != null) fixed (AVFormatContext** ptr = &fmtCtx) avformat_close_input(ptr);
            GC.Collect();

            enabledStreams  = new List<int>();
            streams         = null;
            ioStream        = null;
            status          = Status.None;
            defaultAudioStream = -1;
        }

        public void RefreshStreams()
        {
            for (int i=0; i<fmtCtx->nb_streams; i++)
                fmtCtx->streams[i]->discard = enabledStreams.Contains(i) ? AVDiscard.AVDISCARD_DEFAULT : AVDiscard.AVDISCARD_ALL;
        }

        public void Pause()
        { 
            if (type == MediaType.Video) return;

            forcePause          = true;
            decoder.forcePause  = true;

            while (status == Status.Playing || decoder.status == Status.Playing) { Thread.Sleep(5); }

            forcePause          = false;
            decoder.forcePause  = false;
        }

        public int Seek(long ms, bool foreward = false)
        {
            if (status == Status.None) return -1;
            if (status == Status.Ended) { if (fmtCtx->pb == null) Open(url, decCtx.cfg.audio.Enabled, false); else status = Status.Paused; } //Open(url, ...); // Usefull for HTTP

            int ret;
            long seekTs = CalcSeekTimestamp(ms, ref foreward);
            //Log($"[SEEK] {(foreward ? "F" : "B")} | {Utils.TicksToTime(seekTs)}");

            if (type != MediaType.Video)
            {
                ret = foreward ?
                    avformat_seek_file(fmtCtx, -1, seekTs / 10,     seekTs / 10, Int64.MaxValue, AVSEEK_FLAG_ANY):
                    avformat_seek_file(fmtCtx, -1, Int64.MinValue,  seekTs / 10, seekTs / 10,    AVSEEK_FLAG_ANY);
            } 
            else
            {
                ret =  av_seek_frame(fmtCtx, -1, seekTs / 10, foreward ? AVSEEK_FLAG_FRAME : AVSEEK_FLAG_BACKWARD);
            }

            if (ret < 0)
            {
                Log($"[SEEK] [ERROR-11] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                //ret = avformat_seek_file(fmtCtx, -1, Int64.MinValue, seekTs / 10, Int64.MaxValue, AVSEEK_FLAG_ANY); // Same as av_seek_frame ?
                ret = av_seek_frame(fmtCtx, -1, seekTs / 10, foreward ? AVSEEK_FLAG_BACKWARD : AVSEEK_FLAG_FRAME);
                if (ret < 0) Log($"[SEEK] [ERROR-12] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
            }

            return ret;
        }
        public long CalcSeekTimestamp(long ms, ref bool foreward)
        {
            long ticks = ((ms * 10000) + decoder.info.StartTime);

            if (type == MediaType.Audio) ticks -= (decCtx.cfg.audio.DelayTicks + decCtx.cfg.audio.LatencyTicks);
            if (type == MediaType.Subs ) ticks -=  decCtx.cfg.subs. DelayTicks;

            if (ticks < decoder.info.StartTime) { ticks = decoder.info.StartTime; foreward = true; }
            else if (ticks >= decoder.info.StartTime + decoder.info.Duration) { ticks = decoder.info.StartTime + decoder.info.Duration; foreward = false; }

            return ticks;
        }
        public void ReSync(long ms = -1)
        {
            if (status == Status.None) return;

            Pause();
            decoder.Flush();

            if (ms == -1)
            {
                MediaFrame vFrame = null;
                do
                {
                    decCtx.vDecoder.frames.TryPeek(out vFrame);
                    if (vFrame != null) break; else Thread.Sleep(5);
                } while (vFrame == null || forcePause);

                if (forcePause) return;
                Seek(vFrame.timestamp/10000);
            }
            else
            {
                Seek(ms);
            }

            if (decCtx.status == Status.Playing) decoder.decodeARE.Set();
        }

        public void Demux()
        {
            //int vf = 0,af = 0,sf = 0;

            while (true)
            {
                if (status != Status.Ended) status = Status.Paused;
                demuxARE.Reset();
                demuxARE.WaitOne();
                status = Status.Playing;
                forcePause = false;
                Log("Started");
                int ret = 0;
                int allowedErrors = decCtx.cfg.demuxer.MaxErrors;

                while (true)
                {
                    while (decoder.packets.Count > decCtx.cfg.demuxer.MaxQueueSize && decCtx.isPlaying && !forcePause) { isWaiting = true; Thread.Sleep(20); }
                    isWaiting = false;
                    if (decCtx.status != Status.Playing || forcePause) break;

                    ret = av_read_frame(fmtCtx, pkt);

                    if (ret != 0)
                    {
                        if (ret == AVERROR_EXIT)
                        {
                            Log("AVERROR_EXIT!!! " + decCtx.interrupt);
                            allowedErrors--; if (allowedErrors == 0) break;
                            continue;
                        }

                        if (ret == AVERROR_EOF)// || fmtCtx->pb->eof_reached == 1)// || ret == AVERROR_EXIT)
                        {
                            av_packet_unref(pkt);
                            status = Status.Ended;
                            Log($"EOF ({(ret == AVERROR_EOF ? 1 : (fmtCtx->pb->eof_reached == 1 ? 2 : 3))}) | {decCtx.interrupt}");
                        }
                        else
                            Log($"[ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                        break;
                    }

                    if (!enabledStreams.Contains(pkt->stream_index)) { av_packet_unref(pkt); continue; }

                    switch (fmtCtx->streams[pkt->stream_index]->codecpar->codec_type)
                    {
                        case AVMEDIA_TYPE_AUDIO:
                            decCtx.aDecoder.packets.Enqueue((IntPtr)pkt);
                            pkt = av_packet_alloc();
                            //Log("[Audio] " + Utils.TicksToTime((long)(pkt->pts * streams[decCtx.aDecoder.st->index].Timebase)) + " | pts -> " + pkt->pts);
                            //af++;
                            break;

                        case AVMEDIA_TYPE_VIDEO:
                            decCtx.vDecoder.packets.Enqueue((IntPtr)pkt);
                            //Log("[Video] " + Utils.TicksToTime((long)(pkt->pts * streams[decCtx.vDecoder.st->index].Timebase)) + " | pts -> " + pkt->pts + " | " + pkt->dts + " | " + decoder.packets.Count);
                            pkt = av_packet_alloc();
                            
                            //vf++;
                            break;

                        case AVMEDIA_TYPE_SUBTITLE:
                            decCtx.sDecoder.packets.Enqueue((IntPtr)pkt);
                            pkt = av_packet_alloc();
                            //Log("[ Subs] " + Utils.TicksToTime((long)(pkt->pts * streams[decCtx.sDecoder.st->index].Timebase)) + " | pts -> " + pkt->pts);
                            //sf++;
                            break;

                        default:
                            av_packet_unref(pkt);
                            break;
                    }
                }

                if (type == MediaType.Video && ret != 0 && status != Status.Ended) { decCtx.status = Status.Paused; Log("Stop All"); }

                Log($"Done");// [Total: {vf+af+sf}] [VF: {vf}, AF: {af}, SF: {sf}]");
            }
        }

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{decCtx.player.PlayerId}] [Demuxer: {type.ToString().PadLeft(5, ' ')}] {msg}"); }
    }
}