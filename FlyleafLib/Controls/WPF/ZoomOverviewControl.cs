using FlyleafLib.Controls.WPF.Present;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Zoom;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FlyleafLib.Controls.WPF;

/// <summary>
/// ZoomOverviewControl - WPF control for the zoom minimap.
///
/// The minimap is rendered on the player's render device (<see cref="ZoomFrameProvider"/>)
/// and presented through a <see cref="HybridVideoPresenter"/>, so it renders across
/// adapters and over RDP/headless, matching FlyleafView.
///
///   <zoom:ZoomOverviewControl x:Name="Minimap" .../>
///   Minimap.BindPlayer(player);  // or set Player
/// </summary>
public sealed class ZoomOverviewControl : FrameworkElement, IDisposable
{
    private static readonly Type playerType = typeof(Player);
    private static readonly Type controlType = typeof(ZoomOverviewControl);

    public static readonly DependencyProperty ShowWhenZoomOutProperty =
        DependencyProperty.Register(nameof(ShowWhenZoomOut), typeof(bool), controlType, new PropertyMetadata(false));

    public static readonly DependencyProperty ShowZoomBoxProperty =
        DependencyProperty.Register(nameof(ShowZoomBox), typeof(bool), controlType, new PropertyMetadata(true, OnShowZoomBoxChanged));

    private static readonly DependencyPropertyKey SideXPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SideX), typeof(int), controlType, new FrameworkPropertyMetadata(0));
    public static readonly DependencyProperty SideXProperty = SideXPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SideYPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SideY), typeof(int), controlType, new FrameworkPropertyMetadata(0));
    public static readonly DependencyProperty SideYProperty = SideYPropertyKey.DependencyProperty;

    public static readonly DependencyPropertyKey VideoWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(VideoWidth), typeof(int), controlType, new FrameworkPropertyMetadata(0));
    public static readonly DependencyProperty VideoWidthProperty = VideoWidthPropertyKey.DependencyProperty;

    public static readonly DependencyPropertyKey VideoHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(VideoHeight), typeof(int), controlType, new FrameworkPropertyMetadata(0));
    public static readonly DependencyProperty VideoHeightProperty = VideoHeightPropertyKey.DependencyProperty;

    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(nameof(Player), playerType, controlType, new(null, OnPlayerChanged));

    public Player Player { get => (Player)GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public bool ShowWhenZoomOut { get => (bool)GetValue(ShowWhenZoomOutProperty); set => SetValue(ShowWhenZoomOutProperty, value); }
    public bool ShowZoomBox { get => (bool)GetValue(ShowZoomBoxProperty); set => SetValue(ShowZoomBoxProperty, value); }

    public int VideoWidth => (int)GetValue(VideoWidthProperty);
    public int VideoHeight => (int)GetValue(VideoHeightProperty);
    public int SideX => (int)GetValue(SideXProperty);
    public int SideY => (int)GetValue(SideYProperty);

    internal LogHandler Log;
    private HybridVideoPresenter _presenter;
    private ZoomFrameProvider _provider;
    private Player _player;
    private bool _initialized;
    private bool _disposed;
    private bool _isDragging;
    private readonly int _uniqueId;

    public ZoomOverviewControl()
    {
        _uniqueId = GetUniqueId();
        Log = new(("[#" + _uniqueId + "]").PadRight(8, ' ') + " [ZOVC           ] ");

        ClipToBounds = true;

        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseMove += OnMouseMove;
    }

    /// <summary>Connects the control to a FlyleafLib player. UI thread.</summary>
    public void BindPlayer(Player player)
    {
        if (_disposed)
            return;

        UnbindPlayer();

        _player = player ?? throw new ArgumentNullException(nameof(player));

        var (w, h) = GetPixelSize();
        _provider = new ZoomFrameProvider();
        _provider.Initialize(_player, w, h);
        _provider.Renderer.ShowZoomBox = ShowZoomBox;
        _provider.Renderer.VideoViewSizeChanged = RecalcVideoSize;

        _presenter = new HybridVideoPresenter(HybridVideoPresenter.DefaultMode);
        AddVisualChild(_presenter);
        InvalidateMeasure();
        _presenter.Attach(_provider);

        _player.Config.Video.PropertyChanged += ZoomOverviewPropertyChanged;
        _initialized = true;

        UpdateVisibility();
    }

    /// <summary>Disconnects the control from the player. UI thread.</summary>
    public void UnbindPlayer()
    {
        if (_player != null)
            _player.Config.Video.PropertyChanged -= ZoomOverviewPropertyChanged;

        _initialized = false;

        if (_presenter != null)
        {
            RemoveVisualChild(_presenter);
            _presenter.Dispose(); // disposes provider -> renderer -> frame source
            _presenter = null;
        }
        _provider = null;
        _player = null;

        UpdateVisibility();
    }

    private void SetPlayer(Player oldPlayer)
    {
        if (oldPlayer != null)
            UnbindPlayer();

        if (Player == null)
            return;

        BindPlayer(Player);
    }

    private void ZoomOverviewPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_player is null || !_initialized || _disposed)
            return;

        if (e.PropertyName is nameof(_player.Config.Video.Zoom)
                           or nameof(_player.Config.Video.PanXOffset)
                           or nameof(_player.Config.Video.PanYOffset))
        {
            UpdateVisibility();
        }
    }

    private void UpdateVisibility()
    {
        if (_player == null)
        {
            if (ShowWhenZoomOut)
                Visibility = Visibility.Collapsed;
            return;
        }
        if (!ShowWhenZoomOut)
            return;

        Visibility = _player.Config.Video.Zoom > 100 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnShowZoomBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (ZoomOverviewControl)d;
        if (ctrl._provider?.Renderer is { } renderer && ctrl._initialized)
            renderer.ShowZoomBox = (bool)e.NewValue;
    }

    private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ZoomOverviewControl)d).SetPlayer((Player)e.OldValue);

    // Click-to-pan
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_player == null)
            return;
        _isDragging = true;
        CaptureMouse();
        PanToPosition(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _player == null)
            return;
        PanToPosition(e.GetPosition(this));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }

    private void PanToPosition(Point pos)
    {
        double u = Math.Clamp(pos.X / ActualWidth, 0, 1);
        double v = Math.Clamp(pos.Y / ActualHeight, 0, 1);
        double panX = (u - 0.5) * 2.0;
        double panY = (v - 0.5) * 2.0;

        _player.Config.Video.PanXOffset = -panX;
        _player.Config.Video.PanYOffset = -panY;
    }

    private void RecalcVideoSize()
    {
        var renderer = _provider?.Renderer;
        if (renderer is null || !renderer.IsInitialized)
            return;

        try
        {
            SetValue(SideXPropertyKey, renderer.SideXPixels);
            SetValue(SideYPropertyKey, renderer.SideYPixels);
            SetValue(VideoWidthPropertyKey, (int)renderer.Viewport.Width);
            SetValue(VideoHeightPropertyKey, (int)renderer.Viewport.Height);
        }
        catch { }
    }

    private (int Width, int Height) GetPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return (
            Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)));
    }

    // Visual tree
    protected override int VisualChildrenCount => _presenter != null ? 1 : 0;
    protected override Visual GetVisualChild(int index) => _presenter;

    protected override Size MeasureOverride(Size availableSize)
    {
        _presenter?.Measure(availableSize);
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _presenter?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        MouseLeftButtonDown -= OnMouseDown;
        MouseLeftButtonUp -= OnMouseUp;
        MouseMove -= OnMouseMove;

        UnbindPlayer();
    }
}
