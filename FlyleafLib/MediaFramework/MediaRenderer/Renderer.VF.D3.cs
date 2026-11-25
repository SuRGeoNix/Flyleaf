using System.Text.Json.Serialization;

using Vortice.Direct3D11;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    void D3FiltersSetup(D3CacheEntry d3CacheEntry, bool needsFillUnlock)
    {
        if (needsFillUnlock)
        {   // Fills Supported FilterSpecs to Cache (1st time only)
            var filterCaps = ve.VideoProcessorCaps.FilterCaps;
            foreach (var filterCap in D3CacheEntry.AllFilterCaps)
                if (filterCaps.HasFlag(filterCap))
                {
                    var filter = ToVideoProcessorFilter(filterCap);
                    var range  = ve.GetVideoProcessorFilterRange(filter);
                    d3CacheEntry.Filters.Add(filter, range);
                }

            // Check NVidia Super Resolution availability
            if (gpuAdapter.Vendor == GPUVendor.Nvidia)
                fixed(uint* ptr = &d3CacheEntry.SuperResNvidiaAvailable)
                    vc.VideoProcessorGetStreamExtension(vp, 0, GUID_SUPERRES_NVIDIA, sizeof(uint), (nint)ptr);

            d3CacheEntry.Failed = false;
            Monitor.Exit(d3CacheEntry);
        }

        superResNvidiaAvailable = d3CacheEntry.SuperResNvidiaAvailable == 1;

        lock (ucfg.lockD3Filters)
            foreach (var filter in D3CacheEntry.AllFilters)
                if (d3CacheEntry.Filters.TryGetValue(filter, out var range))
                {
                    if (ucfg.D3Filters.TryGetValue(filter, out var userFilter))
                        userFilter.Initialize(this, range);
                    else
                        ucfg.D3Filters.Add(filter, new(this, filter, range));
                }
                else if (ucfg.D3Filters.TryGetValue(filter, out var unsupported))
                    unsupported.Dispose();

        RefreshD3Filters();
    }

    void RefreshD3Filters()
    {
        lock (ucfg.lockFLFilters)
        lock (ucfg.lockD3Filters)
            foreach (var filter in ucfg.D3Filters.Values)
            {
                if (!filter.Available) continue;

                int level = ucfg.SyncVPFilters && ToFlyleafFilter(filter.Filter) is { } flyleaf
                    ? ProjectFilterLevel(GetSyncedFilterLevel(flyleaf), filter.Minimum, filter.Default, filter.Maximum)
                    : filter.ClampRequestedValue();
                filter.SetEffectiveValue(level);
                if (vc != null && vp != null)
                    vc.VideoProcessorSetStreamFilter(vp, 0, filter.Filter, level != filter.Default, level);
            }
    }

    static VideoProcessorFilter ToVideoProcessorFilter(VideoProcessorFilterCaps filter) => filter switch
    {
        VideoProcessorFilterCaps.Brightness         => VideoProcessorFilter.Brightness,
        VideoProcessorFilterCaps.Contrast           => VideoProcessorFilter.Contrast,
        VideoProcessorFilterCaps.Hue                => VideoProcessorFilter.Hue,
        VideoProcessorFilterCaps.Saturation         => VideoProcessorFilter.Saturation,
        VideoProcessorFilterCaps.EdgeEnhancement    => VideoProcessorFilter.EdgeEnhancement,
        VideoProcessorFilterCaps.NoiseReduction     => VideoProcessorFilter.NoiseReduction,
        VideoProcessorFilterCaps.AnamorphicScaling  => VideoProcessorFilter.AnamorphicScaling,
        VideoProcessorFilterCaps.StereoAdjustment   => VideoProcessorFilter.StereoAdjustment,
        _ => default,
    };
}

public class D3Filter : NotifyPropertyChanged
{
    [JsonIgnore]
    public bool         Available   => renderer != null;
    public VideoProcessorFilter
                        Filter      { get; set; }
    [JsonIgnore]
    public int Minimum { get => activeRange?.Minimum ?? SavedMinimum; set => SavedMinimum = value; }
    [JsonIgnore]
    public int Maximum { get => activeRange?.Maximum ?? SavedMaximum; set => SavedMaximum = value; }
    [JsonIgnore]
    public float Step { get => activeRange?.Multiplier ?? SavedStep; set => SavedStep = value; }
    [JsonIgnore]
    public int Default { get => activeRange?.Default ?? SavedDefault; set => SavedDefault = value; }
    [JsonIgnore]
    public int Value { get => Available ? effectiveValue : RequestedValue; set => SetPreference(value); }

