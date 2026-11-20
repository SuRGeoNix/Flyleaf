using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Flyleaf.Controls.WPF;

namespace Flyleaf;

// FlyleafHost has separate video/overlay HWNDs. Route edge input from all three
// windows to the owner, using one outer-window coordinate system.
internal sealed class WindowResizeFrame : IDisposable
{
    internal const int NativeBottomFramePixels = 1;
    readonly Window owner;
    readonly FlyleafME view;
    readonly HwndSource main;
    readonly List<HwndSource> sources = new();

    internal WindowResizeFrame(Window owner, FlyleafME view, HwndSource main)
    {
        this.owner = owner; this.view = view; this.main = main;
        AddSource(main);
        view.Host.SurfaceCreated += HostWindowsChanged;
        view.Host.OverlayCreated += HostWindowsChanged;
        view.Loaded += HostWindowsChanged;
        AttachChildren();
        // Layered WPF windows own their client area. Keep only the resize hit testing.
        if (owner.AllowsTransparency) return;

        // The retained native edge makes this window eligible for Windows 11's
        // automatic rounding. Match the square WPF outline explicitly.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            const int windowCornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
            int doNotRound = 1; // DWMWCP_DONOTROUND
            int result = DwmSetWindowAttribute(main.Handle, windowCornerPreference, ref doNotRound, sizeof(int));
            if (result < 0) System.Diagnostics.Debug.WriteLine($"Setting square window corners failed: 0x{result:X8}");
        }
        // The HWND already exists at SourceInitialized. Recalculate its client
        // area once so the caption is removed before the window is shown.
        SetWindowPos(main.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0001 | 0x0002 | 0x0004 | 0x0010);
    }

    bool CanResize => owner.WindowState == WindowState.Normal && !view.IsFullScreen &&
        owner.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    void HostWindowsChanged(object? sender, EventArgs e) => AttachChildren();

    void AttachChildren()
    {
        foreach (var hwnd in new[] { view.Host.SurfaceHandle, view.Host.OverlayHandle })
            if (hwnd != IntPtr.Zero && HwndSource.FromHwnd(hwnd) is { } source) AddSource(source);
    }

    void AddSource(HwndSource source)
    {
        if (sources.Contains(source)) return;
        source.AddHook(WindowHook);
        sources.Add(source);
    }

    int HitTest(IntPtr location)
    {
        if (!GetWindowRect(main.Handle, out var bounds)) return 0;
        long packed = location.ToInt64();
        int x = unchecked((short)(packed & 0xFFFF));
        int y = unchecked((short)((packed >> 16) & 0xFFFF));
        var dpi = VisualTreeHelper.GetDpi(owner);
        return WindowResizeGeometry.HitTest(x, y, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
            (int)Math.Ceiling(7 * dpi.DpiScaleX), (int)Math.Ceiling(7 * dpi.DpiScaleY));
    }

    IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (hwnd == main.Handle && !owner.AllowsTransparency)
        {
            if (msg == 0x0083) // WM_NCCALCSIZE
            {
                // Remove the caption, but retain one physical pixel of native
                // frame at the bottom. This avoids a fully client-area opaque
                // HWND while keeping the title area available to our WPF header.
                // Both forms of lParam begin with the proposed client RECT.
                if (lParam != IntPtr.Zero && !view.IsFullScreen)
                {
                    var client = Marshal.PtrToStructure<NativeRect>(lParam);
                    if (client.Bottom > client.Top)
                    {
                        client.Bottom -= NativeBottomFramePixels;
                        Marshal.StructureToPtr(client, lParam, false);
                    }
                }
                // Preserve existing client pixels; do not request WVR_REDRAW.
                handled = true;
                return IntPtr.Zero;
            }
            if (msg == 0x0086 && owner.WindowState != WindowState.Minimized) // WM_NCACTIVATE
            {
                // Preserve activation behavior without drawing a native caption.
                handled = true;
                return DefWindowProc(hwnd, msg, wParam, (IntPtr)(-1));
            }
        }
        if (!CanResize) return IntPtr.Zero;

        if (msg == 0x0084) // WM_NCHITTEST
        {
            int edge = HitTest(lParam);
            if (edge != 0) { handled = true; return (IntPtr)edge; }
        }
        else if (msg == 0x0020) // WM_SETCURSOR, including child-to-parent forwarding
        {
            int edge = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            int cursor = edge switch
            {
                10 or 11 => 32644, // IDC_SIZEWE
                12 or 15 => 32645, // IDC_SIZENS
                13 or 17 => 32642, // IDC_SIZENWSE
                14 or 16 => 32643, // IDC_SIZENESW
                _ => 0
            };
            if (cursor != 0)
            {
                SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)cursor));
                handled = true;
                return (IntPtr)1;
            }
        }
        else if (msg == 0x00A1 && hwnd != main.Handle && wParam.ToInt64() is >= 10 and <= 17) // WM_NCLBUTTONDOWN
        {
            int edge = HitTest(lParam);
            if (edge != 0)
            {
                handled = true;
                ReleaseCapture();
                SendMessage(main.Handle, msg, (IntPtr)edge, lParam);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        view.Host.SurfaceCreated -= HostWindowsChanged;
        view.Host.OverlayCreated -= HostWindowsChanged;
        view.Loaded -= HostWindowsChanged;
        foreach (var source in sources) if (!source.IsDisposed) source.RemoveHook(WindowHook);
        sources.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")] static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] static extern IntPtr DefWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
