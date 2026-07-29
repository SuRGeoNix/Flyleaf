using FlyleafLib.MediaFramework.MediaFrame;
using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;
using MapFlags = Vortice.Direct3D11.MapFlags;


namespace FlyleafLib.Custom;

public unsafe class FlyleafGpuInjector : IDisposable
{   
    private SwsContext* _swsContext;

    private int _cachedWidth = 0;
    private int _cachedHeight = 0;

    private void InitSwsContext(int width, int height, AVPixelFormat dstFormat)
    {
        ReleaseSwsContext();

        SwsContext* _swsContext = sws_getContext(
                width, height, AVPixelFormat.Bgra, // Source bitmap format 
                width, height, dstFormat,          // Flyleaf Target Format
                SwsFlags.Bilinear, null, null, null
            );

        _cachedWidth = width;
        _cachedHeight = height;
    }

    private void ReleaseSwsContext()
    {
        if (_swsContext != null)
            sws_freeContext(_swsContext);
    }

    public static unsafe void ConvertRgbToD3D11NV12(
        BitmapData rgbBitmapData,
        MappedSubresource mappedResource,
        int width,
        int height)
    {
        int gpuPitch = (int)mappedResource.RowPitch;

        byte* pDstBase = (byte*)mappedResource.DataPointer.ToPointer();
        byte* pSrcBase = (byte*)rgbBitmapData.Scan0.ToPointer();

        int srcStride = rgbBitmapData.Stride;

        // NV12 is a YUV 4:2:0 format.
        // We need to calculate Y for each pixel.
        // But U and V are calculated ONCE for a 2x2 pixel block.

        // 1. Parallel calculation of the Y-plane (line-by-line)
        Parallel.For(0, height, y =>
        {
            byte* pSrcRow = pSrcBase + (y * srcStride);
            byte* pDstYRow = pDstBase + (y * gpuPitch);

            for (int x = 0; x < width; x++)
            {
                // In GDI+ Format32bppRgb, the byte order is B, G, R, A.
                byte b = pSrcRow[x * 4 + 0];
                byte g = pSrcRow[x * 4 + 1];
                byte r = pSrcRow[x * 4 + 2];

                // BT.601 formula for Y (luma) Full Range [0..255]
                int yVal = (int)(0.299f * r + 0.587f * g + 0.114f * b);

                // Write to the Y plane of the texture
                pDstYRow[x] = (byte)Math.Clamp(yVal, 0, 255);
            }
        });

        // 2. Parallel calculation of the UV plane (stepping by one row and one pixel)
        byte* pDstUVBase = pDstBase + (height * gpuPitch); // UV starts strictly after Y
        int uvHeight = height / 2;

        Parallel.For(0, uvHeight, uvY =>
        {
            int srcY = uvY * 2; // Take the top row from the 2x2 block
            byte* pSrcRow = pSrcBase + (srcY * srcStride);
            byte* pDstUVRow = pDstUVBase + (uvY * gpuPitch);

            for (int uvX = 0; uvX < width / 2; uvX++)
            {
                int srcX = uvX * 2; // Take the left pixel from the 2x2 block

                byte b = pSrcRow[srcX * 4 + 0];
                byte g = pSrcRow[srcX * 4 + 1];
                byte r = pSrcRow[srcX * 4 + 2];

                // BT.601 formulas for U and V with a +128 offset
                int uVal = (int)(-0.1687f * r - 0.3313f * g + 0.5f * b + 128);
                int vVal = (int)(0.5f * r - 0.4187f * g - 0.0813f * b + 128);

                // In NV12, the U and V channels are interleaved: U, V, U, V...
                pDstUVRow[uvX * 2 + 0] = (byte)Math.Clamp(uVal, 0, 255);
                pDstUVRow[uvX * 2 + 1] = (byte)Math.Clamp(vVal, 0, 255);
            }
        });
    }

    public unsafe void InjectBitmapToNv12Texture(
        ID3D11Device device,
        ID3D11DeviceContext context,
        ID3D11VideoDevice videoDevice,
        ID3D11VideoProcessorEnumerator videoProcessorEnumerator,
        Bitmap srcBitmap,
        VideoFrame frame
        )
    {
        int width = srcBitmap.Width;
        int height = srcBitmap.Height; 

        if (width % 2 != 0 || height % 2 != 0)
        {
            throw new ArgumentException("The width and height of NV12 must be even numbers.");
        }
        
        Texture2DDescription desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12, 
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,              
            BindFlags = BindFlags.ShaderResource | BindFlags.Decoder,
            CPUAccessFlags = CpuAccessFlags.Write,       // Enabling write access.
            MiscFlags = ResourceOptionFlags.None
        };

        ID3D11Texture2D nv12Texture = device.CreateTexture2D(desc);

        BitmapData rgbData = srcBitmap.LockBits(
            new Rectangle(0, 0, srcBitmap.Width, srcBitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb
        );
        MappedSubresource mappedTex = context.Map(nv12Texture, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            ConvertRgbToD3D11NV12(rgbData, mappedTex, width, height);
        }
        finally
        {
            context.Unmap(nv12Texture, 0);
            srcBitmap.UnlockBits(rgbData);
        }

        VideoProcessorInputViewDescription vpivd       = new()
        {
            FourCC          = 0, // TBR: if required to specify this (uint)Format.NV12,
            ViewDimension   = VideoProcessorInputViewDimension.Texture2D,
            Texture2D       = new() { MipSlice = 0, ArraySlice = 0 }
        };
    
        frame.DisposeTexture();
        frame.Texture = [nv12Texture];
        frame.VPIV = videoDevice.CreateVideoProcessorInputView(nv12Texture, videoProcessorEnumerator, vpivd);
        frame.IsTransformedFrame = true;
    }

    public unsafe void InjectBitmapToVideoFrameAsShadowResource(
        ID3D11Device device,        
        Bitmap transformedBitmap,
        VideoFrame frame)
    {   
        int width = transformedBitmap.Width;
        int height = transformedBitmap.Height;

        var texDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, // Corresponds to Format32bppArgb
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None
        };

        // Loading pixels from the Bitmap into BGRA texture.
        var rect = new Rectangle(0, 0, transformedBitmap.Width, transformedBitmap.Height);

        BitmapData bmpData = transformedBitmap.LockBits(
            rect,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb
        );

        try
        {   
            var texture = device.CreateTexture2D(
                texDesc,
                new[]
                {
                    new SubresourceData
                    {
                        DataPointer = bmpData.Scan0,
                        RowPitch = (uint)bmpData.Stride
                    }
                });

            var srv = device.CreateShaderResourceView(texture);

            frame.DisposeTexture();
            frame.Texture = new[] { texture };
            frame.SRV = new[] { srv };
            frame.VPIV = null;
            frame.IsTransformedFrame = true;
        }
        finally
        {
            transformedBitmap.UnlockBits(bmpData);
        }
    }


    public void Dispose()
    {
        ReleaseSwsContext();
    }
}
