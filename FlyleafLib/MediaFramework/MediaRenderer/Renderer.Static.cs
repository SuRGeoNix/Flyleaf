using System;
using System.Collections.Generic;

using Vortice.DXGI;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI.Debug;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public partial class Renderer
    {
        static IDXGIFactory2                    Factory;

        static  InputElementDescription[]       inputElements =
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float,     0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float,        0),
            };

        static float[]                          vertexBufferData =
            {
                -1.0f,  -1.0f,  0,      0.0f, 1.0f,
                -1.0f,   1.0f,  0,      0.0f, 0.0f,
                 1.0f,  -1.0f,  0,      1.0f, 1.0f,
                
                 1.0f,  -1.0f,  0,      1.0f, 1.0f,
                -1.0f,   1.0f,  0,      0.0f, 0.0f,
                 1.0f,   1.0f,  0,      1.0f, 0.0f
            };

        static FeatureLevel[] featureLevels;
        static FeatureLevel[] featureLevelsAll;

        static Blob vsBlob;
        static Blob psBlob;

        static Renderer()
        {
            List<FeatureLevel> features = new List<FeatureLevel>();
            List<FeatureLevel> featuresAll = new List<FeatureLevel>();

            featuresAll.Add(FeatureLevel.Level_12_1);
            featuresAll.Add(FeatureLevel.Level_12_0);
            featuresAll.Add(FeatureLevel.Level_11_1);

            features.Add(FeatureLevel.Level_11_0);
            features.Add(FeatureLevel.Level_10_1);
            features.Add(FeatureLevel.Level_10_0);
            features.Add(FeatureLevel.Level_9_3);
            features.Add(FeatureLevel.Level_9_2);
            features.Add(FeatureLevel.Level_9_1);

            featureLevels = new FeatureLevel[features.Count];
            featureLevelsAll = new FeatureLevel[features.Count + featuresAll.Count];

            for (int i=0; i<featuresAll.Count; i++)
                featureLevelsAll[i] = featuresAll[i];

            for (int i=0; i<features.Count; i++)
            {
                featureLevels[i] = features[i];
                featureLevelsAll[i + featuresAll.Count] = features[i];
            }
             
            if (DXGI.CreateDXGIFactory1(out Factory).Failure)
                throw new InvalidOperationException("Cannot create IDXGIFactory1");
        }

        public static Dictionary<long, GPUAdapter> GetAdapters()
        {
            Dictionary<long, GPUAdapter> adapters = new Dictionary<long, GPUAdapter>();

            #if DEBUG
            Utils.Log("GPU Adapters ...");
            #endif

            for (int adapterIndex = 0; Factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1 adapter).Success; adapterIndex++)
            {
                #if DEBUG
                Utils.Log($"[#{adapterIndex+1}] {adapter.Description1.Description} (Id: {adapter.Description1.DeviceId} | Luid: {adapter.Description1.Luid}) | DVM: {adapter.Description1.DedicatedVideoMemory}");
                #endif

                if ((adapter.Description1.Flags & AdapterFlags.Software) != AdapterFlags.None)
                {
                    adapter.Dispose();
                    continue;
                }

                //Utils.Log($"[#{adapterIndex+1}] {adapter.Description.Description} ({adapter.Description.DeviceId} | {adapter.Description1.AdapterLuid} | {adapter.Description.AdapterLuid}) | {adapter.Description1.DedicatedVideoMemory}");

                bool hasOutput = false;
                adapter.EnumOutputs(0, out IDXGIOutput output);
                if (output != null)
                {
                    hasOutput = true;
                    output.Dispose();
                }

                adapters[adapter.Description1.Luid] = new GPUAdapter() { Description = adapter.Description1.Description, Luid = adapter.Description1.Luid, HasOutput = hasOutput };

                adapter.Dispose();
                adapter = null;
            }

            return adapters;
        }
        public static void ReportLiveObjects()
        {
            if (DXGI.DXGIGetDebugInterface1(out IDXGIDebug1 dxgiDebug).Success)
            {
                dxgiDebug.ReportLiveObjects(DXGI.DebugAll, ReportLiveObjectFlags.Summary | ReportLiveObjectFlags.IgnoreInternal);
                dxgiDebug.Dispose();
            }
        }
        public static void Swap(Renderer renderer1, Renderer renderer2)
        {
            lock (renderer1.lockDevice)
                lock (renderer2.lockDevice)
                {
                    renderer1.Control.Resize -= renderer1.ResizeBuffers;
                    renderer1.rtv.Dispose();
                    renderer1.backBuffer.Dispose();
                    renderer1.swapChain.Dispose();
                    renderer1.context.Flush();

                    renderer2.Control.Resize -= renderer2.ResizeBuffers;
                    renderer2.rtv.Dispose();
                    renderer2.backBuffer.Dispose();
                    renderer2.swapChain.Dispose();
                    renderer2.context.Flush();

                    var saveControl = renderer1.Control;
                    var saveControlHandle = renderer1.ControlHandle;

                    renderer1.Control = renderer2.Control;
                    renderer1.ControlHandle = renderer2.ControlHandle;

                    renderer2.Control = saveControl;
                    renderer2.ControlHandle = saveControlHandle;

                    renderer1.InitializeSwapChain();
                    renderer2.InitializeSwapChain();

                    renderer1.ResizeBuffers(null, null);
                    renderer2.ResizeBuffers(null, null);
                }
        }
    }
}
