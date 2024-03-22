using FFmpeg.AutoGen;

namespace FlyleafLib.MediaFramework.MediaFrame;
public class DataFrame : FrameBase
{
    public AVCodecID DataCodecId;
    public byte[] Data;
}
