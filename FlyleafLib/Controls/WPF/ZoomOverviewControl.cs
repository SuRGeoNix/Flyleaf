using FlyleafLib.MediaPlayer;
using FlyleafLib.Zoom;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vortice.Direct3D11;
using Vortice.Wpf;

namespace FlyleafLib.Controls.WPF;

/// <summary>
///ZoomOverviewControl - WPF control for the zoom minimap..
///
/// XAML-Verwendung:
///   <zoom:ZoomOverviewControl x:Name="Minimap"
///       HorizontalAlignment="Right" VerticalAlignment="Bottom"
///       Margin="0,0,16,16" Panel.ZIndex="10" />
///
///   // Code-behind nach Player-Open:
///   Minimap.BindPlayer(player);
/// </summary>
public sealed class ZoomOverviewControl : FrameworkElement, IDisposable
{
    private static readonly Type playerType = typeof(Player);
    private static readonly Type controlType = typeof(ZoomOverviewControl);

    // Dependency Properties
    public static readonly DependencyProperty ShowWhenZoomOutProperty =
			DependencyProperty.Register(nameof(ShowWhenZoomOut), typeof(bool),
				typeof(ZoomOverviewControl), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowZoomBoxProperty =
            DependencyProperty.Register(nameof(ShowZoomBox), typeof(bool),
                typeof(ZoomOverviewControl), new PropertyMetadata(true,
                    OnShowZoomBoxChanged));

    
    private static readonly DependencyPropertyKey SideXPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SideX), typeof(int),
            controlType, new FrameworkPropertyMetadata(0));

    public static readonly DependencyProperty SideXProperty =
        SideXPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SideYPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SideY), typeof(int),
            controlType, new FrameworkPropertyMetadata(0));

    public static readonly DependencyProperty SideYProperty =
        SideYPropertyKey.DependencyProperty;

    public static readonly DependencyPropertyKey VideoWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(VideoWidth), typeof(int),
            controlType, new FrameworkPropertyMetadata(0));

    public static readonly DependencyProperty VideoWidthProperty = VideoWidthPropertyKey.DependencyProperty;

    public static readonly DependencyPropertyKey VideoHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(VideoHeight), typeof(int),
            controlType, new FrameworkPropertyMetadata(0));
    public static readonly DependencyProperty VideoHeightProperty = VideoHeightPropertyKey.DependencyProperty;

    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(nameof(Player), playerType, controlType, new(null, OnPlayerChanged));

    public Player Player { get => (Player)GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public bool ShowWhenZoomOut { get => (bool)GetValue(ShowWhenZoomOutProperty); set => SetValue(ShowWhenZoomOutProperty, value); }
    public bool ShowZoomBox { get => (bool)GetValue(ShowZoomBoxProperty); set => SetValue(ShowZoomBoxProperty, value);  }

    private ID3D11Device1?        _device;
    private ID3D11DeviceContext1? _deviceContext;
    internal LogHandler Log;
    private DrawingSurface        _surface;           
	private ZoomOverviewRenderer  _renderer;
	private Player                _player;
	private bool                  _initialized;
    private bool                  _surface_initialized;  
	private bool                  _disposed;
	private bool                  _needSurfaceCleaning;
    private int                   _uniqueId;  
	// Click-to-pan drag state
	private bool  _isDragging;

	public ZoomOverviewControl()
	{
        _uniqueId =  GetUniqueId();
        Log = new(("[#" + _uniqueId + "]").PadRight(8, ' ') + " [ZOVC           ] ");
        
        InitSurface();

        SizeChanged += OnSizeChanged;
        
        // Clip to bounds (rounded corners done via a clip geometry)
        ClipToBounds = true;
    }

    public int VideoWidth => (int)GetValue(VideoWidthProperty); 
    public int VideoHeight => (int)GetValue(VideoHeightProperty);
    public int SideX => (int)GetValue(SideXProperty);    
    public int SideY => (int)GetValue(SideYProperty); 
  
    /// <summary>
    /// Connects the control to a FlyleafLib player.
    /// Must be called on the UI thread.
    /// </summary>
    public void BindPlayer(Player player)
	{
		if (!_initialized || _disposed || !IsSurfaceInitialized) 
			return;
		_player = player ?? throw new ArgumentNullException(nameof(player));

        _renderer = new ZoomOverviewRenderer(player, (int)ActualWidth, (int)ActualHeight);        
		_renderer.InitializeD3Resource(_device, _deviceContext);
        _renderer.ShowZoomBox = ShowZoomBox;
        _renderer.VideoViewSizeChanged = RecalcVideoSize;

        _player.Config.Video.PropertyChanged += ZoomOverviewPropertyChanged;

		UpdateVisibility();
	}

    /// <summary>
    /// Disconnects the control from the FlyleafLib player.
    /// Must be called on the UI thread.
    /// </summary>
    public void UnbindPlayer()
	{
		if (!_initialized || _disposed || !IsSurfaceInitialized || _player is null)
			return;
        
        Log.Debug($"Unbind player #{_player?.PlayerId} from zoom overview control #{_uniqueId}");
        _initialized = false;

        if (_player is not null)
			_player.Config.Video.PropertyChanged -= ZoomOverviewPropertyChanged;

        _player = null;

        _renderer?.Dispose();
		_renderer = null;

        UpdateVisibility();
        _needSurfaceCleaning = true;
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
            RequestRender();
		}
	}

    private void SetPlayer(Player oldPlayer)
    {
        if (!IsSurfaceInitialized) return;

        Log.Debug($"SetPlayer( old player #{oldPlayer?.PlayerId}), new player #{Player?.PlayerId}, zoc #{_uniqueId}");
        if (oldPlayer != null)
            UnbindPlayer();

        if (Player == null) return;

        BindPlayer(Player);       
    }

    private void InitSurface()
    {   
        _surface = new DrawingSurface();

        _surface.Draw += OnDrawingSurfaceRender;
        _surface.LoadContent += OnSurfaceContentLoad;
        _surface.UnloadContent += OnSurfaceContentUnload;

        AddVisualChild(_surface);

        // Mouse interaction
        _surface.MouseLeftButtonDown += OnMouseDown;
        _surface.MouseLeftButtonUp += OnMouseUp;
        _surface.MouseMove += OnMouseMove;        
    }

    //  DrawingSurface.Draw Callback
    /// <summary>
    /// Called by DrawingSurface when a new frame is needed.    
    /// </summary>
    private void OnDrawingSurfaceRender(object sender, DrawEventArgs args)
	{
		if (_needSurfaceCleaning && !_disposed)
			ClearSurface(args);
		if (!_initialized || _disposed || _renderer == null)
			return;
        
        _renderer?.RenderIntoTexture(args.Surface.ColorTexture, args);

        args.InvalidateSurface();
	}

	private void ClearSurface(DrawEventArgs args)
	{
		args.Context.OMSetRenderTargets(args.Surface.ColorTextureView);
		args.Context.ClearRenderTargetView(args.Surface.ColorTextureView, new Vortice.Mathematics.Color4(0f, 0f, 0f, 1f));
		args.Context.Draw(3, 0);
		_needSurfaceCleaning = false;
	}

    // Trigger Helper
    private void OnCompositionRendering(object sender, EventArgs e)
	{
		if (!_initialized || _disposed || !IsSurfaceInitialized)
			return;
		RequestRender();
	}

    /// <summary>Instructs DrawingSurface to re-execute OnRender.</summary>
    private void RequestRender()
	{
        // DrawingSurface.Invalidate() is thread-safe and triggers
        // another OnRender call on the next compositing tick.
        _surface.Invalidate();
	}

	//  Visibility
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

		bool zoomed = _player.Config.Video.Zoom > 100;
		Visibility = zoomed ? Visibility.Visible : Visibility.Collapsed;

        if (!zoomed)
            _needSurfaceCleaning = true;
	}

	//  DependencyProperty Callback
	private static void OnShowZoomBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (ZoomOverviewControl)d;
        if (ctrl._renderer is not null && ctrl._initialized)
            ctrl._renderer.ShowZoomBox = (bool)e.NewValue;
    }

    private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ZoomOverviewControl)d).SetPlayer((Player)e.OldValue);

    //  Click-to-pan
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_player == null)
			return;
		_isDragging = true;
		_surface.CaptureMouse();
		PanToPosition(e.GetPosition(_surface));
		e.Handled = true;
	}

	private void OnMouseMove(object sender, MouseEventArgs e)
	{
		if (!_isDragging || _player == null)
			return;
		PanToPosition(e.GetPosition(_surface));
	}

	private void OnMouseUp(object sender, MouseButtonEventArgs e)
	{
		_isDragging = false;
		_surface.ReleaseMouseCapture();
	}

	private void PanToPosition(Point pos)
	{
		double u    = Math.Clamp(pos.X / ActualWidth,  0, 1);
		double v    = Math.Clamp(pos.Y / ActualHeight, 0, 1);
		double panX = (u - 0.5) * 2.0;
		double panY = (v - 0.5) * 2.0;

		_player.Config.Video.PanXOffset = -panX;
		_player.Config.Video.PanYOffset = -panY;
	}

    private void OnSurfaceContentLoad(object sender, DrawingSurfaceEventArgs e)
    {
        // Update-Trigger
        CompositionTarget.Rendering += OnCompositionRendering;

        _device = e.Device;
        _deviceContext = e.Context;
        _initialized = true;

        if (Player is not null)
        {
            BindPlayer(Player);
            _player?.ShowFrame();
        }
    }

    private void OnSurfaceContentUnload(object sender, DrawingSurfaceEventArgs e)
    {
        UnbindPlayer();
        CompositionTarget.Rendering -= OnCompositionRendering;
        _initialized = false;
        _deviceContext = default;
        _device = default;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _surface?.InvalidateMeasure();
        _renderer?.UpdateSize(_surface, (int)e.NewSize.Width, (int)e.NewSize.Height);
    }

    private void RecalcVideoSize()
    {
        if (IsRenderInitialized)
        {   
            try
            {
                SetValue(SideXPropertyKey, _renderer.SideXPixels);
                SetValue(SideYPropertyKey, _renderer.SideYPixels);

                SetValue(VideoWidthPropertyKey, (int)_renderer.Viewport.Width);
                SetValue(VideoHeightPropertyKey, (int)_renderer.Viewport.Height);
            }
            catch { }
        }
    }

    private bool IsRenderInitialized => _renderer is null ? false : _renderer.IsInitialized;
    private bool IsSurfaceInitialized => _device is null || _deviceContext is null ? false : true;
    //  Visual tree
    protected override int VisualChildrenCount => 1;
	protected override Visual GetVisualChild(int index) => _surface;

	protected override Size MeasureOverride(Size availableSize)
	{
		var size = new Size(ActualWidth, ActualHeight);
		_surface.Measure(size);
		return size;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		_surface.Arrange(new Rect(finalSize));
		return finalSize;
	}

	//  IDisposable
	public void Dispose()
	{   
		if (_disposed)
			return;
        _disposed = true;

        SizeChanged -= OnSizeChanged;
        
        _renderer?.Dispose();
        _renderer = default;

        if (_surface != null)
        {
            _surface.Draw -= OnDrawingSurfaceRender;
            _surface.MouseLeftButtonDown -= OnMouseDown;
            _surface.MouseLeftButtonUp -= OnMouseUp;
            _surface.MouseMove -= OnMouseMove;
        }

        // DrawingSurface is an Image (not IDisposable); dispose its D3D11ImageSource source.
        (_surface?.Source as D3D11ImageSource)?.Dispose();
        _surface = default;
    }
}
