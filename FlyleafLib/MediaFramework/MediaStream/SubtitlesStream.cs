using System;
using System.IO;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream;

public unsafe class SubtitlesStream : StreamBase
{
    public bool     IsBitmap    { get; set; }

    public override string GetDump()
        => $"[{Type}  #{StreamIndex}-{Language.IdSubLanguage}{(Title != null ? "(" + Title + ")" : "")}] {Codec} | [BR: {BitRate}] | {Utils.TicksToTime((long)(AVStream->start_time * Timebase))}/{Utils.TicksToTime((long)(AVStream->duration * Timebase))} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}";

    public SubtitlesStream() { }
    public SubtitlesStream(Demuxer demuxer, AVStream* st) : base(demuxer, st) => Refresh();

    public override void Refresh()
    {
        base.Refresh();

        var codecDescr  = avcodec_descriptor_get(CodecID);
        IsBitmap        = codecDescr != null && (codecDescr->props & AV_CODEC_PROP_BITMAP_SUB) != 0;

        if (Demuxer.FormatContext->nb_streams == 1) // External Streams (mainly for .sub will have as start time the first subs timestamp)
            StartTime = 0;
    }

    public void ExternalStreamAdded()
    {
        // VobSub (parse .idx data to extradata - based on .sub url)
        if (CodecID == AVCodecID.AV_CODEC_ID_DVD_SUBTITLE && ExternalStream != null && ExternalStream.Url.EndsWith(".sub", StringComparison.OrdinalIgnoreCase))
        {
            var idxFile = ExternalStream.Url.Substring(0, ExternalStream.Url.Length - 3) + "idx";
            if (File.Exists(idxFile))
            {
                var bytes = File.ReadAllBytes(idxFile);
                AVStream->codecpar->extradata = (byte*)av_malloc((ulong)bytes.Length);
                AVStream->codecpar->extradata_size = bytes.Length;
                Span<byte> src = new(bytes);
                Span<byte> dst = new(AVStream->codecpar->extradata, bytes.Length);
                src.CopyTo(dst);
            }
        }
    }
}
