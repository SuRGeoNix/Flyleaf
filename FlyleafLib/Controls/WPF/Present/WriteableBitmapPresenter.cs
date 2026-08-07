using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlyleafLib.Controls.WPF.Present;

/// <summary>
/// Software present path: reads frames back to the CPU and blits them into a
/// <see cref="WriteableBitmap"/> shown by an <see cref="Image"/>. Adapter-agnostic
/// and needs no D3D front buffer (works over RDP/headless/locked sessions).
/// </summary>
internal sealed class WriteableBitmapPresenter : IVideoPresenter
{
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private WriteableBitmap _bitmap;
    private int _bmpWidth;
    private int _bmpHeight;
    private IVideoFrameProvider _provider;

    public FrameworkElement Host => _image;
    public bool IsHardware => false;

    public void Attach(IVideoFrameProvider provider) => _provider = provider;

    public void Resize(int pixelWidth, int pixelHeight) => EnsureBitmap(pixelWidth, pixelHeight);

    public void Pump()
    {
        if (_provider == null || _bitmap == null || !_provider.HasPendingFrame)
            return;

        _bitmap.Lock();
        try
        {
            if (_provider.TryCopyCpuInto(_bitmap.BackBuffer, _bitmap.BackBufferStride, _bmpWidth, _bmpHeight))
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, _bmpWidth, _bmpHeight));
        }
        finally
        {
            _bitmap.Unlock();
        }
    }

    public void Dispose()
    {
        _image.Source = null;
        _bitmap = null;
        _bmpWidth = 0;
        _bmpHeight = 0;
    }

    private void EnsureBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (_bitmap != null && _bmpWidth == width && _bmpHeight == height)
            return;

        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _image.Source = _bitmap;
        _bmpWidth = width;
        _bmpHeight = height;
    }
}
