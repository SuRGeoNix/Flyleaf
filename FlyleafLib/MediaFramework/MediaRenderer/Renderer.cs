using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using SharpGen.Runtime;

using Vortice.DXGI;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

using FlyleafLib.MediaFramework.MediaFrame;
using VideoDecoder = FlyleafLib.MediaFramework.MediaDecoder.VideoDecoder;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public unsafe partial class Renderer : IDisposable
    {
        public Config           Config          => VideoDecoder?.Config;
        public Control          Control         { get; private set; }
        internal IntPtr ControlHandle; // When we re-created the swapchain so we don't access the control (which requires UI thread)

        public ID3D11Device     Device          { get; private set; }
        public bool             DisableRendering{ get; set; }
        public bool             Disposed        { get; private set; } = true;
        public RendererInfo     Info            { get; internal set; }
        public int              MaxOffScreenTextures
                                                { get; set; } = 20;
        public VideoDecoder     VideoDecoder    { get; internal set; }

        public Viewport         GetViewport     { get; private set; }

        public int              PanXOffset      { get => panXOffset; set { panXOffset = value; SetViewport(); } }
        int panXOffset;
        public int              PanYOffset      { get => panYOffset; set { panYOffset = value; SetViewport(); } }
        int panYOffset;
        public int              Zoom            { get => zoom;       set { zoom       = value; SetViewport(); } }
        int zoom;
        public int              UniqueId        { get; private set; }

        ID3D11DeviceContext                     context;
        IDXGISwapChain1                         swapChain;

        ID3D11RenderTargetView                  rtv;
        ID3D11Texture2D                         backBuffer;
        
        // Used for off screen rendering
        ID3D11RenderTargetView[]                rtv2;
        ID3D11Texture2D[]                       backBuffer2;
        bool[]                                  backBuffer2busy;

        ID3D11SamplerState                      samplerLinear;

        ID3D11PixelShader                       pixelShader;

        ID3D11Buffer                            vertexBuffer;
        ID3D11InputLayout                       vertexLayout;
        ID3D11VertexShader                      vertexShader;

        ID3D11ShaderResourceView[]              curSRVs;
        ShaderResourceViewDescription           srvDescR, srvDescRG;

        object  lockPresentTask     = new object();
        object  lockDevice          = new object();

        bool    isPresenting;
        long    lastPresentAt       = 0;
        long    lastPresentRequestAt= 0;

        public Renderer(VideoDecoder videoDecoder, Control control = null, int uniqueId = -1)
        {
            if (control != null)
            {
                Control         = control;
                ControlHandle   = control.Handle; // Requires UI Access
            }
            
            UniqueId        = uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
            VideoDecoder    = videoDecoder;
        }

        public void Initialize()
        {
            lock (lockDevice)
            {
                Log("Initializing");

                IDXGIAdapter1 adapter = null;

                if (Config.Video.GPUAdapteLuid != -1)
                {
                    for (int adapterIndex = 0; Factory.EnumAdapters1(adapterIndex, out adapter).Success; adapterIndex++)
                    {
                        if (adapter.Description1.Luid == Config.Video.GPUAdapteLuid)
                            break;

                        adapter.Dispose();
                    }

                    if (adapter == null)
                        throw new Exception($"GPU Adapter with {Config.Video.GPUAdapteLuid} has not been found");
                }

                DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;

                #if DEBUG
                if (D3D11.SdkLayersAvailable()) creationFlags |= DeviceCreationFlags.Debug;
                #endif

                // Creates the D3D11 Device based on selected adapter or default hardware (highest to lowest features and fall back to the WARP device. see http://go.microsoft.com/fwlink/?LinkId=286690)
                if (D3D11.D3D11CreateDevice(adapter, adapter == null ? DriverType.Hardware : DriverType.Unknown, creationFlags, featureLevelsAll, out ID3D11Device tempDevice).Failure)
                    if (D3D11.D3D11CreateDevice(adapter, adapter == null ? DriverType.Hardware : DriverType.Unknown, creationFlags, featureLevels, out tempDevice).Failure)
                        D3D11.D3D11CreateDevice(null, DriverType.Warp, creationFlags, featureLevels, out tempDevice).CheckError();

                Device = tempDevice. QueryInterface<ID3D11Device1>();
                context= Device.ImmediateContext;

                // Gets the default adapter from the D3D11 Device
                if (adapter == null)
                {
                    using (var deviceTmp = Device.QueryInterface<IDXGIDevice1>())
                    using (var adapterTmp = deviceTmp.GetAdapter())
                        adapter = adapterTmp.QueryInterface<IDXGIAdapter1>();
                }

                RendererInfo.Fill(this, adapter);
                Log("\r\n" + Info.ToString());

                tempDevice.Dispose();
                adapter.Dispose();
            
                using (var mthread    = Device.QueryInterface<ID3D11Multithread>()) mthread.SetMultithreadProtected(true);
                using (var dxgidevice = Device.QueryInterface<IDXGIDevice1>())      dxgidevice.MaximumFrameLatency = 1;

                if (Control != null)
                    InitializeSwapChain();

                vertexBuffer  = Device.CreateBuffer(BindFlags.VertexBuffer, vertexBufferData);

                samplerLinear = Device.CreateSamplerState(new SamplerDescription()
                {
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    Filter   = Filter.MinMagMipLinear,
                    MinLOD   = 0,
                    MaxLOD   = float.MaxValue, // Required from Win7/8?
                    MaxAnisotropy = 1
                });

                // Compile VS/PS Embedded Resource Shaders (lock any static) | level_9_3 required from Win7/8 can't use level_9_1 for ps instruction limits
                lock (vertexBufferData)
                {
                    Assembly assembly = null;
                    string profileExt = Device.FeatureLevel >= FeatureLevel.Level_11_0 ? "_5_0" : "_4_0_level_9_3";

                    if (vsBlob == null)
                    {
                        assembly = Assembly.GetExecutingAssembly();
                        using (Stream stream = assembly.GetManifestResourceStream(@"FlyleafLib.MediaFramework.MediaRenderer.Shaders.FlyleafVS.hlsl"))
                        {
                            byte[] bytes = new byte[stream.Length];
                            stream.Read(bytes, 0, bytes.Length);
                            Compiler.Compile(bytes, null, null, "main", null, $"vs{profileExt}", ShaderFlags.OptimizationLevel3, out vsBlob, out Blob vsError);
                            if (vsError != null) Log(vsError.ConvertToString());
                        }
                    }

                    if (psBlob == null)
                    {
                        if (assembly == null) assembly = Assembly.GetExecutingAssembly();

                        using (Stream stream = assembly.GetManifestResourceStream(@"FlyleafLib.MediaFramework.MediaRenderer.Shaders.FlyleafPS.hlsl"))
                        {
                            byte[] bytes = new byte[stream.Length];
                            stream.Read(bytes, 0, bytes.Length);
                            Compiler.Compile(bytes, null, null, "main", null, $"ps{profileExt}", ShaderFlags.OptimizationLevel3, out psBlob, out Blob psError);
                            if (psError != null) Log(psError.ConvertToString());
                        }
                    }
                }

                pixelShader  = Device.CreatePixelShader(psBlob);
                vertexLayout = Device.CreateInputLayout(inputElements, vsBlob);
                vertexShader = Device.CreateVertexShader(vsBlob);

                context.IASetInputLayout(vertexLayout);
                context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                context.IASetVertexBuffers(0, new VertexBufferView[] { new VertexBufferView(vertexBuffer, sizeof(float) * 5, 0) });

                context.VSSetShader(vertexShader);
                context.PSSetShader(pixelShader);
                context.PSSetSampler(0, samplerLinear);

                psBuffer = Device.CreateBuffer(new BufferDescription() 
                {
                    Usage           = ResourceUsage.Default,
                    BindFlags       = BindFlags.ConstantBuffer,
                    CpuAccessFlags  = CpuAccessFlags.None,
                    SizeInBytes     = sizeof(PSBufferType)
                });
                context.PSSetConstantBuffer(0, psBuffer);

                psBufferData.hdrmethod  = HDRtoSDRMethod.None;
                psBufferData.brightness = Config.Video.Brightness / 100.0f;
                psBufferData.contrast   = Config.Video.Contrast / 100.0f;

                context.UpdateSubresource(ref psBufferData, psBuffer);

                Disposed = false;

                Log("Initialized");
            }
        }
        public void InitializeSwapChain()
        {
            Control.Resize += ResizeBuffers;

            SwapChainDescription1 swapChainDescription = new SwapChainDescription1()
            {
                Format      = Format.B8G8R8A8_UNorm,
                //Format      = Format.R10G10B10A2_UNorm,
                Width       = Control.Width,
                Height      = Control.Height,
                AlphaMode   = AlphaMode.Ignore,
                BufferUsage = Usage.RenderTargetOutput,

                SampleDescription = new SampleDescription(1, 0)
            };

            SwapChainFullscreenDescription fullscreenDescription = new SwapChainFullscreenDescription
            {
                Windowed = true
            };

            swapChainDescription.BufferCount= 2;
            swapChainDescription.SwapEffect = SwapEffect.FlipDiscard;
            swapChain   = Factory.CreateSwapChainForHwnd(Device, ControlHandle, swapChainDescription, fullscreenDescription);
            backBuffer  = swapChain.GetBuffer<ID3D11Texture2D>(0);
            rtv         = Device.CreateRenderTargetView(backBuffer);
        }
        
        public void Dispose()
        {
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                Log("Disposing");

                if (Control != null)
                    Control.Resize -= ResizeBuffers;

                pixelShader?.Dispose();
                psBuffer?.Dispose();
                vertexShader?.Dispose();
                samplerLinear?.Dispose();
                vertexLayout?.Dispose();
                vertexBuffer?.Dispose();
                backBuffer?.Dispose();
                rtv?.Dispose();
                swapChain?.Dispose();

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
                
                Log("Disposed");
            }
        }

        internal void ResizeBuffers(object sender, EventArgs e)
        {   
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                rtv.Dispose();
                backBuffer.Dispose();
                swapChain.ResizeBuffers(0, Control.Width, Control.Height, Format.Unknown, SwapChainFlags.None);
                backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                rtv = Device.CreateRenderTargetView(backBuffer);

                SetViewport();
            }
        }
        internal void FrameResized()
        {
            // TODO: Win7 doesn't support R8G8_UNorm so use SNorm will need also unormUV on pixel shader
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                curSRVs = new ID3D11ShaderResourceView[0]; // Prevent Draw if backbuffer not set yet (otherwise we see green screen on YUV)

                srvDescR = new ShaderResourceViewDescription()
                {
                    Format = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                };

                srvDescRG = new ShaderResourceViewDescription()
                {
                    Format = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16G16_UNorm : Format.R8G8_UNorm,
                };

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

                if (VideoDecoder.VideoStream.ColorSpace == "BT2020")
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

                context.UpdateSubresource(ref psBufferData, psBuffer);
                
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
                }
            }
        }
        public void SetViewport()
        {
            float ratio;

            if (Config.Video.AspectRatio == AspectRatio.Fill || (VideoDecoder.VideoStream == null && Config.Video.AspectRatio == AspectRatio.Keep))
                ratio = Control.Width / (float)Control.Height;

            else if (Config.Video.AspectRatio == AspectRatio.Keep)
                ratio = VideoDecoder.VideoStream.AspectRatio.Value;

            else if (Config.Video.AspectRatio == AspectRatio.Custom)
                ratio = Config.Video.CustomAspectRatio.Value;

            else
                ratio = Config.Video.AspectRatio.Value;

            if (ratio <= 0) ratio = 1;

            int Height = Control.Height + (zoom * 2);
            int Width = Control.Width + (zoom * 2);

            if (Width / ratio > Height)
                GetViewport = new Viewport(((Control.Width - (Height * ratio)) / 2) + PanXOffset, 0 - zoom + PanYOffset, Height * ratio, Height, 0.0f, 1.0f);
            else
                GetViewport = new Viewport(0 - zoom + PanXOffset, ((Control.Height - (Width / ratio)) / 2) + PanYOffset, Width, Width / ratio, 0.0f, 1.0f);

            context.RSSetViewport(GetViewport.X, GetViewport.Y, GetViewport.Width, GetViewport.Height);

            Present();
        }

        public bool Present(VideoFrame frame)
        {
            if (Monitor.TryEnter(lockDevice, 5))
            {
                try
                {
                    if (VideoDecoder.VideoAccelerated)
                    {
                        curSRVs = new ID3D11ShaderResourceView[2];

                        if (frame.bufRef != null) // Config.Decoder.ZeroCopy
                        {
                            //Log($"Presenting {frame.subresource}");
                            srvDescR. Texture2DArray.FirstArraySlice = frame.subresource;
                            srvDescRG.Texture2DArray.FirstArraySlice = frame.subresource;
                            curSRVs[0]  = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescR);
                            curSRVs[1]  = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescRG);
                        }
                        else
                        {
                            curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                            curSRVs[1]  = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                        }
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
                    
                    context.PSSetShaderResources(0, curSRVs);
                    context.OMSetRenderTargets(rtv);
                    context.ClearRenderTargetView(rtv, Config.Video._BackgroundColor);
                    if (!DisableRendering) context.Draw(6, 0);
                    swapChain.Present(Config.Video.VSync, PresentFlags.None);

                } catch (Exception e) { Log($"Error {e.Message}"); } // Currently seen on video switch when vframe (last frame of previous session) has different config from the new codec (eg. HW accel.)

                if (curSRVs != null)
                {
                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i]?.Dispose();

                    curSRVs = null;
                }

                VideoDecoder.DisposeFrame(frame);

                Monitor.Exit(lockDevice);

                return true;

            } else { Log("Dropped Frame - Lock timeout " + ( frame != null ? Utils.TicksToTime(frame.timestamp) : "")); VideoDecoder.DisposeFrame(frame); }

            return false;
        }
        public void Present()
        {
            // NOTE: We don't have TimeBeginPeriod, FpsForIdle will not be accurate

            lock (lockPresentTask)
            {
                if (VideoDecoder.IsRunning) return;

                if (isPresenting) { lastPresentRequestAt = DateTime.UtcNow.Ticks; return;}
                isPresenting = true;
            }

            Task.Run(() =>
            {
                try
                {
                    do
                    {
                        if (Monitor.TryEnter(lockDevice, 5))
                        {
                            try
                            {
                                long sleepMs = DateTime.UtcNow.Ticks - lastPresentAt;
                                sleepMs = sleepMs < (long)( 1.0/Config.Player.IdleFps * 1000 * 10000) ? (long) (1.0/Config.Player.IdleFps * 1000) : 0;
                                if (sleepMs > 2)
                                    Thread.Sleep((int)sleepMs);

                                if (Disposed)
                                    return;

                                context.OMSetRenderTargets(rtv);
                                context.ClearRenderTargetView(rtv, Config.Video._BackgroundColor);

                                if (!DisableRendering && curSRVs == null) // Prevent Draw if backbuffer not set yet (otherwise we see green screen on YUV)
                                    context.Draw(6, 0);

                                swapChain.Present(Config.Video.VSync, PresentFlags.None);
                                lastPresentAt = DateTime.UtcNow.Ticks;

                                return;

                            } finally { Monitor.Exit(lockDevice); }

                        } else { Log("Dropped Present - Lock timeout"); }

                    } while (lastPresentRequestAt > lastPresentAt);

                    return;

                } finally { isPresenting = false; }
            });
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

                if (VideoDecoder.VideoAccelerated)
                {
                    curSRVs = new ID3D11ShaderResourceView[2];

                    if (frame.bufRef != null) // Config.Decoder.ZeroCopy
                    {
                        srvDescR. Texture2DArray.FirstArraySlice = frame.subresource;
                        srvDescRG.Texture2DArray.FirstArraySlice = frame.subresource;
                        curSRVs[0]  = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescR);
                        curSRVs[1]  = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescRG);
                    }
                    else
                    {
                        curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                        curSRVs[1]  = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                    }
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

                context.PSSetShaderResources(0, curSRVs);
                context.OMSetRenderTargets(rtv2[subresource]);
                //context.ClearRenderTargetView(rtv2[subresource], Config.video._ClearColor);
                context.Draw(6, 0);

                VideoDecoder.DisposeFrame(frame);

                if (curSRVs != null)
                {
                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i]?.Dispose();

                    curSRVs = null;
                }

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

        public void TakeSnapshot(string fileName, ImageFormat imageFormat = null)
        {
	        ID3D11Texture2D snapshotTexture;

	        lock (lockDevice)
            {
                rtv.Dispose();
                backBuffer.Dispose();

                swapChain.ResizeBuffers(0, VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height, Format.Unknown, SwapChainFlags.None);
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

            Thread tmp = new Thread(() =>
            {
                Bitmap snapshotBitmap = GetBitmap(snapshotTexture);
                try { snapshotBitmap.Save(fileName, imageFormat == null ? ImageFormat.Bmp : imageFormat); } catch (Exception) { }
                snapshotBitmap.Dispose();
            });
            tmp.IsBackground = true;
            tmp.Start();
        }

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Renderer] {msg}"); }
    }
}