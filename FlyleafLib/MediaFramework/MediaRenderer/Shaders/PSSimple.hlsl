Texture2D       Texture     : register(t0);
SamplerState    Sampler     : register(s0);

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float2 Texture  : TEXCOORD;
};

float4 main(PixelShaderInput input) : SV_TARGET
{
    float4 color = Texture.Sample(Sampler, input.Texture);

    return color;
}