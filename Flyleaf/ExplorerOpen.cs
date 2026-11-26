using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Threading;

namespace Flyleaf;

// The class factory and its interfaces must be public for the COM callable wrapper.
public static class ExplorerOpen
{
    internal const string ClassId = "{1E4ED613-96E7-4606-BD34-EE8EA617F26D}";
    const uint LocalServer = 4, SingleUse = 0, Copy = 1, Shift = 4;
    const int ShiftVirtualKey = 0x10;

    internal static bool IsActivation(string[] args) => args is [var flag] &&
        (flag.Equals("-Embedding", StringComparison.OrdinalIgnoreCase) || flag.Equals("/Embedding", StringComparison.OrdinalIgnoreCase));

    internal static LaunchRequest? Receive(Guid classId)
    {
        if (OleInitialize(0) < 0) return null;
        try
        {
            var frame = new DispatcherFrame();
            var receiver = new SelectionReceiver(frame);
            // Receive one Explorer selection before starting the engine or forwarding to an existing window.
            int result = CoRegisterClassObject(ref classId, receiver, LocalServer, SingleUse, out uint cookie);
            if (result < 0) return null;

            var timeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
            timeout.Tick += (_, _) => frame.Continue = false;
            try
            {
                timeout.Start();
                Dispatcher.PushFrame(frame);
                return receiver.Request;
            }
            finally
            {
                timeout.Stop();
                CoRevokeClassObject(cookie);
            }
        }
        finally { OleUninitialize(); }
    }

    static string[] ReadFiles(IDataObject data)
    {
        var format = new FORMATETC { cfFormat = 15, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL }; // CF_HDROP
        data.GetData(ref format, out var medium);
        try
        {
            uint count = DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
            var files = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(medium.unionmember, i, null, 0);
                if (length == 0) continue;
                var file = new StringBuilder(checked((int)length + 1));
                if (DragQueryFile(medium.unionmember, i, file, (uint)file.Capacity) == length)
                    files.Add(file.ToString());
            }
            return files.ToArray();
        }
        finally { ReleaseStgMedium(ref medium); }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class SelectionReceiver : IClassFactory, IDropTarget
    {
        readonly DispatcherFrame frame;
        internal LaunchRequest? Request { get; private set; }
        internal SelectionReceiver(DispatcherFrame frame) => this.frame = frame;

        int IClassFactory.CreateInstance(nint outer, in Guid interfaceId, out nint instance)
        {
            instance = 0;
            if (outer != 0) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
            nint unknown = Marshal.GetIUnknownForObject(this);
            try { return Marshal.QueryInterface(unknown, in interfaceId, out instance); }
            finally { Marshal.Release(unknown); }
        }

        int IClassFactory.LockServer(bool locked) => 0;
        int IDropTarget.DragEnter(IDataObject data, uint keys, PointL point, ref uint effect) { effect &= Copy; return 0; }
        int IDropTarget.DragOver(uint keys, PointL point, ref uint effect) { effect &= Copy; return 0; }
        int IDropTarget.DragLeave() => 0;

        int IDropTarget.Drop(IDataObject data, uint keys, PointL point, ref uint effect)
        {
            effect = 0;
            LaunchRequest? request = null;
            try
            {
                var files = ReadFiles(data);
                if (files.Length == 0) return 0;
                request = new(files, StartMinimized: false, NewWindow: (keys & Shift) != 0 || GetAsyncKeyState(ShiftVirtualKey) < 0);
                effect = Copy;
                return 0;
            }
            catch (COMException ex) { return ex.HResult; }
            finally
            {
                frame.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    Request = request;
                    frame.Continue = false;
                }));
            }
        }
    }

    [ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IClassFactory
    {
        [PreserveSig] int CreateInstance(nint outer, in Guid interfaceId, out nint instance);
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
    }

    [ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keys, PointL point, ref uint effect);
        [PreserveSig] int DragOver(uint keys, PointL point, ref uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keys, PointL point, ref uint effect);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointL { public int X, Y; }

    [DllImport("ole32.dll")] static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] static extern void OleUninitialize();
    [DllImport("ole32.dll")] static extern int CoRegisterClassObject(ref Guid classId, [MarshalAs(UnmanagedType.Interface)] IClassFactory factory, uint context, uint flags, out uint cookie);
    [DllImport("ole32.dll")] static extern int CoRevokeClassObject(uint cookie);
    [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern uint DragQueryFile(nint drop, uint index, StringBuilder? path, uint capacity);
}
