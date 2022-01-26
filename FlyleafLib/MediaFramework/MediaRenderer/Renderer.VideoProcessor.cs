using System;
using System.Reflection;
using System.Xml.Serialization;

using Vortice.DXGI;
using Vortice.Direct3D11;

using FlyleafLib.MediaPlayer;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public partial class Renderer
    {
        public bool             D3D11VPFailed       => vc == null;
        public VideoProcessors  VideoProcessor      { get => videoProcessor;    private set { SetUI(ref _VideoProcessor, value); videoProcessor = value; } }
        VideoProcessors _VideoProcessor = VideoProcessors.Flyleaf, videoProcessor = VideoProcessors.Flyleaf;

        public bool  IsHDR                          { get => isHDR;             private set { SetUI(ref _IsHDR, value); isHDR = value; } }
        bool _IsHDR, isHDR;

        VideoColor                          D3D11VPBackgroundColor;
        ID3D11VideoDevice1                  vd1;
        ID3D11VideoProcessor                vp;
        ID3D11VideoContext                  vc;
        ID3D11VideoProcessorEnumerator      vpe;
        ID3D11VideoProcessorInputView       vpiv;
        ID3D11VideoProcessorOutputView      vpov;

        VideoProcessorStream[]              vpsa    = new VideoProcessorStream[] { new VideoProcessorStream() { Enable = true } };
        VideoProcessorContentDescription    vpcd    = new VideoProcessorContentDescription()
            {
                Usage = VideoUsage.PlaybackNormal,
                InputFrameFormat = VideoFrameFormat.InterlacedTopFieldFirst,

                InputFrameRate  = new Rational(1, 1),
                OutputFrameRate = new Rational(1, 1),
            };
        VideoProcessorOutputViewDescription vpovd   = new VideoProcessorOutputViewDescription() { ViewDimension = VideoProcessorOutputViewDimension.Texture2D };
        VideoProcessorInputViewDescription  vpivd   = new VideoProcessorInputViewDescription()
            {
                FourCC          = 0,
                ViewDimension   = VideoProcessorInputViewDimension.Texture2D,
                Texture2D       = new Texture2DVideoProcessorInputView() { MipSlice = 0, ArraySlice = 0 }
            };

        bool configLoadedChecked;

        internal void InitializeVideoProcessor()
        {
            try
            {
                if (VideoDecoder.VideoStream != null)
                {
                    vpcd.InputWidth = VideoDecoder.VideoStream.Width;
                    vpcd.InputHeight= VideoDecoder.VideoStream.Height;
                }
                else if (Control != null)
                {
                    vpcd.InputWidth = Control.Width;
                    vpcd.InputHeight= Control.Height;
                }
                else
                {
                    vpcd.InputWidth = 1280;
                    vpcd.InputHeight= 720;
                }
                
                vpcd.OutputWidth = vpcd.InputWidth;
                vpcd.OutputHeight= vpcd.InputHeight;

                if (VideoProcessorsCapsCache.ContainsKey(Device.Tag.ToString()))
                {
                    if (VideoProcessorsCapsCache[Device.Tag.ToString()].Failed)
                    {
                        InitializeFilters();
                        return;
                    }

                    vd1 = Device.QueryInterface<ID3D11VideoDevice1>();
                    vc  = Device.ImmediateContext.QueryInterface<ID3D11VideoContext1>();

                    vd1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);

                    if (vpe == null)
                    {
                        VPFailed();
                        return;
                    }

                    // if (!VideoProcessorsCapsCache[Device.Tag.ToString()].TypeIndex != -1)
                    vd1.CreateVideoProcessor(vpe, VideoProcessorsCapsCache[Device.Tag.ToString()].TypeIndex, out vp);
                    InitializeFilters();

                    return;
                }

                VideoProcessorCapsCache cache = new VideoProcessorCapsCache();
                VideoProcessorsCapsCache.Add(Device.Tag.ToString(), cache);

                vd1 = Device.QueryInterface<ID3D11VideoDevice1>();
                vc  = Device.ImmediateContext.QueryInterface<ID3D11VideoContext>();

                vd1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);

                if (vpe == null || Device.FeatureLevel < Vortice.Direct3D.FeatureLevel.Level_10_0)
                {
                    VPFailed();
                    return;
                }

                ID3D11VideoProcessorEnumerator1 vpe1 = vpe.QueryInterface<ID3D11VideoProcessorEnumerator1>();
                bool supportHLG = vpe1.CheckVideoProcessorFormatConversion(Format.P010, ColorSpaceType.YcbcrStudioGhlgTopLeftP2020, Format.B8G8R8A8_UNorm, ColorSpaceType.RgbFullG22NoneP709);
                bool supportHDR10Limited = vpe1.CheckVideoProcessorFormatConversion(Format.P010, ColorSpaceType.YcbcrStudioG2084TopLeftP2020, Format.B8G8R8A8_UNorm, ColorSpaceType.RgbStudioG2084NoneP2020);

                VideoProcessorCaps vpCaps = vpe.VideoProcessorCaps;
                string dump = "";

                dump += $"=====================================================\r\n";
                dump += $"MaxInputStreams           {vpCaps.MaxInputStreams}\r\n";
                dump += $"MaxStreamStates           {vpCaps.MaxStreamStates}\r\n";
                dump += $"HDR10 Limited             {(supportHDR10Limited ? "yes" : "no")}\r\n";
                dump += $"HLG                       {(supportHLG ? "yes" : "no")}\r\n";

                dump += $"\n[Video Processor Device Caps]\r\n";
                foreach (VideoProcessorDeviceCaps cap in Enum.GetValues(typeof(VideoProcessorDeviceCaps)))
                    dump += $"{cap.ToString().PadRight(25, ' ')} {((vpCaps.DeviceCaps & cap) != 0 ? "yes" : "no")}\r\n";

                dump += $"\n[Video Processor Feature Caps]\r\n";
                foreach (VideoProcessorFeatureCaps cap in Enum.GetValues(typeof(VideoProcessorFeatureCaps)))
                    dump += $"{cap.ToString().PadRight(25, ' ')} {((vpCaps.FeatureCaps & cap) != 0 ? "yes" : "no")}\r\n";

                dump += $"\n[Video Processor Stereo Caps]\r\n";
                foreach (VideoProcessorStereoCaps cap in Enum.GetValues(typeof(VideoProcessorStereoCaps)))
                    dump += $"{cap.ToString().PadRight(25, ' ')} {((vpCaps.StereoCaps & cap) != 0 ? "yes" : "no")}\r\n";

                dump += $"\n[Video Processor Input Format Caps]\r\n";
                foreach (VideoProcessorFormatCaps cap in Enum.GetValues(typeof(VideoProcessorFormatCaps)))
                    dump += $"{cap.ToString().PadRight(25, ' ')} {((vpCaps.InputFormatCaps & cap) != 0 ? "yes" : "no")}\r\n";

                dump += $"\n[Video Processor Filter Caps]\r\n";
                foreach (VideoProcessorFilterCaps filter in Enum.GetValues(typeof(VideoProcessorFilterCaps)))
                    if ((vpCaps.FilterCaps & filter) != 0)
                    {
                        vpe1.GetVideoProcessorFilterRange(ConvertFromVideoProcessorFilterCaps(filter), out VideoProcessorFilterRange range);
                        dump += $"{filter.ToString().PadRight(25, ' ')} [{range.Minimum.ToString().PadLeft(6, ' ')} - {range.Maximum.ToString().PadLeft(4, ' ')}] | x{range.Multiplier.ToString().PadLeft(4, ' ')} | *{range.Default}\r\n";
                        VideoFilter vf = ConvertFromVideoProcessorFilterRange(range);
                        vf.Filter = (VideoFilters)filter;
                        cache.Filters.Add((VideoFilters)filter, vf);
                    }
                    else
                        dump += $"{filter.ToString().PadRight(25, ' ')} no\r\n";

                dump += $"\n[Video Processor Input Format Caps]\r\n";
                foreach (VideoProcessorAutoStreamCaps cap in Enum.GetValues(typeof(VideoProcessorAutoStreamCaps)))
                    dump += $"{cap.ToString().PadRight(25, ' ')} {((vpCaps.AutoStreamCaps & cap) != 0 ? "yes" : "no")}\r\n";

                int typeIndex = -1;
                VideoProcessorRateConversionCaps rcCap = new VideoProcessorRateConversionCaps();
                for (int i = 0; i < vpCaps.RateConversionCapsCount; i++)
                {
                    vpe.GetVideoProcessorRateConversionCaps(i, out rcCap);
                    VideoProcessorProcessorCaps pCaps = (VideoProcessorProcessorCaps) rcCap.ProcessorCaps;

                    dump += $"\n[Video Processor Rate Conversion Caps #{i}]\r\n";

                    dump += $"\n\t[Video Processor Rate Conversion Caps]\r\n";
                    FieldInfo[] fields = typeof(VideoProcessorRateConversionCaps).GetFields();
                    foreach (FieldInfo field in fields)
                        dump += $"\t{field.Name.PadRight(35, ' ')} {field.GetValue(rcCap)}\r\n";

                    dump += $"\n\t[Video Processor Processor Caps]\r\n";
                    foreach (VideoProcessorProcessorCaps cap in Enum.GetValues(typeof(VideoProcessorProcessorCaps)))
                        dump += $"\t{cap.ToString().PadRight(35, ' ')} {(((VideoProcessorProcessorCaps)rcCap.ProcessorCaps & cap) != 0 ? "yes" : "no")}\r\n";

                    typeIndex = i;

                    if (((VideoProcessorProcessorCaps)rcCap.ProcessorCaps & VideoProcessorProcessorCaps.DeinterlaceBob) != 0)
                        break; // TBR: When we add past/future frames support
                }
                vpe1.Dispose();

                if (CanDebug) Log.Debug($"D3D11 Video Processor\r\n{dump}");

                cache.TypeIndex = typeIndex;
                cache.HLG = supportHLG;
                cache.HDR10Limited = supportHDR10Limited;
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
        internal void VPFailed()
        {
            Log.Error($"D3D11 Video Processor Initialization Failed");

            if (!VideoProcessorsCapsCache.ContainsKey(Device.Tag.ToString()))
                VideoProcessorsCapsCache.Add(Device.Tag.ToString(), new VideoProcessorCapsCache());
            VideoProcessorsCapsCache[Device.Tag.ToString()].Failed = true;

            VideoProcessorsCapsCache[Device.Tag.ToString()].Filters.Add(VideoFilters.Brightness, new VideoFilter()  {  Filter = VideoFilters.Brightness });
            VideoProcessorsCapsCache[Device.Tag.ToString()].Filters.Add(VideoFilters.Contrast, new VideoFilter()    {  Filter = VideoFilters.Contrast });

            DisposeVideoProcessor();
            InitializeFilters();
        }
        internal void DisposeVideoProcessor()
        {
            vpiv?.Dispose();
            vpov?.Dispose();
            vp?.  Dispose();
            vpe?. Dispose();
            vc?.  Dispose();
            vd1?. Dispose();

            vc = null;
        }

        internal void InitializeFilters()
        {
            Filters = VideoProcessorsCapsCache[Device.Tag.ToString()].Filters;

            // Add FLVP filters if D3D11VP does not support them
            if (!Filters.ContainsKey(VideoFilters.Brightness))
                Filters.Add(VideoFilters.Brightness, new VideoFilter(VideoFilters.Brightness));

            if (!Filters.ContainsKey(VideoFilters.Contrast))
                Filters.Add(VideoFilters.Contrast, new VideoFilter(VideoFilters.Contrast));

            foreach(var filter in Filters.Values)
            {
                if (!Config.Video.Filters.ContainsKey(filter.Filter))
                    continue;

                var cfgFilter = Config.Video.Filters[filter.Filter];
                cfgFilter.Available = true;

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
            if (vc != null)
            {
                vc.VideoProcessorSetStreamAutoProcessingMode(vp, 0, false);
                vc.VideoProcessorSetStreamFrameFormat(vp, 0, !Config.Video.Deinterlace ? VideoFrameFormat.Progressive : (Config.Video.DeinterlaceBottomFirst ? VideoFrameFormat.InterlacedBottomFieldFirst : VideoFrameFormat.InterlacedTopFieldFirst));
            }            
        }
        internal void UpdateFilterValue(VideoFilter filter)
        {
            // D3D11VP
            if (Filters.ContainsKey(filter.Filter) && vc != null)
            {
                int scaledValue = (int)Utils.Scale(filter.Value, filter.Minimum, filter.Maximum, Filters[filter.Filter].Minimum, Filters[filter.Filter].Maximum);
                vc.VideoProcessorSetStreamFilter(vp, 0, ConvertFromVideoProcessorFilterCaps((VideoProcessorFilterCaps)filter.Filter), true, scaledValue);
            }

            // FLVP
            switch (filter.Filter)
            {
                case VideoFilters.Brightness:
                    int scaledValue = (int)Utils.Scale(filter.Value, filter.Minimum, filter.Maximum, 0, 100);
                    psBufferData.brightness = scaledValue / 100.0f;
                    context.UpdateSubresource(ref psBufferData, psBuffer);

                    break;

                case VideoFilters.Contrast:
                    scaledValue = (int)Utils.Scale(filter.Value, filter.Minimum, filter.Maximum, 0, 100);
                    psBufferData.contrast = scaledValue / 100.0f;
                    context.UpdateSubresource(ref psBufferData, psBuffer);

                    break;

                default:
                    break;
            }

            Present();
        }
        internal void UpdateBackgroundColor()
        {
            D3D11VPBackgroundColor.Rgba.R = Utils.Scale(Config.Video.BackgroundColor.R, 0, 255, 0, 100) / 100.0f;
            D3D11VPBackgroundColor.Rgba.G = Utils.Scale(Config.Video.BackgroundColor.G, 0, 255, 0, 100) / 100.0f;
            D3D11VPBackgroundColor.Rgba.B = Utils.Scale(Config.Video.BackgroundColor.B, 0, 255, 0, 100) / 100.0f;

            vc?.VideoProcessorSetOutputBackgroundColor(vp, false, D3D11VPBackgroundColor);

            Present();
        }
        internal void UpdateDeinterlace()
        {
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                vc?.VideoProcessorSetStreamFrameFormat(vp, 0, !Config.Video.Deinterlace ? VideoFrameFormat.Progressive : (Config.Video.DeinterlaceBottomFirst ? VideoFrameFormat.InterlacedBottomFieldFirst : VideoFrameFormat.InterlacedTopFieldFirst));

                if (Config.Video.VideoProcessor != VideoProcessors.Auto)
                    return;

                if (Config.Video.Deinterlace)
                {
                    if (videoProcessor == VideoProcessors.Flyleaf && !D3D11VPFailed && VideoDecoder.VideoAccelerated)
                    {
                        VideoProcessor = VideoProcessors.D3D11;
                        FrameResized();
                    }
                }
                else
                {
                    if (videoProcessor == VideoProcessors.D3D11 && isHDR)
                    {
                        VideoProcessor = VideoProcessors.Flyleaf;
                        FrameResized();
                    }
                }

                Present();
            }
        }
        internal void UpdateVideoProcessor()
        {
            if (Config.Video.VideoProcessor == videoProcessor || (Config.Video.VideoProcessor == VideoProcessors.D3D11 && D3D11VPFailed))
                return;

            FrameResized();
            Present();
        }
    }

    public class VideoFilter : NotifyPropertyChanged
    {
        internal Player player;

        [XmlIgnore]
        public bool         Available   { get => _Available;set => SetUI(ref _Available, value); }
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

        public int          Value       { get => _Value;        set { if (Set(ref _Value, value)) player?.renderer?.UpdateFilterValue(this); } }
        int _Value = 50;

        internal void SetValue(int value)
        {
            if (_Value == value)
                return;

            _Value = value;

            RaiseUI(nameof(Value));
        }

        public VideoFilter() { }
        public VideoFilter(VideoFilters filter, Player player = null) { Filter = filter; }
    }
}
