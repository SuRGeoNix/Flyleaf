using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

using FlyleafLib.MediaFramework.MediaInput;

using SuRGeoNix.BitSwarmLib;
using SuRGeoNix.BitSwarmLib.BEP;

using static SuRGeoNix.BitSwarmLib.BitSwarm;

namespace FlyleafLib.Plugins
{
    public class BitSwarm : PluginBase, IOpen, IProvideVideo, ISuggestVideoInput
    {
        SuRGeoNix.BitSwarmLib.BitSwarm bitSwarm;

        TorrentOptions cfg = new TorrentOptions(); //TODO
        Torrent     Torrent;
        int         fileIndex;
        int         fileIndexNext;
        bool        downloadNextStarted;
        bool        torrentReceived;
        List<string> sortedPaths;

        public List<VideoInput> VideoInputs         { get; set; } = new List<VideoInput>();
        public bool             IsPlaylist           => true;

        public string           FolderComplete      => Torrent.file.paths.Count == 1 ? cfg.FolderComplete : Torrent.data.folder;
        public string           FileName            { get; private set; }
        public long             FileSize            { get; private set; }
        public TorrentStream    TorrentStream       { get; private set; }
        public bool             Downloaded          => Torrent != null && Torrent.data.files != null && (Torrent.data.files[fileIndex] == null || Torrent.data.files[fileIndex].Created);

        public override void OnInitializing()
        {
            Dispose();
        }
        public override void OnInitializingSwitch()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return;
            
            if (cfg != null) cfg.EnableBuffering = false;
            TorrentStream?.Cancel();
        }

        public override void OnBuffering()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return;

