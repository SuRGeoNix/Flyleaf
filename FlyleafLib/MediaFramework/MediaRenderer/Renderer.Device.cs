using System.Text.RegularExpressions;

using SharpGen.Runtime;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

using FeatureLevel          = Vortice.Direct3D.FeatureLevel;
using ID3D11Device          = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext   = Vortice.Direct3D11.ID3D11DeviceContext;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaPlayer;

using static FlyleafLib.Config;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer : NotifyPropertyChanged
{
    public int                  UniqueId        { get; private set; }
    public bool                 Disposed        { get; private set; } = true;
    public SwapChain            SwapChain       { get; private set; }
    public VideoDecoder         VideoDecoder    { get; private set; }
    public readonly VideoCache  Frames;
    public Config               Config          { get; private set; }
    internal VideoConfig ucfg;

    public FeatureLevel         FeatureLevel    { get; private set; }
    static FeatureLevel[]   featureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
        FeatureLevel.Level_9_3,
        FeatureLevel.Level_9_2,
        FeatureLevel.Level_9_1
    ];

    public ID3D11Device         Device          => device;
    ID3D11Device device;
    internal object lockDevice = new();
    public IDXGIDevice1         DXGIDevice      { get; private set; }
    internal IDXGIAdapter       DXGIAdapter     { get; private set; }
    public GPUAdapter           GPUAdapter      => gpuAdapter;
    GPUAdapter gpuAdapter;
    ID3D11DeviceContext     context;
    bool                    forceWarp;

    // All VideoFrames this renderer created and not yet disposed. A frame can escape the VideoCache
    // (source switch / seek / screamer hand-off) and then be abandoned without Dispose(); its native
    // surface/SRV/VPIV (and pinned AVFrame) would only be freed by non-deterministic COM finalizers,
    // keeping the D3D11 device referenced so the driver never reclaims the surface memory. Tracking
    // them lets us dispose any survivors deterministically on source switch and on teardown.
    readonly HashSet<MediaFrame.VideoFrame> liveFrames = [];
    readonly object         lockLiveFrames = new();

    internal void TrackFrame(MediaFrame.VideoFrame frame)
    {
        frame.Owner = this;
        lock (lockLiveFrames)
            liveFrames.Add(frame);
    }

    internal void UntrackFrame(MediaFrame.VideoFrame frame)
    {
        lock (lockLiveFrames)
            liveFrames.Remove(frame);
    }

    internal void DisposeLiveFrames()
    {
        MediaFrame.VideoFrame[] survivors;
        lock (lockLiveFrames)
        {
            if (liveFrames.Count == 0)
                return;

            survivors = [.. liveFrames];
            liveFrames.Clear();
        }

        foreach (var frame in survivors)
        {
            frame.Owner = null; // already removed from set; avoid re-entrant Untrack
            frame.Dispose();
        }
    }

    // Forces D3D11 to process deferred destruction of just-released resources. D3D11 defers destroying
    // a released resource until the next Flush / GPU idle; for a paused or source-switched player there
    // is no Present to trigger it, so the freed surface memory lingers until then.
    internal void FlushContext()
    {
        lock (lockDevice)
            if (!Disposed && context != null)
                context.Flush();
    }

    internal LogHandler     Log;
    Player                  player;
    ID2D1Device             device2d;
    internal ID2D1DeviceContext
                            context2d;

    public Renderer(VideoDecoder videoDecoder, int uniqueId = -1, Player player = null)
    {
        UniqueId    = uniqueId == -1 ? GetUniqueId() : uniqueId;
        Log         = new(("[#" + UniqueId + "]").PadRight(8, ' ') + " [Renderer      ] ");
        VideoDecoder= videoDecoder;
        Config      = videoDecoder.Config;
        this.player = player;
        ucfg        = Config.Video;
        Frames      = new(Config.Decoder);
        SwapChain   = new(this);

        Init();
        SetupLocal();
    }

    void Init()
    {
        FLInit();
        D3Init();

        var confAdapter = ucfg.GPUAdapter;
        if (string.IsNullOrEmpty(confAdapter))
            return;

        if (confAdapter.Equals("WARP", StringComparison.CurrentCultureIgnoreCase))
            forceWarp = true;
        else
        {
            foreach (var gpuAdapter in Engine.Video.GPUAdapters.Values)
                if (Regex.IsMatch(gpuAdapter.Description,      confAdapter, RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(gpuAdapter.Luid.ToString(),  confAdapter, RegexOptions.IgnoreCase))
                {
                    this.gpuAdapter = gpuAdapter;
                    DXGIAdapter     = gpuAdapter.dxgiAdapter;
                    break;
                }
        }
    }

    internal void Setup()
    {
        lock (lockDevice)
            SetupLocal();
    }
    internal void SetupLocal()
    {
        DisposeLocal();

        if (CanDebug) Log.Debug("Initializing");

        DeviceCreationFlags debugFlag = DeviceCreationFlags.None;
        #if DEBUG
        if (D3D11.SdkLayersAvailable()) debugFlag |= DeviceCreationFlags.Debug;
        #endif

        Result result;

        // Config or Default (null)
        if (!forceWarp && (result = D3D11.D3D11CreateDevice(DXGIAdapter, DXGIAdapter == null ? DriverType.Hardware : DriverType.Unknown, DeviceCreationFlags.BgraSupport | debugFlag, featureLevels, out device)).Failure)
        {
            forceWarp = true;
            Log.Error($"Initialization failed ({result.NativeApiCode}). Failling back to WARP device.");
        }
        
        // Forced or Fallback to WARP
        if (forceWarp)
            if ((result = D3D11.D3D11CreateDevice(null, DriverType.Warp, debugFlag, featureLevels, out device)).Failure)
            {
                Log.Error($"WARP Initialization failed ({result.NativeApiCode})");
                return;
            }

        Disposed    = false;
        canIdle     = true;
        context     = device.ImmediateContext;
        FeatureLevel= device.FeatureLevel;
        DXGIDevice  = device.QueryInterface<IDXGIDevice1>();

        // Max Latency 1 | Multithread Protected
        DXGIDevice.MaximumFrameLatency = 1;
        using (var mthread = device.QueryInterface<ID3D11Multithread>())
            mthread.SetMultithreadProtected(true);

        // Find Device's Adapter from GPUAdapters (Dispose extra adapter) | Until we use a single device per adapter
        if (DXGIAdapter == null)
        {
            var gpuAdapters = Engine.Video.GPUAdapters;
            DXGIAdapter     = DXGIDevice.GetAdapter();
            var desc        = DXGIAdapter.Description;
            bool dxgiExists = true;

            if (!gpuAdapters.TryGetValue(desc.Luid, out gpuAdapter))
            {
                lock(Engine.Video.GPUAdapters)
                    if (!gpuAdapters.TryGetValue(desc.Luid, out gpuAdapter))
                    {
                        dxgiExists  = false;
                        gpuAdapter  = Engine.Video.GetGPUAdapter(DXGIAdapter, desc);
                        gpuAdapters.Add(GPUAdapter.Luid, GPUAdapter);
                    }
            }

            if (dxgiExists)
            {
                DXGIAdapter.Dispose();
                DXGIAdapter = GPUAdapter.dxgiAdapter;
            }   
        }

        if (ucfg.Use2DGraphics)
        {
            device2d  = D2D1.D2D1CreateDevice(DXGIDevice);
            context2d = device2d.CreateDeviceContext();
            ucfg.OnD2DInitialized(this, context2d);
        }

        FLSetup();
        D3Setup();

        if (CanInfo) Log.Info($"Initialized with Feature Level {(int)FeatureLevel >> 12}.{((int)FeatureLevel >> 8) & 0xf}");

        SwapChain.Setup();
    }

    bool isDeviceReset;
    internal void Reset(bool pausePlayer = true, bool fromDecoder = false)
    {
        lock (lockDevice)
            ResetLocal(pausePlayer, fromDecoder);
    }
    void ResetLocal(bool pausePlayer = true, bool fromDecoder = false)
    {   // Don't call this from VideoDecoder's RunInternal (deadlock)
        var stream      = VideoDecoder.VideoStream;
        var wasPlaying  = pausePlayer && player != null && player.Status == MediaPlayer.Status.Playing;
        var wasRunning  = !fromDecoder && VideoDecoder.IsRunning;
        var hadSC       = !SwapChain.Disposed;

        isDeviceReset = true;

        // Stop loops (Play or Idle)
        if (wasPlaying)
            player.Pause();
        else
            RenderIdleStop();

        if (!fromDecoder)
            VideoDecoder.Dispose();
        SetupLocal();
        isDeviceReset = false;

        if (stream == null)
            return;

        if (!fromDecoder)
        {
            VideoDecoder.Open(stream);
            VideoDecoder.keyPacketRequired  = !VideoDecoder.isIntraOnly;
            VideoDecoder.keyFrameRequired   = false;
        }

        if (wasPlaying)
            player.Play();
        else if (wasRunning)
            VideoDecoder.Start();
    }

    internal void Dispose()
    {
        lock (lockDevice)
            DisposeLocal();
    }
    void DisposeLocal()
    {
        lock (lockDevice)
        {
            if (Disposed)
                return;
            
            if (CanDebug) Log.Debug("Disposing");

            Disposed = true;

            if (!isDeviceReset)
            {   // Stop loops (deadlock from loop threads)
                RenderIdleStop();
                player?.Pause();
                VideoDecoder.Dispose();
            }
            
            SwapChain.DisposeLocal();
            if (!isDeviceReset)
                RenderIdleStop(); // Ensures it didn't start again (after CanPresent = false)
            Frames.Dispose();
            DisposeLiveFrames(); // dispose any frames that escaped the cache so no native view/AVFrame keeps the device referenced
            D3Dispose();
            FLDispose();

            if (device2d != null)
            {
                ucfg.OnD2DDisposing(this, context2d);
                context2d?. Dispose();
                device2d?.  Dispose();
            }
            
            DXGIDevice. Dispose(); DXGIDevice   = null;
            context.    ClearState();
            context.    Flush();
            context.    Dispose(); context      = null;
            device.     Dispose(); device       = null;

            #if DEBUG
            ReportLiveObjects();
            #endif

            if (CanInfo) Log.Info("Disposed");
        }
    }

    #if DEBUG
    int xx01 = 1;
    public void ReportLiveObjects()
    {
        try
        {
            Log.Debug($"======= [ReportLiveObjects: #{xx01++}] =======");

            if (DXGI.DXGIGetDebugInterface1(out Vortice.DXGI.Debug.IDXGIDebug1 dxgiDebug).Success)
            {
                dxgiDebug.ReportLiveObjects(DXGI.DebugAll, Vortice.DXGI.Debug.ReportLiveObjectFlags.Summary | Vortice.DXGI.Debug.ReportLiveObjectFlags.IgnoreInternal);
                dxgiDebug.Dispose();
            }
        } catch { }
    }
    #endif
}
