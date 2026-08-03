using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Controls;
using System.Xml.Linq;
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
    private SwsConverter     _swsConverter;
   
    private Texture2DDescription _txtNv12Desc = new Texture2DDescription
    {   
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.NV12,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Dynamic,
        BindFlags = BindFlags.Decoder,
        CPUAccessFlags = CpuAccessFlags.Write,       // Enabling write access.
        MiscFlags = ResourceOptionFlags.None
    };

    private Texture2DDescription _txtBgraDesc = new Texture2DDescription
    {
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm, // Corresponds to Format32bppArgb
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Immutable,
        BindFlags = BindFlags.ShaderResource,
        CPUAccessFlags = CpuAccessFlags.None
    };
    
    private ShaderResourceViewDescription _descSrvRGB = new ShaderResourceViewDescription
    {
        Format = Format.B8G8R8A8_UNorm, 
        ViewDimension = Vortice.Direct3D.ShaderResourceViewDimension.Texture2D,
        Texture2D = new() { MipLevels = 1, MostDetailedMip = 0 },
        Texture2DArray = new Texture2DArrayShaderResourceView{ MipLevels = 1, ArraySize = 1 },
    };

    private SubresourceData[]  _subData     = new SubresourceData[1];

    public void Dispose()
    {
        ConvertorDispose();
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

        if (_txtNv12Desc.Width != (uint)width || _txtNv12Desc.Height != (uint)height)
        {
            _txtNv12Desc.Width = (uint)width;
            _txtNv12Desc.Height = (uint)height;
        }

        CheckConvertor(width, height, AVPixelFormat.Bgra, AVPixelFormat.Nv12);
        
        ID3D11Texture2D nv12Texture = device.CreateTexture2D(_txtNv12Desc);

        BitmapData rgbData = srcBitmap.LockBits(
            new Rectangle(0, 0, srcBitmap.Width, srcBitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb
        );
        MappedSubresource mappedTex = context.Map(nv12Texture, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            // Preparing pointers to the source data (Bitmap)            
            byte_ptrArray8 srcData = new byte_ptrArray8();
            srcData._0 = rgbData.Scan0;

            int_array8 srcLinesize;
            srcLinesize._[0] = rgbData.Stride;

            byte* dst = (byte*) mappedTex.DataPointer;
            byte_ptrArray8 dstData = new byte_ptrArray8()
            {
                _0 = (IntPtr)dst,
                _1 = (IntPtr)(dst + mappedTex.RowPitch * height),
            };

            int_array8 dstLinesize = new int_array8()
            {
                [0] = (int)mappedTex.RowPitch,
                [1] = (int)mappedTex.RowPitch,
            };
            _swsConverter.Convert(srcData.ToRawArray(), srcLinesize.ToArray(), 0, height, dstData.ToRawArray(), dstLinesize.ToArray());
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

        if (_txtBgraDesc.Width != (uint)width || _txtBgraDesc.Height != (uint)height)
        {
            _txtBgraDesc.Width = (uint)width;
            _txtBgraDesc.Height = (uint)height;
        }

        var rect = new Rectangle(0, 0, transformedBitmap.Width, transformedBitmap.Height);
        BitmapData bmpData = transformedBitmap.LockBits(
            rect,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb
        );

        try
        {
            frame.DisposeTexture();

            _subData[0].RowPitch = (uint)bmpData.Stride;
            _subData[0].DataPointer = bmpData.Scan0;

            ID3D11Texture2D txtBGRA  = device.CreateTexture2D(_txtBgraDesc, _subData);
            var srv = device.CreateShaderResourceView(txtBGRA, _descSrvRGB);

            frame.SRV = [srv];
            frame.VPIV = null;
            frame.IsTransformedFrame = true;

            txtBGRA.Dispose();
        }
        finally
        {   
            transformedBitmap.UnlockBits(bmpData);
            
        }
    }
        
    private void CheckConvertor(int width, int height, AVPixelFormat srcFormat, AVPixelFormat dstFormat)
    {
        if (_swsConverter is not SwsConverter)
        {
            ConvertorInit(width, height, srcFormat, dstFormat);
        }
        _swsConverter.CheckContext(width, height, srcFormat, width, height, dstFormat);
    }
    private void ConvertorInit(int width, int height, AVPixelFormat srcFormat, AVPixelFormat dstFormat)
    {
        _swsConverter = new (
                    width,
                    height,
                    srcFormat,
                    width,
                    height,
                    dstFormat);
    }

    private void ConvertorDispose()
    {   
        _swsConverter?.Dispose();
        _swsConverter = null;
    }
}
