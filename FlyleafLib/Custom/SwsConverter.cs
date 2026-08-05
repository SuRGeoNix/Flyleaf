namespace FlyleafLib.Custom;

public unsafe class SwsConverter : IDisposable
{
    private SwsContext*     _swsCtx;    
    private int _srcWidth;
    private int _srcHeight;
    private int _dstWidth;
    private int _dstHeight;
    private AVPixelFormat _srcFormat;
    private AVPixelFormat _dstFormat;
    public SwsConverter(int srcWidth, int srcHeight, AVPixelFormat srcFormat,int dstWidth, int dstHeight, AVPixelFormat dstFormat)
    {
        _srcWidth = srcWidth;
        _srcHeight = srcHeight;
        _srcFormat = srcFormat;

        _dstWidth = dstWidth;
        _dstHeight = dstHeight;
        _dstFormat = dstFormat; 

        InitContext();
    }
    public bool CheckContext(AVFrame* srcFrame, int dstWidth, int dstHeight, AVPixelFormat dstFormat)
    {
        if (srcFrame == null)
            return false;

         CheckContext(
            srcFrame->width,
            srcFrame->height,
            (AVPixelFormat)srcFrame->format,
            dstWidth,
            dstHeight,
            dstFormat);

        return true;
    }

    public void CheckContext(int srcWidth, int srcHeight, AVPixelFormat srcFormat, int dstWidth, int dstHeight, AVPixelFormat dstFormat)
    {
        if (_swsCtx == null || srcFormat != _srcFormat
            || srcWidth != _srcWidth || srcHeight != _srcHeight
            || dstWidth != _dstWidth || dstHeight != _dstHeight || dstFormat != _dstFormat)
        {
            LocalDispose();

            _srcWidth = srcWidth;
            _srcHeight = srcHeight;
            _srcFormat = srcFormat;

            _dstWidth = dstWidth;
            _dstHeight = dstHeight;
            _dstFormat = dstFormat;

            InitContext();
        }
    }

    public int Convert(AVFrame* srcFrame, int srcSliceY, byte*[] dst, int[] dstStride)
    {
        return sws_scale(_swsCtx,
                        srcFrame->data.ToRawArray(),
                        srcFrame->linesize.ToArray(),
                        srcSliceY,
                        srcFrame->height,
                        dst,
                        dstStride);
    }

    public int Convert(byte*[] src, int[] linesize, int srcSliceY, int srcSliceH, byte*[] dst, int[] dstStride)
    {
        return sws_scale(_swsCtx,
                        src,
                        linesize,
                        srcSliceY,
                        srcSliceH,
                        dst,
                        dstStride);
    }

    private void InitContext()
    {
        _swsCtx = sws_getContext(
            _srcWidth,
            _srcHeight,
            _srcFormat,
            _dstWidth,
            _dstHeight,
            _dstFormat,
            SwsFlags.None, null, null, null);
    }
    private void LocalDispose()
    {   
        if (_swsCtx != null)
        {
            sws_freeContext(_swsCtx);
            _swsCtx = null;
        }
    }

    public void Dispose()
    {
        LocalDispose();
    }
}
