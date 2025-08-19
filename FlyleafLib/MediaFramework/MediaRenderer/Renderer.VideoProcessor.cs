using Microsoft.VisualBasic.FileIO;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static FlyleafLib.Logger;
using static FlyleafLib.Utils;
using ID3D11VideoContext = Vortice.Direct3D11.ID3D11VideoContext;

namespace FlyleafLib.MediaFramework.MediaRenderer;

unsafe public partial class Renderer
{
    /* TODO
     * 1) Try to sync filters between Flyleaf and D3D11 video processors so we will not have to reset on change
     * 2) Filter default values will change when the device/adapter is changed
     */

    static Dictionary<string, VideoProcessorCapsCache> VideoProcessorsCapsCache = [];

    internal static VideoProcessorFilter ConvertFromVideoProcessorFilterCaps(VideoProcessorFilterCaps filter)
    {
        return filter switch
        {
            VideoProcessorFilterCaps.Brightness         => VideoProcessorFilter.Brightness,
            VideoProcessorFilterCaps.Contrast           => VideoProcessorFilter.Contrast,
            VideoProcessorFilterCaps.Hue                => VideoProcessorFilter.Hue,
            VideoProcessorFilterCaps.Saturation         => VideoProcessorFilter.Saturation,
            VideoProcessorFilterCaps.EdgeEnhancement    => VideoProcessorFilter.EdgeEnhancement,
            VideoProcessorFilterCaps.NoiseReduction     => VideoProcessorFilter.NoiseReduction,
            VideoProcessorFilterCaps.AnamorphicScaling  => VideoProcessorFilter.AnamorphicScaling,
            VideoProcessorFilterCaps.StereoAdjustment   => VideoProcessorFilter.StereoAdjustment,
            _ => VideoProcessorFilter.StereoAdjustment,
        };
    }
    internal static VideoProcessorFilterCaps ConvertFromVideoProcessorFilter(VideoProcessorFilter filter)
    {
        return filter switch
        {
            VideoProcessorFilter.Brightness             => VideoProcessorFilterCaps.Brightness,
            VideoProcessorFilter.Contrast               => VideoProcessorFilterCaps.Contrast,
            VideoProcessorFilter.Hue                    => VideoProcessorFilterCaps.Hue,
            VideoProcessorFilter.Saturation             => VideoProcessorFilterCaps.Saturation,
            VideoProcessorFilter.EdgeEnhancement        => VideoProcessorFilterCaps.EdgeEnhancement,
            VideoProcessorFilter.NoiseReduction         => VideoProcessorFilterCaps.NoiseReduction,
            VideoProcessorFilter.AnamorphicScaling      => VideoProcessorFilterCaps.AnamorphicScaling,
            VideoProcessorFilter.StereoAdjustment       => VideoProcessorFilterCaps.StereoAdjustment,
            _ => VideoProcessorFilterCaps.StereoAdjustment,
        };
    }
    internal static VideoFilter ConvertFromVideoProcessorFilterRange(VideoProcessorFilterRange filter) => new()
    {
        Minimum = filter.Minimum,
        Maximum = filter.Maximum,
        Value   = filter.Default,
        Step    = filter.Multiplier
    };

    VideoColor                          D3D11VPBackgroundColor;
    ID3D11VideoDevice1                  vd1;
    ID3D11VideoProcessor                vp;
    ID3D11VideoContext                  vc;
    ID3D11VideoProcessorEnumerator      vpe;
    ID3D11VideoProcessorInputView       vpiv;
    ID3D11VideoProcessorOutputView      vpov;

    VideoProcessorStream[]              vpsa    = [new() { Enable = true }];
    VideoProcessorContentDescription    vpcd    = new()
        {
            Usage           = VideoUsage.PlaybackNormal,
            InputFrameFormat= VideoFrameFormat.Progressive,

            InputFrameRate  = new(1, 1),
            OutputFrameRate = new(1, 1),
        };
    VideoProcessorOutputViewDescription vpovd   = new() { ViewDimension = VideoProcessorOutputViewDimension.Texture2D };
    VideoProcessorInputViewDescription  vpivd   = new()
        {
            FourCC          = 0,
            ViewDimension   = VideoProcessorInputViewDimension.Texture2D,
            Texture2D       = new() { MipSlice = 0, ArraySlice = 0 }
        };
    VideoProcessorColorSpace            inputColorSpace;
    VideoProcessorColorSpace            outputColorSpace;

