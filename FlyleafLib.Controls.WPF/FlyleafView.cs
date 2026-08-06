using FlyleafLib.MediaPlayer;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlyleafLib.Controls.WPF;

/// <summary>
/// A WPF FrameworkElement that renders a Flyleaf <see cref="Player"/> into the
/// WPF visual tree, avoiding the Win32 airspace limitation of
/// <see cref="FlyleafLib.Controls.WPF.FlyleafHost"/>.
///
/// The player decodes/renders on its own (render) adapter; each frame is read
/// back to the CPU by <see cref="FlyleafFrameBridge"/> and presented through a
/// <see cref="WriteableBitmap"/>. This software present path is adapter-agnostic
/// and works where a D3D front buffer is unavailable (RDP, headless, locked
/// sessions), at the cost of a per-frame CPU copy.
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

    private readonly Image image;
    private WriteableBitmap bitmap;
    private int bmpWidth;
    private int bmpHeight;
    private FlyleafFrameBridge bridge;
    private bool renderingHooked;
    private int presentCount;
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
        image = new Image { Stretch = Stretch.Fill };

        // The video is a background visual; the Decorator's Child stays free for
        // caller-provided overlay content drawn on top of the video.
        AddVisualChild(image);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        MouseWheel += OnMouseWheel;
    }

    // Composite the background video (index 0) behind the overlay Child.
    protected override int VisualChildrenCount => 1 + base.VisualChildrenCount;

    protected override Visual GetVisualChild(int index)
        => index == 0 ? image : base.GetVisualChild(index - 1);

    protected override Size MeasureOverride(Size constraint)
    {
        image.Measure(constraint);
        var childSize = base.MeasureOverride(constraint);
        return new Size(
            double.IsInfinity(constraint.Width) ? childSize.Width : constraint.Width,
            double.IsInfinity(constraint.Height) ? childSize.Height : constraint.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        image.Arrange(new Rect(finalSize));
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

    // Present pump on the WPF render loop; only touches the bitmap on new frames.
    private void OnRendering(object sender, EventArgs e)
    {
        if (bridge == null)
            return;

        var size = GetControlPixelSize();
        if (size.Width != bmpWidth || size.Height != bmpHeight)
        {
            bridge.Resize(size.Width, size.Height);
            EnsureBitmap(size.Width, size.Height);
        }

        if (!bridge.HasPendingFrame || bitmap == null)
            return;

        bitmap.Lock();
        try
        {
            if (bridge.CopyLatestFrameInto(bitmap.BackBuffer, bitmap.BackBufferStride, bmpWidth, bmpHeight))
            {
                bitmap.AddDirtyRect(new Int32Rect(0, 0, bmpWidth, bmpHeight));
                int n = ++presentCount;
                if (n <= 3 || n % 120 == 0)
                    DebugLogger.Print($"[FLV] present #{n} {bmpWidth}x{bmpHeight}");
            }
        }
        finally
        {
            bitmap.Unlock();
        }
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
        if (bridge != null || Player?.Renderer == null || !IsLoaded)
            return;

        UpdateDpi();

        var size = GetControlPixelSize();
        bridge = new FlyleafFrameBridge();
        bridge.Initialize(Player, size.Width, size.Height);
        EnsureBitmap(size.Width, size.Height);

        if (!renderingHooked)
        {
            CompositionTarget.Rendering += OnRendering;
            renderingHooked = true;
        }

        DebugLogger.Print($"[FLV] Bridge ready control={size.Width}x{size.Height}");
    }

    private void DisposeBridge()
    {
        if (renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            renderingHooked = false;
        }

        bridge?.Dispose();
        bridge = null;
        bitmap = null;
        image.Source = null;
        bmpWidth = 0;
        bmpHeight = 0;
    }

    private void EnsureBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (bitmap != null && bmpWidth == width && bmpHeight == height)
            return;

        bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        image.Source = bitmap;
        bmpWidth = width;
        bmpHeight = height;
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
