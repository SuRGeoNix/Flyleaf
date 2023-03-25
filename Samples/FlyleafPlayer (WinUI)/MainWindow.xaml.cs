using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;

using System;
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

    public MainWindow()
    {
        Title = "Flyleaf";
        // Initializes Engine (Specifies FFmpeg libraries path which is required)
        Engine.Start(new EngineConfig()
        {
            #if DEBUG
            LogOutput       = ":debug",
            LogLevel        = LogLevel.Debug,
            FFmpegLogLevel  = FFmpegLogLevel.Warning,
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
        FSC.RegisterPropertyChangedCallback(FullScreenContainer.IsFullScreenProperty, FSC_IsFullScreenChanged);

        InitDragMove();
    }

    private void FullScreenContainer_CustomizeFullScreenWindow(object sender, EventArgs e)
    {
        FullScreenContainer.FSW.AppWindow.Title = Title + " (FS)";
        FullScreenContainer.FSW.AppWindow.IsShownInSwitchers = true;
        FullScreenContainer.FSW.Closed += (o, e) => Close();
    }

    private void FSC_IsFullScreenChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (FSC.IsFullScreen)
        {
            btnFullScreen.Content = iconNormal;
            ((OverlappedPresenter)AppWindow.Presenter).Minimize();
            FullScreenContainer.FSW.Activate();
            AppWindow.IsShownInSwitchers = false;
        }
        else
        {
            btnFullScreen.Content = iconFullScreen;
            AppWindow.IsShownInSwitchers = true;
            ((OverlappedPresenter)AppWindow.Presenter).Restore();
            Activate();
        }

        flyleafHost.KFC.Focus(FocusState.Keyboard);
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
