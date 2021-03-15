using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Security;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct2D1;
using SharpDX.Direct3D11;
using SharpDX.WIC;

using Device = SharpDX.Direct3D11.Device;

namespace SuRGeoNix.Flyleaf
{
    public class Utils
    {
        public static List<string> MovieExts = new List<string>() { "mp4", "m4v", "m4e", "mkv", "mpg", "mpeg" , "mpv", "mp4p", "mpe" , "m1v", "m2ts", "m2p", "m2v", "movhd", "moov", "movie", "movx", "mjp", "mjpeg", "mjpg", "amv" , "asf", "m4v", "3gp", "ogm", "ogg", "vob", "ts", "rm", "3gp", "3gp2", "3gpp", "3g2", "f4v", "f4a", "f4p", "f4b", "mts", "m2ts", "gifv", "avi", "mov", "flv", "wmv", "qt", "avchd", "swf", "cam", "nsv", "ram", "rm", "x264", "xvid", "wmx", "wvx", "wx", "video", "viv", "vivo", "vid", "dat", "bik", "bix", "dmf", "divx" };
        public static List<string> SubsExts  = new List<string>() { "srt", "txt", "sub", "ssa", "ass" };

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

        public static string[] GetSystemLanguages()
        {
            List<string>   Languages  = new List<string>();
            Language       systemLang = Language.Get(System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            if (systemLang.LanguageName != "English") Languages.Add(systemLang.ToString());

            foreach (System.Windows.Forms.InputLanguage lang in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
                if (Language.Get(lang.Culture.TwoLetterISOLanguageName).ISO639 != systemLang.ISO639 && Language.Get(lang.Culture.TwoLetterISOLanguageName).LanguageName != "English") Languages.Add(Language.Get(lang.Culture.TwoLetterISOLanguageName).ToString());

            Languages.Add(Language.Get("English").ToString());

            return Languages.ToArray();
        }

        public static Texture2D LoadImage(Device device, System.Drawing.Bitmap bitmap)
        {
            // From File?
            //var bitmap = (System.Drawing.Bitmap)System.Drawing.Image.FromFile(@"c:\...", true);

            var rectArea = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rectArea, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            int stride = bitmap.Width * 4;
            Texture2D texture =  new Texture2D(device, new Texture2DDescription()
            {
                Width           = bitmap.Width,
                Height          = bitmap.Height,
                ArraySize       = 1,
                BindFlags       = BindFlags.ShaderResource,
                Usage           = ResourceUsage.Immutable,
                CpuAccessFlags  = CpuAccessFlags.None,
                Format          = Format.B8G8R8A8_UNorm,
                MipLevels       = 1,
                OptionFlags     = ResourceOptionFlags.None,
                SampleDescription = new SampleDescription(1, 0),
            }, new DataRectangle(bitmapData.Scan0, stride));
            bitmap.UnlockBits(bitmapData);

            return texture;
        }
        public static Texture2D LoadImage(Device device, RenderTarget rtv2d, string filename)
        {
            var imgFactory = new ImagingFactory();

            BitmapDecoder bmpDecoder = new BitmapDecoder(imgFactory, filename, DecodeOptions.CacheOnDemand);
            FormatConverter bitmapSource = new FormatConverter(imgFactory);
            bitmapSource.Initialize(bmpDecoder.GetFrame(0), SharpDX.WIC.PixelFormat.Format32bppPRGBA);

            SharpDX.Direct2D1.Bitmap bitmap = Bitmap1.FromWicBitmap(rtv2d, bitmapSource);
            
            int stride = bitmapSource.Size.Width * 4;
            using (var buffer = new DataStream(bitmapSource.Size.Height * stride, true, true))
            {
                bitmapSource.CopyPixels(stride, buffer);
                Texture2D texture =  new Texture2D(device, new Texture2DDescription()
                {
                    Width           = bitmapSource.Size.Width,
                    Height          = bitmapSource.Size.Height,
                    ArraySize       = 1,
                    BindFlags       = BindFlags.ShaderResource,
                    Usage           = ResourceUsage.Immutable,
                    CpuAccessFlags  = CpuAccessFlags.None,
                    Format          = Format.R8G8B8A8_UNorm,
                    MipLevels       = 1,
                    OptionFlags     = ResourceOptionFlags.None,
                    SampleDescription = new SampleDescription(1, 0),
                }, new DataRectangle(buffer.DataPointer, stride));

                Utilities.Dispose(ref bmpDecoder);
                Utilities.Dispose(ref bitmapSource);
                Utilities.Dispose(ref bitmap);

                return texture;
            }
        }
        public static Texture2D LoadImageConvert(Device device, System.Drawing.Bitmap bitmap)
        {
            var rectArea = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            //var bitmapProperties = new BitmapProperties(new SharpDX.Direct2D1.PixelFormat(Format.R8G8B8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied));
            //var size = new Size2(bitmap.Width, bitmap.Height);

            int stride = bitmap.Width * 4;
            using (var buffer = new DataStream(bitmap.Height * stride, true, true))
            {
                var bitmapData = bitmap.LockBits(rectArea, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                for (int y = 0; y < bitmap.Height; y++)
                {
                    int offset = bitmapData.Stride * y;
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        byte B = Marshal.ReadByte(bitmapData.Scan0, offset++);
                        byte G = Marshal.ReadByte(bitmapData.Scan0, offset++);
                        byte R = Marshal.ReadByte(bitmapData.Scan0, offset++);
                        byte A = Marshal.ReadByte(bitmapData.Scan0, offset++);
                        int rgba = R | (G << 8) | (B << 16) | (A << 24);
                        buffer.Write(rgba);
                    }
 
                }
                bitmap.UnlockBits(bitmapData);

                // Bitmap?
                //buffer.Position = 0;
                //SharpDX.Direct2D1.Bitmap bitmap2 = new SharpDX.Direct2D1.Bitmap(rtv2d, size, tempStream, stride, bitmapProperties);

                Texture2D texture =  new Texture2D(device, new Texture2DDescription()
                {
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    ArraySize = 1,
                    BindFlags = BindFlags.ShaderResource,
                    Usage = ResourceUsage.Immutable,
                    CpuAccessFlags = CpuAccessFlags.None,
                    Format = Format.R8G8B8A8_UNorm,
                    MipLevels = 1,
                    OptionFlags = ResourceOptionFlags.None,
                    SampleDescription = new SampleDescription(1, 0),
                }, new DataRectangle(buffer.DataPointer, stride));

                Utilities.Dispose(ref bitmap);

                return texture;
            }
        }

        public static void EnsureThreadDone(Thread t, long maxMS = 250, int minMS = 10)
        {
            if (t == null || !t.IsAlive) return;

            long escapeInfinity = maxMS / minMS;

            while (t != null && t.IsAlive && escapeInfinity > 0)
            {
                Thread.Sleep(minMS);
                escapeInfinity--;
            }

            if (t != null && t.IsAlive)
            {
                Console.WriteLine("Thread X did not finished properly!");

                t.Abort();
                escapeInfinity = maxMS / minMS;
                while (t != null && t.IsAlive && escapeInfinity > 0)
                {
                    Thread.Sleep(minMS);
                    escapeInfinity--;
                }
            }
        }
        public static T Clone<T>(T source)
        {
            if (!typeof(T).IsSerializable) throw new ArgumentException("The type must be serializable.", nameof(source));

            // Don't serialize a null object, simply return the default for that object
            if (Object.ReferenceEquals(source, null)) return default(T);

            IFormatter formatter = new BinaryFormatter();
            Stream stream = new MemoryStream();
            using (stream)
            {
                formatter.Serialize(stream, source);
                stream.Seek(0, SeekOrigin.Begin);
                return (T)formatter.Deserialize(stream);
            }
        }

        public static string ToHexadecimal(byte[] bytes)
        {
            StringBuilder hexBuilder = new StringBuilder();
            for(int i = 0; i < bytes.Length; i++)
            {
                hexBuilder.Append(bytes[i].ToString("x2"));
            }
            return hexBuilder.ToString();
        }
        public static string GZipDecompress(string filename)
        {
            //File.OpenRead(filename);
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

        public static string GetUserDownloadPath()
        {
            try { return Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders\").GetValue("{374DE290-123F-4565-9164-39C4925E467B}").ToString(); } catch (Exception) { return null; }
        }

        public static long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                if (!Directory.Exists(path)) return 0;
                size = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            } catch (Exception) { size = -1; }
            
            return size;
        }
        public static string BytesToReadableString_(long bytes) // BitSwarm *
        {
            string bd = "";

            if (        bytes < 1024)
                bd =    bytes + " B";
            else if (   bytes > 1024    && bytes < 1024 * 1024)
                bd =    String.Format("{0:n1}",bytes / 1024.0)                  + " KB";
            else if (   bytes > 1024 * 1024 && bytes < 1024 * 1024 * 1024)
                bd =    String.Format("{0:n1}",bytes / (1024 * 1024.0))         + " MB";
            else if (   bytes > 1024 * 1024 * 1024 )
                bd =    String.Format("{0:n1}",bytes / (1024 * 1024 * 1024.0))  + " GB";

            return bd;
        }

        public static string TicksToTime(long ticks) { return new TimeSpan(ticks).ToString(@"hh\:mm\:ss\:fff"); }

        public static void Shutdown()
        {
            ManagementBaseObject mboShutdown = null;
            ManagementClass mcWin32 = new ManagementClass("Win32_OperatingSystem");
            mcWin32.Get();

            // You can't shutdown without security privileges
            mcWin32.Scope.Options.EnablePrivileges = true;
            ManagementBaseObject mboShutdownParams = mcWin32.GetMethodParameters("Win32Shutdown");

             // Flag 1 means we want to shut down the system. Use "2" to reboot.
            mboShutdownParams["Flags"] = "1";
            mboShutdownParams["Reserved"] = "0";
            foreach (ManagementObject manObj in mcWin32.GetInstances())
                mboShutdown = manObj.InvokeMethod("Win32Shutdown",  mboShutdownParams, null);
        }
        
        [SuppressUnmanagedCodeSecurity]
        internal static class SafeNativeMethods
        {
            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
            public static extern int StrCmpLogicalW(string psz1, string psz2);
        }
        public sealed class NaturalStringComparer : IComparer<string>
        {
            public int Compare(string a, string b)
            {
                return SafeNativeMethods.StrCmpLogicalW(a, b);
            }
        }
        public static long GetLastInputTime()
        {
            //uint idleTime = 0;
            LASTINPUTINFO lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf( lastInputInfo );
            lastInputInfo.dwTime = 0;

            uint envTicks = (uint)Environment.TickCount;

            if ( GetLastInputInfo( ref lastInputInfo ) ) return envTicks - lastInputInfo.dwTime;
            
            return -1;
            //return (( idleTime > 0 ) ? ( idleTime / 1000 ) : 0);
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [StructLayout( LayoutKind.Sequential )]
        struct LASTINPUTINFO
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(LASTINPUTINFO));

            [MarshalAs(UnmanagedType.U4)]
            public UInt32 cbSize;    
            [MarshalAs(UnmanagedType.U4)]
            public UInt32 dwTime;
        }
    }
}