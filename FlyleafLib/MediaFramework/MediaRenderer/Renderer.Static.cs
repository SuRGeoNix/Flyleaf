using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;

using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.DXGI.Debug;
using Vortice.Direct3D;
using Vortice.Direct3D11;

using static FlyleafLib.Utils;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public partial class Renderer
    {   
        #if DEBUG
        // Should work at least from main Samples => FlyleafPlayer (WPF Control) (WPF)
        static string EmbeddedShadersFolder = @"..\..\..\..\..\..\FlyleafLib\MediaFramework\MediaRenderer\Shaders";
        #endif

        static InputElementDescription[] inputElements =
        {
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float,     0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float,        0),
        };

        static FeatureLevel[] featureLevels;
        static FeatureLevel[] featureLevelsAll;

        static Dictionary<string, Blob> VSShaderBlobs = new Dictionary<string, Blob>();
        static Dictionary<string, Blob> PSShaderBlobs = new Dictionary<string, Blob>();

        public static Dictionary<string, VideoProcessorCapsCache> VideoProcessorsCapsCache = new Dictionary<string, VideoProcessorCapsCache>();
        public class VideoProcessorCapsCache
        {
            public bool Failed = true;
            public int  TypeIndex = -1;
            public bool HLG;
            public bool HDR10Limited;
            public VideoProcessorCaps               VideoProcessorCaps;
            public VideoProcessorRateConversionCaps VideoProcessorRateConversionCaps;

            public SerializableDictionary<VideoFilters, VideoFilter> Filters { get; set; } = new SerializableDictionary<VideoFilters, VideoFilter>();
        }

        internal static VideoProcessorFilter ConvertFromVideoProcessorFilterCaps(VideoProcessorFilterCaps filter)
        {
            switch (filter)
            {
                case VideoProcessorFilterCaps.Brightness:
                    return VideoProcessorFilter.Brightness;
                case VideoProcessorFilterCaps.Contrast:
                    return VideoProcessorFilter.Contrast;
                case VideoProcessorFilterCaps.Hue:
                    return VideoProcessorFilter.Hue;
                case VideoProcessorFilterCaps.Saturation:
                    return VideoProcessorFilter.Saturation;
                case VideoProcessorFilterCaps.EdgeEnhancement:
                    return VideoProcessorFilter.EdgeEnhancement;
                case VideoProcessorFilterCaps.NoiseReduction:
                    return VideoProcessorFilter.NoiseReduction;
                case VideoProcessorFilterCaps.AnamorphicScaling:
                    return VideoProcessorFilter.AnamorphicScaling;
                case VideoProcessorFilterCaps.StereoAdjustment:
                    return VideoProcessorFilter.StereoAdjustment;

                default:
                    return VideoProcessorFilter.StereoAdjustment;
            }
        }
        internal static VideoProcessorFilterCaps ConvertFromVideoProcessorFilter(VideoProcessorFilter filter)
        {
            switch (filter)
            {
                case VideoProcessorFilter.Brightness:
                    return VideoProcessorFilterCaps.Brightness;
                case VideoProcessorFilter.Contrast:
                    return VideoProcessorFilterCaps.Contrast;
                case VideoProcessorFilter.Hue:
                    return VideoProcessorFilterCaps.Hue;
                case VideoProcessorFilter.Saturation:
                    return VideoProcessorFilterCaps.Saturation;
                case VideoProcessorFilter.EdgeEnhancement:
                    return VideoProcessorFilterCaps.EdgeEnhancement;
                case VideoProcessorFilter.NoiseReduction:
                    return VideoProcessorFilterCaps.NoiseReduction;
                case VideoProcessorFilter.AnamorphicScaling:
                    return VideoProcessorFilterCaps.AnamorphicScaling;
                case VideoProcessorFilter.StereoAdjustment:
                    return VideoProcessorFilterCaps.StereoAdjustment;

                default:
                    return VideoProcessorFilterCaps.StereoAdjustment;
            }
        }
        internal static VideoFilter ConvertFromVideoProcessorFilterRange(VideoProcessorFilterRange filter)
        {
            return new VideoFilter()
            {
                Minimum = filter.Minimum,
                Maximum = filter.Maximum,
                Value   = filter.Default,
                Step    = filter.Multiplier
            };
        }

        internal static void Start()
        {
            #if DEBUG
            if (Directory.Exists(EmbeddedShadersFolder))
                CompileEmbeddedShaders();
            else
                LoadShaders();
            #else
            LoadShaders();
            #endif
            
            List<FeatureLevel> featuresAll = new List<FeatureLevel>
            {
                FeatureLevel.Level_12_1,
                FeatureLevel.Level_12_0,
                FeatureLevel.Level_11_1
            };

            List<FeatureLevel> features = new List<FeatureLevel>
            {
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0,
                FeatureLevel.Level_9_3,
                FeatureLevel.Level_9_2,
                FeatureLevel.Level_9_1
            };

            featureLevels = new FeatureLevel[features.Count];
            featureLevelsAll = new FeatureLevel[features.Count + featuresAll.Count];

            for (int i=0; i<featuresAll.Count; i++)
                featureLevelsAll[i] = featuresAll[i];

            for (int i=0; i<features.Count; i++)
            {
                featureLevels[i] = features[i];
                featureLevelsAll[i + featuresAll.Count] = features[i];
            }
        }

        // Loads compiled blob shaders 
        private static void LoadShaders()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] shaders = assembly.GetManifestResourceNames().Where(x => GetUrlExtention(x) == "blob").ToArray();
            string tempFile = Path.GetTempFileName();

            foreach (string shader in shaders)
            {
                using (Stream stream = assembly.GetManifestResourceStream(shader))
                {
                    var shaderName = shader.Substring(0, shader.Length - 5);
                    shaderName = shaderName.Substring(shaderName.LastIndexOf('.') + 1);

                    byte[] bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);

                    Dictionary<string, Blob> curShaders = shaderName.Substring(0, 2).ToLower() == "vs" ? VSShaderBlobs : PSShaderBlobs;

                    File.WriteAllBytes(tempFile, bytes);
                    curShaders.Add(shaderName, Compiler.ReadFileToBlob(tempFile));
                }
            }
        }

        #if DEBUG
        // Use this to update blob compiled shaders if you change them (add them as embedded resources)
        private unsafe static void CompileEmbeddedShaders()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] shaders = assembly.GetManifestResourceNames().Where(x => GetUrlExtention(x) == "hlsl").ToArray();
            string profileExt = "_4_0_level_9_3";

            foreach (string shader in shaders)
                using (Stream stream = assembly.GetManifestResourceStream(shader))
                {
                    var shaderName = shader.Substring(0, shader.Length - 5);
                    shaderName = shaderName.Substring(shaderName.LastIndexOf('.') + 1);

                    Engine.Log.Debug($"[ShaderCompiler] {shaderName}");

                    string psOrvs;
                    Dictionary<string, Blob> curShaders;
                    if (shaderName.Substring(0, 2).ToLower() == "vs")
                    {
                        curShaders = VSShaderBlobs;
                        psOrvs = "vs";
                    }
                    else
                    {
                        curShaders = PSShaderBlobs;
                        psOrvs = "ps";
                    }

                    byte[] bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    
                    Compiler.Compile(bytes, null, null, "main", null, $"{psOrvs}{profileExt}", 
                        ShaderFlags.OptimizationLevel3, out Blob shaderBlob, out Blob psError);

                    if (psError != null && psError.BufferPointer != IntPtr.Zero)
                    {
                        string[] errors = BytePtrToStringUTF8((byte*)psError.BufferPointer).Split('\n');

                        foreach (var line in errors)
                            if (!string.IsNullOrWhiteSpace(line) && line.IndexOf("X3571") == -1)
                                Engine.Log.Error($"[Renderer] [{shaderName}]: {line}");
                    }

                    if (shaderBlob != null)
                    {
                        Compiler.WriteBlobToFile(shaderBlob, Path.Combine(EmbeddedShadersFolder, shaderName + ".blob"), true);
                        curShaders.Add(shaderName, shaderBlob);
                    }
                }
        }
        
        public static void ReportLiveObjects()
        {
            try
            {
                if (DXGI.DXGIGetDebugInterface1(out IDXGIDebug1 dxgiDebug).Success)
                {
                    dxgiDebug.ReportLiveObjects(DXGI.DebugAll, ReportLiveObjectFlags.Summary | ReportLiveObjectFlags.IgnoreInternal);
                    dxgiDebug.Dispose();
                }
            } catch { }
        }
        #endif
    }
}
