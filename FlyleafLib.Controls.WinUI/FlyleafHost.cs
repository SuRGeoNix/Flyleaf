using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using System;

using Vortice.DXGI;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls.WinUI;

[TemplatePart(Name = nameof(KFC), Type = typeof(KeyboardFocusControl))]
[TemplatePart(Name = nameof(SCP), Type = typeof(SwapChainPanel))]
public sealed class FlyleafHost : ContentControl, IHostPlayer
{
    /*
     * Fix issue with key events / focus (in combination of those we have the issue with DoubleTap for FullScreen)
     * Note: On PointerReleased we can't set the focus element as it will change it after that (that's why a dispatcher is required... for delay)
     *
     *  1) https://github.com/microsoft/microsoft-ui-xaml/issues/7330
     *  2) https://github.com/microsoft/microsoft-ui-xaml/issues/3986
     *  3) https://github.com/microsoft/microsoft-ui-xaml/issues/6179
     *
     */

    public SwapChainPanel           SCP         { get; set; } = null!;
    public KeyboardFocusControl?    KFC         { get; set; } = null!;

    public FullScreenContainer?     FSC
    {
        get { return (FullScreenContainer)GetValue(FSCProperty); }
        set { SetValue(FSCProperty, value); }
    }
    public static readonly DependencyProperty FSCProperty =
        DependencyProperty.Register(nameof(FSC), typeof(FullScreenContainer), typeof(FlyleafHost), new PropertyMetadata(null));

    public Player? Player
    {
        get { return (Player?)GetValue(PlayerProperty); }
        set { SetValue(PlayerProperty, value); }
    }
    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(nameof(Player), typeof(Player), typeof(FlyleafHost), new PropertyMetadata(null, OnPlayerChanged));
    private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = d as FlyleafHost;

        if (host == null || host.SCP == null || host.Player == null)
            return;

        host.PlayerChanged((Player)e.OldValue);
    }

    public bool KeyBindings
    {
        get { return (bool)GetValue(KeyBindingsProperty); }
        set { SetValue(KeyBindingsProperty, value); }
    }
    public static readonly DependencyProperty KeyBindingsProperty =
        DependencyProperty.Register(nameof(KeyBindings), typeof(bool), typeof(FlyleafHost), new PropertyMetadata(true));

    public int          UniqueId        { get; private set; }

    static int idGenerator = 1;
    readonly LogHandler Log;

    public FlyleafHost()
    {
        DefaultStyleKey = typeof(FlyleafHost);
        UniqueId = idGenerator++;
        Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost NP] ");
    }

    protected override void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
    {
        if (FSC != null)
            FSC.IsFullScreen = !FSC.IsFullScreen;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        FrameworkElement parent = this;
        while (parent.Parent is FrameworkElement)
        {
            parent = (FrameworkElement)parent.Parent;

            if (parent is FullScreenContainer)
            {
                FSC = (FullScreenContainer)parent;
                break;
            }
        }

        if (GetTemplateChild(nameof(KFC)) is KeyboardFocusControl kfc && KeyBindings)
        {
            KFC = kfc;
            KFC.KeyDown += KFC_KeyDown;
            KFC.KeyUp += KFC_KeyUp;
            KFC.Focus(FocusState.Keyboard);
        }

        if (GetTemplateChild(nameof(SCP)) is SwapChainPanel scp)
        {
            SCP = scp;
            SCP.SizeChanged += SCP_SizeChanged;

            if (KeyBindings) // Currently used only for keys which causes issues with KFC focus
            {
                SCP.PointerPressed += Scp_PointerPressed;
                SCP.PointerReleased += SCP_PointerReleased;
            }

            //SCP.DoubleTapped += SCP_DoubleTapped; // Disabled causes issues
        }
        else
            throw new Exception("SCP not found");


        if (Player != null)
            PlayerChanged(null);
    }

    private void KFC_KeyDown(object sender, KeyRoutedEventArgs e)
        { if (KeyBindings) Player.KeyDown(Player, System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)e.Key)); }
    private void KFC_KeyUp(object sender, KeyRoutedEventArgs e)
        { if (KeyBindings) Player.KeyUp(Player, System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)e.Key)); }

    private void SCP_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    { // not working?
        if (FSC != null)
            FSC.IsFullScreen = !FSC.IsFullScreen;
    }

    private void Scp_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint((UIElement)sender).Properties;
        if (properties.IsLeftButtonPressed)
            ((UIElement)sender).CapturePointer(e.Pointer);
    }

    private void SCP_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ((UIElement)sender).ReleasePointerCaptures();
        DispatcherQueue.TryEnqueue(() => KFC!.Focus(FocusState.Programmatic));
    }
    private void SCP_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Player?.Renderer.SwapChain.Resize((int)e.NewSize.Width, (int)e.NewSize.Height);
    }

    private void PlayerChanged(Player? oldPlayer)
    {
        if (oldPlayer != null)
        {
            Log.Debug($"De-assign Player #{oldPlayer.PlayerId}");
            oldPlayer.Renderer?.SwapChain.Dispose(rendererFrame: false);
            oldPlayer.Host = null;
        }

        if (Player == null)
            return;

        Log.Prefix = ("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost #{Player.PlayerId}] ";

        // De-assign new Player's Handle/FlyleafHost
        Player.Host?.Player_Disposed();

        if (Player == null) // We might just de-assign our Player
            return;

        // Assign new Player's (Handle/FlyleafHost)
        Log.Debug($"Assign Player #{Player.PlayerId}");
        Player.Host = this;
        Background = new SolidColorBrush(new() { A = Player.Config.Video.BackColor.A, R = Player.Config.Video.BackColor.R, G = Player.Config.Video.BackColor.G, B = Player.Config.Video.BackColor.B });
        Player.Renderer.SwapChain.SetupWinUI(SwapChainClbk);
    }

    private void SwapChainClbk(IDXGISwapChain2 swapChain)
    {
        using (var nativeObject = SharpGen.Runtime.ComObject.As<Vortice.WinUI.ISwapChainPanelNative2>(SCP))
            nativeObject.SetSwapChain(swapChain);

        if (swapChain != null)
            Player?.Renderer.SwapChain.Resize((int)ActualWidth, (int)ActualHeight);
    }
    public bool Player_CanHideCursor() => Window.Current == FullScreenContainer.FSW;
    public bool Player_GetFullScreen() => FSC != null && FSC.IsFullScreen;
    public void Player_SetFullScreen(bool value) { if (FSC != null) FSC.IsFullScreen = value; }
    public void Player_Disposed() => Utils.UIInvokeIfRequired(() => Player = null);
    public void Player_RatioChanged(double keepRatio) { }
    public bool Player_HandlesRatioResize(int width, int height) { return false; }
}

public class KeyboardFocusControl : Control { }