    [JsonInclude, JsonPropertyName("Minimum")]
    internal int SavedMinimum { get; set; }
    [JsonInclude, JsonPropertyName("Maximum")]
    internal int SavedMaximum { get; set; }
    [JsonInclude, JsonPropertyName("Step")]
    internal float SavedStep { get; set; }
    [JsonInclude, JsonPropertyName("Default")]
    internal int SavedDefault { get; set; }
    [JsonInclude, JsonPropertyName("Value")]
    internal int RequestedValue { get => _Value; set => _Value = value; }
    protected int _Value;

    int effectiveValue;
    VideoProcessorFilterRange? activeRange;
    internal Renderer renderer;

    public D3Filter() { }

    internal D3Filter(Renderer renderer, VideoProcessorFilter filter, VideoProcessorFilterRange range)
    {
        Filter = filter;
        activeRange = range;
        CapturePreference(range.Default);
        effectiveValue = range.Default;
        this.renderer = renderer;
    }

    /// <summary>Restore the neutral filter level, including its shared preference.</summary>
    public void Reset() => SetPreference(Default, force: true);

    void SetPreference(int value, bool force = false)
    {
        if (!force && value == Value) return;
        if (renderer is { } owner)
            owner.EditFilter(this, value);
        else
            Set(ref _Value, value, propertyName: nameof(Value));
    }

    internal void CapturePreference(int level)
    {
        SavedMinimum = Minimum;
        SavedMaximum = Maximum;
        SavedDefault = Default;
        SavedStep = Step;
        RequestedValue = level;
    }

    internal int ClampRequestedValue() => RequestedValue == SavedDefault ? Default :
        Math.Clamp(RequestedValue, Minimum, Maximum);

    internal void SetEffectiveValue(int value) => SetUI(ref effectiveValue, value, propertyName: nameof(Value));

    internal D3Filter ClonePreference()
    {
        var copy = new D3Filter { Filter = Filter };
        copy.CopyPreference(this);
        return copy;
    }

    internal void CopyPreference(D3Filter source)
    {
        SavedMinimum = source.SavedMinimum;
        SavedMaximum = source.SavedMaximum;
        SavedDefault = source.SavedDefault;
        SavedStep = source.SavedStep;
        RequestedValue = source.RequestedValue;
    }

    internal void Initialize(Renderer renderer, VideoProcessorFilterRange range)
    {
        bool wasAvailable = Available;
        activeRange = range;
        effectiveValue = ClampRequestedValue();
        this.renderer = renderer;
        if (!wasAvailable) RaiseUI(nameof(Available));
        RaiseUI(nameof(Minimum));
        RaiseUI(nameof(Maximum));
        RaiseUI(nameof(Default));
        RaiseUI(nameof(Step));
        RaiseUI(nameof(Value));
    }

    internal void Dispose()
    {
        bool wasAvailable = Available;
        renderer = null;
        activeRange = null;
        if (wasAvailable) RaiseUI(nameof(Available));
    }
}

class D3CacheEntry
{   // Checks Success and Filters support for D3VP once per LUID

    public static VideoProcessorFilter[]        AllFilters      = Enum.GetValues<VideoProcessorFilter>();
    public static VideoProcessorFilterCaps[]    AllFilterCaps   = Enum.GetValues<VideoProcessorFilterCaps>();

    public static Dictionary<long, D3CacheEntry>
                D3Cache = [];

    public Dictionary<VideoProcessorFilter, VideoProcessorFilterRange>
                Filters = [];

    public bool Failed  = true;
    public uint SuperResNvidiaAvailable;

    public static D3CacheEntry Get(long luid, out bool needsFillUnlock)
    {
        lock (D3Cache)
        {
            if (D3Cache.TryGetValue(luid, out var d3vp))
            {
                needsFillUnlock = false;
                lock(d3vp) return d3vp;
            }

            needsFillUnlock = true;
            var d3filter = new D3CacheEntry();
            Monitor.Enter(d3filter);
            D3Cache.Add(luid, d3filter);
            return d3filter;
        }
    }
}
