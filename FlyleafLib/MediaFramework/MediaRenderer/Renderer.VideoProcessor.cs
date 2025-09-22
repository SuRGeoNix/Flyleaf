using System.Numerics;
using System.Runtime.InteropServices;

using Vortice.Direct3D11;
using Vortice.DXGI;

using ID3D11VideoContext = Vortice.Direct3D11.ID3D11VideoContext;

namespace FlyleafLib.MediaFramework.MediaRenderer;

unsafe public partial class Renderer
{
    /* TODO
     * 1) Try to sync filters between Flyleaf and D3D11 video processors so we will not have to reset on change
     * 2) Filter default values will change when the device/adapter is changed
     */

    static Dictionary<string, VideoProcessorCapsCache> VideoProcessorsCapsCache = [];
    VideoProcessorCapsCache curVPCC;

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
    internal static VideoFilterLocal ConvertFromVideoProcessorFilterRange(VideoProcessorFilterRange filter) => new()
    {
        Minimum     = filter.Minimum,
        Maximum     = filter.Maximum,
        Default     = filter.Default,
        Step        = filter.Multiplier
    };

    [StructLayout(LayoutKind.Sequential)]
    struct SuperResNVidia(bool enable)
    {
        uint version = 0x1;
        uint method  = 0x2;
        uint enabled = enable ? 1u : 0u;
    }
    static SuperResNVidia   SuperResEnabledNVidia   = new(true);
    static SuperResNVidia   SuperResDisabledNVidia  = new(false);
    static Guid             GUID_SUPERRES_NVIDIA    = Guid.Parse("d43ce1b3-1f4b-48ac-baee-c3c25375e6f7");

    [StructLayout(LayoutKind.Sequential)]
    struct SuperResIntel
    {
        public IntelFunction    function;
        public IntPtr           param;
    }
    enum IntelFunction : uint
    {
        kIntelVpeFnVersion  = 0x01,
        kIntelVpeFnMode     = 0x20,
		kIntelVpeFnScaling  = 0x37
    }
    static Guid             GUID_SUPERRES_INTEL     = Guid.Parse("edd1d4b9-8659-4cbc-a4d6-9831a2163ac3");

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

    bool InitializeVideoProcessor()
    {
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

            lock (VideoProcessorsCapsCache)
            {
                if (VideoProcessorsCapsCache.TryGetValue(Device.Tag.ToString(), out curVPCC))
                {
                    while (curVPCC.Wait)
                        Thread.Sleep(10);

                    if (curVPCC.Failed)
                        return false;

                    vd1 = Device.QueryInterface<ID3D11VideoDevice1>();
                    vc  = context.QueryInterface<ID3D11VideoContext1>();

                    vd1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);

                    if (vpe == null)
                        return false;

                    vd1.CreateVideoProcessor(vpe, (uint)curVPCC.TypeIndex, out vp);

                    return true;
                }

                curVPCC = new();
                VideoProcessorsCapsCache.Add(Device.Tag.ToString(), curVPCC);
            }

            vd1 = Device.QueryInterface<ID3D11VideoDevice1>();
            vc  = context.QueryInterface<ID3D11VideoContext>();