    uint actualRotation;
    bool actualHFlip, actualVFlip;
    bool configLoadedChecked;

    void InitializeVideoProcessor()
    {
        lock (VideoProcessorsCapsCache)
            try
            {
                vpcd.InputWidth = 1;
                vpcd.InputHeight= 1;
                vpcd.OutputWidth = vpcd.InputWidth;
                vpcd.OutputHeight= vpcd.InputHeight;

                outputColorSpace = new VideoProcessorColorSpace()
                {
                    Usage           = 0,
                    RGB_Range       = 0,
                    YCbCr_Matrix    = 1,
                    YCbCr_xvYCC     = 0,
                    Nominal_Range   = 2
                };

                if (VideoProcessorsCapsCache.TryGetValue(Device.Tag.ToString(), out var cache))
                {
                    if (cache.Failed)
                    {
                        InitializeFilters();
                        return;
                    }

                    vd1 = Device.QueryInterface<ID3D11VideoDevice1>();
                    vc  = context.QueryInterface<ID3D11VideoContext1>();

                    vd1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);

                    if (vpe == null)
                    {
                        VPFailed();
                        return;
                    }

                    // if (!vpcc.TypeIndex != -1)
                    vd1.CreateVideoProcessor(vpe, (uint)cache.TypeIndex, out vp);
                    InitializeFilters();

                    return;
                }

                cache = new();
                VideoProcessorsCapsCache.Add(Device.Tag.ToString(), cache);

                vd1 = Device.QueryInterface<ID3D11VideoDevice1>();
                vc  = context.QueryInterface<ID3D11VideoContext>();

                vd1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);

                if (vpe == null || Device.FeatureLevel < Vortice.Direct3D.FeatureLevel.Level_10_0)
                {
                    VPFailed();
                    return;
                }

                var vpe1    = vpe.QueryInterface<ID3D11VideoProcessorEnumerator1>();
                cache.HLG   = vpe1.CheckVideoProcessorFormatConversion(Format.P010, ColorSpaceType.YcbcrStudioGhlgTopLeftP2020,  Format.B8G8R8A8_UNorm, ColorSpaceType.RgbFullG22NoneP709);
                cache.HDR10 = vpe1.CheckVideoProcessorFormatConversion(Format.P010, ColorSpaceType.YcbcrStudioG2084TopLeftP2020, Format.B8G8R8A8_UNorm, ColorSpaceType.RgbStudioG2084NoneP2020);

                var vpCaps  = vpe.VideoProcessorCaps;
                string dump = "";

                if (CanDebug)
                {
                    dump += $"=====================================================\r\n";
                    dump += $"MaxInputStreams           {vpCaps.MaxInputStreams}\r\n";
                    dump += $"MaxStreamStates           {vpCaps.MaxStreamStates}\r\n";
                    dump += $"HDR10 Limited             {(cache.HLG     ? "yes" : "no")}\r\n";
                    dump += $"HLG                       {(cache.HDR10   ? "yes" : "no")}\r\n";

                    dump += $"\n[Video Processor Device Caps]\r\n";
                    foreach (VideoProcessorDeviceCaps cap in Enum.GetValues(typeof(VideoProcessorDeviceCaps)))
                        dump += $"{cap,-25} {((vpCaps.DeviceCaps & cap) != 0 ? "yes" : "no")}\r\n";

                    dump += $"\n[Video Processor Feature Caps]\r\n";
                    foreach (VideoProcessorFeatureCaps cap in Enum.GetValues(typeof(VideoProcessorFeatureCaps)))
                        dump += $"{cap,-25} {((vpCaps.FeatureCaps & cap) != 0 ? "yes" : "no")}\r\n";

                    dump += $"\n[Video Processor Stereo Caps]\r\n";
                    foreach (VideoProcessorStereoCaps cap in Enum.GetValues(typeof(VideoProcessorStereoCaps)))
                        dump += $"{cap,-25} {((vpCaps.StereoCaps & cap) != 0 ? "yes" : "no")}\r\n";

                    dump += $"\n[Video Processor Input Format Caps]\r\n";
                    foreach (VideoProcessorFormatCaps cap in Enum.GetValues(typeof(VideoProcessorFormatCaps)))
                        dump += $"{cap,-25} {((vpCaps.InputFormatCaps & cap) != 0 ? "yes" : "no")}\r\n";

                    dump += $"\n[Video Processor Filter Caps]\r\n";
                }

