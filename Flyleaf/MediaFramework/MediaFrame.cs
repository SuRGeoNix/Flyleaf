using System;
using System.Collections.Generic;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using SharpDX.Direct3D11;
using System.Runtime.InteropServices;
using SharpDX;

namespace SuRGeoNix.Flyleaf.MediaFramework
{
    public unsafe class MediaFrame
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

        public static int ProcessAudioFrame(Decoder decoder, MediaFrame mFrame, AVFrame* frame)
        {
            /* References
             * 
             * https://www.programmersought.com/article/70255648018/ 
             * https://ffmpeg.org/doxygen/2.4/resampling__audio_8c_source.html
             * 
             * Notes
             * 
             * Currently output sample rate = input sample rate which means in case we change that we should review that actually works properly
             * If frame->nb_samples are known initially or by decoding on frame we can avoid av_samples_alloc_array_and_samples here and transfer it to SetupAudio
             */

            int ret = 0;

            try
            {
                int dst_nb_samples;

                if (decoder.m_max_dst_nb_samples == -1)
                {
                    if (decoder.m_dst_data != null && (IntPtr)(*decoder.m_dst_data) != IntPtr.Zero) { av_freep(&decoder.m_dst_data[0]); decoder.m_dst_data = null; }

                    decoder.m_max_dst_nb_samples = (int)av_rescale_rnd(frame->nb_samples, decoder.opt.audio.SampleRate, decoder.codecCtx->sample_rate, AVRounding.AV_ROUND_UP);
                    fixed(byte*** dst_data = &decoder.m_dst_data)
                    fixed(int *dst_linesize = &decoder.m_dst_linesize)
                    ret = av_samples_alloc_array_and_samples(dst_data, dst_linesize, decoder.opt.audio.Channels, decoder.m_max_dst_nb_samples, decoder.opt.audio.SampleFormat, 0);
                }

                fixed (int* dst_linesize = &decoder.m_dst_linesize)
                {
                    dst_nb_samples = (int)av_rescale_rnd(swr_get_delay(decoder.swrCtx, decoder.codecCtx->sample_rate) + frame->nb_samples, decoder.opt.audio.SampleRate, decoder.codecCtx->sample_rate, AVRounding.AV_ROUND_UP);

                    if (dst_nb_samples > decoder.m_max_dst_nb_samples)
                    {
                        av_freep(&decoder.m_dst_data[0]);
                        ret = av_samples_alloc(decoder.m_dst_data, dst_linesize, decoder.opt.audio.Channels, (int)dst_nb_samples, decoder.opt.audio.SampleFormat, 0);
                    }

                    ret = swr_convert(decoder.swrCtx, decoder.m_dst_data, dst_nb_samples, (byte**)&frame->data, frame->nb_samples);
                    if (ret < 0) return ret;

                    int dst_data_len = av_samples_get_buffer_size(dst_linesize, decoder.opt.audio.Channels, ret, decoder.opt.audio.SampleFormat, 1);

                    mFrame.audioData    = new byte[dst_data_len]; Marshal.Copy((IntPtr)(*decoder.m_dst_data), mFrame.audioData, 0, mFrame.audioData.Length); //Marshal.FreeHGlobal((IntPtr)(*m_dst_data));
                }
            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessAudioFrame(), Msg: " + e.Message + " - " + e.StackTrace); }

            return ret;
        }

        public static int ProcessVideoFrame(Decoder decoder, MediaFrame mFrame, AVFrame* frame)
        {
            int ret = 0;

            try
            {
                // Hardware Frame (NV12|P010)   | CopySubresourceRegion FFmpeg -> textureHW -> VideoProcessBlt RGBA
                if (decoder.hwAccelSuccess)
                {
                    decoder.textureFFmpeg       = new Texture2D((IntPtr) frame->data.ToArray()[0]);
                    decoder.textDescHW.Format   = decoder.textureFFmpeg.Description.Format;
                    mFrame.textureHW            = new Texture2D(decoder.decCtx.device, decoder.textDescHW);

                    lock (decoder.decCtx.device)
                        decoder.decCtx.device.ImmediateContext.CopySubresourceRegion(decoder.textureFFmpeg, (int) frame->data.ToArray()[1], new ResourceRegion(0, 0, 0, mFrame.textureHW.Description.Width, mFrame.textureHW.Description.Height, 1), mFrame.textureHW, 0);

                    return ret;
                }

                // Software Frame (YUV420P)     | YUV byte* -> Device YUV (srv R8 * 3) -> PixelShader YUV->RGBA
                else if (frame->format == (int)AVPixelFormat.AV_PIX_FMT_YUV420P)
                {
                    decoder.textDescYUV.Width   = decoder.codecCtx->width;
                    decoder.textDescYUV.Height  = decoder.codecCtx->height;

                    DataStream dsY = new DataStream(frame->linesize.ToArray()[0] * decoder.codecCtx->height, true, true);
                    DataStream dsU = new DataStream(frame->linesize.ToArray()[1] * decoder.codecCtx->height / 2, true, true);
                    DataStream dsV = new DataStream(frame->linesize.ToArray()[2] * decoder.codecCtx->height / 2, true, true);

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

                    mFrame.textureY = new Texture2D(decoder.decCtx.device, decoder.textDescYUV, new DataBox[] { dbY });
                    decoder.textDescYUV.Width   = decoder.codecCtx->width  / 2;
                    decoder.textDescYUV.Height  = decoder.codecCtx->height / 2;

                    mFrame.textureU = new Texture2D(decoder.decCtx.device, decoder.textDescYUV, new DataBox[] { dbU });
                    mFrame.textureV = new Texture2D(decoder.decCtx.device, decoder.textDescYUV, new DataBox[] { dbV });

                    Utilities.Dispose(ref dsY);
                    Utilities.Dispose(ref dsU);
                    Utilities.Dispose(ref dsV);
                }

                // Software Frame (OTHER/sws_scale) | X byte* -> Sws_Scale RGBA -> Device RGA
                else if (!decoder.hwAccelSuccess) 
                {
                    if (decoder.swsCtx == null)
                    {
                        decoder.outData                         = new byte_ptrArray4();
                        decoder.outLineSize                     = new int_array4();
                        decoder.outBufferSize                   = av_image_get_buffer_size(decoder.opt.video.PixelFormat, decoder.codecCtx->width, decoder.codecCtx->height, 1);
                        Marshal.FreeHGlobal(decoder.outBufferPtr);
                        decoder.outBufferPtr                    = Marshal.AllocHGlobal(decoder.outBufferSize);
                        
                        av_image_fill_arrays(ref decoder.outData, ref decoder.outLineSize, (byte*)decoder.outBufferPtr, decoder.opt.video.PixelFormat, decoder.codecCtx->width, decoder.codecCtx->height, 1);

                        int vSwsOptFlags = decoder.opt.video.SwsHighQuality ? DecoderContext.SCALING_HQ : DecoderContext.SCALING_LQ;
                        decoder.swsCtx = sws_getContext(decoder.codecCtx->coded_width, decoder.codecCtx->coded_height, decoder.codecCtx->pix_fmt, decoder.codecCtx->width, decoder.codecCtx->height, decoder.opt.video.PixelFormat, vSwsOptFlags, null, null, null);
                        if (decoder.swsCtx == null) { Log($"[ProcessVideoFrame|RGB] [ERROR-1] Failed to allocate SwsContext"); return ret; }
                    }

                    sws_scale(decoder.swsCtx, frame->data, frame->linesize, 0, frame->height, decoder.outData, decoder.outLineSize);

                    DataStream ds   = new DataStream(decoder.outLineSize[0] * decoder.codecCtx->height, true, true);
                    DataBox db      = new DataBox();

                    db.DataPointer  = ds.DataPointer;
                    db.RowPitch     = decoder.outLineSize[0];
                    ds.WriteRange((IntPtr)decoder.outData.ToArray()[0], ds.Length);

                    mFrame.textureRGB = new Texture2D(decoder.decCtx.device, decoder.textDescRGB, new DataBox[] { db });
                    Utilities.Dispose(ref ds);
                }

                return ret;

            } catch (Exception e) { ret = -1;  Log("Error[" + (ret).ToString("D4") + "], Func: ProcessVideoFrame(), Msg: " + e.Message + " - " + e.StackTrace); }

            return ret;
        }

        public static int ProcessSubsFrame(Decoder decoder, MediaFrame mFrame, AVSubtitle * sub)
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
                        line = Utils.BytePtrToStringUTF8(cur->ass);
                        break;

                    case AVSubtitleType.SUBTITLE_TEXT:
                        buffer = new byte[1024];
                        line = Utils.BytePtrToStringUTF8(cur->ass);

                        break;

                    case AVSubtitleType.SUBTITLE_BITMAP:
                        Log("Subtitles BITMAP -> Not Implemented yet");

                        return -1;
                }

                mFrame.text         = Subtitles.SSAtoSubStyles(line, out List<OSDMessage.SubStyle> subStyles);
                mFrame.subStyles    = subStyles;
                mFrame.duration     = (int) (sub->end_display_time - sub->start_display_time);

                //Log("SUBS ......... " + Utils.TicksToTime(mFrame.timestamp));

            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessSubsFrame(), Msg: " + e.Message + " - " + e.StackTrace); }

            return ret;
        }

        private static void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [MediaFrame] {msg}"); }
    }
}
