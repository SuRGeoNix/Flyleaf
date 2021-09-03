using System;

using Vortice.DXGI;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public unsafe class RendererInfo
    {
        public string   AdapterDesc     { get; set; }
        public UInt64   SystemMemory    { get; set; }
        public UInt64   VideoMemory     { get; set; }
        public UInt64   SharedMemory    { get; set; }
        public int      Outputs         { get; set; }
        public string   OutputName      { get; set; }
        public int      ScreenWidth     { get; set; }
        public int      ScreenHeight    { get; set; }
        public System.Drawing.Rectangle ScreenBounds { get; set; }

        public static void Fill(Renderer renderer, IDXGIAdapter1 adapter)
        {
            RendererInfo ri = new RendererInfo();

            ri.AdapterDesc  = adapter.Description.Description;
            ri.SystemMemory = (UInt64)((IntPtr)adapter.Description.DedicatedSystemMemory).ToPointer();
            ri.VideoMemory  = (UInt64)((IntPtr)adapter.Description.DedicatedVideoMemory).ToPointer();
            ri.SharedMemory = (UInt64)((IntPtr)adapter.Description.SharedSystemMemory).ToPointer();

            int maxVerticalResolution = 0;
            for(int i=0; ; i++)
            {
                var res = adapter.EnumOutputs(i, out IDXGIOutput output);
                if (output == null) break;

                var bounds = output.Description.DesktopCoordinates;

                if (maxVerticalResolution < bounds.Bottom - bounds.Top) maxVerticalResolution = bounds.Bottom - bounds.Top;

                if (i == 0)
                {
                    ri.OutputName   = output.Description.DeviceName;
                    ri.ScreenBounds = new System.Drawing.Rectangle(new System.Drawing.Point(bounds.Top, bounds.Left), new System.Drawing.Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
                    ri.ScreenWidth  = bounds.Right - bounds.Left;
                    ri.ScreenHeight = bounds.Bottom - bounds.Top;
                }

                output.Dispose();
            }
            renderer.Config.Video.MaxVerticalResolutionAuto = maxVerticalResolution;

            renderer.Info = ri;
        }

        public string GetBytesReadable(UInt64 i)
        {
            // Determine the suffix and readable value
            string suffix;
            double readable;
            if (i >= 0x1000000000000000) // Exabyte
            {
                suffix = "EB";
                readable = (i >> 50);
            }
            else if (i >= 0x4000000000000) // Petabyte
            {
                suffix = "PB";
                readable = (i >> 40);
            }
            else if (i >= 0x10000000000) // Terabyte
            {
                suffix = "TB";
                readable = (i >> 30);
            }
            else if (i >= 0x40000000) // Gigabyte
            {
                suffix = "GB";
                readable = (i >> 20);
            }
            else if (i >= 0x100000) // Megabyte
            {
                suffix = "MB";
                readable = (i >> 10);
            }
            else if (i >= 0x400) // Kilobyte
            {
                suffix = "KB";
                readable = i;
            }
            else
            {
                return i.ToString("0 B"); // Byte
            }
            // Divide by 1024 to get fractional value
            readable = (readable / 1024);
            // Return formatted number with suffix
            return readable.ToString("0.### ") + suffix;
        }

        public override string ToString()
        {
            var gcd = Utils.GCD(ScreenWidth, ScreenHeight);
            return $"[Adapter] {AdapterDesc} System: {GetBytesReadable(SystemMemory)} Video: {GetBytesReadable(VideoMemory)} Shared: {GetBytesReadable(SharedMemory)}\r\n[Output ] {OutputName} (X={ScreenBounds.X}, Y={ScreenBounds.Y}) {ScreenWidth}x{ScreenHeight}" + (gcd > 0 ? $" [{ScreenWidth/gcd}:{ScreenHeight/gcd}]" : "");
        }
    }
}