                foreach (VideoProcessorFilterCaps filter in Enum.GetValues(typeof(VideoProcessorFilterCaps)))
                    if ((vpCaps.FilterCaps & filter) != 0)
                    {
                        vpe1.GetVideoProcessorFilterRange(ConvertFromVideoProcessorFilterCaps(filter), out var range);
                        if (CanDebug) dump += $"{filter,-25} [{range.Minimum,6} - {range.Maximum,4}] | x{range.Multiplier,4} | *{range.Default}\r\n";
                        var vf = ConvertFromVideoProcessorFilterRange(range);
                        vf.Filter = (VideoFilters)filter;
                        cache.Filters.Add((VideoFilters)filter, vf);
                    }
                    else if (CanDebug)
                        dump += $"{filter,-25} no\r\n";

                if (CanDebug)
                {
                    dump += $"\n[Video Processor Input Format Caps]\r\n";
                    foreach (VideoProcessorAutoStreamCaps cap in Enum.GetValues(typeof(VideoProcessorAutoStreamCaps)))
                        dump += $"{cap,-25} {((vpCaps.AutoStreamCaps & cap) != 0 ? "yes" : "no")}\r\n";
                }

                uint typeIndex = 0;
                VideoProcessorRateConversionCaps rcCap = new();
                for (uint i = 0; i < vpCaps.RateConversionCapsCount; i++)
                {
                    vpe.GetVideoProcessorRateConversionCaps(i, out rcCap);
                    VideoProcessorProcessorCaps pCaps = (VideoProcessorProcessorCaps) rcCap.ProcessorCaps;

                    if (CanDebug)
                    {
                        dump += $"\n[Video Processor Rate Conversion Caps #{i}]\r\n";

                        dump += $"\n\t[Video Processor Rate Conversion Caps]\r\n";
                        var fields = typeof(VideoProcessorRateConversionCaps).GetFields();
                        foreach (var field in fields)
                            dump += $"\t{field.Name,-35} {field.GetValue(rcCap)}\r\n";

                        dump += $"\n\t[Video Processor Processor Caps]\r\n";
                        foreach (VideoProcessorProcessorCaps cap in Enum.GetValues(typeof(VideoProcessorProcessorCaps)))
                            dump += $"\t{cap,-35} {(((VideoProcessorProcessorCaps)rcCap.ProcessorCaps & cap) != 0 ? "yes" : "no")}\r\n";
                    }

                    typeIndex = i;

                    if (((VideoProcessorProcessorCaps)rcCap.ProcessorCaps & VideoProcessorProcessorCaps.DeinterlaceBob) != 0)
                        break; // TBR: When we add past/future frames support
                }
                vpe1.Dispose();

                if (CanDebug) Log.Debug($"D3D11 Video Processor\r\n{dump}");

                cache.TypeIndex = (int)typeIndex;
                cache.VideoProcessorCaps = vpCaps;
                cache.VideoProcessorRateConversionCaps = rcCap;

                //if (typeIndex != -1)
                vd1.CreateVideoProcessor(vpe, typeIndex, out vp);
                if (vp == null)
                {
                    VPFailed();
                    return;
                }

