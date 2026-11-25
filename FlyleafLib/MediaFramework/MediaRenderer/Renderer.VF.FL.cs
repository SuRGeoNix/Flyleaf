using System.Text.Json.Serialization;

using Vortice.Direct3D11;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    bool hasFLFilters;

    void FLFiltersSetup()
    {
        lock (ucfg.lockFLFilters)
            foreach (var spec in FLFilter.FLFilterSpecs)
            {
                if (ucfg.FLFilters.TryGetValue(spec.Filter, out var filter))
                    filter.Initialize(this);
                else
                    ucfg.FLFilters.Add(spec.Filter, new(this, spec));
            }

        RefreshFLFilters();
    }

    void RefreshFLFilters(bool request = false)
    {
        bool previous = hasFLFilters;
        hasFLFilters = false;
        lock (ucfg.lockFLFilters)
        lock (ucfg.lockD3Filters)
            foreach (var filter in ucfg.FLFilters.Values)
            {
                int level = ucfg.SyncVPFilters
                    ? ProjectFilterLevel(GetSyncedFilterLevel(filter.Filter), filter.Minimum, filter.Default, filter.Maximum)
                    : Math.Clamp(filter.RequestedValue, filter.Minimum, filter.Maximum);
                filter.SetEffectiveValue(level);
                hasFLFilters |= level != filter.Default;
                ApplyFLFilter(filter);
            }

        var requests = VPRequestType.UpdatePS;
        if (request && previous != hasFLFilters && VideoProcessor != VideoProcessors.D3D11)
            requests |= VPRequestType.ReConfigVP;
        VPRequest(requests, execute: request);
    }

    void ApplyFLFilter(FLFilter cfgFilter)
    {
        float value = cfgFilter.Minimum == cfgFilter.Maximum ? cfgFilter.MinimumPS :
            Scale(cfgFilter.Value, cfgFilter.Minimum, cfgFilter.Maximum, cfgFilter.MinimumPS, cfgFilter.MaximumPS);

        switch (cfgFilter.Filter)
        {
            case FLFilters.Brightness:
                psData.Brightness   = value;
                break;

            case FLFilters.Contrast:
                psData.Contrast     = value;
                break;

            case FLFilters.Hue:
                psData.Hue          = value;
                break;

            case FLFilters.Saturation:
                psData.Saturation   = value;
                break;
        }
    }

    static VideoProcessorFilter ToVideoProcessorFilter(FLFilters filter) => filter switch
    {
        FLFilters.Brightness    => VideoProcessorFilter.Brightness,
        FLFilters.Contrast      => VideoProcessorFilter.Contrast,
        FLFilters.Hue           => VideoProcessorFilter.Hue,
        FLFilters.Saturation    => VideoProcessorFilter.Saturation,
        _ => default,
    };
}

public class FLFilter : NotifyPropertyChanged
{   // TBR: Publics that currently required for Serialization

    [JsonIgnore]
    public bool         Available   => true; // TBR: FL always available
    public FLFilters    Filter      { get; set; }
    public int          Minimum     { get; set; }
    public float        MinimumPS   { get; set; }
    public int          Maximum     { get; set; }
    public float        MaximumPS   { get; set; }
    public float        Step        { get; set; }
    public int          Default     { get; set; }
    [JsonIgnore]
    public int          Value       { get => renderer == null ? RequestedValue : effectiveValue; set => SetPreference(value); }
    [JsonInclude, JsonPropertyName("Value")]
    internal int RequestedValue { get => _Value; set => _Value = value; }
    protected int _Value;
    int effectiveValue;

    Renderer renderer;

    public FLFilter() { }

    internal FLFilter(Renderer renderer, FLFilterSpec filterSpec)
    {
        Filter      = filterSpec.Filter;
        Minimum     = filterSpec.Minimum;
        MinimumPS   = filterSpec.MinimumPS;
        Maximum     = filterSpec.Maximum;
        MaximumPS   = filterSpec.MaximumPS;
        Step        = filterSpec.Step;
        Default     = RequestedValue = effectiveValue = filterSpec.Default;
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

    internal void SetEffectiveValue(int value) => SetUI(ref effectiveValue, value, propertyName: nameof(Value));

    internal FLFilter ClonePreference() => new()
    {
        Filter = Filter, Minimum = Minimum, Maximum = Maximum, MinimumPS = MinimumPS, MaximumPS = MaximumPS,
        Step = Step, Default = Default, RequestedValue = RequestedValue
    };

    internal void Initialize(Renderer renderer)
    {
        if (this.renderer == null) effectiveValue = RequestedValue;
        this.renderer = renderer;
    }

    internal void Dispose()
        => renderer = null;

    internal static List<FLFilterSpec> FLFilterSpecs =
        [new()
        {
            Filter  = FLFilters.Brightness,
            Minimum = -100,
            Maximum = 100,
            Default = 0,
            Step    = 1,

            MinimumPS = -0.5f,
            MaximumPS =  0.5f
        },
        new()
        {
            Filter  = FLFilters.Contrast,
            Minimum = -100,
            Maximum = 100,
            Default = 0,
            Step    = 1,

            MinimumPS = 0f,
            MaximumPS = 2f
        },
        new()
        {
            Filter  = FLFilters.Hue,
            Minimum = -180,
            Maximum = 180,
            Default = 0,
            Step    = 1,

            MinimumPS = -3.14f,
            MaximumPS =  3.14f
        },
        new()
        {
            Filter  = FLFilters.Saturation,
            Minimum = -100,
            Maximum = 100,
            Default = 0,
            Step    = 1,

            MinimumPS = 0,
            MaximumPS = 2
        }];

    internal class FLFilterSpec
    {
        public FLFilters Filter;
        public int      Default;
        public float    Step;
        public int      Minimum;
        public float    MinimumPS;
        public int      Maximum;
        public float    MaximumPS;
    }
}
