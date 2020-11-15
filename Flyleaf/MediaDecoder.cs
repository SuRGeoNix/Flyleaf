/* FFmpeg Decoding Implementation
 * ------------------------------
 * Based on SharpDX, FFmpeg [https://ffmpeg.org/] & FFmpeg.AutoGen C# .NET bindings by Ruslan Balanukhin [https://github.com/Ruslan-B/FFmpeg.AutoGen]
 * by John Stamatakis
 */
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Security;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;

using FFmpeg.AutoGen;

using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D11;

using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.AVCodecID;

using Device    = SharpDX.Direct3D11.Device;
using Resource  = SharpDX.Direct3D11.Resource;

using static SuRGeoNix.Flyleaf.MediaRouter;

namespace SuRGeoNix.Flyleaf
{
    unsafe public class MediaDecoder
    {
        #region Declaration

        MediaRouter player;

        // Audio Output Parameters [ BITS | CHANNELS | RATE ]
        AVSampleFormat _SAMPLE_FORMAT   = AVSampleFormat.AV_SAMPLE_FMT_S16; int _CHANNELS = 2; 
        public int _RATE { get; private set; } // Will be set from Input Format

        // Video Output Parameters
        
        Texture2DDescription    textDescNV12;
        Texture2DDescription    textDescYUV;
        Texture2DDescription    textDescRGB;
        Texture2D               textureFFmpeg;
        Texture2D               textureNV12;
        public KeyedMutex textureNV12Mutex;

        AVPixelFormat           _PIXEL_FORMAT   = AVPixelFormat.AV_PIX_FMT_RGBA;
        int                     _SCALING_HQ     = SWS_ACCURATE_RND | SWS_BITEXACT | SWS_LANCZOS | SWS_FULL_CHR_H_INT | SWS_FULL_CHR_H_INP;
        int                     _SCALING_LQ     = SWS_BICUBIC;
        int                     vSwsOptFlags;
        
        // Video Output Buffer [For sws_scall fall-back to rgb texture]
        IntPtr                  outBufferPtr; 
        int                     outBufferSize;
        byte_ptrArray4          outData;
        int_array4              outLineSize;

        // Contexts             [Audio]     [Video]     [Subs]      [Audio/Video]       [Subs/Video]
        public DecoderContext   audio,      video,      subs;
        AVStream*               aStream,    vStream,    sStream;
        AVCodecContext*         aCodecCtx,  vCodecCtx,  sCodecCtx;
        AVCodec*                aCodec,     vCodec,     sCodec;
        SwrContext*             swrCtx;
        SwsContext*                         swsCtx;   //sSwsCtx;

        // Status
        public enum Status
        {
            READY,
            RUNNING,
            SEEKING,
            STOPPING,
            STOPPED,
            OPENING
        }
        public Status           status  { get; set; }

        // AVIO / Custom IO Buffering
        public Action<bool, double> BufferingDone;
        const int               IOBufferSize    = 0x200000;

        // HW Acceleration
        const int               AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        public Device           avD3D11Device;
        AVBufferRef*            hw_device_ctx;
        List<AVHWDeviceType>    hwDevices;
        List<HWDeviceSupported> hwDevicesSupported;
        struct HWDeviceSupported
        {
            public AVHWDeviceType       type;
            public List<AVPixelFormat>  pixFmts;
        }
        #endregion

        #region Properties

        public class MediaFrame
        {
            public long         timestamp;
            public long         pts;

            // Video Textures
            public Texture2D    textureHW;
            public Texture2D    textureRGB;
            public Texture2D    textureY;
            public Texture2D    textureU;
            public Texture2D    textureV;

            // Audio Samples
            public byte[]       audioData;
            
            // Subtitles
            public int          duration;
            public string       text;
            public List<OSDMessage.SubStyle> subStyles;
        }
        public struct AudioStreamInfo
        {
            public short    streamIndex;
            public double   timebase;
            public double   timebaseLowTicks;
            public long     startTimeTicks;
            public long     durationTicks;
            public int      frameSize;
        }
        public struct VideoStreamInfo
        {
            public short    streamIndex;
            public double   timebase;
            public double   timebaseLowTicks;
            public long     startTimeTicks;
            public long     durationTicks;
            public long     frameAvgTicks;
            public int      height;
            public int      width;
            public double   fps;
        }

        public AudioStreamInfo  aStreamInfo;
        public VideoStreamInfo  vStreamInfo;
        double sTimbebaseLowTicks;


        public bool isRunning       => status == Status.RUNNING;
        public bool isSeeking       => status == Status.SEEKING;
        public bool isStopped       => status == Status.STOPPED;
        public bool isReady         { get; private set; }
        public bool Finished        { get; private set; }
        public bool isForBuffering  { get; set; }


        public bool hasAudio        { get; private set; }
        public bool hasVideo        { get; private set; }
        public bool hasSubs         { get; private set; }

        bool _doAudio = true;
        public bool doAudio         { 
            get { return _doAudio; }

            set
            {
                if (_doAudio == value) return;

                _doAudio = value;

                if (aStream == null) return;

                if (audio == null)
                {
                    if (video == null) return;

                    if (value)
                    {
                        if (!video.activeStreamIds.Contains(aStream->index))
                            video.activeStreamIds.Add(aStream->index);
                    }
                    else
                        video.activeStreamIds.Remove(aStream->index);
                }
                else
                {
                    if (value)
                        audio.ReSync();
                    else
                        audio.Pause();
                }

                player.aFrames = new System.Collections.Concurrent.ConcurrentQueue<MediaFrame>();
            }
        }
        bool _doSubs = true;
        public bool doSubs          { 
            get { return _doSubs; }

            set
            {
                if (_doSubs == value) return;

                _doSubs = value;

                if (sStream == null) return;

                if (subs == null)
                {
                    if (video == null) return;

                    if (value)
                    {
                        if (!video.activeStreamIds.Contains(sStream->index))
                            video.activeStreamIds.Add(sStream->index);
                    }
                    else
                        video.activeStreamIds.Remove(sStream->index);
                }
                else
                {
                    if (value)
                        subs.ReSync();
                    else
                        subs.Pause();
                }

                player.sFrames = new System.Collections.Concurrent.ConcurrentQueue<MediaFrame>();
            }
        }
        //public bool isSubsExternal  { get; private set; }

        public bool HighQuality     { get; set;         }
        public bool HWAccel         { get; set;         } = true;   // Requires re-open
        public bool hwAccelSuccess  { get; private set; }
        public int  Threads         { get; set;         } = 4;      // Requires re-open
        public int  verbosity       { get; set;         }
        #endregion

        #region Initialization
        public MediaDecoder(MediaRouter player) { this.player = player; }
        public MediaDecoder (MediaRouter player, int verbosity = 0) { this.player = player; Init(verbosity); }
        public void Init    (int verbosity = 0)
        {
            RegisterFFmpegBinaries();
            if      (verbosity == 1) { av_log_set_level(AV_LOG_ERROR);        av_log_set_callback(ffmpegLogCallback); } 
            else if (verbosity  > 1) { av_log_set_level(AV_LOG_MAX_OFFSET);   av_log_set_callback(ffmpegLogCallback); }
            this.verbosity      = verbosity;

            hwDevices           = GetHWDevices();
            hwDevicesSupported  = new List<HWDeviceSupported>();

            //Initialize();
        }
        #endregion

