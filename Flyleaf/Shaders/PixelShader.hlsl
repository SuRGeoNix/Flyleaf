struct PixelShaderInput
{
   float4 pos : SV_POSITION;
   float2 tex : TEXCOORD0;
};

Texture2D picture : register(t0);
SamplerState pictureSampler : register(s0);

float4 main(PixelShaderInput input) : SV_TARGET
{
   return picture.Sample(pictureSampler, input.tex);
}