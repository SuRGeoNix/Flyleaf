using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct2D1;
using SharpDX.Direct3D11;
using SharpDX.DirectWrite;
using SharpDX.D3DCompiler;

using Device        = SharpDX.Direct3D11.Device;
using Resource      = SharpDX.Direct3D11.Resource;
using Buffer        = SharpDX.Direct3D11.Buffer;
using InputElement  = SharpDX.Direct3D11.InputElement;
using Filter        = SharpDX.Direct3D11.Filter;
using DeviceContext = SharpDX.Direct3D11.DeviceContext;
using Point         = System.Drawing.Point;

using static SuRGeoNix.Flyleaf.OSDMessage;
using static SuRGeoNix.Flyleaf.MediaRouter;

using SuRGeoNix.Flyleaf.MediaFramework;

namespace SuRGeoNix.Flyleaf
{
    public unsafe class MediaRenderer
    {
        #region Declaration
        MediaRouter                         player;

        internal DeviceDebug                deviceDbg;
        internal Device                     device;
        DeviceContext                       context;
        SwapChain1                          swapChain;
        Surface                             surface;
        Texture2D                           backBuffer;
        RenderTargetView                    rtv;

        Dictionary<string, PixelShader>     pixelShaders;
        VertexShader                        vertexShader;
        Buffer                              vertexBuffer;
        InputLayout                         vertexLayout;

        PixelShader                         curPixelShader;
        ShaderResourceView[]                curSRVs;
        ShaderResourceViewDescription       srvDescR, srvDescRG;
        
        SharpDX.Direct2D1.Factory               factory2d;
        internal SharpDX.DirectWrite.Factory    factoryWrite;
        internal RenderTarget                   rtv2d;
        internal SolidColorBrush                brush2d;
        internal SolidColorBrush                brush2dOutline;
        internal OutlineRenderer                outlineRenderer = new OutlineRenderer();
        #endregion

        #region Properties

        int vsync = 0;
        public bool     VSync               { get { return vsync == 1; } set { vsync = value ? 1 : 0; } }

        Color clearColor    = Color.Black;
        Color outlineColor  = Color.Black;
        public System.Drawing.Color ClearColor      { get { return System.Drawing.Color.FromArgb(clearColor.A, clearColor.R, clearColor.G, clearColor.B); } set { clearColor = new Color(value.R, value.G, value.B, value.A); } }
        public System.Drawing.Color OutlineColor    { get { return System.Drawing.Color.FromArgb(outlineColor.A, outlineColor.R, outlineColor.G, outlineColor.B); } set { outlineColor = new Color(value.R, value.G, value.B, value.A); if (rtv2d != null) brush2dOutline = new SolidColorBrush(rtv2d, outlineColor); } }

        public int      OutlinePixels       { get; set; } = 1;

        public bool     OSDEnabled          { get; set; } = true;
        //public bool     HDREnabled          { get; set; } = true; // TODO
        public bool     IsFullScreen        { get; set; }
        public Viewport GetViewport         { get; private set; }
        public IntPtr   HookHandle          { get; private set; }
        public Control  HookControl         { get; private set; }
        public int      SubsPosition        { get; set; } = 0;
        
        public Dictionary<string, OSDSurface>               osd         = new Dictionary<string, OSDSurface>();
        Dictionary<OSDMessage.Type, OSDMessage>             messages    = new Dictionary<OSDMessage.Type, OSDMessage>();
        public Dictionary<OSDMessage.Type, string>          msgToSurf   = new Dictionary<OSDMessage.Type, string>();
        public Dictionary<OSDMessage.Type, VisibilityMode>  msgToVis    = new Dictionary<OSDMessage.Type, VisibilityMode>();
        #endregion

