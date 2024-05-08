using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

using FlyleafLib;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafExtractor
{
    public partial class Form1 : Form
    {
        public Config           Config          { get; set; }
        public DecoderContext   DecCtx          { get; set; }
        public VideoDecoder     VideoDecoder    => DecCtx.VideoDecoder;
        

        public string           Filename        { get; set; }
        public string           Extension       { get; set; }
        public ImageFormat      ImageFormat     { get; set; }

        public Form1()
        {
            // Initializes Engine (Specifies FFmpeg libraries path which is required)
            Engine.Start(new EngineConfig()
            {
                #if DEBUG
                LogOutput       = ":debug",
                LogLevel        = LogLevel.Debug,
                FFmpegLogLevel  = FFmpegLogLevel.Warning,
                #endif
                
                PluginsPath     = ":Plugins",
                FFmpegPath      = ":FFmpeg",
            });

            // Prepares Decoder Context's Configuration
            Config = new Config();
            //Config.Video.VideoProcessor = VideoProcessors.Flyleaf;

            //Config.Demuxer.AllowInterrupts = false;
            Config.Demuxer.AllowTimeouts = false;

            Config.Demuxer.BufferDuration = 99999999999999;     // We might want to use small if we use both VideoDemuxer/AudioDemuxer to avoid having a lot of audio without video
            Config.Demuxer.ReadTimeout = 60 * 1000 * 10000;     // 60 seconds to retry or fail
            Config.Video.MaxVerticalResolutionCustom = 1080;    // Default Plugins Suggest based on this

            // Initializes the Decoder Context
            DecCtx = new DecoderContext(Config);

            InitializeComponent();

            txtUrl.AllowDrop = true;
            txtUrl.DragEnter += TxtUrl_DragEnter;
            txtUrl.DragDrop += TxtUrl_DragDrop;

            txtSavePath.Text = AppDomain.CurrentDomain.BaseDirectory;
        }
        private void TxtUrl_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
                txtUrl.Text = e.Data.GetData(DataFormats.Text, false).ToString();
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                txtUrl.Text = ((string[])e.Data.GetData(DataFormats.FileDrop, false))[0];
        }

        private void TxtUrl_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }

        private void btnExtract_Click(object sender, EventArgs e)
        {
            if (btnExtract.Text == "Stop")
            {
                DecCtx.Pause();
                return;
            }

            switch (cmbFormat.Text)
            {
                case "Bmp":
                    ImageFormat = ImageFormat.Bmp;
                    break;

                case "Png":
                    ImageFormat = ImageFormat.Png;
                    break;

                case "Jpeg":
                    ImageFormat = ImageFormat.Jpeg;
                    break;

                case "Gif":
                    ImageFormat = ImageFormat.Gif;
                    break;

                case "Tiff":
                    ImageFormat = ImageFormat.Tiff;
                    break;
            }
            
            Extension = cmbFormat.Text.ToLower(); if (Extension == "jpeg") Extension = "jpg";

            int start   = int.Parse(txtStartAt.Text) -1;
            int end     = !string.IsNullOrEmpty(txtEndAt.Text.Trim()) && int.Parse(txtEndAt.Text.Trim()) > start + 1 ? int.Parse(txtEndAt.Text.Trim()) - 1 : int.MaxValue;
            int step    = int.Parse(txtStep.Text);

            if (chkSingle.Checked)
                SaveFrame(VideoDecoder.GetFrame(start), start);
            else
            {
                btnExtract.Text = "Stop";
                Thread thread = new Thread(() => ExtractFrames(start, end, step)); 
                thread.IsBackground = true; thread.Start();
            }
        }

        public void ExtractFrames(int start, int end, int step)
        {
            /* TODO
             * 
             * 1) When we have large step (greater than Keyframes distance) it is faster to seek everytime to previous keyframe
             * 2) SaveFrame (Bitmap.Save) is too heavy we should use multithreading and SaveTextureToFile from DirectX (TBR)
             * 3) Review how fast it should decode and possible add some 'brakes' / user parameter
             */

            SaveFrame(VideoDecoder.GetFrame(start), start); // Possible to get null frame?
            Config.Video.MaxOutputFps = DecCtx.VideoStream.FPS; // might FPS change after fill from codec
            VideoDecoder.Speed = step;
            VideoDecoder.ResetSpeedFrame(); // make sures we skip by step (will not get the first frame as we manually do with GetFrame)
            DecCtx.Start();

            int curFrameNumber = start + step;
            while (curFrameNumber <= end && (VideoDecoder.IsRunning || !VideoDecoder.Frames.IsEmpty))
            {
                if (VideoDecoder.Frames.IsEmpty) { Thread.Sleep(20); continue; }

                VideoDecoder.Frames.TryDequeue(out VideoFrame frame);
                SaveFrame(frame, curFrameNumber);
                curFrameNumber += step;
            }

            DecCtx.Pause();

            if (InvokeRequired)
                Invoke(new Action(() => btnExtract.Text = "Extract" ));
            else
                btnExtract.Text = "Extract";
        }

        public void SaveFrame(VideoFrame frame, int frameNumber)
        {
            if (frame == null) return;
            Bitmap bmp = VideoDecoder.Renderer.ExtractFrame(frame);
            string fullpath = Path.Combine(txtSavePath.Text, $"{Filename}_{frameNumber + 1}.{Extension}");
            bmp.Save(fullpath, ImageFormat);
            bmp.Dispose();
            VideoDecoder.DisposeFrame(frame);
        }

        private void chkSingle_CheckedChanged(object sender, EventArgs e)
        {
            txtEndAt.Enabled = !chkSingle.Checked;
            txtStep.Enabled = !chkSingle.Checked;
        }

        private void btnOpen_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(txtSavePath.Text))
                Directory.CreateDirectory(txtSavePath.Text);

            DecCtx.Stop();

            var res = DecCtx.Open(txtUrl.Text, true, true, false, false);
            if (!res.Success)
            {
                MessageBox.Show($"{res.Error}", "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (DecCtx.VideoStream == null || DecCtx.VideoStream.TotalFrames < 1)
            {
                MessageBox.Show($"Cannot find video or calculate total frames", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Config.Video.MaxOutputFps = DecCtx.VideoStream.FPS; // Making speed acting as step
            txtEndAt.Text = DecCtx.VideoStream.TotalFrames.ToString();
            btnExtract.Enabled = true;

            Filename = DecCtx.Playlist.Selected.Title;

            if (string.IsNullOrEmpty(Filename))
                Filename = $"flyleafExtractor";
            else
            {
                if (Filename.Length > 50) Filename = Filename[..50];
                Filename = Utils.GetValidFileName(Filename);
            }
        }
    }
}
