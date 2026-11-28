namespace FlyleafLib.MediaFramework.MediaRenderer;

internal static partial class ShaderCompiler
{
    internal static ReadOnlySpan<byte> PS_SUBTITLES_SCRGB => """
Texture2D Subtitle : register(t0);
SamplerState PointSampler : register(s0);

cbuffer SubtitleColor : register(b4)
{
    float WhiteScale;
};

float4 main(float4 position : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    float4 sampleColor = Subtitle.Sample(PointSampler, uv);
    float3 rgb = sampleColor.rgb;
    float3 linearRgb = lerp(rgb / 12.92, pow((rgb + 0.055) / 1.055, 2.4), step(0.04045, rgb));
    return float4(linearRgb * WhiteScale, sampleColor.a);
}
"""u8;
}