        #region Actions / Public
        public int Open(string url = "", string aUrl = "", string sUrl = "", string referer = "", Func<long, int, byte[]> IORead = null, long ioLength = 0)
        {
            int ret = 0;

            if (IORead != null || url  != "")
                            ret = FormatContextOpen(url, true, aUrl != "" ? false : doAudio, sUrl != "" ? false : doSubs, referer, IORead, ioLength);
            if (ret != 0) return ret;
            if (aUrl != "") ret = FormatContextOpen(aUrl, false, true, false, referer, IORead, ioLength);
            if (ret != 0) return ret;
            if (sUrl != "") ret = FormatContextOpen(sUrl, false, false, true, referer, IORead, ioLength);

            return ret;
        }
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public int FormatContextOpen(string url, bool doVideo = true, bool doAudio = true, bool doSubs = true, string referer = "", Func<long, int, byte[]> IORead = null, long ioLength = 0)
        {
            int ret = -1;

            if (doVideo) Finished = false;
            if (doVideo) isReady = false;

            DecoderContext decCtx;
            AVMediaType decType;

            if (doVideo)
                { decCtx = video; decType = AVMEDIA_TYPE_VIDEO; }
            else if (doAudio)
                { decCtx = audio; decType = AVMEDIA_TYPE_AUDIO; }
            else if (doSubs)
                { decCtx = subs;  decType = AVMEDIA_TYPE_SUBTITLE;}
            else return -1;

            if (decCtx != null) decCtx.Dispose();

            Log("Opening Format Context [" + decType + "]");
            decCtx = new DecoderContext(this, decType, verbosity);
            decCtx.fmtCtx = avformat_alloc_context();
            if (decCtx.fmtCtx == null) return ret;

            decCtx.url = url;
            if (IORead != null && ioLength != 0) decCtx.IOContextConfig(IORead, ioLength);

            AVDictionary *opt = null;
            
            // Required for Youtube-dl to avoid 403 Forbidden
            av_dict_set(&opt, "referer", referer, 0);

            /* Issue with HTTP/TLS - (sample video -> https://www.youtube.com/watch?v=sExEvN1bPRo)
             * 
             * Required probably only for AUDIO and some specific formats?
             * 
             * [tls @ 0e691280] Error in the pull function.
             * [tls @ 0e691280] The specified session has been invalidated for some reason.
             * [DECTX AVMEDIA_TYPE_AUDIO] AVMEDIA_TYPE_UNKNOWN - Error[-0005], Msg: I/O error
             */
            av_dict_set_int(&opt, "reconnect", 1, 0);
            av_dict_set_int(&opt, "reconnect_streamed", 1, 0);
            av_dict_set_int(&opt, "reconnect_delay_max", 3, 0);
            //av_dict_set_int(&opt, "reconnect_at_eof", 1, 0); Maybe will use this for another similar issues? | will not stop the decoders (no EOF)

            // set seekable to false on http options for live streaming?
            // seekable = iformat->read_seek2 ? 1 : (iformat->read_seek ? 1 : 0); ?

            // TODO: beter aboard for decoder?
            //AVIOInterruptCB_callback_func interruptClbk = new AVIOInterruptCB_callback_func();
            //AVIOInterruptCB_callback InterruptClbk = (p0) => { return 0; };

            //interruptClbk.Pointer               = Marshal.GetFunctionPointerForDelegate(InterruptClbk);
            //fmtCtx->interrupt_callback.callback = interruptClbk;
            //fmtCtx->interrupt_callback.opaque   = fmtCtx;

            fixed (AVFormatContext** ptr = &decCtx.fmtCtx) ret = avformat_open_input(ptr, url, null, &opt);
            if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

            av_format_inject_global_side_data(decCtx.fmtCtx);

            ret = avformat_find_stream_info(decCtx.fmtCtx, null);
            if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

            if (doVideo)
            {
                hasVideo = false;
                vStreamInfo = new VideoStreamInfo();

                ret = av_find_best_stream(decCtx.fmtCtx, AVMEDIA_TYPE_VIDEO,   -1, -1, null, 0);
                if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); decCtx.Dispose(); return ret; }
                vStream = decCtx.fmtCtx->streams[ret];
                hasVideo = true;
            }

            if (doAudio)
            {
                hasAudio = false;
                ret = av_find_best_stream(decCtx.fmtCtx, AVMEDIA_TYPE_AUDIO,   -1, vStream->index, null, 0);
                if (ret >= 0) { aStream = decCtx.fmtCtx->streams[ret]; hasAudio = true; }
            }
            
            if (doSubs)
            {
                hasSubs = false;
                ret = av_find_best_stream(decCtx.fmtCtx, AVMEDIA_TYPE_SUBTITLE, -1, -1, null, 0);
                if (ret >= 0) { sStream = decCtx.fmtCtx->streams[ret]; hasSubs = true; }
            }

