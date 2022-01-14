using System;
using System.Xml.Serialization;

using Vortice.DXGI;
using Vortice.Direct3D11;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public partial class Renderer
    {
        public bool             D3D11VPFailed       => vc == null; //Device == null || VideoProcessorsCapsCache == null || !VideoProcessorsCapsCache.ContainsKey(Device.Tag.ToString()) || VideoProcessorsCapsCache[Device.Tag.ToString()].Failed;
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
                        DisposeVideoProcessor();
                        InitializeFilters();

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

                if (vpe == null)
                {
                    DisposeVideoProcessor();
                    InitializeFilters();

                    return;
                }

                ID3D11VideoProcessorEnumerator1 vpe1 = vpe.QueryInterface<ID3D11VideoProcessorEnumerator1>();
                bool supportHLG = vpe1.CheckVideoProcessorFormatConversion(Format.P010, ColorSpaceType.YcbcrStudioGhlgTopLeftP2020, Format.B8G8R8A8_UNorm, ColorSpaceType.RgbFullG22NoneP709);
                bool supportHDR10Limited = vpe1.CheckVideoProcessorFormatConversion(Format.P010, ColorSpaceType.YcbcrStudioG2084TopLeftP2020, Format.B8G8R8A8_UNorm, ColorSpaceType.RgbStudioG2084NoneP2020);

                VideoProcessorCaps vpCaps = vpe.VideoProcessorCaps;

                Log($"[Video Processor Caps]");
                Log($"RateConversionCapsCount   {vpCaps.RateConversionCapsCount}");
                Log($"FeatureCaps               {vpCaps.FeatureCaps}");
                Log($"DeviceCaps                {vpCaps.DeviceCaps}");
                Log($"InputFormatCaps           {vpCaps.InputFormatCaps}");
                Log($"MaxInputStream            {vpCaps.MaxInputStreams}");
                Log($"MaxStreamStates           {vpCaps.MaxStreamStates}");

                Log($"YCbCr matrix conversion   {(vpCaps.DeviceCaps     & VideoProcessorDeviceCaps.YCbCrMatrixConversion) != 0}");
                Log($"YUV nominal range         {(vpCaps.DeviceCaps     & VideoProcessorDeviceCaps.NominalRange) != 0}");

                Log($"Shader Usage              {(vpCaps.FeatureCaps    & VideoProcessorFeatureCaps.ShaderUsage) != 0}");
                Log($"HDR10                     {(vpCaps.FeatureCaps    & VideoProcessorFeatureCaps.MetadataHdr10) != 0}");
                Log($"HDR10 Limited             {supportHDR10Limited}");
                Log($"HLG                       {supportHLG}");
                Log($"Legacy                    {(vpCaps.FeatureCaps    & VideoProcessorFeatureCaps.Legacy) != 0}");

                VideoProcessorFilterRange range;
                var available = Enum.GetValues(typeof(VideoProcessorFilterCaps));

                foreach (VideoProcessorFilterCaps filter in available)
                    if ((vpCaps.FilterCaps & filter) != 0)
                    {
                        vpe.GetVideoProcessorFilterRange(ConvertFromVideoProcessorFilterCaps(filter), out range);
                        Log($"{filter.ToString().PadRight(25, ' ')} [{range.Minimum} - {range.Maximum}] | x{range.Multiplier} | *{range.Default}");
                        VideoFilter vf = ConvertFromVideoProcessorFilterRange(range);
                        vf.Filter = (VideoFilters)filter;
                        cache.Filters.Add((VideoFilters)filter, vf);
                    }

                Log($"PaletteInterlaced         {(vpCaps.InputFormatCaps& VideoProcessorFormatCaps.PaletteInterlaced) != 0}");
                Log($"RgbInterlaced             {(vpCaps.InputFormatCaps& VideoProcessorFormatCaps.RgbInterlaced) != 0}");
                Log($"RgbLumaKey                {(vpCaps.InputFormatCaps& VideoProcessorFormatCaps.RgbLumaKey) != 0}");
                Log($"RgbProcamp                {(vpCaps.InputFormatCaps& VideoProcessorFormatCaps.RgbProcamp) != 0}");
            
                var asCaps = vpCaps.AutoStreamCaps;
                if (asCaps > 0)
                {
                    Log("... AutoStreamCaps ...");
                    Log($"Denoise               {(asCaps & VideoProcessorAutoStreamCaps.Denoise) != 0}");
                    Log($"Deringing             {(asCaps & VideoProcessorAutoStreamCaps.Deringing) != 0}");
                    Log($"EdgeEnhancement       {(asCaps & VideoProcessorAutoStreamCaps.EdgeEnhancement) != 0}");
                    Log($"ColorCorrection       {(asCaps & VideoProcessorAutoStreamCaps.ColorCorrection) != 0}");
                    Log($"FleshToneMapping      {(asCaps & VideoProcessorAutoStreamCaps.FleshToneMapping) != 0}");
                    Log($"ImageStabilization    {(asCaps & VideoProcessorAutoStreamCaps.ImageStabilization) != 0}");
                    Log($"SuperResolution       {(asCaps & VideoProcessorAutoStreamCaps.SuperResolution) != 0}");
                    Log($"AnamorphicScaling     {(asCaps & VideoProcessorAutoStreamCaps.AnamorphicScaling) != 0}");
                }

                int typeIndex = -1;
                VideoProcessorRateConversionCaps rcCap = new VideoProcessorRateConversionCaps();
                for (int i = 0; i < vpCaps.RateConversionCapsCount; i++)
                {
                    vpe.GetVideoProcessorRateConversionCaps(i, out rcCap);
                    VideoProcessorProcessorCaps pCaps = (VideoProcessorProcessorCaps) rcCap.ProcessorCaps;
                    Log($"[Video Processor #{i+1} Caps]");
                    Log($"CustomRateCount           {rcCap.CustomRateCount}");
                    Log($"PastFrames                {rcCap.PastFrames}");
                    Log($"FutureFrames              {rcCap.FutureFrames}");
                    Log($"DeinterlaceBlend          {(pCaps & VideoProcessorProcessorCaps.DeinterlaceBlend) != 0}");
                    Log($"DeinterlaceBob            {(pCaps & VideoProcessorProcessorCaps.DeinterlaceBob) != 0}");
                    Log($"DeinterlaceAdaptive       {(pCaps & VideoProcessorProcessorCaps.DeinterlaceAdaptive) != 0}");
                    Log($"DeinterlaceMotion         {(pCaps & VideoProcessorProcessorCaps.DeinterlaceMotionCompensation) != 0}");
                    Log($"FrameRateConversion       {(pCaps & VideoProcessorProcessorCaps.FrameRateConversion) != 0}");
                    Log($"InverseTelecine           {(pCaps & VideoProcessorProcessorCaps.InverseErseTelecine) != 0}");

                    typeIndex = i;

                    if ((pCaps & VideoProcessorProcessorCaps.DeinterlaceBob) != 0)
                        break; // TBR: When we add past/future frames support
                }
                vpe1.Dispose();

                cache.TypeIndex = typeIndex;
                cache.HLG = supportHLG;
                cache.HDR10Limited = supportHDR10Limited;
                cache.VideoProcessorCaps = vpCaps;
                cache.VideoProcessorRateConversionCaps = rcCap;

                //if (typeIndex != -1)
                vd1.CreateVideoProcessor(vpe, typeIndex, out vp);
                if (vp == null)
                {
                    DisposeVideoProcessor();
                    InitializeFilters();

                    return;

                }

                cache.Failed = false;

            } catch { DisposeVideoProcessor(); }

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
                    cfgFilter.Minimum   = filter.Minimum;
                    cfgFilter.Maximum   = filter.Maximum;
                    cfgFilter.Value     = filter.Value;
                    cfgFilter.Step      = filter.Step;
                }

                UpdateFilterValue(cfgFilter);
            }

            configLoadedChecked = true;
            UpdateBackgroundColor();

            vc.VideoProcessorSetStreamAutoProcessingMode(vp, 0, false);
            vc.VideoProcessorSetStreamFrameFormat(vp, 0, !Config.Video.Deinterlace ? VideoFrameFormat.Progressive : (Config.Video.DeinterlaceBottomFirst ? VideoFrameFormat.InterlacedBottomFieldFirst : VideoFrameFormat.InterlacedTopFieldFirst));
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
                    return;
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

        public VideoFilters Filter      { get => _Filter;   set => SetUI(ref _Filter, value); }
        VideoFilters _Filter = VideoFilters.Brightness;
        public int          Minimum     { get => _Minimum;  set => SetUI(ref _Minimum, value); }
        int _Minimum = 0;
        public int          Maximum     { get => _Maximum;  set => SetUI(ref _Maximum, value); }
        int _Maximum = 100;
        public float        Step        { get => _Step;     set => SetUI(ref _Step, value); }
        float _Step = 1;
        public int          Value       { get => _Value;    set { if (Set(ref _Value, value)) player?.renderer?.UpdateFilterValue(this); } }
        int _Value = 50;
        internal void SetValue(int value)
        {
            if (_Value == value)
                return;

            _Value = value;

            RaiseUI(nameof(Value));
        }

        [XmlIgnore]
        public bool         Available   { get => _Available;set => SetUI(ref _Available, value); }
        bool _Available;

        public VideoFilter() { }
        public VideoFilter(VideoFilters filter, Player player = null) { Filter = filter; }
    }
}
