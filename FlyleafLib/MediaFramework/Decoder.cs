using System;
using System.Collections.Concurrent;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace FlyleafLib.MediaFramework
{
    public unsafe class Decoder
    {
        internal static readonly AVPixelFormat  VOutPixelFormat     = AVPixelFormat.AV_PIX_FMT_RGBA;
        internal static readonly AVSampleFormat AOutSampleFormat    = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        internal static readonly int            AOutChannelLayout   = AV_CH_LAYOUT_STEREO;
        internal static readonly int            AOutChannels        = av_get_channel_layout_nb_channels((ulong)AOutChannelLayout);

        AVFrame* frame;

        public bool     isPlaying           => status == Status.Playing;
        //public bool     isWaiting           { get; private set; }

        public Status   status;
        public MediaType     type;
        public bool     isEmbedded;
        
        public DecoderContext               decCtx;
        public Demuxer                      demuxer;
        public StreamInfo                   info;

        public AVCodecContext               *codecCtx;
        public AVStream                     *st;
        public ConcurrentQueue<IntPtr>      packets;
        public ConcurrentQueue<MediaFrame>  frames;

        Thread                              decodeThread;
        public AutoResetEvent               decodeARE;
        public bool                         forcePause;

        public bool                         hwAccelSuccess;

        public SwsContext*                  swsCtx;
        internal Texture2DDescription       textDesc, textDescUV;
        internal Texture2D                  textureFFmpeg;

        public SwrContext*                  swrCtx;

        public IntPtr                       outBufferPtr; 
        public int                          outBufferSize;
        public byte_ptrArray4               outData;
        public int_array4                   outLineSize;

        public byte**                       m_dst_data;
        public int                          m_max_dst_nb_samples;
        public int                          m_dst_linesize;

        public Decoder(MediaType type, DecoderContext decCtx)
        {
            this.type   = type;
            this.decCtx = decCtx;

            decodeARE   = new AutoResetEvent(false);
            packets     = new ConcurrentQueue<IntPtr>();
            frames      = new ConcurrentQueue<MediaFrame>();

            status      = Status.None;
        }

        public int Open(Demuxer demuxer, AVStream *st)
        {
            Log($"Opening StreamIndex #{st->index}");
            if (status != Status.None) Close();

            this.st         = st;
            this.demuxer    = demuxer;
            isEmbedded      = demuxer.type == MediaType.Video ? true : false;
            info            = demuxer.streams[st->index];

            int ret = OpenCodec();
            if (ret < 0) return ret;

            if (decodeThread == null || !decodeThread.IsAlive)
            {
                decodeThread = new Thread(() => Decode());
                decodeThread.IsBackground = true;
                decodeThread.Start();
                while (status != Status.Paused) Thread.Sleep(5);
            }
            else
                status = Status.Paused;

            frame = av_frame_alloc();
            demuxer.enabledStreams.Add(st->index);
            st->discard = AVDiscard.AVDISCARD_DEFAULT;

            return ret;
        }
        public int OpenCodec()
        {
            int ret;

            AVCodec* codec = avcodec_find_decoder(st->codecpar->codec_id);
            if (codec == null)      { Log($"[CodecOpen {type}] [ERROR-1] No suitable codec found"); return -1; }

            codecCtx = avcodec_alloc_context3(null);
            if (codecCtx == null)   { Log($"[CodecOpen {type}] [ERROR-2] Failed to allocate context3"); return -1; }

            ret      = avcodec_parameters_to_context(codecCtx, st->codecpar);
            if (ret < 0)            { Log($"[CodecOpen {type}] [ERROR-3] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); return ret; }

            codecCtx->pkt_timebase  = st->time_base;
            codecCtx->codec_id      = codec->id;

            if (type == MediaType.Audio)
                ret = SetupAudio();
            else if (type == MediaType.Video)
                ret = SetupVideo(codec);

            if (ret < 0) return ret;

            ret = avcodec_open2(codecCtx, codec, null);
            
            return ret;
        }

        public int SetupAudio()
        {
            int ret;

            if (swrCtx ==  null) swrCtx = swr_alloc();
            
            m_max_dst_nb_samples    = -1;

            av_opt_set_int(swrCtx,           "in_channel_layout",   (int)codecCtx->channel_layout, 0);
            av_opt_set_int(swrCtx,           "in_channel_count",         codecCtx->channels, 0);
            av_opt_set_int(swrCtx,           "in_sample_rate",           codecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(swrCtx,    "in_sample_fmt",            codecCtx->sample_fmt, 0);

            av_opt_set_int(swrCtx,           "out_channel_layout",       AOutChannelLayout, 0);
            av_opt_set_int(swrCtx,           "out_channel_count",        AOutChannels, 0);
            av_opt_set_int(swrCtx,           "out_sample_rate",          codecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(swrCtx,    "out_sample_fmt",           AOutSampleFormat, 0);
            
            ret = swr_init(swrCtx);
            if (ret < 0) Log($"[AudioSetup] [ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); 

            decCtx.audioPlayer.Initialize(codecCtx->sample_rate);

            return ret;
        }
        public int SetupVideo(AVCodec* codec)
        {
            hwAccelSuccess = false;

            if (decCtx.cfg.decoder.HWAcceleration)
            {
                if (VideoAcceleration.CheckCodecSupport(codec))
                {
                    if (demuxer.decCtx.va.hw_device_ctx == null) demuxer.decCtx.va.Init(decCtx.renderer.device);

                    if (demuxer.decCtx.va.hw_device_ctx != null)
                    {
                        codecCtx->hw_device_ctx = av_buffer_ref(demuxer.decCtx.va.hw_device_ctx);
                        hwAccelSuccess = true;
                        Log("HW Acceleration Success");
                    }
                }
                else
                    Log("HW Acceleration Failed");
            }
            else
                Log("HW Acceleration Disabled");

            codecCtx->thread_count = Math.Min(decCtx.cfg.decoder.VideoThreads, codecCtx->codec_id == AV_CODEC_ID_HEVC ? 32 : 16);
            codecCtx->thread_type  = 0;
            //vCodecCtx->active_thread_type = FF_THREAD_FRAME;
            //vCodecCtx->active_thread_type = FF_THREAD_SLICE;
            //codecCtx->thread_safe_callbacks = 1;

            int bits = info.PixelFormatDesc->comp.ToArray()[0].depth;

            textDesc = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Default,
                BindFlags           = BindFlags.ShaderResource,

                Format              = bits > 8 ? Format.R16_UNorm : Format.R8_UNorm, // FOR HW/SW will be set later
                Width               = codecCtx->width,
                Height              = codecCtx->height,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };

            textDescUV = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Default,
                BindFlags           = BindFlags.ShaderResource,

                //Format              = bits > 8 ? Format.R16G16_UNorm : Format.R8G8_UNorm, // FOR HW/SW will be set later
                Format              = bits > 8 ? Format.R16_UNorm : Format.R8_UNorm, // FOR HW/SW will be set later
                Width               = codecCtx->width >> info.PixelFormatDesc->log2_chroma_w,
                Height              = codecCtx->height >> info.PixelFormatDesc->log2_chroma_h,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };

            decCtx.renderer.FrameResized();

            return 0;
        }

        public void Pause() { }
        public void Flush()
        {
            if (status == Status.None) return;

            foreach (IntPtr pktPtr in packets)
            {
                AVPacket* pkt = (AVPacket*)pktPtr;
                av_packet_free(&pkt);
            }

            if (type == MediaType.Video) Utils.DisposeVideoFrames(frames);

            packets = new ConcurrentQueue<IntPtr>();
            frames  = new ConcurrentQueue<MediaFrame>();

            avcodec_flush_buffers(codecCtx);

            if (status == Status.Ended) status = Status.Paused;
        }
        public void Close()
        {
            if (status == Status.None) return;
            if (decodeThread.IsAlive) { forcePause = true; Thread.Sleep(20); if (decodeThread.IsAlive) decodeThread.Abort(); }

            if (demuxer.enabledStreams.Contains(st->index))
            {
                Log($"Closing StreamIndex #{st->index}");
                st->discard = AVDiscard.AVDISCARD_ALL;
                demuxer.enabledStreams.Remove(st->index);
            }

            Flush();

            if (type == MediaType.Video)
            {
                av_buffer_unref(&codecCtx->hw_device_ctx);
                if (swsCtx != null) { sws_freeContext(swsCtx); swsCtx = null; }
            }
            //else if (type == MediaType.Audio)
            //{
            //    //fixed (SwrContext** ptr = &swrCtx) swr_free(ptr);
            //}

            avcodec_close(codecCtx);
            if (frame != null) fixed (AVFrame** ptr = &frame) av_frame_free(ptr);
            if (codecCtx != null) fixed (AVCodecContext** ptr = &codecCtx) avcodec_free_context(ptr);
            codecCtx    = null;
            decodeARE.Reset();
            demuxer     = null;
            st          = null;
            info        = null;
            isEmbedded  = false;
            status      = Status.None;
        }

        public void Decode()
        {
            //int xf = 0;
            AVPacket *pkt;

            while (true)
            {
                if (status != Status.Ended) status = Status.Paused;
                decodeARE.Reset();
                decodeARE.WaitOne();
                status              = Status.Playing;
                forcePause          = false;
                bool shouldStop     = false;
                int  allowedErrors  = decCtx.cfg.decoder.MaxErrors;
                int  ret            = -1;

                Log("Started");

                // Wait for demuxer to come up
                if (demuxer.status == Status.Paused)
                {
                    demuxer.demuxARE.Set();
                    while (!demuxer.isPlaying && demuxer.status != Status.Ended && !forcePause && decCtx.isPlaying) Thread.Sleep(1);
                }

                while (true)
                {
                    // No Packets || Max Frames Brakes
                    if (packets.Count == 0 ||
                        (type == MediaType.Audio && frames.Count > decCtx.cfg.decoder.MaxAudioFrames) || 
                        (type == MediaType.Video && frames.Count > decCtx.cfg.decoder.MaxVideoFrames) || 
                        (type == MediaType.Subs  && frames.Count > decCtx.cfg.decoder.MaxSubsFrames))
                    {
                        shouldStop  = false;
                        //isWaiting   = true;

                        do
                        {
                            if (!decCtx.isPlaying || forcePause) // Proper Pause
                                { Log("Pausing"); shouldStop = true; break; }
                            else if (packets.Count == 0 && demuxer.status == Status.Ended) // Drain
                                { Log("Draining"); break; }
                            //else if (packets.Count == 0 && (!demuxer.isPlaying || demuxer.isWaiting)) // No reason to run
                            else if (packets.Count == 0 && (!demuxer.isPlaying || ((!isEmbedded || type == MediaType.Video) && demuxer.isWaiting))) // No reason to run
                                { Log("Exhausted " + isPlaying); shouldStop = true; break; }

                            Thread.Sleep(10);

                        } while (packets.Count == 0 ||
                                (type == MediaType.Audio && frames.Count > decCtx.cfg.decoder.MaxAudioFrames) || 
                                (type == MediaType.Video && frames.Count > decCtx.cfg.decoder.MaxVideoFrames) || 
                                (type == MediaType.Subs  && frames.Count > decCtx.cfg.decoder.MaxSubsFrames));

                        //isWaiting = false;
                        if (shouldStop) break;
                    }

                    if (packets.Count == 0 && demuxer.status == Status.Ended)
                    {
                        if (type == MediaType.Video)
                        {
                            // Check case pause while draining
                            Log("Draining...");
                            pkt = null;
                        }
                        else
                        {
                            status = Status.Ended;
                            Log("EOF");
                            break;
                        }
                    }
                    else
                    {
                        packets.TryDequeue(out IntPtr pktPtr);
                        pkt = (AVPacket*) pktPtr;

                        if (type == MediaType.Subs)
                        {
                            MediaFrame mFrame = new MediaFrame();
                            mFrame.pts = pkt->pts;
                            mFrame.timestamp = (long)((mFrame.pts * info.Timebase)) + decCtx.cfg.audio.LatencyTicks + decCtx.cfg.subs.DelayTicks;
                            //Log(Utils.TicksToTime((long)(mFrame.pts * demuxer.streams[st->index].timebase)) + " | pts -> " + mFrame.pts);
                            //xf++;

                            if (mFrame.pts == AV_NOPTS_VALUE)
                            {
                                av_packet_free(&pkt);
                                continue;
                            }

                            int gotFrame = 0;
                            AVSubtitle sub = new AVSubtitle();

                            // drain mode todo
                            // pkt->data set to NULL && pkt->size = 0 until it stops returning subtitles
                            ret = avcodec_decode_subtitle2(codecCtx, &sub, &gotFrame, pkt);
                            if (ret < 0)
                            {
                                allowedErrors--;
                                Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                                if (allowedErrors == 0)
                                {
                                    Log("[ERROR-0] Too many errors!");
                                    break;
                                }

                                continue;
                            }
                            
                            if (gotFrame < 1 || sub.num_rects < 1 ) continue;

                            MediaFrame.ProcessSubsFrame(this, mFrame, &sub);

                            frames.Enqueue(mFrame);
                            avsubtitle_free(&sub);
                            av_packet_free(&pkt);

                            continue;
                        }
                    }

                    ret = avcodec_send_packet(codecCtx, pkt);
                    
                    if (ret != 0 && ret != AVERROR(EAGAIN))
                    {
                        if (ret == AVERROR_EOF)
                        {
                            status = Status.Ended;
                            Log("EOF");
                            break;
                        }
                        else
                        //if (ret == AVERROR_INVALIDDATA) // We also get Error number -16976906 occurred
                        {
                            allowedErrors--;
                            Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                            if (allowedErrors == 0)
                            {
                                Log("[ERROR-0] Too many errors!");
                                break;
                            }

                            continue;
                        }
                    }

                    av_packet_free(&pkt);

                    while (true)
                    {
                        ret = avcodec_receive_frame(codecCtx, frame);

                        if (ret == 0)
                        {
                            MediaFrame mFrame = new MediaFrame();
                            mFrame.pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;

                            if (mFrame.pts == AV_NOPTS_VALUE)
                            {
                                av_frame_unref(frame);
                                continue;
                            }

                            //Log(Utils.TicksToTime((long)(mFrame.pts * demuxer.streams[st->index].Timebase)) + " | pts -> " + mFrame.pts);
                            
                            if (type == MediaType.Video)
                            {
                                if (hwAccelSuccess && frame->hw_frames_ctx == null)
                                {
                                    Log("HW Acceleration Failed 2");
                                    hwAccelSuccess = false;
                                    decCtx.renderer.FrameResized();
                                }
                                mFrame.timestamp = ((long)(mFrame.pts * info.Timebase) - info.StartTime) + decCtx.cfg.audio.LatencyTicks;
                                if (MediaFrame.ProcessVideoFrame(this, mFrame, frame) != 0) mFrame = null;

                            }
                            else // Audio
                            {
                                
                                mFrame.timestamp = ((long)(mFrame.pts * info.Timebase) - info.StartTime) + decCtx.cfg.audio.DelayTicks + (info.StartTime - demuxer.decCtx.vDecoder.info.StartTime);
                                if (MediaFrame.ProcessAudioFrame(this, mFrame, frame) < 0) mFrame = null;
                            }

                            if (mFrame != null)
                            {
                                frames.Enqueue(mFrame);
                                //xf++;
                            }
                            
                            av_frame_unref(frame);
                            continue;
                        }

                        av_frame_unref(frame);
                        break;
                    }

                    if (ret == AVERROR_EOF)
                    {
                        status = Status.Ended;
                        Log("EOF");
                        if      (type == MediaType.Video && decCtx.aDecoder.status != Status.Playing) { Log("EOF All"); decCtx.status = Status.Ended; }
                        else if (type == MediaType.Audio && decCtx.vDecoder.status != Status.Playing) { Log("EOF All"); decCtx.status = Status.Ended; }
                        break;
                    }

                    if (ret != AVERROR(EAGAIN)) { Log($"[ERROR-3] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})"); break; }
                }

                Log($"Done {(allowedErrors == decCtx.cfg.decoder.MaxErrors ? "" : $"[Errors: {decCtx.cfg.decoder.MaxErrors - allowedErrors}]")}");
            }
        }

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{decCtx.player.PlayerId}] [Decoder: {type.ToString().PadLeft(5, ' ')}] {msg}"); }
    }
}