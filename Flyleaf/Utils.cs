using System;
using System.IO;
using System.IO.Compression;
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
            if (t != null && !t.IsAlive) return;

            long escapeInfinity = maxMS / minMS;

            while (t != null && t.IsAlive && escapeInfinity > 0)
            {
                Thread.Sleep(minMS);
                escapeInfinity--;
            }

            if (t != null && t.IsAlive)
            {
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
        public static string TicksToTime(long ticks) { return new TimeSpan(ticks).ToString(@"hh\:mm\:ss\:fff"); }
    }
}
