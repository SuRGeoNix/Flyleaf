using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream;

public unsafe class SubtitlesStream : StreamBase
{
    public int      Height      { get; set; }
    public int      Width       { get; set; }
    public bool     IsBitmap    { get; set; }

    public override string GetDump()
        => $"[{Type}  #{StreamIndex}-{Language.IdSubLanguage}{(Title != null ? "(" + Title + ")" : "")}] {Codec} | [BR: {BitRate}] | {Utils.TicksToTime((long)(AVStream->start_time * Timebase))}/{Utils.TicksToTime((long)(AVStream->duration * Timebase))} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}";

    public SubtitlesStream() { }
    public SubtitlesStream(Demuxer demuxer, AVStream* st) : base(demuxer, st) => Refresh();

    public override void Refresh()
    {
        base.Refresh();

        Width           = AVStream->codecpar->width;
        Height          = AVStream->codecpar->height;
        var codecDescr  = avcodec_descriptor_get(CodecID);
        IsBitmap        = codecDescr != null && (codecDescr->props & AV_CODEC_PROP_BITMAP_SUB) != 0;
    }
}
