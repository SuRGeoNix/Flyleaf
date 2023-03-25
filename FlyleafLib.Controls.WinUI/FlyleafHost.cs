using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using Vortice.DXGI;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls.WinUI;

[TemplatePart(Name = nameof(KFC), Type = typeof(KeyboardFocusControl))]
[TemplatePart(Name = nameof(SCP), Type = typeof(SwapChainPanel))]
public sealed class FlyleafHost : ContentControl, IHostPlayer
{
    public SwapChainPanel           SCP         { get; set; } = null!;
    public KeyboardFocusControl     KFC         { get; set; } = null!;

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

    public int          UniqueId        { get; private set; }

    static int idGenerator = 1;
    readonly LogHandler Log;

    public FlyleafHost()
    {
        DefaultStyleKey = typeof(FlyleafHost);
        UniqueId = idGenerator++;
        Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + $" [FlyleafHost NP] ");
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

        if (GetTemplateChild(nameof(KFC)) is KeyboardFocusControl kfc)
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
            SCP.PointerPressed += Scp_PointerPressed;
            SCP.PointerReleased += SCP_PointerReleased;
            SCP.DoubleTapped += SCP_DoubleTapped;
            if (Player != null)
                PlayerChanged(null);
        }
    }

    private void KFC_KeyDown(object sender, KeyRoutedEventArgs e)
        => Player.KeyDown(Player, System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)e.Key));
    private void KFC_KeyUp(object sender, KeyRoutedEventArgs e)
        => Player.KeyUp(Player, System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)e.Key));

    private void SCP_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FSC == null)
            return;

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
        DispatcherQueue.TryEnqueue(() => KFC.Focus(FocusState.Keyboard));
    }
    private void SCP_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Player == null)
            return;

        Player.renderer.ResizeBuffers((int) (e.NewSize.Width * Utils.NativeMethods.DpiX), (int) (e.NewSize.Height * Utils.NativeMethods.DpiY));
    }

    private void PlayerChanged(Player? oldPlayer)
    {
        if (oldPlayer != null)
        {
            Log.Debug($"De-assign Player #{oldPlayer.PlayerId}");
            oldPlayer.VideoDecoder.DestroySwapChain();
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
        Background = new SolidColorBrush(new() { A = Player.Config.Video.BackgroundColor.A, R = Player.Config.Video.BackgroundColor.R, G = Player.Config.Video.BackgroundColor.G, B = Player.Config.Video.BackgroundColor.B });
        Player.VideoDecoder.CreateSwapChain(SwapChainClbk);
    }

    private void SwapChainClbk(IDXGISwapChain2 swapChain)
    {
        using (var nativeObject = SharpGen.Runtime.ComObject.As<Vortice.WinUI.ISwapChainPanelNative2>(SCP))
            nativeObject.SetSwapChain(swapChain);

        if (Player != null)
            Player.renderer.ResizeBuffers((int) (ActualWidth * Utils.NativeMethods.DpiX), (int) (ActualHeight * Utils.NativeMethods.DpiY));
    }
    public bool Player_CanHideCursor() => Window.Current == FullScreenContainer.FSW;
    public bool Player_GetFullScreen() => FSC != null && FSC.IsFullScreen;
    public void Player_SetFullScreen(bool value) { if (FSC != null) FSC.IsFullScreen = value; }
    public void Player_Disposed() => Utils.UIInvokeIfRequired(() => Player = null);

}

public class KeyboardFocusControl : Control { }
