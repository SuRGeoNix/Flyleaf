using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;

namespace Wpf_Samples
{
    /// <summary>
    /// Sample how to export frames (to .bmp files) from the video decoder
    /// </summary>
    public partial class Sample5_ExportVideoFrames : Window , INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        private string _UserInput;
        public string   UserInput
        {
            get => _UserInput;
            set {  _UserInput = value; OnPropertyChanged(nameof(UserInput)); }
        }

        static string sampleVideo = (Environment.Is64BitProcess ? "../" : "") + "../../../../Sample.mp4";

        Demuxer demuxer;
        VideoDecoder vDecoder;
        Config config;

        public Sample5_ExportVideoFrames()
        {
            Master.RegisterFFmpeg(":2");
            InitializeComponent();
            DataContext = this;

            OpenVideo   = new RelayCommand(OpenVideoAction);
            UserInput   = sampleVideo;
            
            config = new Config();
            config.Demuxer.AllowInterrupts = false; // Enable it only for network protocols?
            config.Decoder.MaxVideoFrames = 10;    // How much does your CPU/GPU/RAM handles?

            demuxer = new Demuxer(config.Demuxer);
            vDecoder = new VideoDecoder(config);
        }
        public ICommand     OpenVideo   { get ; set; }
        public void OpenVideoAction(object param)
        {
            if (string.IsNullOrEmpty(UserInput)) UserInput = sampleVideo;

            // OPEN
            if (demuxer.Open(UserInput) != null) { MessageBox.Show($"Cannot open input {UserInput}"); return; }
            if (vDecoder.Open(demuxer.VideoStreams[0]) != null) { MessageBox.Show($"Cannot open the decoder"); return; }

            // TEST CASES HERE
            Case1_ExportAll(1);
            //Case2_ExportWithStep(10);
            //Case3_ExportCustom();

            // CLOSE
            vDecoder.Dispose();
            demuxer.Dispose();
            //vDecoder.DisposeVA(); // When you are done with vDecoder overall
        }

        public void Case1_ExportAll(int threads = 10)
        {
            demuxer.Start();
            vDecoder.Start();

            int curFrame = 0;
            int runningThreads = 0;

            //demuxer.Config.MaxQueueSize = threads;
            vDecoder.Config.Decoder.MaxVideoFrames = threads;
            vDecoder.Renderer.MaxOffScreenTextures = threads;

            while (vDecoder.IsRunning || vDecoder.Frames.Count != 0 || runningThreads != 0)
            {
                if (vDecoder.Frames.Count == 0) { continue; }

                if (runningThreads > threads) Thread.Sleep(10);
                Interlocked.Increment(ref runningThreads);

                Task.Run(() =>
                {
                    try
                    {
                        vDecoder.Frames.TryDequeue(out VideoFrame frame);
                        if (frame == null) return;
                        SaveFrame(frame, (++curFrame).ToString());
                    } finally
                    {
                        Interlocked.Decrement(ref runningThreads);
                    }
                });
            }
        }

        public void Case2_ExportWithStep(int step)
        {
            vDecoder.Speed = step;
            demuxer.Start();
            vDecoder.Start();

            int curFrame = step;
            while (vDecoder.IsRunning || vDecoder.Frames.Count != 0)
            {
                if (vDecoder.Frames.Count == 0) { continue; }

                vDecoder.Frames.TryDequeue(out VideoFrame frame);
                SaveFrame(frame, (curFrame).ToString());
                curFrame += step;
            }
        }

        public void Case3_ExportCustom()
        {
            SaveFrame(vDecoder.GetFrame(0), "1");
            SaveFrame(vDecoder.GetFrame(9), "10");
            SaveFrame(vDecoder.GetFrame(99), "100");
        }

        public void SaveFrame(VideoFrame frame, string filename)
        {
            Bitmap bmp = vDecoder.Renderer.GetBitmap(frame);
            bmp.Save($"{filename}.bmp", ImageFormat.Bmp);
            bmp.Dispose();
            VideoDecoder.DisposeFrame(frame);
        }
     
    }
}
