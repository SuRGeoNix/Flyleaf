cbuffer cBuf : register(b0)
{
    matrix mat;
}
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

PixelShaderInput main(VertexShaderInput vsi)
{
    PixelShaderInput psi;

    psi.Position = mul(vsi.Position, mat);
    psi.Texture = vsi.Texture;

    return psi;
}