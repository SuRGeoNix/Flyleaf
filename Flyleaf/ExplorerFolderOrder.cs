using System.IO;
using System.Runtime.InteropServices;

using Microsoft.CSharp.RuntimeBinder;

namespace Flyleaf;

internal static class ExplorerFolderOrder
{
    static readonly Guid shellWindowsClass = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    static readonly Guid topLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    const uint AllItemsInViewOrder = 0x80000002; // SVGIO_ALLVIEW | SVGIO_FLAG_VIEWORDER
    const uint FileSystemPath = 0x80058000; // SIGDN_FILESYSPATH
    const uint NoStrCmpLogical = 0x4000007E; // REST_NOSTRCMPLOGICAL

    internal static bool UsesLiteralNameOrder() => SHRestricted(NoStrCmpLogical) != 0;

    [DllImport("shell32.dll", ExactSpelling = true)]
    static extern uint SHRestricted(uint restriction);

    internal static Dictionary<string, Dictionary<string, int>> Capture(string folder, bool includeSubfolders, CancellationToken cancellation)
    {
        var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        bool ShouldCapture(string path) => !result.ContainsKey(path) &&
            (path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
             includeSubfolders && path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase));
        object? collection = null;
        try
        {
            collection = Activator.CreateInstance(Type.GetTypeFromCLSID(shellWindowsClass, throwOnError: true)!);
            dynamic windows = collection!;
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                object? window = null;
                try
                {
                    window = windows.Item(i);
                    if (window is IServiceProvider browserService &&
                        GetMatchingViewOrder(browserService, ShouldCapture, cancellation) is { } match)
                        result.Add(match.Path, match.Order);
                }
                catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException or
                    UnauthorizedAccessException or ArgumentException)
                {
                    // Explorer can navigate away, close, or expose a virtual folder during enumeration.
                }
                finally { Release(window); }
            }
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException or UnauthorizedAccessException)
        {
            // The Shell collection may be unavailable. The caller falls back to the Shell name policy.
        }
        finally { Release(collection); }
        return result;
    }

    static (string Path, Dictionary<string, int> Order)? GetMatchingViewOrder(
        IServiceProvider service, Func<string, bool> shouldCapture, CancellationToken cancellation)
    {
        var browserId = typeof(IShellBrowser).GUID;
        service.QueryService(topLevelBrowser, browserId, out var browserObject);
        try
        {
            if (browserObject is not IShellBrowser browser) return null;
            browser.QueryActiveShellView(out var shellView);
            try
            {
                if (shellView is not IFolderView folderView || GetFolderPath(folderView) is not { } path ||
                    !shouldCapture(path)) return null;
                var arrayId = typeof(IShellItemArray).GUID;
                folderView.Items(AllItemsInViewOrder, arrayId, out var arrayObject);
                try
                {
                    if (arrayObject is not IShellItemArray items) return null;
                    var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    items.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        items.GetItemAt(i, out var item);
                        try
                        {
                            if (item.GetDisplayName(FileSystemPath, out var name) >= 0 && name != null)
                                order.TryAdd(name, (int)i);
                        }
                        finally { Release(item); }
                    }
                    return (path, order);
                }
                finally { Release(arrayObject); }
            }
            finally { Release(shellView); }
        }
        finally { Release(browserObject); }
    }

    static string? GetFolderPath(IFolderView view)
    {
        var folderId = typeof(IPersistFolder2).GUID;
        view.GetFolder(folderId, out var folderObject);
        try
        {
            if (folderObject is not IPersistFolder2 folder) return null;
            folder.GetCurFolder(out var pidl);
            try
            {
                if (pidl == 0 || SHGetNameFromIDList(pidl, FileSystemPath, out var path) < 0 || path == null)
                    return null;
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        finally { Release(folderObject); }
    }

    [DllImport("shell32.dll")]
    static extern int SHGetNameFromIDList(nint pidl, uint format, [MarshalAs(UnmanagedType.LPWStr)] out string? name);

    static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    // Unused methods occupy their documented vtable slots.
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        void QueryService(in Guid service, in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();
        void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFolderView
    {
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder(in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object folder);
        void Item();
        void ItemCount();
        void Items(uint flags, in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object array);
    }

    [ComImport, Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFolder2
    {
        void GetClassID();
        void Initialize();
        void GetCurFolder(out nint pidl);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemArray
    {
        void BindToHandler();
        void GetPropertyStore();
        void GetPropertyDescriptionList();
        void GetAttributes();
        void GetCount(out uint count);
        void GetItemAt(uint index, out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler();
        void GetParent();
        [PreserveSig]
        int GetDisplayName(uint type, [MarshalAs(UnmanagedType.LPWStr)] out string? name);
    }
}
