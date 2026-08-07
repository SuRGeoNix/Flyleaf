using FlyleafLib.Controls.WPF.Present;
using FlyleafLib.MediaPlayer;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FlyleafLib.Controls.WPF;

/// <summary>
/// A WPF FrameworkElement that renders a Flyleaf <see cref="Player"/> into the
/// WPF visual tree, avoiding the Win32 airspace limitation of
/// <see cref="FlyleafLib.Controls.WPF.FlyleafHost"/>.
///
/// Presentation is delegated to a <see cref="HybridVideoPresenter"/>, which selects
/// (once, at load) a GPU DrawingSurface path or a software WriteableBitmap path
/// per <see cref="PresentMode"/>. The video is a background visual; the Decorator's
/// <see cref="Decorator.Child"/> stays free for caller-provided overlay content.
/// </summary>
public class FlyleafView : Decorator, IHostPlayer, IDisposable
{
    private static readonly Type FlType = typeof(FlyleafView);
    private static readonly Type PlayerType = typeof(Player);

    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(nameof(Player), PlayerType, FlType, new(null, OnPlayerChanged));

    public static readonly DependencyProperty ReplicaPlayerProperty =
        DependencyProperty.Register(nameof(ReplicaPlayer), typeof(Player), FlType, new PropertyMetadata(null, OnReplicaPlayerChanged));

    public static readonly DependencyProperty HostDataContextProperty =
        DependencyProperty.Register(nameof(HostDataContext), typeof(object), FlType, new(null));

    private HybridVideoPresenter _presenter;
    private bool _isFullScreen;

    public FlyleafView()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        MouseWheel += OnMouseWheel;
    }

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

    // Composite the background video presenter (index 0) behind the overlay Child.
    protected override int VisualChildrenCount => (_presenter != null ? 1 : 0) + base.VisualChildrenCount;

    protected override Visual GetVisualChild(int index)
    {
        if (_presenter == null)
            return base.GetVisualChild(index);
        return index == 0 ? _presenter : base.GetVisualChild(index - 1);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _presenter?.Measure(constraint);
        var childSize = base.MeasureOverride(constraint);
        return new Size(
            double.IsInfinity(constraint.Width) ? childSize.Width : constraint.Width,
            double.IsInfinity(constraint.Height) ? childSize.Height : constraint.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _presenter?.Arrange(new Rect(finalSize));
        base.ArrangeOverride(finalSize);
        return finalSize;
    }

    public bool Player_CanHideCursor() => IsMouseOver;

    public bool Player_GetFullScreen() => _isFullScreen;

    public void Player_SetFullScreen(bool value)
    {
        _isFullScreen = value;

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

    private VideoPresentMode ResolveMode() => Engine.Config.VideoPresentMode;

    private void EnsureBridge()
    {
        if (_presenter != null || Player?.Renderer == null || !IsLoaded)
            return;

        UpdateDpi();

        var size = GetControlPixelSize();
        var bridge = new FlyleafFrameBridge();
        bridge.Initialize(Player, size.Width, size.Height);

        _presenter = new HybridVideoPresenter(ResolveMode());
        AddVisualChild(_presenter);
        InvalidateMeasure();
        _presenter.Attach(bridge);

        DebugLogger.Print($"[FLV] Presenter ready control={size.Width}x{size.Height} mode={ResolveMode()}");
    }

    private void DisposeBridge()
    {
        if (_presenter == null)
            return;

        RemoveVisualChild(_presenter);
        _presenter.Dispose();
        _presenter = null;
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
