using System;
using System.Collections.Generic;
using System.Diagnostics;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;

namespace FlyleafLib.MediaFramework.MediaRemuxer
{
    public unsafe class Remuxer
    {
        public int          UniqueId        { get; set; }
        public bool         Disposed        { get; private set; } = true;
        public string       Filename        { get; private set; }
        public bool         HasStreams      => mapInOutStreams2.Count > 0 || mapInOutStreams.Count > 0;
        public bool         HeaderWritten   { get; private set; }


        AVFormatContext*        fmtCtx;
        AVOutputFormat*         fmt;

        Dictionary<IntPtr, IntPtr>
                                mapInOutStreams = new Dictionary<IntPtr, IntPtr>();

        Dictionary<int, IntPtr>
                                mapInInStream   = new Dictionary<int, IntPtr>();

        Dictionary<int, long>   mapInStreamToDts= new Dictionary<int, long>();

        Dictionary<IntPtr, IntPtr>
                                mapInOutStreams2 = new Dictionary<IntPtr, IntPtr>();

        Dictionary<int, IntPtr>
                                mapInInStream2   = new Dictionary<int, IntPtr>();

        Dictionary<int, long>   mapInStreamToDts2= new Dictionary<int, long>();
        
        public Remuxer(int uniqueId = -1)
        {
            UniqueId= uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
        }

        public int Open(string filename)
        {
            int ret;
            Filename = filename;

            fixed (AVFormatContext** ptr = &fmtCtx)
                ret = avformat_alloc_output_context2(ptr, null, null, Filename);

            if (ret < 0) return ret;

            fmt = fmtCtx->oformat;
            mapInStreamToDts = new Dictionary<int, long>();
            Disposed = false;

            return 0;
        }

        public int AddStream(AVStream* in_stream, bool isAudioDemuxer = false)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            int ret = -1;

            if (in_stream == null || (in_stream->codec->codec_type != AVMEDIA_TYPE_VIDEO && in_stream->codec->codec_type != AVMEDIA_TYPE_AUDIO)) return ret;
            
            AVStream *out_stream;
            AVCodecParameters *in_codecpar = in_stream->codecpar;

            out_stream = avformat_new_stream(fmtCtx, null);
            if (out_stream == null) return -1;

            ret = avcodec_parameters_copy(out_stream->codecpar, in_codecpar);
            if (ret < 0) return ret;

            if ((fmt->flags & AVFMT_GLOBALHEADER) != 0)
                out_stream->codec->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;

            out_stream->codecpar->codec_tag = 0;

            // TODO:
            // validate that the output format container supports the codecs (if not change the container?)
            // check whether we remux from mp4 to mpegts (requires bitstream filter h264_mp4toannexb)

            //if (av_codec_get_tag(fmtCtx->oformat->codec_tag, in_stream->codecpar->codec_id) == 0)
            //    Log("Not Found");
            //else
            //    Log("Found");

            if (isAudioDemuxer)
            {
                mapInOutStreams2.Add((IntPtr)in_stream, (IntPtr)out_stream);
                mapInInStream2.Add(in_stream->index, (IntPtr)in_stream);
            }
            else
            {
                mapInOutStreams.Add((IntPtr)in_stream, (IntPtr)out_stream);
                mapInInStream.Add(in_stream->index, (IntPtr)in_stream);
            }
            

            return 0;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public int WriteHeader()
        {
            if (!HasStreams) throw new Exception("No streams have been configured for the remuxer");

            int ret;

            ret = avio_open(&fmtCtx->pb, Filename, AVIO_FLAG_WRITE);
            if (ret < 0) { Dispose(); return ret; }

            ret = avformat_write_header(fmtCtx, null);
            if (ret < 0) { Dispose(); return ret; }
            
            HeaderWritten = true;

            return 0;
        }

        public int Write(AVPacket* packet, bool isAudioDemuxer = false)
        {
            lock (this)
            {
                Dictionary<int, IntPtr>     mapInInStream       = !isAudioDemuxer? this.mapInInStream   : mapInInStream2;
                Dictionary<IntPtr, IntPtr>  mapInOutStreams     = !isAudioDemuxer? this.mapInOutStreams : mapInOutStreams2;
                Dictionary<int, long>       mapInStreamToDts    = !isAudioDemuxer? this.mapInStreamToDts: mapInStreamToDts2;

                AVStream* in_stream     =  (AVStream*) mapInInStream[packet->stream_index];
                AVStream* out_stream    =  (AVStream*) mapInOutStreams[(IntPtr)in_stream];
                av_rescale_q(packet->dts,in_stream->time_base, av_get_time_base_q());
                if (packet->dts != AV_NOPTS_VALUE)
                {

                    if (!mapInStreamToDts.ContainsKey(in_stream->index))
                    {
                        // TODO: In case of AudioDemuxer calculate the diff with the VideoDemuxer and add it in one of them - all stream - (in a way to have positive)
                        mapInStreamToDts.Add(in_stream->index, packet->dts);
                    }

                    packet->pts         = av_rescale_q_rnd(packet->pts - mapInStreamToDts[in_stream->index], in_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                    packet->dts         = av_rescale_q_rnd(packet->dts - mapInStreamToDts[in_stream->index], in_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                }
                else
                {
                    packet->pts         = av_rescale_q_rnd(packet->pts, in_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                    packet->dts         = av_rescale_q_rnd(packet->dts, in_stream->time_base, out_stream->time_base, AVRounding.AV_ROUND_NEAR_INF | AVRounding.AV_ROUND_PASS_MINMAX);
                }

                packet->duration        = av_rescale_q(packet->duration,in_stream->time_base, out_stream->time_base);
                packet->stream_index    = out_stream->index;
                packet->pos             = -1;

                int ret = av_interleaved_write_frame(fmtCtx, packet);
                av_packet_free(&packet);

                return ret;
            }
        }

        public int WriteTrailer() { return Dispose(); }
        public int Dispose()
        {
            if (Disposed) return -1;

            int ret = 0;

            if (fmtCtx != null)
            {
                if (HeaderWritten)
                {
                    ret = av_write_trailer(fmtCtx);
                    avio_closep(&fmtCtx->pb);
                }

                avformat_free_context(fmtCtx);
            }

            fmtCtx = null;
            Filename = null;
            Disposed = true;
            HeaderWritten = false;
            mapInOutStreams.Clear();
            mapInInStream.Clear();
            mapInOutStreams2.Clear();
            mapInInStream2.Clear();

            return ret;
        }
        
        private void Log (string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Remuxer] {msg}"); }
    }
}
