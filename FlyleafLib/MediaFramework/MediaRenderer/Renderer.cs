using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D11;

using Device        = SharpDX.Direct3D11.Device;
using Resource      = SharpDX.Direct3D11.Resource;
using Buffer        = SharpDX.Direct3D11.Buffer;
using InputElement  = SharpDX.Direct3D11.InputElement;
using Filter        = SharpDX.Direct3D11.Filter;
using DeviceContext = SharpDX.Direct3D11.DeviceContext;

using FlyleafLib.MediaFramework.MediaFrame;
using VideoDecoder = FlyleafLib.MediaFramework.MediaDecoder.VideoDecoder;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public unsafe class Renderer : IDisposable
    {
        public Config           Config          { get; private set; }
        public Control          Control         { get; private set; }
        public Device           Device          { get; private set; }
        public Viewport         GetViewport     { get; private set; }
        public RendererInfo     Info            { get; internal set; }
        public int              UniqueId        { get; private set; }
        public VideoDecoder     VideoDecoder    { get; internal set; }
        public int              Zoom            { get => zoom; set { zoom = value; SetViewport(); if (!VideoDecoder.IsRunning) PresentFrame(); } }
        int zoom;

        //DeviceDebug                         deviceDbg;
        
        DeviceContext                       context;
        SwapChain1                          swapChain;
        Texture2D                           backBuffer;
        RenderTargetView                    rtv;

        Buffer                              vertexBuffer;
        InputLayout                         vertexLayout;

        PixelShader                         curPixelShader;
        ShaderResourceView[]                curSRVs;
        ShaderResourceViewDescription       srvDescR, srvDescRG;

        Dictionary<string, PixelShader>     pixelShaders;    
        VertexShader                        vertexShader;

        static InputElement[]               inputElements =
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float,     0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float,        0),
                //new InputElement("COLOR",    0, Format.R32G32B32A32_Float,  0)
            };
        static float[]                      vertexBufferData =
            {
                -1.0f,  -1.0f,  0,      0.0f, 1.0f,
                -1.0f,   1.0f,  0,      0.0f, 0.0f,
                 1.0f,  -1.0f,  0,      1.0f, 1.0f,
                
                 1.0f,  -1.0f,  0,      1.0f, 1.0f,
                -1.0f,   1.0f,  0,      0.0f, 0.0f,
                 1.0f,   1.0f,  0,      1.0f, 0.0f
            };

        public Renderer(Control control, Config cfg, int uniqueId = -1)
        {
            UniqueId= uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;

            this.Config = cfg;
            Control = control;
            UniqueId = uniqueId;
            Control.Resize += ResizeBuffers;
            
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

            Device = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport);
            //device = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport | DeviceCreationFlags.Debug);
            //deviceDbg = new DeviceDebug(device); // To Report Live Objects if required
            using (var mthread = Device.QueryInterface<Multithread>()) mthread.SetMultithreadProtected(true);

            using (var device2 = Device.QueryInterface<SharpDX.DXGI.Device2>())
            using (var adapter = device2.Adapter)
            using (var factory = adapter.GetParent<Factory2>())
            {
                device2.MaximumFrameLatency = 1; // Dont queue more than 1 frame

                RendererInfo.Fill(this, adapter);
                Log("\r\n" + Info.ToString());

                // Swap Chain (TODO: Backwards compatibility)
                var desc1 = new SwapChainDescription1()
                {
                    BufferCount = Device.FeatureLevel >= SharpDX.Direct3D.FeatureLevel.Level_11_0 ? 6 : 1,  // Should be 1 for Win < 8 | HDR 60 fps requires 6 for non drops
                    SwapEffect  = Device.FeatureLevel >= SharpDX.Direct3D.FeatureLevel.Level_11_0 ? SwapEffect.FlipSequential : SwapEffect.Discard,

                    //Format      = HDREnabled ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm, // Create always 10 bit and fallback to 8?
                    Format      = Format.B8G8R8A8_UNorm,
                    Width       = Control.Width,
                    Height      = Control.Height,
                    AlphaMode   = AlphaMode.Ignore,
                    Usage       = Usage.RenderTargetOutput,
                    Scaling     = Device.FeatureLevel >= SharpDX.Direct3D.FeatureLevel.Level_11_0 ? Scaling.None : Scaling.Stretch,
                    //Flags = SwapChainFlags.AllowModeSwitch,
                    //Flags = 0 (or if already in fullscreen while recreating -> SwapChainFlags.AllowModeSwitch)
                    SampleDescription = new SampleDescription()
                    {
                        Count   = 1,
                        Quality = 0
                    }
                };

                swapChain = new SwapChain1(factory, Device, this.Control.Handle, ref desc1);
            }
            
            backBuffer      = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
            rtv             = new RenderTargetView(Device, backBuffer);
            context         = Device.ImmediateContext;
            vertexBuffer    = Buffer.Create(Device, BindFlags.VertexBuffer, vertexBufferData);

            textureSampler = new SamplerState(Device, new SamplerStateDescription()
            {
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = Comparison.Never,
                Filter = Filter.MinMagMipLinear,
                MaximumLod = float.MaxValue
            });

            // Load Shaders Byte Code
            Dictionary<string, byte[]> Shaders = Shaders_v5.Shaders;
            if (Device.FeatureLevel < SharpDX.Direct3D.FeatureLevel.Level_11_0) Shaders = Shaders_v4.Shaders;
            
            pixelShaders = new Dictionary<string, PixelShader>();
            foreach(var entry in Shaders)
                if (entry.Key.ToString() == "VertexShader")
                {
                    vertexLayout = new InputLayout(Device, entry.Value, inputElements);
                    vertexShader = new VertexShader(Device, entry.Value);
                }
                else
                    pixelShaders.Add(entry.Key.ToString(), new PixelShader(Device, entry.Value));

            context.InputAssembler.InputLayout      = vertexLayout;
            context.InputAssembler.PrimitiveTopology= SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertexBuffer, Utilities.SizeOf<float>() * 5, 0));

            context.VertexShader.Set(vertexShader);
            context.PixelShader. SetSampler(0, textureSampler);

            GetViewport = new Viewport(0, 0, Control.Width, Control.Height);
            context.Rasterizer.SetViewport(0, 0, Control.Width, Control.Height);
        }
        SamplerState textureSampler;
        bool disposed = false;
        public void Dispose()
        {
            if (Device == null) return;

            lock (Device)
            {
                if (disposed) return;

                Control.Resize -= ResizeBuffers;

                foreach (var t1 in pixelShaders)
                    t1.Value.Dispose();

                vertexShader.Dispose();

                context.InputAssembler.Dispose();
                context.VertexShader.Dispose();
                context.PixelShader.Dispose();
                context.Rasterizer.Dispose();
                context.OutputMerger.Dispose();

                Utilities.Dispose(ref textureSampler);
                Utilities.Dispose(ref vertexLayout);
                Utilities.Dispose(ref vertexBuffer);
                Utilities.Dispose(ref backBuffer);
                Utilities.Dispose(ref rtv);

                //curPixelShader.Dispose();

                context.Flush();
                context.ClearState();
                context.Flush();
                context.ClearState();
                Utilities.Dispose(ref context);
                Utilities.Dispose(ref swapChain);

                if (curSRVs != null) { for (int i=0; i<curSRVs.Length; i++) { Utilities.Dispose(ref curSRVs[i]); } curSRVs = null; }
                disposed = true;
            }

            //deviceDbg.ReportLiveDeviceObjects(ReportingLevel.Detail);
            //Utilities.Dispose(ref device); // This will cause stack overflow sometimes
            
            pixelShaders = null;
            vertexShader = null;
            vertexLayout = null;
            Device = null;
        }

        private void ResizeBuffers(object sender, EventArgs e)
        {
            if (Device == null) return;
            
            lock (Device)
            {
                Utilities.Dispose(ref rtv);
                Utilities.Dispose(ref backBuffer);

                swapChain.ResizeBuffers(0, Control.Width, Control.Height, Format.Unknown, SwapChainFlags.None);
                backBuffer  = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
                rtv         = new RenderTargetView(Device, backBuffer);

                SetViewport();
                PresentFrame(null);
            }
        }
        internal void FrameResized()
        {
            lock (Device)
            {
                srvDescR = new ShaderResourceViewDescription()
                {
                    Format      = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                    Dimension   = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                    Texture2D   = new ShaderResourceViewDescription.Texture2DResource()
                    {
                        MipLevels       = 1,
                        MostDetailedMip = 0
                    }
                };

                srvDescRG = new ShaderResourceViewDescription()
                {
                    Format      = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16G16_UNorm : Format.R8G8_UNorm,
                    Dimension   = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                    Texture2D   = new ShaderResourceViewDescription.Texture2DResource()
                    {
                        MipLevels       = 1,
                        MostDetailedMip = 0
                    }
                };

                string yuvtype = "";
                string curPixelShaderStr = "";

                if (VideoDecoder.VideoAccelerated)
                    yuvtype = "Y_UV";
                else if (VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                    yuvtype = "Y_U_V";
                else
                    curPixelShaderStr = "PixelShader";

                if (yuvtype != "") curPixelShaderStr = $"{VideoDecoder.VideoStream.ColorSpace}_{yuvtype}_{VideoDecoder.VideoStream.ColorRange}";
                
                Log($"Selected PixelShader: {curPixelShaderStr}");
                curPixelShader = pixelShaders[curPixelShaderStr];

                SetViewport();
            }
        }
        public void SetViewport()
        {
            if (Config.video.AspectRatio == AspectRatio.Fill || (Config.video.AspectRatio == AspectRatio.Keep && VideoDecoder.VideoStream == null))// || !player.Session.CanPlay)
            {
                GetViewport     = new Viewport(0, 0, Control.Width, Control.Height);
                context.Rasterizer.SetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
            }
            else
            {
                float ratio = Config.video.AspectRatio == AspectRatio.Keep ? VideoDecoder.VideoStream.AspectRatio.Value : (Config.video.AspectRatio == AspectRatio.Custom ? Config.video.CustomAspectRatio.Value : Config.video.AspectRatio.Value);
                if (ratio <= 0) ratio = 1;

                if (Control.Width / ratio > Control.Height)
                {
                    GetViewport = new Viewport((int)(Control.Width - (Control.Height * ratio)) / 2, 0 ,(int) (Control.Height * ratio),Control.Height, 0.0f, 1.0f);
                    context.Rasterizer.SetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
                }
                else
                {
                    GetViewport = new Viewport(0,(int)(Control.Height - (Control.Width / ratio)) / 2, Control.Width,(int) (Control.Width / ratio), 0.0f, 1.0f);
                    context.Rasterizer.SetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
                }
            }
        }

        public bool PresentFrame(VideoFrame frame = null)
        {
            if (Device == null) return false;

            // Drop Frames | Priority on video frames
            bool gotIn = frame == null ? Monitor.TryEnter(Device, 1) : Monitor.TryEnter(Device, 5);

            if (gotIn)
            {
                if (rtv == null) return false;

                try
                {
                    if (frame != null)
                    {
                        if (VideoDecoder.VideoAccelerated)
                        {
                            curSRVs     = new ShaderResourceView[2];
                            curSRVs[0]  = new ShaderResourceView(Device, frame.textures[0], srvDescR);
                            curSRVs[1]  = new ShaderResourceView(Device, frame.textures[0], srvDescRG);
                        }
                        else if (VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                        {
                            curSRVs     = new ShaderResourceView[3];
                            curSRVs[0]  = new ShaderResourceView(Device, frame.textures[0]);
                            curSRVs[1]  = new ShaderResourceView(Device, frame.textures[1]);
                            curSRVs[2]  = new ShaderResourceView(Device, frame.textures[2]);
                        }
                        else
                        {
                            curSRVs     = new ShaderResourceView[1];
                            curSRVs[0]  = new ShaderResourceView(Device, frame.textures[0]);
                        }

                        context.PixelShader.Set(curPixelShader);
                        context.PixelShader.SetShaderResources(0, curSRVs);
                    }

                    context.OutputMerger.SetRenderTargets(rtv);
                    context.ClearRenderTargetView(rtv, Config.video._ClearColor);
                    context.Draw(6, 0);

                    swapChain.Present(Config.video.VSync, PresentFlags.None);

                    if (frame != null)
                    {
                        if (frame.textures  != null)   for (int i=0; i<frame.textures.Length; i++) Utilities.Dispose(ref frame.textures[i]);
                        if (curSRVs         != null) { for (int i=0; i<curSRVs.Length; i++)      { Utilities.Dispose(ref curSRVs[i]); } curSRVs = null; }
                    }

                } catch (Exception e) { Log($"Error {e.Message}"); // Currently seen on video switch when vframe (last frame of previous session) has different config from the new codec (eg. HW accel.)
                } finally { Monitor.Exit(Device); }

                return true;

            } else { Log("Dropped Frame - Lock timeout " + ( frame != null ? Utils.TicksToTime(frame.timestamp) : "")); VideoDecoder.DisposeFrame(frame); }

            return false;
        }

        public void TakeSnapshot(string fileName)
        {
	        Texture2D snapshotTexture;

	        lock (Device)
            {
                Utilities.Dispose(ref rtv);
                Utilities.Dispose(ref backBuffer);

                swapChain.ResizeBuffers(0, VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height, Format.Unknown, SwapChainFlags.None);
                backBuffer  = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
                rtv         = new RenderTargetView(Device, backBuffer);
                context.Rasterizer.SetViewport(0, 0, backBuffer.Description.Width, backBuffer.Description.Height);

                for (int i=0; i<swapChain.Description.BufferCount; i++)
                { 
	                context.OutputMerger.SetRenderTargets(rtv);
	                context.ClearRenderTargetView(rtv, Config.video._ClearColor);
	                context.Draw(6, 0);
	                swapChain.Present(Config.video.VSync, PresentFlags.None);
                }
		
                snapshotTexture = new Texture2D(Device, new Texture2DDescription()
                {
	                Usage           = ResourceUsage.Staging,
	                ArraySize       = 1,
	                MipLevels       = 1,
	                Width           = backBuffer.Description.Width,
	                Height          = backBuffer.Description.Height,
	                Format          = Format.B8G8R8A8_UNorm,
	                BindFlags       = BindFlags.None,
	                CpuAccessFlags  = CpuAccessFlags.Read,
	                OptionFlags     = ResourceOptionFlags.None,
	                SampleDescription = new SampleDescription(1, 0)         
                });
                context.CopyResource(backBuffer, snapshotTexture);
                ResizeBuffers(null, null);
            }

            System.Drawing.Bitmap snapshotBitmap = new System.Drawing.Bitmap(snapshotTexture.Description.Width, snapshotTexture.Description.Height);
            DataBox db      = context.MapSubresource(snapshotTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            var bitmapData  = snapshotBitmap.LockBits(new System.Drawing.Rectangle(0, 0, snapshotBitmap.Width, snapshotBitmap.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var sourcePtr   = db.DataPointer;
            var destPtr     = bitmapData.Scan0;
            for (int y = 0; y < snapshotBitmap.Height; y++)
            {
                Utilities.CopyMemory(destPtr, sourcePtr, snapshotBitmap.Width * 4);

                sourcePtr   = IntPtr.Add(sourcePtr, db.RowPitch);
                destPtr     = IntPtr.Add(destPtr, bitmapData.Stride);
            }
            snapshotBitmap.UnlockBits(bitmapData);
            context.UnmapSubresource(snapshotTexture, 0);
            snapshotTexture.Dispose();

            try { snapshotBitmap.Save(fileName); } catch (Exception) { }
            snapshotBitmap.Dispose();
        }

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Renderer] {msg}"); }
    }
}