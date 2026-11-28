using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FlyleafLib.Plugins.LibAss;

internal static unsafe class LibAssNative
{
    const string Library = "Flyleaf.LibAss.Native";
    static readonly Lazy<nint> native = new(() => NativeLibrary.Load(Path.Combine(
        Path.GetDirectoryName(typeof(LibAssNative).Assembly.Location), "native", "win-x64", "ass.dll")));

    static LibAssNative() => NativeLibrary.SetDllImportResolver(typeof(LibAssNative).Assembly, Resolve);

    static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath) => name == Library ? native.Value : 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Image
    {
        public int Width, Height, Stride;
        public byte* Bitmap;
        public uint Color;
        public int X, Y;
        public Image* Next;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ass_library_version();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint ass_library_init();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_library_done(nint library);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_set_extract_fonts(nint library, int enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_add_font(nint library, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte* data, int size);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint ass_renderer_init(nint library);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_renderer_done(nint renderer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_set_fonts(nint renderer, nint defaultFont, [MarshalAs(UnmanagedType.LPUTF8Str)] string defaultFamily, int provider, nint config, int update);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_set_frame_size(nint renderer, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_set_storage_size(nint renderer, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_set_pixel_aspect(nint renderer, double aspect);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_set_line_position(nint renderer, double position);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint ass_new_track(nint library);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_free_track(nint track);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_process_codec_private(nint track, byte* header, int size);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_set_check_readorder(nint track, int enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_process_chunk(nint track, byte* data, int size, long start, long duration);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_configure_prune(nint track, long delay);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern Image* ass_render_frame(nint renderer, nint track, long timestamp, out int changed);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_set_style_overrides(nint library, nint* overrides);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ass_process_force_style(nint track);
}
