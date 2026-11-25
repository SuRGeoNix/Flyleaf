using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using FlyleafLib.Controls;
using FlyleafLib.MediaPlayer;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME : Grid, IHostPlayer, IDisposable
{
    public static readonly DependencyProperty OverlayContentProperty =
        DependencyProperty.Register(nameof(OverlayContent), typeof(object), typeof(FlyleafME));

    public object? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    public static readonly DependencyProperty UIConfigProperty =
        DependencyProperty.Register(nameof(UIConfig), typeof(FlyleafUIConfig), typeof(FlyleafME),
            new PropertyMetadata(null, OnUIConfigChanged, CoerceUIConfig));

    public FlyleafUIConfig UIConfig
    {
        get => (FlyleafUIConfig)GetValue(UIConfigProperty);
        set => SetValue(UIConfigProperty, value);
    }

    public event EventHandler? UIConfigChanged;
    FlyleafUIConfig? observedConfig;

    static object CoerceUIConfig(DependencyObject d, object value) => value ?? new FlyleafUIConfig();

    static void OnUIConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (FlyleafME)d;
        host.ObserveConfig(host.IsLoaded ? host.UIConfig : null);
        host.RefreshAppearance();
        host.ApplyMousePreferences();
        host.RefreshPanelContexts();
        host.UIConfigChanged?.Invoke(host, EventArgs.Empty);
        if (host.IsLoaded) host.UpdatePlaybackDisplay();
    }

    void ObserveConfig(FlyleafUIConfig? config)
    {
        if (observedConfig != null) observedConfig.PropertyChanged -= ConfigChanged;
        observedConfig = config;
        if (observedConfig != null) observedConfig.PropertyChanged += ConfigChanged;
    }

    void ConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ConfigChanged(sender, e));
            return;
        }
        if (!ReferenceEquals(sender, observedConfig)) return;
        if (e.PropertyName is null or "" or nameof(FlyleafUIConfig.Appearance)) RefreshAppearance();
        if (e.PropertyName is null or "" or nameof(FlyleafUIConfig.Mouse)) ApplyMousePreferences();
        RefreshPanelContexts();
        UIConfigChanged?.Invoke(this, EventArgs.Empty);
        if (IsLoaded) UpdatePlaybackDisplay();
    }

    public void RefreshAppearance()
    {
        var appearance = UIConfig.Appearance;
        FlyleafThemeManager.Apply(Resources, appearance.Theme, appearance.Accent, appearance.CustomAccentColor);
        Host.Resources = Resources;
        if (Host.Overlay != null) Host.Overlay.Resources = Resources;
        RefreshPanelThemes();
    }

    void ApplyMousePreferences()
    {
        var mouse = UIConfig.Mouse;
        Host.SetCurrentValue(FlyleafHost.MouseBindingsProperty, mouse.Bindings);
        Host.SetCurrentValue(FlyleafHost.PanMoveOnCtrlProperty, mouse.PanOnCtrlDrag);
        Host.SetCurrentValue(FlyleafHost.PanZoomOnCtrlWheelProperty, mouse.ZoomOnCtrlWheel);
        Host.SetCurrentValue(FlyleafHost.PanRotateOnShiftWheelProperty, mouse.RotateOnShiftWheel);
        ToggleFullScreenOnDoubleClick = mouse.FullscreenOnDoubleClick;
    }

    public FlyleafME()
    {
        debugOverlay = new(Information);
        InitializeComposition();
        SetCurrentValue(UIConfigProperty, new FlyleafUIConfig());
        Queue.SlideShowIntervalProvider = () => UIConfig.Queue.SlideShowIntervalMilliseconds;
        SetBinding(ActivityTimeoutProperty, new Binding("UIConfig.PlaybackBar.AutoHideDelayMilliseconds") { Source = this, Mode = BindingMode.OneWay });
        Loaded += (_, _) =>
        {
            ObserveConfig(UIConfig);
            AttachHostInput();
            RefreshAppearance();
            ObserveTextPlayer(Player);
            UIConfigChanged?.Invoke(this, EventArgs.Empty);
        };
        Unloaded += (_, _) =>
        {
            ObserveConfig(null);
            DetachHostInput();
            CloseAllPanels();
            ObserveTextPlayer(null);
        };

        var presenter = new ContentPresenter();
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(OverlayContent)) { Source = this });

        overlayRoot.Children.Add(presenter);
        FinishOverlayComposition();
    }
}
