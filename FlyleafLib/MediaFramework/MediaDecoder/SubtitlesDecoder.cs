using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaDecoder
{
    public unsafe class SubtitlesDecoder : DecoderBase
    {
        public SubtitlesStream  SubtitlesStream     => (SubtitlesStream) Stream;

        public ConcurrentQueue<SubtitlesFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<SubtitlesFrame>();

        public SubtitlesDecoder(Config config, int uniqueId = -1) : base(config, uniqueId) { }

        protected override unsafe int Setup(AVCodec* codec) { return 0; }

        protected override void DisposeInternal()
        {
            Frames = new ConcurrentQueue<SubtitlesFrame>();
        }

        public void Flush()
        {
            lock (lockActions)
            lock (lockCodecCtx)
            {
                if (Disposed) return;

                if (Status == Status.Ended) Status = Status.Stopped;
                //else if (Status == Status.Draining) Status = Status.Stopping;

                DisposeFrames();
                avcodec_flush_buffers(codecCtx);
                curSpeedFrame = Speed;
            }
        }

        protected override void RunInternal()
        {
            int ret = 0;
            int allowedErrors = Config.Decoder.MaxErrors;
            AVPacket *packet;

            do
            {
                // Wait until Queue not Full or Stopped
                if (Frames.Count >= Config.Decoder.MaxSubsFrames)
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueFull;

                    while (Frames.Count >= Config.Decoder.MaxSubsFrames && Status == Status.QueueFull) Thread.Sleep(20);

                    lock (lockStatus)
                    {
                        if (Status != Status.QueueFull) break;
                        Status = Status.Running;
                    }       
                }

                // While Packets Queue Empty (Ended | Quit if Demuxer stopped | Wait until we get packets)
                if (demuxer.SubtitlesPackets.Count == 0)
                {
                    CriticalArea = true;

                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueEmpty;

                    while (demuxer.SubtitlesPackets.Count == 0 && Status == Status.QueueEmpty)
                    {
                        if (demuxer.Status == Status.Ended)
                        {
                            Status = Status.Ended;
                            break;
                        }
                        else if (!demuxer.IsRunning)
                        {
                            Log($"Demuxer is not running [Demuxer Status: {demuxer.Status}]");

                            int retries = 5;

                            while (retries > 0)
                            {
                                retries--;
                                Thread.Sleep(10);
                                if (demuxer.IsRunning) break;
                            }

                            lock (demuxer.lockStatus)
                            lock (lockStatus)
                            {
                                if (demuxer.Status == Status.Pausing || demuxer.Status == Status.Paused)
                                    Status = Status.Pausing;
                                else if (demuxer.Status != Status.Ended)
                                    Status = Status.Stopping;
                                else
                                    continue;
                            }

                            break;
                        }
                        
                        Thread.Sleep(20);
                    }

                    lock (lockStatus)
                    {
                        CriticalArea = false;
                        if (Status != Status.QueueEmpty) break;
                        Status = Status.Running;
                    }
                }
                
                lock (lockCodecCtx)
                {
                    if (Status == Status.Stopped || demuxer.SubtitlesPackets.Count == 0) continue;
                    demuxer.SubtitlesPackets.TryDequeue(out IntPtr pktPtr);
                    packet = (AVPacket*) pktPtr;
                    int gotFrame = 0;
                    AVSubtitle sub = new AVSubtitle();
                    ret = avcodec_decode_subtitle2(codecCtx, &sub, &gotFrame, packet);
                    if (ret < 0)
                    {
                        allowedErrors--;
                        Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                        if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); Status = Status.Stopping; break; }

                        continue;
                    }
                            
                    if (gotFrame < 1 || sub.num_rects < 1 ) continue;
                    if (packet->pts == AV_NOPTS_VALUE) { avsubtitle_free(&sub); av_packet_free(&packet); continue; }

                    SubtitlesFrame mFrame = ProcessSubtitlesFrame(packet, &sub);
                    if (mFrame != null) Frames.Enqueue(mFrame);

                    avsubtitle_free(&sub);
                    av_packet_free(&packet);
                }
            } while (Status == Status.Running);
        }

        private SubtitlesFrame ProcessSubtitlesFrame(AVPacket* packet, AVSubtitle* sub)
        {
            SubtitlesFrame mFrame;
            if (Speed != 1)
            {
                curSpeedFrame++;
                if (curSpeedFrame < Speed) return null;
                curSpeedFrame = 0;
                sub->start_display_time /= (uint) Speed;
                sub->end_display_time /= (uint) Speed;
                mFrame = new SubtitlesFrame();
                mFrame.timestamp = ((long)(packet->pts * SubtitlesStream.Timebase) - demuxer.StartTime) + Config.Audio.Latency + Config.Subtitles.Delay;
                mFrame.timestamp /= Speed;
            }
            else {
                mFrame = new SubtitlesFrame();
                mFrame.timestamp = ((long)(packet->pts * SubtitlesStream.Timebase) - demuxer.StartTime) + Config.Audio.Latency + Config.Subtitles.Delay;
            }

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

                        return null;
                }

                mFrame.text         = SSAtoSubStyles(line, out List<SubStyle> subStyles);
                mFrame.subStyles    = subStyles;
                mFrame.duration     = (int) (sub->end_display_time - sub->start_display_time);
            } catch (Exception e) {  Log("[ProcessSubtitlesFrame] [Error] " + e.Message + " - " + e.StackTrace); return null; }

            return mFrame;
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
                                if (color.value != Color.Transparent) styles.Add(color);
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

                                color.value = Color.FromArgb(255, red, green, blue);
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

        public void DisposeFrames()
        {
            Frames = new ConcurrentQueue<SubtitlesFrame>();
        }
    }
}