        #region Initialize / Dispose
        public MediaRenderer(MediaRouter mr)    { player = mr; factoryWrite    = new SharpDX.DirectWrite.Factory(); CreateSurfaces(); }
        public  void InitHandle(IntPtr handle)  { HookHandle = handle; Initialize(); }
        public MediaRenderer(MediaRouter mr, IntPtr handle) { player = mr; factoryWrite = new SharpDX.DirectWrite.Factory(); CreateSurfaces(); HookHandle = handle; Initialize(); }
        private void CreateSurfaces()
        {
            // Surfaces Default
            osd.Add("tl",   new OSDSurface(this, OSDSurface.Alignment.TOPLEFT,      new Point( 12, 12), "Perpetua", 26));
            osd.Add("tr",   new OSDSurface(this, OSDSurface.Alignment.TOPRIGHT,     new Point(-12, 12), "Perpetua", 26));
            osd.Add("tl2",  new OSDSurface(this, OSDSurface.Alignment.TOPLEFT,      new Point( 12, 60), "Perpetua", 26));
            osd.Add("tr2",  new OSDSurface(this, OSDSurface.Alignment.TOPRIGHT,     new Point(-12, 60), "Perpetua", 26));
            osd.Add("bl",   new OSDSurface(this, OSDSurface.Alignment.BOTTOMLEFT,   new Point( 12,-12), "Perpetua", 26));
            osd.Add("br",   new OSDSurface(this, OSDSurface.Alignment.BOTTOMRIGHT,  new Point(-12,-40), "Perpetua", 26));
            osd.Add("bc",   new OSDSurface(this, OSDSurface.Alignment.BOTTOMCENTER, new Point(  0,-55), "Arial",    39, System.Drawing.FontStyle.Bold, FontWeight.Heavy));

            foreach (var osdsurf in osd)
            {
                osdsurf.Value.name          = osdsurf.Key;
                osdsurf.Value.OnViewPort    = true;
                osdsurf.Value.color         = new Color(255, 255, 0);
                osdsurf.Value.rectColor     = new Color(78, 0, 131, 168);
                osdsurf.Value.rectPadding   = new Padding(2, -2, 2, -2);
            }

            // Subtitles Surface
            osd["br"].OnViewPort    = false;
            osd["bc"].OnViewPort    = false;
            osd["bc"].rectEnabled   = false;
            osd["bc"].color         = Color.White;

            // Messages Default
            msgToSurf[OSDMessage.Type.Time]			= "tl";
            msgToSurf[OSDMessage.Type.HardwareAcceleration] = "tl2";
            msgToSurf[OSDMessage.Type.Volume]		= "tr";
            msgToSurf[OSDMessage.Type.Play]			= "tr";
            msgToSurf[OSDMessage.Type.Paused]		= "tr";
            msgToSurf[OSDMessage.Type.Mute]			= "tr";
            msgToSurf[OSDMessage.Type.Open]			= "tl2";
            msgToSurf[OSDMessage.Type.Buffering]	= "tr";
            msgToSurf[OSDMessage.Type.Failed]		= "tr";
            msgToSurf[OSDMessage.Type.AudioDelay]	= "tl2";
            msgToSurf[OSDMessage.Type.SubsDelay]	= "tl2";
            msgToSurf[OSDMessage.Type.SubsHeight]	= "tl2";
            msgToSurf[OSDMessage.Type.SubsFontSize]	= "tl2";
            msgToSurf[OSDMessage.Type.Subtitles]    = "bc";
            msgToSurf[OSDMessage.Type.TorrentStats] = "br";
            msgToSurf[OSDMessage.Type.TopLeft]      = "tl";
            msgToSurf[OSDMessage.Type.TopLeft2]     = "tl2";
            msgToSurf[OSDMessage.Type.TopRight]     = "tr";
            msgToSurf[OSDMessage.Type.TopRight2]    = "tr2";
            msgToSurf[OSDMessage.Type.BottomLeft]   = "bl";
            msgToSurf[OSDMessage.Type.BottomRight]  = "br";

            foreach (OSDMessage.Type type in Enum.GetValues(typeof(OSDMessage.Type)))
                msgToVis[type] = VisibilityMode.Always;

            msgToVis[OSDMessage.Type.Time]          = VisibilityMode.OnActive;
            msgToVis[OSDMessage.Type.TorrentStats]  = VisibilityMode.OnActive;
        }
        public  void CreateSample(System.Drawing.Bitmap bitmap = null)
        {
            int notimeout = Int32.MaxValue;

            NewMessage(OSDMessage.Type.Volume,      null,       null, notimeout);
            NewMessage(OSDMessage.Type.Mute,        "Muted",    null, notimeout);
            NewMessage(OSDMessage.Type.Paused,      "Paused",   null, notimeout);
            NewMessage(OSDMessage.Type.HardwareAcceleration, "Hardware Acceleration On", new SubStyle(22, 2, Color.Green), notimeout);
            NewMessage(OSDMessage.Type.SubsDelay,   null,       null, notimeout);
            NewMessage(OSDMessage.Type.AudioDelay,  null,       null, notimeout);
            NewMessage(OSDMessage.Type.TorrentStats,"D: XX, W: XX | 100KB/s", null, notimeout);
            NewMessage(OSDMessage.Type.BottomLeft,  "D: XX, W: XX | 100KB/s", null, notimeout);
            NewMessage(OSDMessage.Type.Subtitles,   "SUBTITLES: Here we are, make us proud\r\n- I will do my best!", null, notimeout);

            SetSampleFrame(bitmap);
        }
        public  void SetSampleFrame(System.Drawing.Bitmap bitmap) { /* TODO */ }

