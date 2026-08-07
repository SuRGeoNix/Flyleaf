namespace FlyleafLib.Controls.WPF.Present;

/// <summary>How <see cref="HybridVideoPresenter"/> presents frames.</summary>
public enum VideoPresentMode
{
    /// <summary>Use the GPU DrawingSurface when a D3D front buffer is available, else software.</summary>
    Auto,
    /// <summary>Always use the GPU DrawingSurface path.</summary>
    Hardware,
    /// <summary>Always use the software WriteableBitmap path (works over RDP/headless/locked).</summary>
    Software
}
