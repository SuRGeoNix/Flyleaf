using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

using SharpGen.Runtime;

using Vortice.DXGI;
using Vortice.DXGI.Debug;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

using FlyleafLib.MediaFramework.MediaFrame;
using VideoDecoder = FlyleafLib.MediaFramework.MediaDecoder.VideoDecoder;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public unsafe class Renderer : IDisposable
    {
        public Config           Config          { get; private set; }
        public int              UniqueId        { get; private set; }

        public Control          Control         { get; private set; }
        public ID3D11Device     Device          { get; private set; }
        public bool             DisableRendering{ get; set; }
        public bool             Disposed        { get; private set; } = true;
        public Viewport         GetViewport     { get; private set; }
        public RendererInfo     Info            { get; internal set; }
        public int              MaxOffScreenTextures
                                                { get; set; } = 20;
        public VideoDecoder     VideoDecoder    { get; internal set; }
        public int              Zoom            { get => zoom; set { zoom = value; SetViewport(); if (!VideoDecoder.IsRunning) PresentFrame(); } }
        int zoom;

        //DeviceDebug                       deviceDbg;
        
        ID3D11DeviceContext1                    context;
        IDXGISwapChain1                         swapChain;

        ID3D11RenderTargetView                  rtv;
        ID3D11Texture2D                         backBuffer;
        
        // Used for off screen rendering
        ID3D11RenderTargetView[]                rtv2;
        ID3D11Texture2D[]                       backBuffer2;
        bool[]                                  backBuffer2busy;

        ID3D11Buffer                            vertexBuffer;
        ID3D11InputLayout                       vertexLayout;

        ID3D11PixelShader                       curPixelShader;
        ID3D11ShaderResourceView[]              curSRVs;
        ShaderResourceViewDescription           srvDescR, srvDescRG;

        Dictionary<string, ID3D11PixelShader>   pixelShaders;    
        ID3D11VertexShader                      vertexShader;

        ID3D11SamplerState                      textureSampler;
        
        static  InputElementDescription[]       inputElements =
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float,     0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float,        0),
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

        FeatureLevel[] featureLevels = new[]
        {
            //FeatureLevel.Level_12_2,
            //FeatureLevel.Level_12_1,
            //FeatureLevel.Level_12_0,
            //FeatureLevel.Level_11_1, // If enabled and device not supported creation will fail https://docs.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-d3d11createdevice
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
            FeatureLevel.Level_9_3,
            FeatureLevel.Level_9_2,
            FeatureLevel.Level_9_1
        };

        FeatureLevel FeatureLevel;

        IDXGIFactory2 Factory;

        public Renderer(VideoDecoder videoDecoder, Config config, Control control = null, int uniqueId = -1)
        {
            Config      = config;
            Control     = control;
            UniqueId    = uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
            VideoDecoder= videoDecoder;

            if (CreateDXGIFactory1(out Factory).Failure)
            {
                throw new InvalidOperationException("Cannot create IDXGIFactory1");
            }

            using (IDXGIAdapter1 adapter = GetHardwareAdapter())
            {
                RendererInfo.Fill(this, adapter);
                Log("\r\n" + Info.ToString());

                DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;

                #if DEBUG
                if (SdkLayersAvailable()) creationFlags |= DeviceCreationFlags.Debug;
                #endif

                if (D3D11CreateDevice(adapter, DriverType.Unknown, creationFlags, featureLevels, out ID3D11Device tempDevice, out FeatureLevel, out ID3D11DeviceContext tempContext).Failure)
                {
                    // If the initialization fails, fall back to the WARP device.
                    // For more information on WARP, see:
                    // http://go.microsoft.com/fwlink/?LinkId=286690
                    D3D11CreateDevice(null, DriverType.Warp, creationFlags, featureLevels, out tempDevice, out FeatureLevel, out tempContext).CheckError();
                }

                Device = tempDevice. QueryInterface<ID3D11Device1>();
                context= tempContext.QueryInterface<ID3D11DeviceContext1>();
                tempContext.Dispose();
                tempDevice.Dispose();
            }
            
            using (var mthread = Device.QueryInterface<ID3D11Multithread>()) mthread.SetMultithreadProtected(true);

            if (Control != null)
            {
                Control.Resize += ResizeBuffers;

                SwapChainDescription1 swapChainDescription = new SwapChainDescription1()
                {
                    BufferCount = FeatureLevel >= FeatureLevel.Level_11_0 ? 6 : 1,
                    SwapEffect  = FeatureLevel >= FeatureLevel.Level_11_0 ? SwapEffect.FlipSequential : SwapEffect.Discard,

                    Format      = Format.B8G8R8A8_UNorm,
                    Width       = Control.Width,
                    Height      = Control.Height,
                    AlphaMode   = AlphaMode.Ignore,
                    Usage       = Usage.RenderTargetOutput,
                    Scaling     = FeatureLevel >= FeatureLevel.Level_11_0 ? Scaling.None : Scaling.Stretch,

                    SampleDescription = new SampleDescription(1, 0)
                };

                SwapChainFullscreenDescription fullscreenDescription = new SwapChainFullscreenDescription
                {
                    Windowed = true
                };
            
                swapChain = Factory.CreateSwapChainForHwnd(Device, Control.Handle, swapChainDescription, fullscreenDescription);
                backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                rtv = Device.CreateRenderTargetView(backBuffer);

                GetViewport = new Viewport(0, 0, Control.Width, Control.Height);
                context.RSSetViewport(GetViewport);
            }

            vertexBuffer = Device.CreateBuffer(BindFlags.VertexBuffer, vertexBufferData);

            textureSampler = Device.CreateSamplerState(new SamplerDescription()
            {
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = ComparisonFunction.Never,
                Filter = Filter.MinMagMipLinear,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
                MaxAnisotropy = 1
            });

            // Load Shaders Byte Code
            Dictionary<string, byte[]> Shaders = Shaders_v5.Shaders;
            if (FeatureLevel < FeatureLevel.Level_11_0) Shaders = Shaders_v4.Shaders;
            
            pixelShaders = new Dictionary<string, ID3D11PixelShader>();

            foreach(var entry in Shaders)
                if (entry.Key.ToString() == "VertexShader")
                {
                    vertexLayout = Device.CreateInputLayout(inputElements, entry.Value);
                    vertexShader = Device.CreateVertexShader(entry.Value);
                }
                else
                    pixelShaders.Add(entry.Key.ToString(), Device.CreatePixelShader(entry.Value));

            context.IASetInputLayout(vertexLayout);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.IASetVertexBuffers(0, new VertexBufferView(vertexBuffer, sizeof(float) * 5, 0));

            context.VSSetShader(vertexShader);
            context.PSSetSampler(0, textureSampler);

            Disposed = false;
        }
        private IDXGIAdapter1 GetHardwareAdapter()
        {
            IDXGIAdapter1 adapter = null;

            if (Config.Video.GPUAdapteLuid != -1)
            {
                for (int adapterIndex = 0; Factory.EnumAdapters1(adapterIndex, out adapter).Success; adapterIndex++)
                {
                    if (adapter.Description.Luid == Config.Video.GPUAdapteLuid)
                        return adapter;

                    adapter.Dispose();
                }

                throw new Exception($"GPU Adapter with {Config.Video.GPUAdapteLuid} has not been found");
            }
            
            IDXGIFactory6 factory6 = Factory.QueryInterfaceOrNull<IDXGIFactory6>();
            if (factory6 != null)
            {
                for (int adapterIndex = 0; factory6.EnumAdapterByGpuPreference(adapterIndex, GpuPreference.HighPerformance, out adapter).Success; adapterIndex++)
                {
                    if (adapter == null)
                        continue;

                    if ((adapter.Description1.Flags & AdapterFlags.Software) != AdapterFlags.None)
                    {
                        adapter.Dispose();
                        continue;
                    }

                    return adapter;
                }

                factory6.Dispose();
            }

            if (adapter == null)
            {
                for (int adapterIndex = 0; Factory.EnumAdapters1(adapterIndex, out adapter).Success; adapterIndex++)
                {
                    if ((adapter.Description1.Flags & AdapterFlags.Software) != AdapterFlags.None)
                    {
                        adapter.Dispose();
                        continue;
                    }

                    return adapter;
                }
            }

            return adapter;
        }
        public static Dictionary<long, GPUAdapter> GetAdapters()
        {
            Dictionary<long, GPUAdapter> adapters = new Dictionary<long, GPUAdapter>();

            if (CreateDXGIFactory1(out IDXGIFactory2 factory).Failure)
                throw new InvalidOperationException("Cannot create IDXGIFactory1");

            #if DEBUG
            Utils.Log("GPU Adapters ...");
            #endif

            for (int adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1 adapter).Success; adapterIndex++)
            {
                #if DEBUG
                Utils.Log($"[#{adapterIndex+1}] {adapter.Description.Description} ({adapter.Description.DeviceId})");
                #endif

                if ((adapter.Description1.Flags & AdapterFlags.Software) != AdapterFlags.None)
                {
                    adapter.Dispose();
                    continue;
                }

                bool hasOutput = false;
                adapter.EnumOutputs(0, out IDXGIOutput output);
                if (output != null)
                {
                    hasOutput = true;
                    output.Dispose();
                }

                adapters.Add(adapter.Description.Luid, new GPUAdapter() { Description = adapter.Description.Description, Luid = adapter.Description.Luid, HasOutput = hasOutput });

                adapter.Dispose();
                adapter = null;
            }

            factory.Dispose();

            return adapters;
        }
        public void Dispose()
        {
            if (Device == null) return;

            lock (Device)
            {
                if (Disposed) return;

                if (Control != null) Control.Resize -= ResizeBuffers;

                foreach (var pixelShader in pixelShaders)
                    pixelShader.Value.Dispose();

                vertexShader.Dispose();
                textureSampler.Dispose();
                vertexLayout.Dispose();
                vertexBuffer.Dispose();
                backBuffer.Dispose();
                rtv.Dispose();

                if (rtv2 != null)
                    for(int i=0; i<rtv2.Length-1; i++)
                        rtv2[i].Dispose();

                if (backBuffer2 != null)
                    for(int i=0; i<backBuffer2.Length-1; i++)
                        backBuffer2[i].Dispose();

                if (curSRVs != null) { for (int i=0; i<curSRVs.Length; i++) { curSRVs[i].Dispose(); curSRVs = null; } }

                
                context.ClearState();
                context.Flush();
                context.Dispose();
                Device.ImmediateContext.Dispose();
                swapChain.Dispose();
                Factory.Dispose();
                
                Disposed = true;
            }

            #if DEBUG
            if (DXGIGetDebugInterface1(out IDXGIDebug1 dxgiDebug).Success)
            {
                dxgiDebug.ReportLiveObjects(DebugAll, ReportLiveObjectFlags.Summary | ReportLiveObjectFlags.IgnoreInternal);
                dxgiDebug.Dispose();
            }
            #endif

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
                rtv.Dispose();
                backBuffer.Dispose();

                swapChain.ResizeBuffers(0, Control.Width, Control.Height, Format.Unknown, SwapChainFlags.None);
                backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                rtv = Device.CreateRenderTargetView(backBuffer);

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
                    Format = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                    ViewDimension = ShaderResourceViewDimension.Texture2D,
                    Texture2D = new Texture2DShaderResourceView()
                    {
                        MipLevels = 1,
                        MostDetailedMip = 0
                    }
                };

                srvDescRG = new ShaderResourceViewDescription()
                {
                    Format = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16G16_UNorm : Format.R8G8_UNorm,
                    ViewDimension = ShaderResourceViewDimension.Texture2D,
                    Texture2D = new Texture2DShaderResourceView()
                    {
                        MipLevels = 1,
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

                if (Control != null)
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
                    context.PSSetShader(curPixelShader);
                }
            }
        }
        public void SetViewport()
        {
            if (Config.Video.AspectRatio == AspectRatio.Fill || (Config.Video.AspectRatio == AspectRatio.Keep && VideoDecoder.VideoStream == null))
            {
                GetViewport     = new Viewport(0, 0, Control.Width, Control.Height);
                context.RSSetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
            }
            else
            {
                float ratio = Config.Video.AspectRatio == AspectRatio.Keep ? VideoDecoder.VideoStream.AspectRatio.Value : (Config.Video.AspectRatio == AspectRatio.Custom ? Config.Video.CustomAspectRatio.Value : Config.Video.AspectRatio.Value);
                if (ratio <= 0) ratio = 1;

                if (Control.Width / ratio > Control.Height)
                {
                    GetViewport = new Viewport((int)(Control.Width - (Control.Height * ratio)) / 2, 0 ,(int) (Control.Height * ratio),Control.Height, 0.0f, 1.0f);
                    context.RSSetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
                }
                else
                {
                    GetViewport = new Viewport(0,(int)(Control.Height - (Control.Width / ratio)) / 2, Control.Width,(int) (Control.Width / ratio), 0.0f, 1.0f);
                    context.RSSetViewport(GetViewport.X - zoom, GetViewport.Y - zoom, GetViewport.Width + (zoom * 2), GetViewport.Height + (zoom * 2));
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
                            curSRVs     = new ID3D11ShaderResourceView[2];
                            curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                            curSRVs[1]  = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                        }
                        else if (VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                        {
                            curSRVs     = new ID3D11ShaderResourceView[3];
                            curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0]);
                            curSRVs[1]  = Device.CreateShaderResourceView(frame.textures[1]);
                            curSRVs[2]  = Device.CreateShaderResourceView(frame.textures[2]);
                        }
                        else
                        {
                            curSRVs     = new ID3D11ShaderResourceView[1];
                            curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0]);
                        }

                        context.PSSetShader(curPixelShader);
                        context.PSSetShaderResources(0, curSRVs);
                    }

                    context.OMSetRenderTargets(rtv);
                    context.ClearRenderTargetView(rtv, Config.Video._BackgroundColor);
                    if (!DisableRendering) context.Draw(6, 0);
                    swapChain.Present(Config.Video.VSync, PresentFlags.None);
                    
                    if (frame != null)
                    {
                        if (frame.textures  != null)   for (int i=0; i<frame.textures.Length; i++) frame.textures[i].Dispose();
                        if (curSRVs         != null) { for (int i=0; i<curSRVs.Length; i++)      { curSRVs[i].Dispose(); } curSRVs = null; }
                    }

                } catch (Exception e) { Log($"Error {e.Message}"); // Currently seen on video switch when vframe (last frame of previous session) has different config from the new codec (eg. HW accel.)
                } finally { Monitor.Exit(Device); }

                return true;

            } else { Log("Dropped Frame - Lock timeout " + ( frame != null ? Utils.TicksToTime(frame.timestamp) : "")); VideoDecoder.DisposeFrame(frame); }

            return false;
        }

        public Bitmap GetBitmap(VideoFrame frame)
        {
            if (Device == null || frame == null) return null;

            int subresource = -1;

            ID3D11Texture2D stageTexture = Device.CreateTexture2D(new Texture2DDescription()
            {
	            Usage           = ResourceUsage.Staging,
	            ArraySize       = 1,
	            MipLevels       = 1,
	            Width           = backBuffer2[0].Description.Width,
	            Height          = backBuffer2[0].Description.Height,
	            Format          = Format.B8G8R8A8_UNorm,
	            BindFlags       = BindFlags.None,
	            CpuAccessFlags  = CpuAccessFlags.Read,
	            OptionFlags     = ResourceOptionFlags.None,
	            SampleDescription = new SampleDescription(1, 0)
            });

            lock (Device)
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

                if (VideoDecoder.VideoAccelerated)
                {
                    curSRVs     = new ID3D11ShaderResourceView[2];
                    curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                    curSRVs[1]  = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                }
                else if (VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                {
                    curSRVs     = new ID3D11ShaderResourceView[3];
                    curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0]);
                    curSRVs[1]  = Device.CreateShaderResourceView(frame.textures[1]);
                    curSRVs[2]  = Device.CreateShaderResourceView(frame.textures[2]);
                }
                else
                {
                    curSRVs     = new ID3D11ShaderResourceView[1];
                    curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0]);
                }

                context.PSSetShader(curPixelShader);
                context.PSSetShaderResources(0, curSRVs);
                context.OMSetRenderTargets(rtv2[subresource]);
                //context.ClearRenderTargetView(rtv2[subresource], Config.video._ClearColor);
                context.Draw(6, 0);

                for (int i=0; i<frame.textures.Length; i++) frame.textures[i].Dispose();
                for (int i=0; i<curSRVs.Length; i++)      { curSRVs[i].Dispose(); } curSRVs = null;

                context.CopyResource(stageTexture, backBuffer2[subresource]);
                backBuffer2busy[subresource] = false;
            }

            return GetBitmap(stageTexture);
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
            stageTexture.Dispose();
            
            return bitmap;
        }

        public void TakeSnapshot(string fileName)
        {
	        ID3D11Texture2D snapshotTexture;

	        lock (Device)
            {
                rtv.Dispose();
                backBuffer.Dispose();

                swapChain.ResizeBuffers(6, VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height, Format.Unknown, SwapChainFlags.None);
                backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                rtv = Device.CreateRenderTargetView(backBuffer);
                context.RSSetViewport(0, 0, backBuffer.Description.Width, backBuffer.Description.Height);

                for (int i=0; i<swapChain.Description.BufferCount; i++)
                { 
	                context.OMSetRenderTargets(rtv);
	                context.ClearRenderTargetView(rtv, Config.Video._BackgroundColor);
	                context.Draw(6, 0);
	                swapChain.Present(Config.Video.VSync, PresentFlags.None);
                }

                snapshotTexture = Device.CreateTexture2D(new Texture2DDescription()
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
                context.CopyResource(snapshotTexture, backBuffer);
                ResizeBuffers(null, null);
            }

            Bitmap snapshotBitmap = GetBitmap(snapshotTexture);
            try { snapshotBitmap.Save(fileName, ImageFormat.Bmp); } catch (Exception) { }
            snapshotBitmap.Dispose();
        }

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Renderer] {msg}"); }
    }
}