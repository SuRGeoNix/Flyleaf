using Vortice.Direct3D11;

namespace FlyleafLib.Controls.WPF.Present;

/// <summary>
/// Produces the current player frame and hands it to a presenter. Depending on the
/// active <see cref="PresentKind"/> and whether render/compositor are the same
/// adapter, a frame is exposed as a GPU shared texture (zero-copy) and/or as CPU
/// pixels (readback).
/// </summary>
internal interface IVideoFrameProvider : IDisposable
{
    bool HasPendingFrame { get; }
    bool IsCrossAdapter { get; }

    /// <summary>Hardware path: the DrawingSurface's device (used for LUID cross-adapter detection).</summary>
    void SetCompositorDevice(ID3D11Device1 device);
    /// <summary>Selects readback behaviour; recreates the frame texture lazily.</summary>
    void SetPresentMode(PresentKind kind);
    void Resize(int pixelWidth, int pixelHeight);

    /// <summary>Same-adapter GPU path. Invokes <paramref name="copyFromHandle"/> under lock with the
    /// render-device shared texture handle; returns whatever the callback returns (true = presented).</summary>
    bool TryPresentShared(Func<nint, bool> copyFromHandle);

    /// <summary>Cross-adapter GPU path. Invokes <paramref name="upload"/> under lock with the CPU
    /// frame (ptr, rowPitch, width, height); returns whatever the callback returns.</summary>
    bool TryPresentCpu(Func<nint, int, int, int, bool> upload);

    /// <summary>Software path. Copies the CPU frame into a locked WriteableBitmap back buffer.</summary>
    bool TryCopyCpuInto(nint dest, int destStride, int destWidth, int destHeight);
}
