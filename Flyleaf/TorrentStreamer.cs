using System;
using System.Collections.Generic;
using System.IO;

using SuRGeoNix.BitSwarmLib;
using SuRGeoNix.BitSwarmLib.BEP;
using static SuRGeoNix.Flyleaf.MediaRouter;

namespace SuRGeoNix.Flyleaf
{
    public class TorrentStreamer
    {
        public string           FolderComplete      { get; private set; }
        public string           FileName            { get; private set; }
        public long             FileSize            { get; private set; }
        public Torrent          Torrent             { get; private set; }
        public bool             Disposed            { get; private set; }

        public Settings.Torrent config;

        MediaRouter player;
        BitSwarm    bitSwarm;
        Options     bitSwarmOpt;

        int         fileIndex;
        int         fileIndexNext;
        bool        downloadNextStarted;
        List<string> sortedPaths;

        public TorrentStreamer(MediaRouter player) { this.player = player; bitSwarmOpt = new Options(); }

        private void Initialize()
        {
            Dispose();
            ParseSettingsToBitSwarm();
            bitSwarmOpt.PieceTimeout = config.TimeoutGlobal;
            bitSwarmOpt.PieceRetries = config.RetriesGlobal;

            bitSwarmOpt.Verbosity = 4;
            bitSwarmOpt.LogStats = true;

            bitSwarm                    = new BitSwarm(bitSwarmOpt);
            bitSwarm.MetadataReceived   += MetadataReceived;
            bitSwarm.StatsUpdated       += StatsUpdated;
            bitSwarm.OnFinishing        += OnFinishing;

            Disposed = false;
        }

        public void Dispose()
        {
            if (Disposed) return;

            bitSwarm?.Dispose();
            Torrent?. Dispose();
            bitSwarm    = null;
            Torrent     = null;
            sortedPaths = null;
            downloadNextStarted = false;
            Disposed    = true;
        }

        public int Open(string input)
        {
            Initialize();

            try
            {
                bitSwarm.Open(input);
                bitSwarm.Start();
            }
            catch(Exception e)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(e.Message, "completed or is invalid"))
                {
                    MetadataReceived(this, new BitSwarm.MetadataReceivedArgs(bitSwarm.torrent));
                    return 0;
                }

                Initialize();
                return -1;
            }

            return 0;
        }

        public int OpenStream(string fileName)
        {
            FileName        = fileName;
            FolderComplete  = Torrent.file.paths.Count == 1 ? config.DownloadPath : Torrent.data.folder;
            fileIndex       = Torrent.file.paths.IndexOf(fileName);
            FileSize        = Torrent.file.lengths[fileIndex];

            downloadNextStarted = false;
            fileIndexNext = -1;

            if (Torrent.data.files[fileIndex] != null && !Torrent.data.files[fileIndex].Created)
                player.UrlType = InputType.TorrentPart;
            else if (File.Exists(Path.Combine(FolderComplete, FileName)))
                player.UrlType = InputType.TorrentFile;
            else
                return -1;

            int ret;

            bitSwarmOpt.PieceTimeout    = config.TimeoutBuffer;
            bitSwarmOpt.PieceRetries    = config.RetriesBuffer;

            if (player.UrlType == InputType.TorrentPart)
            {
                bitSwarm.IncludeFiles(new List<string>() { fileName });
                if (!bitSwarm.isRunning) bitSwarm.Start();
                ret = player.decoder.Open(Torrent.StreamFiles[FileName].Stream);
            }
            else
            {
                if (!DownloadNext()) bitSwarm.Pause();
                ret = player.decoder.Open(Path.Combine(FolderComplete, FileName));
            }

            bitSwarmOpt.PieceTimeout = config.TimeoutGlobal;
            bitSwarmOpt.PieceRetries = config.RetriesGlobal;

            return ret;
        }

        private bool DownloadNext()
        {
            if (config.DownloadNext && !downloadNextStarted && Torrent != null && fileIndex > -1 && (Torrent.data.files[fileIndex] == null || Torrent.data.files[fileIndex].Created))
            {
                downloadNextStarted = true;

                var fileIndex = sortedPaths.IndexOf(Torrent.file.paths[this.fileIndex]) + 1;
                if (fileIndex > sortedPaths.Count - 1) return false;

                var fileIndex2 = Torrent.file.paths.IndexOf(sortedPaths[fileIndex]);
                if (fileIndex2 == -1 || Torrent.data.files[fileIndex2] == null || Torrent.data.files[fileIndex2].Created) return false;

                Log("Downloading next " + Torrent.file.paths[fileIndex2]);

                bitSwarm.IncludeFiles(new List<string>() { Torrent.file.paths[fileIndex2] });

                if (!bitSwarm.isRunning) bitSwarm.Start();

                fileIndexNext = fileIndex2;
                return true;
            }

            return false;
        }
        private void OnFinishing(object source, BitSwarm.FinishingArgs e) { Log("Download of " + Torrent.file.paths[fileIndexNext == -1 ? fileIndex : fileIndexNext] + " finished"); e.Cancel = DownloadNext(); }
        private void StatsUpdated(object source, BitSwarm.StatsUpdatedArgs e) { player.renderer.NewMessage(OSDMessage.Type.TorrentStats, $"{(downloadNextStarted ? "(N) " : "")}D: {e.Stats.PeersDownloading} | W: {e.Stats.PeersChoked}/{e.Stats.PeersInQueue} | {String.Format("{0:n0}", (e.Stats.DownRate / 1024))} KB/s | {e.Stats.Progress}%"); }
        private void MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            Torrent     = e.Torrent;
            sortedPaths = Utils.GetMoviesSorted(Torrent.file.paths);

            Log("Metadata Received");
            Torrent = e.Torrent;

            List<string>    paths   = new List<string>();
            List<long>      lengths = new List<long>();

            foreach (string path in Torrent.file.paths)
                paths.Add(path);

            foreach (long length in Torrent.file.lengths)
                lengths.Add(length);

            player.MediaFilesClbk?.BeginInvoke(paths, lengths, null, null);
        }

        public void ParseSettingsToBitSwarm()
        {
            bitSwarmOpt.FolderComplete  = config.DownloadPath;
            bitSwarmOpt.FolderIncomplete= config.DownloadTemp;
            bitSwarmOpt.FolderTorrents  = config.DownloadTemp;
            bitSwarmOpt.FolderSessions  = config.DownloadTemp;

            bitSwarmOpt.SleepModeLimit  = config.SleepMode;
            bitSwarmOpt.MinThreads      = config.MinThreads;
            bitSwarmOpt.MaxThreads      = config.MaxThreads;
            bitSwarmOpt.BlockRequests   = config.BlockRequests;
            //bitSwarmOpt.PieceTimeout    = config.TimeoutGlobal;
            //bitSwarmOpt.PieceRetries    = config.RetriesGlobal;
        }
        private void Log(string msg) { if (player.verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [BitSwarm] {msg}"); }
    }
}
