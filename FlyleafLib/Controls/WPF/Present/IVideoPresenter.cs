using System.Windows;

namespace FlyleafLib.Controls.WPF.Present;

/// <summary>A WPF-hostable present target fed by an <see cref="IVideoFrameProvider"/>.</summary>
internal interface IVideoPresenter : IDisposable
{
    FrameworkElement Host { get; }
    bool IsHardware { get; }
    void Attach(IVideoFrameProvider provider);
    /// <summary>One render-loop tick; presents the latest frame if one is pending.</summary>
    void Pump();
    void Resize(int pixelWidth, int pixelHeight);
}
