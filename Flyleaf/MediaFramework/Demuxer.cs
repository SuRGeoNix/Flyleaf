using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;

namespace SuRGeoNix.Flyleaf.MediaFramework
{
    public unsafe class Demuxer
    {
        public bool                 isPlaying   => status == Status.PLAY;
        public bool                 isWaiting   { get; private set; }

        AVPacket* pkt;

        public string               url;
        public Status               status;
        public Type                 type;
        AVMediaType                 mType;
        public string               fmtName;

        public DecoderContext       decCtx;
        Decoder                     decoder;

        public AVFormatContext      *fmtCtx;
        public List<int>            enabledStreams;
        public StreamInfo[]         streams;
        public int                  defaultAudioStream;

        Thread                      demuxThread;
        public AutoResetEvent       demuxARE;
        bool                        forcePause;

        List<object>    gcPrevent = new List<object>();
        AVIOContext*    ioCtx;
        public Stream   ioStream;
        const int       ioBufferSize    = 0x200000;
        byte[]          ioBuffer;

        internal string headers, referer, userAgent;

        avio_alloc_context_read_packet IORead = (opaque, buffer, bufferSize) =>
        {
            GCHandle decCtxHandle = (GCHandle) ((IntPtr) opaque);
            DecoderContext decCtx = (DecoderContext) decCtxHandle.Target;

            //Console.WriteLine($"** R | { decCtx.demuxer.fmtCtx->pb->pos} - {decCtx.demuxer.ioStream.Position} | {decCtx.demuxer.fmtCtx->io_repositioned}");

            // During seek, it will destroy the sesion on Matroska (requires reopen the format input)
            if (decCtx.interrupt == 1) { Console.WriteLine("[Stream] Interrupt 1"); return AVERROR_EXIT; }

            //Thread.Sleep(1700); // Testing "slow" network streams

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

        public Demuxer(Type type = Type.Video, DecoderContext decCtx = null)
        {
            this.decCtx     = decCtx;
            this.type       = type;
            status          = Status.NOTSET;

            switch (type)
            {
                case Type.Video:
                    decoder = decCtx.vDecoder;
                    mType = AVMEDIA_TYPE_VIDEO;
                    break;

                case Type.Audio:
                    decoder = decCtx.aDecoder;
                    mType = AVMEDIA_TYPE_AUDIO;
                    break;

                case Type.Subs:
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
            if (status == Status.NOTSET) return null;

            return $"[# Format] {Utils.BytePtrToStringUTF8(fmtCtx->iformat->long_name)}/{Utils.BytePtrToStringUTF8(fmtCtx->iformat->name)} | {Utils.BytePtrToStringUTF8(fmtCtx->iformat->extensions)} | {new TimeSpan(fmtCtx->start_time * 10)}/{new TimeSpan(fmtCtx->duration * 10)}";
        }

        public int Open(string url, bool doAudio = true, bool doSubs = true, Stream stream = null, bool closeExternals = true)
        {
            if (url == null && stream == null) return -1;

            if (type == Type.Video && closeExternals)
            {
                decCtx.aDemuxer.Close();
                decCtx.sDemuxer.Close();
            }

            Close(closeExternals);

            int ret;
            this.url = url;
            Log($"Opening {url}");

            // TODO: Expose AV Format Options to Settings
            AVDictionary *opt = null;

            // Reduce those on network streams for faster opening
            av_dict_set_int(&opt, "probesize", 116 * (long)1024 * 1024, 0);         // (Bytes) Default 5MB | Higher for weird formats (such as .ts?)
            av_dict_set_int(&opt, "analyzeduration", 333 * (long)1000 * 1000, 0);  // (Microseconds) Default 5 seconds | Higher for network streams
            //av_dict_set_int(&opt, "max_probe_packets ", 15500, 0);         // (Packets) Default 2500

            // Required for Youtube-dl to avoid 403 Forbidden (Saves them in case of re-open)
            headers     = decCtx.Headers;
            referer     = decCtx.Referer;
            userAgent   = decCtx.UserAgent;

            if (headers  != null && headers   != "") av_dict_set(&opt, "headers",   headers,    0);
            if (referer  != null && referer   != "") av_dict_set(&opt, "referer",   referer,    0);
            if (userAgent!= null && userAgent != "") av_dict_set(&opt, "user_agent",userAgent,  0);

            /* Issue with HTTP/TLS - (sample video -> https://www.youtube.com/watch?v=sExEvN1bPRo)
             * 
             * Required probably only for AUDIO and some specific formats?
             * 
             * [tls @ 0e691280] Error in the pull function.
             * [tls @ 0e691280] The specified session has been invalidated for some reason.
             * [DECTX AVMEDIA_TYPE_AUDIO] AVMEDIA_TYPE_UNKNOWN - Error[-0005], Msg: I/O error
             */

            av_dict_set_int(&opt, "reconnect"               , 1, 0);    // auto reconnect after disconnect before EOF
            av_dict_set_int(&opt, "reconnect_streamed"      , 1, 0);    // auto reconnect streamed / non seekable streams
            av_dict_set_int(&opt, "reconnect_delay_max"     , 5, 0);    // max reconnect delay in seconds after which to give up
            //av_dict_set_int(&opt, "reconnect_on_network_error", 1, 0);
            //av_dict_set_int(&opt, "reconnect_at_eof", 1, 0);          // auto reconnect at EOF | Maybe will use this for another similar issues? | will not stop the decoders (no EOF)
            //av_dict_set_int(&opt, "multiple_requests", 1, 0);

            // RTSP
            av_dict_set(&opt, "rtsp_transport", "tcp", 0);              // Seems UDP causing issues (use this by default?)
            av_dict_set_int(&opt, "stimeout", 20 * 1000 * 1000, 0);     // RTSP microseconds timeout

            // hls more? | https://ffmpeg.org/ffmpeg-formats.html#toc-hls-1
            //av_dict_set_int(&opt, "max_reload", 1123123, 0);
            //av_dict_set_int(&opt, "m3u8_hold_counters", 1123123, 0);

            // misc
            //av_dict_set_int(&opt, "multiple_requests", 1, 0);
            //av_dict_set_int(&opt, "rw_timeout", 10 * 1000 * 1000, 0);
            //av_dict_set_int(&opt, "timeout", 10 * 1000 * 1000, 0);

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
            if (ret < 0) { Log($"[Format] [ERROR-1] {Utils.ErrorCodeToMsg(ret)} ({ret})"); return ret; }

            // validate that we need this
            av_format_inject_global_side_data(fmtCtx);

            ret = avformat_find_stream_info(fmtCtx, null);
            if (ret < 0) { Log($"[Format] [ERROR-2] {Utils.ErrorCodeToMsg(ret)} ({ret})"); avformat_close_input(&fmtCtxPtr); return ret; }

            StreamInfo.Fill(this);
            fmtName = Utils.BytePtrToStringUTF8(fmtCtx->iformat->long_name);
            
            // In case of multiple video streams (Youtube-dl manifest?)
            //if (decCtx.opt.video.PreferredHeight != -1 && type == Type.Video)
            //{
            //    ret = -1;
            //    var iresults =
            //        from    vstream in streams
            //        where   vstream.Type == AVMEDIA_TYPE_VIDEO && vstream.Height <= decCtx.opt.video.PreferredHeight
            //        orderby vstream.Height descending
            //        select  vstream;

            //    var results = iresults.ToList();
            //    if (results.Count != 0) ret = iresults.ToList()[0].StreamIndex;
            //}
            //if (ret == -1)

            ret = av_find_best_stream(fmtCtx, mType, -1, -1, null, 0);
            if (ret < 0) { Log($"[Format] [ERROR-3] {Utils.ErrorCodeToMsg(ret)} ({ret})"); avformat_close_input(&fmtCtxPtr); return ret; }

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
                            Log($"[Format] [ERROR-7] [Audio] {Utils.ErrorCodeToMsg(ret)} ({ret})");
                    }

                    if (doSubs)
                    {
                        if ((ret = av_find_best_stream(fmtCtx, AVMEDIA_TYPE_SUBTITLE, -1, decoder.st->index, null, 0)) >= 0)
                            decCtx.sDecoder.Open(this, fmtCtx->streams[ret]);
                        else if (ret != AVERROR_STREAM_NOT_FOUND)
                            Log($"[Format] [ERROR-7] [Subs ] {Utils.ErrorCodeToMsg(ret)} ({ret})");
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
                while (status != Status.READY) Thread.Sleep(5); // Wait for thread to come up
            }
            else
                status = Status.READY;

            pkt = av_packet_alloc();

            return 0;
        }
        public void Close(bool closeExternals = true)
        {
            decoder.Close();

            if (type == Type.Video)
            {
                if (decCtx.aDecoder.isEmbedded || closeExternals)
                    decCtx.aDecoder.Close();

                if (decCtx.sDecoder.isEmbedded || closeExternals)
                    decCtx.sDecoder.Close();
            }

            if (demuxThread != null && demuxThread.IsAlive) demuxThread.Abort();

            if (status == Status.NOTSET) return;

            if (pkt != null)    fixed (AVPacket** ptr = &pkt) av_packet_free(ptr);
            if (ioCtx != null)  { av_free(ioCtx->buffer); fixed (AVIOContext** ptr = &ioCtx) avio_context_free(ptr); }
            if (fmtCtx != null) fixed (AVFormatContext** ptr = &fmtCtx) avformat_close_input(ptr);
            GC.Collect();

            enabledStreams  = new List<int>();
            streams         = null;
            ioStream        = null;
            status          = Status.NOTSET;
            defaultAudioStream = -1;
        }
        public void RefreshStreams()
        {
            for (int i=0; i<fmtCtx->nb_streams; i++)
                fmtCtx->streams[i]->discard = enabledStreams.Contains(i) ? AVDiscard.AVDISCARD_DEFAULT : AVDiscard.AVDISCARD_ALL;
        }

        public void Pause()
        { 
            if (type == Type.Video) return;

            forcePause          = true;
            decoder.forcePause  = true;

            while (status == Status.PLAY || decoder.status == Status.PLAY) { Thread.Sleep(5); }

            forcePause          = false;
            decoder.forcePause  = false;
        }

        public int Seek(long ms, bool foreward = false)
        {
            if (status == Status.NOTSET) return -1;
            if (status == Status.END) { if (fmtCtx->pb == null) Open(url, decCtx.opt.audio.Enabled, false); else status = Status.READY; } //Open(url, ...); // Usefull for HTTP

            int ret;
            long seekTs = CalcSeekTimestamp(ms, ref foreward);
            //Log($"[SEEK] {(foreward ? "F" : "B")} | {Utils.TicksToTime(seekTs)}");

            if (type != Type.Video)
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
                Log($"[SEEK] [ERROR-11] {Utils.ErrorCodeToMsg(ret)} ({ret})");
                //ret = avformat_seek_file(fmtCtx, -1, Int64.MinValue, seekTs / 10, Int64.MaxValue, AVSEEK_FLAG_ANY); // Same as av_seek_frame ?
                ret = av_seek_frame(fmtCtx, -1, seekTs / 10, foreward ? AVSEEK_FLAG_BACKWARD : AVSEEK_FLAG_FRAME);
                if (ret < 0) Log($"[SEEK] [ERROR-12] {Utils.ErrorCodeToMsg(ret)} ({ret})");
            }

            return ret;
        }
        public long CalcSeekTimestamp(long ms, ref bool foreward)
        {
            long ticks = ((ms * 10000) + streams[decoder.st->index].StartTime);

            if (type == Type.Audio) ticks -= (decCtx.opt.audio.DelayTicks + decCtx.opt.audio.LatencyTicks);
            if (type == Type.Subs ) ticks -= decCtx.opt.subs. DelayTicks;

            if (ticks < streams[decoder.st->index].StartTime) { ticks = streams[decoder.st->index].StartTime; foreward = true; }
            else if (ticks >= streams[decoder.st->index].StartTime + streams[decoder.st->index].DurationTicks) { ticks = streams[decoder.st->index].StartTime + streams[decoder.st->index].DurationTicks; foreward = false; }

            return ticks;
        }
        public void ReSync(long ms = -1)
        {
            if (status == Status.NOTSET) return;

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

            if (decCtx.status == Status.PLAY) decoder.decodeARE.Set();
        }

