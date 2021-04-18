using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using SharpDX;
using SharpDX.Direct3D11;

namespace FlyleafLib.MediaFramework
{
    public unsafe class MediaFrame
    {
        public long         timestamp;
        public long         pts;

        public Texture2D[]  textures;   // Video Textures (planes)
        public byte[]       audioData;  // Audio Samples
            
        // Subtitles
        public int          duration;
        public string       text;
        public List<SubStyle> subStyles;

        public static int ProcessVideoFrame(Decoder decoder, MediaFrame mFrame, AVFrame* frame)
        {
            int ret = 0;

            try
            {
                // Hardware Frame (NV12|P010)   | CopySubresourceRegion FFmpeg Texture Array -> Device Texture[1] (NV12|P010) / SRV (RX_RXGX) -> PixelShader (Y_UV)
                if (decoder.hwAccelSuccess)
                {
                    decoder.textureFFmpeg   = new Texture2D((IntPtr) frame->data.ToArray()[0]);
                    decoder.textDesc.Format = decoder.textureFFmpeg.Description.Format;
                    mFrame. textures        = new Texture2D[1];
                    mFrame. textures[0]     = new Texture2D(decoder.decCtx.renderer.device, decoder.textDesc);
                    decoder.decCtx.renderer.device.ImmediateContext.CopySubresourceRegion(decoder.textureFFmpeg, (int) frame->data.ToArray()[1], new ResourceRegion(0, 0, 0, mFrame.textures[0].Description.Width, mFrame.textures[0].Description.Height, 1), mFrame.textures[0], 0);

                    return ret;
                }

                // Software Frame (8-bit YUV)   | YUV byte* -> Device Texture[3] (RX) / SRV (RX_RX_RX) -> PixelShader (Y_U_V)
                else if (decoder.info.PixelFormatType == PixelFormatType.Software_Handled)
                {
                    mFrame.textures = new Texture2D[3];

                    // YUV Planar [Y0 ...] [U0 ...] [V0 ....]
                    if (decoder.info.IsPlanar)
                    {
                        DataBox db          = new DataBox();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[0];
                        db.RowPitch         = frame->linesize.ToArray()[0];
                        mFrame.textures[0]  = new Texture2D(decoder.decCtx.renderer.device, decoder.textDesc,  new DataBox[] { db });

                        db                  = new DataBox();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[1];
                        db.RowPitch         = frame->linesize.ToArray()[1];
                        mFrame.textures[1]  = new Texture2D(decoder.decCtx.renderer.device, decoder.textDescUV, new DataBox[] { db });

                        db                  = new DataBox();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[2];
                        db.RowPitch         = frame->linesize.ToArray()[2];
                        mFrame.textures[2]  = new Texture2D(decoder.decCtx.renderer.device, decoder.textDescUV, new DataBox[] { db });
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

                        for (int i=0; i<totalSize; i+=decoder.info.Comp0Step)
                            dsY.WriteByte(*(dataPtr + i));

                        for (int i=1; i<totalSize; i+=decoder.info.Comp1Step)
                            dsU.WriteByte(*(dataPtr + i));

                        for (int i=3; i<totalSize; i+=decoder.info.Comp2Step)
                            dsV.WriteByte(*(dataPtr + i));

                        mFrame.textures[0] = new Texture2D(decoder.decCtx.renderer.device, decoder.textDesc,   new DataBox[] { dbY });
                        mFrame.textures[1] = new Texture2D(decoder.decCtx.renderer.device, decoder.textDescUV, new DataBox[] { dbU });
                        mFrame.textures[2] = new Texture2D(decoder.decCtx.renderer.device, decoder.textDescUV, new DataBox[] { dbV });

                        Utilities.Dispose(ref dsY); Utilities.Dispose(ref dsU); Utilities.Dispose(ref dsV);
                    }
                }

                // Software Frame (OTHER/sws_scale) | X byte* -> Sws_Scale RGBA -> Device Texture[1] (RGBA) / SRV (RGBA) -> PixelShader (RGBA)
                else
                {
                    if (decoder.swsCtx == null)
                    {
                        decoder.textDesc.Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm;
                        decoder.outData         = new byte_ptrArray4();
                        decoder.outLineSize     = new int_array4();
                        decoder.outBufferSize   = av_image_get_buffer_size(Decoder.VOutPixelFormat, decoder.codecCtx->width, decoder.codecCtx->height, 1);
                        Marshal.FreeHGlobal(decoder.outBufferPtr);
                        decoder.outBufferPtr    = Marshal.AllocHGlobal(decoder.outBufferSize);
                        av_image_fill_arrays(ref decoder.outData, ref decoder.outLineSize, (byte*) decoder.outBufferPtr, Decoder.VOutPixelFormat, decoder.codecCtx->width, decoder.codecCtx->height, 1);
                        
                        int vSwsOptFlags        = decoder.decCtx.cfg.video.SwsHighQuality ? DecoderContext.SCALING_HQ : DecoderContext.SCALING_LQ;
                        decoder.swsCtx          = sws_getContext(decoder.codecCtx->coded_width, decoder.codecCtx->coded_height, decoder.codecCtx->pix_fmt, decoder.codecCtx->width, decoder.codecCtx->height, Decoder.VOutPixelFormat, vSwsOptFlags, null, null, null);
                        if (decoder.swsCtx == null) { Log($"[ProcessVideoFrame|RGB] [ERROR-1] Failed to allocate SwsContext"); return ret; }
                    }

                    sws_scale(decoder.swsCtx, frame->data, frame->linesize, 0, frame->height, decoder.outData, decoder.outLineSize);

                    DataBox db          = new DataBox();
                    db.DataPointer      = (IntPtr)decoder.outData.ToArray()[0];
                    db.RowPitch         = decoder.outLineSize[0];
                    mFrame.textures     = new Texture2D[1];
                    mFrame.textures[0]  = new Texture2D(decoder.decCtx.renderer.device, decoder.textDesc, new DataBox[] { db });
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

                    decoder.m_max_dst_nb_samples = (int)av_rescale_rnd(frame->nb_samples, decoder.codecCtx->sample_rate, decoder.codecCtx->sample_rate, AVRounding.AV_ROUND_UP);
                    fixed(byte*** dst_data = &decoder.m_dst_data)
                    fixed(int *dst_linesize = &decoder.m_dst_linesize)
                    ret = av_samples_alloc_array_and_samples(dst_data, dst_linesize, Decoder.AOutChannels, decoder.m_max_dst_nb_samples, Decoder.AOutSampleFormat, 0);
                }

                fixed (int* dst_linesize = &decoder.m_dst_linesize)
                {
                    dst_nb_samples = (int)av_rescale_rnd(swr_get_delay(decoder.swrCtx, decoder.codecCtx->sample_rate) + frame->nb_samples, decoder.codecCtx->sample_rate, decoder.codecCtx->sample_rate, AVRounding.AV_ROUND_UP);

                    if (dst_nb_samples > decoder.m_max_dst_nb_samples)
                    {
                        av_freep(&decoder.m_dst_data[0]);
                        ret = av_samples_alloc(decoder.m_dst_data, dst_linesize, Decoder.AOutChannels, (int)dst_nb_samples, Decoder.AOutSampleFormat, 0);
                    }

                    ret = swr_convert(decoder.swrCtx, decoder.m_dst_data, dst_nb_samples, (byte**)&frame->data, frame->nb_samples);
                    if (ret < 0) return ret;

                    int dst_data_len = av_samples_get_buffer_size(dst_linesize, Decoder.AOutChannels, ret, Decoder.AOutSampleFormat, 1);

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

                mFrame.text         = SSAtoSubStyles(line, out List<SubStyle> subStyles);
                mFrame.subStyles    = subStyles;
                mFrame.duration     = (int) (sub->end_display_time - sub->start_display_time);

                //Log("SUBS ......... " + Utils.TicksToTime(mFrame.timestamp));

            } catch (Exception e) { ret = -1; Log("Error[" + (ret).ToString("D4") + "], Func: ProcessSubsFrame(), Msg: " + e.Message + " - " + e.StackTrace); }

            return ret;
        }

        public static string SSAtoSubStyles(string s, out List<SubStyle> styles)
        {
            int     pos     = 0;
            string  sout    = "";
            styles          = new List<SubStyle>();

            SubStyle bold       = new SubStyle(SubStyles.BOLD);
            SubStyle italic     = new SubStyle(SubStyles.ITALIC);
            SubStyle underline  = new SubStyle(SubStyles.UNDERLINE);
            SubStyle strikeout  = new SubStyle(SubStyles.STRIKEOUT);
            SubStyle color      = new SubStyle(SubStyles.COLOR);

            //SubStyle fontname      = new SubStyle(SubStyles.FONTNAME);
            //SubStyle fontsize      = new SubStyle(SubStyles.FONTSIZE);

            s = s.LastIndexOf(",,") == -1 ? s : s.Substring(s.LastIndexOf(",,") + 2).Replace("\\N", "\n").Trim();

            for (int i=0; i<s.Length; i++)
            {
                if (s[i] == '{') continue;

                if (s[i] == '\\' && s[i-1] == '{')
                {
                    int codeLen = s.IndexOf('}', i) -i;
                    if (codeLen == -1) continue;

                    string code = s.Substring(i, codeLen).Trim();

                    switch (code[1])
                    {
                        case 'c':
                            if ( code.Length == 2 )
                            {
                                if (color.from == -1) break;

                                color.len = pos - color.from;
                                if ((Color) color.value != Color.Transparent) styles.Add(color);
                                color = new SubStyle(SubStyles.COLOR);                                
                            }
                            else
                            {
                                color.from = pos;
                                color.value = Color.Transparent;
                                if (code.Length < 7) break;

                                int colorEnd = code.LastIndexOf("&");
                                if (colorEnd < 6) break;

                                string hexColor = code.Substring(4, colorEnd - 4);
                                int red = int.Parse(hexColor.Substring(hexColor.Length-2, 2), NumberStyles.HexNumber);
                                int green = 0;
                                int blue = 0;

                                if (hexColor.Length-2 > 0)
                                {
                                    hexColor = hexColor.Substring(0, hexColor.Length-2);
                                    green = int.Parse(hexColor.Substring(hexColor.Length-2, 2), NumberStyles.HexNumber);
                                }
                                if (hexColor.Length-2 > 0)
                                {
                                    hexColor = hexColor.Substring(0, hexColor.Length-2);
                                    blue = int.Parse(hexColor.Substring(hexColor.Length-2, 2), NumberStyles.HexNumber);
                                }

                                color.value = new Color(red, green, blue);
                            }
                            break;

                        case 'b':
                            if ( code[2] == '0' )
                            {
                                if (bold.from == -1) break;

                                bold.len = pos - bold.from;
                                styles.Add(bold);
                                bold = new SubStyle(SubStyles.BOLD);
                            }
                            else
                            {
                                bold.from = pos;
                                //bold.value = code.Substring(2, code.Length-2);
                            }

                            break;

                        case 'u':
                            if ( code[2] == '0' )
                            {
                                if (underline.from == -1) break;

                                underline.len = pos - underline.from;
                                styles.Add(underline);
                                underline = new SubStyle(SubStyles.UNDERLINE);
                            }
                            else
                            {
                                underline.from = pos;
                            }
                            
                            break;

                        case 's':
                            if ( code[2] == '0' )
                            {
                                if (strikeout.from == -1) break;

                                strikeout.len = pos - strikeout.from;
                                styles.Add(strikeout);
                                strikeout = new SubStyle(SubStyles.STRIKEOUT);
                            }
                            else
                            {
                                strikeout.from = pos;
                            }
                            
                            break;

                        case 'i':
                            if ( code[2] == '0' )
                            {
                                if (italic.from == -1) break;

                                italic.len = pos - italic.from;
                                styles.Add(italic);
                                italic = new SubStyle(SubStyles.ITALIC);
                            }
                            else
                            {
                                italic.from = pos;
                            }
                            
                            break;
                    }

                    i += codeLen;
                    continue;
                }

                sout += s[i];
                pos ++;
            }

            // Non-Closing Codes
            int soutPostLast = sout.Length;
            if (bold.from != -1) { bold.len = soutPostLast - bold.from; styles.Add(bold); }
            if (italic.from != -1) { italic.len = soutPostLast - italic.from; styles.Add(italic); }
            if (strikeout.from != -1) { strikeout.len = soutPostLast - strikeout.from; styles.Add(strikeout); }
            if (underline.from != -1) { underline.len = soutPostLast - underline.from; styles.Add(underline); }
            if (color.from != -1 && (Color) color.value != Color.Transparent) { color.len = soutPostLast - color.from; styles.Add(color); }

            return sout;
        }

        public struct SubStyle
        {
            public SubStyles style;
            public Color value;

            public int from;
            public int len;

            public SubStyle(int from, int len, Color value) : this(SubStyles.COLOR, from, len, value) { }
            public SubStyle(SubStyles style, int from = -1, int len = -1, Color? value = null)
            {
                this.style  = style;
                this.value  = value == null ? Color.White : (Color)value;
                this.from   = from;
                this.len    = len;
            }
        }
        public enum SubStyles
        {
            BOLD,
            ITALIC,
            UNDERLINE,
            STRIKEOUT,
            FONTSIZE,
            FONTNAME,
            COLOR
        }

        private static void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [MediaFrame] {msg}"); }
    }
}
