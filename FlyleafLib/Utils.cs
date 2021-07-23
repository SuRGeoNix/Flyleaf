﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

using Microsoft.Win32;

using SharpDX;
//using SharpDX.D3DCompiler; // Enable this if you need to re-compile shaders

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

namespace FlyleafLib
{
    public unsafe static class Utils
    {
        #region MediaEngine
        //public static private bool            IsDesignMode=> (bool) DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue;
        public static bool          IsDesignMode    = LicenseManager.UsageMode == LicenseUsageMode.Designtime; // Will not work properly (need to be called from non-static class constructor)
        //public static bool          IsWin10         = Regex.IsMatch(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString(), "Windows 10");
        public static List<string>  MovieExts       = new List<string>() { "mp4", "m4v", "m4e", "mkv", "mpg", "mpeg" , "mpv", "mp4p", "mpe" , "m1v", "m2ts", "m2p", "m2v", "movhd", "moov", "movie", "movx", "mjp", "mjpeg", "mjpg", "amv" , "asf", "m4v", "3gp", "ogm", "ogg", "vob", "ts", "rm", "3gp", "3gp2", "3gpp", "3g2", "f4v", "f4a", "f4p", "f4b", "mts", "m2ts", "gifv", "avi", "mov", "flv", "wmv", "qt", "avchd", "swf", "cam", "nsv", "ram", "rm", "x264", "xvid", "wmx", "wvx", "wx", "video", "viv", "vivo", "vid", "dat", "bik", "bix", "dmf", "divx" };
        public static List<string>  SubsExts        = new List<string>() { "srt", "txt", "sub", "ssa", "ass" };

        public static List<string> GetMoviesSorted(List<string> movies)
        {
            List<string> moviesSorted = new List<string>();

            for (int i=0; i<movies.Count; i++)
            {
                string ext = Path.GetExtension(movies[i]);
                if (ext == null || ext.Trim() == "") continue;

                if (MovieExts.Contains(ext.Substring(1,ext.Length-1))) moviesSorted.Add(movies[i]);
            }

            moviesSorted.Sort(new NaturalStringComparer());

            return moviesSorted;
        }
        public sealed class NaturalStringComparer : IComparer<string> { public int Compare(string a, string b) { return NativeMethods.StrCmpLogicalW(a, b); } }

