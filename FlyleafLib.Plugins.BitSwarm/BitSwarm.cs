using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaStream;
using SuRGeoNix.BitSwarmLib;
using SuRGeoNix.BitSwarmLib.BEP;

namespace FlyleafLib.Plugins
{
    public class BitSwarm : PluginBase, IPluginVideo
    {
        /* 1. we need an event when the decoder finishes open to restore EnableBuffering = false;
         * 2. we need to cancel when we close a stream (causes issues)
         * 3. similarly for Player.Seek Re-open!
         * 4. Configuration pass to main config
        */
            // Cancel current stream to avoid errors :) 
            //if (Torrent.data.files[fileIndex] != null && !Torrent.data.files[fileIndex].Created) Torrent.data.files[fileIndex]


        public bool IsPlaylist => true;
        Session Session => Player.Session;

        SuRGeoNix.BitSwarmLib.BitSwarm bitSwarm;

        TorrentOptions cfg = new TorrentOptions(); //TODO
        Torrent Torrent;
        int         fileIndex;
        int         fileIndexNext;
        bool        downloadNextStarted;
        List<string> sortedPaths;

        public string           FolderComplete      => Torrent.file.paths.Count == 1 ? cfg.FolderComplete : Torrent.data.folder;
        public string           FileName            { get; private set; }
        public long             FileSize            { get; private set; }
        public bool             Disposed            { get; private set; }
        public bool             Downloaded          => Torrent != null && Torrent.data.files != null && (Torrent.data.files[fileIndex] == null || Torrent.data.files[fileIndex].Created);

        public override void OnInitializing()
        {
            if (Player.curVideoPlugin == null || Player.curVideoPlugin.PluginName != PluginName) return;
            if (Session.CurVideoStream != null && Session.CurVideoStream.Stream is TorrentStream) ((TorrentStream)Session.CurVideoStream.Stream).Cancel();

            try
            {
                base.OnInitialized();
                bitSwarm?.Dispose();
                Torrent?. Dispose();
                bitSwarm    = null;
                Torrent     = null;
                sortedPaths = null;
                downloadNextStarted = false;
                cfg.EnableBuffering = false;
            } catch(Exception e)
            {
                Log("Error ... " + e.Message);
            }
        }

        public override void OnInitializingSwitch()
        {
            if (Player.curVideoPlugin == null || Player.curVideoPlugin.PluginName != PluginName) return;
            if (Session.CurVideoStream != null && Session.CurVideoStream.Stream is TorrentStream) ((TorrentStream)Session.CurVideoStream.Stream).Cancel();
            if (cfg != null) cfg.EnableBuffering = false;
        }

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [BitSwarm] {msg}"); }
        private void OnFinishing(object source, SuRGeoNix.BitSwarmLib.BitSwarm.FinishingArgs e)
        {
            Log("Download of " + Torrent.file.paths[fileIndexNext == -1 ? fileIndex : fileIndexNext] + " finished"); e.Cancel = DownloadNext(); if (!e.Cancel) Log("Stopped");
        }

        private void MetadataReceived(object source, SuRGeoNix.BitSwarmLib.BitSwarm.MetadataReceivedArgs e)
        {
            try
            {
                Torrent     = e.Torrent;
                sortedPaths = Utils.GetMoviesSorted(Torrent.file.paths);

                foreach (var file in sortedPaths)
                {
                    //var fileSize = Torrent.file.lengths[Torrent.file.paths.IndexOf(file)];

                    VideoStreams.Add(new VideoStream()
                    {
                        Movie = new Movie()
                        {
                            UrlType = UrlType.Torrent,
                            Title = file
                        }
                    });
                }

                if (VideoStreams.Count == 0) { Player.OpenFailed(); return; }
                if (!isOpening) Player.Open(VideoStreams[0]);
            }
            catch (Exception e2)
            {
                Log("Error ... " + e2.Message);
            }
        }

