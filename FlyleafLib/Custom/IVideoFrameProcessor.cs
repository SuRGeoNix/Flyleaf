using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using Vortice.Direct3D11;
using ID3D11VideoDevice = Vortice.Direct3D11.ID3D11VideoDevice;

namespace FlyleafLib.Custom;

public interface IVideoFrameProcessor
{
    bool Process(Renderer renderer, VideoFrame frame);
}