        public static string GetRecInnerException(Exception e)
        {
            string dump = "";
            var cur = e.InnerException;

            for (int i=0; i<4; i++)
            {
                if (cur == null) break;
                dump += "\r\n - " + cur.Message;
                cur = cur.InnerException;
            }

            return dump;
        }
        public static string GetUrlExtention(string url) { return url.LastIndexOf(".")  > 0 ? url.Substring(url.LastIndexOf(".") + 1) : ""; }
        public static List<Language> GetSystemLanguages()
        {
            List<Language>  Languages  = new List<Language>();
            Language        systemLang = Language.Get(CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            if (systemLang.LanguageName != "English") Languages.Add(systemLang);

            foreach (System.Windows.Forms.InputLanguage lang in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
                if (Language.Get(lang.Culture.TwoLetterISOLanguageName).ISO639 != systemLang.ISO639 && Language.Get(lang.Culture.TwoLetterISOLanguageName).LanguageName != "English") 
                    Languages.Add(Language.Get(lang.Culture.TwoLetterISOLanguageName));

            Languages.Add(Language.Get("English"));

            return Languages;
        }

        public static void EnsureThreadDone(Thread t, long maxMS = 2000, int minMS = 10)
        {
            if (t == null || !t.IsAlive) return;

            if (t.ManagedThreadId == Thread.CurrentThread.ManagedThreadId)
                { Log($"Thread {t.Name} is not allowed to suicide!"); return; }

            long escapeInfinity = maxMS / minMS;

            while (t != null && t.IsAlive && escapeInfinity > 0)
            {
                Thread.Sleep(minMS);
                escapeInfinity--;
            }

            if (t != null && t.IsAlive)
            {
                Log($"Thread {t.Name} did not finished properly!");
                throw new Exception($"Thread {t.Name} did not finished properly!");
            }
        }

        public static string GetUserDownloadPath() { try { return Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders\").GetValue("{374DE290-123F-4565-9164-39C4925E467B}").ToString(); } catch (Exception) { return null; } }

        static List<PerformanceCounter> gpuCounters;

        public static void GetGPUCounters()
        {
            var category        = new PerformanceCounterCategory("GPU Engine");
            var counterNames    = category.GetInstanceNames();
            gpuCounters         = new List<PerformanceCounter>();

            foreach (string counterName in counterNames)
                if (counterName.EndsWith("engtype_3D"))
                    foreach (PerformanceCounter counter in category.GetCounters(counterName))
                        if (counter.CounterName == "Utilization Percentage")
                            gpuCounters.Add(counter);
        }
        public static float GetGPUUsage()
        {
            float result = 0f;

            try
            {
                if (gpuCounters == null) GetGPUCounters();

                gpuCounters.ForEach(x => { _ = x.NextValue(); });
                Thread.Sleep(1000);
                gpuCounters.ForEach(x => { result += x.NextValue(); });

            } catch (Exception e) { Log($"[GPUUsage] Error {e.Message}"); result = -1f; GetGPUCounters(); }

            return result;
        }
        public static string GZipDecompress(string filename)
        {
            string newFileName = "";

            FileInfo fileToDecompress = new FileInfo(filename);
            using (FileStream originalFileStream = fileToDecompress.OpenRead())
            {
                string currentFileName = fileToDecompress.FullName;
                newFileName = currentFileName.Remove(currentFileName.Length - fileToDecompress.Extension.Length);

                using (FileStream decompressedFileStream = File.Create(newFileName))
                {
                    using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                    {
                        decompressionStream.CopyTo(decompressedFileStream);
                    }
                }
            }

            return newFileName;
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
        
        public static System.Windows.Media.Color SharpDXToWpfColor(Color sColor) { return System.Windows.Media.Color.FromArgb(sColor.A, sColor.R, sColor.G, sColor.B); }
        public static Color WpfToSharpDXColor(System.Windows.Media.Color wColor) { return new Color(wColor.R, wColor.G, wColor.B, wColor.A); }
        public static string ToHexadecimal(byte[] bytes)
        {
            StringBuilder hexBuilder = new StringBuilder();
            for(int i = 0; i < bytes.Length; i++)
            {
                hexBuilder.Append(bytes[i].ToString("x2"));
            }
            return hexBuilder.ToString();
        }
        public static int GCD(int a, int b) { return b == 0 ? a : GCD(b, a % b); }
        public static string TicksToTime(long ticks) { return new TimeSpan(ticks).ToString(@"hh\:mm\:ss\:fff"); }
        private static void Log(string msg) { try { System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [MediaEngine] {msg}"); } catch (Exception) { System.Diagnostics.Debug.WriteLine($"[............] [MediaFramework] {msg}"); } } // System.ArgumentOutOfRangeException ???
        #endregion

        public unsafe static class FFmpeg
        {
            public static string ErrorCodeToMsg(int error)
            {
                byte* buffer = stackalloc byte[10240];
                av_strerror(error, buffer, 10240);
                return Marshal.PtrToStringAnsi((IntPtr)buffer);
            }
            public static av_log_set_callback_callback ffmpegLogCallback = (p0, level, format, vl) =>
            {
                if (level > av_log_get_level()) return;

                var buffer = stackalloc byte[10240];
                var printPrefix = 1;
                av_log_format_line(p0, level, format, vl, buffer, 10240, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)buffer);
                Log(line.Trim());
            };
            private static void Log(string msg) { try { System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [FFmpeg] {msg}"); } catch (Exception) { System.Diagnostics.Debug.WriteLine($"[............] [MediaFramework] {msg}"); } } // System.ArgumentOutOfRangeException ???
        }

        public static class NativeMethods
        {
            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
            public static extern int StrCmpLogicalW(string psz1, string psz2);

            [DllImport ( "user32.dll" )]
            public static extern int SetWindowLong (IntPtr hWnd, int nIndex, uint dwNewLong);

            [DllImport("user32.dll",SetLastError = true)]
            public static extern bool GetWindowInfo(IntPtr hwnd, ref WINDOWINFO pwi);

            [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
            public static extern uint TimeBeginPeriod(uint uMilliseconds);

            [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
            public static extern uint TimeEndPeriod(uint uMilliseconds);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto,SetLastError = true)]
            public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
            [FlagsAttribute]
            public enum EXECUTION_STATE :uint
            {
                ES_AWAYMODE_REQUIRED    = 0x00000040,
                ES_CONTINUOUS           = 0x80000000,
                ES_DISPLAY_REQUIRED     = 0x00000002,
                ES_SYSTEM_REQUIRED      = 0x00000001
            }

            [DllImport("user32.dll")]
            public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

            public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

            [DllImport("user32.dll")]
            public static extern bool GetWindowRect(IntPtr hwnd, ref RECT rectangle);

            [StructLayout(LayoutKind.Sequential)]
            public struct WINDOWINFO
            {
                public uint cbSize;
                public RECT rcWindow;
                public RECT rcClient;
                public uint dwStyle;
                public uint dwExStyle;
                public uint dwWindowStatus;
                public uint cxWindowBorders;
                public uint cyWindowBorders;
                public ushort atomWindowType;
                public ushort wCreatorVersion;

                public WINDOWINFO(Boolean ?   filler)   :   this()   // Allows automatic initialization of "cbSize" with "new WINDOWINFO(null/true/false)".
                {
                    cbSize = (UInt32)(Marshal.SizeOf(typeof( WINDOWINFO )));
                }

            }
            public struct RECT
            {
                public int Left     { get; set; }
                public int Top      { get; set; }
                public int Right    { get; set; }
                public int Bottom   { get; set; }
            }
        }

        public static class SubtitleConverter
        {
            public static bool Convert(string fileNameIn, string fileNameOut, Encoding input, Encoding output)
            {
                try
                {
                    StreamReader sr = new StreamReader(new FileStream(fileNameIn, FileMode.Open), input);
                    StreamWriter sw = new StreamWriter(new FileStream(fileNameOut, FileMode.Create), output);

                    sw.Write(sr.ReadToEnd());
                    sw.Flush();
                    sr.Close();
                    sw.Close();

                }
                catch (Exception e) { System.Diagnostics.Debug.WriteLine($"[Subs Convert] {e.Message}"); return false; }

                return true;
            }
            public static Encoding Detect(string fileName)
            {
                Encoding ret = Encoding.Default;

                // Check if BOM
                if ((ret = CheckByBOM(fileName)) != Encoding.Default) return ret;

                // Check if UTF-8
                using (BufferedStream fstream = new BufferedStream(File.OpenRead(fileName)))
                {
                    if (IsUtf8(fstream)) ret = Encoding.UTF8;
                }

                return ret;
            }
            public static Encoding CheckByBOM(string path)
            {
                if (path == null) throw new ArgumentNullException("path");

                var encodings = Encoding.GetEncodings()
                    .Select(e => e.GetEncoding())
                    .Select(e => new { Encoding = e, Preamble = e.GetPreamble() })
                    .Where(e => e.Preamble.Any())
                    .ToArray();

                var maxPrembleLength = encodings.Max(e => e.Preamble.Length);
                byte[] buffer = new byte[maxPrembleLength];

                using (var stream = File.OpenRead(path))
                {
                    stream.Read(buffer, 0, (int)Math.Min(maxPrembleLength, stream.Length));
                }

                return encodings
                    .Where(enc => enc.Preamble.SequenceEqual(buffer.Take(enc.Preamble.Length)))
                    .Select(enc => enc.Encoding)
                    .FirstOrDefault() ?? Encoding.Default;
            }
            public static bool IsUtf8(Stream stream)
            {
                int count = 4 * 1024;
                byte[] buffer;
                int read;
                while (true)
                {
                    buffer = new byte[count];
                    stream.Seek(0, SeekOrigin.Begin);
                    read = stream.Read(buffer, 0, count);
                    if (read < count)
                    {
                        break;
                    }
                    buffer = null;
                    count *= 2;
                }
                return IsUtf8(buffer, read);
            }
            public static bool IsUtf8(byte[] buffer, int length)
            {
                int position = 0;
                int bytes = 0;
                while (position < length - 4)
                {
                    if (!IsValid(buffer, position, length, ref bytes))
                    {
                        return false;
                    }
                    position += bytes;
                }
                return true;
            }
            public static bool IsValid(byte[] buffer, int position, int length, ref int bytes)
            {
                if (length > buffer.Length)
                {
                    throw new ArgumentException("Invalid length");
                }

                if (position > length - 1)
                {
                    bytes = 0;
                    return true;
                }

                byte ch = buffer[position];

                if (ch <= 0x7F)
                {
                    bytes = 1;
                    return true;
                }

                if (ch >= 0xc2 && ch <= 0xdf)
                {
                    if (position >= length - 2)
                    {
                        bytes = 0;
                        return false;
                    }
                    if (buffer[position + 1] < 0x80 || buffer[position + 1] > 0xbf)
                    {
                        bytes = 0;
                        return false;
                    }
                    bytes = 2;
                    return true;
                }

                if (ch == 0xe0)
                {
                    if (position >= length - 3)
                    {
                        bytes = 0;
                        return false;
                    }

                    if (buffer[position + 1] < 0xa0 || buffer[position + 1] > 0xbf ||
                        buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf)
                    {
                        bytes = 0;
                        return false;
                    }
                    bytes = 3;
                    return true;
                }


                if (ch >= 0xe1 && ch <= 0xef)
                {
                    if (position >= length - 3)
                    {
                        bytes = 0;
                        return false;
                    }

                    if (buffer[position + 1] < 0x80 || buffer[position + 1] > 0xbf ||
                        buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf)
                    {
                        bytes = 0;
                        return false;
                    }

                    bytes = 3;
                    return true;
                }

                if (ch == 0xf0)
                {
                    if (position >= length - 4)
                    {
                        bytes = 0;
                        return false;
                    }

                    if (buffer[position + 1] < 0x90 || buffer[position + 1] > 0xbf ||
                        buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf ||
                        buffer[position + 3] < 0x80 || buffer[position + 3] > 0xbf)
                    {
                        bytes = 0;
                        return false;
                    }

                    bytes = 4;
                    return true;
                }

                if (ch == 0xf4)
                {
                    if (position >= length - 4)
                    {
                        bytes = 0;
                        return false;
                    }

                    if (buffer[position + 1] < 0x80 || buffer[position + 1] > 0x8f ||
                        buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf ||
                        buffer[position + 3] < 0x80 || buffer[position + 3] > 0xbf)
                    {
                        bytes = 0;
                        return false;
                    }

                    bytes = 4;
                    return true;
                }

                if (ch >= 0xf1 && ch <= 0xf3)
                {
                    if (position >= length - 4)
                    {
                        bytes = 0;
                        return false;
                    }

                    if (buffer[position + 1] < 0x80 || buffer[position + 1] > 0xbf ||
                        buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf ||
                        buffer[position + 3] < 0x80 || buffer[position + 3] > 0xbf)
                    {
                        bytes = 0;
                        return false;
                    }

                    bytes = 4;
                    return true;
                }

                return false;
            }
        }

        #region Enable this if you need to re-compile shaders
        //public static class MediaRenderer
        //{
        //    /// <summary>
        //    /// Using Project Resources and gets all byte[] data
        //    /// </summary>
        //    public static void CompileShaders()
        //    {
        //        // v5
        //        //string vertexProfile    = "vs_5_0";
        //        //string pixelProfile     = "ps_5_0";

        //        // v4
        //        string vertexProfile    = "vs_4_0_level_9_1";
        //        string pixelProfile     = "ps_4_0_level_9_1";

        //        string shadersCode = "";
        //        System.Resources.ResourceSet rsrcSet = Properties.Resources.ResourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);
        //        foreach (System.Collections.DictionaryEntry entry in rsrcSet)
        //            if (entry.Value is byte[])
        //            {
        //                byte[] byteCode = ShaderBytecode.Compile((byte[])rsrcSet.GetObject(entry.Key.ToString()), "main", entry.Key.ToString() == "VertexShader" ? pixelProfile : vertexProfile, ShaderFlags.Debug);

        //                shadersCode += $"{{ \"{entry.Key.ToString()}\", new byte[] {{ {byteCode[0]}";
        //                for (int i = 1; i < byteCode.Length; i++)
        //                    shadersCode += $",{byteCode[i]}";
        //                shadersCode += $" }}}},\r\n";
        //            }

        //        File.WriteAllText(@"Shaders.cs", shadersCode);
        //    }
        //}
        #endregion
    }


    [Serializable] // https://scatteredcode.net/c-serializable-dictionary/
    public class SerializableDictionary<TKey, TVal> : Dictionary<TKey, TVal>, IXmlSerializable, ISerializable
    {
        #region Private Properties
        protected XmlSerializer ValueSerializer
        {
            get { return _valueSerializer ?? (_valueSerializer = new XmlSerializer(typeof(TVal))); }
        }
        private XmlSerializer KeySerializer
        {
            get { return _keySerializer ?? (_keySerializer = new XmlSerializer(typeof(TKey))); }
        }
        #endregion
        #region Private Members
        private XmlSerializer _keySerializer;
        private XmlSerializer _valueSerializer;
        #endregion
        #region Constructors
        public SerializableDictionary()
        {
        }
        public SerializableDictionary(IDictionary<TKey, TVal> dictionary) : base(dictionary) { }
        public SerializableDictionary(IEqualityComparer<TKey> comparer) : base(comparer) { }
        public SerializableDictionary(int capacity) : base(capacity) { }
        public SerializableDictionary(IDictionary<TKey, TVal> dictionary, IEqualityComparer<TKey> comparer)
          : base(dictionary, comparer) { }
        public SerializableDictionary(int capacity, IEqualityComparer<TKey> comparer)
          : base(capacity, comparer) { }
        #endregion
        #region ISerializable Members
        protected SerializableDictionary(SerializationInfo info, StreamingContext context)
        {
            int itemCount = info.GetInt32("itemsCount");
            for (int i = 0; i < itemCount; i++)
            {
                KeyValuePair<TKey, TVal> kvp = (KeyValuePair<TKey, TVal>)info.GetValue(String.Format(CultureInfo.InvariantCulture, "Item{0}", i), typeof(KeyValuePair<TKey, TVal>));
                Add(kvp.Key, kvp.Value);
            }
        }
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("itemsCount", Count);
            int itemIdx = 0; foreach (KeyValuePair<TKey, TVal> kvp in this)
            {
                info.AddValue(String.Format(CultureInfo.InvariantCulture, "Item{0}", itemIdx), kvp, typeof(KeyValuePair<TKey, TVal>));
                itemIdx++;
            }
        }
        #endregion
        #region IXmlSerializable Members
        void IXmlSerializable.WriteXml(XmlWriter writer)
        {
            foreach (KeyValuePair<TKey, TVal> kvp in this)
            {
                writer.WriteStartElement("item");
                writer.WriteStartElement("key");
                KeySerializer.Serialize(writer, kvp.Key);
                writer.WriteEndElement();
                writer.WriteStartElement("value");
                ValueSerializer.Serialize(writer, kvp.Value);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }
        void IXmlSerializable.ReadXml(XmlReader reader)
        {
            if (reader.IsEmptyElement)
            {
                return;
            }
            // Move past container
            if (reader.NodeType == XmlNodeType.Element && !reader.Read())
                throw new XmlException("Error in De serialization of SerializableDictionary");
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                reader.ReadStartElement("item");
                reader.ReadStartElement("key");
                TKey key = (TKey)KeySerializer.Deserialize(reader);
                reader.ReadEndElement();
                reader.ReadStartElement("value");
                TVal value = (TVal)ValueSerializer.Deserialize(reader);
                reader.ReadEndElement();
                reader.ReadEndElement();
                Add(key, value);
                reader.MoveToContent();
            }
            // Move past container
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                reader.ReadEndElement();
            }
            else
            {
                throw new XmlException("Error in Deserialization of SerializableDictionary");
            }
        }
        XmlSchema IXmlSerializable.GetSchema()
        {
            return null;
        }
        #endregion
    }
}