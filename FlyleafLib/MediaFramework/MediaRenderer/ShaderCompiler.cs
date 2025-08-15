using System.Collections.Generic;
using System.Text;
using System.Threading;

using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;

using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using static FlyleafLib.Utils;

namespace FlyleafLib.MediaFramework.MediaRenderer;

internal class BlobWrapper
{
    public Blob blob;
    public BlobWrapper(Blob blob) => this.blob = blob;
    public BlobWrapper() { }
}

internal static class ShaderCompiler
{
    internal static Blob VSBlob = Compile(VS, false); // TODO Embedded?

    const int MAXSIZE = 64;
    static Dictionary<string, BlobWrapper> cache = new();

    internal static ID3D11PixelShader CompilePS(ID3D11Device device, string uniqueId, string hlslSample, List<string> defines = null)
    {
        BlobWrapper bw;

        lock (cache)
        {
            if (cache.Count > MAXSIZE)
            {
                Engine.Log.Trace($"[ShaderCompiler] Clears cache");
                foreach (var bw1 in cache.Values)
                    bw1.blob.Dispose();

                cache.Clear();
            }

            cache.TryGetValue(uniqueId, out var bw2);
            if (bw2 != null)
            {
                Engine.Log.Trace($"[ShaderCompiler] Found in cache {uniqueId}");
                lock(bw2)
                    return device.CreatePixelShader(bw2.blob);
            }

            bw = new();
            Monitor.Enter(bw);
            cache.Add(uniqueId, bw);
        }

        if (Engine.Config.LogLevel >= LogLevel.Trace)
            Engine.Log.Trace($"[ShaderCompiler] Compiling {uniqueId} ...\r\n{PS_HEADER}{hlslSample}{PS_FOOTER}");

        var blob = Compile(PS_HEADER + hlslSample + PS_FOOTER, true, defines);
        bw.blob = blob;
        var ps = device.CreatePixelShader(bw.blob);
        Monitor.Exit(bw);

        Engine.Log.Trace($"[ShaderCompiler] Compiled {uniqueId}");
        return ps;
    }
    internal static Blob Compile(string hlsl, bool isPS = true, List<string> defines = null)
    {
        ShaderMacro[] definesMacro = null;

        if (defines != null)
        {
            definesMacro = new ShaderMacro[defines.Count + 1];
            for(int i=0; i<defines.Count; i++)
                definesMacro[i] = new ShaderMacro() { Name = defines[i], Definition = "" };
        }

        return Compile(Encoding.UTF8.GetBytes(hlsl), isPS, definesMacro);

    }
    internal static unsafe Blob Compile(byte[] bytes, bool isPS = true, ShaderMacro[] defines = null)
    {
        string psOrvs = isPS ? "ps" : "vs";

        // Optimization could actually cause issues (mainly with literal values)
        #if DEBUG
        Compiler.Compile(bytes, defines, null, "main", null, $"{psOrvs}_5_0", ShaderFlags.SkipOptimization, out var shaderBlob, out var psError);
        #else
        Compiler.Compile(bytes, defines, null, "main", null, $"{psOrvs}_5_0", ShaderFlags.OptimizationLevel3, out var shaderBlob, out var psError);
        #endif

        if (psError != null && psError.BufferPointer != IntPtr.Zero)
        {
            string[] errors = BytePtrToStringUTF8((byte*)psError.BufferPointer).Split('\n');

            foreach (string line in errors)
                Engine.Log.Error($"[ShaderCompile] {line}");
        }

        return shaderBlob;
    }

    //private static void CompileEmbeddedShaders() // Not Used
    //{
    //    Assembly assembly = Assembly.GetExecutingAssembly();
    //    string[] shaders = assembly.GetManifestResourceNames().Where(x => GetUrlExtention(x) == "hlsl").ToArray();

    //    foreach (string shader in shaders)
    //        using (Stream stream = assembly.GetManifestResourceStream(shader))
    //        {
    //            string shaderName = shader.Substring(0, shader.Length - 5);
    //            shaderName = shaderName.Substring(shaderName.LastIndexOf('.') + 1);

