using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

using Vortice;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/* TODO
 * 1) Attach on every frame video output configuration so we will not have to worry for video codec change etc.
 *      this will fix also dynamic video stream change
 *      we might have issue with bufRef / ffmpeg texture array on zero copy
 *      
 * 2) Use different context/video processor for off rendering so we dont have to reset pixel shaders/viewports etc (review also rtvs for extractor)
 * 
 * 3) Add Crop (Left/Right/Top/Bottom) -on Source- support per pixels (easy implemantation with D3D11VP, FlyleafVP requires more research)
 * 
 * 4) Improve A/V Sync
 * 
 *  a. vsync / vblack
 *  b. Present can cause a delay (based on device load), consider using more buffers for high frame rates that could minimize the delay
 *  c. display refresh rate vs input rate, consider using max latency > 1 ?
 *  d. frame drops
 *  
 *      swapChain.GetFrameStatistics(out var stats);
 *      swapChain.LastPresentCount - stats.PresentCount;
 */

public partial class Renderer : NotifyPropertyChanged, IDisposable
{
    public Config           Config          => VideoDecoder?.Config;
    public int              ControlWidth    { get; private set; }
    public int              ControlHeight   { get; private set; }
    internal IntPtr         ControlHandle;

    internal Action<IDXGISwapChain2> 
                            SwapChainWinUIClbk;

    public ID3D11Device     Device          { get; private set; }
    public bool             D3D11VPFailed   => vc == null;
    public GPUAdapter       GPUAdapter      { get; private set; }
    public bool             Disposed        { get; private set; } = true;
    public bool             SCDisposed      { get; private set; } = true;
    public int              MaxOffScreenTextures
                                            { get; set; } = 20;
    public VideoDecoder     VideoDecoder    { get; internal set; }
    public VideoStream      VideoStream     => VideoDecoder.VideoStream;

    public Viewport         GetViewport     { get; private set; }

    public CornerRadius     CornerRadius    { get => cornerRadius;  set { if (cornerRadius == value) return; cornerRadius = value; UpdateCornerRadius(); } }
    CornerRadius cornerRadius = new(0);
    CornerRadius zeroCornerRadius = new(0);

    public bool             IsHDR           { get => isHDR;         private set { SetUI(ref _IsHDR, value); isHDR = value; } }
    bool _IsHDR, isHDR;

    public int              PanXOffset      { get => panXOffset;    set => SetPanX(value); }
    int panXOffset;
    public void SetPanX(int panX, bool refresh = true)
    {
        panXOffset = panX;

        lock(lockDevice)
        {
            if (Disposed) return;
            SetViewport(refresh);
        }
    }

    public int              PanYOffset      { get => panYOffset;    set => SetPanY(value); }
    int panYOffset;
    public void SetPanY(int panY, bool refresh = true)
    {
        panYOffset = panY;

        lock(lockDevice)
        {
            if (Disposed) return;
            SetViewport(refresh);
        }
    }

    public uint             Rotation        { get => _RotationAngle;set { UpdateRotation(value); SetViewport(); } }
    uint _RotationAngle; VideoProcessorRotation _d3d11vpRotation  = VideoProcessorRotation.Identity;

    public VideoProcessors  VideoProcessor      { get => videoProcessor;    private set => SetUI(ref videoProcessor, value); }
    VideoProcessors videoProcessor = VideoProcessors.Flyleaf;

    public int              Zoom            { get => zoom;          set => SetZoom(value); }
    int zoom = 100;
    public void SetZoom(int zoom, bool refresh = true)
    {
        this.zoom = zoom;

        lock(lockDevice)
        {
            if (Disposed) return;
            SetViewport(refresh);
        }
    }

    public int              UniqueId        { get; private set; }

    public Dictionary<VideoFilters, VideoFilter> 
                            Filters         { get; set; }
    public VideoFrame       LastFrame       { get; set; }
    public RawRect          VideoRect       { get; set; }

    LogHandler Log;

    public Renderer(VideoDecoder videoDecoder, IntPtr handle = new IntPtr(), int uniqueId = -1)
    {
        UniqueId = uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
        VideoDecoder = videoDecoder;
        Log = new LogHandler(("[#" + UniqueId + "]").PadRight(8, ' ') + " [Renderer      ] ");

        singleStageDesc = new Texture2DDescription()
        {
            Usage       = ResourceUsage.Staging,
            Format      = Format.B8G8R8A8_UNorm,
            ArraySize   = 1,
            MipLevels   = 1,
            BindFlags   = BindFlags.None,
            CPUAccessFlags      = CpuAccessFlags.Read,
            SampleDescription   = new SampleDescription(1, 0),

            Width       = -1,
            Height      = -1
        };

        singleGpuDesc = new Texture2DDescription()
        {
            Usage       = ResourceUsage.Default,
            Format      = Format.B8G8R8A8_UNorm,
            ArraySize   = 1,
            MipLevels   = 1,
            BindFlags   = BindFlags.RenderTarget | BindFlags.ShaderResource,
            SampleDescription   = new SampleDescription(1, 0)
        };

        wndProcDelegate = new(WndProc);
        wndProcDelegatePtr = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
        ControlHandle = handle;
        Initialize();
    }
}