            vd1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);
            
            if (vpe == null || Device.FeatureLevel < Vortice.Direct3D.FeatureLevel.Level_10_0)
                return false;

            var vpe1    = vpe.QueryInterface<ID3D11VideoProcessorEnumerator1>();
            var vpCaps  = vpe.VideoProcessorCaps;
            string dump = "";

            if (CanDebug)
            {
                var hlg     = vpe1.CheckVideoProcessorFormatConversion(Format.P010, ColorSpaceType.YcbcrStudioGhlgTopLeftP2020,  Format.B8G8R8A8_UNorm, ColorSpaceType.RgbFullG22NoneP709);
                var hdr10   = vpe1.CheckVideoProcessorFormatConversion(Format.P010, ColorSpaceType.YcbcrStudioG2084TopLeftP2020, Format.B8G8R8A8_UNorm, ColorSpaceType.RgbStudioG2084NoneP2020);

                dump += $"=====================================================\r\n";
                dump += $"MaxInputStreams           {vpCaps.MaxInputStreams}\r\n";
                dump += $"MaxStreamStates           {vpCaps.MaxStreamStates}\r\n";
                dump += $"HDR10 Limited             {(hlg   ? "yes" : "no")}\r\n";
                dump += $"HLG                       {(hdr10 ? "yes" : "no")}\r\n";

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
                    curVPCC.Filters.Add((VideoFilters)filter, vf);
                }
                else if (CanDebug)
                    dump += $"{filter,-25} no\r\n";

            if (CanDebug)
            {
                dump += $"\n[Video Processor Auto Stream Caps]\r\n";
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

            curVPCC.TypeIndex = (int)typeIndex;
            vd1.CreateVideoProcessor(vpe, typeIndex, out vp);
            if (vp == null)
                return false;

            curVPCC.Failed = false;
            Log.Info($"D3D11 Video Processor Initialized (Rate Caps #{typeIndex})");

            return true;

        }
        catch { return false; }
        finally
        {
            curVPCC.Wait = false;

            if (curVPCC.Failed)
            {
                Log.Error($"D3D11 Video Processor Initialization Failed");
                DisposeVideoProcessor();
            }
            else
            {
                UpdateBackgroundColor();
                vc.VideoProcessorSetStreamAutoProcessingMode(vp, 0, false);
                vc.VideoProcessorSetStreamFrameFormat(vp, 0, FieldType);

                if (Config.Video.SuperResolutionNVidia)
                    UpdateSuperResNVidia(true);

                if (Config.Video.SuperResolutionIntel)
                    UpdateSuperResIntel(true);
            }

            lock (Config.Video.lockFilters)
                lock (Config.Video.D3Filters)
                    SetupFilters();
        }
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

    VideoProcessors GetVP()
    {
        var fieldType = Config.Video.DeInterlace == DeInterlace.Auto ? VideoStream.FieldOrder : (VideoFrameFormat)Config.Video.DeInterlace;

        if (VideoDecoder.VideoAccelerated && VideoStream.ColorSpace != ColorSpace.Bt2020 && vc != null && (
                Config.Video.VideoProcessor == VideoProcessors.D3D11 ||
                Config.Video.SuperResolutionNVidia || Config.Video.SuperResolutionIntel ||
                (fieldType != VideoFrameFormat.Progressive && Config.Video.VideoProcessor == VideoProcessors.Auto)))
        {
            FieldType = fieldType;
            vc.VideoProcessorSetStreamFrameFormat(vp, 0, FieldType);
            return VideoProcessors.D3D11;
        }

        FieldType = VideoFrameFormat.Progressive;
        return VideoProcessors.Flyleaf;
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
            if (Disposed || VideoStream == null || parent != null)
                return;

            if (GetVP() != VideoProcessor)
                ConfigPlanes();
        }
    }

    internal void SetFieldType(VideoFrameFormat fieldType)
    {
        lock (lockDevice)
            if (!Disposed)
                vc.VideoProcessorSetStreamFrameFormat(vp, 0, fieldType);
                //TBR: vpsa[0].InputFrameOrField = fieldType == DeInterlace.BottomField ? 1u : 0u; // TBR
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
        }

        vc?.VideoProcessorSetStreamRotation(vp, 0, true, _d3d11vpRotation);

        UpdateAspectRatio(refresh);
    }

    internal void UpdateAspectRatio(bool refresh = true)
    {
        lock (lockDevice) // TBR: Fix AspectRatio generally* Separate Enum + CustomValue (respect SAR, clarify Fit/Fill/Keep/Stretch/Original/Custom ...)
        {
            if (Config.Video.AspectRatio == AspectRatio.Keep)
                curRatio = keepRatio;
            else if (Config.Video.AspectRatio == AspectRatio.Fill)
                curRatio = fillRatio;
            else if (Config.Video.AspectRatio == AspectRatio.Custom)
                curRatio = Config.Video.CustomAspectRatio.Value;
            else
                curRatio = Config.Video.AspectRatio.Value;

            if (actualRotation == 90 || actualRotation == 270)
                curRatio = 1 / curRatio;

            if (refresh)
                SetViewport();

            child?.UpdateAspectRatio(refresh);
        }
    }

    internal void UpdateCropping(bool refresh = true)
    {
        /* TODO (SW)
         * 
         * 1) Visible vs Padded
         *  When we fix texture arrays and texture width/height to use padded properly,
         *  we will need to ensure cropping pixels are in the same logic with vertex shader
         * 
         * 2) Chroma Location + Crop Calculation/Adjustments
         *  PSInput additional uv coords (both vertex/pixel shaders) and use new coords for sampling
         *      float2 Texture  : TEXCOORD;
         *  
         *  e.g. UV Chroma (Semi-Planar) Crop +- (0.5f / (W|H / 2f))?
         */

        lock (lockDevice)
        {
            cropRect = VideoStream.cropRect;
            
            if (Config.Video.HasUserCrop)
            {
                cropRect.Top    += Config.Video._Crop.Top;
                cropRect.Left   += Config.Video._Crop.Left;
                cropRect.Right  += Config.Video._Crop.Right;
                cropRect.Bottom += Config.Video._Crop.Bottom;
            }

            vsBufferData.cropRegion = new()
            {
                X = cropRect.Left / (float)textWidth,
                Y = cropRect.Top  / (float)textHeight,
                Z = (textWidth  - cropRect.Right)  / (float)textWidth, //1.0f - (right  / (float)textWidth),
                W = (textHeight - cropRect.Bottom) / (float)textHeight //1.0f - (bottom / (float)textHeight)
            };

            if (parent == null)
                context.UpdateSubresource(vsBufferData, vsBuffer);

            VisibleWidth    = textWidth  - (cropRect.Left + cropRect.Right);
            VisibleHeight   = textHeight - (cropRect.Top  + cropRect.Bottom);

            int x, y;
            _ = av_reduce(&x, &y, VisibleWidth * VideoStream.SAR.Num, VisibleHeight * VideoStream.SAR.Den, 1024 * 1024);
            DAR = new(x, y);
            keepRatio = DAR.Value;
            
            if (Config.Video.AspectRatio == AspectRatio.Keep)
                curRatio = actualRotation == 90 || actualRotation == 270 ? 1 / keepRatio : keepRatio;

            if (refresh)
                SetViewport();
        }
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

    internal void UpdateSuperResNVidia(bool enabled)
    {
        if (vc == null)
            return;

        try
        {
            if (enabled)
                fixed (SuperResNVidia* ptr = &SuperResEnabledNVidia)
                    vc.VideoProcessorSetStreamExtension(vp, 0, GUID_SUPERRES_NVIDIA, (uint)sizeof(SuperResNVidia), (nint)ptr);
            else
                fixed (SuperResNVidia* ptr = &SuperResDisabledNVidia)
                    vc.VideoProcessorSetStreamExtension(vp, 0, GUID_SUPERRES_NVIDIA, (uint)sizeof(SuperResNVidia), (nint)ptr);
        } catch (Exception e) { Log.Error($"UpdateNVidiaSuperRes() failed: {e.Message}"); } // Never fails?*
    }

    internal unsafe void UpdateSuperResIntel(bool enabled)
    {
        if (vc == null)
            return;

        try
        {
            IntPtr          paramPtr    = Marshal.AllocHGlobal(sizeof(uint));
            SuperResIntel   intel       = new() { param = paramPtr };
            GCHandle        handle      = GCHandle.Alloc(intel, GCHandleType.Pinned);
            
            intel.function = IntelFunction.kIntelVpeFnVersion;
            Marshal.WriteInt32(paramPtr, 3); // kIntelVpeVersion3
            vc.VideoProcessorSetOutputExtension(vp,     GUID_SUPERRES_INTEL, (uint)sizeof(SuperResIntel), handle.AddrOfPinnedObject());

            intel.function = IntelFunction.kIntelVpeFnMode;
            Marshal.WriteInt32(paramPtr, enabled ? 1 : 0); // kIntelVpeModePreproc : kIntelVpeModeNone
            vc.VideoProcessorSetOutputExtension(vp,     GUID_SUPERRES_INTEL, (uint)sizeof(SuperResIntel), handle.AddrOfPinnedObject());

            intel.function = IntelFunction.kIntelVpeFnScaling;
            Marshal.WriteInt32(paramPtr, enabled ? 2 : 0); // kIntelVpeScalingSuperResolution : kIntelVpeScalingDefault
            vc.VideoProcessorSetStreamExtension(vp, 0,  GUID_SUPERRES_INTEL, (uint)sizeof(SuperResIntel), handle.AddrOfPinnedObject());

            handle.Free();
            Marshal.FreeHGlobal(paramPtr);
        } catch (Exception e) { Log.Error($"UpdateIntelSuperRes() failed: {e.Message}"); } // Never fails?*
    }
}

internal class VideoProcessorCapsCache
{
    public bool Wait        = true; // to avoid locking until init
    public bool Failed      = true;
    public int  TypeIndex   = -1;

    public Dictionary<VideoFilters, VideoFilterLocal> Filters { get; set; } = [];
}
