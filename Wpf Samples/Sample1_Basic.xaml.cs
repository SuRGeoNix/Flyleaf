using System;
using System.Threading;
using System.Windows;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace Wpf_Samples
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Sample1 : Window
    {
        public Player       Player      { get ; set; }

        public Sample1()
        {
            Config config = new Config();
            config.demuxer.VideoFormatOpt.Add("probesize",(50 * (long)1024 * 1024).ToString());
            config.demuxer.VideoFormatOpt.Add("analyzeduration",(10 * (long)1000 * 1000).ToString());
            Player = new Player(config);

            Master.RegisterFFmpeg(":2");
            InitializeComponent();
            DataContext = this;

            // Experimental new Download feature
            //StartDownloader();
        }

        #region Testing Downloader
        int downloadCounter = 0;
        public void StartDownloader()
        {
            Thread downThread = new Thread(() =>
            {
                // Remove waitMs for full download
                TestDownload(
                    @"https://multiplatform-f.akamaihd.net/i/multi/will/bunny/big_buck_bunny_,640x360_400,640x360_700,640x360_1000,950x540_1500,.f4v.csmil/master.m3u8",
                    2000
                    );

                // Partial save of live stream
                TestDownload(
                    @"https://cph-p2p-msl.akamaized.net/hls/live/2000341/test/master.m3u8",
                    5000
                    );
            });
            downThread.Start();
        }
        public void TestDownload(string url, int waitMs = -1)
        {
            FlyleafLib.MediaFramework.MediaDemuxer.DemuxerBase demuxer = new FlyleafLib.MediaFramework.MediaDemuxer.VideoDemuxer(new Config(), 777);
            demuxer.Open(url);
            demuxer.EnableStream(demuxer.VideoStreams[0]);
            if (demuxer.AudioStreams.Count != 0)
                demuxer.EnableStream(demuxer.AudioStreams[0]);
            demuxer.Download(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"testVideo{downloadCounter++}.mp4"));
            if (waitMs != -1)
            {
                Thread.Sleep(waitMs);
                demuxer.Stop();
            }
        }
        #endregion
    }
}