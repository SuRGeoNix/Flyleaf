using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D11;

using Device        = SharpDX.Direct3D11.Device;
using Resource      = SharpDX.Direct3D11.Resource;
using Buffer        = SharpDX.Direct3D11.Buffer;
using InputElement  = SharpDX.Direct3D11.InputElement;
using Filter        = SharpDX.Direct3D11.Filter;
using DeviceContext = SharpDX.Direct3D11.DeviceContext;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaFrame;

using VideoDecoder = FlyleafLib.MediaFramework.MediaDecoder.VideoDecoder;

namespace FlyleafLib.MediaRenderer
{
    public class Renderer : IDisposable
    {
        public RendererInfo Info            { get; internal set; }
        public Viewport     GetViewport     { get; private set; }
        public int          Zoom            { get => zoom; set { zoom = value; SetViewport(); if (!player.IsPlaying) PresentFrame(); } }
        int zoom;

        Player              player;
        DecoderContext      decoder => player.decoder;
        Config              cfg     => player.Config;
        
        //DeviceDebug                         deviceDbg;
        internal Device                     device;
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

        public Renderer(Player player)
        {
            this.player = player;
            this.player.Control.Resize += ResizeBuffers;

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

            //device = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport | DeviceCreationFlags.Debug, ((SharpDX.Direct3D.FeatureLevel[]) Enum.GetValues(typeof(SharpDX.Direct3D.FeatureLevel))).Reverse().ToArray() );
            //deviceDbg = new DeviceDebug(device); // To Report Live Objects if required
            device = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport, !Utils.IsWin10 ? null : ((SharpDX.Direct3D.FeatureLevel[]) Enum.GetValues(typeof(SharpDX.Direct3D.FeatureLevel))).Reverse().ToArray());
            using (var mthread = device.QueryInterface<Multithread>()) mthread.SetMultithreadProtected(true);

            using (var device2 = device.QueryInterface<SharpDX.DXGI.Device2>())
            using (var adapter = device2.Adapter)
            using (var factory = adapter.GetParent<Factory2>())
            {
                device2.MaximumFrameLatency = 1; // Dont queue more than 1 frame

                RendererInfo.Fill(this, adapter);
                Log("\r\n" + Info.ToString());

                // Swap Chain (TODO: Backwards compatibility)
                var desc1 = new SwapChainDescription1()
                {
                    BufferCount = device.FeatureLevel >= SharpDX.Direct3D.FeatureLevel.Level_11_0 ? 6 : 1,  // Should be 1 for Win < 8 | HDR 60 fps requires 6 for non drops
                    SwapEffect  = Utils.IsWin10 ? SwapEffect.FlipSequential : SwapEffect.Discard,

                    //Format      = HDREnabled ? Format.R10G10B10A2_UNorm : Format.B8G8R8A8_UNorm, // Create always 10 bit and fallback to 8?
                    Format      = Format.B8G8R8A8_UNorm,
                    Width       = player.Control.Width,
                    Height      = player.Control.Height,
                    AlphaMode   = AlphaMode.Ignore,
                    Usage       = Usage.RenderTargetOutput,
                    Scaling     = Utils.IsWin10 ? Scaling.None : Scaling.Stretch,
                    //Flags = SwapChainFlags.AllowModeSwitch,
                    //Flags = 0 (or if already in fullscreen while recreating -> SwapChainFlags.AllowModeSwitch)
                    SampleDescription = new SampleDescription()
                    {
                        Count   = 1,
                        Quality = 0
                    }
                };

                swapChain = new SwapChain1(factory, device, this.player.Control.Handle, ref desc1);
            }
            
            backBuffer      = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
            rtv             = new RenderTargetView(device, backBuffer);
            context         = device.ImmediateContext;
            vertexBuffer    = Buffer.Create(device, BindFlags.VertexBuffer, vertexBufferData);

            SamplerState textureSampler = new SamplerState(device, new SamplerStateDescription()
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
            if (device.FeatureLevel < SharpDX.Direct3D.FeatureLevel.Level_11_0) Shaders = Shaders_v4.Shaders;
            
            pixelShaders = new Dictionary<string, PixelShader>();
            foreach(var entry in Shaders)
                if (entry.Key.ToString() == "VertexShader")
                {
                    vertexLayout = new InputLayout(device, entry.Value, inputElements);
                    vertexShader = new VertexShader(device, entry.Value);
                }
                else
                    pixelShaders.Add(entry.Key.ToString(), new PixelShader(device, entry.Value));

            context.InputAssembler.InputLayout      = vertexLayout;
            context.InputAssembler.PrimitiveTopology= SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertexBuffer, Utilities.SizeOf<float>() * 5, 0));

            context.VertexShader.Set(vertexShader);
            context.PixelShader. SetSampler(0, textureSampler);