    //            byte[] bytes = new byte[stream.Length];
    //            stream.Read(bytes, 0, bytes.Length);

    //            CompileShader(bytes, shaderName);
    //        }
    //}

    //private unsafe static void CompileFileShaders()
    //{
    //    List<string> shaders = Directory.EnumerateFiles(EmbeddedShadersFolder, "*.hlsl").ToList();
    //    foreach (string shader in shaders)
    //    {
    //        string shaderName = shader.Substring(0, shader.Length - 5);
    //        shaderName = shaderName.Substring(shaderName.LastIndexOf('\\') + 1);

    //        CompileShader(File.ReadAllBytes(shader), shaderName);
    //    }
    //}

    // Loads compiled blob shaders
    //private static void LoadShaders()
    //{
    //    Assembly assembly = Assembly.GetExecutingAssembly();
    //    string[] shaders = assembly.GetManifestResourceNames().Where(x => GetUrlExtention(x) == "blob").ToArray();
    //    string tempFile = Path.GetTempFileName();

    //    foreach (string shader in shaders)
    //    {
    //        using (Stream stream = assembly.GetManifestResourceStream(shader))
    //        {
    //            var shaderName = shader.Substring(0, shader.Length - 5);
    //            shaderName = shaderName.Substring(shaderName.LastIndexOf('.') + 1);

    //            byte[] bytes = new byte[stream.Length];
    //            stream.Read(bytes, 0, bytes.Length);

    //            Dictionary<string, Blob> curShaders = shaderName.Substring(0, 2).ToLower() == "vs" ? VSShaderBlobs : PSShaderBlobs;

    //            File.WriteAllBytes(tempFile, bytes);
    //            curShaders.Add(shaderName, Compiler.ReadFileToBlob(tempFile));
    //        }
    //    }
    //}

    // Should work at least from main Samples => FlyleafPlayer (WPF Control) (WPF)
    //static string EmbeddedShadersFolder = @"..\..\..\..\..\..\FlyleafLib\MediaFramework\MediaRenderer\Shaders";
    //static Assembly ASSEMBLY        = Assembly.GetExecutingAssembly();
    //static string   SHADERS_NS      = typeof(Renderer).Namespace + ".Shaders.";

    //static byte[] GetEmbeddedShaderResource(string shaderName)
    //{
    //    using (Stream stream = ASSEMBLY.GetManifestResourceStream(SHADERS_NS + shaderName + ".hlsl"))
    //    {
    //        byte[] bytes = new byte[stream.Length];
    //        stream.Read(bytes, 0, bytes.Length);

    //        return bytes;
    //    }
    //}

    const string PS_HEADER = @"
#pragma warning( disable: 3571 )
Texture2D		Texture1		: register(t0);
Texture2D		Texture2		: register(t1);
Texture2D		Texture3		: register(t2);
Texture2D		Texture4		: register(t3);

struct ConfigData
{
    int coefsIndex;
    float brightness;
    float contrast;
    float hue;
    float saturation;
    float texWidth;
    int tonemap;
    float hdrtone;
};

cbuffer         Config          : register(b0)
{
    ConfigData Config;
};


SamplerState Sampler : IMMUTABLE
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
    AddressW = CLAMP;
    ComparisonFunc = NEVER;
    MinLOD = 0;
};

inline float3 Gamut2020To709(float3 c)
{
    static const float3x3 mat = 
    {
         1.6605, -0.5876, -0.0728,
        -0.1246,  1.1329, -0.0083,
        -0.0182, -0.1006,  1.1187
    };
    return mul(mat, c);
}

