using FlyleafLib.MediaPlayer;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vortice.Direct3D11;
using Vortice.Wpf;

namespace FlyleafLib.Controls.WPF;

/// <summary>
/// A WPF FrameworkElement that renders a Flyleaf <see cref="Player"/> into the
/// WPF visual tree via a <see cref="DrawingSurface"/>, avoiding the Win32
/// airspace limitation of <see cref="FlyleafLib.Controls.WPF.FlyleafHost"/>.
///
/// The player renders on its own (render) adapter; each frame is delivered into
/// the DrawingSurface's ColorTexture by <see cref="FlyleafFrameBridge"/>, which
/// handles the case where the render GPU differs from the adapter WPF composites
/// on (forced discrete card / headless GPU / Optimus).
/// </summary>
public class FlyleafView : Decorator, IHostPlayer, IDisposable
{
    private static readonly Type flType = typeof(FlyleafView);
    private static readonly Type playerType = typeof(Player);

    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(nameof(Player), playerType, flType, new(null, OnPlayerChanged));

    public static readonly DependencyProperty ReplicaPlayerProperty =
        DependencyProperty.Register(nameof(ReplicaPlayer), typeof(Player), flType, new PropertyMetadata(null, OnReplicaPlayerChanged));

    public static readonly DependencyProperty HostDataContextProperty =
        DependencyProperty.Register(nameof(HostDataContext), typeof(object), flType, new(null));

    private readonly DrawingSurface surface;
    private FlyleafFrameBridge bridge;
    private ID3D11Device1 compositorDevice;
    private int lastTextureWidth;
    private int lastTextureHeight;
    private bool isFullScreen;

    public Player Player
    {
        get => (Player)GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public Player ReplicaPlayer
    {
        get => (Player)GetValue(ReplicaPlayerProperty);
        set => SetValue(ReplicaPlayerProperty, value);
    }

    public object HostDataContext
    {
        get => GetValue(HostDataContextProperty);
        set => SetValue(HostDataContextProperty, value);
    }

    public double DpiX { get; private set; } = 1;
    public double DpiY { get; private set; } = 1;

    public FlyleafView()
    {
        surface = new DrawingSurface { AlwaysRefresh = true };
        surface.LoadContent += OnSurfaceLoad;
        surface.UnloadContent += OnSurfaceUnload;
        surface.Draw += OnSurfaceDraw;

        AddVisualChild(surface);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        MouseWheel += OnMouseWheel;
    }

    // Composite the background video surface (index 0) behind the overlay Child.
    protected override int VisualChildrenCount => 1 + base.VisualChildrenCount;

    protected override Visual GetVisualChild(int index)
        => index == 0 ? surface : base.GetVisualChild(index - 1);

    protected override Size MeasureOverride(Size constraint)
    {
        surface.Measure(constraint);
        var childSize = base.MeasureOverride(constraint);
        return new Size(
            double.IsInfinity(constraint.Width) ? childSize.Width : constraint.Width,
            double.IsInfinity(constraint.Height) ? childSize.Height : constraint.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        surface.Arrange(new Rect(finalSize));
        base.ArrangeOverride(finalSize);
        return finalSize;
    }

    public bool Player_CanHideCursor() => IsMouseOver;

    public bool Player_GetFullScreen() => isFullScreen;

    public void Player_SetFullScreen(bool value)
    {
        isFullScreen = value;

        var window = Window.GetWindow(this);
        if (window == null)
            return;

        if (value)
        {
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowState = WindowState.Maximized;
            return;
        }

        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.ResizeMode = ResizeMode.CanResize;
        window.WindowState = WindowState.Normal;
    }

    public void Player_RatioChanged(double keepRatio)
    {
        // WPF layout handles sizing; no explicit resize needed.
    }

    public bool Player_HandlesRatioResize(int width, int height) => false;

    public void Player_Disposed()
        => Dispatcher.BeginInvoke(() => Player = null);

    public void SetReplicaPlayer(Player oldPlayer)
    {
        // temporary placeholder
    }

    public void Dispose()
    {
        DisposeBridge();

        if (Player == null)
            return;

        var currentPlayer = Player;
        Player = null;
        currentPlayer.Host = null;
    }

    private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((FlyleafView)d).SetPlayer((Player)e.OldValue);

    private static void OnReplicaPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((FlyleafView)d).SetReplicaPlayer((Player)e.OldValue);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateDpi();
        EnsureBridge();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => DisposeBridge();

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) || Player == null)
            return;

        var relativeMousePosition = e.GetPosition(this);
        Point currentDpiPoint = new(relativeMousePosition.X * DpiX, relativeMousePosition.Y * DpiY);

        if (e.Delta > 0)
            Player.Config.Video.ZoomIn(currentDpiPoint);
        else
            Player.Config.Video.ZoomOut(currentDpiPoint);
    }

    private void OnSurfaceLoad(object sender, DrawingSurfaceEventArgs e)
    {
        compositorDevice = e.Device;
        DebugLogger.Print("[FLV] Surface LoadContent");
        EnsureBridge();
    }

    private void OnSurfaceUnload(object sender, DrawingSurfaceEventArgs e)
    {
        DebugLogger.Print("[FLV] Surface UnloadContent");
        DisposeBridge();
        compositorDevice = null;
    }

    private void OnSurfaceDraw(object sender, DrawEventArgs args)
    {
        if (bridge == null)
            return;

        var colorTexture = args.Surface.ColorTexture;
        if (colorTexture != null)
        {
            var desc = colorTexture.Description;
            int w = (int)desc.Width;
            int h = (int)desc.Height;
            if (w != lastTextureWidth || h != lastTextureHeight)
            {
                lastTextureWidth = w;
                lastTextureHeight = h;
                bridge.Resize(w, h);
            }
        }

        bridge.CopyLatestFrameTo(args);
        args.InvalidateSurface();
    }

    private void SetPlayer(Player oldPlayer)
    {
        if (oldPlayer != null)
        {
            DisposeBridge();
            oldPlayer.Host = null;
        }

        if (Player == null)
            return;

        Player.Host?.Player_Disposed();
        if (Player == null)
            return;

        Player.Host = this;
        EnsureBridge();
    }

    private void EnsureBridge()
    {
        if (bridge != null || Player?.Renderer == null || compositorDevice == null || !IsLoaded)
            return;

        UpdateDpi();

        var size = GetControlPixelSize();
        bridge = new FlyleafFrameBridge();
        bridge.Initialize(Player, size.Width, size.Height);
        bridge.SetCompositorDevice(compositorDevice);
        lastTextureWidth = 0;
        lastTextureHeight = 0;

        DebugLogger.Print($"[FLV] Bridge ready control={size.Width}x{size.Height}");
    }

    private void DisposeBridge()
    {
        bridge?.Dispose();
        bridge = null;
        lastTextureWidth = 0;
        lastTextureHeight = 0;
    }

    private void UpdateDpi()
    {
        var window = Window.GetWindow(this);
        var source = PresentationSource.FromVisual(window);
        if (source == null)
            return;

        DpiX = source.CompositionTarget?.TransformToDevice.M11 ?? 1;
        DpiY = source.CompositionTarget?.TransformToDevice.M22 ?? 1;
    }

    private (int Width, int Height) GetControlPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return (
            Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)));
    }
}
