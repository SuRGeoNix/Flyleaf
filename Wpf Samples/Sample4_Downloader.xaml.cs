using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaContext;

using Path = System.IO.Path;

namespace Wpf_Samples
{
    /// <summary>
    /// Interaction logic for Sample4_Downloader.xaml
    /// </summary>
    public partial class Sample4_Downloader : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        public Downloader Downloader { get ; set; }
        Thread downThread;

        public ICommand StartDownload { get ; set; }
        public ICommand StopDownload { get ; set; }
        public void StopDownloadAction(object obj = null) { Downloader.Dispose(); }
        public void StartDownloadAction(object userInput)
        {
            Downloader.Dispose();

            while (downThread != null && downThread.IsAlive) Thread.Sleep(10);

            downThread = new Thread(() =>
            {
                StartDownloader(userInput.ToString());
            });
            downThread.IsBackground = true;
            downThread.Start();
        }
        public Sample4_Downloader()
        {
            Master.RegisterFFmpeg(":2");

            Config config = new Config();

            // For live stream might required
            //config.demuxer.FormatOpt.Add("live_start_index", "0"); // Download from the beggining of the stream (as back as available)

            // Especially for live streams but generally large read timeouts
            config.Demuxer.ReadTimeout = 40 * 1000 * 10000;
            config.Demuxer.BufferDuration = 40 * 1000 * 10000;
            config.Demuxer.FormatOpt.Add("probesize",(50 * (long)1024 * 1024).ToString());
            config.Demuxer.FormatOpt.Add("analyzeduration",(10 * (long)1000 * 1000).ToString());
            Downloader = new Downloader(config);

            InitializeComponent();
            DataContext = this;
            UserInput.Text = @"https://multiplatform-f.akamaihd.net/i/multi/will/bunny/big_buck_bunny_,640x360_400,640x360_700,640x360_1000,950x540_1500,.f4v.csmil/master.m3u8";

            StartDownload = new RelayCommand(StartDownloadAction);
            StopDownload = new RelayCommand(StopDownloadAction);
            Downloader.DownloadCompleted += Downloader_DownloadCompleted;
        }

        private void Downloader_DownloadCompleted(object sender, bool success)
        {
            if (success)
            {
                if (Downloader.DownloadPercentage == 100)
                    MessageBox.Show("Download Completed!");
                else
                    MessageBox.Show("Partial/Live Download Completed!");
            }
            else
                MessageBox.Show("Download Failed!");
        }

        int downloadCounter = 0;
        public void StartDownloader(string url)
        {
            string error = null;
            if ((error = Downloader.Open(url)) != null) { MessageBox.Show(error); return; }

            //Downloader.DecCtx.OpenVideoInput(Downloader.DecCtx.PluginHandler.PluginsInput["YoutubeDL"].VideoInputs[0]);
            //Downloader.DecCtx.OpenAudioInput(Downloader.DecCtx.PluginHandler.PluginsInput["YoutubeDL"].AudioInputs[2]);

            // Example adding all Audio/Video streams
            //for (int i=0; i<Downloader.DecCtx.VideoDemuxer.AVStreamToStream.Count; i++)
            //Downloader.DecCtx.VideoDemuxer.EnableStream(Downloader.DecCtx.VideoDemuxer.AVStreamToStream[i]);

            // Example adding first video stream and first audio (if exists)
            //Downloader.DecCtx.VideoDemuxer.EnableStream(Downloader.DecCtx.VideoDemuxer.VideoStreams[0]);
            //if (Downloader.DecCtx.VideoDemuxer.AudioStreams.Count != 0)
            //    Downloader.DecCtx.VideoDemuxer.EnableStream(Downloader.DecCtx.VideoDemuxer.AudioStreams[0]);

            string filename = $"SampleVideo{downloadCounter++}";
            Downloader.Download(ref filename);
        }
    }
}