#if defined(dYUVLimited)
static const float3x3 coefs[3] =
{
    // 0: BT.2020 (Limited)
    {
        1.16438356,  0.00000000,  1.67867410,
        1.16438356, -0.18732601, -0.65042418,
        1.16438356,  2.14177196,  0.00000000
    },
    // 1: BT.709 (Limited)
    {
        1.16438356,  0.00000000,  1.79274107,
        1.16438356, -0.21324861, -0.53290933,
        1.16438356,  2.11240179,  0.00000000
    },
    // 2: BT.601 (Limited)
    {
        1.16438356,  0.00000000,  1.59602678,
        1.16438356, -0.39176160, -0.81296823,
        1.16438356,  2.01723214,  0.00000000
    }
};

inline float3 YUVToRGBLimited(float3 yuv)
{
    yuv.x   -= 0.0625;
    yuv.yz  -= 0.5;
    return mul(coefs[Config.coefsIndex], yuv);
}
#elif defined(dYUVFull)
static const float3x3 coefs[3] =
{
    // 0: BT.2020 (Full)
    {
        1.00000000,  0.00000000,  1.47460000,
        1.00000000, -0.16455313, -0.57135313,
        1.00000000,  1.88140000,  0.00000000
    },
    // 1: BT.709 (Full)
    {
        1.00000000,  0.00000000,  1.57480000,
        1.00000000, -0.18732600, -0.46812400,
        1.00000000,  1.85560000,  0.00000000
    },
    // 2: BT.601 (Full)
    {
        1.00000000,  0.00000000,  1.40200000,
        1.00000000, -0.34413600, -0.71413600,
        1.00000000,  1.77200000,  0.00000000
    }
};

inline float3 YUVToRGBFull(float3 yuv)
{
    yuv.x   = (yuv.x - 0.0625) * 1.16438356;
    yuv.yz -= 0.5;
    return mul(coefs[Config.coefsIndex], yuv);
}
#endif

#if defined(dPQToLinear) || defined(dHLGToLinear)
static const float ST2084_m1 = 0.1593017578125;
static const float ST2084_m2 = 78.84375;
static const float ST2084_c1 = 0.8359375;
static const float ST2084_c2 = 18.8515625;
static const float ST2084_c3 = 18.6875;

inline float3 PQToLinear(float3 rgb, float factor)
{
    rgb = pow(rgb, 1.0 / ST2084_m2);
    rgb = max(rgb - ST2084_c1, 0.0) / (ST2084_c2 - ST2084_c3 * rgb);
    rgb = pow(rgb, 1.0 / ST2084_m1);
    rgb *= factor;
    return rgb;
}

inline float3 LinearToPQ(float3 rgb, float divider)
{
    rgb /= divider;
    rgb = pow(rgb, ST2084_m1);
    rgb = (ST2084_c1 + ST2084_c2 * rgb) / (1.0f + ST2084_c3 * rgb);
    rgb = pow(rgb, ST2084_m2);
    return rgb;
}
#endif

#if defined(dHLGToLinear)
inline float3 HLGInverse(float3 rgb)
{
    const float a = 0.17883277;
    const float b = 0.28466892;
    const float c = 0.55991073;

    rgb = (rgb <= 0.5)
        ? rgb * rgb * 4.0
        : (exp((rgb - c) / a) + b);

    // This will require different factor-nits for HLG (*19.5)
    //rgb = (rgb <= 0.5)
    //    ? (rgb * rgb) / 3.0
    //    : (exp((rgb - c) / a) + b) / 12.0;
    return rgb;
}

inline float3 HLGToLinear(float3 rgb)
{
    static const float3 ootf_2020 = float3(0.2627, 0.6780, 0.0593);

    rgb = HLGInverse(rgb);
    float ootf_ys = 2000.0f * dot(ootf_2020, rgb);
    rgb *= pow(ootf_ys, 0.2f);
    return rgb;
}
#endif

#if defined(dTone)
inline float3 ToneAces(float3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
}