        public void Demux()
        {
            //int vf = 0,af = 0,sf = 0;

            while (true)
            {
                if (status != Status.END) status = Status.READY;
                demuxARE.Reset();
                demuxARE.WaitOne();
                status = Status.PLAY;
                forcePause = false;
                Log("Started");
                int ret = 0;

                while (true)
                {
                    while (decoder.packets.Count > decCtx.opt.demuxer.MaxQueueSize && decCtx.isPlaying && !forcePause) { isWaiting = true; Thread.Sleep(20); }
                    isWaiting = false;
                    if (decCtx.status != Status.PLAY || forcePause) break;

                    ret = av_read_frame(fmtCtx, pkt);

                    if (ret != 0)
                    {
                        if (ret == AVERROR_EXIT)
                        {
                            Log("AVERROR_EXIT!!! " + decCtx.interrupt);
                            continue;
                        }

                        if (ret == AVERROR_EOF)// || fmtCtx->pb->eof_reached == 1)// || ret == AVERROR_EXIT)
                        {
                            av_packet_unref(pkt);
                            status = Status.END;
                            Log($"EOF ({(ret == AVERROR_EOF ? 1 : (fmtCtx->pb->eof_reached == 1 ? 2 : 3))}) | {decCtx.interrupt}");
                        }
                        else
                            Log($"[ERROR-1] {Utils.ErrorCodeToMsg(ret)} ({ret})");

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

                if (type == Type.Video && ret != 0 && status != Status.END) { decCtx.status = Status.PAUSE; Log("Stop All"); }

                Log($"Done");// [Total: {vf+af+sf}] [VF: {vf}, AF: {af}, SF: {sf}]");
            }
        }

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [Demuxer: {type.ToString().PadLeft(5, ' ')}] {msg}"); }
    }
}