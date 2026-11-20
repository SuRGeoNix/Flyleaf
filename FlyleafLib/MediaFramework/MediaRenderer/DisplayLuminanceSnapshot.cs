namespace FlyleafLib.MediaFramework.MediaRenderer;

public readonly record struct DisplayLuminanceRange(float Minimum, float Peak)
{
    /// <summary>The same override, automatic black-level scaling and clamping used by display mapping.</summary>
    public static DisplayLuminanceRange Resolve(float minimumOverride, float peakOverride, float automaticMinimum, float automaticPeak, bool hdr)
    {
        float referencePeak = float.IsFinite(automaticPeak) && automaticPeak > 0 ? automaticPeak : hdr ? 1000 : 203;
        float referenceMinimum = float.IsFinite(automaticMinimum) && automaticMinimum >= 0 ? automaticMinimum : hdr ? 0 : 0.203f;
        float peak = float.IsFinite(peakOverride) && peakOverride > 0 ? peakOverride : referencePeak;
        peak = Math.Max(peak, 0.1f);
        float minimum = float.IsFinite(minimumOverride) && minimumOverride >= 0 ? minimumOverride : peak * referenceMinimum / referencePeak;
        return new(Math.Clamp(minimum, 0, peak - 0.001f), peak);
    }
}

/// <summary>Automatic references, resolved configuration targets and the currently applied display-mapping range.</summary>
public readonly record struct DisplayLuminanceSnapshot(DisplayLuminanceRange AutomaticSDR, DisplayLuminanceRange SDR,
    DisplayLuminanceRange AutomaticHDR, DisplayLuminanceRange HDR, bool HDROutput, bool MappingActive, DisplayLuminanceRange Applied);