        public override void Dispose()
        {
            OnInitialized();
            base.Dispose();
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
        public VideoStream GetVideoStream(VideoStream stream)
        {
            FileName        = stream.Movie.Title;
            fileIndex       = Torrent.file.paths.IndexOf(FileName);
            FileSize        = Torrent.file.lengths[fileIndex];

            downloadNextStarted     = false;
            bitSwarm.FocusAreInUse  = false;
            fileIndexNext           = -1;

            if (!Downloaded)
            {
                stream.Stream  = Torrent.GetTorrentStream(FileName);
                bitSwarm.IncludeFiles(new List<string>() { FileName });
                if (!bitSwarm.isRunning) { Log("Starting"); bitSwarm.Start(); }
            }
            else if (File.Exists(Path.Combine(FolderComplete, FileName)))
            {
                stream.Stream = null;
                stream.Url = Path.Combine(FolderComplete, FileName);

                if (!DownloadNext()) { Log("Pausing"); bitSwarm.Pause(); }
            }
            else
                return null;

            stream.Movie.Url            = Path.Combine(FolderComplete, FileName);
            stream.Movie.UrlType        = UrlType.Torrent;
            stream.Movie.Folder         = FolderComplete;
            stream.Movie.FileSize       = FileSize;

            return stream;
        }

        bool isOpening = false;
        public OpenVideoResults OpenVideo()
        {
            try
            {
                if (SuRGeoNix.BitSwarmLib.BitSwarm.ValidateInput(Player.Session.InitialUrl) == SuRGeoNix.BitSwarmLib.BitSwarm.InputType.Unkown) return null;

                isOpening = true;
                bitSwarm                    = new SuRGeoNix.BitSwarmLib.BitSwarm(cfg);
                bitSwarm.MetadataReceived   += MetadataReceived;
                bitSwarm.OnFinishing        += OnFinishing;

                bitSwarm.Open(Player.Session.InitialUrl);
                Log("Starting");
                bitSwarm.Start();

                if (sortedPaths != null)
                {
                    isOpening = false;
                    return new OpenVideoResults(VideoStreams[0]);
                }

                isOpening = false;
                return new OpenVideoResults() { runAsync = true };
            }
            catch(Exception e)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(e.Message, "completed or is invalid"))
                {
                    isOpening = false;
                    MetadataReceived(this, new SuRGeoNix.BitSwarmLib.BitSwarm.MetadataReceivedArgs(bitSwarm.torrent));
                    return new OpenVideoResults() { runAsync = true };
                }

                Log("Error ... " + e.Message);
            }

            isOpening = false;
            return new OpenVideoResults() { forceFailure = true };
        }
        public VideoStream OpenVideo(VideoStream stream)
        {
            //return stream;

            if (string.IsNullOrEmpty(stream.Movie.Title)) return null;

            foreach(var vstream in VideoStreams)
                if (vstream.Movie.Title == stream.Movie.Title) return GetVideoStream(stream);

            return null;
        }

        public class TorrentOptions : Options // NotifyPropertyChanged!
        {
            //internal Player player;

            public new TorrentOptions Clone() { return (TorrentOptions) MemberwiseClone(); }

            public bool             DownloadNext    { get; set; } = true;

            public TorrentOptions()
            {
                FolderComplete  = Utils.GetUserDownloadPath() != null ? Path.Combine(Utils.GetUserDownloadPath(), "Torrents") : Path.Combine(Path.GetTempPath(), "Torrents");
                FolderIncomplete= Utils.GetUserDownloadPath() != null ? Path.Combine(Utils.GetUserDownloadPath(), "Torrents", "_incomplete") : Path.Combine(Path.GetTempPath(), "Torrents", "_incomplete");
                FolderTorrents  = FolderIncomplete;
                FolderSessions  = FolderIncomplete;
                BlockRequests   = 2;
                MinThreads      = 12;
                MaxThreads      = 70;
                SleepModeLimit  = -1;
            }
        }
    }
}