        //Output6 output6;
        private void Initialize()
        {
            HookControl          = Control.FromHandle(HookHandle);
            HookControl.Resize  += HookResized;

            /* [Enable Debug Layer]
             * 
             * 1) Enable native code debugging in your main project properties
             * 2) Requires SDK
             * 
             * https://docs.microsoft.com/en-us/windows/win32/direct3d11/overviews-direct3d-11-devices-layers
             * https://docs.microsoft.com/en-us/windows/win32/direct3d11/using-the-debug-layer-to-test-apps
             * 
             * For Windows 7 with Platform Update for Windows 7 (KB2670838) or Windows 8.x, to create a device that supports the debug layer, install the Windows Software Development Kit (SDK) for Windows 8.x to get D3D11_1SDKLayers.dll
             * For Windows 10, to create a device that supports the debug layer, enable the "Graphics Tools" optional feature. Go to the Settings panel, under System, Apps & features, Manage optional Features, Add a feature, and then look for "Graphics Tools".
             */

            //OSDEnabled = false;
            //HDREnabled = true;
            //device = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport | DeviceCreationFlags.Debug, ((SharpDX.Direct3D.FeatureLevel[]) Enum.GetValues(typeof(SharpDX.Direct3D.FeatureLevel))).Reverse().ToArray() );
            //device = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport | DeviceCreationFlags.Debug, new SharpDX.Direct3D.FeatureLevel[] { SharpDX.Direct3D.FeatureLevel.Level_10_1 });
            //deviceDbg = new DeviceDebug(device); // To Report Live Objects if required
            device = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport, ((SharpDX.Direct3D.FeatureLevel[]) Enum.GetValues(typeof(SharpDX.Direct3D.FeatureLevel))).Reverse().ToArray() );
            using (var mthread = device.QueryInterface<SharpDX.Direct3D11.Multithread>()) mthread.SetMultithreadProtected(true);

            using (var device2 = device.QueryInterface<SharpDX.DXGI.Device2>())
            using (var adapter = device2.Adapter)
            using (var factory = adapter.GetParent<SharpDX.DXGI.Factory2>())
            {
                device2.MaximumFrameLatency = 1; // Dont queue more than 1 frame

                // Adapters
                #if DEBUG
                foreach (var adapter2 in factory.Adapters)
                {
                    // TODO: calc dpi (will change desktop width/height)
                    Log($"{adapter2.Description.Description} DSM: {(int)(adapter2.Description.DedicatedSystemMemory / 1024)}MB, DVM: {(int)(adapter2.Description.DedicatedVideoMemory / 1024)}MB, SSM: {(int)(adapter2.Description.SharedSystemMemory / 1024)}MB, OUT: {adapter2.Outputs.Length}");
                    foreach (var output in adapter2.Outputs)
                    {
                        //if (output6 == null) output6 = output.QueryInterface<Output6>();
                        Log($"\t - {output.Description.DeviceName} ({output.Description.DesktopBounds.Right - output.Description.DesktopBounds.Left}x{output.Description.DesktopBounds.Bottom - output.Description.DesktopBounds.Top})");
                    }
                }
                #endif

                // Swap Chain (TODO: Backwards compatibility)
                var desc1 = new SwapChainDescription1()
                {
                    BufferCount = device.FeatureLevel >= SharpDX.Direct3D.FeatureLevel.Level_11_0 ? 6 : 1,  // Should be 1 for Win < 8 | HDR 60 fps requires 6 for non drops
                    SwapEffect  = device.FeatureLevel >= SharpDX.Direct3D.FeatureLevel.Level_12_0 ? SwapEffect.FlipSequential : SwapEffect.FlipDiscard,
                    //Format      = HDREnabled ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm, // Create always 10 bit and fallback to 8?
                    Format      = Format.B8G8R8A8_UNorm,
                    //Format      = Format.R16G16B16A16_Float,
                    //Format      = Format.R10G10B10A2_UNorm,
                    Width       = HookControl.Width,
                    Height      = HookControl.Height,
                    AlphaMode   = SharpDX.DXGI.AlphaMode.Ignore,
                    Usage       = Usage.RenderTargetOutput,
                    Scaling     = Scaling.None,
                    //Flags = 0 (or if already in fullscreen while recreating -> SwapChainFlags.AllowModeSwitch)
                    SampleDescription = new SampleDescription()
                    {
                        Count   = 1,
                        Quality = 0
                    }
                };
                swapChain = new SwapChain1(factory, device, HookHandle, ref desc1);
            }
            
            backBuffer      = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
            rtv             = new RenderTargetView(device, backBuffer);
            context         = device.ImmediateContext;
            
            factoryWrite    = new SharpDX.DirectWrite.Factory();
            surface         = backBuffer.QueryInterface<Surface>();

