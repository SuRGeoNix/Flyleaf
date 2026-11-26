using Vortice.Direct3D11;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    // Zero is neutral on both processors, even when a device reports an asymmetric range.
    internal static double NormalizeFilterLevel(int level, int minimum, int neutral, int maximum)
    {
        double span = level < neutral ? (double)neutral - minimum : (double)maximum - neutral;
        return span > 0 ? Math.Clamp((level - (double)neutral) / span, -1, 1) : 0;
    }

    internal static int ProjectFilterLevel(double level, int minimum, int neutral, int maximum)
    {
        if (!double.IsFinite(level)) return neutral;
        double span = level < 0 ? (double)neutral - minimum : (double)maximum - neutral;
        return (int)Math.Round(neutral + Math.Clamp(level, -1, 1) * Math.Max(0, span));
    }

    static FLFilters? ToFlyleafFilter(VideoProcessorFilter filter) => filter switch
    {
        VideoProcessorFilter.Brightness => FLFilters.Brightness,
        VideoProcessorFilter.Contrast   => FLFilters.Contrast,
        VideoProcessorFilter.Hue        => FLFilters.Hue,
        VideoProcessorFilter.Saturation => FLFilters.Saturation,
        _ => null
    };

    // Old profiles have only the per-processor entries. Use the saved Flyleaf value
    // when both have edits, or the saved D3D11 value if it is the only adjustment.
    double GetSyncedFilterLevel(FLFilters filter)
    {
        if (ucfg.SyncedFilterLevels.TryGetValue(filter, out double level))
            return level;

        if (ucfg.FLFilters.TryGetValue(filter, out var fl) && fl.RequestedValue != fl.Default)
            return NormalizeFilterLevel(fl.RequestedValue, fl.Minimum, fl.Default, fl.Maximum);

        if (ucfg.D3Filters.TryGetValue(ToVideoProcessorFilter(filter), out var d3))
            return NormalizeFilterLevel(d3.RequestedValue, d3.SavedMinimum, d3.SavedDefault, d3.SavedMaximum);

        return 0;
    }

    internal void EditFilter(FLFilter filter, int level)
    {
        lock (lockDevice)
        {
            lock (ucfg.lockFLFilters)
            {
                filter.RequestedValue = level;
                if (ucfg.SyncVPFilters)
                    ucfg.SyncedFilterLevels[filter.Filter] = NormalizeFilterLevel(level, filter.Minimum, filter.Default, filter.Maximum);
            }

            RefreshEditedFilters();
        }
    }

    internal void EditFilter(D3Filter filter, int level)
    {
        lock (lockDevice)
        {
            lock (ucfg.lockFLFilters)
            lock (ucfg.lockD3Filters)
            {
                filter.CapturePreference(level);
                if (ucfg.SyncVPFilters && ToFlyleafFilter(filter.Filter) is { } flyleaf)
                    ucfg.SyncedFilterLevels[flyleaf] = NormalizeFilterLevel(level, filter.Minimum, filter.Default, filter.Maximum);
            }

            RefreshEditedFilters();
        }
    }

    internal void SyncVideoFilterPreferenceChanged()
    {
        lock (lockDevice)
        {
            // Enabling sync uses the settings of the processor the user is looking at.
            if (ucfg.SyncVPFilters)
            {
                lock (ucfg.lockFLFilters)
                lock (ucfg.lockD3Filters)
                {
                    if (VideoProcessor == VideoProcessors.D3D11)
                    {
                        foreach (var filter in ucfg.D3Filters.Values)
                            if (filter.Available && ToFlyleafFilter(filter.Filter) is { } flyleaf)
                                ucfg.SyncedFilterLevels[flyleaf] = NormalizeFilterLevel(filter.ClampRequestedValue(),
                                    filter.Minimum, filter.Default, filter.Maximum);
                    }
                    else
                        foreach (var filter in ucfg.FLFilters.Values)
                            ucfg.SyncedFilterLevels[filter.Filter] = NormalizeFilterLevel(filter.RequestedValue,
                                filter.Minimum, filter.Default, filter.Maximum);
                }
            }

            RefreshEditedFilters();
        }
    }

    /// <summary>Restores saved filter preferences from a snapshot without replacing the live editor objects.</summary>
    public void RestoreVideoFilterPreferences(Config.VideoConfig snapshot) => RestoreVideoFilterPreferences(snapshot, replace: false);

    internal void RestoreVideoFilterPreferences(Config.VideoConfig snapshot, bool replace)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (lockDevice)
        {
            ucfg.RestoreFilterPreferences(snapshot, replace);
            RefreshEditedFilters();
        }
        if (replace) NotifyFiltersChanged();
    }

    void RefreshEditedFilters()
    {
        if (D3Disposed) return;

        RefreshD3Filters();
        RefreshFLFilters(request: true);
    }
}
