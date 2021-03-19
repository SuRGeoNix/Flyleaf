using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using SharpDX;
using SharpDX.Direct3D11;

namespace SuRGeoNix.Flyleaf.MediaFramework
{
    public unsafe class MediaFrame
    {
        public long         timestamp;
        public long         pts;

        public Texture2D[]  textures;   // Video Textures

        public byte[]       audioData;  // Audio Samples
            
        // Subtitles
        public int          duration;
        public string       text;
        public List<OSDMessage.SubStyle> subStyles;

        public static void CreateTexture(Decoder decoder, IntPtr dataPtr, int pitch, Texture2DDescription textDesc, out Texture2D texture)
        {
            DataStream ds   = new DataStream(pitch * textDesc.Height, true, true);
            DataBox    db   = new DataBox();
            db.DataPointer  = ds.DataPointer;
            db.RowPitch     = pitch;
            ds.WriteRange(dataPtr, ds.Length);
            texture = new Texture2D(decoder.decCtx.device, textDesc, new DataBox[] { db });
            Utilities.Dispose(ref ds);
        }
        public static int ProcessVideoFrame(Decoder decoder, MediaFrame mFrame, AVFrame* frame)
        {
            int ret = 0;

            try
            {
                // Hardware Frame (NV12|P010)   | CopySubresourceRegion FFmpeg -> texture (RX_RXGX) -> PixelShader (Y_UV)
                if (decoder.hwAccelSuccess) // frame->format == (int) AVPixelFormat.AV_PIX_FMT_D3D11 (NV12 | P010)
                {
                    decoder.textureFFmpeg   = new Texture2D((IntPtr) frame->data.ToArray()[0]);
                    decoder.textDesc.Format = decoder.textureFFmpeg.Description.Format;
                    mFrame.textures         = new Texture2D[1];
                    mFrame.textures[0]      = new Texture2D(decoder.decCtx.device, decoder.textDesc);
                    decoder.decCtx.device.ImmediateContext.CopySubresourceRegion(decoder.textureFFmpeg, (int) frame->data.ToArray()[1], new ResourceRegion(0, 0, 0, mFrame.textures[0].Description.Width, mFrame.textures[0].Description.Height, 1), mFrame.textures[0], 0);

                    return ret;
                }

                // Software Frame (8-bit YUV)     | YUV byte* -> Device YUV (srv R8 * 3) -> PixelShader (Y_U_V)
                else if (decoder.info.PixelFormatType == PixelFormatType.Software_Handled)// || frame->format == (int)AVPixelFormat.AV_PIX_FMT_YUV420P)
                {
                    // TODO: Semi-Planar
                    mFrame.textures = new Texture2D[3];

                    // YUV Planar [Y0 ...] [U0 ...] [V0 ....]
                    if (decoder.info.IsPlanar)
                    {
                        CreateTexture(decoder, (IntPtr)frame->data.ToArray()[0], frame->linesize.ToArray()[0], decoder.textDesc,   out mFrame.textures[0]);
                        CreateTexture(decoder, (IntPtr)frame->data.ToArray()[1], frame->linesize.ToArray()[1], decoder.textDescUV, out mFrame.textures[1]);
                        CreateTexture(decoder, (IntPtr)frame->data.ToArray()[2], frame->linesize.ToArray()[2], decoder.textDescUV, out mFrame.textures[2]);

                        return ret;
                    }

                    // YUV Packed ([Y0U0Y1V0] ....)
                    else
                    {
                        DataStream dsY  = new DataStream(decoder.textDesc.  Width * decoder.textDesc.  Height, true, true);
                        DataStream dsU  = new DataStream(decoder.textDescUV.Width * decoder.textDescUV.Height, true, true);
                        DataStream dsV  = new DataStream(decoder.textDescUV.Width * decoder.textDescUV.Height, true, true);
                        DataBox    dbY  = new DataBox();
                        DataBox    dbU  = new DataBox();
                        DataBox    dbV  = new DataBox();

                        dbY.DataPointer = dsY.DataPointer;
                        dbU.DataPointer = dsU.DataPointer;
                        dbV.DataPointer = dsV.DataPointer;

                        dbY.RowPitch    = decoder.textDesc.  Width;
                        dbU.RowPitch    = decoder.textDescUV.Width;
                        dbV.RowPitch    = decoder.textDescUV.Width;

                        long totalSize = frame->linesize.ToArray()[0] * decoder.textDesc.Height;

                        byte* dataPtr = frame->data.ToArray()[0];
                        AVComponentDescriptor[] comps = decoder.info.PixelFormatDesc->comp.ToArray();

                        for (int i=0; i<totalSize; i += decoder.info.Comp0Step)
                            dsY.WriteByte((byte)(*(dataPtr + i)));

                        for (int i=1; i<totalSize; i += decoder.info.Comp1Step)
                            dsU.WriteByte((byte)(*(dataPtr + i)));

                        for (int i=3; i<totalSize; i += decoder.info.Comp2Step)
                            dsV.WriteByte((byte)(*(dataPtr + i)));

                        mFrame.textures[0] = new Texture2D(decoder.decCtx.device, decoder.textDesc,   new DataBox[] { dbY });
                        mFrame.textures[1] = new Texture2D(decoder.decCtx.device, decoder.textDescUV, new DataBox[] { dbU });
                        mFrame.textures[2] = new Texture2D(decoder.decCtx.device, decoder.textDescUV, new DataBox[] { dbV });

                        Utilities.Dispose(ref dsY); Utilities.Dispose(ref dsU); Utilities.Dispose(ref dsV);
                    }
                }

                // Software Frame (OTHER/sws_scale) | X byte* -> Sws_Scale RGBA -> Device RGA
                else
                {
                    if (decoder.swsCtx == null)
                    {
                        decoder.textDesc.Format                 = SharpDX.DXGI.Format.R8G8B8A8_UNorm;
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

                    DataStream ds   = new DataStream(decoder.outLineSize[0] * decoder.textDesc.Height, true, true);
                    DataBox    db   = new DataBox();

                    db.DataPointer  = ds.DataPointer;
                    db.RowPitch     = decoder.outLineSize[0];
                    ds.WriteRange((IntPtr)decoder.outData.ToArray()[0], ds.Length);

                    mFrame.textures     = new Texture2D[1];
                    mFrame.textures[0]  = new Texture2D(decoder.decCtx.device, decoder.textDesc, new DataBox[] { db });
                    Utilities.Dispose(ref ds);
                }

                return ret;

            } catch (Exception e) { ret = -1;  Log("Error[" + (ret).ToString("D4") + "], Func: ProcessVideoFrame(), Msg: " + e.Message + " - " + e.StackTrace); }

            return ret;
        }
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
        public static int ProcessSubsFrame (Decoder decoder, MediaFrame mFrame, AVSubtitle * sub)
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