            factory2d       = new SharpDX.Direct2D1.Factory(SharpDX.Direct2D1.FactoryType.MultiThreaded, DebugLevel.Information);
            rtv2d           = new RenderTarget(factory2d, surface, new RenderTargetProperties(new PixelFormat(Format.Unknown, SharpDX.Direct2D1.AlphaMode.Premultiplied)));
            brush2d         = new SolidColorBrush(rtv2d, Color.White);
            brush2dOutline  = new SolidColorBrush(rtv2d, outlineColor);

            outlineRenderer.renderer = this;

            InputElement[] inputElements =
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float,     0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float,        0),
                //new InputElement("COLOR",    0, Format.R32G32B32A32_Float,  0)
            };

            vertexBuffer = Buffer.Create(device, BindFlags.VertexBuffer, new[]
            {
                -1.0f,  -1.0f,  0,      0.0f, 1.0f,
                -1.0f,   1.0f,  0,      0.0f, 0.0f,
                 1.0f,  -1.0f,  0,      1.0f, 1.0f,
                
                 1.0f,  -1.0f,  0,      1.0f, 1.0f,
                -1.0f,   1.0f,  0,      0.0f, 0.0f,
                 1.0f,   1.0f,  0,      1.0f, 0.0f
            });

            SamplerState textureSampler = new SamplerState(device, new SamplerStateDescription()
            {
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = Comparison.Never,
                Filter = Filter.MinMagMipLinear,
                //Filter = Filter.MinMagMipPoint,
                //MaximumAnisotropy = 1,
                //MinimumLod = 0.05f,
                //MipLodBias = 0.0f,
                MaximumLod = float.MaxValue
            });

            // Vertex & Pixel Shader Compiler (Temporary on runtime)
            string vertexProfile    = "vs_5_0";
            string pixelProfile     = "ps_5_0";
            if (device.FeatureLevel < SharpDX.Direct3D.FeatureLevel.Level_11_0) { vertexProfile   = "vs_4_0_level_9_1"; pixelProfile    = "ps_4_0_level_9_1"; }

            pixelShaders = new Dictionary<string, PixelShader>();
            System.Resources.ResourceSet rsrcSet = Properties.Resources.ResourceManager.GetResourceSet(System.Globalization.CultureInfo.CurrentCulture, false, true);
            foreach (System.Collections.DictionaryEntry entry in rsrcSet)
                if (entry.Value is byte[])
                {
                    if (entry.Key.ToString() == "VertexShader")
                    {
                        var byteCode = ShaderBytecode.Compile((byte[])rsrcSet.GetObject(entry.Key.ToString()), "main", vertexProfile, ShaderFlags.Debug);
                        vertexLayout = new InputLayout(device, byteCode, inputElements);
                        vertexShader = new VertexShader(device, byteCode);
                    }
                    else
                    {
                        var byteCode = ShaderBytecode.Compile((byte[])rsrcSet.GetObject(entry.Key.ToString()), "main", pixelProfile, ShaderFlags.Debug);
                        pixelShaders.Add(entry.Key.ToString(), new PixelShader(device, byteCode));
                    }
                }

            context.InputAssembler.InputLayout      = vertexLayout;
            context.InputAssembler.PrimitiveTopology= SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertexBuffer, Utilities.SizeOf<float>() * 5, 0));

            context.VertexShader.Set(vertexShader);
            context.PixelShader. SetSampler(0, textureSampler);

            SetViewport();
        }
        public  void Dispose()
        {
            lock (device)
            {
                foreach (var osdsurf in osd) osdsurf.Value.Dispose();
                osd.Clear();

                Utilities.Dispose(ref vertexLayout);
                Utilities.Dispose(ref vertexShader);
                Utilities.Dispose(ref vertexBuffer);

                Utilities.Dispose(ref rtv);
                Utilities.Dispose(ref rtv2d);
                Utilities.Dispose(ref surface);
                Utilities.Dispose(ref backBuffer);
                Utilities.Dispose(ref factory2d);
                Utilities.Dispose(ref factoryWrite);
                Utilities.Dispose(ref brush2d);
                Utilities.Dispose(ref brush2dOutline);

                Utilities.Dispose(ref factoryWrite);
                Utilities.Dispose(ref factory2d);
                Utilities.Dispose(ref surface);
                Utilities.Dispose(ref swapChain);

                context.Flush();
                context.ClearState();
                Utilities.Dispose(ref context);
                Utilities.Dispose(ref vertexLayout);
                Utilities.Dispose(ref vertexBuffer);
                Utilities.Dispose(ref vertexShader);
            }

            //deviceDbg.ReportLiveDeviceObjects(ReportingLevel.Detail);
            Utilities.Dispose(ref device);
        }
        #endregion

        #region Resize / ViewPort
        public  void HookResized(object sender, EventArgs e)
        {
            if (device == null) return;

            lock (device)
            {
                if (HookControl.Width == 0 || HookControl.Height == 0)  return;

                Utilities.Dispose(ref rtv);
                Utilities.Dispose(ref rtv2d);
                Utilities.Dispose(ref surface);
                Utilities.Dispose(ref backBuffer);
                Utilities.Dispose(ref brush2d);
                Utilities.Dispose(ref brush2dOutline);
                
                swapChain.ResizeBuffers(0, HookControl.Width, HookControl.Height, Format.Unknown, SwapChainFlags.None);

                backBuffer      = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
                rtv             = new RenderTargetView(device, backBuffer);
                surface         = backBuffer.QueryInterface<Surface>();
                rtv2d           = new RenderTarget(factory2d, surface, new RenderTargetProperties(new PixelFormat(Format.Unknown, SharpDX.Direct2D1.AlphaMode.Premultiplied)));
                brush2d         = new SolidColorBrush(rtv2d, Color.White);
                brush2dOutline  = new SolidColorBrush(rtv2d, outlineColor);

                SetViewport();
                foreach (var osdsurf in osd)
                    osdsurf.Value.requiresUpdate = true;

                PresentFrame(null);
            }
        }
        public  void FrameResized(object source, EventArgs e)
        {
            lock (device)
            {
                srvDescR = new ShaderResourceViewDescription()
                {
                    Format      = player.decoder.vStreamInfo.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                    Dimension   = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                    Texture2D   = new ShaderResourceViewDescription.Texture2DResource()
                    {
                        MipLevels       = 1,
                        MostDetailedMip = 0
                    }
                };

                srvDescRG = new ShaderResourceViewDescription()
                {
                    Format      = player.decoder.vStreamInfo.PixelBits > 8 ? Format.R16G16_UNorm : Format.R8G8_UNorm,
                    Dimension   = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                    Texture2D   = new ShaderResourceViewDescription.Texture2DResource()
                    {
                        MipLevels       = 1,
                        MostDetailedMip = 0
                    }
                };

                string yuvtype = "";
                string curPixelShaderStr = "";

                if (player.decoder.vDecoder.hwAccelSuccess)
                    yuvtype = "Y_UV";
                else if (player.decoder.vStreamInfo.PixelFormatType == PixelFormatType.Software_Handled)
                    yuvtype = "Y_U_V";
                else
                    curPixelShaderStr = "PixelShader";

                if (yuvtype != "") curPixelShaderStr = $"{player.decoder.vStreamInfo.ColorSpace}_{yuvtype}_{player.decoder.vStreamInfo.ColorRange}";
                
                Log($"Selected PixelShader: {curPixelShaderStr}");
                curPixelShader = pixelShaders[curPixelShaderStr];

                //curPixelShader = pixelShaders["BT2020_Y_UV_LIMITED"];
                //curPixelShader = pixelShaders["PixelShader"];
                SetViewport();
            }
        }
        private void SetViewport()
        {
            if (!IsFullScreen || player.ViewPort == MediaRouter.ViewPorts.FILL)
            {
                GetViewport = new Viewport(0, 0, HookControl.Width, HookControl.Height);
                context.Rasterizer.SetViewport(0, 0, HookControl.Width, HookControl.Height);
            }
            else
            {
                float ratio = player.ViewPort == MediaRouter.ViewPorts.KEEP ? player.decoder.vStreamInfo.AspectRatio : player.CustomRatio;
                if (HookControl.Width / ratio > HookControl.Height)
                {
                    GetViewport = new Viewport((int)(HookControl.Width - (HookControl.Height * ratio)) / 2, 0 ,(int) (HookControl.Height * ratio),HookControl.Height, 0.0f, 1.0f);
                    context.Rasterizer.SetViewport(GetViewport);
                }
                else
                {
                    GetViewport = new Viewport(0,(int)(HookControl.Height - (HookControl.Width / ratio)) / 2, HookControl.Width,(int) (HookControl.Width / ratio), 0.0f, 1.0f);
                    context.Rasterizer.SetViewport(GetViewport);
                }
            }
        }
        #endregion

        #region Rendering / Presentation
        private void PresentOSD()
        {
            if (ShouldVisible(player.Activity,msgToVis[OSDMessage.Type.Time]))
            {
                if (player.Duration != 0)
                {
                    long curTime = player.SeekTime == -1 ? player.CurTime : player.SeekTime;

                    int percentage = (int)(((player.CurTime) / (player.Duration / 100)) + 0.3);
                    if (percentage > 100) percentage = 100;
                    if (percentage < 0) percentage = 0;

                    if (curTime > player.Duration) curTime = player.Duration;

                    osd[msgToSurf[OSDMessage.Type.Time]].DrawText((new TimeSpan(curTime)).ToString(@"hh\:mm\:ss") + " / " + (new TimeSpan(player.Duration)).ToString(@"hh\:mm\:ss") + " | " + percentage + "%");
                }
                else
                    osd[msgToSurf[OSDMessage.Type.Time]].DrawText((new TimeSpan(player.CurTime)).ToString(@"hh\:mm\:ss") + " / --:--:--");
            }
            
            lock (messages)
            { 
                if (messages.Count > 0)
                {
                    long curTicks = DateTime.UtcNow.Ticks;
                    
                    // Remove Timed-out Messages (Except subs if not playing)
                    List<OSDMessage.Type> removeKeys = new List<OSDMessage.Type>();
                    foreach (KeyValuePair<OSDMessage.Type, OSDMessage> msgKV in messages)
                        if (msgKV.Value.type == OSDMessage.Type.Subtitles)
                        {
                            if (player.CurTime - msgKV.Value.startAt > (long)msgKV.Value.duration * 10000)
                                removeKeys.Add(msgKV.Key); 
                        }
                        else if (curTicks - msgKV.Value.startAt > (long)msgKV.Value.duration * 10000) 
                        {
                            //if (msgKV.Value.type == OSDMessage.Type.Subtitles && !player.isPlaying) continue; // Dont remove subs if stopped
                            
                                removeKeys.Add(msgKV.Key); 
                        }
                    foreach (OSDMessage.Type key in removeKeys) messages.Remove(key);

                    // Add Text/SubStyles to Surfaces
                    foreach (KeyValuePair<OSDMessage.Type, OSDMessage> msgKV in messages)
                    {
                        OSDMessage msg = msgKV.Value;

                        if (!ShouldVisible(player.Activity,msgToVis[msg.type])) continue;

                        switch (msg.type)
                        {
                            case OSDMessage.Type.HardwareAcceleration:
                                msg.msg = "Hardware Acceleration " + (player.HWAccel ? "On" : "Off") + " (" + (!player.isReady || player.iSHWAccelSuccess ? "Success" : "Failed") + ")";
                                if (player.HWAccel)
                                    msg.UpdateStyle(new SubStyle(22, 2, Color.Green));
                                else
                                    msg.UpdateStyle(new SubStyle(22, 3, Color.Red));
                                
                                break;

                            case OSDMessage.Type.AudioDelay:
                                var delay = (player.AudioExternalDelay / 10000); // + AudioPlayer.NAUDIO_DELAY_MS;

                                msg.msg = "Audio Delay " + (delay > 0 ? "+" : "") + delay + "ms";
                                
                                break;

                            case OSDMessage.Type.SubsDelay:
                                msg.msg = "Subtitles Delay " + (player.SubsExternalDelay / 10000 > 0 ? "+" : "") + player.SubsExternalDelay / 10000 + "ms";
                                
                                break;

                            case OSDMessage.Type.SubsFontSize:
                                msg.msg = "Subtitles Fontsize " + player.SubsFontSize;
                                
                                break;

                            case OSDMessage.Type.SubsHeight:
                                msg.msg = "Subtitles Height " + (player.SubsPosition > 0 ? "+" : "") + player.SubsPosition;

                                break;

                            case OSDMessage.Type.Volume:
                                msg.msg = "Volume " + player.Volume + "%";
                                
                                break;
                        }

                        osd[msgToSurf[msg.type]].msgs.Add(msg);
                    }

                    foreach (var osdsurf in osd)
                        osdsurf.Value.DrawMessages();
                }
            }
        }
        public  void PresentFrame   (MediaFrame frame = null)
        {
            if (device == null) return;

            // Drop Frames | Priority on video frames
            bool gotIn = frame == null ? Monitor.TryEnter(device, 1) : Monitor.TryEnter(device, 5); // Should be calculated based on fps (also calculate time of present)

            if (gotIn)
            {
                try
                {
                    if (frame != null)
                    {
                        if (player.decoder.vDecoder.hwAccelSuccess)
                        {
                            curSRVs     = new ShaderResourceView[2];
                            curSRVs[0]  = new ShaderResourceView(device, frame.textures[0], srvDescR);
                            curSRVs[1]  = new ShaderResourceView(device, frame.textures[0], srvDescRG);
                        }
                        else if (player.decoder.vStreamInfo.PixelFormatType == PixelFormatType.Software_Handled)
                        {
                            curSRVs     = new ShaderResourceView[3];
                            curSRVs[0]  = new ShaderResourceView(device, frame.textures[0]);
                            curSRVs[1]  = new ShaderResourceView(device, frame.textures[1]);
                            curSRVs[2]  = new ShaderResourceView(device, frame.textures[2]);
                        }
                        else
                        {
                            curSRVs     = new ShaderResourceView[1];
                            curSRVs[0]  = new ShaderResourceView(device, frame.textures[0]);
                        }

                        context.PixelShader.Set(curPixelShader);
                        context.PixelShader.SetShaderResources(0, curSRVs);
                    }

                    context.OutputMerger.SetRenderTargets(rtv);
                    context.ClearRenderTargetView(rtv, clearColor);
                    context.Draw(6, 0);

                    if (OSDEnabled)
                    {
                        rtv2d.BeginDraw();
                        try
                        {
                            PresentOSD();
                        } finally {
                            rtv2d.EndDraw();
                        }
                    }
                    
                    swapChain.Present(vsync, PresentFlags.None);

                    if (frame != null)
                    {
                        if (frame.textures  != null)   for (int i=0; i<frame.textures.Length; i++) Utilities.Dispose(ref frame.textures[i]);
                        if (curSRVs         != null) { for (int i=0; i<curSRVs.Length; i++)      { Utilities.Dispose(ref curSRVs[i]); } curSRVs = null; }
                    }

                } finally { Monitor.Exit(device); }

            } else { Console.WriteLine("[RENDERER] Drop Frame - Lock timeout " + ( frame != null ? Utils.TicksToTime(frame.timestamp) : "")); player.ClearVideoFrame(frame); }
        }
        #endregion

        #region Messages
        public void NewMessage(OSDMessage.Type type, string msg, SubStyle style, int duration = -1)
        {
            if (!OSDEnabled) return;
            NewMessage(type, msg, new List<SubStyle>() { style }, duration);
        }
        public void NewMessage(OSDMessage.Type type, string msg = "", List<SubStyle> styles = null, int duration = -1)
        {
            if (!OSDEnabled) return;
            lock (messages)
            {
                messages[type] = new OSDMessage(type, msg, styles, duration);
                if (type == OSDMessage.Type.Subtitles)
                    messages[type].startAt = player.CurTime;
            }
            if (!player.isPlaying) PresentFrame(null);
        }
        public void ClearMessages() { lock(messages) messages.Clear(); }
        public void ClearMessages(params OSDMessage.Type[] types)
        {
            lock(messages) foreach (OSDMessage.Type type in types) messages.Remove(type);
        }
        #endregion

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [RENDERER] {msg}"); }

        #region HDR Testing
        /* 
         * 1) SharpDX doesn't support Output6 flags to check HDR support 
         * 2) SharpDX doesn't support HDR+ (HdrMetadataType enum)
         * 3) SharpDX HdrMetadataHdr10 structure had invalid format (solved with our custom HdrMetadataHdr10)
         * 4) SharpDX SetHDRMetaData had no effect on HDR display (will be re-tested - might an issue with hdmi cable)
         * 5) PixelShaders / Tone mappers required for non HDR displays to translate HDR->SDR
         */
        int curColor = 0;
        public void tryNext()
        {
            var t1 = SwapChain4.FromPointer<SwapChain4>(swapChain.NativePointer);
            var colors = Enum.GetNames(typeof(ColorSpaceType));
            
            for (int i = curColor; i<colors.Length; i++)
            {
                if ((int) t1.CheckColorSpaceSupport((ColorSpaceType) Enum.Parse(typeof(ColorSpaceType), colors[i])) > 0 )
                {
                    HdrMetadataHdr10 hdr10 = new HdrMetadataHdr10();
                    
                    
                    //IntPtr t21 = new IntPtr();
                    //Marshal.StructureToPtr(hdr10, t133, true);
                    //var t001 = Marshal.SizeOf(hdr10);
                    int t132 = Utilities.SizeOf<HdrMetadataHdr10>();
                    var t133 = Utilities.GetIUnknownForObject(hdr10);


                    hdr10.RedPrimary[0] = 34000;
                    hdr10.RedPrimary[1] = 16000;
                    hdr10.GreenPrimary[0] = 13250;
                    hdr10.GreenPrimary[1] = 34500;
                    hdr10.BluePrimary[0] = 7500;
                    hdr10.BluePrimary[1] = 3000;
                    hdr10.WhitePoint[0] = 15635;
                    hdr10.WhitePoint[1] = 16450;
                    hdr10.MaxMasteringLuminance = 12000000;
                    hdr10.MinMasteringLuminance = 500;
                    //hdr10.MaxContentLightLevel = 2000;
                    //hdr10.MaxFrameAverageLightLevel = 500;

                    

                    //hdr10.RedPrimary[0] = (ushort) (0.680f * 50000);
                    //hdr10.RedPrimary[1] =(ushort) (0.320f * 50000);
                    //hdr10.GreenPrimary[0] =(ushort) (0.265f * 50000);
                    //hdr10.GreenPrimary[1] =(ushort) (0.690f * 50000);
                    //hdr10.BluePrimary[0] =(ushort) (0.150f * 50000);
                    //hdr10.BluePrimary[1] =(ushort) (0.060f * 50000);
                    //hdr10.WhitePoint[0] =(ushort) (0.3127f * 50000);
                    //hdr10.WhitePoint[1] =(ushort) (0.3290f * 50000);
                    //hdr10.MaxMasteringLuminance = (1000 * 10000);
                    //hdr10.MinMasteringLuminance = (int) (0.001f * 10000);
                    //hdr10.MaxContentLightLevel = 2000;
                    //hdr10.MaxFrameAverageLightLevel = 500;

                    t1.SetHDRMetaData(HdrMetadataType.Hdr10, t132, t133);

                    //t1.ColorSpace1 = ColorSpaceType.RgbFullG22NoneP709;
                    //t1.ColorSpace1 = ColorSpaceType.RgbFullG2084NoneP2020;

                    //DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709 | SDR
                    //DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 | HDR
                    t1.ColorSpace1 = (ColorSpaceType) Enum.Parse(typeof(ColorSpaceType), colors[i]);
                    Log("Using " + (ColorSpaceType) Enum.Parse(typeof(ColorSpaceType), colors[i]));
                    curColor = i+1;
                    if (i == colors.Length -1) { curColor = 0; }
                    if (!player.isPlaying) PresentFrame();
                    break;
                }

                if (i == colors.Length -1) { curColor = 0; }
            }
        }

        public struct HdrMetadataHdr10
        {
            //
            // Summary:
            //     The chromaticity coordinates of the 1.0 red value. Index 0 contains the X coordinate
            //     and index 1 contains the Y coordinate.
            public fixed ushort RedPrimary[2];
            //
            // Summary:
            //     The chromaticity coordinates of the 1.0 green value. Index 0 contains the X coordinate
            //     and index 1 contains the Y coordinate.
            public fixed ushort GreenPrimary[2];
            //
            // Summary:
            //     The chromaticity coordinates of the 1.0 blue value. Index 0 contains the X coordinate
            //     and index 1 contains the Y coordinate.
            public fixed ushort BluePrimary[2];
            //
            // Summary:
            //     The chromaticity coordinates of the white point. Index 0 contains the X coordinate
            //     and index 1 contains the Y coordinate.
            public fixed ushort WhitePoint[2];
            //
            // Summary:
            //     The maximum number of nits of the display used to master the content. Units are
            //     0.0001 nit, so if the value is 1 nit, the value should be 10,000.
            public uint MaxMasteringLuminance;
            //
            // Summary:
            //     The minimum number of nits (in units of 0.00001 nit) of the display used to master
            //     the content.
            public uint MinMasteringLuminance;
            //
            // Summary:
            //     The maximum nit value (in units of 0.00001 nit) used anywhere in the content.
            public ushort MaxContentLightLevel;
            //
            // Summary:
            //     The per-frame average of the maximum nit values (in units of 0.00001 nit).
            public ushort MaxFrameAverageLightLevel;
        }
        #endregion

        /* NOTES
         * 
         * Text with custom effects will not have an outline (custom color/italic etc.) | possible resolve this by applying effects after glyphrun?
         * Consider using outline brush color as user defined property (check also outline pixel width if possible)
         * Running glyphrunoutline for each letter seems CPU performance issue, consider creating fonts with the outline once?
         */
        public class OutlineRenderer : TextRendererBase
        {
            public MediaRenderer           renderer;

            public override Result DrawGlyphRun(object clientDrawingContext, float baselineOriginX, float baselineOriginY, MeasuringMode measuringMode, GlyphRun glyphRun, GlyphRunDescription glyphRunDescription, ComObject clientDrawingEffect)
            {
                using (PathGeometry path = new PathGeometry(renderer.factory2d))
                {
                    using (GeometrySink sink = path.Open())
                    {
                        glyphRun.FontFace.GetGlyphRunOutline(glyphRun.FontSize, glyphRun.Indices, glyphRun.Advances, glyphRun.Offsets, glyphRun.Advances.Length, glyphRun.IsSideways, (glyphRun.BidiLevel % 2) > 0, sink);
                        sink.Close();
                    }

                    var matrix = new Matrix3x2()
                    {
                        M11 = 1,
                        M12 = 0,
                        M21 = 0,
                        M22 = 1,
                        M31 = baselineOriginX,
                        M32 = baselineOriginY
                    };

                    TransformedGeometry transformedGeometry = new TransformedGeometry(renderer.factory2d, path, matrix);
                    renderer.rtv2d.FillGeometry(transformedGeometry, renderer.brush2d);
                    renderer.rtv2d.DrawGeometry(transformedGeometry, renderer.brush2dOutline, renderer.OutlinePixels);
                    Utilities.Dispose(ref transformedGeometry);
                }

                return new Result();   
            }
        }
    }
}