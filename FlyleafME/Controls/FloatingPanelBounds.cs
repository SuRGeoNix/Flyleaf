using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Flyleaf.Controls.WPF;

internal static class FloatingPanelBounds
{
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);
    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromRect(ref NativeRect bounds, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    // A monitor may have disappeared since the panel was last shown. Work in native
    // screen pixels after WPF has applied the target monitor's DPI and size limits.
    internal static void KeepOnScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds)) return;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref bounds, 2), ref monitor)) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var fitted = Fit(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
            monitor.Work.Left, monitor.Work.Top, monitor.Work.Right, monitor.Work.Bottom,
            (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY));
        SetWindowPos(handle, IntPtr.Zero, fitted.Left, fitted.Top, fitted.Width, fitted.Height, 0x0004 | 0x0010);
    }

    internal static (int Left, int Top, int Width, int Height) Fit(int left, int top, int width, int height,
        int workLeft, int workTop, int workRight, int workBottom, int minWidth, int minHeight)
    {
        width = Math.Clamp(width, minWidth, Math.Max(minWidth, workRight - workLeft));
        height = Math.Clamp(height, minHeight, Math.Max(minHeight, workBottom - workTop));
        return (Math.Clamp(left, workLeft, Math.Max(workLeft, workRight - width)),
            Math.Clamp(top, workTop, Math.Max(workTop, workBottom - height)), width, height);
    }

    internal static bool IsValid(double left, double top, double width, double height,
        double minWidth, double minHeight) =>
        double.IsFinite(left) && double.IsFinite(top) &&
        double.IsFinite(width) && double.IsFinite(height) &&
        width >= minWidth && height >= minHeight;
}