            if (!doVideo && ((doAudio && !hasAudio) || (doSubs && !hasSubs)) )
            {
                Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));
                decCtx.Dispose();
                return ret; 
            }

            if (doVideo && hasVideo)
            {
                ret = SetupCodec(AVMEDIA_TYPE_VIDEO);
                if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                decCtx.activeStreamIds.Add(vStream->index);
            }

            if (doAudio && hasAudio)
            {
                ret = SetupCodec(AVMEDIA_TYPE_AUDIO);
                if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return ret; }

                decCtx.activeStreamIds.Add(aStream->index);
            }

            if (decCtx.type == AVMEDIA_TYPE_VIDEO)
            {
                for (int i=0; i<decCtx.fmtCtx->nb_streams; i++)
                {
                    if (decCtx.fmtCtx->streams[i]->codec->codec_type == AVMEDIA_TYPE_SUBTITLE)
                    {

                        AVCodec* sCodec = avcodec_find_decoder(decCtx.fmtCtx->streams[i]->codec->codec_id);
                        ret = avcodec_open2(decCtx.fmtCtx->streams[i]->codec, sCodec, null);
                        if (ret < 0) Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));

                        string lang = null;

                        AVDictionaryEntry* b = null;
                        while (true)
                        {
                            b = av_dict_get(decCtx.fmtCtx->streams[i]->metadata, "", b, AV_DICT_IGNORE_SUFFIX);
                            if (b == null) break;

                            if (BytePtrToStringUTF8(b->key).ToLower() == "language")
                                lang = BytePtrToStringUTF8(b->value);
                        }
                        player.AvailableSubs.Add(new SubAvailable(Language.Get(lang), i));
                    }
                }
            }
            else if (decCtx.type == AVMEDIA_TYPE_SUBTITLE)
            {
                AVCodec* sCodec = avcodec_find_decoder(sStream->codec->codec_id);
                ret = avcodec_open2(decCtx.fmtCtx->streams[sStream->index]->codec, sCodec, null);
                if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); if (!doVideo) return ret; }
                decCtx.activeStreamIds.Add(sStream->index);
                sTimbebaseLowTicks = av_q2d(sStream->time_base) * 10000 * 1000;
                //isSubsExternal = true;
            }

            for (int i=0; i<decCtx.fmtCtx->nb_streams; i++)
                if ( !decCtx.activeStreamIds.Contains(i) ) 
                    decCtx.fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;

            //TODO: In case of Custom Multi-Context & Same Url Required for matroska (AUDIO stream dont discard VIDEO)
            //if (decCtx.type == AVMEDIA_TYPE_AUDIO) decCtx.fmtCtx->streams[<<<>>>>]->discard = AVDiscard.AVDISCARD_DEFAULT;
            
            if (doVideo && hasVideo) SetupVideo(decCtx.fmtCtx);
            if (doAudio && hasAudio) SetupAudio(decCtx.fmtCtx);

            decCtx.isReady = true;
            if (doVideo && hasVideo) isReady = true;

            if (doVideo)
                video = decCtx;
            else if (doAudio)
                audio = decCtx;
            else if (doSubs)
                subs  = decCtx;

            if (isRunning && decCtx.type == AVMEDIA_TYPE_SUBTITLE)
            {
                subs.Seek(player.CurTime / 10000);
                subs.Run();
            }

            if (verbosity > 0) FormatContextDump(decCtx);

            return 0;
        }
        private void FormatContextDump(DecoderContext decCtx)
        {
            Log($"[{BytePtrToStringUTF8(decCtx.fmtCtx->iformat->long_name)} | {BytePtrToStringUTF8(decCtx.fmtCtx->iformat->name)} | {BytePtrToStringUTF8(decCtx.fmtCtx->iformat->mime_type)}]");
            Log("[" + (new TimeSpan(decCtx.fmtCtx->start_time * 10)).ToString(@"hh\:mm\:ss") + " / " + (new TimeSpan(decCtx.fmtCtx->duration * 10)).ToString(@"hh\:mm\:ss") + "]");

            Log($"============================ STREAMS ===================================");
            for (int i=0; i<decCtx.fmtCtx->nb_streams; i++)
            {
                if (decCtx.fmtCtx->streams[i]->codec->codec_type == AVMEDIA_TYPE_VIDEO)
                    Log($"{i}. [{decCtx.fmtCtx->streams[i]->codec->codec_type.ToString().Replace("AVMEDIA_TYPE_", "")} | {decCtx.fmtCtx->streams[i]->codec->codec_id.ToString().Replace("AV_CODEC_ID_", "")} | {decCtx.fmtCtx->streams[i]->codec->pix_fmt.ToString().Replace("AV_PIX_FMT_","")} | {decCtx.fmtCtx->streams[i]->codec->width} x {decCtx.fmtCtx->streams[i]->codec->height}] [{(new TimeSpan(av_rescale_q(decCtx.fmtCtx->streams[i]->duration, decCtx.fmtCtx->streams[i]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss")} / {(new TimeSpan(av_rescale_q(decCtx.fmtCtx->streams[i]->duration, decCtx.fmtCtx->streams[i]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss")}] | {decCtx.fmtCtx->streams[i]->codec->time_base.num} / {decCtx.fmtCtx->streams[i]->codec->time_base.den}");
                else
                    Log($"{i}. [{decCtx.fmtCtx->streams[i]->codec->codec_type.ToString().Replace("AVMEDIA_TYPE_", "")} | {decCtx.fmtCtx->streams[i]->codec->codec_id.ToString().Replace("AV_CODEC_ID_", "")}] [{(new TimeSpan(av_rescale_q(decCtx.fmtCtx->streams[i]->duration, decCtx.fmtCtx->streams[i]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss")} / {(new TimeSpan(av_rescale_q(decCtx.fmtCtx->streams[i]->duration, decCtx.fmtCtx->streams[i]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss")}] | {decCtx.fmtCtx->streams[i]->codec->time_base.num} / {decCtx.fmtCtx->streams[i]->codec->time_base.den}");
            }
            Log($"========================================================================");

            Log($"============================ METADATA ===================================");
            for (int i = 0; i < decCtx.fmtCtx->nb_streams; i++)
            {
                Log($"{i}. [{decCtx.fmtCtx->streams[i]->codec->codec_type.ToString().Replace("AVMEDIA_TYPE_", "")} | {decCtx.fmtCtx->streams[i]->codec->codec_id.ToString().Replace("AV_CODEC_ID_", "")}] [{(new TimeSpan(av_rescale_q(decCtx.fmtCtx->streams[i]->duration, decCtx.fmtCtx->streams[i]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss")} / {(new TimeSpan(av_rescale_q(decCtx.fmtCtx->streams[i]->duration, decCtx.fmtCtx->streams[i]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss")}] | {decCtx.fmtCtx->streams[i]->codec->time_base.num} / {decCtx.fmtCtx->streams[i]->codec->time_base.den}");
                Dictionary<string, string> metaEntries = new Dictionary<string, string>();

                AVDictionaryEntry* b = null;
                while (true)
                {
                    b = av_dict_get(decCtx.fmtCtx->streams[i]->metadata, "", b, AV_DICT_IGNORE_SUFFIX);
                    if (b == null) break;
                    metaEntries.Add(BytePtrToStringUTF8(b->key), BytePtrToStringUTF8(b->value));

                }
                foreach (KeyValuePair<string, string> metaEntry in metaEntries)
                    Log($"{metaEntry.Key} -> {metaEntry.Value}");

                Log($"=======================================================================");
            }

            //av_dump_format(decCtx.fmtCtx, 0, url, 0);
        }

        public void Seek(long ms, bool onlyVideo = false, bool foreward = false)
        {
            video?.Seek(ms, foreward);
            if (onlyVideo) return;

            ReSync();
        }
        public void ReSync()
        {
            if (subs == null && audio == null) return;

            if (!video.isRunning) video.DecodeFrame();
            if (player.vFrames.Count == 0) return;

            player.vFrames.TryPeek(out MediaFrame vFrame);

            Log("Resyncing at " + vFrame.timestamp / 10000);

            audio?.Seek(vFrame.timestamp / 10000);
            subs ?.Seek(vFrame.timestamp / 10000);

            if (player.isPlaying)
            {
                audio?.Run();
                subs?.Run();
            }
        }
        public void Pause()
        {
            if (!isReady) return;

            status = Status.STOPPING;
            video?.Pause();
            audio?.Pause();
            subs ?.Pause();
            status = Status.STOPPED;
        }
        public void Run()
        {
            if (!isReady) return;

            if (isRunning) Pause();

            status = Status.RUNNING;

            if (video.requiresResync)
            {
                ReSync();
                video.requiresResync = false;
            }
            else
            {
                audio?.Run();
                subs ?.Run();
            }
            video?.Run();
            
        }
        #endregion

        #region Decoder Context
        public class DecoderContext
        {
            #region Declaration / Properties
            MediaDecoder    dec;
            int             verbosity;

            // Format Context (Type/Packet/Frame)
            public  AVFormatContext*    fmtCtx;
            public  AVMediaType         type; // Multi (V - A | S) , Single (A | S)
                    AVPacket*           pkt;
                    AVFrame*            frame;

            // Status / Activity
            Thread              runThread   = new Thread(() => { });
            Status              status      = Status.STOPPED;
            public bool         isRunning => status == Status.RUNNING;
            public bool         isReady;
            public bool         finished;

            public string       url;
            public List<int>    activeStreamIds = new List<int>();
            public bool         drainMode;
            public bool         hasMoreFrames;
            public bool         hwFramesInit;
            
            // Custom IO / Buffering
            AVIOContext*        IOCtx;
            List<object>        gcPrevent = new List<object>();
            long                ioPos;

            public bool         requiresResync;
            int errors;
            #endregion

            #region Initialization
            public DecoderContext(MediaDecoder dec, AVMediaType mType, int verbosity = 0)
            {
                this.verbosity = verbosity;
                this.dec = dec;
                type = mType;
                pkt = av_packet_alloc();
            }

            public void DisableEmbeddedSubs()
            {
                if (type != AVMEDIA_TYPE_VIDEO) return;

                for (int i=0; i<fmtCtx->nb_streams; i++)
                {
                    if (fmtCtx->streams[i]->codec->codec_type == AVMEDIA_TYPE_SUBTITLE)
                    {
                        fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
                        if (activeStreamIds.Contains(i)) activeStreamIds.Remove(i);
                    }
                }

                dec.sStream = null;
            }
            public void EnableEmbeddedSubs(int streamIndex)
            {
                if (type != AVMEDIA_TYPE_VIDEO || !dec.doSubs) return;

                DisableEmbeddedSubs();  

                if (dec.subs != null) { dec.subs.Dispose(); dec.subs = null; }

                if (fmtCtx->streams[streamIndex]->codec->codec_type == AVMEDIA_TYPE_SUBTITLE)
                {
                    avcodec_flush_buffers(fmtCtx->streams[streamIndex]->codec);
                    fmtCtx->streams[streamIndex]->discard = AVDiscard.AVDISCARD_DEFAULT;
                    if (!activeStreamIds.Contains(streamIndex)) activeStreamIds.Add(streamIndex);
                    dec.sStream = fmtCtx->streams[streamIndex];
                    dec.sTimbebaseLowTicks = av_q2d(dec.sStream->time_base) * 10000 * 1000;
                }
            }
            public List<int> GetRegisteredStreams()
            {
                List<int> streams = new List<int>();
                for (int i=0; i<fmtCtx->nb_streams; i++)
                    if (fmtCtx->streams[i]->discard != AVDiscard.AVDISCARD_ALL) streams.Add(i);

                return streams;
            }

            public void RegisterStreams(List<int> streams)
            {
                activeStreamIds = new List<int>();
                for (int i=0; i<fmtCtx->nb_streams; i++)
                {
                    if (streams.Contains(i))
                    {
                        fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_DEFAULT;
                        activeStreamIds.Add(i);
                    }
                    else
                    {
                        fmtCtx->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
                    }
                }            
            }


            public void Dispose()
            {
                Pause();

                if (type == AVMEDIA_TYPE_VIDEO)
                {
                    if (dec.swsCtx != null) { sws_freeContext(dec.swsCtx); dec.swsCtx = null; }

                    dec.audio?.Dispose(); dec.audio = null;
                    dec.subs?. Dispose(); dec.subs  = null;
                    dec.aStream = null;
                    dec.sStream = null;
                    dec.aCodecCtx = null;
                    dec.vCodecCtx = null;
                    dec.sCodecCtx = null;

                    dec.player.AvailableSubs = new List<SubAvailable>();
                }
                else if (type == AVMEDIA_TYPE_AUDIO)
                {
                    dec.aStream = null;
                    dec.audio   = null;
                    dec.aCodecCtx = null;
                }
                else if (type == AVMEDIA_TYPE_SUBTITLE)
                {
                    dec.sStream = null;
                    dec.subs    = null;
                    dec.sCodecCtx = null;
                }

                if (IOCtx != null) IOCtx = null;
                if (pkt  != null) av_packet_unref(pkt);
                if (frame!= null) fixed (AVFrame** ptr = &frame) av_frame_free(ptr);
                if (fmtCtx!=null) fixed (AVFormatContext** ptr = &fmtCtx) if (fmtCtx != null) avformat_close_input(ptr);
            }
            #endregion

            #region Public Actions
            public void ReSync()
            {
                if (dec.player.vFrames.Count == 0 || type == AVMEDIA_TYPE_VIDEO) return;

                if ((type == AVMEDIA_TYPE_AUDIO && !dec.doAudio) || type == AVMEDIA_TYPE_SUBTITLE && !dec.doSubs) return;

                dec.player.vFrames.TryPeek(out MediaFrame vFrame);

                Log("Resyncing at " + Utils.TicksToTime(vFrame.timestamp));

                if (type == AVMEDIA_TYPE_AUDIO)
                {
                    dec.audio?.Seek(vFrame.timestamp / 10000);
                    if(dec.player.isPlaying) dec.audio?.Run();
                }
                else
                {
                    dec.subs ?.Seek(vFrame.timestamp / 10000);
                    if(dec.player.isPlaying) dec.subs ?.Run();
                }
            }
            public void Seek(long ms, bool foreward = false, bool seek2any = false)
            {
                if (!isReady) return;

                Pause();

                if ((type == AVMEDIA_TYPE_AUDIO && !dec.doAudio) || type == AVMEDIA_TYPE_SUBTITLE && !dec.doSubs) return;

                Log("Seek at " + Utils.TicksToTime(ms * 10000));
                
                status = Status.SEEKING;

                finished        = false;
                drainMode       = false;
                hasMoreFrames   = false;

                if (type == AVMEDIA_TYPE_VIDEO)
                {
                    if (!dec.isForBuffering)
                    {
                        dec.player.ClearMediaFrames();
                        if (dec.hasAudio) avcodec_flush_buffers(dec.aCodecCtx);
                        if (dec.hasSubs ) avcodec_flush_buffers(fmtCtx->streams[dec.sStream->index]->codec);

                        requiresResync  = true;
                    }

                    dec.Finished    = false;
                    hwFramesInit    = false;
                    avcodec_flush_buffers(dec.vCodecCtx);
                    
                    if (seek2any)
                        avformat_seek_file(fmtCtx, -1, Int64.MinValue, ms * 1000, ms * 1000, AVSEEK_FLAG_ANY);
                    else if (foreward)
                        av_seek_frame(fmtCtx, -1, ms * 1000, AVSEEK_FLAG_FRAME);
                    else
                        av_seek_frame(fmtCtx, -1, ms * 1000, AVSEEK_FLAG_BACKWARD);

                    // FLV Seek Issue (should use byte pos?)
                    //long bytepos = dec.vStream->index_entries[av_index_search_timestamp(dec.vStream, ms * 1000, AVSEEK_FLAG_BACKWARD)].pos;
                }
                else if (type == AVMEDIA_TYPE_AUDIO)
                {
                    dec.player.aFrames = new System.Collections.Concurrent.ConcurrentQueue<MediaFrame>();

                    if (dec.hasAudio) avcodec_flush_buffers(dec.aCodecCtx);

                    //avformat_seek_file(fmtCtx, -1, Int64.MinValue, ms * 1000, ms * 1000, AVSEEK_FLAG_ANY);  // - (AudioPlayer.NAUDIO_DELAY_MS * 1000)); | We Add the Audio Delay to Video/Subs Timestamp (because is backwards)

                    //av_seek_frame(fmtCtx, -1, ms * 1000, AVSEEK_FLAG_BACKWARD);
                    avformat_seek_file(fmtCtx, -1, Int64.MinValue, ms * 1000 - ((dec.player.AudioExternalDelay / 10) + AudioPlayer.NAUDIO_DELAY_MS), Int64.MaxValue, AVSEEK_FLAG_ANY);
                }
                else if (type == AVMEDIA_TYPE_SUBTITLE)
                {
                    dec.player.sFrames = new System.Collections.Concurrent.ConcurrentQueue<MediaFrame>();

                    avcodec_flush_buffers(fmtCtx->streams[dec.sStream->index]->codec);

                    avformat_seek_file(fmtCtx, -1, Int64.MinValue, ms * 1000 - ((dec.player.SubsExternalDelay / 10) + AudioPlayer.NAUDIO_DELAY_MS), Int64.MaxValue, AVSEEK_FLAG_ANY);
                }
            }
            public void Pause()
            {
                status = Status.STOPPING;
                Utils.EnsureThreadDone(runThread);
                status = Status.STOPPED;
            }
            public void Run()
            {
                if (!isReady) return;

                Pause();

                if ((type == AVMEDIA_TYPE_AUDIO && !dec.doAudio) || type == AVMEDIA_TYPE_SUBTITLE && !dec.doSubs) return;

                status = Status.RUNNING;

                runThread = new Thread(() =>
                {
                    Log(status.ToString());
                    DecodeFrames();
                    status = Status.STOPPED;
                    Log(status.ToString());
                });
                runThread.SetApartmentState(ApartmentState.STA);
                runThread.Start();
            }
            #endregion 

            #region Main Implementation
            public void BufferPackets(int fromMs, int duration, bool foreward = false)
            {
                if (!isReady) return;

                Seek(fromMs, foreward); // Making sure it will start at the same point as the main decoder (I/B/P)

                runThread = new Thread(() =>
                {
                    int ret = 0;
                    status = Status.RUNNING;
                    Log(status.ToString());

                    bool        informed        = false;
                    double      fromTimestamp   = -1;
                    double      curTimestamp    =  0;
                    double      endTimestamp    =  0; //(fromMs + duration) / 1000.0;
                    List<int>   doneStreams     = new List<int>();

                    long        informInterval  = 0; // To avoid spamming threads & rendering

                    while (isRunning && ret == 0)
                    {
                        av_packet_unref(pkt);
                        ret = av_read_frame(fmtCtx, pkt);

                        if (doneStreams.Count == activeStreamIds.Count)
                        {
                            if (informed) continue;
                            informed = true;
                            dec.BufferingDone?.BeginInvoke(true, 1, null, null);
                            Log($"Buffering success to -> {Utils.TicksToTime((long)(pkt->dts * av_q2d(fmtCtx->streams[pkt->stream_index]->time_base) * 1000 * (long)10000))} | Requested for {Utils.TicksToTime((long) (endTimestamp * 1000 * (long)10000))}");
                        }

                        if (doneStreams.     Contains(pkt->stream_index)) continue;
                        if (!activeStreamIds.Contains(pkt->stream_index)) continue;

                        curTimestamp = pkt->dts * av_q2d(fmtCtx->streams[pkt->stream_index]->time_base);

                        if (curTimestamp >= 0)
                        {
                            if (fromTimestamp == -1)
                            {
                                fromTimestamp   = curTimestamp;
                                endTimestamp    = fromTimestamp + (duration / 1000.0);
                                Log($"Buffering first timestamp -> {Utils.TicksToTime((long)(pkt->dts * av_q2d(fmtCtx->streams[pkt->stream_index]->time_base) * 1000 * (long)10000))} | Requested for {Utils.TicksToTime((long) (endTimestamp * 1000 * (long)10000))}");
                            }

                            if ( curTimestamp > endTimestamp) doneStreams.Add(pkt->stream_index);
                        }
                    }
                    status = Status.STOPPED;
                    Log(status.ToString());

                    if (!informed && ret == AVERROR_EOF) dec.BufferingDone?.BeginInvoke(true, 1, null, null);
                });
                runThread.SetApartmentState(ApartmentState.STA);
                runThread.Start();
            }
            public void DecodeFrame()
            {
                if (!isReady) return;

                Pause();
                dec.player.ClearMediaFrames();

                int ret = 0;
                AVMediaType mType;
            
                while ((ret = GetNextFrame(out mType)) == 0)
                {
                    if (frame == null && mType != AVMEDIA_TYPE_SUBTITLE) continue;

                    if (mType == AVMEDIA_TYPE_AUDIO)
                    {
                        dec.ProcessAudioFrame(frame);
                        fixed (AVFrame** ptr = &frame) av_frame_free(ptr);
                        if (type == AVMEDIA_TYPE_AUDIO) break;
                    }   
                    else if (mType == AVMEDIA_TYPE_VIDEO)
                    {
                        dec.ProcessVideoFrame(frame);
                        fixed (AVFrame** ptr = &frame) av_frame_free(ptr);
                        hwFramesInit = false;
                        if (type == AVMEDIA_TYPE_VIDEO) break;
                    }
                    else if (mType == AVMEDIA_TYPE_SUBTITLE)
                    {
                        dec.DecodeFrameSubs(fmtCtx->streams[dec.sStream->index]->codec, pkt);
                        if (type == AVMEDIA_TYPE_SUBTITLE) break;
                    }
                }
            }
            public void DecodeFrames()
            {
                //bool once = true;
                int ret = 0;
                errors = 0;
                AVMediaType mType = AVMEDIA_TYPE_UNKNOWN;
                
                /* TODO
                 * 
                 * Seperate threading for Demuxing Packets & Decoding Frames
                 * Required especially for streaming to allow demuxing continue based on required bitrate
                 * Also thread sleeps will prevent fast demuxing as well here for now
                 */

                while (dec.isRunning && isRunning && (ret = GetNextFrame(out mType)) != AVERROR_EOF && errors < 200)
                {
                    if (ret != 0) { errors++; if (pkt != null) Log("Stream " + pkt->stream_index.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); continue; }

                    if (frame == null && mType != AVMEDIA_TYPE_SUBTITLE) continue;

                    //if (pkt != null && frame != null) Console.WriteLine("F | " + pkt->stream_index + " | " + (new TimeSpan(av_rescale_q(frame->best_effort_timestamp, fmtCtx->streams[pkt->stream_index]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss") + " | " + frame->best_effort_timestamp  + " | " + av_rescale_q(frame->best_effort_timestamp, fmtCtx->streams[pkt->stream_index]->time_base, av_get_time_base_q()));
                    //if (pkt != null && mType == AVMEDIA_TYPE_SUBTITLE) Console.WriteLine("S | "  + pkt->stream_index + " | " + (pkt->pts * dec.sTimbebaseLowTicks));

                    switch (mType)
                    {
                        case AVMEDIA_TYPE_AUDIO:
                            dec.ProcessAudioFrame(frame);
                            fixed (AVFrame** ptr = &frame) av_frame_free(ptr);
                            break;

                        case AVMEDIA_TYPE_VIDEO:
                            dec.ProcessVideoFrame(frame);
                            fixed (AVFrame** ptr = &frame) av_frame_free(ptr);
                            //if (once)
                            //{
                            //    if (pkt != null && mType == AVMEDIA_TYPE_SUBTITLE) Console.WriteLine("A | "  + pkt->stream_index + " | " + (new TimeSpan(av_rescale_q(frame->best_effort_timestamp, fmtCtx->streams[pkt->stream_index]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss\:fff") + " | " + new TimeSpan((long)(frame->best_effort_timestamp * dec.aStreamInfo.timebaseLowTicks)).ToString(@"hh\:mm\:ss\:fff"));
                            //    once = false;
                            //}
                            break;

                        case AVMEDIA_TYPE_SUBTITLE:
                            //if (once)
                            //{
                            //    if (pkt != null && mType == AVMEDIA_TYPE_SUBTITLE) Console.WriteLine("S | "  + pkt->stream_index + " | " + (new TimeSpan(av_rescale_q(pkt->pts, fmtCtx->streams[pkt->stream_index]->time_base, av_get_time_base_q()) * 10)).ToString(@"hh\:mm\:ss\:fff") + " | " + new TimeSpan((long)(pkt->pts * dec.sTimbebaseLowTicks)).ToString(@"hh\:mm\:ss\:fff"));
                            //    once = false;
                            //}
                            dec.DecodeFrameSubs(fmtCtx->streams[dec.sStream->index]->codec, pkt);
                            break;
                    }

                    // Probably should remove Audio/Subtitles from here (only Video frames should control the flow, at least if we talk about video player and not audio player)
                    while   (type == AVMEDIA_TYPE_SUBTITLE  && dec.isRunning && dec.player.sFrames.Count > dec.player.SUBS_MAX_QUEUE_SIZE)
                        Thread.Sleep(70);

                    while   (type == AVMEDIA_TYPE_AUDIO     && dec.isRunning && dec.player.aFrames.Count > dec.player.AUDIO_MAX_QUEUE_SIZE)
                        Thread.Sleep(20);

                    // We use 3 * max queue size for live streaming (currently live stream means duration = 0 | possible check if hls?)
                    // We need to ensure that we have also enough Audio Samples (for embedded audio stream) - is it possible to calculate this?
                    while   (type == AVMEDIA_TYPE_VIDEO     && dec.isRunning && dec.player.vFrames.Count > (dec.player.Duration == 0 ? dec.player.VIDEO_MAX_QUEUE_SIZE * 3 : dec.player.VIDEO_MAX_QUEUE_SIZE))
                        Thread.Sleep(5);
                }

                if (ret != 0 )
                {
                    if (ret != AVERROR_EOF) Log(mType.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));
                    if (type == AVMEDIA_TYPE_VIDEO) { requiresResync  = dec.isForBuffering ? false : true; dec.Finished = true; }
                }

                if (errors == 200) Log(mType.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: Too many errors");

                status = Status.STOPPED;
                if (type == AVMEDIA_TYPE_VIDEO) dec.status = Status.STOPPED;
            }
            public int  GetNextFrame(out AVMediaType mType)
            {
                int ret = 0;
                mType = AVMEDIA_TYPE_UNKNOWN;

                if (drainMode)
                {
                    frame   = av_frame_alloc();
                    mType   = AVMEDIA_TYPE_VIDEO;
                    ret     = avcodec_receive_frame(dec.vCodecCtx, frame);

                    if (ret == 0) { Log("Drain Frame " + type); hasMoreFrames = true; return ret; }
                    if (ret == AVERROR(EAGAIN)) return GetNextFrame(out mType);

                    hasMoreFrames   = false;
                    drainMode       = false;
                    finished        = true;

                    return ret;
                }

                if (!hasMoreFrames)
                {
                    av_packet_unref(pkt);
                    ret = av_read_frame(fmtCtx, pkt);

                    if (ret == AVERROR_EOF)
                    { 
                        if (type != AVMEDIA_TYPE_VIDEO) return AVERROR_EOF;

                        drainMode = true;
                        av_packet_unref(pkt);

                        ret = avcodec_send_packet(dec.vCodecCtx, null);
                        if (ret != 0 && ret != AVERROR(EAGAIN)) return ret;

                        return GetNextFrame(out mType);
                    }

                    if (ret != 0 && ret!= AVERROR_EOF) return ret;

                    if (!activeStreamIds.Contains(pkt->stream_index)) return GetNextFrame(out mType);
                    if (fmtCtx->streams[pkt->stream_index]->codecpar->codec_type == AVMEDIA_TYPE_SUBTITLE) { mType = AVMEDIA_TYPE_SUBTITLE; return ret; }

                    ret = avcodec_send_packet(fmtCtx->streams[pkt->stream_index]->codec, pkt);

                    // Allow INVALID DATA?
                    if (ret == AVERROR_INVALIDDATA) { errors++; Log("Stream " + pkt->stream_index.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); return 0; }

                    if (ret != 0 && ret != AVERROR(EAGAIN)) return ret;
                }

                frame = av_frame_alloc();
                ret = avcodec_receive_frame(fmtCtx->streams[pkt->stream_index]->codec, frame);

                if (ret == 0)
                {
                    hasMoreFrames = true;

                    if (type == AVMEDIA_TYPE_VIDEO && pkt->stream_index == dec.vStream->index)
                    { 
                        mType = AVMEDIA_TYPE_VIDEO;

                        // Couldn't find a way to do it directly [libavutil/hwcontext_d3d11va.c -> d3d11va_frames_init()] - Without it, first frame (after flush buffers) will be YUV Green screen (still happens few times) - maybe will retry with CreateQuery/GetData event
                        if (!hwFramesInit || dec.player.vFrames.Count == 0)
                        {
                            // In case GPU fails to alocate FFmpeg decoding texture
                            if (dec.hwAccelSuccess && frame->hw_frames_ctx == null) dec.hwAccelSuccess = false;
                            if (dec.hwAccelSuccess) Thread.Sleep(40);
                            hwFramesInit = true;
                        }
                    }
                    else if (pkt->stream_index == dec.aStream->index)
                        mType = AVMEDIA_TYPE_AUDIO;

                    return ret; 
                }

                hasMoreFrames = false;

                if (ret == AVERROR(EAGAIN)) return GetNextFrame(out mType);

                return ret;
            }
            public void IOContextConfig(Func<long, int, byte[]> ReadPacketClbk, long totalSize)
            {   
                gcPrevent.Clear();
                ioPos = 0;
                
                avio_alloc_context_read_packet IOReadPacket = (opaque, buffer, bufferSize) =>
                {
                    try
                    {
                        if (ioPos >= totalSize) return AVERROR_EOF;

                        int bytesRead   = ioPos + bufferSize > totalSize ? (int) (totalSize - ioPos) : bufferSize;
                        byte[] data     = ReadPacketClbk(ioPos, bytesRead);
                        if (data == null || data.Length < bytesRead) { Log($"[CASE 001] A Empty Data"); return -1; }

                        Marshal.Copy(data, 0, (IntPtr) buffer, bytesRead);
                        ioPos += bytesRead;

                        return bytesRead;
                    } 
                    catch (ThreadAbortException t) { Log($"[CASE 001] A Killed Empty Data"); throw t; }
                    catch (Exception e) { Log("[CASE 001] A " + e.Message + "\r\n" + e.StackTrace); return -1; }
                };

                avio_alloc_context_seek IOSeek = (opaque, offset, wehnce) =>
                {
                    try
                    {
                        if ( wehnce == AVSEEK_SIZE )
                            return totalSize;
                        else if ( (SeekOrigin) wehnce == SeekOrigin.Begin )
                            ioPos = offset;
                        else if ( (SeekOrigin) wehnce == SeekOrigin.Current )
                            ioPos += offset;
                        else if ( (SeekOrigin) wehnce == SeekOrigin.End )
                            ioPos = totalSize - offset;
                        else
                            ioPos = -1;

                        return ioPos;
                    }
                    catch (ThreadAbortException t) { Log($"[CASE 001] A Seek Killed"); throw t; }
                };

                avio_alloc_context_read_packet_func ioread = new avio_alloc_context_read_packet_func();
                ioread.Pointer = Marshal.GetFunctionPointerForDelegate(IOReadPacket);
            
                avio_alloc_context_seek_func ioseek = new avio_alloc_context_seek_func();
                ioseek.Pointer = Marshal.GetFunctionPointerForDelegate(IOSeek);
            
                byte* aReadBuffer = (byte*)av_malloc(IOBufferSize);
                IOCtx = avio_alloc_context(aReadBuffer, IOBufferSize, 0, null, ioread, null, ioseek);
                fmtCtx->pb = IOCtx;
                fmtCtx->flags |= AVFMT_FLAG_CUSTOM_IO;

                gcPrevent.Add(ioread);
                gcPrevent.Add(ioseek);
                gcPrevent.Add(IOReadPacket);
                gcPrevent.Add(IOSeek);
            }
            #endregion

            void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [DECTX " + (dec.isForBuffering ? "BUFFER " : "") + $"{type}] {msg}"); }
        }
        #endregion

        #region Process AVS
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private int ProcessVideoFrame(AVFrame* frame)
        {
            /* TODO
             * 
             * Transfer all the post-process logic here (from MediaRenderer VideoProcessorBlt etc)
             * Use only pixel shaders like the masters did it here -> 
             * https://github.com/videolan/vlc/blob/master/modules/video_output/win32/d3d11_shaders.c
             * https://github.com/videolan/vlc/blob/777f36c15564b076bf13af6641493d97cd5ee224/modules/video_chroma/dxgi_fmt.c
             * https://github.com/videolan/vlc/blob/777f36c15564b076bf13af6641493d97cd5ee224/modules/video_chroma/i420_rgb_c.h
             * https://github.com/videolan/vlc/blob/777f36c15564b076bf13af6641493d97cd5ee224/modules/video_chroma/d3d11_fmt.c
             */

            int ret = 0;

            try
            {
                MediaFrame mFrame   = new MediaFrame();
                mFrame.pts          = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                mFrame.timestamp    = (long)((mFrame.pts * vStreamInfo.timebaseLowTicks) + (AudioPlayer.NAUDIO_DELAY_MS * (long)10000));
                if (mFrame.pts == AV_NOPTS_VALUE) return -1;

                // Hardware Frame (NV12)        | AVDevice NV12 -> Device NV12 -> VideoProcessBlt RGBA
                if (hwAccelSuccess)
                {
                    textureFFmpeg       = new Texture2D((IntPtr) frame->data.ToArray()[0]);
                    textDescNV12.Format = textureFFmpeg.Description.Format;
                    textureNV12         = new Texture2D(avD3D11Device, textDescNV12);

                    avD3D11Device.ImmediateContext.CopySubresourceRegion(textureFFmpeg, (int) frame->data.ToArray()[1], null, textureNV12, 0);
                    avD3D11Device.ImmediateContext.Flush();

                    SharpDX.DXGI.Resource sharedResource    = textureNV12.QueryInterface<SharpDX.DXGI.Resource>();
                    mFrame.textureHW = player.renderer.device.OpenSharedResource<Texture2D>(sharedResource.SharedHandle);
                    player.vFrames.Enqueue(mFrame);

                    Utilities.Dispose(ref sharedResource);
                    Utilities.Dispose(ref textureNV12);
                }

                // Software Frame (YUV420P)     | YUV byte* -> Device YUV (srv R8 * 3) -> PixelShader YUV->RGBA
                else if (frame->format == (int)AVPixelFormat.AV_PIX_FMT_YUV420P)
                {
                    textDescYUV.Width   = vCodecCtx->width;
                    textDescYUV.Height  = vCodecCtx->height;

                    DataStream dsY = new DataStream(frame->linesize.ToArray()[0] * vCodecCtx->height, true, true);
                    DataStream dsU = new DataStream(frame->linesize.ToArray()[1] * vCodecCtx->height / 2, true, true);
                    DataStream dsV = new DataStream(frame->linesize.ToArray()[2] * vCodecCtx->height / 2, true, true);

                    DataBox dbY = new DataBox();
                    DataBox dbU = new DataBox();
                    DataBox dbV = new DataBox();

                    dbY.DataPointer = dsY.DataPointer;
                    dbU.DataPointer = dsU.DataPointer;
                    dbV.DataPointer = dsV.DataPointer;

                    dbY.RowPitch = frame->linesize.ToArray()[0];
                    dbU.RowPitch = frame->linesize.ToArray()[1];
                    dbV.RowPitch = frame->linesize.ToArray()[2];

                    dsY.WriteRange((IntPtr)frame->data.ToArray()[0], dsY.Length);
                    dsU.WriteRange((IntPtr)frame->data.ToArray()[1], dsU.Length);
                    dsV.WriteRange((IntPtr)frame->data.ToArray()[2], dsV.Length);

                    mFrame.textureY = new Texture2D(player.renderer.device, textDescYUV, new DataBox[] { dbY });
                    textDescYUV.Width = vCodecCtx->width / 2;
                    textDescYUV.Height = vCodecCtx->height / 2;

                    mFrame.textureU = new Texture2D(player.renderer.device, textDescYUV, new DataBox[] { dbU });
                    mFrame.textureV = new Texture2D(player.renderer.device, textDescYUV, new DataBox[] { dbV });

                    Utilities.Dispose(ref dsY);
                    Utilities.Dispose(ref dsU);
                    Utilities.Dispose(ref dsV);

                    player.vFrames.Enqueue(mFrame);
                }

                // Software Frame (OTHER/sws_scale) | X byte* -> Sws_Scale RGBA -> Device RGA
                else if (!hwAccelSuccess) 
                {
                    if (swsCtx == null)
                    {
                        outData                         = new byte_ptrArray4();
                        outLineSize                     = new int_array4();
                        outBufferSize                   = av_image_get_buffer_size(_PIXEL_FORMAT, vCodecCtx->width, vCodecCtx->height, 1);
                        Marshal.FreeHGlobal(outBufferPtr);
                        outBufferPtr                    = Marshal.AllocHGlobal(outBufferSize);
                        
                        av_image_fill_arrays(ref outData, ref outLineSize, (byte*)outBufferPtr, _PIXEL_FORMAT, vCodecCtx->width, vCodecCtx->height, 1);
                        swsCtx = sws_getContext(vCodecCtx->width, vCodecCtx->height, vCodecCtx->pix_fmt, vCodecCtx->width, vCodecCtx->height, _PIXEL_FORMAT, vSwsOptFlags, null, null, null);
                    }
                    ret = sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, outData, outLineSize);

                    if (ret < 0) { Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret)); }
                    ret = 0;

                    DataStream ds = new DataStream(outLineSize[0] * vCodecCtx->height, true, true);
                    DataBox db = new DataBox();

                    db.DataPointer = ds.DataPointer;
                    db.RowPitch = outLineSize[0];
                    ds.WriteRange((IntPtr)outData.ToArray()[0], ds.Length);

                    mFrame.textureRGB = new Texture2D(player.renderer.device, textDescRGB, new DataBox[] { db });
                    Utilities.Dispose(ref ds);
                    
                    player.vFrames.Enqueue(mFrame);
                }

                return ret;

            } catch (ThreadAbortException) {
            } catch (Exception e) { ret = -1;  Log("Error[" + (ret).ToString("D4") + "], Func: ProcessVideoFrame(), Msg: " + e.Message + " - " + e.StackTrace); }

            return ret;
        }
        private int ProcessAudioFrame(AVFrame* frame)
        {
            int ret = 0;

            try
            {
                var bufferSize  = av_samples_get_buffer_size(null, _CHANNELS, frame->nb_samples, _SAMPLE_FORMAT, 1);
                byte[] buffer   = new byte[bufferSize];

                fixed (byte** buffers = new byte*[8])
                {
                    fixed (byte* bufferPtr = &buffer[0])
                    {
                        // Convert
                        buffers[0]          = bufferPtr;
                        swr_convert(swrCtx, buffers, frame->nb_samples, (byte**)&frame->data, frame->nb_samples);

                        // Send Frame
                        if (frame->nb_samples > 0)
                        {
                            MediaFrame mFrame   = new MediaFrame();
                            mFrame.audioData         = new byte[bufferSize]; System.Buffer.BlockCopy(buffer, 0, mFrame.audioData, 0, bufferSize);
                            mFrame.pts          = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                            mFrame.timestamp    = (long)(mFrame.pts * aStreamInfo.timebaseLowTicks) + player.AudioExternalDelay;
                            if (mFrame.pts == AV_NOPTS_VALUE) return -1;

                            player.aFrames.Enqueue(mFrame);
                            //SendFrame(mFrame, AVMEDIA_TYPE_AUDIO);
                        }
                    }
                }
            } catch (ThreadAbortException) { 
            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessAudioFrame(), Msg: " + e.StackTrace); }

            return ret;
        }
        private int ProcessSubsFrame(AVPacket* avpacket, AVSubtitle* sub)
        {
            int ret = 0;

            try
            {
                string line = "";
                byte[] buffer;
                AVSubtitleRect** rects = sub->rects;
                AVSubtitleRect* cur = rects[0];
                
                switch (cur->type)
                {
                    case AVSubtitleType.SUBTITLE_ASS:
                        buffer = new byte[1024];
                        line = BytePtrToStringUTF8(cur->ass);
                        break;

                    case AVSubtitleType.SUBTITLE_TEXT:
                        buffer = new byte[1024];
                        line = BytePtrToStringUTF8(cur->ass);

                        break;

                    case AVSubtitleType.SUBTITLE_BITMAP:
                        Log("Subtitles BITMAP -> Not Implemented yet");

                        break;
                }

                MediaFrame mFrame   = new MediaFrame();
                mFrame.text         = Subtitles.SSAtoSubStyles(line, out List<OSDMessage.SubStyle> subStyles);
                mFrame.subStyles    = subStyles;
                mFrame.pts          = avpacket->pts;
                mFrame.timestamp    = (long) ((mFrame.pts * sTimbebaseLowTicks) + (AudioPlayer.NAUDIO_DELAY_MS * (long)10000) + player.SubsExternalDelay);
                mFrame.duration     = (int) (sub->end_display_time - sub->start_display_time);
                if (mFrame.pts == AV_NOPTS_VALUE) return -1;

                player.sFrames.Enqueue(mFrame);
                //SendFrame(mFrame, AVMEDIA_TYPE_SUBTITLE);

            } catch (ThreadAbortException) {
            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessSubsFrame(), Msg: " + e.StackTrace); }

            return ret;
        }

        private int DecodeFrameSubs(AVCodecContext* codeCtx, AVPacket* avpacket)
        {
            int ret = 0;
            int gotFrame = 0;
            AVSubtitle sub = new AVSubtitle();
            
            ret = avcodec_decode_subtitle2(codeCtx, &sub, &gotFrame, avpacket);
            if (ret < 0)  { Log(codeCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret) ); return ret; }
            if (gotFrame < 1 || sub.num_rects < 1 ) return -1;

            ret = ProcessSubsFrame(avpacket, &sub);
            avsubtitle_free(&sub);
            if (ret != 0) { Log(codeCtx->codec_type.ToString() + " - Error[" + ret.ToString("D4") + "], Msg: Failed to process SUBS frame, PTS: " + avpacket->pts); return -1; }

            return 0;
        }
        #endregion

        #region Setup Codecs/Streams
        private int SetupCodec(AVMediaType mType)
        {
            int ret = 0;

            if      (mType == AVMEDIA_TYPE_AUDIO && hasAudio)
            {
                aCodecCtx   = aStream->codec;
                aCodec      = avcodec_find_decoder(aStream->codec->codec_id);
                ret         = avcodec_open2(aCodecCtx, aCodec, null); 
            }
            else if (mType == AVMEDIA_TYPE_VIDEO && hasVideo)
            {
                vCodecCtx   = vStream->codec;

                // Threading
                vCodecCtx->thread_count = Math.Min(Threads, vCodecCtx->codec_id == AV_CODEC_ID_HEVC ? 32 : 16);
                vCodecCtx->thread_type  = 0;
                //vCodecCtx->active_thread_type = FF_THREAD_FRAME;
                //vCodecCtx->active_thread_type = FF_THREAD_SLICE;
                vCodecCtx->thread_safe_callbacks = 1;
                 
                vCodec = avcodec_find_decoder(vStream->codec->codec_id);
                SetupHQAndHWAcceleration();
                ret = avcodec_open2(vCodecCtx, vCodec, null); 
            }
            else
                return -1;

            if (ret != 0) Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));

            return ret;
        }
        private int SetupHQAndHWAcceleration()
        {
            /* TODO
             * 
             * Simplify code & consider initializing the device (hw_device_ctx) only once for the whole session?
             * Also check if supported by the current codec?
             * 
             * AVBufferRef* hw_device_ctx;
             * av_hwdevice_ctx_create(&hw_device_ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, "auto", null, 0);
             * vCodecCtx->hw_device_ctx = av_buffer_ref(hw_device_ctx);
             * av_buffer_unref(&hw_device_ctx);
             * 
             */

            // For SWS
            vSwsOptFlags    = HighQuality ? _SCALING_HQ : _SCALING_LQ;
            hwAccelSuccess  = false;

            if (HWAccel && hwDevices.Count > 0)
            {
                if (avD3D11Device != null) { avD3D11Device.ImmediateContext.ClearState(); avD3D11Device.ImmediateContext.Flush(); Thread.Sleep(20); if (vCodecCtx->codec_id == AV_CODEC_ID_HEVC) Thread.Sleep(1000);}

                hwDevicesSupported = GetHWDevicesSupported();
                foreach (AVHWDeviceType hwDevice in hwDevices)
                {
                    if (hwDevice != AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA) continue;

                    // GPU device is in Codec's supported list
                    bool found = false;
                    foreach (HWDeviceSupported hwDeviceSupported in hwDevicesSupported)
                        if (hwDeviceSupported.type == hwDevice) { found = true; break; }
                    if (!found) continue;
                    found = false;

                    // HW Deivce Context (Temporary)
                    AVBufferRef* hw_device_ctx2 = null;
                    if ( hw_device_ctx == null )
                        { if (av_hwdevice_ctx_create(&hw_device_ctx2, hwDevice, "auto", null, 0) != 0) continue; }
                    else
                        hw_device_ctx2 = hw_device_ctx;

                    // Available Pixel Format's are supported from SWS (Currently using only NV12 for RGBA convert later with sws_scale)
                    AVHWFramesConstraints* hw_frames_const = av_hwdevice_get_hwframe_constraints(hw_device_ctx2, null);
                    if (hw_frames_const == null) { av_buffer_unref(&hw_device_ctx2); continue; }
                    for (AVPixelFormat* p = hw_frames_const->valid_sw_formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
                        if (sws_isSupportedInput(*p) > 0)
                            if (*p == AVPixelFormat.AV_PIX_FMT_NV12) { found = true; break; }
                    if (!found) { av_buffer_unref(&hw_device_ctx2); continue; }

                    if ( hw_device_ctx == null ) 
                    {
                        hw_device_ctx = hw_device_ctx2;
                        AVHWDeviceContext* hw_device_ctx3 = (AVHWDeviceContext*)hw_device_ctx->data;
                        AVD3D11VADeviceContext* hw_d3d11_dev_ctx = (AVD3D11VADeviceContext*)hw_device_ctx3->hwctx;
                        avD3D11Device = Device.FromPointer<Device>((IntPtr) hw_d3d11_dev_ctx->device);
                    }
                    
                    // Hardware Textures
                    textDescNV12 = new Texture2DDescription()
                    {
	                    Usage               = ResourceUsage.Default,
	                    //Format              = Format.NV12 | Format.P010

	                    Width               = vCodecCtx->width,
	                    Height              = vCodecCtx->height,

	                    BindFlags           = BindFlags.Decoder,
	                    CpuAccessFlags      = CpuAccessFlags.None,
	                    OptionFlags         = ResourceOptionFlags.Shared,

	                    SampleDescription   = new SampleDescription(1, 0),
	                    ArraySize           = 1,
	                    MipLevels           = 1
                    };

                    vCodecCtx->hw_device_ctx = av_buffer_ref(hw_device_ctx);
                    hwAccelSuccess = true;
                    Log("[HWACCEL] Enabled! Device -> " + hwDevice + ", Codec -> " + Marshal.PtrToStringAnsi((IntPtr)vCodec->name));
                    
                    break;
                }
            }

            // Software Textures
            textDescYUV = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Immutable,
                Format              = Format.R8_UNorm,

                Width               = vCodecCtx->width,
                Height              = vCodecCtx->height,

                BindFlags           = BindFlags.ShaderResource,
                CpuAccessFlags      = CpuAccessFlags.None,
                OptionFlags         = ResourceOptionFlags.None,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };

            textDescRGB = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Immutable,
                Format              = Format.R8G8B8A8_UNorm,

                Width               = vCodecCtx->width,
                Height              = vCodecCtx->height,

                BindFlags           = BindFlags.ShaderResource,
                CpuAccessFlags      = CpuAccessFlags.None,
                OptionFlags         = ResourceOptionFlags.None,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };
            
            return 0;
        }
        private int SetupAudio(AVFormatContext* fmtCtx = null)
        {
            int ret = 0;

            aStreamInfo.timebase                    = av_q2d(aStream->time_base);
            aStreamInfo.timebaseLowTicks            = av_q2d(aStream->time_base) * 10000 * 1000;
            aStreamInfo.startTimeTicks              = (aStreamInfo.startTimeTicks != AV_NOPTS_VALUE) ? (long)(aStream->start_time * aStreamInfo.timebaseLowTicks) : 0;
            aStreamInfo.durationTicks               = (aStream->duration > 0) ? (long)(aStream->duration * aStreamInfo.timebaseLowTicks) : fmtCtx->duration * 10;
            aStreamInfo.frameSize                   = aCodecCtx->frame_size;
            _RATE                                   = aCodecCtx->sample_rate;
            swrCtx = swr_alloc();

            av_opt_set_int(swrCtx,           "in_channel_layout",   (int)aCodecCtx->channel_layout, 0);
            av_opt_set_int(swrCtx,           "in_channel_count",         aCodecCtx->channels, 0);
            av_opt_set_int(swrCtx,           "in_sample_rate",           aCodecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(swrCtx,    "in_sample_fmt",            aCodecCtx->sample_fmt, 0);

            av_opt_set_int(swrCtx,           "out_channel_layout",       av_get_default_channel_layout(_CHANNELS), 0);
            av_opt_set_int(swrCtx,           "out_channel_count",        _CHANNELS, 0);
            av_opt_set_int(swrCtx,           "out_sample_rate",          _RATE, 0);
            av_opt_set_sample_fmt(swrCtx,    "out_sample_fmt",           _SAMPLE_FORMAT, 0);

            ret = swr_init(swrCtx);
            
            if (ret != 0) Log("Error[" + ret.ToString("D4") + "], Msg: " + ErrorCodeToMsg(ret));

            return ret;
        }
        private int SetupVideo(AVFormatContext* fmtCtx = null)
        {
            // TODO: should add Audio Delay to duration?
            vStreamInfo.timebase            = av_q2d(vStream->time_base);
            vStreamInfo.timebaseLowTicks    = av_q2d(vStream->time_base) * 10000 * 1000;
            vStreamInfo.startTimeTicks      = (vStreamInfo.startTimeTicks != AV_NOPTS_VALUE) ? (long)(vStream->start_time * vStreamInfo.timebaseLowTicks) : 0;
            vStreamInfo.durationTicks       = (vStream->duration > 0) ? (long)(vStream->duration * vStreamInfo.timebaseLowTicks) : fmtCtx->duration * 10;
            vStreamInfo.fps                 = av_q2d(vStream->avg_frame_rate);
            vStreamInfo.frameAvgTicks       = (long)((1 / vStreamInfo.fps) * 1000 * 10000);
            vStreamInfo.height              = vCodecCtx->height;
            vStreamInfo.width               = vCodecCtx->width;

            return 0;
        }
        #endregion

        #region Misc
        private List<AVHWDeviceType>    GetHWDevices()
        {
            List<AVHWDeviceType> hwDevices  = new List<AVHWDeviceType>();
            AVHWDeviceType       type       = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

            while ( (type = av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE )
                hwDevices.Add(type);

            return hwDevices;
        }
        private List<HWDeviceSupported> GetHWDevicesSupported()
        {
            List<HWDeviceSupported> hwDevicesSupported = new List<HWDeviceSupported>();

            for (int i = 0; ; i++)
            {
                AVCodecHWConfig* config = avcodec_get_hw_config(vCodec, i);
                if (config == null) break;

                Log("[HWACCEL] Codec Supports " + config->device_type + " - " + config->pix_fmt + " - " + config->methods);
                if ((config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == 0 || config->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE) continue;

                foreach (HWDeviceSupported hwDeviceExists in hwDevicesSupported)
                    if (hwDeviceExists.type == config->device_type) { hwDeviceExists.pixFmts.Add(config->pix_fmt); continue; }

                HWDeviceSupported hwDeviceNew = new HWDeviceSupported();
                hwDeviceNew.type        = config->device_type;
                hwDeviceNew.pixFmts     = new List<AVPixelFormat>();
                hwDeviceNew.pixFmts.Add(config->pix_fmt);
                hwDevicesSupported.Add(hwDeviceNew);
            }
#pragma warning disable CS0162 // Unreachable code detected
            return hwDevicesSupported;
#pragma warning restore CS0162 // Unreachable code detected
        }

        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [DECODER] {msg}"); }
        public unsafe string BytePtrToStringUTF8(byte* bytePtr)
        {
            if (bytePtr == null) return null;
            if (*bytePtr == 0) return string.Empty;

            var byteBuffer = new List<byte>(1024);
            var currentByte = default(byte);

            while (true)
            {
                currentByte = *bytePtr;
                if (currentByte == 0)
                    break;

                byteBuffer.Add(currentByte);
                bytePtr++;
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }
        private static string ErrorCodeToMsg(int error)
        {
            byte* buffer = stackalloc byte[1024];
            av_strerror(error, buffer, 1024);
            return Marshal.PtrToStringAnsi((IntPtr)buffer);
        }
        private av_log_set_callback_callback ffmpegLogCallback = (p0, level, format, vl) =>
        {
            if (level > av_log_get_level()) return;

            var buffer = stackalloc byte[1024];
            var printPrefix = 1;
            av_log_format_line(p0, level, format, vl, buffer, 1024, &printPrefix);
            var line = Marshal.PtrToStringAnsi((IntPtr)buffer);
            Console.WriteLine(line.Trim());
        };

        public static bool alreadyRegister = false;
        public static void RegisterFFmpegBinaries()
        {
            if (alreadyRegister) 
                return;
            alreadyRegister = true;

            var current = Environment.CurrentDirectory;
            var probe = Path.Combine("Libs", Environment.Is64BitProcess ? "x64" : "x86", "FFmpeg");

            while (current != null)
            {
                var ffmpegBinaryPath = Path.Combine(current, probe);
                if (Directory.Exists(ffmpegBinaryPath))
                {
                    Console.WriteLine($"FFmpeg binaries found in: {ffmpegBinaryPath}");
                    RootPath = ffmpegBinaryPath;
                    return;
                }
                current = Directory.GetParent(current)?.FullName;
            }
        }
        #endregion
    }
}