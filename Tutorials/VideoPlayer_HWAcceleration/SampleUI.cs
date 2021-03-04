/* 
 * C# Video Demuxing | GPU Decoding & Processing Acceleration Tutorial
 * (Based on FFmpeg.Autogen bindings for FFmpeg & SharpDX bindings for DirectX)
 *                                           By John Stamatakis (aka SuRGeoNix)
 *
 * Implementing Sample UI For Video Demuxing | GPU Decoding & Processing Acceleration
 */

using SharpDX.Direct3D11;
using System;
using System.Threading;
using System.Windows.Forms;

namespace VideoPlayer_HWAcceleration
{
    public partial class SampleUI : Form
    {
        string      fileToPlay = @"C:\root\down\samples\0.mp4";

        FFmpeg      ffmpeg;         // FFmpeg Video Demuxing & HW Decoding
        DirectX     directX;        // DirectX Video Processing & Rendering
        Thread      threadPlay;     // Simulates FPS

        private void Form1_Load(object sender, EventArgs e)
        {
            ffmpeg  = new FFmpeg();
            directX = new DirectX(this.Handle); // Parses SampleUI 's Handle for output
            if (!ffmpeg.InitHWAccel(directX._device)) { MessageBox.Show("Failed to Initialize FFmpeg's HW Acceleration"); return; }
            if (!ffmpeg.Open(fileToPlay)) { MessageBox.Show("FFmpeg failed to open input"); return; }

            threadPlay = new Thread(() =>
            {
                while (true)
                {
                    // FFmpeg HW Decode Frame
                    Texture2D textureHW = ffmpeg.GetFrame();
                    if (textureHW == null) { Console.WriteLine("Empty Texture!"); continue; }

                    // DirectX HW Process & Present Frame
                    directX.PresentFrame(textureHW);

                    Thread.Sleep(41); // Simulates FPS
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
