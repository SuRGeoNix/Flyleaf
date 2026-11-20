using System.Linq;

using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaStream;

using static FlyleafLib.Utils.NativeMethods;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    public event EventHandler ViewportChanged;

    /// <summary>Raised on the UI dispatcher after filter setup or processor reconfiguration.</summary>
    public event EventHandler FiltersChanged;

    public DisplayLuminanceSnapshot? GetDisplayLuminance()
    {
        lock (lockDevice)
        {
            if (D3Disposed || scfg == null) return null;
            return new(
                DisplayLuminanceRange.Resolve(-1, -1, autoSDRMinNits, autoSDRPeakNits, false),
                DisplayLuminanceRange.Resolve(ucfg.TargetSDRMinNits, ucfg.TargetSDRPeakNits, autoSDRMinNits, autoSDRPeakNits, false),
                DisplayLuminanceRange.Resolve(-1, -1, autoHDRMinNits, autoHDRPeakNits, true),
                DisplayLuminanceRange.Resolve(ucfg.TargetHDRMinNits, ucfg.TargetHDRPeakNits, autoHDRMinNits, autoHDRPeakNits, true),
                hdrSwapchain, VideoProcessor != VideoProcessors.D3D11 && UsesDisplayMapping,
                new(hdrData.TargetMinNits, hdrData.TargetPeakNits));
        }
    }

    /// <summary>Available source pixels for user cropping, after source/padding crop and before split-alpha layout.</summary>
    public (uint Width, uint Height) GetCropBounds()
    {
        lock (lockDevice)
        {
            if (D3Disposed || scfg == null) return (0, 0);
            var sourceWidth = VideoProcessor == VideoProcessors.SwsScale ? swsWidth : scfg.txtWidth;
            var sourceHeight = VideoProcessor == VideoProcessors.SwsScale ? swsHeight : scfg.txtHeight;
            var sourceCrop = VideoProcessor == VideoProcessors.SwsScale ? swsCrop : scfg.Crop;
            return ((uint)Math.Max(0, (long)sourceWidth - sourceCrop.Left - sourceCrop.Right),
                (uint)Math.Max(0, (long)sourceHeight - sourceCrop.Top - sourceCrop.Bottom));
        }
    }

    /// <summary>Captures the active processor and references to its existing configuration filters.</summary>
    public (VideoProcessors Processor, FLFilter[] FlyleafFilters, D3Filter[] D3D11Filters) GetVideoFilters()
    {
        lock (lockDevice)
        {
            if (D3Disposed || scfg == null) return (VideoProcessors.Auto, [], []);
            return (VideoProcessor, ucfg.FLFilters.Values.ToArray(), ucfg.D3Filters.Values.Where(f => f.Available).ToArray());
        }
    }

    void NotifyFiltersChanged()
    {
        if (FiltersChanged != null) UI(() => FiltersChanged?.Invoke(this, EventArgs.Empty));
    }

    public VideoProcessors  VideoProcessor  { get; private set; } = VideoProcessors.Auto; // Ensures we catch the 'change' initially
    public Viewport         Viewport        { get; private set; }
    public int              ControlWidth    { get; private set; }
    public int              ControlHeight   { get; private set; }
    public uint             VisibleWidth    { get; private set; }
    public uint             VisibleHeight   { get; private set; }
    public AspectRatio      DAR             { get; private set; }

    VideoStream     scfg;

    CropRect        crop;
    uint            rotation;

    public int      SideXPixels     => sideXPixels;
    public int      SideYPixels     => sideYPixels;
    int             sideXPixels, sideYPixels;
    internal double curRatio, keepRatio, fillRatio;

    bool            canFL, canD3;
    int             pendingVPRequests;
    VPRequestType   vpRequests; // Render-thread work, including requests added by the current pass

    float           autoSDRMinNits      = 0.203f;
    float           autoSDRPeakNits     = 203;
    float           autoHDRMinNits      = 0.0f;
    float           autoHDRPeakNits     = 1000.0f;
    bool            displayHDREnabled;
    bool            hdrSwapchain;

    internal delegate VideoFrame FillPlanesDelegate(ref AVFrame* frame);
    internal FillPlanesDelegate FillPlanes;

    internal void VPRequest(VPRequestType request, bool execute = true)
    {
        Interlocked.Or(ref pendingVPRequests, (int)request);
        if (execute)
            RenderRequest();
    }

    VPRequestType TakeVPRequests()
        => (VPRequestType)Interlocked.Exchange(ref pendingVPRequests, 0);

    VideoProcessors VPSelection()
    {
        /* D3 Pros
         * - Performance & Power Efficiency
         * - Deinterlace & Super Resolution     ** Might Switch VP
         * - Scaling (?) & Extra/More Accurate Filters (?)
         * 
         * D3 Cons
         * - Extra rendering steps for SW Frames
         * 
         * FL Pros
         * - Alpha Channel & Split Frame Alpha  ** Might Switch VP
         * - HV Flip                            ** Might Switch VP
         * - HDR to SDR
         * 
         * Sws Pros
         * - More Pixel Formats (e.g. Paletted, BE, higher bit depth?)
         * - More Scaling Algorithms (but we don't use them currently as FillPlanes is at pre-Scale stage *only for Bitmap subs)
         * 
         * Sws Cons
         * - Slow Performance (CPU/RAM)
         */

        canD3 = vp != null && (VideoDecoder.HWAccelerated ||
            (!scfg.VFlip && scfg.PixelComp0Depth == 8));
        
        canFL = VideoDecoder.HWAccelerated ||
            (!scfg.PixelFormatDesc->flags.HasFlag(PixFmtFlags.Pal) && (!scfg.PixelFormatDesc->flags.HasFlag(PixFmtFlags.Be) || scfg.PixelComp0Depth <= 8));

        // 360 Panoramic mode requires Flyleaf VP (custom pixel shader)
        if (Pano360._enabled)
            return canFL ? VideoProcessors.Flyleaf : VideoProcessors.SwsScale;

        if (PreferredVideoProcessor == VideoProcessors.D3D11    && canD3)
            return VideoProcessors.D3D11;

        if (PreferredVideoProcessor == VideoProcessors.Flyleaf  && canFL)
            return VideoProcessors.Flyleaf;

        if (PreferredVideoProcessor == VideoProcessors.SwsScale) // always support?
            return VideoProcessors.SwsScale;

        // Auto Selection | TBR: (Maybe AutoD3D11, AutoFlyleaf or user selection with callback?)

        var fieldType = DeInterlace == DeInterlace.Auto ? scfg.FieldOrder : (VideoFrameFormat)DeInterlace;

        bool requiresFL =
            // Alpha | Split Frame Alpha | HV Flip | BT.2020/HDR
            FLDoviSupported() || !canD3 || scfg.ColorSpace == ColorSpace.BT2020 || scfg.iccData != null || userHFlip || userVFlip || SplitFrameAlphaPosition != SplitFrameAlphaPosition.None || scfg.PixelFormatDesc->flags.HasFlag(PixFmtFlags.Alpha) ||
            // SW w/o Deinterlace | Super Resolution
            (!VideoDecoder.HWAccelerated && fieldType == VideoFrameFormat.Progressive && !ucfg.SuperResolution);

        if (canFL && requiresFL)
            return VideoProcessors.Flyleaf;

        if (canD3)
            return VideoProcessors.D3D11;

        return VideoProcessors.SwsScale;
    }

    internal bool VPConfig(VideoStream videoStream, AVFrame* frame)
    {
        lock (lockRenderLoops)
            Frames.SetRendererFrame(null);
        
        scfg = videoStream;
        Interlocked.And(ref pendingVPRequests, ~(int)VPRequestType.ReConfigVP);
        VPConfigHelper(true, frame);

        return true; // todo
    }
    internal void VPSwitchCheck()
    {   // User's VP Switch
        if (scfg != null && VideoProcessor != VPSelection())
            VPRequest(VPRequestType.ReConfigVP);
    }
    void VPSwitch()
    {   // Called from ProcessRequests (RenderLoop) | lockRenderLoops
        bool wasRunning = VideoDecoder.IsRunning;
        if (wasRunning)
        {
            Monitor.Exit(lockRenderLoops);
            VideoDecoder.Pause();
            Monitor.Enter(lockRenderLoops);
        }

        try
        {
            lock (Frames)
            {
                var oldvp = VideoProcessor;
                var avframe = Frames.RendererFrame?.AVFrame;
                if (avframe == null)
                    avframe = Frames.Last?.AVFrame;

                VPConfigHelper(request: false, avframe); // this comes from RenderLoop

                if (oldvp == VideoProcessor)
                    return;

                if (!VideoDecoder.HWAccelerated)
                {
                    Frames.Dispose();

                    // Clear Screen
                    context.OMSetRenderTargets(SwapChain.BackBufferRtv);
                    context.ClearRenderTargetView(SwapChain.BackBufferRtv, ucfg.flBackColor);
                    SwapChain.Present(1, PresentFlags.None);

                    return;
                }

                bool renderFrameDone = false;
                var curFrame = Frames.First;
                while (curFrame != null)
                {
                    if (Frames.RendererFrame == curFrame)
                        renderFrameDone = true;

                    VPSwitchFrame(curFrame);
                    curFrame = curFrame.Next;
                }

                if (!renderFrameDone && Frames.RendererFrame != null)
                    VPSwitchFrame(Frames.RendererFrame);
            }
        }
        finally
        {
            if (wasRunning)
            {
                Monitor.Exit(lockRenderLoops);
                VideoDecoder.Start();
                Monitor.Enter(lockRenderLoops);
            }
        }
    }
    void VPSwitchFrame(VideoFrame mFrame)
    {   // TBR: Cannot run in parallel with Fill Frames!!! (same txt/srv/swsframe etc..)
        mFrame.DisposeTexture();

        if (mFrame.AVFrame != null) // HW Frame
        {
            if (VideoProcessor == VideoProcessors.D3D11)
            {
                vpivd.Texture2D.ArraySlice = (uint)mFrame.AVFrame->data[1];
                mFrame.VPIV = vd.CreateVideoProcessorInputView(ffTexture, ve, vpivd);
            }
            else if (VideoProcessor == VideoProcessors.Flyleaf)
            {
                srvDesc[0].Texture2DArray.FirstArraySlice = srvDesc[1].Texture2DArray.FirstArraySlice = (uint)mFrame.AVFrame->data[1];
                mFrame.SRV = [
                    device.CreateShaderResourceView(ffTexture, srvDesc[0]),
                    device.CreateShaderResourceView(ffTexture, srvDesc[1])];
            }
            else if (VideoProcessor == VideoProcessors.SwsScale)
            {
                var frame   = av_frame_alloc();
                int ret     = av_hwframe_transfer_data(frame, mFrame.AVFrame, 0);
                ret         = av_frame_copy_props(frame, mFrame.AVFrame);
                SwsFillPlanesHelper(mFrame, frame);
                av_frame_free(&frame);
            }
        }
    }
    void VPConfigHelper(bool request, AVFrame* frame)
    {
        if (scfg == null)
            return;

        var oldVP       = VideoProcessor;
        var vpRequests  = VPRequestType.RotationFlip | VPRequestType.Crop | VPRequestType.UpdatePS | VPRequestType.UpdateSwapChain; // TBR: we should set them all here as we don't compare with previous states
        VideoProcessor  = VPSelection();
        isPQSpline      = false;
        isDovi          = false;
        DynamicRange    = "SDR";
        
        if (CanTrace) Log.Trace($"Preparing planes for {scfg.PixelFormatStr} with {VideoProcessor}");

        if (VideoProcessor == VideoProcessors.D3D11)
        {
            if (VideoProcessor != oldVP)
            {
                RaiseUI(nameof(VideoProcessor));
                vpRequests |= VPRequestType.Resize;
                D3FiltersSync();
            }

            D3Config(frame);
        }
        else
        {
            if (VideoProcessor != oldVP)
            {
                RaiseUI(nameof(VideoProcessor));

                if (oldVP == VideoProcessors.D3D11)
                {
                    SwapChain.VPOV?.Dispose();

                    if (psCase == PSCase.SWD3) // prev
                    {
                        context.VSSetShader(vsMain);
                        psIdPrev = "f^"; // force SetPS
                    }

                    if (FieldType != VideoFrameFormat.Progressive)
                    {
                        FieldType = VideoFrameFormat.Progressive;
                        RaiseUI(nameof(FieldType));
                    }

                    FLFiltersSync();
                }
            }

            FLSwsConfig(frame);
        }

        if (player != null)
        {
            RaiseUI(nameof(DynamicRange));

            VPRequest(vpRequests, execute: request);
        }
        else // TBR: Extractor
        {
            VPRequest(vpRequests & ~VPRequestType.Resize, execute: false);
            Interlocked.And(ref pendingVPRequests, ~(int)VPRequestType.Resize); // No SwapChain

            if (VideoProcessor == VideoProcessors.D3D11)
                D3ProcessRequests();
            else
                FLProcessRequests();
        }

        NotifyFiltersChanged();
        if (CanDebug) Log.Debug($"Prepared planes for {scfg.PixelFormatStr} with {VideoProcessor} [{psCase}]");
    }

    internal void UpdateDisplay()
    {
        var output = GetCurrentOutput();
        if (output == null)
            return;

        bool    updated         = false;
        bool    hdrStateUpdated = false;
        float   sdrMinNits      = 0.203f;
        float   sdrPeakNits     = 203.0f;
        float   hdrMinNits      = 0.0f;
        float   hdrPeakNits     = 1000.0f;
        bool    hdrEnabled      = false;

        try
        {
            var desc = output.Description;

            DisplayConfig.TryGetHDRInfo(desc.DeviceName, out var hdr);
            hdrEnabled = hdr.Enabled;

            IDXGIOutput6 output6;
            try { output6 = output.QueryInterfaceOrNull<IDXGIOutput6>(); } finally { }

            if (output6 != null)
            {
                var desc1 = output6.Description1;

                sdrMinNits = desc1.MinLuminance;
                if (!hdr.Supported)
                    sdrPeakNits = desc1.MaxLuminance;

                if (hdr.Supported)
                {
                    hdrMinNits = MathF.Max(desc1.MinLuminance, 0.0f);
                    if (desc1.MaxLuminance > 0)
                        hdrPeakNits = desc1.MaxLuminance;
                }

                var coord = desc1.DesktopCoordinates;
                if (ucfg.MaxVerticalResolutionAuto != coord.Bottom - coord.Top)
                {
                    updated = true;
                    ucfg.MaxVerticalResolutionAuto = coord.Bottom - coord.Top;
                }

                output6.Dispose();
            }

            // When Windows HDR is enabled, this is the white level used for SDR
            // content on the HDR desktop. It remains our SDR-swapchain target.
            if (hdr.Enabled && hdr.SDRWhiteNits is float sdrWhite)
                sdrPeakNits = sdrWhite;
        }
        catch { }
        finally
        {
            output.Dispose();
        }

        if (autoSDRMinNits != sdrMinNits)
        {
            autoSDRMinNits = sdrMinNits;
            updated = true;
        }

        if (autoSDRPeakNits != sdrPeakNits)
        {
            autoSDRPeakNits = sdrPeakNits;
            updated = true;
        }

        if (autoHDRMinNits != hdrMinNits)
        {
            autoHDRMinNits = hdrMinNits;
            updated = true;
        }

        if (autoHDRPeakNits != hdrPeakNits)
        {
            autoHDRPeakNits = hdrPeakNits;
            updated = true;
        }

        if (displayHDREnabled != hdrEnabled)
        {
            displayHDREnabled = hdrEnabled;
            hdrStateUpdated = true;
        }

        if (hdrStateUpdated)
            VPRequest(VPRequestType.UpdateSwapChain, execute: false);

        if (updated)
            FLUpdateTargetNits();

        if (updated || hdrStateUpdated)
            RenderRequest();
    }

    void UpdateHDRSwapchain()
    {
        bool enabled = CanHDRDisplay;
        
        if (SwapChain.Disposed)
            enabled = false;
        else
        {
            if (SwapChain.HDR != enabled)
                SwapChain.SetHDR(enabled);

            enabled = SwapChain.HDR;
        }

        if (hdrSwapchain != enabled)
        {
            hdrSwapchain = enabled;
            FLUpdateTargetNits();

            if (VideoProcessor == VideoProcessors.D3D11)
                vpRequests |= VPRequestType.Resize;
        }
    }

    IDXGIOutput GetCurrentOutput()
    {
        nint monitor = MonitorFromWindow(SwapChain.ControlHwnd, MonitorOptions.MONITOR_DEFAULTTONEAREST);

        for (uint i = 0; DXGIAdapter.EnumOutputs(i, out var output).Success; i++)
        {
            if (output.Description.Monitor == monitor)
                return output;

            output.Dispose();
        }

        return null;
    }
    internal void UpdateSize(int width, int height)
    {
        ControlWidth    = width;
        ControlHeight   = height;
    }
    void SetSize()
    {
        SwapChain.SetSize();

        fillRatio = ControlWidth / (double)ControlHeight;
        if (AspectRatio == AspectRatio.Fill)
            curRatio = fillRatio;

        vpRequests |=  VPRequestType.Viewport;
    }
    void SetViewport(int width, int height)
    {
        int newWidth, newHeight;
        if (OriginalSize && VisibleHeight > 0 && keepRatio > 0)
        {
            // Correct non-square source pixels using the display ratio before rotation.
            // Both the swap-chain surface and the resulting viewport use physical pixels.
            int sourceHeight = (int)VisibleHeight;
            int sourceWidth = Math.Max(1, (int)Math.Round(sourceHeight * keepRatio));
            if (rotation is 90 or 270)
                (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);

            sideXPixels = width - sourceWidth;
            sideYPixels = height - sourceHeight;
            newWidth    = (int)(sourceWidth * zoom);
            newHeight   = (int)(sourceHeight * zoom);
        }
        else
        {
            bool shouldFill = player?.Host?.Player_HandlesRatioResize(width, height) == true;

            // A subpixel fit at the surface edge can become a full uncovered pixel
            // when the fitted viewport is truncated to integer dimensions.
            if (curRatio < fillRatio)
            {
                double unusedWidth = width - height * curRatio;
                newHeight   = (int)(height * zoom);
                newWidth    = shouldFill || unusedWidth < 1 ? (int)(width * zoom) : (int)(newHeight * curRatio);
                sideXPixels = (int)unusedWidth & ~1;
                sideYPixels = 0;
            }
            else
            {
                double unusedHeight = height - width / curRatio;
                newWidth    = (int)(width * zoom);
                newHeight   = shouldFill || unusedHeight < 1 ? (int)(height * zoom) : (int)(newWidth / curRatio);
                sideXPixels = 0;
                sideYPixels = (int)unusedHeight & ~1;
            }
        }

        int x = (int)(width * panXOffset) + sideXPixels / 2;
        int y = (int)(height * panYOffset) + sideYPixels / 2;
        int xZoomPixels = newWidth - (width - sideXPixels);
        int yZoomPixels = newHeight - (height - sideYPixels);
        Viewport = new((int)(x - xZoomPixels * (float)zoomCenter.X), (int)(y - yZoomPixels * (float)zoomCenter.Y), newWidth, newHeight);
        ViewportChanged?.Invoke(this, new());
    }
    void SetRotation()
    {
        bool was0_180   = rotation == 0 || rotation == 180;
        rotation        = (userRotation + scfg.Rotation) % 360;
        bool is0_180    = rotation == 0 || rotation == 180;

        if (was0_180 != is0_180 && !vpRequests.HasFlag(VPRequestType.Crop)) // TBR: Crop / AspectRatio will check too
        {
            curRatio = 1 / curRatio;
            if (AspectRatio == AspectRatio.Keep)
                player?.Host?.Player_RatioChanged(curRatio);
        }
        
        vpRequests |=  VPRequestType.Viewport;
    }
    void SetAspectRatio()
    {
        bool isKeep = AspectRatio == AspectRatio.Keep;
        if (isKeep)
            curRatio = keepRatio;
        else if (AspectRatio == AspectRatio.Fill)
            curRatio = fillRatio;
        else
            curRatio = AspectRatio == AspectRatio.Custom ? AspectRatioCustom.Value : AspectRatio.Value;

        if (rotation == 90 || rotation == 270)
            curRatio = 1 / curRatio;

        if (isKeep)
            player?.Host?.Player_RatioChanged(curRatio); // return handled and avoid SetViewport?*

        vpRequests |=  VPRequestType.Viewport;
    }
    void SetBackColor()
    {
        vc?.VideoProcessorSetOutputBackgroundColor(vp, false, ucfg.d3BackColor);
        vpRequests |=  VPRequestType.Viewport;
    }

    void SetVisibleSizeAndRatioHelper()
    {
        int x, y;
        _ = av_reduce(&x, &y, VisibleWidth * scfg.SAR.Num, VisibleHeight * scfg.SAR.Den, 1024 * 1024);
        DAR = new(x, y);
        keepRatio = DAR.Value;

        player?.Video.SetUISize((int)VisibleWidth, (int)VisibleHeight, DAR);

        if (AspectRatio == AspectRatio.Keep)
        {
            curRatio = rotation == 0 || rotation == 180 ? keepRatio : 1 / keepRatio;
            player?.Host?.Player_RatioChanged(curRatio);
        }
    }

    internal void SyncFilters()
    {   // Forces resync of current's VideoProcessor Filters to others (useful before saving)
        lock (lockDevice)
        {
            if (D3Disposed)
                return;

            if (VideoProcessor == VideoProcessors.D3D11)
                FLFiltersSync();
            else
                D3FiltersSync();
        }
    }
}

[Flags]
enum VPRequestType
{
    Empty           = 0,

    BackColor       = 1 << 0,
    ReConfigVP      = 1 << 1,   // User VP Switch + FL PS Update (e.g. w/o Filters)

    RotationFlip    = 1 << 2,   // Both - Flyleaf (for Flip)
    Resize          = 1 << 3,
    Crop            = 1 << 4,
    AspectRatio     = 1 << 5,
    Viewport        = 1 << 6,

    Deinterlace     = 1 << 7,   // D3D11
    UpdatePS        = 1 << 8,   // Flyleaf
    UpdateVS        = 1 << 9,   // Flyleaf
    UpdateHDR       = 1 << 10,
    UpdatePano      = 1 << 11,  // Flyleaf - 360 Panoramic params update
    UpdateSwapChain = 1 << 12,  // SDR <-> HDR/scRGB output
}
