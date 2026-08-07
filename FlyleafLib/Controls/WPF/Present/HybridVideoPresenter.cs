using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace FlyleafLib.Controls.WPF.Present;

/// <summary>
/// Hosts a single video present strategy and owns the WPF render-loop pump.
/// The strategy is chosen once, at load, from <see cref="VideoPresentMode"/>:
/// Hardware = <see cref="DrawingSurfacePresenter"/> (GPU), Software =
/// <see cref="WriteableBitmapPresenter"/> (CPU). Auto starts on GPU and falls back
/// to software one time if no D3D front buffer is available (remote session /
/// headless / locked), then locks the decision.
/// </summary>
public sealed class HybridVideoPresenter : FrameworkElement, IDisposable
{
    /// <summary>Global fallback present mode used by controls that don't override it
    /// (e.g. set once at startup to pin Software for a customer/debug).</summary>
    public static VideoPresentMode DefaultMode { get; set; } = VideoPresentMode.Auto;

    private const int SettleTicks = 60; // ~1s window for Auto to observe the front buffer

    private readonly VideoPresentMode _mode;
    private IVideoFrameProvider _provider;
    private IVideoPresenter _active;

    private bool _renderingHooked;
    private bool _decided;
    private int _settleTicks;
    private int _pixelWidth;
    private int _pixelHeight;

    public HybridVideoPresenter(VideoPresentMode mode)
    {
        _mode = mode;
        Loaded += (_, _) => HookPump();
        Unloaded += (_, _) => UnhookPump();
    }

    public void Attach(VideoFrameProviderBase provider)
    {
        _provider = provider;

        bool startSoftware = _mode == VideoPresentMode.Software
            || (_mode == VideoPresentMode.Auto && IsRemoteSession());

        SetActive(startSoftware ? new WriteableBitmapPresenter() : new DrawingSurfacePresenter());
        _decided = _mode != VideoPresentMode.Auto || startSoftware;

        DebugLogger.Print($"[FLV] HybridVideoPresenter mode={_mode} start={(_active.IsHardware ? "Hardware" : "Software")} decided={_decided}");
        HookPump();
    }

    private void SetActive(IVideoPresenter presenter)
    {
        if (_active != null)
        {
            RemoveVisualChild(_active.Host);
            _active.Dispose();
        }

        _active = presenter;
        _active.Attach(_provider);
        _provider.SetPresentMode(_active.IsHardware ? PresentKind.Hardware : PresentKind.Software);

        AddVisualChild(_active.Host);
        if (_pixelWidth > 0)
            _active.Resize(_pixelWidth, _pixelHeight);

        InvalidateMeasure();
        InvalidateArrange();
    }

    private void HookPump()
    {
        if (!_renderingHooked && _provider != null)
        {
            CompositionTarget.Rendering += OnRendering;
            _renderingHooked = true;
        }
    }

    private void UnhookPump()
    {
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }
    }

    private void OnRendering(object sender, EventArgs e)
    {
        if (_active == null || _provider == null)
            return;

        var (w, h) = GetPixelSize();
        if (w != _pixelWidth || h != _pixelHeight)
        {
            _pixelWidth = w;
            _pixelHeight = h;
            _provider.Resize(w, h);
            _active.Resize(w, h);
        }

        if (!_decided)
            EvaluateAutoFallback();

        _active.Pump();
    }

    // Auto: keep watching the DrawingSurface's front buffer for a short window; if it
    // is unavailable, switch to software once and lock the decision.
    private void EvaluateAutoFallback()
    {
        if (_active is not DrawingSurfacePresenter ds || !ds.IsLoaded)
            return;

        if (!ds.IsFrontBufferAvailable)
        {
            DebugLogger.Print("[FLV] HybridVideoPresenter Auto -> Software (no front buffer)");
            SetActive(new WriteableBitmapPresenter());
            _decided = true;
            return;
        }

        if (++_settleTicks >= SettleTicks)
        {
            DebugLogger.Print("[FLV] HybridVideoPresenter Auto -> Hardware (front buffer stable)");
            _decided = true;
        }
    }

    private (int Width, int Height) GetPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return (
            Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY)));
    }

    protected override int VisualChildrenCount => _active != null ? 1 : 0;

    protected override Visual GetVisualChild(int index) => _active.Host;

    protected override Size MeasureOverride(Size availableSize)
    {
        _active?.Host.Measure(availableSize);
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _active?.Host.Arrange(new Rect(finalSize));
        return finalSize;
    }

    public void Dispose()
    {
        UnhookPump();
        if (_active != null)
        {
            RemoveVisualChild(_active.Host);
            _active.Dispose();
            _active = null;
        }
        _provider?.Dispose();
        _provider = null;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static bool IsRemoteSession() => GetSystemMetrics(0x1000) != 0; // SM_REMOTESESSION
}
