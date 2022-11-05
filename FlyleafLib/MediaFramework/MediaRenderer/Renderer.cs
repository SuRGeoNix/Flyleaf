using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

using SharpGen.Runtime;

using Vortice;
using Vortice.DXGI;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

using FlyleafLib.MediaFramework.MediaFrame;
using VideoDecoder = FlyleafLib.MediaFramework.MediaDecoder.VideoDecoder;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    /* TODO
     * 1) Attach on every frame video output configuration so we will not have to worry for video codec change etc.
     *      this will fix also dynamic video stream change
     *      we might have issue with bufRef / ffmpeg texture array on zero copy
     *      
     * 2) Use different context/video processor for off rendering so we dont have to reset pixel shaders/viewports etc (review also rtvs for extractor)
     */

    public unsafe partial class Renderer : NotifyPropertyChanged, IDisposable
    {
        public Config           Config          => VideoDecoder?.Config;
        public int              ControlWidth    { get; private set; }
        public int              ControlHeight   { get; private set; }
        internal IntPtr         ControlHandle; // When we re-created the swapchain so we don't access the control (which requires UI thread)

        public Window           WindowRef       { get; set; }
        public Form             FormRef         { get; set; }
        public UserControl      ControlRef      { get; set; }

        public ID3D11Device     Device          { get; private set; }
        public bool             Disposed        { get; private set; } = true;
        public bool             SCDisposed      { get; private set; } = true;
        public RendererInfo     AdapterInfo     { get; internal set; }
        public int              MaxOffScreenTextures
                                                { get; set; } = 20;
        public VideoDecoder     VideoDecoder    { get; internal set; }

        public Viewport         GetViewport     { get; private set; }

        public int              PanXOffset      { get => panXOffset; set { panXOffset = value; lock(lockDevice) { if (Disposed) return; SetViewport(); } } }
        int panXOffset;
        public int              PanYOffset      { get => panYOffset; set { panYOffset = value; lock(lockDevice) { if (Disposed) return; SetViewport(); } } }
        int panYOffset;
        public int              Zoom            { get => zoom;       set { zoom       = value; lock(lockDevice) { if (Disposed) return; SetViewport(); } } }
        int zoom;
        public int              UniqueId        { get; private set; }

        public Dictionary<VideoFilters, VideoFilter> 
                                Filters         { get; set; }
        public VideoFrame       LastFrame       { get; set; }
        public RawRect          VideoRect       { get; set; }

        ID3D11DeviceContext                     context;
        IDXGISwapChain1                         swapChain;

        ID3D11Texture2D                         backBuffer;
        ID3D11RenderTargetView                  backBufferRtv;

        // Used for off screen rendering
        Texture2DDescription                    singleStageDesc, singleGpuDesc;
        ID3D11Texture2D                         singleStage;
        ID3D11Texture2D                         singleGpu;
        ID3D11RenderTargetView                  singleGpuRtv;
        Viewport                                singleViewport;
        
        // Used for parallel off screen rendering
        ID3D11RenderTargetView[]                rtv2;
        ID3D11Texture2D[]                       backBuffer2;
        bool[]                                  backBuffer2busy;

        ID3D11SamplerState                      samplerLinear;
        //ID3D11SamplerState                      samplerPoint;

        //ID3D11BlendState                        blendStateAlpha;
        //ID3D11BlendState                        blendStateAlphaInv;

        Dictionary<string, ID3D11PixelShader>   PSShaders = new Dictionary<string, ID3D11PixelShader>();
        Dictionary<string, ID3D11VertexShader>  VSShaders = new Dictionary<string, ID3D11VertexShader>();

        ID3D11Buffer                            vertexBuffer;
        ID3D11InputLayout                       vertexLayout;


        ID3D11ShaderResourceView[]              curSRVs;
        ShaderResourceViewDescription           srvDescR, srvDescRG;

        VideoProcessorColorSpace                inputColorSpace;
        VideoProcessorColorSpace                outputColorSpace;

        LogHandler Log;
        internal object  lockDevice = new object();
        object  lockPresentTask     = new object();
        bool    isPresenting;
        long    lastPresentAt       = 0;
        long    lastPresentRequestAt= 0;
        float   curRatio            = 1.0f;

        public Renderer(VideoDecoder videoDecoder, object control = null, int uniqueId = -1)
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
            
            Initialize();

            if (control != null)
                SetControl(control);
        }

        public void SetControl(object control)
        {
            lock (lockDevice)
            {
                DisposeSwapChain(true);

                if (control == null)
                    return;

                if (control is Window)
                {
                    WindowRef = (Window)control;

                    ControlHandle = new WindowInteropHelper(WindowRef).EnsureHandle();
                    ControlWidth = (int)WindowRef.ActualWidth;
                    ControlHeight = (int)WindowRef.ActualHeight;

                    InitializeSwapChain();
                    Log.Debug("Swap chain with control ready");
                }
                else if (control is UserControl)
                {
                    ControlRef = (UserControl)control;
                    ControlWidth = ControlRef.Width;
                    ControlHeight = ControlRef.Height;

                    if (ControlRef.Handle == IntPtr.Zero)
                    {
                        Log.Debug("Waiting for control handle to be created");

                        ControlRef.HandleCreated += (o, e) =>
                        {
                            lock (lockDevice)
                            {
                                ControlHandle = ControlRef.Handle;
                                InitializeSwapChain();
                                Log.Debug("Swap chain with control ready");
                            }
                        };
                    }
                    else
                    {
                        lock (lockDevice)
                        {
                            ControlHandle = ControlRef.Handle;
                            InitializeSwapChain();
                            Log.Debug("Swap chain with control ready");
                        }
                    }
                }
            }
        }
        private void ControlRef_SizeChanged(object sender, EventArgs e) => ResizeBuffers((int)(ControlRef.Width * Utils.NativeMethods.DpiX), (int)(ControlRef.Height * Utils.NativeMethods.DpiY));
        private void WindowRef_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeBuffers((int)(e.NewSize.Width * Utils.NativeMethods.DpiX), (int)(e.NewSize.Height * Utils.NativeMethods.DpiY));

        public void Initialize()
        {
            lock (lockDevice)
            {
                try
                {
                    if (CanDebug) Log.Debug("Initializing");

                    Disposed = false;

                    ID3D11Device tempDevice;
                    IDXGIAdapter1 adapter = null;
                    DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;

                    #if DEBUG
                    if (D3D11.SdkLayersAvailable()) creationFlags |= DeviceCreationFlags.Debug;
                    #endif

                    // Finding User Definied adapter
                    if (!string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) && Config.Video.GPUAdapter.ToUpper() != "WARP")
                    {
                        for (int i=0; Engine.Video.Factory.EnumAdapters1(i, out adapter).Success; i++)
                        {
                            if (adapter.Description1.Description == Config.Video.GPUAdapter)
                                break;

                            if (Regex.IsMatch(adapter.Description1.Description + " luid=" + adapter.Description1.Luid, Config.Video.GPUAdapter, RegexOptions.IgnoreCase))
                                break;

                            adapter.Dispose();
                        }

                        if (adapter == null)
                        {
                            Log.Error($"GPU Adapter with {Config.Video.GPUAdapter} has not been found. Falling back to default.");
                            Config.Video.GPUAdapter = null;
                        }
                    }

                    // Creating WARP (force by user or us after late failure)
                    if (!string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) && Config.Video.GPUAdapter.ToUpper() == "WARP")
                    {
                        D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.None, featureLevels, out tempDevice).CheckError();
                    }

                    // Creating User Defined or Default
                    else
                    {
                        // Creates the D3D11 Device based on selected adapter or default hardware (highest to lowest features and fall back to the WARP device. see http://go.microsoft.com/fwlink/?LinkId=286690)
                        if (D3D11.D3D11CreateDevice(adapter, adapter == null ? DriverType.Hardware : DriverType.Unknown, creationFlags, featureLevelsAll, out tempDevice).Failure)
                            if (D3D11.D3D11CreateDevice(adapter, adapter == null ? DriverType.Hardware : DriverType.Unknown, creationFlags, featureLevels, out tempDevice).Failure)
                            {
                                Config.Video.GPUAdapter = "WARP";
                                D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.None, featureLevels, out tempDevice).CheckError();
                            }
                    }

                    Device = tempDevice.QueryInterface<ID3D11Device1>();
                    context= Device.ImmediateContext;

                    // Gets the default adapter from the D3D11 Device
                    if (adapter == null)
                    {
                        Device.Tag = (new Luid()).ToString();
                        using (var deviceTmp = Device.QueryInterface<IDXGIDevice1>())
                        using (var adapterTmp = deviceTmp.GetAdapter())
                            adapter = adapterTmp.QueryInterface<IDXGIAdapter1>();
                    }
                    else
                        Device.Tag = adapter.Description.Luid.ToString();

                    RendererInfo.Fill(this, adapter);
                    if (CanDebug) Log.Debug($"Adapter Info\r\n{AdapterInfo}\r\n");

                    tempDevice.Dispose();
                    adapter.Dispose();
            
                    using (var mthread    = Device.QueryInterface<ID3D11Multithread>()) mthread.SetMultithreadProtected(true);
                    using (var dxgidevice = Device.QueryInterface<IDXGIDevice1>())      dxgidevice.MaximumFrameLatency = 1;

                    ReadOnlySpan<float> vertexBufferData = new float[]
                    {
                        -1.0f,  -1.0f,  0,      0.0f, 1.0f,
                        -1.0f,   1.0f,  0,      0.0f, 0.0f,
                         1.0f,  -1.0f,  0,      1.0f, 1.0f,
                
                         1.0f,  -1.0f,  0,      1.0f, 1.0f,
                        -1.0f,   1.0f,  0,      0.0f, 0.0f,
                         1.0f,   1.0f,  0,      1.0f, 0.0f
                    };
                    vertexBuffer = Device.CreateBuffer(vertexBufferData, new BufferDescription() { BindFlags = BindFlags.VertexBuffer });
                    context.IASetVertexBuffer(0, vertexBuffer, sizeof(float) * 5);

                    samplerLinear = Device.CreateSamplerState(new SamplerDescription()
                    {
                        ComparisonFunction = ComparisonFunction.Never,
                        AddressU = TextureAddressMode.Clamp,
                        AddressV = TextureAddressMode.Clamp,
                        AddressW = TextureAddressMode.Clamp,
                        Filter   = Filter.MinMagMipLinear,
                        MinLOD   = 0,
                        MaxLOD   = float.MaxValue
                    });

                    //samplerPoint = Device.CreateSamplerState(new SamplerDescription()
                    //{
                    //    ComparisonFunction = ComparisonFunction.Never,
                    //    AddressU = TextureAddressMode.Clamp,
                    //    AddressV = TextureAddressMode.Clamp,
                    //    AddressW = TextureAddressMode.Clamp,
                    //    Filter   = Filter.MinMagMipPoint,
                    //    MinLOD   = 0,
                    //    MaxLOD   = float.MaxValue
                    //});

                    // Blend
                    //var blendDesc = new BlendDescription();
                    //blendDesc.RenderTarget[0].IsBlendEnabled = true;
                    //blendDesc.RenderTarget[0].SourceBlend = Blend.One;
                    //blendDesc.RenderTarget[0].DestinationBlend = Blend.SourceAlpha;
                    //blendDesc.RenderTarget[0].BlendOperation = BlendOperation.Add;
                    //blendDesc.RenderTarget[0].SourceBlendAlpha = Blend.One;
                    //blendDesc.RenderTarget[0].DestinationBlendAlpha = Blend.Zero;
                    //blendDesc.RenderTarget[0].BlendOperationAlpha = BlendOperation.Add;
                    //blendDesc.RenderTarget[0].RenderTargetWriteMask = ColorWriteEnable.All;
                    //blendStateAlpha = Device.CreateBlendState(blendDesc);

                    //blendDesc.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
                    //blendStateAlphaInv = Device.CreateBlendState(blendDesc);

                    // Vertex
                    foreach(var shader in VSShaderBlobs)
                    {
                        VSShaders.Add(shader.Key, Device.CreateVertexShader(shader.Value));
                        vertexLayout = Device.CreateInputLayout(inputElements, shader.Value);

                        context.IASetInputLayout(vertexLayout);
                        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                        context.VSSetShader(VSShaders["VSSimple"]);

                        break; // Using single vertex
                    }

                    // Pixel Shaders
                    foreach(var shader in PSShaderBlobs)
                        PSShaders.Add(shader.Key, Device.CreatePixelShader(shader.Value));

                    context.PSSetShader(PSShaders["PSSimple"]);
                    context.PSSetSampler(0, samplerLinear);

                    psBuffer = Device.CreateBuffer(new BufferDescription() 
                    {
                        Usage           = ResourceUsage.Default,
                        BindFlags       = BindFlags.ConstantBuffer,
                        CPUAccessFlags  = CpuAccessFlags.None,
                        ByteWidth       = sizeof(PSBufferType)
                    });
                    context.PSSetConstantBuffer(0, psBuffer);

                    psBufferData.hdrmethod  = HDRtoSDRMethod.None;
                    context.UpdateSubresource(psBufferData, psBuffer);

                    InitializeVideoProcessor();

                    // TBR: Device Removal Event
                    //ID3D11Device4 device4 = Device.QueryInterface<ID3D11Device4>(); device4.RegisterDeviceRemovedEvent(..);

                    if (ControlHandle != IntPtr.Zero)
                        InitializeSwapChain();

                    if (CanInfo) Log.Info($"Initialized with Feature Level {(int)Device.FeatureLevel >> 12}.{(int)Device.FeatureLevel >> 8 & 0xf}");

                } catch (Exception e)
                {
                    if (string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) || Config.Video.GPUAdapter.ToUpper() != "WARP")
                    {
                        try { if (Device != null) Log.Warn($"Device Remove Reason = {Device.DeviceRemovedReason.Description}"); } catch { } // For troubleshooting
                        
                        Log.Warn($"Initialization failed ({e.Message}). Failling back to WARP device.");
                        Config.Video.GPUAdapter = "WARP";
                        Dispose();
                        Initialize();
                    }
                    else
                        Log.Error($"Initialization failed ({e.Message})");
                }
            }
        }
        public void InitializeSwapChain()
        {
            Log.Info($"Initializing {(Config.Video.Swap10Bit ? "10-bit" : "8-bit")} swap chain with {Config.Video.SwapBuffers} buffers [Handle: {ControlHandle}]");

            SCDisposed = false;

            if (WindowRef != null)
                WindowRef.SizeChanged += WindowRef_SizeChanged;
            else if (ControlRef != null)
                ControlRef.SizeChanged += ControlRef_SizeChanged;

            SwapChainDescription1 swapChainDescription = new SwapChainDescription1()
            {
                Format      = Config.Video.Swap10Bit ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm,
                Width       = ControlWidth,
                Height      = ControlHeight,
                AlphaMode   = AlphaMode.Ignore,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0)
            };

            if (Device.FeatureLevel < FeatureLevel.Level_10_0 || (!string.IsNullOrWhiteSpace(Config.Video.GPUAdapter) && Config.Video.GPUAdapter.ToUpper() == "WARP"))
            {
                swapChainDescription.BufferCount= 1;
                swapChainDescription.SwapEffect = SwapEffect.Discard;
                swapChainDescription.Scaling    = Scaling.Stretch;
            }
            else
            {
                swapChainDescription.BufferCount= Config.Video.SwapBuffers; // TBR: for hdr output or >=60fps maybe use 6
                swapChainDescription.SwapEffect = SwapEffect.FlipDiscard;
                swapChainDescription.Scaling    = Scaling.None;
            }

            swapChain    = Engine.Video.Factory.CreateSwapChainForHwnd(Device, ControlHandle, swapChainDescription, new SwapChainFullscreenDescription() { Windowed = true });
            backBuffer   = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv= Device.CreateRenderTargetView(backBuffer);

            ResizeBuffers(ControlWidth, ControlHeight);
            Present();
        }
        public void DisposeSwapChain(bool includingControls = false)
        {
            lock (lockDevice)
            {
                if (SCDisposed && ControlHandle == IntPtr.Zero)
                    return;

                // Clear Screan
                if (!Disposed && swapChain != null)
                {
                    try
                    {
                        context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                        swapChain.Present(Config.Video.VSync, PresentFlags.None);
                    } catch { }
                }

                Log.Info($"Destroying swap chain [Handle: {ControlHandle}]");
                SCDisposed = true;

                // TBR! When Player.Stop we DisposeVA which disposes the renderer and the swap chain, when it tries to initialize we have lost the handles
                if (includingControls)
                {
                    if (WindowRef != null)
                        WindowRef.SizeChanged -= WindowRef_SizeChanged;
                    else if (ControlRef != null)
                        ControlRef.SizeChanged -= ControlRef_SizeChanged;

                    WindowRef = null;
                    ControlRef = null;
                    ControlHandle = IntPtr.Zero;
                }

                vpov?.Dispose();
                backBufferRtv?.Dispose();
                backBuffer?.Dispose();
                swapChain?.Dispose();
                if (Device != null)
                    context?.Flush();
            }
        }
        public void Dispose()
        {
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                if (CanDebug) Log.Debug("Disposing");

                VideoDecoder.DisposeFrame(LastFrame);
                RefreshLayout();

                DisposeVideoProcessor();

                foreach(var shader in PSShaders.Values)
                    shader.Dispose();
                PSShaders.Clear();

                foreach(var shader in VSShaders.Values)
                    shader.Dispose();
                VSShaders.Clear();

                //samplerPoint?.Dispose();
                //blendStateAlpha?.Dispose();
                //blendStateAlphaInv?.Dispose();

                psBuffer?.Dispose();
                samplerLinear?.Dispose();
                vertexLayout?.Dispose();
                vertexBuffer?.Dispose();
                DisposeSwapChain();

                singleGpu?.Dispose();
                singleStage?.Dispose();
                singleGpuRtv?.Dispose();
                singleStageDesc.Width = -1; // ensures re-allocation

                if (rtv2 != null)
                {
                    for(int i=0; i<rtv2.Length-1; i++)
                        rtv2[i].Dispose();

                    rtv2 = null;
                }

                if (backBuffer2 != null)
                    for(int i=0; i<backBuffer2.Length-1; i++)
                        backBuffer2[i]?.Dispose();

                if (curSRVs != null)
                {
                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i]?.Dispose();

                    curSRVs = null;
                }

                if (Device != null)
                {
                    context.ClearState();
                    context.Flush();
                    context.Dispose();
                    Device.Dispose();
                    Device = null;
                }

                #if DEBUG
                ReportLiveObjects();
                #endif

                Disposed = true;
                curRatio = 1.0f;
                if (CanInfo) Log.Info("Disposed");
            }
        }

        internal void FrameResized()
        {
            // TODO: Win7 doesn't support R8G8_UNorm so use SNorm will need also unormUV on pixel shader
            lock (lockDevice)
            {
                if (Disposed || VideoDecoder.VideoStream == null)
                    return;

                curRatio = VideoDecoder.VideoStream.AspectRatio.Value;
                IsHDR = VideoDecoder.VideoStream.ColorSpace == "BT2020";

                var oldVP = videoProcessor;
                VideoProcessor = !VideoDecoder.VideoAccelerated || D3D11VPFailed || Config.Video.VideoProcessor == VideoProcessors.Flyleaf || (Config.Video.VideoProcessor == VideoProcessors.Auto && isHDR && !Config.Video.Deinterlace) ? VideoProcessors.Flyleaf : VideoProcessors.D3D11;
                
                if (videoProcessor == VideoProcessors.Flyleaf)
                {
                    // Reset FLVP filters to defaults (can be different from D3D11VP filters scaling)
                    if (oldVP != videoProcessor)
                    {
                        Config.Video.Filters[VideoFilters.Brightness].Value = Config.Video.Filters[VideoFilters.Brightness].Minimum + (Config.Video.Filters[VideoFilters.Brightness].Maximum - Config.Video.Filters[VideoFilters.Brightness].Minimum) / 2;
                        Config.Video.Filters[VideoFilters.Contrast].Value = Config.Video.Filters[VideoFilters.Contrast].Minimum + (Config.Video.Filters[VideoFilters.Contrast].Maximum - Config.Video.Filters[VideoFilters.Contrast].Minimum) / 2;
                    }

                    srvDescR = new ShaderResourceViewDescription();
                    srvDescR.Format = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm;

                    srvDescRG = new ShaderResourceViewDescription();
                    srvDescRG.Format = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16G16_UNorm : Format.R8G8_UNorm;

                    if (VideoDecoder.ZeroCopy)
                    {
                        srvDescR.ViewDimension = ShaderResourceViewDimension.Texture2DArray;
                        srvDescR.Texture2DArray = new Texture2DArrayShaderResourceView()
                        {
                            ArraySize = 1,
                            MipLevels = 1
                        };

                        srvDescRG.ViewDimension = ShaderResourceViewDimension.Texture2DArray;
                        srvDescRG.Texture2DArray = new Texture2DArrayShaderResourceView()
                        {
                            ArraySize = 1,
                            MipLevels = 1
                        };
                    }
                    else
                    {
                        srvDescR.ViewDimension = ShaderResourceViewDimension.Texture2D;
                        srvDescR.Texture2D = new Texture2DShaderResourceView()
                        {
                            MipLevels = 1,
                            MostDetailedMip = 0
                        };

                        srvDescRG.ViewDimension = ShaderResourceViewDimension.Texture2D;
                        srvDescRG.Texture2D = new Texture2DShaderResourceView()
                        {
                            MipLevels = 1,
                            MostDetailedMip = 0
                        };
                    }

                    psBufferData.format = VideoDecoder.VideoAccelerated ? PSFormat.Y_UV : ((VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled ? PSFormat.Y_U_V : PSFormat.RGB));
                
                    lastLumin = 0;
                    psBufferData.hdrmethod = HDRtoSDRMethod.None;

                    if (isHDR)
                    {
                        psBufferData.coefsIndex = 0;
                        UpdateHDRtoSDR(null, false);
                    }
                    else if (VideoDecoder.VideoStream.ColorSpace == "BT709")
                        psBufferData.coefsIndex = 1;
                    else if (VideoDecoder.VideoStream.ColorSpace == "BT601")
                        psBufferData.coefsIndex = 2;
                    else
                        psBufferData.coefsIndex = 2;

                    context.UpdateSubresource(psBufferData, psBuffer);
                }
                else
                {
                    // Reset D3D11 filters to defaults
                    if (oldVP != videoProcessor)
                    {
                        Config.Video.Filters[VideoFilters.Brightness].Value = Config.Video.Filters[VideoFilters.Brightness].DefaultValue;
                        Config.Video.Filters[VideoFilters.Contrast].Value = Config.Video.Filters[VideoFilters.Contrast].DefaultValue;
                    }

                    vpov?.Dispose();
                    vd1.CreateVideoProcessorOutputView(backBuffer, vpe, vpovd, out vpov);

                    inputColorSpace = new VideoProcessorColorSpace()
                    {
                        Usage           = 0,
                        RGB_Range       = VideoDecoder.VideoStream.AVStream->codecpar->color_range == FFmpeg.AutoGen.AVColorRange.AVCOL_RANGE_JPEG ? (uint) 0 : 1,
                        YCbCr_Matrix    = VideoDecoder.VideoStream.ColorSpace != "BT601" ? (uint) 1 : 0,
                        YCbCr_xvYCC     = 0,
                        Nominal_Range   = VideoDecoder.VideoStream.AVStream->codecpar->color_range == FFmpeg.AutoGen.AVColorRange.AVCOL_RANGE_JPEG ? (uint) 2 : 1
                    };

                    outputColorSpace = new VideoProcessorColorSpace()
                    {
                        Usage           = 0,
                        RGB_Range       = 0,
                        YCbCr_Matrix    = 1,
                        YCbCr_xvYCC     = 0,
                        Nominal_Range   = 2
                    };
                }

                VideoDecoder.DisposeFrame(LastFrame);
                VideoRect = new RawRect(0, 0, VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height);

                if (ControlHandle != IntPtr.Zero)
                    SetViewport();
                else
                {
                    if (rtv2 != null)
                        for (int i = 0; i < rtv2.Length - 1; i++)
                            rtv2[i].Dispose();

                    if (backBuffer2 != null)
                        for (int i = 0; i < backBuffer2.Length - 1; i++)
                            backBuffer2[i].Dispose();

                    backBuffer2busy = new bool[MaxOffScreenTextures];
                    rtv2 = new ID3D11RenderTargetView[MaxOffScreenTextures];
                    backBuffer2 = new ID3D11Texture2D[MaxOffScreenTextures];

                    for (int i = 0; i < MaxOffScreenTextures; i++)
                    {
                        backBuffer2[i] = Device.CreateTexture2D(new Texture2DDescription()
                        {
                            Usage       = ResourceUsage.Default,
                            BindFlags   = BindFlags.RenderTarget,
                            Format      = Format.B8G8R8A8_UNorm,
                            Width       = VideoDecoder.VideoStream.Width,
                            Height      = VideoDecoder.VideoStream.Height,

                            ArraySize   = 1,
                            MipLevels   = 1,
                            SampleDescription = new SampleDescription(1, 0)
                        });

                        rtv2[i] = Device.CreateRenderTargetView(backBuffer2[i]);
                    }

                    context.RSSetViewport(0, 0, VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height);
                }
            }
        }
        internal void ResizeBuffers(int width, int height)
        {   
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                ControlWidth = width;
                ControlHeight = height;

                backBufferRtv.Dispose();
                vpov?.Dispose();
                backBuffer.Dispose();
                swapChain.ResizeBuffers(0, ControlWidth, ControlHeight, Format.Unknown, SwapChainFlags.None);
                backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                backBufferRtv = Device.CreateRenderTargetView(backBuffer);
                if (videoProcessor == VideoProcessors.D3D11)
                    vd1.CreateVideoProcessorOutputView(backBuffer, vpe, vpovd, out vpov);

                SetViewport();
            }
        }
        public void SetViewport()
        {
            float ratio;

            if (Config.Video.AspectRatio == AspectRatio.Keep)
                ratio = curRatio;

            else if (Config.Video.AspectRatio == AspectRatio.Fill)
                ratio = ControlWidth / (float)ControlHeight;

            else if (Config.Video.AspectRatio == AspectRatio.Custom)
                ratio = Config.Video.CustomAspectRatio.Value;
            else
                ratio = Config.Video.AspectRatio.Value;

            if (ratio <= 0) ratio = 1;

            int Height = ControlHeight + (zoom * 2);
            int Width  = ControlWidth  + (zoom * 2);

            if (Width / ratio > Height)
                GetViewport = new Viewport(((ControlWidth - (Height * ratio)) / 2) + PanXOffset, 0 - zoom + PanYOffset, Height * ratio, Height, 0.0f, 1.0f);
            else
                GetViewport = new Viewport(0 - zoom + PanXOffset, ((ControlHeight - (Width / ratio)) / 2) + PanYOffset, Width, Width / ratio, 0.0f, 1.0f);

            if (videoProcessor == VideoProcessors.D3D11)
            {
                RawRect src, dst;

                if (GetViewport.X + GetViewport.Width <= 0 || GetViewport.X >= ControlWidth || GetViewport.Y + GetViewport.Height <= 0 || GetViewport.Y >= ControlHeight)
                {
                    //Log.Debug("Out of screen");
                    src = new RawRect();
                    dst = new RawRect();
                }
                else
                {
                    if (GetViewport.Y + GetViewport.Height > ControlHeight)
                        Height = (int) (VideoRect.Bottom- ((GetViewport.Y + GetViewport.Height - ControlHeight)* (VideoRect.Bottom / GetViewport.Height)));
                    else
                        Height = VideoRect.Bottom;

                    if (GetViewport.X + GetViewport.Width > ControlWidth)
                        Width = (int) (VideoRect.Right  - ((GetViewport.X + GetViewport.Width - ControlWidth)  * (VideoRect.Right / GetViewport.Width)));
                    else
                        Width  = VideoRect.Right;

                    src = new RawRect((int) (Math.Min(GetViewport.X, 0f) * ((float)VideoRect.Right / (float)GetViewport.Width) * -1f), (int) (Math.Min(GetViewport.Y, 0f) * ((float)VideoRect.Bottom / (float)GetViewport.Height) * -1f), Width, Height);
                    dst = new RawRect(Math.Max((int)GetViewport.X, 0), Math.Max((int)GetViewport.Y, 0), Math.Min((int)GetViewport.Width + (int)GetViewport.X, ControlWidth), Math.Min((int)GetViewport.Height + (int)GetViewport.Y, ControlHeight));   
                }

                vc.VideoProcessorSetStreamSourceRect(vp, 0, true, src);
                vc.VideoProcessorSetStreamDestRect  (vp, 0, true, dst);
                vc.VideoProcessorSetOutputTargetRect(vp, true, new RawRect(0, 0, ControlWidth, ControlHeight));
            }

            Present();
        }
        
        public bool Present(VideoFrame frame)
        {
            if (Monitor.TryEnter(lockDevice, 5))
            {
                try
                {
                    PresentInternal(frame);
                    VideoDecoder.DisposeFrame(LastFrame);
                    LastFrame = frame;

                    return true;

                } catch (Exception e)
                {
                    if (CanWarn) Log.Warn($"Present frame failed {e.Message} | {Device?.DeviceRemovedReason}");
                    VideoDecoder.DisposeFrame(frame);

                    if (vpiv != null)
                        vpiv.Dispose();

                    if (curSRVs != null)
                        for (int i=0; i<curSRVs.Length; i++)
                            curSRVs[i].Dispose();

                    return false;

                } finally
                {
                    Monitor.Exit(lockDevice);
                }
            }

            if (CanDebug) Log.Debug("Dropped Frame - Lock timeout " + (frame != null ? Utils.TicksToTime(frame.timestamp) : ""));
            VideoDecoder.DisposeFrame(frame);

            return false;
        }
        public void Present()
        {
            if (ControlHandle == IntPtr.Zero)
                return;

            // NOTE: We don't have TimeBeginPeriod, FpsForIdle will not be accurate
            lock (lockPresentTask)
            {
                if ((Config.Player.player == null || !Config.Player.player.requiresBuffering) && VideoDecoder.IsRunning)
                    return;

                if (isPresenting)
                {
                    lastPresentRequestAt = DateTime.UtcNow.Ticks;
                    return;
                }

                isPresenting = true;
            }

            Task.Run(() =>
            {
                do
                {
                    long sleepMs = DateTime.UtcNow.Ticks - lastPresentAt;
                    sleepMs = sleepMs < (long)( 1.0/Config.Player.IdleFps * 1000 * 10000) ? (long) (1.0 / Config.Player.IdleFps * 1000) : 0;
                    if (sleepMs > 2)
                        Thread.Sleep((int)sleepMs);

                    RefreshLayout();

                    lastPresentAt = DateTime.UtcNow.Ticks;

                } while (lastPresentRequestAt > lastPresentAt);

                isPresenting = false;
            });
        }
        internal void PresentInternal(VideoFrame frame)
        {
            if (SCDisposed)
                return;

            if (VideoDecoder.VideoAccelerated)
            {
                if (videoProcessor == VideoProcessors.D3D11)
                {
                    if (frame.bufRef != null)
                    {
                        vpivd.Texture2D.ArraySlice = frame.subresource;
                        vd1.CreateVideoProcessorInputView(VideoDecoder.textureFFmpeg, vpe, vpivd, out vpiv);
                    }
                    else
                    {
                        vpivd.Texture2D.ArraySlice = 0;
                        vd1.CreateVideoProcessorInputView(frame.textures[0], vpe, vpivd, out vpiv);
                    }
                    
                    vpsa[0].InputSurface = vpiv;
                    vc.VideoProcessorSetStreamColorSpace(vp, 0, inputColorSpace);
                    vc.VideoProcessorSetOutputColorSpace(vp, outputColorSpace);
                    vc.VideoProcessorBlt(vp, vpov, 0, 1, vpsa);
                    swapChain.Present(Config.Video.VSync, PresentFlags.None);

                    vpiv.Dispose();
                }
                else
                {
                    curSRVs = new ID3D11ShaderResourceView[2];

                    if (frame.bufRef != null)
                    {
                        srvDescR. Texture2DArray.FirstArraySlice = frame.subresource;
                        srvDescRG.Texture2DArray.FirstArraySlice = frame.subresource;
                        curSRVs[0] = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescR);
                        curSRVs[1] = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescRG);
                    }
                    else
                    {
                        curSRVs[0] = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                        curSRVs[1] = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                    }

                    context.OMSetRenderTargets(backBufferRtv);
                    context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                    context.RSSetViewport(GetViewport);
                    context.PSSetShader(PSShaders["FlyleafPS"]);
                    context.PSSetSampler(0, samplerLinear);
                    context.PSSetShaderResources(0, curSRVs);
                    context.Draw(6, 0);
                    swapChain.Present(Config.Video.VSync, PresentFlags.None);

                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i].Dispose();
                }
            }
            else
            {
                curSRVs = new ID3D11ShaderResourceView[frame.textures.Length];
                for (int i=0; i<frame.textures.Length; i++)
                    curSRVs[i] = Device.CreateShaderResourceView(frame.textures[i]);

                context.OMSetRenderTargets(backBufferRtv);
                context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                context.RSSetViewport(GetViewport);
                context.PSSetShader(PSShaders["FlyleafPS"]);
                context.PSSetSampler(0, samplerLinear);
                context.PSSetShaderResources(0, curSRVs);
                context.Draw(6, 0);
                swapChain.Present(Config.Video.VSync, PresentFlags.None);

                for (int i=0; i<curSRVs.Length; i++)
                    curSRVs[i].Dispose();
            }
        }
        internal void PresentOffline(VideoFrame frame, ID3D11RenderTargetView rtv, Viewport viewport)
        {
            if (VideoDecoder.VideoAccelerated)
            {
                if (videoProcessor == VideoProcessors.D3D11)
                {
                    vd1.CreateVideoProcessorOutputView(rtv.Resource, vpe, vpovd, out ID3D11VideoProcessorOutputView vpov);

                    RawRect rect = new RawRect((int)viewport.X, (int)viewport.Y, (int)(viewport.Width + viewport.X), (int)(viewport.Height + viewport.Y));
                    vc.VideoProcessorSetStreamSourceRect(vp, 0, true, VideoRect);
                    vc.VideoProcessorSetStreamDestRect(vp, 0, true, rect);
                    vc.VideoProcessorSetOutputTargetRect(vp, true, rect);

                    if (frame.bufRef != null)
                    {
                        vpivd.Texture2D.ArraySlice = frame.subresource;
                        vd1.CreateVideoProcessorInputView(VideoDecoder.textureFFmpeg, vpe, vpivd, out vpiv);
                    }
                    else
                    {
                        vpivd.Texture2D.ArraySlice = 0;
                        vd1.CreateVideoProcessorInputView(frame.textures[0], vpe, vpivd, out vpiv);
                    }
                    
                    vpsa[0].InputSurface = vpiv;                    

                    vc.VideoProcessorSetStreamColorSpace(vp, 0, inputColorSpace);
                    vc.VideoProcessorSetOutputColorSpace(vp, outputColorSpace);
                    vc.VideoProcessorBlt(vp, vpov, 0, 1, vpsa);
                    vpiv.Dispose();
                    vpov.Dispose();
                }
                else
                {
                    curSRVs = new ID3D11ShaderResourceView[2];

                    if (frame.bufRef != null)
                    {
                        srvDescR. Texture2DArray.FirstArraySlice = frame.subresource;
                        srvDescRG.Texture2DArray.FirstArraySlice = frame.subresource;
                        curSRVs[0] = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescR);
                        curSRVs[1] = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescRG);
                    }
                    else
                    {
                        curSRVs[0] = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                        curSRVs[1] = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                    }

                    context.OMSetRenderTargets(rtv);
                    context.ClearRenderTargetView(rtv, Config.Video._BackgroundColor);
                    context.RSSetViewport(viewport);
                    context.PSSetShader(PSShaders["FlyleafPS"]);
                    context.PSSetSampler(0, samplerLinear);
                    context.PSSetShaderResources(0, curSRVs);
                    context.Draw(6, 0);

                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i].Dispose();
                }
            }
            else
            {
                curSRVs = new ID3D11ShaderResourceView[frame.textures.Length];
                for (int i=0; i<frame.textures.Length; i++)
                    curSRVs[i] = Device.CreateShaderResourceView(frame.textures[i]);

                context.OMSetRenderTargets(rtv);
                context.ClearRenderTargetView(rtv, Config.Video._BackgroundColor);
                context.RSSetViewport(viewport);
                context.PSSetShader(PSShaders["FlyleafPS"]);
                context.PSSetSampler(0, samplerLinear);
                context.PSSetShaderResources(0, curSRVs);
                context.Draw(6, 0);

                for (int i=0; i<curSRVs.Length; i++)
                    curSRVs[i].Dispose();
            }
        }

        public void RefreshLayout()
        {
            if (Monitor.TryEnter(lockDevice, 5))
            {
                try
                {
                    if (SCDisposed)
                    {
                        if (ControlHandle != IntPtr.Zero)
                        {
                            // TBR: Requires Layout Update/Refresh
                            Utils.UI(() =>
                            {
                                if (WindowRef != null)
                                    WindowRef.Background = new System.Windows.Media.SolidColorBrush(Config.Video.BackgroundColor);
                                else if (ControlRef != null)
                                    ControlRef.BackColor = Utils.WPFToWinFormsColor(Config.Video.BackgroundColor);
                            });
                        }
                    }
                    else if (LastFrame != null && (LastFrame.textures != null || LastFrame.bufRef != null))
                        PresentInternal(LastFrame);
                    else
                    {
                        context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                        swapChain.Present(Config.Video.VSync, PresentFlags.None);
                    }
                }
                catch (Exception e)
                {
                    if (CanWarn) Log.Warn($"Present idle failed {e.Message} | {Device.DeviceRemovedReason}");
                }
                finally
                {
                    Monitor.Exit(lockDevice);
                }
            }
        }
        public void ClearScreen()
        {
            VideoDecoder.DisposeFrame(LastFrame);
            Present();
        }

        /// <summary>
        /// Gets bitmap from a video frame
        /// </summary>
        /// <param name="width">Specify the width (-1: will keep the ratio based on height)</param>
        /// <param name="height">Specify the height (-1: will keep the ratio based on width)</param>
        /// <param name="frame">Video frame to process (null: will use the current/last frame)</param>
        /// <returns></returns>
        public Bitmap GetBitmap(int width = -1, int height = -1, VideoFrame frame = null)
        {
            try
            {
                lock (lockDevice)
                {
                    if (frame == null)
                        frame = LastFrame;

                    if (Disposed || frame == null || (frame.textures == null && frame.bufRef == null))
                        return null;

                    if (width == -1 && height == -1)
                    {
                        width   = VideoRect.Right;
                        height  = VideoRect.Bottom;
                    }
                    else if (width != -1 && height == -1)
                        height  = (int) (width / curRatio);
                    else if (height != -1 && width == -1)
                        width   = (int) (height * curRatio);

                    if (singleStageDesc.Width != width || singleStageDesc.Height != height)
                    {
                        singleGpu?.Dispose();
                        singleStage?.Dispose();
                        singleGpuRtv?.Dispose();

                        singleStageDesc.Width   = width;
                        singleStageDesc.Height  = height;
                        singleGpuDesc.Width     = width;
                        singleGpuDesc.Height    = height;

                        singleStage = Device.CreateTexture2D(singleStageDesc);
                        singleGpu   = Device.CreateTexture2D(singleGpuDesc);
                        singleGpuRtv= Device.CreateRenderTargetView(singleGpu);

                        singleViewport = new Viewport(width, height);
                    } 

                    PresentOffline(frame, singleGpuRtv, singleViewport);

                    if (videoProcessor == VideoProcessors.D3D11)
                        SetViewport();
                }

                context.CopyResource(singleStage, singleGpu);
                return GetBitmap(singleStage);

            } catch (Exception e)
            {
                Log.Warn($"GetBitmap failed with: {e.Message}");
                return null;
            }
        }
        public Bitmap GetBitmap(ID3D11Texture2D stageTexture)
        {
            Bitmap bitmap   = new Bitmap(stageTexture.Description.Width, stageTexture.Description.Height);
            var db          = context.Map(stageTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            var bitmapData  = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            
            if (db.RowPitch == bitmapData.Stride)
                MemoryHelpers.CopyMemory(bitmapData.Scan0, db.DataPointer, bitmap.Width * bitmap.Height * 4);
            else
            {
                var sourcePtr   = db.DataPointer;
                var destPtr     = bitmapData.Scan0;

                for (int y = 0; y < bitmap.Height; y++)
                {
                    MemoryHelpers.CopyMemory(destPtr, sourcePtr, bitmap.Width * 4);

                    sourcePtr   = IntPtr.Add(sourcePtr, db.RowPitch);
                    destPtr     = IntPtr.Add(destPtr, bitmapData.Stride);
                }
            }

            bitmap.UnlockBits(bitmapData);
            context.Unmap(stageTexture, 0);

            return bitmap;
        }

        /// <summary>
        /// Extracts a bitmap from a video frame
        /// (Currently cannot be used in parallel with the rendering)
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public Bitmap ExtractFrame(VideoFrame frame)
        {
            if (Device == null || frame == null) return null;

            int subresource = -1;

            var stageDesc = new Texture2DDescription()
            {
                Usage       = ResourceUsage.Staging,
                Width       = VideoDecoder.VideoStream.Width,
                Height      = VideoDecoder.VideoStream.Height,
                Format      = Format.B8G8R8A8_UNorm,
                ArraySize   = 1,
                MipLevels   = 1,
                BindFlags   = BindFlags.None,
                CPUAccessFlags      = CpuAccessFlags.Read,
                SampleDescription   = new SampleDescription(1, 0)
            };
            ID3D11Texture2D stage = Device.CreateTexture2D(stageDesc);

            lock (lockDevice)
            {
                while (true)
                {
                    for (int i=0; i<MaxOffScreenTextures; i++)
                        if (!backBuffer2busy[i]) { subresource = i; break;}

                    if (subresource != -1)
                        break;
                    else
                        Thread.Sleep(5);
                }

                backBuffer2busy[subresource] = true;
                PresentOffline(frame, rtv2[subresource], new Viewport(backBuffer2[subresource].Description.Width, backBuffer2[subresource].Description.Height));
                VideoDecoder.DisposeFrame(frame);

                if (curSRVs != null)
                {
                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i]?.Dispose();

                    curSRVs = null;
                }

                context.CopyResource(stage, backBuffer2[subresource]);
                backBuffer2busy[subresource] = false;
            }

            var bitmap = GetBitmap(stage);
            stage.Dispose(); // TODO use array stage
            return bitmap;
        }
    }
}