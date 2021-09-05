struct VertexShaderInput
{
    float4 Position : POSITION;
    float2 Texture  : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float2 Texture  : TEXCOORD0;
};

PixelShaderInput main(VertexShaderInput input)
{
    return input;
}