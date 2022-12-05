﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows;

namespace FlyleafLib
{
    public static partial class Utils
    {    
        public static class ZOrderHandler
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

            enum GetWindow_Cmd : uint {
                GW_HWNDFIRST = 0,
                GW_HWNDLAST = 1,
                GW_HWNDNEXT = 2,
                GW_HWNDPREV = 3,
                GW_OWNER = 4,
                GW_CHILD = 5,
                GW_ENABLEDPOPUP = 6
            }

            public class ZOrder
            {
                public string window;
                public int order;
            }

            public class Owner
            {
                static int uniqueNameId = 0;

                public List<ZOrder> CurZOrder = null;
                public List<ZOrder> SavedZOrder = null;

                public Window Window;
                public IntPtr WindowHwnd;

                Dictionary<string, IntPtr> WindowNamesHandles = new Dictionary<string, IntPtr>();
                Dictionary<string, Window> WindowNamesWindows = new Dictionary<string, Window>();

                public Owner(Window window, IntPtr windowHwnd)
                {
                    Window = window;
                    WindowHwnd = windowHwnd;
                    Window.StateChanged += Window_StateChanged;
                    lastState = Window.WindowState;

                    // TBR: Minimize with WindowsKey + D for example will not fire (none of those)
                    //HwndSource source = HwndSource.FromHwnd(WindowHwnd);
                    //source.AddHook(new HwndSourceHook(WndProc));
                }

                public const Int32 WM_SYSCOMMAND = 0x112;
                public const Int32 SC_MAXIMIZE = 0xF030;
                private const int SC_MINIMIZE = 0xF020;
                private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                {
                    if (msg == WM_SYSCOMMAND)
                    {
                        if (wParam.ToInt32() == SC_MINIMIZE)
                        {
                            Save();
                            //handled = true;
                        }
                    }
                    return IntPtr.Zero;
                }

                private IntPtr GetHandle(Window window)
                {
                    IntPtr hwnd = IntPtr.Zero;

                    if (string.IsNullOrEmpty(window.Name))
                    {
                        hwnd = new WindowInteropHelper(window).Handle;
                        window.Name = "Zorder" + (uniqueNameId++).ToString();
                        WindowNamesHandles.Add(window.Name, hwnd);
                        WindowNamesWindows.Add(window.Name, window);
                    }
                    else if (!WindowNamesHandles.ContainsKey(window.Name))
                    {
                        hwnd = new WindowInteropHelper(window).Handle;
                        WindowNamesHandles.Add(window.Name, hwnd);
                        WindowNamesWindows.Add(window.Name, window);
                    }
                    else
                        hwnd = WindowNamesHandles[window.Name];

                    return hwnd;
                }

                WindowState lastState = WindowState.Normal;

                private void Window_StateChanged(object sender, EventArgs e)
                {
                    if (Window.OwnedWindows.Count < 2)
                        return;

                    if (lastState == WindowState.Minimized)
                        Restore();
                    else if (Window.WindowState == WindowState.Minimized)
                        Save();

                    lastState = Window.WindowState;
                }

                public void Save()
                {
                    SavedZOrder = GetZOrder();
                    Debug.WriteLine("Saved");
                    DumpZOrder(SavedZOrder);
                }

                public void Restore()
                {
                    if (SavedZOrder == null)
                        return;

                    Task.Run(() =>
                    {
                        System.Threading.Thread.Sleep(50);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            for (int i=0; i<SavedZOrder.Count; i++)
                                if (WindowNamesWindows.TryGetValue(SavedZOrder[i].window, out Window window) && window.ShowActivated == false)
                                    window.Activate();

                            Debug.WriteLine("Restored");
                            DumpZOrder(GetZOrder());
                        });
                    });
                }

                public List<ZOrder> GetZOrder()
                {
                    List<ZOrder> zorders = new List<ZOrder>();

                    foreach(Window window in Window.OwnedWindows)
                    {
                        ZOrder zorder = new ZOrder();
                        IntPtr curHwnd = GetHandle(window);
                        zorder.window = window.Name;

                        while ((curHwnd = GetWindow(curHwnd, (uint)GetWindow_Cmd.GW_HWNDNEXT)) != WindowHwnd)
                            zorder.order++;

                        zorders.Add(zorder);
                    }

                    return zorders.OrderBy((o) => o.order).ToList<ZOrder>();
                }

                public void DumpZOrder(List<ZOrder> zorders)
                {
                    for (int i=0; i<zorders.Count; i++)
                        Debug.WriteLine(zorders[i].order + " " + zorders[i].window + " | " + WindowNamesWindows[zorders[i].window].Background);
                }
            }

            public static Dictionary<IntPtr, Owner> Owners = new Dictionary<IntPtr, Owner>();

            public static void Register(Window window)
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (Owners.ContainsKey(hwnd))
                    return;

                Owners.Add(hwnd, new Owner(window, hwnd));
            }
        }
    }
}
