using Vortice.DXGI;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public unsafe class RendererInfo
    {
        public string   AdapterDesc     { get; set; }
        public long     AdapterLuid     { get; set; }
        public long     SystemMemory    { get; set; }
        public long     VideoMemory     { get; set; }
        public long     SharedMemory    { get; set; }
        public int      Outputs         { get; set; }
        public string   OutputName      { get; set; }
        public int      ScreenWidth     { get; set; }
        public int      ScreenHeight    { get; set; }
        public string   Vendor          { get; set; }
        public System.Drawing.Rectangle ScreenBounds { get; set; }

        public static void Fill(Renderer renderer, IDXGIAdapter1 adapter)
        {
            RendererInfo ri = new RendererInfo();
            
            ri.AdapterLuid  = adapter.Description1.Luid;
            ri.AdapterDesc  = adapter.Description1.Description;
            ri.SystemMemory = adapter.Description1.DedicatedSystemMemory;
            ri.VideoMemory  = adapter.Description1.DedicatedVideoMemory;
            ri.SharedMemory = adapter.Description1.SharedSystemMemory;
            ri.Vendor       = VendorIdStr(adapter.Description1.VendorId);

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

            renderer.AdapterInfo = ri;
        }

        public static string VendorIdStr(int vendorId)
        {
            switch (vendorId)
            {
                case 0x1002:
                    return "ATI";
                case 0x10DE:
                    return "NVIDIA";
                case 0x1106:
                    return "VIA";
                case 0x8086:
                    return "Intel";
                case 0x5333:
                    return "S3 Graphics";
                case 0x4D4F4351:
                    return "Qualcomm";
                default:
                    return "Unknown";
            }
        }

        public static string GetBytesReadable(long i)
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
            return readable.ToString("0.## ") + suffix;
        }

        public override string ToString()
        {
            var gcd = Utils.GCD(ScreenWidth, ScreenHeight);
            return $"[Adapter-{AdapterLuid}] {AdapterDesc} System: {GetBytesReadable(SystemMemory)} Video: {GetBytesReadable(VideoMemory)} Shared: {GetBytesReadable(SharedMemory)}\r\n[Output ] {OutputName} (X={ScreenBounds.X}, Y={ScreenBounds.Y}) {ScreenWidth}x{ScreenHeight}" + (gcd > 0 ? $" [{ScreenWidth/gcd}:{ScreenHeight/gcd}]" : "");
        }
    }
}