using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaDemuxer;

namespace Wpf_Samples
{
    /// <summary>
    /// Interaction logic for Sample4_Downloader.xaml
    /// </summary>
    public partial class Sample4_Downloader : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        public DemuxerBase Downloader { get ; set; } = new VideoDemuxer(new Config(), 777);
        public ICommand StartDownload { get ; set; }
        public ICommand StopDownload { get ; set; }
        public void StopDownloadAction(object obj = null) { Downloader.Stop(); }
        public void StartDownloadAction(object userInput)
        {
            Thread downThread = new Thread(() =>
            {
                Downloader.Stop();
                StartDownloader(userInput.ToString());
            });
            downThread.IsBackground = true;
            downThread.Start();
        }
        public Sample4_Downloader()
        {
            Master.RegisterFFmpeg(":2");
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

            Downloader.Stop();
        }

        #region Testing Downloader
        int downloadCounter = 0;
        public void StartDownloader(string url)
        {
            if (Downloader.Open(url) != 0) { MessageBox.Show("Could not open url input");  return; }
            Downloader.EnableStream(Downloader.VideoStreams[0]);
            if (Downloader.AudioStreams.Count != 0)
                Downloader.EnableStream(Downloader.AudioStreams[0]);
            Downloader.Download(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"testVideo{downloadCounter++}.mp4"));
        }
        #endregion
    }
}