                cache.Failed = false;
                Log.Info($"D3D11 Video Processor Initialized (Rate Caps #{typeIndex})");

            } catch { DisposeVideoProcessor(); Log.Error($"D3D11 Video Processor Initialization Failed"); }

        InitializeFilters();
    }
    void VPFailed()
    {
        Log.Error($"D3D11 Video Processor Initialization Failed");

        if (!VideoProcessorsCapsCache.TryGetValue(Device.Tag.ToString(), out var vpcc))
        {
            vpcc = new();
            VideoProcessorsCapsCache.Add(Device.Tag.ToString(), vpcc);
        }

        vpcc.Failed = true;
        vpcc.Filters.Add(VideoFilters.Brightness, new() { Filter = VideoFilters.Brightness });
        vpcc.Filters.Add(VideoFilters.Contrast,   new() { Filter = VideoFilters.Contrast });

        DisposeVideoProcessor();
        InitializeFilters();
    }
    void DisposeVideoProcessor()
    {
        vpiv?.Dispose();
        vpov?.Dispose();
        vp?.  Dispose();
        vpe?. Dispose();
        vc?.  Dispose();
        vd1?. Dispose();

        vc = null;
    }
    void InitializeFilters()
    {
        Filters = VideoProcessorsCapsCache[Device.Tag.ToString()].Filters;

        // Add FLVP filters if D3D11VP does not support them
        if (!Filters.ContainsKey(VideoFilters.Brightness))
            Filters.Add(VideoFilters.Brightness,new(VideoFilters.Brightness));

        if (!Filters.ContainsKey(VideoFilters.Contrast))
            Filters.Add(VideoFilters.Contrast,  new(VideoFilters.Contrast));

        if (!Filters.ContainsKey(VideoFilters.Hue))
            Filters.Add(VideoFilters.Hue,       new(VideoFilters.Hue));

        if (!Filters.ContainsKey(VideoFilters.Saturation))
            Filters.Add(VideoFilters.Saturation,new(VideoFilters.Saturation));

        foreach(var filter in Filters.Values)
        {
            if (!Config.Video.Filters.ContainsKey(filter.Filter))
                continue;

            var cfgFilter = Config.Video.Filters[filter.Filter];
            cfgFilter.Available = true;
            cfgFilter.renderer  = this;

            if (!configLoadedChecked && !Config.Loaded)
            {
                cfgFilter.Minimum       = filter.Minimum;
                cfgFilter.Maximum       = filter.Maximum;
                cfgFilter.DefaultValue  = filter.Value;
                cfgFilter.Value         = filter.Value;
                cfgFilter.Step          = filter.Step;
            }

            UpdateFilterValue(cfgFilter);
        }

        configLoadedChecked = true;
        UpdateBackgroundColor();

        vc?.VideoProcessorSetStreamAutoProcessingMode(vp, 0, false);

        // Reset FLVP filters to defaults (can be different from D3D11VP filters scaling)
        if (videoProcessor == VideoProcessors.Flyleaf)
        {
            Config.Video.Filters[VideoFilters.Brightness].Value = Config.Video.Filters[VideoFilters.Brightness].Minimum + ((Config.Video.Filters[VideoFilters.Brightness].Maximum - Config.Video.Filters[VideoFilters.Brightness].Minimum) / 2);
            Config.Video.Filters[VideoFilters.Contrast].Value   = Config.Video.Filters[VideoFilters.Contrast].  Minimum + ((Config.Video.Filters[VideoFilters.Contrast].  Maximum - Config.Video.Filters[VideoFilters.Contrast].  Minimum) / 2);
        }
    }

    internal void UpdateBackgroundColor()
    {
        D3D11VPBackgroundColor.Rgba.R = Scale(Config.Video.BackgroundColor.R, 0, 255, 0, 100) / 100.0f;
        D3D11VPBackgroundColor.Rgba.G = Scale(Config.Video.BackgroundColor.G, 0, 255, 0, 100) / 100.0f;
        D3D11VPBackgroundColor.Rgba.B = Scale(Config.Video.BackgroundColor.B, 0, 255, 0, 100) / 100.0f;

        vc?.VideoProcessorSetOutputBackgroundColor(vp, false, D3D11VPBackgroundColor);

        Present();
    }
    internal void UpdateDeinterlace()
    {
        lock (lockDevice)
        {
            if (Disposed || parent != null)
                return;

            FieldType = Config.Video.DeInterlace == DeInterlace.Auto ? (VideoStream != null ? VideoStream.FieldOrder : DeInterlace.Progressive) : Config.Video.DeInterlace;
            vc?.VideoProcessorSetStreamFrameFormat(vp, 0, FieldType == DeInterlace.Progressive ? VideoFrameFormat.Progressive : (FieldType == DeInterlace.BottomField ? VideoFrameFormat.InterlacedBottomFieldFirst : VideoFrameFormat.InterlacedTopFieldFirst));
            psBufferData.fieldType = FieldType;
            
            var fieldType = Config.Video.DeInterlace == DeInterlace.Auto ? VideoStream.FieldOrder : Config.Video.DeInterlace;
            var newVp = !D3D11VPFailed && VideoDecoder.VideoAccelerated &&
                (Config.Video.VideoProcessor == VideoProcessors.D3D11 || (fieldType != DeInterlace.Progressive && Config.Video.VideoProcessor != VideoProcessors.Flyleaf)) ?
                VideoProcessors.D3D11 : VideoProcessors.Flyleaf;

            if (newVp != VideoProcessor)
                ConfigPlanes();
            else
                context.UpdateSubresource(psBufferData, psBuffer);

            Present();
        }
    }
    internal void SetFieldType(DeInterlace fieldType)
    {
        lock (lockDevice)
        {
            if (Disposed)
                return;

            //vpsa[0].InputFrameOrField = fieldType == DeInterlace.BottomField ? 1u : 0u; // TBR
            vc?.VideoProcessorSetStreamFrameFormat(vp, 0, fieldType == DeInterlace.Progressive ? VideoFrameFormat. Progressive : (fieldType == DeInterlace.BottomField ? VideoFrameFormat.InterlacedBottomFieldFirst : VideoFrameFormat.InterlacedTopFieldFirst));
            psBufferData.fieldType = fieldType;
            context.UpdateSubresource(psBufferData, psBuffer);
        }
    }
    internal void UpdateFilterValue(VideoFilter filter)
    {
        // D3D11VP
        if (Filters.TryGetValue(filter.Filter, out var filterFilter) && vc != null)
        {
            int scaledValue = (int) Scale(filter.Value, filter.Minimum, filter.Maximum, filterFilter.Minimum, filterFilter.Maximum);
            vc.VideoProcessorSetStreamFilter(vp, 0, ConvertFromVideoProcessorFilterCaps((VideoProcessorFilterCaps)filter.Filter), true, scaledValue);
        }

        if (parent != null)
            return;

        // FLVP
        switch (filter.Filter)
        {
            case VideoFilters.Brightness:
                int scaledValue = (int) Scale(filter.Value, filter.Minimum, filter.Maximum, 0, 100);
                psBufferData.brightness = scaledValue / 100.0f;
                context.UpdateSubresource(psBufferData, psBuffer);
                break;

            case VideoFilters.Contrast:
                scaledValue = (int) Scale(filter.Value, filter.Minimum, filter.Maximum, 0, 100);
                psBufferData.contrast = scaledValue / 100.0f;
                context.UpdateSubresource(psBufferData, psBuffer);
                break;

            case VideoFilters.Hue:
                psBufferData.hue = Scale(filter.Value, filter.Minimum, filter.Maximum, -3.14f, 3.14f);
                context.UpdateSubresource(psBufferData, psBuffer);
                break;

            case VideoFilters.Saturation:
                psBufferData.saturation = Scale(filter.Value, filter.Minimum, filter.Maximum, 0, 2);
                context.UpdateSubresource(psBufferData, psBuffer);
                break;
        }

        Present();
    }
    internal void UpdateHDRtoSDR(bool updateResource = true)
    {
        if(parent != null)
            return;

        psBufferData.tonemap = Config.Video.HDRtoSDRMethod;

        if (psBufferData.tonemap == HDRtoSDRMethod.Hable)
            psBufferData.hdrtone = 10_000f / Config.Video.SDRDisplayNits;
        else if (psBufferData.tonemap == HDRtoSDRMethod.Reinhard)
            psBufferData.hdrtone = (10_000f / Config.Video.SDRDisplayNits) / 2f;
        else if (psBufferData.tonemap == HDRtoSDRMethod.Aces)
            psBufferData.hdrtone = (10_000f / Config.Video.SDRDisplayNits) / 7f;

        if (updateResource)
        {
            context.UpdateSubresource(psBufferData, psBuffer);
            Present();
        }
    }
    void UpdateRotation(uint angle, bool refresh = true)
    {
        _RotationAngle = angle;

        uint newRotation = _RotationAngle;

        if (VideoStream != null)
            newRotation += (uint)VideoStream.Rotation;

        if (rotationLinesize)
            newRotation += 180;

        newRotation %= 360;

        if (Disposed || (actualRotation == newRotation && actualHFlip == _HFlip && actualVFlip == _VFlip))
            return;

        bool hvFlipChanged = (actualHFlip || actualVFlip) != (_HFlip || _VFlip);

        actualRotation  = newRotation;
        actualHFlip     = _HFlip;
        actualVFlip     = _VFlip;

        if (actualRotation < 45 || actualRotation == 360)
            _d3d11vpRotation = VideoProcessorRotation.Identity;
        else if (actualRotation < 135)
            _d3d11vpRotation = VideoProcessorRotation.Rotation90;
        else if (actualRotation < 225)
            _d3d11vpRotation = VideoProcessorRotation.Rotation180;
        else if (actualRotation < 360)
            _d3d11vpRotation = VideoProcessorRotation.Rotation270;

        vsBufferData.mat = Matrix4x4.CreateFromYawPitchRoll(0.0f, 0.0f, (float) (Math.PI / 180 * actualRotation));

        if (_HFlip || _VFlip)
        {
            vsBufferData.mat *= Matrix4x4.CreateScale(_HFlip ? -1 : 1, _VFlip ? -1 : 1, 1);
            if (hvFlipChanged)
            {
                // Renders both sides required for H-V Flip - TBR: consider for performance changing the vertex buffer / input layout instead?
                rasterizerState?.Dispose();
                rasterizerState = Device.CreateRasterizerState(new(CullMode.None, FillMode.Solid));
                context.RSSetState(rasterizerState);
            }
        }
        else if (hvFlipChanged)
        {
            // Removes back rendering for better performance
            rasterizerState?.Dispose();
            rasterizerState = Device.CreateRasterizerState(new(CullMode.Back, FillMode.Solid));
            context.RSSetState(rasterizerState);
        }

        if (parent == null)
            context.UpdateSubresource(vsBufferData, vsBuffer);

        if (child != null)
        {
            child.actualRotation    = actualRotation;
            child._d3d11vpRotation  = _d3d11vpRotation;
            child._RotationAngle    = _RotationAngle;
            child.rotationLinesize  = rotationLinesize;
            child.SetViewport();
        }

        vc?.VideoProcessorSetStreamRotation(vp, 0, true, _d3d11vpRotation);

        if (refresh)
            SetViewport();
    }
    internal void UpdateVideoProcessor()
    {
        if(parent != null)
            return;

        if (Config.Video.VideoProcessor == videoProcessor || (Config.Video.VideoProcessor == VideoProcessors.D3D11 && D3D11VPFailed))
            return;

        ConfigPlanes();
        Present();
    }
}

