using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace FlyleafLib.Controls.WinUI;

public sealed class FullScreenContainer : ContentControl
{
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

    public static Window?       FSW;
    public static AppWindow?    FSWApp;
    private static Grid         FSWGrid = new();

    public static FullScreenContainer? 
                                FSCInUse; // Each monitor should have one

    public static int           FSCCounter;

    public AppWindow?           Owner;

    
    public static event EventHandler? CustomizeFullScreenWindow;

    public FullScreenContainer()
    {
        DefaultStyleKey = typeof(FullScreenContainer);
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
        
        var visual = ElementCompositionPreview.GetElementVisual(this);
        visual.Compositor.DispatcherQueue.TryEnqueue(() =>
        {
            var contentIsland = ContentIsland.FindByVisual(visual);
            
            foreach (IntPtr hWnd in EnumerateProcessWindowHandles(Process.GetCurrentProcess().Id))
            {
                WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWin = AppWindow.GetFromWindowId(wndId);
                
                if (wndId == contentIsland.Window.WindowId)
                    Owner = appWin;
            }

        });

        if (FSW == null)
            CreateFSW();
    }
    
    private void CreateFSW()
    {
        FSW = new Window();
        FSW.Content = FSWGrid;

        IntPtr hWnd = WindowNative.GetWindowHandle(FSW);
        WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
        
        FSWApp = AppWindow.GetFromWindowId(wndId);
        OverlappedPresenter op = ((OverlappedPresenter)FSWApp.Presenter);
        op.SetBorderAndTitleBar(false, false);
        op.IsMinimizable = false;
        op.IsResizable = false;
        
        FSWApp.IsShownInSwitchers = false;
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

        // Move FSW to current FSC before FullScreen to ensure right Screen?
        var p = GetPosition();
        if (Owner != null)
        {
            p.X += Owner.Position.X;
            p.Y += Owner.Position.Y;
        }
        FSWApp.Move(p);
        FSWApp.SetPresenter(AppWindowPresenterKind.FullScreen);

        // Move Content from current FSC to FSW
        UIElement content = (UIElement)Content;
        Content = null;
        FSWGrid.DataContext = DataContext;
        FSWGrid.Children.Add(content);
        FSW.Activate();
    }

    public void ExitFullScreen()
    {
        if (FSW == null || FSWApp == null || FSCInUse == null)
            return;

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

        // OwnerWindow.Activate() ?
    }

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

