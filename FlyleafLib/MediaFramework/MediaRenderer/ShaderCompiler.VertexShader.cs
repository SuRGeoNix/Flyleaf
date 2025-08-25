﻿namespace FlyleafLib.MediaFramework.MediaRenderer;

internal static partial class ShaderCompiler
{
    static ReadOnlySpan<byte> VS => @"
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
    psi.Texture  = vsi.Texture;

    return psi;
}
"u8;
}
