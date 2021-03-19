Texture2D theTextureY : register(t0);
Texture2D theTextureUV : register(t1);
SamplerState theSampler : register(s0);
struct PixelShaderInput
{
	float4 pos : SV_POSITION;
	float2 tex : TEXCOORD0;
};
float4 main(PixelShaderInput input) : SV_TARGET
{
	const float3 offset = {0.0, -0.501960814, -0.501960814};
	const float3 Rcoeff = {1.0000,  0.0000,  1.4746};
	const float3 Gcoeff = {1.0000, -0.1646, -0.5714};
	const float3 Bcoeff = {1.0000,  1.8814,  0.0000};
	float4 Output;
	float3 yuv;
	yuv.x = theTextureY.Sample(theSampler, input.tex).r;
	yuv.yz = theTextureUV.Sample(theSampler, input.tex).gr;
	yuv += offset;
	Output.r = dot(yuv, Rcoeff);
	Output.g = dot(yuv, Gcoeff);
	Output.b = dot(yuv, Bcoeff);
	Output.a = 1.0f;
	return Output;
}