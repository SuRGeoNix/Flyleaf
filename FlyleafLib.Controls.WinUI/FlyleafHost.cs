using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls.WinUI;

[TemplatePart(Name = nameof(KFC), Type = typeof(KeyboardFocusControl))]
[TemplatePart(Name = nameof(SCP), Type = typeof(SwapChainPanel))]
public sealed class FlyleafHost : Control
{
    private SwapChainPanel          SCP         { get; set; } = null!;
    private KeyboardFocusControl    KFC         { get; set; } = null!;

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

        host.PlayerChanged((Player)e.OldValue, (Player)e.NewValue);
    }

    public FlyleafHost()
    {
        DefaultStyleKey = typeof(FlyleafHost);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

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
            SCP.PointerReleased += SCP_PointerReleased;
            if (Player != null)
                PlayerChanged(null, Player);
        }
    }

    private void SCP_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => KFC.Focus(FocusState.Keyboard));
    }

    private void KFC_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        Player.KeyDown(Player, System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)e.Key));
    }

    private void KFC_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        Player.KeyUp(Player, System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)e.Key));
    }

    private void PlayerChanged(Player? oldPlayer, Player? newPlayer)
    {
        if (newPlayer == null)
            return;

        using (var nativeObject = SharpGen.Runtime.ComObject.As<Vortice.WinUI.ISwapChainPanelNative2>(SCP))
            nativeObject.SetSwapChain(newPlayer.renderer.InitializeWinUISwapChain());
    }

    private void SCP_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Player == null)
            return;

        Player.renderer.ResizeBuffers((int)e.NewSize.Width, (int)e.NewSize.Height); // TODO DPI
    }
}

class KeyboardFocusControl : Control { }
