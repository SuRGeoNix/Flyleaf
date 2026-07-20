using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.Zoom;
using System.Drawing;

namespace FlyleafLib.Custom;

public interface ICustomPlayer
{
    ZoomOverviewRenderer OverviewRenderer { get; set; }
    bool CustomHandlerEnabled { get; }
    void FillCustomPlanes(Renderer sender, VideoFrame frame, out Bitmap? transformedBitmap);

    void InitStreamContext(Stream stream);
}
