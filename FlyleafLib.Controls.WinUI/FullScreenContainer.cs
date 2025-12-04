using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using WinRT.Interop;

namespace FlyleafLib.Controls.WinUI;

public sealed class FullScreenContainer : ContentControl
{
    public event EventHandler<EventArgs>? FullScreenEntering;
    public event EventHandler<EventArgs>? FullScreenEnter;
    public event EventHandler<EventArgs>? FullScreenExiting;
    public event EventHandler<EventArgs>? FullScreenExit;

    public bool IsFullScreen
    {
        get { return (bool)GetValue(IsFullScreenProperty); }
        set { SetValue(IsFullScreenProperty, value); }
    }
    public static readonly DependencyProperty IsFullScreenProperty =
        DependencyProperty.Register(nameof(IsFullScreen), typeof(bool), typeof(FullScreenContainer), new PropertyMetadata(false, OnFullScreenChanged));

    private static void OnFullScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = d as FullScreenContainer;
        if (host == null)
            return;

        if ((bool)e.NewValue)
            host.FullScreen();
        else
            host.ExitFullScreen();
    }

    public nint OwnerHwnd;
    public AppWindow Owner
    {
        get { return (AppWindow)GetValue(OwnerProperty); }
        set { SetValue(OwnerProperty, value); }
    }
    public static readonly DependencyProperty OwnerProperty =
        DependencyProperty.Register(nameof(Owner), typeof(AppWindow), typeof(FullScreenContainer), new PropertyMetadata(null));

    public nint FSWHwnd;
    public static Window?       FSW;
    public static AppWindow?    FSWApp;
    private static Grid         FSWGrid = new();

    public static FullScreenContainer?
                                FSCInUse; // Each monitor should have one

    public static int           FSCCounter;

    public static event EventHandler? CustomizeFullScreenWindow;

    public FullScreenContainer()
    {
        DefaultStyleKey = typeof(FullScreenContainer);
        IsTabStop = false;


        Loaded += (o, e) => FSCCounter++;
        Unloaded += (o, e) =>
        {
            FSCCounter--;
            if (FSCCounter == 0)
                FSW?.Close();
        };
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        var contentIsland = XamlRoot?.ContentIslandEnvironment?.AppWindowId;

        foreach (IntPtr hWnd in EnumerateProcessWindowHandles(Process.GetCurrentProcess().Id))
        {
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);

            if (wndId == contentIsland)
            {
                var appWin = AppWindow.GetFromWindowId(wndId);
                OwnerHwnd = hWnd;
                Owner = appWin;
                break;
            }
        }

        if (FSW == null)
            CreateFSW();
    }

    private void CreateFSW()
    {
        FSW = new Window();
        FSW.Content = FSWGrid;

        FSWHwnd = WindowNative.GetWindowHandle(FSW);
        WindowId wndId = Win32Interop.GetWindowIdFromWindow(FSWHwnd);

        FSWApp = AppWindow.GetFromWindowId(wndId);
        OverlappedPresenter op = ((OverlappedPresenter)FSWApp.Presenter);
        op.SetBorderAndTitleBar(false, false);
        op.IsMinimizable = false;
        op.IsResizable = false;

        FSWApp.IsShownInSwitchers = true;
        FSWApp.Resize(new(1,1));
        FSWApp.Hide();

        CustomizeFullScreenWindow?.Invoke(FSW, new());
    }

    public void FullScreen()
    {
        if (FSCInUse != null)
        {
            if (FSCInUse == this)
                return;

            FSCInUse.IsFullScreen = false;
        }

        if (Content == null || FSW == null || FSWApp == null)
            return;

        FSCInUse = this;
        FullScreenEntering?.Invoke(this, new());

        // Move FSW to current FSC before FullScreen to ensure right Screen?
        var p = GetPosition();
        if (Owner != null)
        {
            p.X += Owner.Position.X;
            p.Y += Owner.Position.Y;
        }
        FSWApp.Move(p);
        FSWApp.SetPresenter(AppWindowPresenterKind.FullScreen);

        Owner?.Hide();

        // Move Content from current FSC to FSW
        UIElement content = (UIElement)Content;
        Content = null;
        FSWGrid.DataContext = DataContext;
        FSWGrid.Children.Add(content);

        // WinUI bug: Activate will not SetForegroundWindow
        FSW.Activate();
        Task.Run(() =>
        {
            Thread.Sleep(100);
            SetForegroundWindow(FSWHwnd);
        });

        FullScreenEnter?.Invoke(this, new());
    }

    public void ExitFullScreen()
    {
        if (FSW == null || FSWApp == null || FSCInUse == null)
            return;

        FullScreenExiting?.Invoke(this, new());

        // Move Content from FSW to FSCInuse
        UIElement content = FSWGrid.Children[0];
        FSWGrid.Children.Clear();
        FSWGrid.DataContext = null;
        FSCInUse.Content = content;

        // Hide & Reset FSW
        FSWApp.SetPresenter(AppWindowPresenterKind.Overlapped);
        FSWApp.Resize(new(1, 1));
        FSWApp.Hide();

        FSCInUse = null;

        // WinUI bug: Activate will not SetForegroundWindow
        Owner.Show();
        Task.Run(() =>
        {
            Thread.Sleep(100);
            SetForegroundWindow(OwnerHwnd);
        });

        FullScreenExit?.Invoke(this, new());
    }

    [DllImport("user32.dll")]
    static extern void SetForegroundWindow(IntPtr hWnd);

    private PointInt32 GetPosition()
    {
        PointInt32 p = new();
        FrameworkElement parent = this;

        do
        {
            p.X += (int)parent.ActualOffset.X;
            p.Y += (int)parent.ActualOffset.Y;

            if (parent.Parent != null && parent.Parent is FrameworkElement)
                parent = (FrameworkElement)parent.Parent;
            else
                break;

        } while (true);

        return p;
    }

    delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumThreadWindows(int dwThreadId, EnumThreadDelegate lpfn,
        IntPtr lParam);

    static IEnumerable<IntPtr> EnumerateProcessWindowHandles(int processId)
    {
        var handles = new List<IntPtr>();

        foreach (ProcessThread thread in Process.GetProcessById(processId).Threads)
            EnumThreadWindows(thread.Id,
                (hWnd, lParam) => { handles.Add(hWnd); return true; }, IntPtr.Zero);

        return handles;
    }
}

public interface IFullScreen
{
    public bool IsFullScreen { get; set; }
}
