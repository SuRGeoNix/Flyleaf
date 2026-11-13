using System;
using System.Text;
using System.Threading;

using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

using ID3D11Device = Vortice.Direct3D11.ID3D11Device;

using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafPlayer;

public enum TestPostProcessMode
{
    Disabled, // Completely bypass Flyleaf's extra rendering pass
    Copy,     // Use the pass but leave the colors unchanged
    Invert    // Use the pass and visibly invert RGB
}

public sealed class TestPostProcessorFactory : IVideoPostProcessorFactory
{
    private volatile TestPostProcessMode mode = TestPostProcessMode.Invert;
    private long frames, snapshots;

    public TestPostProcessMode Mode
    {
        get => mode;
        set => mode = value;
    }

    public long ProcessedFrames => Interlocked.Read(ref frames);
    public long ProcessedSnapshots => Interlocked.Read(ref snapshots);

    public IVideoPostProcessor Create(ID3D11Device device) => new TestPostProcessor(device, this);

    internal void Count(bool snapshot)
    {
        if (snapshot) Interlocked.Increment(ref snapshots);
        else          Interlocked.Increment(ref frames);
    }
}

internal sealed class TestPostProcessor : IVideoPostProcessor
{
    private readonly TestPostProcessorFactory factory;
    private ID3D11VertexShader vs;
    private ID3D11PixelShader psCopy, psInvert;
    private ID3D11SamplerState sampler;
    private ID3D11RasterizerState rasterizer;

    public bool IsEnabled => factory.Mode != TestPostProcessMode.Disabled;

    public TestPostProcessor(ID3D11Device device, TestPostProcessorFactory factory)
    {
        this.factory = factory;

        try
        {
            using (var blob = Compile("VSMain", "vs_5_0"))
                vs = device.CreateVertexShader(blob);

            using (var blob = Compile("PSCopy", "ps_5_0"))
                psCopy = device.CreatePixelShader(blob);

            using (var blob = Compile("PSInvert", "ps_5_0"))
                psInvert = device.CreatePixelShader(blob);

            sampler = device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunc = ComparisonFunction.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue
            });

            rasterizer = device.CreateRasterizerState(new(CullMode.None, FillMode.Solid));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Process(in VideoPostProcessContext frame)
    {
        if (frame.SurfaceFormat != Format.B8G8R8A8_UNorm)
            throw new NotSupportedException($"Test shader expects BGRA8, received {frame.SurfaceFormat}.");

        var context = frame.DeviceContext;
        var mode = factory.Mode;

        // Always cover the entire render target, including letterbox/pillarbox regions.
        context.OMSetRenderTargets(frame.Output);
        context.OMSetBlendState(null);
        context.OMSetDepthStencilState(null, 0);
        context.RSSetState(rasterizer);
        context.RSSetViewport(0, 0, frame.OutputWidth, frame.OutputHeight);

        // SV_VertexID creates our fullscreen triangle without a vertex buffer.
        context.IASetInputLayout(null);
        context.IASetVertexBuffer(0, null, 0);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vs);
        context.HSSetShader(null);
        context.DSSetShader(null);
        context.GSSetShader(null);
        context.PSSetShader(mode == TestPostProcessMode.Invert ? psInvert : psCopy);
        context.PSSetShaderResource(0, frame.Input);
        context.PSSetSampler(0, sampler);
        context.Draw(3, 0);

        // Flyleaf restores its saved graphics state after Process returns.
        factory.Count(frame.IsSnapshot);
    }

    public void Dispose()
    {
        rasterizer?.Dispose(); rasterizer = null;
        sampler?.Dispose();    sampler = null;
        psInvert?.Dispose();   psInvert = null;
        psCopy?.Dispose();     psCopy = null;
        vs?.Dispose();         vs = null;
    }

    private static Blob Compile(string entry, string profile)
    {
        var result = Compiler.Compile(Encoding.UTF8.GetBytes(Hlsl), null, null,
            entry, null, profile, ShaderFlags.OptimizationLevel3,
            out var bytecode, out var errors);

        try
        {
            if (result.Failure || bytecode == null)
            {
                bytecode?.Dispose();
                throw new InvalidOperationException(
                    $"Failed to compile {entry}: {errors?.AsString() ?? result.ToString()}");
            }
            return bytecode;
        }
        finally { errors?.Dispose(); }
    }

    private const string Hlsl = """
        Texture2D Frame : register(t0);
        SamplerState FrameSampler : register(s0);

        struct VSOut
        {
            float4 Position : SV_Position;
            float2 UV : TEXCOORD0;
        };

        VSOut VSMain(uint id : SV_VertexID)
        {
            VSOut o;
            // 3 vertices form a triangle larger than the screen.
            float2 uv = float2((id << 1) & 2, id & 2);
            o.Position = float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.0, 1.0);
            o.UV = uv;
            return o;
        }

        float4 PSCopy(VSOut input) : SV_Target
        {
            float4 color = Frame.Sample(FrameSampler, input.UV);
            return float4(color.rgb, 1.0);
        }

        float4 PSInvert(VSOut input) : SV_Target
        {
            float4 color = Frame.Sample(FrameSampler, input.UV);
            return float4(1.0 - color.rgb, 1.0);
        }
        """;
}