public class VideoFilter : NotifyPropertyChanged
{
    internal Renderer renderer;

    [JsonIgnore]
    public bool         Available   { get => _Available;    set => SetUI(ref _Available, value); }
    bool _Available;

    public VideoFilters Filter      { get => _Filter;       set => SetUI(ref _Filter, value); }
    VideoFilters _Filter = VideoFilters.Brightness;

    public int          Minimum     { get => _Minimum;      set => SetUI(ref _Minimum, value); }
    int _Minimum = 0;

    public int          Maximum     { get => _Maximum;      set => SetUI(ref _Maximum, value); }
    int _Maximum = 100;

    public float        Step        { get => _Step;         set => SetUI(ref _Step, value); }
    float _Step = 1;

    public int          DefaultValue{ get => _DefaultValue; set => SetUI(ref _DefaultValue, value); }
    int _DefaultValue = 50;

    public int          Value       { get => _Value;        set { if (Set(ref _Value, value)) renderer?.UpdateFilterValue(this); } }
    int _Value = 50;

    internal void SetValue(int value) => SetUI(ref _Value, value, true, nameof(Value));

    public VideoFilter() { }
    public VideoFilter(VideoFilters filter)
        => Filter = filter;
}

public class VideoProcessorCapsCache
{
    public bool Failed      = true;
    public int  TypeIndex   = -1;
    public bool HLG;
    public bool HDR10;
    public VideoProcessorCaps               VideoProcessorCaps;
    public VideoProcessorRateConversionCaps VideoProcessorRateConversionCaps;

    public Dictionary<VideoFilters, VideoFilter> Filters { get; set; } = [];
}
