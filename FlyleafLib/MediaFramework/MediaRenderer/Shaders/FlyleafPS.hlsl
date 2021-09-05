Texture2D       TextureRGB_Y    : register(t0);
Texture2D       TextureU_UV     : register(t1);
Texture2D       TextureV        : register(t2);

SamplerState    Sampler         : register(s0);

cbuffer         Config 		    : register(b0)
{
    int format;
    int coefsIndex;
    int hdrmethod;
    
    float brightness;
    float contrast;
    
    float g_luminance;
    float g_toneP1;
    float g_toneP2;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float2 Texture  : TEXCOORD0;
};

// format enum
static const int RGB        = 1;
static const int Y_UV       = 2;
static const int Y_U_V      = 3;

// hdrmethod enum
static const int Aces       = 1;
static const int Hable      = 2;
static const int Reinhard   = 3;

// YUV to RGB matrix coefficients
static const float4x4 coefs[] =
{
    // Limited -> Full
    {
        // BT2020 (srcBits = 10)
        { 1.16438353, 1.16438353, 1.16438353, 0 },
        { 0, -0.187326103, 2.14177227, 0 },
        { 1.67867422, -0.650424361, 0, 0 },
        { -0.915688038, 0.347458541, -1.14814520, 1 }
    },
    
        // BT709
    {
        { 1.16438341, 1.16438341, 1.16438341, 0 },
        { 0, -0.213248596, 2.11240149, 0 },
        { 1.79274082, -0.532909214, 0, 0 },
        { -0.972944975, 0.301482648, -1.13340211, 1 }
    },
        // BT601
    {
        { 1.16438341, 1.16438341, 1.16438341, 0 },
        { 0, -0.391762286, 2.01723194, 0 },
        { 1.59602666, -0.812967658, 0, 0 },
        { -0.874202192, 0.531667829, -1.08563077, 1 },
    }
};

// HDR to SDR color convert (Thanks to KODI community https://github.com/thexai/xbmc)
static const float ST2084_m1 = 2610.0f / (4096.0f * 4.0f);
static const float ST2084_m2 = (2523.0f / 4096.0f) * 128.0f;
static const float ST2084_c1 = 3424.0f / 4096.0f;
static const float ST2084_c2 = (2413.0f / 4096.0f) * 32.0f;
static const float ST2084_c3 = (2392.0f / 4096.0f) * 32.0f;

static const float4x4 bt2020tobt709color =
{
    { 1.6604f, -0.1245f, -0.0181f, 0 },
    { -0.5876f, 1.1329f, -0.10057f, 0 },
    { -0.07284f, -0.0083f, 1.1187f, 0 },
    { 0, 0, 0, 0 }
};

float3 inversePQ(float3 x)
{
    x = pow(max(x, 0.0f), 1.0f / ST2084_m2);
    x = max(x - ST2084_c1, 0.0f) / (ST2084_c2 - ST2084_c3 * x);
    x = pow(x, 1.0f / ST2084_m1);
    return x;
}

float3 aces(float3 x)
{
    const float A = 2.51f;
    const float B = 0.03f;
    const float C = 2.43f;
    const float D = 0.59f;
    const float E = 0.14f;
    return (x * (A * x + B)) / (x * (C * x + D) + E);
}

float3 hable(float3 x)
{
    const float A = 0.15f;
    const float B = 0.5f;
    const float C = 0.1f;
    const float D = 0.2f;
    const float E = 0.02f;
    const float F = 0.3f;
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

static const float3 bt709coefs = { 0.2126f, 1.0f - 0.2126f - 0.0722f, 0.0722f };
float reinhard(float x)
{
    return x * (1.0f + x / (g_toneP1 * g_toneP1)) / (1.0f + x);
}

float4 main(PixelShaderInput input) : SV_TARGET
{
    float4 color;
    
    if (format == Y_UV)
    {
        color = float4(TextureRGB_Y.Sample(Sampler, input.Texture).r, TextureU_UV.Sample(Sampler, input.Texture).rg, 1.0);
        color = mul(color, coefs[coefsIndex]);
    }
    else if (format == Y_U_V)
    {
        color = float4(TextureRGB_Y.Sample(Sampler, input.Texture).r, TextureU_UV.Sample(Sampler, input.Texture).r, TextureV.Sample(Sampler, input.Texture).r, 1.0);
        color = mul(color, coefs[coefsIndex]);
    }
    else // RGB
    {
        color = TextureRGB_Y.Sample(Sampler, input.Texture);
    }
    
    if (hdrmethod != 0)
    {
        // BT2020 -> BT709
        color.rgb = pow(max(0.0, color.rgb), 2.4f);
        color.rgb = max(0.0, mul(color, bt2020tobt709color).rgb);
        color.rgb = pow(color.rgb, 1.0f / 2.2f);
        
        if (hdrmethod == Aces)
        {
            color.rgb = inversePQ(color.rgb);
            color.rgb *= (10000.0f / g_luminance) * (2.0f / g_toneP1);
            color.rgb = aces(color.rgb);
            color.rgb *= (1.24f / g_toneP1);
            color.rgb = pow(color.rgb, 0.27f);
        }
        else if (hdrmethod == Hable)
        {
            color.rgb = inversePQ(color.rgb);
            color.rgb *= g_toneP1;
            color.rgb = hable(color.rgb * g_toneP2) / hable(g_toneP2);
            color.rgb = pow(color.rgb, 1.0f / 2.2f);
        }
        else if (hdrmethod == Reinhard)
        {
            float luma = dot(color.rgb, bt709coefs);
            color.rgb *= reinhard(luma) / luma;
        }
    }
    
    color *= contrast * 2.0f;
    color += brightness - 0.5f;
    
    color.a = 1;
    return color;
}