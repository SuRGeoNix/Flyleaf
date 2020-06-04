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

        FFmpeg      ffmpeg;         // FFmpeg  Video Decoder    (GPU Acceleration)
        DirectX     directX;        // DirectX Video Processing (GPU Acceleration)
        Thread      threadPlay;     // Fake Play Thread with Sleep

        private void Form1_Load(object sender, EventArgs e)
        {
            ffmpeg  = new FFmpeg(1);
            directX = new DirectX(this.Handle); // Parses SampleUI 's Handle for output

            if ( !ffmpeg.Open(fileToPlay) ) { MessageBox.Show("Decoder failed to open input"); return; }
            if ( !ffmpeg.hwAccelStatus )    { MessageBox.Show("FFmpeg failed to initialize D3D11VA device"); return; }

            /* FFmpeg  Decode  Frame
             * DirectX Process & Present Frame
             */
            threadPlay = new Thread(() =>
            {
                while ( true )
                {
                    // FFmpeg  Decode  Frame
                    IntPtr curFrame = ffmpeg.GetFrame();
                    if ( curFrame == IntPtr.Zero && !ffmpeg.hwAccelStatus) { MessageBox.Show("Pixel Format not supported by GPU"); return; }
                    if ( curFrame == IntPtr.Zero ) continue;

                    //DirectX Process & Present Frame
                    directX.PresentFrame(curFrame);

                    // Fake Play Thread with Sleep
                    Thread.Sleep(30);
                }
            });

            threadPlay.SetApartmentState(ApartmentState.STA);
            threadPlay.Start();
        }

        // Misc
        public SampleUI() { InitializeComponent(); }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e) { if ( threadPlay != null && threadPlay.IsAlive ) threadPlay.Abort(); }
    }
}
