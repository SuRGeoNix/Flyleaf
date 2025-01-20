using System;
using System.IO;
using System.Windows.Forms;

using FlyleafLib;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafDownloader
{
    public partial class Form1 : Form
    {
        public Downloader   Downloader  { get; set; }
        public Config       Config      { get; set; }

        bool isOpened;
        int  lastHeight = 600;
        public Form1()
        {
            // Initializes Engine (Specifies FFmpeg libraries path which is required)
            Engine.Start(new EngineConfig()
            {
                #if DEBUG
                LogOutput       = ":debug",
                LogLevel        = LogLevel.Debug,
                FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Warn,
                #endif

                PluginsPath     = ":Plugins",
                FFmpegPath      = ":FFmpeg",
            });

            // Prepares Player's Configuration
            Config = new Config();
            Config.Demuxer.FormatOptToUnderlying = true;        // Mainly for HLS to pass the original query which might includes session keys
            Config.Demuxer.BufferDuration = 60 * 1000 * 10000;  // 60 seconds should be enough to allow max download speed
            Config.Demuxer.ReadTimeout    = 60 * 1000 * 10000;  // 60 seconds to retry or fail
            Config.Video.MaxVerticalResolutionCustom = 1080;    // Default Plugins Suggest based on this

            // Initializes the Downloader
            Downloader = new Downloader(Config);
            Downloader.DownloadCompleted        += Downloader_DownloadCompleted;
            Downloader.PropertyChanged          += Downloader_PropertyChanged;

            InitializeComponent();

            FixSplitters(this);
            splitContainer2.SplitterDistance = splitContainer2.Height / 2;
            splitContainer3.SplitterDistance = splitContainer3.Width / 2;
            splitContainer5.SplitterDistance = splitContainer3.SplitterDistance;
            splitContainer1.Panel2Collapsed = true;
            Height = splitContainer1.SplitterDistance + 50;

            txtSavePath.Text = Path.GetTempPath();

            txtUrl.AllowDrop = true;
            txtUrl.DragEnter += TxtUrl_DragEnter;
            txtUrl.DragDrop += TxtUrl_DragDrop;
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

        void FixSplitters(Control parentControl)
        {
            foreach (Control control in parentControl.Controls)
            {
                if (control is SplitContainer)
                {
                    ((SplitContainer)control).IsSplitterFixed = true;
                    ((SplitContainer)control).SplitterWidth = 1;

                    ((SplitContainer)control).Panel1.Cursor = Cursors.Default;
                    ((SplitContainer)control).Panel2.Cursor = Cursors.Default;
                }

                FixSplitters(control);
            }
        }

        private void Downloader_DownloadCompleted(object sender, bool e)
        {
            Action action = new Action(() =>
            {
                lblStatus.Text = !e ? "Download Failed" : (Downloader.DownloadPercentage == 100 ? "Download Completed" : "Download Completed (Partial)");
                btnDownload.Enabled = true;
                btnStop.Enabled = false;
                btnBrowse.Enabled = true;
                isOpened = false;
                btnStop.Text = "Close";
                splitContainer1.Panel2.Enabled = true;
                //progressBar1.Visible = false;

                if (!splitContainer1.Panel2Collapsed)
                {
                    lastHeight = Height;
                    splitContainer1.Panel2Collapsed = true;
                    Height = splitContainer1.SplitterDistance + 50;
                    return;
                }
            });

            if (!InvokeRequired)
                action();
            else
                BeginInvoke(action);
        }

        private void Downloader_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Duration":
                case "CurTime":
                    if (!InvokeRequired)
                        lblCurTime.Text = $"{(new TimeSpan(Downloader.CurTime)).ToString(@"hh\:mm\:ss")}/{(new TimeSpan(Downloader.Duration).ToString(@"hh\:mm\:ss"))}";
                    else
                        BeginInvoke(new Action(() => { lblCurTime.Text = $"{(new TimeSpan(Downloader.CurTime)).ToString(@"hh\:mm\:ss")}/{(new TimeSpan(Downloader.Duration).ToString(@"hh\:mm\:ss"))}"; }));
                    break;

                case "DownloadPercentage":
                    if (!InvokeRequired)
                        { progressBar1.Value = (int) Downloader.DownloadPercentage; lblPercent.Text = $"{(int) Downloader.DownloadPercentage}%"; }
                    else
                        BeginInvoke(new Action(() => { { progressBar1.Value = (int) Downloader.DownloadPercentage; lblPercent.Text = $"{(int) Downloader.DownloadPercentage}%"; } }));
                    break;
            }
        }

        void RefreshVideoInputs(bool selectEnabled = false)
        {
            lstVideoInputs.BeginUpdate();
            lstVideoInputs.Items.Clear();
            lstVideoStreams.Items.Clear();
            lstAudioStreams.Items.Clear();

            if (Downloader.DecCtx.Playlist.Selected == null) return;

            foreach (var extStream in Downloader.DecCtx.Playlist.Selected.ExternalVideoStreams)
            {
                string dump;
                if (extStream.Width > 0)
                    dump = $"{extStream.Width}x{extStream.Height}@{extStream.FPS} ({extStream.Codec}) {extStream.BitRate} Kbps";
                else
                    dump = extStream.Url;

                lstVideoInputs.Items.Add(dump);
                lstVideoInputs.Items[lstVideoInputs.Items.Count-1].Tag = extStream;
                if (selectEnabled) lstVideoInputs.Items[lstVideoInputs.Items.Count-1].Selected = extStream.Enabled;
            }
            lstVideoInputs.Columns[0].Width = -1;
            lstVideoInputs.EndUpdate();
        }

        void RefreshAudioInputs(bool selectEnabled = false)
        {
            lstAudioInputs.Items.Clear();
            lstAudioStreams2.Items.Clear();

            //if (!(Downloader.DecCtx.OpenedPlugin is IProvideAudio)) return;

            foreach (var extStream in Downloader.DecCtx.Playlist.Selected.ExternalAudioStreams)
            {
                string dump = $"{extStream.Codec} {extStream.BitRate} Kbps";

                lstAudioInputs.Items.Add(dump);
                lstAudioInputs.Items[lstAudioInputs.Items.Count-1].Tag = extStream;
                if (selectEnabled) lstAudioInputs.Items[lstAudioInputs.Items.Count-1].Selected = extStream.Enabled;
            }
            lstAudioInputs.Columns[0].Width = -1;
        }

        void RefreshVideoStreams(bool selectEnabled = false)
        {
            lstVideoStreams.Items.Clear();
            foreach (var stream in Downloader.DecCtx.VideoDemuxer.VideoStreams)
            {
                string dump;
                if (stream.Width > 0 && stream.Height > 0)
                    dump = $"#{stream.StreamIndex} {stream.Width}x{stream.Height}@{stream.FPS} ({stream.Codec}) {stream.BitRate/1000} Kbps";
                else
                    dump = "Faulty";

                lstVideoStreams.Items.Add(dump);
                lstVideoStreams.Items[lstVideoStreams.Items.Count-1].Tag = stream;
                if (selectEnabled) lstVideoStreams.Items[lstVideoStreams.Items.Count-1].Selected = stream.Enabled;
            }
            lstVideoStreams.Columns[0].Width = -1;
        }

        void RefreshAudioStreams(bool selectEnabled = false)
        {
            lstAudioStreams.Items.Clear();
            foreach (var stream in Downloader.DecCtx.VideoDemuxer.AudioStreams)
            {
                string dump = $"#{stream.StreamIndex} {stream.Codec} {stream.SampleFormatStr} {stream.ChannelLayoutStr} {stream.SampleRate/1000}KHz {stream.BitRate/1000} Kbps ({stream.Language})";
                lstAudioStreams.Items.Add(dump);
                lstAudioStreams.Items[lstAudioStreams.Items.Count-1].Tag = stream;
                if (selectEnabled) lstAudioStreams.Items[lstAudioStreams.Items.Count-1].Selected = stream.Enabled;
            }
            lstAudioStreams.Columns[0].Width = -1;
        }

        void RefreshAudioStreams2(bool selectEnabled = false)
        {
            lstAudioStreams2.Items.Clear();
            //var curAudioStream = Downloader.DecCtx.VideoStream == null ? Downloader.DecCtx.VideoDemuxer.AudioStreams : Downloader.DecCtx.AudioDemuxer.AudioStreams;
            foreach (var stream in Downloader.DecCtx.AudioDemuxer.AudioStreams)
            {
                string dump = $"#{stream.StreamIndex} {stream.Codec} {stream.SampleFormatStr} {stream.ChannelLayoutStr} {stream.SampleRate/1000}KHz {stream.BitRate/1000} Kbps ({stream.Language})";
                lstAudioStreams2.Items.Add(dump);
                lstAudioStreams2.Items[lstAudioStreams2.Items.Count-1].Tag = stream;
                if (selectEnabled) lstAudioStreams2.Items[lstAudioStreams2.Items.Count-1].Selected = stream.Enabled;
            }
            lstAudioStreams2.Columns[0].Width = -1;
        }

        private void btnInputs_Click(object sender, EventArgs e)
        {
            if (!splitContainer1.Panel2Collapsed)
            {
                lstAudioInputs.SelectedIndexChanged -= lstAudioInputs_SelectedIndexChanged;
                lstVideoInputs.SelectedIndexChanged -= lstVideoInputs_SelectedIndexChanged;
                lastHeight = Height;
                splitContainer1.Panel2Collapsed = true;
                Height = splitContainer1.SplitterDistance + 50;
                lstAudioInputs.SelectedIndexChanged += lstAudioInputs_SelectedIndexChanged;
                lstVideoInputs.SelectedIndexChanged += lstVideoInputs_SelectedIndexChanged;
                return;
            }

            if (isOpened)
            {
                lstAudioInputs.SelectedIndexChanged -= lstAudioInputs_SelectedIndexChanged;
                lstVideoInputs.SelectedIndexChanged -= lstVideoInputs_SelectedIndexChanged;
                splitContainer1.Panel2Collapsed = false;
                Height = lastHeight;
                lstAudioInputs.SelectedIndexChanged += lstAudioInputs_SelectedIndexChanged;
                lstVideoInputs.SelectedIndexChanged += lstVideoInputs_SelectedIndexChanged;
                return;
            }

            string error = Downloader.Open(txtUrl.Text);
            if (error != null)
            {
                MessageBox.Show($"{error}", "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Open Failed";
                btnStop.Enabled = false;
                btnBrowse.Enabled = true;
                return;
            }

            btnStop.Enabled = true;
            btnBrowse.Enabled = false;
            isOpened = true;
            lblStatus.Text = "";
            progressBar1.Visible = false; lblPercent.Visible = false;

            lstAudioInputs.SelectedIndexChanged -= lstAudioInputs_SelectedIndexChanged;
            lstVideoInputs.SelectedIndexChanged -= lstVideoInputs_SelectedIndexChanged;
            RefreshVideoInputs(true);
            RefreshAudioInputs(true);
            RefreshVideoStreams(true);
            RefreshAudioStreams(true);
            RefreshAudioStreams2(true);
            if (splitContainer1.Panel2Collapsed)
            {
                splitContainer1.Panel2Collapsed = false;
                Height = lastHeight;
            }
            lstAudioInputs.SelectedIndexChanged += lstAudioInputs_SelectedIndexChanged;
            lstVideoInputs.SelectedIndexChanged += lstVideoInputs_SelectedIndexChanged;
        }

        private void lstVideoInputs_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstVideoInputs.SelectedItems.Count == 0) return;

            Downloader.DecCtx.Open((ExternalVideoStream)lstVideoInputs.SelectedItems[0].Tag, chkDefaultAudio.Checked); //, true, chkDefaultAudio.Checked, false);

            if (chkDefaultAudio.Checked)
            {
                lstAudioInputs.SelectedIndexChanged -= lstAudioInputs_SelectedIndexChanged;
                lstVideoInputs.SelectedIndexChanged -= lstVideoInputs_SelectedIndexChanged;
                //RefreshVideoInputs(true); // Will cause deselect of the selection
                RefreshAudioInputs(true);
                RefreshVideoStreams(true);
                RefreshAudioStreams(true);
                RefreshAudioStreams2(true);
                lstAudioInputs.SelectedIndexChanged += lstAudioInputs_SelectedIndexChanged;
                lstVideoInputs.SelectedIndexChanged += lstVideoInputs_SelectedIndexChanged;
            }
            else
            {
                RefreshVideoStreams();
                RefreshAudioStreams();
            }
        }
        private void lstAudioInputs_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstAudioInputs.SelectedItems.Count == 0) return;

            Downloader.DecCtx.Open((ExternalAudioStream)lstAudioInputs.SelectedItems[0].Tag);

            RefreshAudioStreams2(true);
        }

        private void btnDownload_Click(object sender, EventArgs e)
        {
            if (!isOpened)
            {
                lblStatus.Text = "";
                progressBar1.Visible = false; lblPercent.Visible = false;

                string error = Downloader.Open(txtUrl.Text);
                if (error != null)
                {
                    MessageBox.Show($"{error}", "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnStop.Enabled = false;
                    btnBrowse.Enabled = true;
                    return;
                }

                isOpened = true;
                btnStop.Enabled = true;
                btnBrowse.Enabled = false;
            }
            else
            {
                if (lstVideoStreams.SelectedItems.Count != 0 || lstAudioStreams.SelectedItems.Count != 0)
                {
                    foreach (ListViewItem streamObj in lstVideoStreams.Items)
                        if (streamObj.Selected)
                            ((StreamBase)streamObj.Tag).Demuxer.EnableStream(((StreamBase)streamObj.Tag));
                        else
                            ((StreamBase)streamObj.Tag).Demuxer.DisableStream(((StreamBase)streamObj.Tag));

                    if (lstAudioStreams.SelectedItems.Count != 0)
                    {
                        foreach (ListViewItem streamObj in lstAudioStreams.Items)
                        if (streamObj.Selected)
                            ((StreamBase)streamObj.Tag).Demuxer.EnableStream(((StreamBase)streamObj.Tag));
                        else
                            ((StreamBase)streamObj.Tag).Demuxer.DisableStream(((StreamBase)streamObj.Tag));
                    }
                }
                else
                    Downloader.DecCtx.VideoDemuxer.Dispose();

                if (lstAudioInputs.SelectedItems.Count != 0)
                {
                    foreach (ListViewItem streamObj in lstAudioStreams2.Items)
                        if (streamObj.Selected)
                            ((StreamBase)streamObj.Tag).Demuxer.EnableStream(((StreamBase)streamObj.Tag));
                        else
                            ((StreamBase)streamObj.Tag).Demuxer.DisableStream(((StreamBase)streamObj.Tag));
                }
                else
                    Downloader.DecCtx.AudioDemuxer.Dispose();

            }

            if (Downloader.DecCtx.VideoDemuxer.EnabledStreams.Count == 0)
                Downloader.DecCtx.VideoDemuxer.Dispose();

            if (Downloader.DecCtx.AudioDemuxer.EnabledStreams.Count == 0)
                Downloader.DecCtx.AudioDemuxer.Dispose();

            if (Downloader.DecCtx.AudioDemuxer.Disposed && Downloader.DecCtx.VideoDemuxer.Disposed)
            {
                MessageBox.Show($"No streams have been selected", "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            lstAudioInputs.SelectedIndexChanged -= lstAudioInputs_SelectedIndexChanged;
            lstVideoInputs.SelectedIndexChanged -= lstVideoInputs_SelectedIndexChanged;
            RefreshVideoInputs(true);
            RefreshAudioInputs(true);
            RefreshVideoStreams(true);
            RefreshAudioStreams(true);
            RefreshAudioStreams2(true);
            lstAudioInputs.SelectedIndexChanged += lstAudioInputs_SelectedIndexChanged;
            lstVideoInputs.SelectedIndexChanged += lstVideoInputs_SelectedIndexChanged;

            string filename = Downloader.DecCtx.Playlist.Selected.Title;

            if (string.IsNullOrEmpty(filename))
                filename = $"flyleafDownload";
            else
            {
                if (filename.Length > 50) filename = filename.Substring(0, 50);
                filename = Utils.GetValidFileName(filename);
            }

            btnDownload.Enabled = false;
            btnStop.Text = "Stop";
            lblStatus.Text = "Downloading ...";
            splitContainer1.Panel2.Enabled = false;
            if (!Downloader.DecCtx.VideoDemuxer.IsLive && !Downloader.DecCtx.AudioDemuxer.IsLive) { progressBar1.Visible = true; lblPercent.Visible = true; }

            // if only audio we can rename to .mp3 on completition
            string ext = !Downloader.DecCtx.VideoDemuxer.Disposed ? Downloader.DecCtx.VideoDemuxer.Extension : Downloader.DecCtx.AudioDemuxer.Extension;

            filename = Utils.FindNextAvailableFile(Path.Combine(txtSavePath.Text, filename + "." + ext));
            Downloader.Download(ref filename, false);
        }

        // We prevent to add audio from both embedded and external input
        private void lstAudioStreams_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstAudioStreams.SelectedItems.Count == 0) return;

            lstAudioStreams2.SelectedItems.Clear();
        }

        // We prevent to add audio from both embedded and external input
        private void lstAudioStreams2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstAudioStreams2.SelectedItems.Count == 0) return;

            lstAudioStreams.SelectedItems.Clear();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            Downloader.Dispose();
            btnDownload.Enabled = true;
            btnStop.Enabled = false;
            btnBrowse.Enabled = true;
            isOpened = false;
            btnStop.Text = "Close";
            splitContainer1.Panel2.Enabled = true;
            //progressBar1.Visible = false;

            if (!splitContainer1.Panel2Collapsed)
            {
                lastHeight = Height;
                splitContainer1.Panel2Collapsed = true;
                Height = splitContainer1.SplitterDistance + 50;
                return;
            }
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using(var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    txtSavePath.Text = fbd.SelectedPath;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Downloader.Dispose();
        }
    }
}
