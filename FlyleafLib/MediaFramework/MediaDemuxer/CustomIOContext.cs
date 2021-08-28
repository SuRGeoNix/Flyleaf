using System;
using System.IO;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

namespace FlyleafLib.MediaFramework.MediaDemuxer
{
    public unsafe class CustomIOContext
    {
        //List<object>    gcPrevent = new List<object>();
        AVIOContext*    avioCtx;
        public Stream   stream;
        const int       bufferSize = 0x200000; // Should be exposed to config as well
        byte[]          buffer;
        Demuxer         demuxer;

        public CustomIOContext(Demuxer demuxer)
        {
            this.demuxer = demuxer;

            ioread.Pointer  = Marshal.GetFunctionPointerForDelegate(IORead);
            ioseek.Pointer  = Marshal.GetFunctionPointerForDelegate(IOSeek);
        }

        public void Initialize(Stream stream)
        {
            this.stream = stream;
            //this.stream.Seek(0, SeekOrigin.Begin);

            if (buffer == null)
                buffer  = new byte[bufferSize]; // NOTE: if we use small buffer ffmpeg might request more than we suggest

            avioCtx = avio_alloc_context((byte*)av_malloc(bufferSize), bufferSize, 0, (void*) GCHandle.ToIntPtr(demuxer.handle), ioread, null, ioseek);
            demuxer.FormatContext->pb     = avioCtx;
            demuxer.FormatContext->flags |= AVFMT_FLAG_CUSTOM_IO;
        }

        public void Dispose()
        {
            if (avioCtx != null) 
            {
                av_free(avioCtx->buffer); 
                fixed (AVIOContext** ptr = &avioCtx) avio_context_free(ptr);
            }
            avioCtx = null;
            stream = null;
        }

        avio_alloc_context_read_packet_func ioread  = new avio_alloc_context_read_packet_func();    
        avio_alloc_context_seek_func        ioseek  = new avio_alloc_context_seek_func();

        avio_alloc_context_read_packet IORead = (opaque, buffer, bufferSize) =>
        {
            GCHandle demuxerHandle = (GCHandle)((IntPtr)opaque);
            Demuxer demuxer = (Demuxer)demuxerHandle.Target;

            if (demuxer.Interrupter.ShouldInterrupt(demuxer) != 0) return AVERROR_EXIT;
            int ret = demuxer.CustomIOContext.stream.Read(demuxer.CustomIOContext.buffer, 0, bufferSize);
            if (ret < 0) { demuxer.Log("CustomIOContext Interrupted"); return AVERROR_EXIT; }

            Marshal.Copy(demuxer.CustomIOContext.buffer, 0, (IntPtr) buffer, ret);

            return ret;
        };

        avio_alloc_context_seek IOSeek = (opaque, offset, wehnce) =>
        {
            GCHandle demuxerHandle = (GCHandle)((IntPtr)opaque);
            Demuxer demuxer = (Demuxer)demuxerHandle.Target;

            //System.Diagnostics.Debug.WriteLine($"** S | {decCtx.demuxer.fmtCtx->pb->pos} - {decCtx.demuxer.ioStream.Position}");

            if (wehnce == AVSEEK_SIZE) return demuxer.CustomIOContext.stream.Length;

            return demuxer.CustomIOContext.stream.Seek(offset, (SeekOrigin) wehnce);
        };
    }
}