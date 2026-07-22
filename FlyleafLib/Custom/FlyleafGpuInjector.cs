using System;
using System.Drawing;
using System.Drawing.Imaging;
using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using ID3D11Device = Vortice.Direct3D11.ID3D11Device;
using ID3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;
using ID3D11VideoContext = Vortice.Direct3D11.ID3D11VideoContext;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;
using MapFlags = Vortice.Direct3D11.MapFlags;


namespace FlyleafLib.Custom;

public unsafe class FlyleafGpuInjector : IDisposable
{   
    private ID3D11Texture2D _srcBgraTexture;
    private ID3D11VideoDevice _videoDevice;
    private ID3D11VideoContext _videoContext;
    private ID3D11VideoProcessor _videoProcessor;
    private ID3D11VideoProcessorEnumerator _videoEnumerator;

    private SwsContext* _swsContext;

    private int _cachedWidth = 0;
    private int _cachedHeight = 0;


    private void InitSwsContext(int width, int height, AVPixelFormat dstFormat)
    {
        ReleaseSwsContext();

        SwsContext* _swsContext = sws_getContext(
                width, height, AVPixelFormat.Bgra, // Source bitmap format 
                width, height, dstFormat,                     // Flyleaf Target Format
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
    private void InitVideoProcessor(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        // Clean up old resources if the size has changed.
        ReleaseResources();

        // 1. Creating an intermediate dynamic BGRA texture for the Bitmap.
        var texDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, // Corresponds to Format32bppArgb
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None
        };
        _srcBgraTexture = device.CreateTexture2D(texDesc);

        // 2. Obtain the interfaces for working with the video converter.
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();

        // 3. Configuring the conversion (from BGRA to NV12)
        var videoDesc = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = (uint)width,
            InputHeight = (uint)height,
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = (uint)width,
            OutputHeight = (uint)height,
            Usage = VideoUsage.PlaybackNormal
        };

        _videoEnumerator = _videoDevice.CreateVideoProcessorEnumerator(videoDesc);
        _videoProcessor = _videoDevice.CreateVideoProcessor(_videoEnumerator, 0);

        _cachedWidth = width;
        _cachedHeight = height;
    }

    private void ReleaseResources()
    {
        _srcBgraTexture?.Dispose();
        _videoProcessor?.Dispose();
        _videoEnumerator?.Dispose();
        _videoContext?.Dispose();
        _videoDevice?.Dispose();
    }


    public unsafe void InjectBitmapToFlyleafPlanes(
        Bitmap transformedBitmap,
        byte** dstData,
        int* dstLinesize,
        AVFrame* frame,
        AVPixelFormat dstFormat)
    {
        int width = transformedBitmap.Width;
        int height = transformedBitmap.Height; 

        BitmapData bmpData = transformedBitmap.LockBits(                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb
        );

        try
        {
            if (_swsContext == null || _cachedWidth != width || _cachedHeight != height)
            {
                InitSwsContext(width, height, dstFormat);
            }

            if (_swsContext == null)
                throw new Exception("Failed to create SwsContext for the renderer.");

            // Preparing pointers to the source data (Bitmap)            
            byte_ptrArray8 srcData = new byte_ptrArray8();
            srcData._0 = bmpData.Scan0;

            int_array8 srcLinesize;
            srcLinesize._[0] = bmpData.Stride;

            sws_scale(
                _swsContext,
                srcData.ToRawArray(),
                srcLinesize.ToArray(),
                0,
                height,
                frame->data.ToRawArray(),
                frame->linesize.ToArray()
            );
        }
        finally
        {   
            transformedBitmap.UnlockBits(bmpData);
        }
    }



    public unsafe void InjectBitmapToD3D11Target(
        ID3D11Device device,
        ID3D11DeviceContext context,
        ID3D11Texture2D flyleafTargetTexture,
        Bitmap transformedBitmap)
    {
        int width = transformedBitmap.Width;
        int height = transformedBitmap.Height; // Our image: width == height

        // If the size has changed or this is the first frame, initialize the pipeline.
        if (_srcBgraTexture == null || _cachedWidth != width || _cachedHeight != height)
        {
            InitVideoProcessor(device, context, width, height);
        }

        // STEP 1: Loading pixels from the Bitmap into our intermediate BGRA texture.
        BitmapData bmpData = transformedBitmap.LockBits(
        new Rectangle(0, 0, width, height),
        ImageLockMode.ReadOnly,
        PixelFormat.Format32bppArgb
    );

        try
        {
            // Map the GPU texture for writing from the CPU.
            MappedSubresource mapped = context.Map(_srcBgraTexture, 0, MapMode.WriteDiscard, MapFlags.None);

            byte* srcPtr = (byte*)bmpData.Scan0;
            byte* dstPtr = (byte*)mapped.DataPointer;
            int srcStride = bmpData.Stride;
            uint dstStride = mapped.RowPitch;

            // We copy data from system memory to video card memory line by line.
            for (int y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(
                    srcPtr + (y * srcStride),
                    dstPtr + (y * dstStride),
                    dstStride,
                    Math.Min(srcStride, dstStride)
                );
            }

            context.Unmap(_srcBgraTexture, 0);
        }
        finally
        {
            transformedBitmap.UnlockBits(bmpData);
        }

        // STEP 2: Hardware Blit (BGRA -> NV12 conversion to Flyleaf texture)
        // We create temporary View interfaces for the current conversion operation.
        // description for input View (BGRA texture)
        var inputDesc = new VideoProcessorInputViewDescription
        {
            FourCC          = 0,
            ViewDimension   = VideoProcessorInputViewDimension.Texture2D,
            Texture2D       = new() { MipSlice = 0, ArraySlice = 0 }
        };        

        ID3D11VideoProcessorInputView inputView = _videoDevice.CreateVideoProcessorInputView(
            _srcBgraTexture,
            _videoEnumerator,
            inputDesc
        );

        // 2. description for output View (Target NV12 texture Flyleaf)
        var outputDesc = new VideoProcessorOutputViewDescription
        {
            ViewDimension = VideoProcessorOutputViewDimension.Texture2D // Направление на 2D текстуру
        };
        outputDesc.Texture2D.MipSlice = 0;

        ID3D11VideoProcessorOutputView outputView = _videoDevice.CreateVideoProcessorOutputView(
            flyleafTargetTexture,
            _videoEnumerator,
            outputDesc
        );


        var stream = new VideoProcessorStream
        {
            Enable = true,
            InputSurface = inputView
        };

        // The graphics card transforms the format and writes pixels to Flyleaf in a single pass.
        _videoContext.VideoProcessorBlt(_videoProcessor, outputView, 0, 1, new[] { stream });

        // We release the local Views (without touching the texture itself or the processor!).
        inputView.Dispose();
        outputView.Dispose();
    }

    public void Dispose()
    {
        ReleaseResources();
        ReleaseSwsContext();
    }
}
