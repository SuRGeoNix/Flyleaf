struct VertexShaderInput
{
    float4 pos : POSITION;
    float2 tex : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 pos : SV_POSITION;
    float2 tex : TEXCOORD0;
};

VertexShaderOutput main(VertexShaderInput input)
{
    return input;
}