            if (player.Status == Status.Stopped)
            {
                GetViewport = new Viewport(0, 0, player.Control.Width, player.Control.Height);
                context.Rasterizer.SetViewport(0, 0, player.Control.Width, player.Control.Height);
            }
            else
                SetViewport();
        }
        public void Dispose()
        {
            lock (device)
            {
                player.Control.Resize -= ResizeBuffers;

                Utilities.Dispose(ref rtv);
                Utilities.Dispose(ref backBuffer);
                Utilities.Dispose(ref swapChain);
                Utilities.Dispose(ref vertexLayout);
                Utilities.Dispose(ref vertexBuffer);

                context.Flush();
                context.ClearState();
                Utilities.Dispose(ref context);
            }

            Utilities.Dispose(ref device);
        }

        private void ResizeBuffers(object sender, EventArgs e)
        {
            if (device == null) return;
            
            lock (device)
            {
                Utilities.Dispose(ref rtv);
                Utilities.Dispose(ref backBuffer);

                swapChain.ResizeBuffers(0, player.Control.Width, player.Control.Height, Format.Unknown, SwapChainFlags.None);
                backBuffer  = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
                rtv         = new RenderTargetView(device, backBuffer);

                SetViewport();
                PresentFrame(null);
            }
        }
        internal void FrameResized()
        {
            lock (device)
            {
                srvDescR = new ShaderResourceViewDescription()
                {
                    Format      = decoder.VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                    Dimension   = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                    Texture2D   = new ShaderResourceViewDescription.Texture2DResource()
                    {
                        MipLevels       = 1,
                        MostDetailedMip = 0
                    }
                };

                srvDescRG = new ShaderResourceViewDescription()
                {
                    Format      = decoder.VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16G16_UNorm : Format.R8G8_UNorm,
                    Dimension   = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
                    Texture2D   = new ShaderResourceViewDescription.Texture2DResource()
                    {
                        MipLevels       = 1,
                        MostDetailedMip = 0
                    }
                };

                string yuvtype = "";
                string curPixelShaderStr = "";

                if (decoder.VideoDecoder.VideoAccelerated)
                    yuvtype = "Y_UV";
                else if (decoder.VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                    yuvtype = "Y_U_V";
                else
                    curPixelShaderStr = "PixelShader";

                if (yuvtype != "") curPixelShaderStr = $"{decoder.VideoDecoder.VideoStream.ColorSpace}_{yuvtype}_{decoder.VideoDecoder.VideoStream.ColorRange}";
                
                Log($"Selected PixelShader: {curPixelShaderStr}");
                curPixelShader = pixelShaders[curPixelShaderStr];

                SetViewport();
            }
        }
        public void SetViewport()
        {
            if (cfg.video.AspectRatio == AspectRatio.Fill || (cfg.video.AspectRatio == AspectRatio.Keep && decoder.VideoDecoder.VideoStream == null))// || !player.Session.CanPlay)
            {
                GetViewport     = new Viewport(0, 0, player.Control.Width, player.Control.Height);
                context.Rasterizer.SetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
            }
            else
            {
                float ratio = cfg.video.AspectRatio == AspectRatio.Keep ? decoder.VideoDecoder.VideoStream.AspectRatio.Value : (cfg.video.AspectRatio == AspectRatio.Custom ? cfg.video.CustomAspectRatio.Value : cfg.video.AspectRatio.Value);
                if (ratio <= 0) ratio = 1;

                if (player.Control.Width / ratio > player.Control.Height)
                {
                    GetViewport = new Viewport((int)(player.Control.Width - (player.Control.Height * ratio)) / 2, 0 ,(int) (player.Control.Height * ratio),player.Control.Height, 0.0f, 1.0f);
                    context.Rasterizer.SetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
                }
                else
                {
                    GetViewport = new Viewport(0,(int)(player.Control.Height - (player.Control.Width / ratio)) / 2, player.Control.Width,(int) (player.Control.Width / ratio), 0.0f, 1.0f);
                    context.Rasterizer.SetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
                }
            }
        }

        public void PresentFrame(VideoFrame frame = null)
        {
            if (device == null) return;

            // Drop Frames | Priority on video frames
            bool gotIn = frame == null ? Monitor.TryEnter(device, 1) : Monitor.TryEnter(device, 5);

            if (gotIn)
            {
                try
                {
                    if (frame != null)
                    {
                        if (decoder.VideoDecoder.VideoAccelerated)
                        {
                            curSRVs     = new ShaderResourceView[2];
                            curSRVs[0]  = new ShaderResourceView(device, frame.textures[0], srvDescR);
                            curSRVs[1]  = new ShaderResourceView(device, frame.textures[0], srvDescRG);
                        }
                        else if (decoder.VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
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
                    context.ClearRenderTargetView(rtv, cfg.video._ClearColor);
                    context.Draw(6, 0);

                    swapChain.Present(cfg.video.VSync, PresentFlags.None);

                    if (frame != null)
                    {
                        if (frame.textures  != null)   for (int i=0; i<frame.textures.Length; i++) Utilities.Dispose(ref frame.textures[i]);
                        if (curSRVs         != null) { for (int i=0; i<curSRVs.Length; i++)      { Utilities.Dispose(ref curSRVs[i]); } curSRVs = null; }
                    }

                } finally { Monitor.Exit(device); }

            } else { Log("Dropped Frame - Lock timeout " + ( frame != null ? Utils.TicksToTime(frame.timestamp) : "")); VideoDecoder.DisposeFrame(frame); }
        }

        public void TakeSnapshot(string fileName)
        {
	        Texture2D snapshotTexture;

	        lock (device)
            {
                Utilities.Dispose(ref rtv);
                Utilities.Dispose(ref backBuffer);

                swapChain.ResizeBuffers(0, decoder.VideoDecoder.VideoStream.Width, decoder.VideoDecoder.VideoStream.Height, Format.Unknown, SwapChainFlags.None);
                backBuffer  = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
                rtv         = new RenderTargetView(device, backBuffer);
                context.Rasterizer.SetViewport(0, 0, backBuffer.Description.Width, backBuffer.Description.Height);

                for (int i=0; i<swapChain.Description.BufferCount; i++)
                { 
	                context.OutputMerger.SetRenderTargets(rtv);
	                context.ClearRenderTargetView(rtv, cfg.video._ClearColor);
	                context.Draw(6, 0);
	                swapChain.Present(cfg.video.VSync, PresentFlags.None);
                }
		
                snapshotTexture = new Texture2D(device, new Texture2DDescription()
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

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{player.PlayerId}] [Renderer] {msg}"); }
    }
}