using FFmpeg.AutoGen;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace FlyleafLib.MediaFramework.MediaFrame
{
    public unsafe class VideoFrame : FrameBase
    {
        public ID3D11Texture2D[]    textures;       // P010/NV12 (Y_UV) | Y_U_V | RGB

        // Zero-Copy
        public int                  subresource;    // FFmpeg texture's array index
        public AVBufferRef*         bufRef;         // Lets ffmpeg to know that we still need it
    }
}
