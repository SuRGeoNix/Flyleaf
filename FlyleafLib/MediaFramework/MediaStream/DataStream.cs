using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream;

public unsafe class DataStream : StreamBase
{
    public DataStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
        => Type = MediaType.Data;

    public override void Initialize() { }
}
