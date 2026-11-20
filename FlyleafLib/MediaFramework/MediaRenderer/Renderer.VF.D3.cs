using System.Linq;
using System.Text.Json.Serialization;

using Vortice.Direct3D11;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    bool hasD3Filters;

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

        hasD3Filters = false;
        foreach (var filter in D3CacheEntry.AllFilters)
            if (d3CacheEntry.Filters.TryGetValue(filter, out var filterSpec))
            {
                if (ucfg.D3Filters.TryGetValue(filter, out var userFilter))
                    userFilter.Initialize(this, filterSpec);
                else
                {
                    userFilter = new(this, filter, filterSpec);
                    lock (ucfg.lockD3Filters)
                        ucfg.D3Filters.Add(filter, userFilter);
                }
                if (userFilter.Value != userFilter.Default)
                {
                    hasD3Filters = true;
                    vc.VideoProcessorSetStreamFilter(vp, 0, filter, true, userFilter.Value);
                }
            }
            else if (ucfg.D3Filters.TryGetValue(filter, out var unsupported))
                unsupported.Dispose();

    }
    void D3FiltersSync()
    {
        if (!ucfg.SyncVPFilters || (!hasD3Filters && !hasFLFilters))
            return;

        hasD3Filters = false;

        foreach(var flFilter in ucfg.FLFilters.Values)
            if (ucfg.D3Filters.TryGetValue(ToVideoProcessorFilter(flFilter.Filter), out var d3Filter) && d3Filter.Available)
            {
                d3Filter.Value = flFilter.Minimum == flFilter.Maximum ? d3Filter.Default :
                    Math.Clamp((int)Math.Round(Scale(flFilter.Value, flFilter.Minimum, flFilter.Maximum, d3Filter.Minimum, d3Filter.Maximum)), d3Filter.Minimum, d3Filter.Maximum);
                if (d3Filter.Value != d3Filter.Default)
                    hasD3Filters = true;

                vc.VideoProcessorSetStreamFilter(vp, 0, d3Filter.Filter, true, d3Filter.Value);
            }
        // Include device-specific filters that have no Flyleaf counterpart.
        hasD3Filters = ucfg.D3Filters.Values.Any(filter => filter.Available && filter.Value != filter.Default);
    }
    internal void D3SetFilter(D3Filter filter)
    {   // Direct Call from Config
        if (filter.Value != filter.Default)
            hasD3Filters = true;
        else
            hasD3Filters = ucfg.D3Filters.Values.Any(f => f.Available && f.Value != f.Default);

        if (vc != null && vp != null)
        {
            vc.VideoProcessorSetStreamFilter(vp, 0, filter.Filter, true, filter.Value);
            RenderRequest();
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
{   // NOTE: Serialization requires Public sets and constructor

    [JsonIgnore]
    public bool         Available   => renderer != null;
    public VideoProcessorFilter
                        Filter      { get; set; }
    public int          Minimum     { get; set; }
    public int          Maximum     { get; set; }
    public float        Step        { get; set; }
    public int          Default     { get; set; }
    public int          Value       { get => _Value; set { if (Set(ref _Value, value)) renderer?.D3SetFilter(this); }}
    protected int _Value;

    internal Renderer renderer;

    public D3Filter() { }

    internal D3Filter(Renderer renderer, VideoProcessorFilter filter, VideoProcessorFilterRange filterSpec)
    {
        Filter      = filter;
        Minimum     = filterSpec.Minimum;
        Maximum     = filterSpec.Maximum;
        Step        = filterSpec.Multiplier;
        Default     = Value = filterSpec.Default;
        this.renderer = renderer;
    }

    internal void Initialize(Renderer renderer, VideoProcessorFilterRange range)
    {
        bool wasAvailable = Available;
        Minimum = range.Minimum;
        Maximum = range.Maximum;
        Default = range.Default;
        Step = range.Multiplier; // Raw integer levels are multiplied for display, not slider increments.
        _Value = Math.Clamp(_Value, Minimum, Maximum);
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
