struct VertexShaderInput
{
    float4 Position : POSITION;
    float2 Texture  : TEXCOORD;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float2 Texture  : TEXCOORD;
};

PixelShaderInput main(VertexShaderInput input)
{
    return input;
}