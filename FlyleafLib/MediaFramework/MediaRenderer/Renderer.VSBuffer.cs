using System;
using System.Numerics;
using System.Runtime.InteropServices;

using Vortice.Direct3D11;

namespace FlyleafLib.MediaFramework.MediaRenderer
{
    public partial class Renderer
    {
        ID3D11Buffer vsBuffer;
        VSBufferType vsBufferData = new VSBufferType();

        [StructLayout(LayoutKind.Sequential)]
        struct VSBufferType
        {
            public Matrix4x4 mat;
        }

        private void SetRotation(int angle)
        {
            if (angle > 360)
                _RotationAngle = 360;
            else if (angle < 0)
                _RotationAngle = 0;
            else
                _RotationAngle = angle;

            if (_RotationAngle < 45 || _RotationAngle == 360)
                _d3d11vpRotation = VideoProcessorRotation.Identity;
            else if (_RotationAngle < 135)
                _d3d11vpRotation = VideoProcessorRotation.Rotation90;
            else if (_RotationAngle < 225)
                _d3d11vpRotation = VideoProcessorRotation.Rotation180;
            else if (_RotationAngle < 360)
                _d3d11vpRotation = VideoProcessorRotation.Rotation270;
            
            vsBufferData.mat = Matrix4x4.CreateFromYawPitchRoll(0.0f, 0.0f, (float) ((Math.PI / 180) * angle));
            //vsBufferData.mat = Matrix4x4.Transpose(vsBufferData.mat); TBR
            context.UpdateSubresource(vsBufferData, vsBuffer);

            Present();
        }
    }
}
