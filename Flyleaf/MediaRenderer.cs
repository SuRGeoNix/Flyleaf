using System;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;

using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct2D1;
using SharpDX.Direct3D11;
using SharpDX.DirectWrite;
using SharpDX.D3DCompiler;
using SharpDX.Mathematics.Interop;

using Device        = SharpDX.Direct3D11.Device;
using Resource      = SharpDX.Direct3D11.Resource;
using Buffer        = SharpDX.Direct3D11.Buffer;
using InputElement  = SharpDX.Direct3D11.InputElement;
using Filter        = SharpDX.Direct3D11.Filter;
using DeviceContext = SharpDX.Direct3D11.DeviceContext;

using FactoryDX     = SharpDX.DXGI.Factory;
using Factory2D     = SharpDX.Direct2D1.Factory;
using FactoryDW     = SharpDX.DirectWrite.Factory;

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

        internal Device                     device;
        DeviceContext                       context;
        SwapChain                           swapChain;
        Surface                             surface;
        Texture2D                           backBuffer;
        RenderTargetView                    rtv;
        
        Factory2D                           factory2d;
        internal FactoryDW                  factoryWrite;
        internal RenderTarget               rtv2d;
        internal SolidColorBrush            brush2d;
        internal SolidColorBrush            brush2dOutline;

        internal OutlineRenderer outlineRenderer = new OutlineRenderer();

        PixelShader                         pixelShader, pixelShaderYUV;
        VertexShader                        vertexShader;
        Buffer                              vertexBuffer;
        InputLayout                         vertexLayout;

        MediaFrame                          lastFrame = new MediaFrame();
        Texture2D                           textureRGB;
        ShaderResourceView                  srvRGB, srvY, srvU, srvV;
        ShaderResourceViewDescription       srvDescYUV;

        VideoDevice                         videoDevice1;
        VideoProcessor                      videoProcessor;
        VideoContext                        videoContext1;
        VideoProcessorEnumerator            vpe;
        VideoProcessorContentDescription    vpcd;
        VideoProcessorOutputViewDescription vpovd;
        VideoProcessorInputViewDescription  vpivd;
        VideoProcessorInputView             vpiv;
        VideoProcessorOutputView            vpov;
        VideoProcessorStream[]              vpsa;
        #endregion

        #region Properties

        int vsync = 0;
        public bool     VSync               { get { return vsync == 1; } set { vsync = value ? 1 : 0; } }

        Color clearColor    = Color.Black;
        Color outlineColor  = Color.Black;
        public System.Drawing.Color ClearColor      { get { return System.Drawing.Color.FromArgb(clearColor.A, clearColor.R, clearColor.G, clearColor.B); } set { clearColor = new Color(value.R, value.G, value.B, value.A); } }
        public System.Drawing.Color OutlineColor    { get { return System.Drawing.Color.FromArgb(outlineColor.A, outlineColor.R, outlineColor.G, outlineColor.B); } set { outlineColor = new Color(value.R, value.G, value.B, value.A); if (rtv2d != null) brush2dOutline = new SolidColorBrush(rtv2d, outlineColor); } }

        public int OutlinePixels {  get; set; } = 1;

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
        public MediaRenderer(MediaRouter mr)
        {
            player = mr;
            factoryWrite    = new FactoryDW();
            CreateSurfaces();
        }
        public  void InitHandle(IntPtr handle)
        {
            HookHandle = handle; Initialize();
        }
        public MediaRenderer(MediaRouter mr, IntPtr handle) { player = mr; factoryWrite = new FactoryDW(); CreateSurfaces(); HookHandle = handle; Initialize(); }
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
        public  void SetSampleFrame(System.Drawing.Bitmap bitmap)
        {
            if (device == null) return;

            if (bitmap == null)
            {
                Utilities.Dispose(ref lastFrame.textureRGB); 
                context.PixelShader.SetShaderResource(0, srvRGB);
            }
            else
            {
                lastFrame.textureRGB = Utils.LoadImage(device, bitmap);
            }
            
            PresentFrame(lastFrame);
        }
        private void Initialize()
        {
            factory2d       = new Factory2D(SharpDX.Direct2D1.FactoryType.MultiThreaded, DebugLevel.Information);

            HookControl          = Control.FromHandle(HookHandle);
            HookControl.Resize  += HookResized;

            var desc = new SwapChainDescription()
            {
                BufferCount         = 1,
                ModeDescription     = new ModeDescription(0, 0, new Rational(0, 0), Format.B8G8R8A8_UNorm), // BGRA | Required for Direct2D/DirectWrite (<Win8)
                IsWindowed          = true,
                OutputHandle        = HookHandle,
                SampleDescription   = new SampleDescription(1, 0),
                SwapEffect          = SwapEffect.Discard,
                Usage               = Usage.RenderTargetOutput
            };

            /* [Enable Debug Layer]
             * https://docs.microsoft.com/en-us/windows/win32/direct3d11/overviews-direct3d-11-devices-layers
             * https://docs.microsoft.com/en-us/windows/win32/direct3d11/using-the-debug-layer-to-test-apps
             * 
             * For Windows 7 with Platform Update for Windows 7 (KB2670838) or Windows 8.x, to create a device that supports the debug layer, install the Windows Software Development Kit (SDK) for Windows 8.x to get D3D11_1SDKLayers.dll
             * For Windows 10, to create a device that supports the debug layer, enable the "Graphics Tools" optional feature. Go to the Settings panel, under System, Apps & features, Manage optional Features, Add a feature, and then look for "Graphics Tools".
             * 
             */

            // Enable on-demand to avoid "Failed to create device issue"
            //#if DEBUG
                //Device.CreateWithSwapChain(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.Debug | DeviceCreationFlags.BgraSupport, desc, out device, out swapChain);
            //#else
                Device.CreateWithSwapChain(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport, desc, out device, out swapChain);
            //#endif

            var factory     = swapChain.GetParent<FactoryDX>();
            factory.MakeWindowAssociation(HookHandle, WindowAssociationFlags.IgnoreAll);
            
            backBuffer      = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
            rtv             = new RenderTargetView(device, backBuffer);
            context         = device.ImmediateContext;
            
            factoryWrite    = new FactoryDW();
            surface         = backBuffer.QueryInterface<Surface>();
            rtv2d           = new RenderTarget(factory2d, surface, new RenderTargetProperties(new PixelFormat(Format.Unknown, SharpDX.Direct2D1.AlphaMode.Premultiplied)));
            brush2d         = new SolidColorBrush(rtv2d, Color.White);
            brush2dOutline  = new SolidColorBrush(rtv2d, outlineColor);

            outlineRenderer.renderer = this;

            string vertexProfile    = "vs_5_0";
            string pixelProfile     = "ps_5_0";

            if (device.FeatureLevel == SharpDX.Direct3D.FeatureLevel.Level_9_1 ||
                device.FeatureLevel == SharpDX.Direct3D.FeatureLevel.Level_9_2 ||
                device.FeatureLevel == SharpDX.Direct3D.FeatureLevel.Level_9_3 ||
                device.FeatureLevel == SharpDX.Direct3D.FeatureLevel.Level_10_0 ||
                device.FeatureLevel == SharpDX.Direct3D.FeatureLevel.Level_10_1)
            {
                vertexProfile   = "vs_4_0_level_9_1";
                pixelProfile    = "ps_4_0_level_9_1";
            }

            var VertexShaderByteCode    = ShaderBytecode.Compile(Properties.Resources.VertexShader,     "main", vertexProfile, ShaderFlags.Debug);
            vertexLayout    = new InputLayout (device, VertexShaderByteCode, new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float,   12, 0, InputClassification.PerVertexData, 0),
            });
            vertexShader    = new VertexShader(device, VertexShaderByteCode);

            var PixelShaderByteCode     = ShaderBytecode.Compile(Properties.Resources.PixelShader,      "main", pixelProfile, ShaderFlags.Debug);
            pixelShader     = new PixelShader (device, PixelShaderByteCode);

             var PixelShaderByteCodeYUV = ShaderBytecode.Compile(Properties.Resources.PixelShader_YUV,  "main", pixelProfile, ShaderFlags.Debug);
            pixelShaderYUV  = new PixelShader (device, PixelShaderByteCodeYUV);
            
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
                MaximumAnisotropy = 1,
                MaximumLod = float.MaxValue,
                MinimumLod = 0,
                MipLodBias = 0.0f
            });

            context.InputAssembler.InputLayout = vertexLayout;
            context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertexBuffer, Utilities.SizeOf<float>() * 5, 0));

            context.VertexShader.Set(vertexShader);
            context.PixelShader.SetSampler(0, textureSampler);
            
            textureRGB  = new Texture2D(device, new Texture2DDescription()
            {
                Usage               = ResourceUsage.Default,
                Format              = Format.R8G8B8A8_UNorm,

                Width               = HookControl.Width,
                Height              = HookControl.Height,

                BindFlags           = BindFlags.ShaderResource | BindFlags.RenderTarget,
                CpuAccessFlags      = CpuAccessFlags.None,
                OptionFlags         = ResourceOptionFlags.None,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            });

            srvDescYUV  = new ShaderResourceViewDescription();
            srvDescYUV.Dimension                   = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D;
            srvDescYUV.Format                      = Format.R8_UNorm;
            srvDescYUV.Texture2D.MostDetailedMip   = 0;
            srvDescYUV.Texture2D.MipLevels         = 1;

            // Falling back from videoDevice1/videoContext1 to videoDevice/videoContext to ensure backwards compatibility
            videoDevice1    = device.QueryInterfaceOrNull<VideoDevice>();
            videoContext1   = context.QueryInterfaceOrNull<VideoContext>();

            if (videoDevice1 == null || videoContext1 == null) { SetViewport(); return; }

            vpcd    = new VideoProcessorContentDescription()
            {
                Usage           = VideoUsage.PlaybackNormal,
                InputFrameFormat= VideoFrameFormat.Progressive,
                InputFrameRate  = new Rational(1, 1),
                OutputFrameRate = new Rational(1, 1),
                InputWidth      = 1,
                OutputWidth     = 1,
                InputHeight     = 1,
                OutputHeight    = 1
            };
            videoDevice1.CreateVideoProcessorEnumerator(ref vpcd, out vpe);
            videoDevice1.CreateVideoProcessor(vpe, 0, out videoProcessor);
            
            vpivd   = new VideoProcessorInputViewDescription()
            {
                FourCC          = 0,
                Dimension       = VpivDimension.Texture2D,
                Texture2D       = new Texture2DVpiv() { MipSlice = 0, ArraySlice = 0 }
            };
            vpovd   = new VideoProcessorOutputViewDescription() { Dimension = VpovDimension.Texture2D };
            vpsa    = new VideoProcessorStream[1];

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
                Utilities.Dispose(ref pixelShader);
                Utilities.Dispose(ref pixelShaderYUV);

                Utilities.Dispose(ref textureRGB);
                Utilities.Dispose(ref srvRGB);
                Utilities.Dispose(ref srvY);
                Utilities.Dispose(ref srvU);
                Utilities.Dispose(ref srvV);
                

                Utilities.Dispose(ref vpiv);
                Utilities.Dispose(ref vpov);
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

                if (videoContext1 != null) Utilities.Dispose(ref videoContext1);
                if (videoDevice1 != null) Utilities.Dispose(ref videoDevice1);
            }
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

                backBuffer  = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
                rtv         = new RenderTargetView(device, backBuffer);
                surface     = backBuffer.QueryInterface<Surface>();
                rtv2d       = new RenderTarget(factory2d, surface, new RenderTargetProperties(new PixelFormat(Format.Unknown, SharpDX.Direct2D1.AlphaMode.Premultiplied)));
                brush2d     = new SolidColorBrush(rtv2d, Color.White);
                brush2dOutline  = new SolidColorBrush(rtv2d, outlineColor);

                SetViewport();
                foreach (var osdsurf in osd)
                    osdsurf.Value.requiresUpdate = true;

                PresentFrame(null);
            }
        }
        public  void FrameResized(int width, int height)
        {
            lock (device)
            {
                Utilities.Dispose(ref textureRGB);
                Utilities.Dispose(ref srvRGB);

                textureRGB =  new Texture2D(device, new Texture2DDescription()
                {
                    Usage               = ResourceUsage.Default,
                    Format              = Format.R8G8B8A8_UNorm,

                    Width               = width,
                    Height              = height,

                    BindFlags           = BindFlags.ShaderResource | BindFlags.RenderTarget,
                    CpuAccessFlags      = CpuAccessFlags.None,
                    OptionFlags         = ResourceOptionFlags.None,

                    SampleDescription   = new SampleDescription(1, 0),
                    ArraySize           = 1,
                    MipLevels           = 1
                });

                srvRGB          = new ShaderResourceView(device, textureRGB);
            }

            if (videoDevice1 == null || videoContext1 == null) return;

            Utilities.Dispose(ref vpov);
            videoDevice1.CreateVideoProcessorOutputView((Resource) textureRGB, vpe, vpovd, out vpov);
        }
        private void SetViewport()
        {
            if (IsFullScreen)
            {
                float ratio = player.ViewPort == MediaRouter.ViewPorts.KEEP ? player.DecoderRatio : player.CustomRatio;
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
            else
            {
                GetViewport = new Viewport(0, 0, HookControl.Width, HookControl.Height);
                context.Rasterizer.SetViewport(0, 0, HookControl.Width, HookControl.Height);
            }
        }
        #endregion

        #region Rendering / Presentation
        private void PresentNV12P010(MediaFrame frame, bool dispose = true)
        {
            try
            {
                Utilities.Dispose(ref vpiv);

                videoDevice1.CreateVideoProcessorInputView(frame.textureHW, vpe, vpivd, out vpiv);

                VideoProcessorStream vps = new VideoProcessorStream()
                {
                    PInputSurface = vpiv,
                    Enable = new RawBool(true)
                };
                vpsa[0] = vps;
                videoContext1.VideoProcessorBlt(videoProcessor, vpov, 0, 1, vpsa);

                context.PixelShader.SetShaderResource(0, srvRGB);
                context.PixelShader.Set(pixelShader);

            } catch (Exception e) {
                Console.WriteLine(e.Message);
            } finally { if (dispose) Utilities.Dispose(ref frame.textureHW); }
        }
        private void PresentYUV     (MediaFrame frame, bool dispose = true)
        {
            try
            {
                srvY    = new ShaderResourceView(device, frame.textureY, srvDescYUV);
                srvU    = new ShaderResourceView(device, frame.textureU, srvDescYUV);
                srvV    = new ShaderResourceView(device, frame.textureV, srvDescYUV);

                context.PixelShader.SetShaderResources(0, srvY, srvU, srvV);
                context.PixelShader.Set(pixelShaderYUV);

            } catch (Exception) {
            } finally
            {
                if (dispose)
                {
                    Utilities.Dispose(ref frame.textureY);
                    Utilities.Dispose(ref frame.textureU);
                    Utilities.Dispose(ref frame.textureV);
                }

                Utilities.Dispose(ref srvY);
                Utilities.Dispose(ref srvU);
                Utilities.Dispose(ref srvV);
            }
        }
        private void PresentRGB     (MediaFrame frame, bool dispose = true)
        {
            try
            {
                srvRGB = new ShaderResourceView(device, frame.textureRGB);
                context.PixelShader.SetShaderResources(0, srvRGB);
                context.PixelShader.Set(pixelShader);

            } catch (Exception) {
            } finally
            {
                if (dispose) Utilities.Dispose(ref frame.textureRGB);

                Utilities.Dispose(ref srvRGB);
            }
        }
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
            // Design Mode Only?
            if (device == null) return;

            // Should work better in case of frame delay (to avoid more delay on screamer)
            if (Monitor.TryEnter(device, 10))
            {
                try
                {
                    if (frame != null)
                    {
                        // NV12 | P010
                        if      (frame.textureHW  != null)  PresentNV12P010(frame);
                    
                        // YUV420P
                        else if (frame.textureY   != null)  PresentYUV(frame);

                        // RGB
                        else if (frame.textureRGB != null)  PresentRGB(frame);
                    }

                    context.OutputMerger.SetRenderTargets(rtv);
                    context.ClearRenderTargetView(rtv, clearColor);
                    context.Draw(6, 0);

                    rtv2d.BeginDraw();
                    try
                    {
                        PresentOSD();
                    } finally {
                        rtv2d.EndDraw();
                    }
                    swapChain.Present(vsync, PresentFlags.None);

                } finally { Monitor.Exit(device); }

            } else { Console.WriteLine("[RENDERER] Drop Frame - Lock timeout " + ( frame != null ? Utils.TicksToTime(frame.timestamp) : "")); player.ClearVideoFrame(frame); }
        }
        
        #endregion

        #region Messages
        public void NewMessage(OSDMessage.Type type, string msg, SubStyle style, int duration = -1)
        {
            NewMessage(type, msg, new List<SubStyle>() { style }, duration);
        }
        public void NewMessage(OSDMessage.Type type, string msg = "", List<SubStyle> styles = null, int duration = -1)
        {
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