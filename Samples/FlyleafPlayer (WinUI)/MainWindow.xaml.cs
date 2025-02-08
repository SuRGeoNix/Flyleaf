using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

using WinRT.Interop;

using FlyleafLib;
using FlyleafLib.Controls.WinUI;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer__WinUI_;

public sealed partial class MainWindow : Window
{
    public Player Player { get; set; }

    SymbolIcon iconNormal = new SymbolIcon(Symbol.BackToWindow);
    SymbolIcon iconFullScreen = new SymbolIcon(Symbol.FullScreen);
    SymbolIcon iconPlay = new SymbolIcon(Symbol.Play);
    SymbolIcon iconPause = new SymbolIcon(Symbol.Pause);

    LogHandler Log = new LogHandler("[Main] ");
    AppWindow MainAppWindow;

    public MainWindow()
    {
        Title = "Flyleaf";
        // Initializes Engine (Specifies FFmpeg libraries path which is required)
        Engine.Start(new EngineConfig()
        {
            #if DEBUG
            LogOutput       = ":debug",
            LogLevel        = LogLevel.Debug,
            FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Warn,
            #endif

            UIRefresh       = false, // For Activity Mode usage
            PluginsPath     = ":Plugins",
            FFmpegPath      = ":FFmpeg",
        });

        Player = new Player();

        FullScreenContainer.CustomizeFullScreenWindow += FullScreenContainer_CustomizeFullScreenWindow;

        InitializeComponent();
        rootGrid.DataContext = this;

        btnFullScreen.Content = FSC.IsFullScreen ? iconNormal : iconFullScreen;
        btnPlayback.Content = Player.Status == Status.Paused ? iconPlay : iconPause;
        Player.PropertyChanged += Player_PropertyChanged;

        InitDragMove();

        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
        MainAppWindow = AppWindow.GetFromWindowId(wndId);

        // WinUI bug: keyboard focus
        FSC.FullScreenEnter += (o, e) =>
        {
            btnFullScreen.Content = iconNormal;
            MainAppWindow.IsShownInSwitchers = false;
            flyleafHost.KFC.Focus(FocusState.Keyboard);
        };

        FSC.FullScreenExit += (o, e) =>
        {
            btnFullScreen.Content = iconFullScreen;
            MainAppWindow.IsShownInSwitchers = true;
            Task.Run(() => { Thread.Sleep(10); Utils.UIInvoke(() => flyleafHost.KFC.Focus(FocusState.Keyboard)); });
        };

        rootGrid.PointerReleased += (o, e) =>
        {
            Task.Run(() => { Thread.Sleep(10); Utils.UIInvoke(() => flyleafHost.KFC.Focus(FocusState.Keyboard)); });
        };

        #if DEBUG
        FocusManager.GotFocus += (o, e) =>
        {
            if (e.NewFocusedElement is FrameworkElement fe)
                Log.Info($"Focus to {fe.GetType()} | {fe.Name}");
            else
                Log.Info($"Focus to null");
        };
        #endif
    }

    private void FullScreenContainer_CustomizeFullScreenWindow(object sender, EventArgs e)
    {
        FullScreenContainer.FSWApp.Title = Title + " (FS)";
        FullScreenContainer.FSW.Closed += (o, e) => Close();
    }

    private void Player_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Player.Status):
                btnPlayback.Content = Player.Status == Status.Paused ? iconPlay : iconPause;

                break;
        }
    }

    #region DragMove (Should be added within FlyleafHost?)
    [DllImport("User32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GetCursorPos(out Windows.Graphics.PointInt32 lpPoint);

    int nX = 0, nY = 0, nXWindow = 0, nYWindow = 0;
    bool bMoving = false;
    AppWindow _apw;

    private void InitDragMove()
    {
        _apw = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));
        UIElement root = Content;
        root.PointerMoved += Root_PointerMoved;
        root.PointerPressed += Root_PointerPressed;
        root.PointerReleased += Root_PointerReleased;
    }

    Pointer cur;
    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {

        ((UIElement)sender).ReleasePointerCaptures();
        bMoving = false;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        cur = null;
        var properties = e.GetCurrentPoint((UIElement)sender).Properties;
        if (properties.IsLeftButtonPressed)
        {
            cur = e.Pointer;
            ((UIElement)sender).CapturePointer(e.Pointer);
            nXWindow = _apw.Position.X;
            nYWindow = _apw.Position.Y;
            Windows.Graphics.PointInt32 pt;
            GetCursorPos(out pt);
            nX = pt.X;
            nY = pt.Y;
            bMoving = true;
        }
    }
    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint((UIElement)sender).Properties;
        if (properties.IsLeftButtonPressed)
        {
            Windows.Graphics.PointInt32 pt;
            GetCursorPos(out pt);

            if (bMoving)
                _apw.Move(new Windows.Graphics.PointInt32(nXWindow + (pt.X- nX), nYWindow + (pt.Y - nY)));

            e.Handled = true;
        }
    }
    #endregion
}