inline float3 ToneHable(float3 x)
{
    const float A = 0.15f;
    const float B = 0.5f;
    const float C = 0.1f;
    const float D = 0.2f;
    const float E = 0.02f;
    const float F = 0.3f;

    // some use those
    //const float A = 0.22f;
    //const float B = 0.3f;
    //const float C = 0.1f;
    //const float D = 0.2f;
    //const float E = 0.01f;
    //const float F = 0.3f;

    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}
static const float3 HABLE48 = ToneHable(4.8);

inline float3 ToneReinhard(float3 x) //, float whitepoint=2.2) // or gamma?*
{
    return x * (1.0 + x / 4.84) / (x + 1.0); 
}
#endif

inline float3 Hue(float3 rgb, float angle)
{
    if (angle == 0)
        return rgb;

    float c = cos(angle);
    float s = -sin(angle);
    
    return mul(float3x3(
        0.299 + 0.701 * c + 0.168 * s,  0.587 - 0.587 * c + 0.330 * s,  0.114 - 0.114 * c - 0.497 * s,
        0.299 - 0.299 * c - 0.328 * s,  0.587 + 0.413 * c + 0.035 * s,  0.114 - 0.114 * c + 0.292 * s,
        0.299 - 0.300 * c + 1.250 * s,  0.587 - 0.588 * c - 1.050 * s,  0.114 + 0.886 * c - 0.203 * s
    ), rgb);
}

inline float3 Saturation(float3 rgb, float saturation)
{
    if (saturation == 1.0)
        return rgb;

    static const float3 kBT709 = float3(0.2126, 0.7152, 0.0722);

    float luminance = dot(rgb, kBT709);
    return lerp(luminance.rrr, rgb, saturation);
}

// hdrmethod enum
static const int Aces       = 1;
static const int Hable      = 2;
static const int Reinhard   = 3;

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 Texture  : TEXCOORD;
};

float4 main(PSInput input) : SV_TARGET
{
    float4 color;

    // Dynamic Sampling
";

    const string PS_FOOTER = @"

    float3 c = color.rgb;

#if defined(dYUVLimited)
	c = YUVToRGBLimited(c);
#elif defined(dYUVFull)
	c = YUVToRGBFull(c);
#endif

#if defined(dBT2020)
	c = pow(c, 2.2); // TODO: transferfunc gamma*
	c = Gamut2020To709(c);
	c = saturate(c);
	c = pow(c, 1.0 / 2.2);
#else

#if defined(dPQToLinear)
	c = PQToLinear(c, Config.hdrtone);
#elif defined(dHLGToLinear)
	c = HLGToLinear(c);
	c = LinearToPQ(c, 1000.0);
	c = PQToLinear(c, Config.hdrtone);
#endif

#if defined(dTone)
	if (Config.tonemap == Hable)
	{
		c = ToneHable(c) / HABLE48;
		c = Gamut2020To709(c);
		c = saturate(c);
		c = pow(c, 1.0 / 2.2);
	}
	else if (Config.tonemap == Reinhard)
	{
		c = ToneReinhard(c);
		c = Gamut2020To709(c);
		c = saturate(c);
		c = pow(c, 1.0 / 2.2);
	}
	else if (Config.tonemap == Aces)
	{
		c = ToneAces(c);
		c = Gamut2020To709(c);
		c = saturate(c);
		c = pow(c, 0.27);
	}
    else
    {
        c = pow(c, 1.0 / 2.2);
    }
#endif

#endif

    // Contrast / Brightness / Hue / Saturation
    c *= Config.contrast * 2.0f;
    c += Config.brightness - 0.5f;
    c = Hue(c, Config.hue);
    c = Saturation(c, Config.saturation);

    return saturate(float4(c, color.a));
}
";

    const string VS = @"
cbuffer cBuf : register(b0)
{
    matrix mat;
}

struct VSInput
{
    float4 Position : POSITION;
    float2 Texture  : TEXCOORD;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 Texture  : TEXCOORD;
};

PSInput main(VSInput vsi)
{
    PSInput psi;

    psi.Position = mul(vsi.Position, mat);
    psi.Texture = vsi.Texture;

    return psi;
}
";
}