            TorrentStream?.Cancel();
            if (cfg != null) cfg.EnableBuffering = true;
        }
        public override void OnBufferingCompleted()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return;

            if (cfg != null) cfg.EnableBuffering = false;
        }

        public override void Dispose()
        {
            if (Disposed) return;

            TorrentStream?.Cancel();

            try
            {
                bitSwarm?.Dispose();
                Torrent?. Dispose();
                bitSwarm    = null;
                Torrent     = null;
                sortedPaths = null;
                TorrentStream = null;
                downloadNextStarted = false;
                cfg.EnableBuffering = false;

                VideoInputs.Clear();
            } catch(Exception e)
            {
                Log("Error ... " + e.Message);
            }

            Disposed = true;
        }

        public bool IsValidInput(string url)
        {
            return ValidateInput(url) != InputType.Unkown;
        }
        public OpenResults Open(string url)
        {
            try
            {
                if (!IsValidInput(url)) return null;

                Disposed                    = false;
                torrentReceived             = false;
                bitSwarm                    = new SuRGeoNix.BitSwarmLib.BitSwarm(cfg);
                bitSwarm.MetadataReceived   += MetadataReceived;
                bitSwarm.OnFinishing        += OnFinishing;

                bitSwarm.Open(url);
                Log("Starting");
                bitSwarm.Start();

                while (!torrentReceived && !Handler.Interrupt) { Thread.Sleep(35); }
                if (Handler.Interrupt) { Dispose(); return null; }

                if (sortedPaths == null || sortedPaths.Count == 0) { Dispose(); return new OpenResults("No video files found in torrent"); }

                return new OpenResults();
            }
            catch(Exception e)
            {
                if (Regex.IsMatch(e.Message, "completed or is invalid"))
                {
                    MetadataReceived(this, new MetadataReceivedArgs(bitSwarm.torrent));
                    return new OpenResults();
                }

                Log("Error ... " + e.Message);
                return new OpenResults(e.Message);
            }
        }
        public OpenResults Open(Stream iostream)
        {
            return null;
        }
        public VideoInput SuggestVideo()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return null;

            return VideoInputs[0];
        }
        public override OpenResults OnOpenVideo(VideoInput input)
        {
            if (input.Plugin == null || input.Plugin.Name != Name) return null;

            FileName    = input.InputData.Title;
            fileIndex   = Torrent.file.paths.IndexOf(FileName);
            FileSize    = Torrent.file.lengths[fileIndex];

            downloadNextStarted     = false;
            bitSwarm.FocusAreInUse  = false;
            fileIndexNext           = -1;

            if (!Downloaded)
            {
                TorrentStream = Torrent.GetTorrentStream(FileName);
                input.IOStream  = TorrentStream;
                bitSwarm.IncludeFiles(new List<string>() { FileName });
                if (!bitSwarm.isRunning) { Log("Starting"); bitSwarm.Start(); }

                // Prepare for subs (add interrupt!)
                bool threadDone = false;

                Thread prepSubs = new Thread(() =>
                {
                    byte[] tmp = new byte[65536];
                    TorrentStream.Position = 0;
                    TorrentStream.Read(tmp, 0, 65536);
                    if (Handler.Interrupt) { threadDone = true; return; }
                    TorrentStream.Position = TorrentStream.Length - 65536;
                    TorrentStream.Read(tmp, 0, 65536);
                    threadDone = true;
                });
                prepSubs.IsBackground = true;
                prepSubs.Start();

                while (!threadDone)
                {
                    if (Handler.Interrupt) TorrentStream?.Cancel();
                    Thread.Sleep(30);
                }

                TorrentStream.Position = 0;
            }
            else if (File.Exists(Path.Combine(FolderComplete, FileName)))
            {
                input.IOStream = null;
                input.Url = Path.Combine(FolderComplete, FileName);

                if (!DownloadNext()) { Log("Pausing"); bitSwarm.Pause(); }
            }
            else
                return null;

            input.InputData.Folder  = FolderComplete;
            input.InputData.FileSize= FileSize;

            return new OpenResults();
        }

        private void OnFinishing(object source, FinishingArgs e)
        {
            Log("Download of " + Torrent.file.paths[fileIndexNext == -1 ? fileIndex : fileIndexNext] + " finished"); e.Cancel = DownloadNext(); if (!e.Cancel) Log("Stopped");
        }
        private void MetadataReceived(object source, MetadataReceivedArgs e)
        {
            try
            {
                Torrent     = e.Torrent;
                sortedPaths = Utils.GetMoviesSorted(Torrent.file.paths);

                foreach (var file in sortedPaths)
                {
                    VideoInputs.Add(new VideoInput()
                    {
                        InputData = new InputData()
                        {
                            Title = file
                        }
                    });
                }
            }
            catch (Exception e2)
            {
                Log("Error ... " + e2.Message);
            }

            torrentReceived = true;
        }
        private bool DownloadNext()
        {
            if (cfg.DownloadNext && !downloadNextStarted && Torrent != null && fileIndex > -1 && (Torrent.data.files[fileIndex] == null || Torrent.data.files[fileIndex].Created))
            {
                downloadNextStarted = true;

                var fileIndex = sortedPaths.IndexOf(Torrent.file.paths[this.fileIndex]) + 1;
                if (fileIndex > sortedPaths.Count - 1) return false;

                var fileIndex2 = Torrent.file.paths.IndexOf(sortedPaths[fileIndex]);
                if (fileIndex2 == -1 || Torrent.data.files[fileIndex2] == null || Torrent.data.files[fileIndex2].Created) return false;

                Log("Downloading next " + Torrent.file.paths[fileIndex2]);

                bitSwarm.IncludeFiles(new List<string>() { Torrent.file.paths[fileIndex2] });

                if (!bitSwarm.isRunning) { Log("Starting"); bitSwarm.Start(); }

                fileIndexNext = fileIndex2;

                return true;
            }

            return false;
        }

        public class TorrentOptions : Options
        {
            public new TorrentOptions Clone() { return (TorrentOptions) MemberwiseClone(); }

            public bool             DownloadNext    { get; set; } = true;

            public TorrentOptions()
            {
                FolderComplete      = Utils.GetUserDownloadPath() != null ? Path.Combine(Utils.GetUserDownloadPath(), "Torrents") : Path.Combine(Path.GetTempPath(), "Torrents");
                FolderIncomplete    = Utils.GetUserDownloadPath() != null ? Path.Combine(Utils.GetUserDownloadPath(), "Torrents", "_incomplete") : Path.Combine(Path.GetTempPath(), "Torrents", "_incomplete");
                FolderTorrents      = FolderIncomplete;
                FolderSessions      = FolderIncomplete;               

                MaxTotalConnections = 80;
                MaxNewConnections   = 15;
                BlockRequests       = 4;

                PreventTimePeriods  = true;

                //Verbosity = 2;
            }
        }
    }
}