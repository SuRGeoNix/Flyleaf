using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.AVCodecID;

namespace SuRGeoNix.Flyleaf.MediaFramework
{
    public unsafe class Utils
    {
        public static string TicksToTime(long ticks) { return new TimeSpan(ticks).ToString(@"hh\:mm\:ss\:fff"); }



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
                    RootPath = ffmpegBinaryPath;
                    uint ver = ffmpeg.avformat_version();
                    Log($"[Version: {ver >> 16}.{ver >> 8 & 255}.{ver & 255}] [Location: {ffmpegBinaryPath}]");

                    return;
                }
                current = Directory.GetParent(current)?.FullName;
            }
        }
        public unsafe static string BytePtrToStringUTF8(byte* bytePtr)
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
        public static string ErrorCodeToMsg(int error)
        {
            byte* buffer = stackalloc byte[1024];
            av_strerror(error, buffer, 1024);
            return Marshal.PtrToStringAnsi((IntPtr)buffer);
        }
        public static av_log_set_callback_callback ffmpegLogCallback = (p0, level, format, vl) =>
        {
            if (level > av_log_get_level()) return;

            var buffer = stackalloc byte[1024];
            var printPrefix = 1;
            av_log_format_line(p0, level, format, vl, buffer, 1024, &printPrefix);
            var line = Marshal.PtrToStringAnsi((IntPtr)buffer);
            Log(line.Trim());
        };

        private static void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [MediaFramework] {msg}"); }
    }
}
