using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream;

public unsafe class SubtitlesStream : StreamBase
{
    public bool     IsBitmap    { get; private set; }

    public SubtitlesStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
        => Type = MediaType.Subs;

    public override void Initialize()
    {
        var codecDescr  = avcodec_descriptor_get(CodecID);
        IsBitmap        = codecDescr != null && (codecDescr->props & CodecPropFlags.BitmapSub) != 0;
        if (Demuxer.FormatContext->nb_streams == 1) // External Streams (mainly for .sub will have as start time the first subs timestamp)
            StartTime = 0;
    }

    public void Refresh(SubtitlesDecoder decoder)
    {
        ReUpdate();

        if (CanDebug)
            Demuxer.Log.Debug($"Stream Info (Filled)\r\n{GetDump()}");
    }
        

    public void ExternalStreamAdded()
    {
        // VobSub (parse .idx data to extradata - based on .sub url)
        if (CodecID == AVCodecID.DvdSubtitle && ExternalStream != null && ExternalStream.Url.EndsWith(".sub", StringComparison.OrdinalIgnoreCase))
        {
            var idxFile = string.Concat(ExternalStream.Url.AsSpan(0, ExternalStream.Url.Length - 3), "idx");
            if (File.Exists(idxFile))
            {
                var bytes = File.ReadAllBytes(idxFile);
                cp->extradata = (byte*)av_malloc((nuint)bytes.Length);
                cp->extradata_size = bytes.Length;
                Span<byte> src = new(bytes);
                Span<byte> dst = new(cp->extradata, bytes.Length);
                src.CopyTo(dst);
            }
        }
    }
}
