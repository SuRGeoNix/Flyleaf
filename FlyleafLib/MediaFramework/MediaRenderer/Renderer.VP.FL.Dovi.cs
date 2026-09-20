using System.Numerics;
using System.Runtime.InteropServices;

using Vortice.Direct3D11;

using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    const int doviVectors             = 184;
    const int doviComponentVectors    = 59;
    const int doviComponentBase       = 7;
    const int doviMmrVectors          = 48;
    const float doviPivotSentinel     = 1e9f;

    // Dovi[0]      : sampleScale, 1, 0, 0
    // Dovi[1..3]   : affine YCC -> nonlinear RGB rows
    // Dovi[4..6]   : linear DOVI RGB -> BT.709 rows, BT.2020 luma coefficients in .w
    // Dovi[7..]    : three reshape components, 59 float4s each
    static BufferDescription doviDesc = new()
    {
        Usage          = ResourceUsage.Default,
        BindFlags      = BindFlags.ConstantBuffer,
        CPUAccessFlags = CpuAccessFlags.None,
        ByteWidth      = doviVectors * 16
    };

    [StructLayout(LayoutKind.Sequential)]
    internal struct DoviBufferType
    {
        public fixed float Data[doviVectors * 4];
    }

    internal sealed class DoviFrameData
    {
        public DoviBufferType Buffer;
    }

    // Prepare-side cache: consecutive frames commonly repeat the same RPU mapping.
    // Reuse the immutable DoviFrameData instead of allocating one object per frame.
    DoviFrameData doviPreparedData;

    // Render-side cache: if consecutive frames share the prepared state, don't upload b2 again.
    DoviFrameData doviAppliedData;
    bool doviAppliedEmpty;

    static ReadOnlySpan<float> DoviHpeLmsTo2020 =>
    [
         3.06441879f, -2.16597676f,  0.10155818f,
        -0.65612108f,  1.78554118f, -0.12943749f,
         0.01736321f, -0.04725154f,  1.03004253f
    ];

    static ReadOnlySpan<float> Dovi2020To709 =>
    [
         1.6605f, -0.5876f, -0.0728f,
        -0.1246f,  1.1329f, -0.0083f,
        -0.0182f, -0.1006f,  1.1187f
    ];

    static ReadOnlySpan<float> DoviLuma2020 => [0.2627f, 0.6780f, 0.0593f];

    bool FLDoviSupported()
    {
        if (scfg == null || !scfg.DoviRpuPresent || !scfg.DoviBlPresent)
            return false;

        return scfg.DoviProfile switch
        {
            // Profile 5 has no standards-compatible BL and therefore requires
            // Dolby reconstruction for correct playback. Compatibility ID must be 0.
            5 => scfg.DoviCompatibility == 0,

            // Profile 7 is HDR10/UHD-BD compatible. Older configuration records
            // may omit the compatibility nibble (0), while current ones use 6.
            7 => scfg.DoviCompatibility is 0 or 6,

            // Profile 8 is single-layer and the compatibility ID defines the BL:
            // 0 none/proprietary, 1 HDR10, 2 SDR, 4 HLG, 6 UHD-BD/HDR10.
            8 => scfg.DoviCompatibility is 0 or 1 or 2 or 4 or 6,

            // Profile 10 is single-layer 10-bit AV1. Public Dolby variants are
            // 10.0 (native/proprietary), 10.1 (HDR10) and 10.4 (HLG). FFmpeg
            // can also signal compatibility 2 for a BT.709/SDR-compatible BL,
            // and the parsed RPU uses the same AVDOVIMetadata reconstruction.
            10 => scfg.DoviCompatibility is 0 or 1 or 2 or 4,

            _ => false
        };
    }

    void FLDoviReset()
    {
        doviPreparedData = null;
        doviAppliedData  = null;
        doviAppliedEmpty = false;
    }

    DoviFrameData FLDoviPrepare(AVFrame* frame)
    {   // TBR: Whether possible to avoid checking per frame or at least reconstruding the whole data
        
        var side = av_frame_side_data_get(
            frame->side_data,
            frame->nb_side_data,
            AVFrameSideDataType.DoviMetadata);

        if (side == null || side->data == null)
            return null;

        var dovi    = (AVDOVIMetadata*)side->data;
        var header  = (AVDOVIRpuDataHeader*)((byte*)dovi + dovi->header_offset);
        var mapping = (AVDOVIDataMapping*)  ((byte*)dovi + dovi->mapping_offset);
        var color   = (AVDOVIColorMetadata*)((byte*)dovi + dovi->color_offset);

        // FFmpeg defines BL depth as [8, 16]. Keep malformed/unsupported metadata
        // out of the shader rather than coercing it into a seemingly valid mapping.
        if (header->bl_bit_depth < 8 || header->bl_bit_depth > 16)
            return null;

        int sampleBits = scfg.PixelComp0Depth > 8 ? 16 : 8;
        if (header->bl_bit_depth > sampleBits)
            return null;

        uint sampleMax = (1u << sampleBits) - 1u;
        uint blMax     = (1u << header->bl_bit_depth) - 1u;
        int shift      = sampleBits - header->bl_bit_depth;

        float sampleScale = sampleMax / (blMax * (float)(1u << shift));
        float coefScale   = MathF.ScaleB(1.0f, -header->coef_log2_denom);
        float pivotScale  = 1.0f / blMax;

        if (!float.IsFinite(sampleScale) || !float.IsFinite(coefScale))
            return null;

        DoviBufferType buffer = default;
        DoviSet(ref buffer, 0, new(sampleScale, 1, 0, 0));

        // All temporary matrix work stays on the stack. RPU metadata may be present
        // on every frame, so avoid creating managed 3x3 arrays in this hot path.
        Span<float> ycc        = stackalloc float[9];
        Span<float> offset     = stackalloc float[3];
        Span<float> rgbToLms   = stackalloc float[9];
        Span<float> doviTo2020 = stackalloc float[9];
        Span<float> doviTo709  = stackalloc float[9];
        Span<float> luma       = stackalloc float[3];

        for (int r = 0; r < 3; r++)
        {
            if (!DoviTryFloat(color->ycc_to_rgb_offset[r], out offset[r]))
                return null;

            for (int c = 0; c < 3; c++)
            {
                if (!DoviTryFloat(color->ycc_to_rgb_matrix[r * 3 + c], out ycc[r * 3 + c]) ||
                    !DoviTryFloat(color->rgb_to_lms_matrix[r * 3 + c], out rgbToLms[r * 3 + c]))
                    return null;
            }
        }

        // RPU custom YCC -> nonlinear RGB. Fold the neutral offsets into row .w.
        for (int r = 0; r < 3; r++)
        {
            int i = r * 3;
            float bias = -(ycc[i] * offset[0] + ycc[i + 1] * offset[1] + ycc[i + 2] * offset[2]);
            DoviSet(ref buffer, 1 + r, new(ycc[i], ycc[i + 1], ycc[i + 2], bias));
        }

        // Fold RGB->LMS, fixed HPE LMS->BT.2020 and BT.2020->BT.709 into one matrix.
        DoviMul3x3(DoviHpeLmsTo2020, rgbToLms, doviTo2020);
        DoviMul3x3(Dovi2020To709, doviTo2020, doviTo709);
        DoviMulRow3x3(DoviLuma2020, doviTo2020, luma);

        for (int r = 0; r < 3; r++)
        {
            int i = r * 3;
            Vector4 row = new(doviTo709[i], doviTo709[i + 1], doviTo709[i + 2], luma[r]);
            if (!DoviFinite(row))
                return null;

            DoviSet(ref buffer, 4 + r, row);
        }

        // Seven possible internal pivots. Allocate once for the whole method, not
        // inside the component loop (avoids CA2014 and repeated stack growth).
        Span<float> pivots = stackalloc float[7];

        for (int component = 0; component < 3; component++)
        {
            ref var curve = ref mapping->curves[component];
            int numPivots = curve.num_pivots;

            // FFmpeg contract: [2, 9], strictly sorted ascending.
            if (numPivots < 2 || numPivots > 9)
                return null;

            ushort previous = curve.pivots[0];
            if (previous > blMax)
                return null;

            for (int i = 1; i < numPivots; i++)
            {
                ushort current = curve.pivots[i];
                if (current <= previous || current > blMax)
                    return null;
                previous = current;
            }

            int componentBase = doviComponentBase + component * doviComponentVectors;
            float lo = curve.pivots[0] * pivotScale;
            float hi = curve.pivots[numPivots - 1] * pivotScale;

            DoviSet(ref buffer, componentBase, new(lo, hi, numPivots, 0));

            pivots.Fill(doviPivotSentinel);
            for (int i = 1; i < numPivots - 1; i++)
                pivots[i - 1] = curve.pivots[i] * pivotScale;

            DoviSet(ref buffer, componentBase + 1,
                new(pivots[0], pivots[1], pivots[2], pivots[3]));
            DoviSet(ref buffer, componentBase + 2,
                new(pivots[4], pivots[5], pivots[6], doviPivotSentinel));

            int mmrIndex = 0;
            int pieces   = numPivots - 1;

            for (int p = 0; p < pieces; p++)
            {
                switch (curve.mapping_idc[p])
                {
                    case AVDOVIMappingMethod.Polynomial:
                    {
                        int order = curve.poly_order[p];
                        if (order < 1 || order > 2)
                            return null;

                        float c0 = curve.poly_coef[p][0] * coefScale;
                        float c1 = curve.poly_coef[p][1] * coefScale;
                        float c2 = order == 2 ? curve.poly_coef[p][2] * coefScale : 0;

                        if (!float.IsFinite(c0) || !float.IsFinite(c1) || !float.IsFinite(c2))
                            return null;

                        DoviSet(ref buffer, componentBase + 3 + p, new(c0, c1, c2, 0));
                        break;
                    }

                    case AVDOVIMappingMethod.Mmr:
                    {
                        int order = curve.mmr_order[p];
                        if (order < 1 || order > 3)
                            return null;

                        int vectors = order * 2;
                        if (mmrIndex + vectors > doviMmrVectors)
                            return null;

                        float constant = curve.mmr_constant[p] * coefScale;
                        if (!float.IsFinite(constant))
                            return null;

                        DoviSet(ref buffer, componentBase + 3 + p,
                            new(constant, mmrIndex, 0, order));

                        for (int o = 0; o < order; o++)
                        {
                            int mmrBase = componentBase + 11 + mmrIndex;

                            Vector4 a = new(
                                curve.mmr_coef[p][o][0] * coefScale,
                                curve.mmr_coef[p][o][1] * coefScale,
                                curve.mmr_coef[p][o][2] * coefScale,
                                0);

                            Vector4 b = new(
                                curve.mmr_coef[p][o][3] * coefScale,
                                curve.mmr_coef[p][o][4] * coefScale,
                                curve.mmr_coef[p][o][5] * coefScale,
                                curve.mmr_coef[p][o][6] * coefScale);

                            if (!DoviFinite(a) || !DoviFinite(b))
                                return null;

                            DoviSet(ref buffer, mmrBase,     a);
                            DoviSet(ref buffer, mmrBase + 1, b);

                            mmrIndex += 2;
                        }
                        break;
                    }

                    default:
                        return null;
                }
            }
        }

        // Most streams repeat the same mapping over many consecutive frames.
        // Compare the final packed representation so caching covers every field
        // that can affect the shader, without relying on scene_refresh_flag.
        if (doviPreparedData != null && DoviEquals(ref buffer, ref doviPreparedData.Buffer))
            return doviPreparedData;

        doviPreparedData = new() { Buffer = buffer };
        return doviPreparedData;
    }

    void FLDoviApply(VideoFrame frame)
    {
        var data = frame.Dovi;

        if (data != null)
        {
            if (ReferenceEquals(data, doviAppliedData))
                return;

            context.UpdateSubresource(data.Buffer, doviBuffer);
            doviAppliedData = data;
            return;
        }

        // A DOVI shader should normally always have RPU metadata. Clear once when
        // metadata is missing/invalid so an older frame's RPU is never reused.
        if (doviAppliedEmpty)
            return;

        DoviBufferType empty = default;
        context.UpdateSubresource(empty, doviBuffer);
        doviAppliedData  = null;
        doviAppliedEmpty = true;
    }

    static bool DoviTryFloat(AVRational value, out float result)
    {
        if (value.Den == 0)
        {
            result = 0;
            return false;
        }

        result = (float)value.Num / value.Den;
        return float.IsFinite(result);
    }

    static bool DoviFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) &&
           float.IsFinite(value.Z) && float.IsFinite(value.W);

    static void DoviSet(ref DoviBufferType buffer, int index, Vector4 value)
    {
        fixed (float* p = buffer.Data)
            ((Vector4*)p)[index] = value;
    }

    static bool DoviEquals(ref DoviBufferType a, ref DoviBufferType b)
    {
        fixed (float* pa = a.Data)
        fixed (float* pb = b.Data)
        {
            ulong* a64 = (ulong*)pa;
            ulong* b64 = (ulong*)pb;
            int count = sizeof(DoviBufferType) / sizeof(ulong);

            for (int i = 0; i < count; i++)
                if (a64[i] != b64[i])
                    return false;

            return true;
        }
    }

    static void DoviMul3x3(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> dst)
    {
        for (int r = 0; r < 3; r++)
        {
            int ri = r * 3;
            for (int c = 0; c < 3; c++)
            {
                dst[ri + c] =
                    a[ri]     * b[c] +
                    a[ri + 1] * b[3 + c] +
                    a[ri + 2] * b[6 + c];
            }
        }
    }

    static void DoviMulRow3x3(ReadOnlySpan<float> row, ReadOnlySpan<float> matrix, Span<float> dst)
    {
        dst[0] = row[0] * matrix[0] + row[1] * matrix[3] + row[2] * matrix[6];
        dst[1] = row[0] * matrix[1] + row[1] * matrix[4] + row[2] * matrix[7];
        dst[2] = row[0] * matrix[2] + row[1] * matrix[5] + row[2] * matrix[8];
    }
}
