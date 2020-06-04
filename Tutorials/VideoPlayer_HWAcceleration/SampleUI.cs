/* 
 * C# Video Demuxing | GPU Decoding & Processing Acceleration Tutorial
 * (Based on FFmpeg.Autogen bindings for FFmpeg & SharpDX bindings for DirectX)
 *                                           By John Stamatakis (aka SuRGeoNix)
 *
 * Implementing Sample UI For Video Demuxing | GPU Decoding & Processing Acceleration
 */

using System;
using System.Threading;
using System.Windows.Forms;

namespace VideoPlayer_HWAcceleration
{
    public partial class SampleUI : Form
    {
        string      fileToPlay = @"C:\FILENAME_HERE.mp4";

        FFmpeg      ffmpeg;
        DirectX     directX;
        Thread      threadPlay;

        private void Form1_Load(object sender, EventArgs e)
        {
            ffmpeg  = new FFmpeg(1);
            directX = new DirectX(this.Handle);

            if ( !ffmpeg.Open(fileToPlay) ) { MessageBox.Show("Decoder failed to open input"); return; }

            if ( !ffmpeg.hwAccelStatus ) { MessageBox.Show("FFmpeg failed to initialize D3D11VA device"); return; }

            threadPlay = new Thread(() =>
            {
                while ( true )
                {
                    IntPtr curFrame = ffmpeg.GetFrame();
                    if ( curFrame == IntPtr.Zero && !ffmpeg.hwAccelStatus) { MessageBox.Show("Pixel Format not supported by GPU"); return; }
                    if ( curFrame == IntPtr.Zero ) continue;

                    directX.PresentFrame(curFrame);

                    Thread.Sleep(30);
                }
            });

            threadPlay.SetApartmentState(ApartmentState.STA);
            threadPlay.Start();
        }

        public SampleUI() { InitializeComponent(); }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e) { if ( threadPlay != null && threadPlay.IsAlive ) threadPlay.Abort(); }
    }
}
