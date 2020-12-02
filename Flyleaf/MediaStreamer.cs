/* Torrent Streaming implementation based on BitSwarm [https://github.com/SuRGeoNix/BitSwarm]
 * 
 * TODO
 * 
 * Issue: TCPClient .NET Standard -> .NET Framework (Dual mode IPv4/IPv6 fails)
 * [2601:1c0:c801:9060:f995:8e8e:af94:4210] Exception -> None of the discovered or specified addresses match the socket address family.
 * https://github.com/dotnet/runtime/issues/26036
 * 
 * by John Stamatakis
 */

using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

using SuRGeoNix.BitSwarmLib;
using SuRGeoNix.BitSwarmLib.BEP;

namespace SuRGeoNix.Flyleaf
{
    public class MediaStreamer
    {
        #region Declaration
        MediaDecoder            decoder;
        MediaRouter             player;
        BitSwarm                bitSwarm;
        Torrent                 torrent;
        Thread                  threadBuffer;

        public string           FolderComplete      { get; private set; }
        public string           FileName            { get; private set; }
        public long             FileSize            { get; private set; }
        public bool             IsBuffering => status == Status.BUFFERING;
        public bool             RequiresBuffering => torrent.data.files[fileIndex] != null && !torrent.data.files[fileIndex].Created;

        public Action<bool>                     BufferingDoneClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;
        public Action<int, int, int, int>       StatsClbk;

        //bool        seemsCompleted;
        int         fileIndex;
        int         fileIndexNext;
        int         fileLastPiece;
        long        fileDistance;
        bool        downloadNextStarted;
        List<string> sortedPaths;

        enum Status
        {
            STOPPED,
            OPENING,
            OPENED,
            BUFFERING,
            BUFFERED,
            FAILED
        }
        Status status;

        public enum StreamType
        {
            FILE,
            TORRENT
        }
        StreamType  streamType;
        FileStream          fsStream;   // For Debugging
        #endregion

        #region Initialize
        public MediaStreamer(MediaRouter player)
        {
            this.player                 = player;

            decoder                     = new MediaDecoder(player, player.verbosity);
            decoder.isForBuffering      = true;
            decoder.HWAccel             = false;
            decoder.Threads             = 1;
            decoder.BufferingDone       = BufferingDone;
        }
        public void Initialize()
        {
            Pause();

            fileIndex       = -1;
            fileIndexNext   = -1;
            fileDistance    = -1;
            status          = Status.STOPPED;

            bitSwarm?.Dispose();
            torrent?.Dispose();
            bitSwarm = null;
            torrent = null;
        }
        #endregion

        #region Open | SetMediaFile | GetMovieHash | Pause
        public int Open(string url, StreamType streamType = StreamType.TORRENT)
        {
            this.streamType = streamType;
            status          = Status.OPENING;
            Initialize();

            if (streamType == StreamType.FILE)
            { 
                fsStream = new FileStream(url, FileMode.Open, FileAccess.Read);
                FileSize = fsStream.Length;

                Torrent torrent     = new Torrent(null);
                torrent.file        = new Torrent.TorrentFile();
                torrent.file.paths  = new List<string>();
                torrent.file.lengths= new List<long>();

                torrent.file.paths.  Add("MediaFile1.mp4");
                torrent.file.paths.  Add("MediaFile2.mp4");
                torrent.file.lengths.Add(123123894);
                torrent.file.lengths.Add(123123897);

                MetadataReceived(this, new BitSwarm.MetadataReceivedArgs(torrent));

                status = Status.OPENED;
            }
            else if (streamType == StreamType.TORRENT)
            {
                ParseSettingsToBitSwarm(); // Resets Options (Note: can be changed during a sessions)
                bitSwarmOpt.PieceTimeout    = config.TimeoutOpen;
                bitSwarmOpt.PieceRetries    = config.RetriesOpen;

                // Testing
                //bitSwarmOpt.SleepModeLimit  = config.SleepMode;
                //bitSwarmOpt.MinThreads      = config.MinThreads;
                //bitSwarmOpt.MaxThreads      = config.MaxThreads;
                //bitSwarmOpt.BlockRequests   = config.BlockRequests;

                //bitSwarmOpt.TrackersPath    = @"c:\root\trackers2.txt";

                //bitSwarmOpt.FolderComplete      = @"c:\root\_f01";
                //bitSwarmOpt.FolderIncomplete    = @"c:\root\_f01";
                //bitSwarmOpt.FolderSessions      = @"c:\root\_f01";
                //bitSwarmOpt.FolderTorrents      = @"c:\root\_f01";

                //bitSwarmOpt.LogPeer = true;
                //bitSwarmOpt.LogTracker = true;
                //bitSwarmOpt.LogStats = true;
                //bitSwarmOpt.Verbosity = 4;

                bitSwarm = new BitSwarm(bitSwarmOpt);

                bitSwarm.MetadataReceived   += MetadataReceived;
                bitSwarm.OnFinishing        += DownloadCompleted;
                bitSwarm.StatsUpdated       += Stats;

                try
                {
                    //seemsCompleted = false;
                    bitSwarm.Open(url);
                }
                catch (Exception e)
                {
                    Log($"BitSwarm Failed Opening Url {e.Message} [Will consider completed]");

                    if (System.Text.RegularExpressions.Regex.IsMatch(e.Message, "completed or is invalid"))
                    {
                        //seemsCompleted = true;
                        sortedPaths = null;
                        status      = Status.OPENED;

                        MetadataReceived(this, new BitSwarm.MetadataReceivedArgs(bitSwarm.torrent));

                        return 0;
                    }
                    
                    Initialize();
                    status = Status.FAILED;
                    return -1;    
                }

                bitSwarm.Start();
                sortedPaths = null;
                status      = Status.OPENED;
            }

            return 0;
        }
        public int SetMediaFile(string fileName)
        {
            Log($"{streamType.ToString()}: File Selected {fileName}");
            
            if (streamType == StreamType.TORRENT)
            {
                Pause();

                FileName        = fileName;
                fileIndex       = torrent.file.paths.IndexOf(fileName);
                FileSize        = torrent.file.lengths[fileIndex];
                fileDistance    = 0;
                for (int i=0; i<fileIndex; i++) fileDistance += torrent.file.lengths[i];
                fileLastPiece   = FilePosToPiece(FileSize);
                
                if (sortedPaths == null) sortedPaths = Utils.GetMoviesSorted(torrent.file.paths);
                downloadNextStarted = false;
                fileIndexNext = -1;

                FolderComplete  = torrent.file.paths.Count == 1 ? config.DownloadPath : torrent.data.folder;

                if (torrent.data.files[fileIndex] != null)
                {
                    if (!torrent.data.files[fileIndex].Created)
                    {
                        bitSwarm.IncludeFiles(new List<string>() { fileName });
                        if (!bitSwarm.isRunning) bitSwarm.Start();
                    }
                }

                // Both complete & incomplete files are missing!
                else
                {
                    if (File.Exists(Path.Combine(FolderComplete, FileName)))
                    {
                        status = Status.OPENED;
                        if (!DownloadNext()) bitSwarm.Pause();
                        player.UrlType = MediaRouter.InputType.TorrentFile;
                        return player.decoder.Open(Path.Combine(FolderComplete, FileName));
                    }
                    else
                        return -5; // File Missing?!
                }
                
                player.UrlType = MediaRouter.InputType.TorrentPart;
                status = Status.BUFFERING;
                player.renderer.NewMessage(OSDMessage.Type.Buffering, $"Buffering ...", null, 30000);

                Log($"[BB OPENING]");
                int ret = decoder.Open(null, "", "", "", DecoderRequestsBuffer, FileSize);
                if (ret != 0) return ret;

                Log($"[DD OPENING]");
                ret = player.decoder.Open(null, "", "", "", DecoderRequests, FileSize);
                if (ret != 0) return ret;

                status = Status.OPENED;
                DownloadNext();

                return ret;
            }
            else
            {
                Log($"[BB OPENING]");
                int ret = decoder.Open(null, "", "", "", DecoderRequestsBuffer, FileSize);
                if (ret != 0) return ret;

                Log($"[DD OPENING]");
                ret = player.decoder.Open(null, "", "", "", DecoderRequests, FileSize);
                if (ret != 0) return ret;

                return 0;
            }
        }
        public string GetMovieHash()
        {
            string hash = null;

            using (MemoryStream ms = new MemoryStream())
            {
                status = Status.BUFFERING;
                ms.Write(FileRead(0, 65536), 0, 65536);
                ms.Write(FileRead(FileSize - 65536, 65536), 0, 65536);
                ms.Position = 0;
                status = Status.OPENED;
                hash = Utils.ToHexadecimal(OpenSubtitles.ComputeMovieHash(ms, FileSize));
            }

            return hash;
        }
        public void Pause()
        {
            status = Status.STOPPED;
            decoder.Pause();
            Utils.EnsureThreadDone(threadBuffer);
        }
        #endregion

        #region Data Retrieval | Buffering
        public int Buffer(int fromMs, bool foreward = false)
        {
            if (streamType != StreamType.TORRENT || (torrent != null && fileIndex > -1 && torrent.data.files[fileIndex] != null && torrent.data.files[fileIndex].Created))
                { BufferingDoneClbk?.BeginInvoke(true, null, null); return 0; }
            
            if (!decoder.isReady) return -1;

            Pause();
            status = Status.BUFFERING;

            threadBuffer = new Thread(() =>
            {
                try
                {
                    //bitSwarm.CancelRequestedPieces();
                    bitSwarmOpt.PieceTimeout = config.TimeoutBuffer;
                    bitSwarmOpt.PieceRetries = config.RetriesBuffer;

                    player.renderer.NewMessage(OSDMessage.Type.Buffering, $"Buffering ...", null, 30000);
                    decoder.video.RegisterStreams(player.decoder.video.GetRegisteredStreams());
                    decoder.video.activeStreamIds = new List<int>();
                    for (int i=0; i<player.decoder.video.activeStreamIds.Count; i++)
                        decoder.video.activeStreamIds.Add(player.decoder.video.activeStreamIds[i]);

                    decoder.video.DisableEmbeddedSubs();
                    decoder.video.BufferPackets(fromMs, config.BufferDuration, foreward); // TODO: Expose as property

                } catch (Exception e) { Log("Seeking Error " + e.Message + " - " + e.StackTrace); }
                
            });
            threadBuffer.SetApartmentState(ApartmentState.STA);
            threadBuffer.Start();

            return 0;
        }
        private byte[] DecoderRequests(long pos, int len)
        {
            if (streamType == StreamType.FILE)
            {  
                byte[] data = new byte[len];
                
                //Console.WriteLine($"[DD] [REQUEST] [POS: {pos}] [LEN: {len}]");

                lock (fsStream)
                {
                    fsStream.Seek(pos, SeekOrigin.Begin);
                    fsStream.Read(data, 0, len);
                }

                return data;
            }
            else
            {
                if (torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) == -1)
                    return torrent.data.files[fileIndex].Read(pos, len);

                return FileRead(pos, len, true);
            }
        }
        private byte[] DecoderRequestsBuffer(long pos, int len)
        {
            if (streamType == StreamType.FILE)
            {
                byte[] data = new byte[len];

                lock (fsStream)
                {
                    fsStream.Seek(pos, SeekOrigin.Begin);
                    fsStream.Read(data, 0, len);
                }

                return data;
            }
            else
                return FileRead(pos, len);
        }
        private byte[] FileRead(long pos, long len, bool isMainDecoder = false)
        {
            int pieceFrom   = FilePosToPiece(pos);
            int pieceTo     = FilePosToPiece(pos + len);

            if (torrent.data.progress.GetFirst0(pieceFrom, pieceTo) == -1)
            {
                //Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [RR] [REQUEST] [POS: {pos}] [LEN: {len}] [PIECES: {pieceFrom} - {pieceTo}]");
                return torrent.data.files[fileIndex].Read(pos, len);
            }

            bitSwarm.FocusArea = new Tuple<int, int>(pieceFrom, fileLastPiece);
            //Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [BB] [REQUEST] [POS: {pos}] [LEN: {len}] [PIECES: {pieceFrom} - {pieceTo}]");

            bool notCanceled;

            do
            {
                Thread.Sleep(40);
                notCanceled = isMainDecoder || (!isMainDecoder && IsBuffering);
                //notCanceled = (!isMainDecoder && isBuffering) || (isMainDecoder && !player.isStopping && !player.isStopped);
            }
            while (torrent.data.progress.GetFirst0(pieceFrom, pieceTo) != -1 && notCanceled);

            return notCanceled ? torrent.data.files[fileIndex].Read(pos, len) : null;
        }
        #endregion

        #region Events [BufferingDone | MetadataReceived | DownloadCompleted + DownloadNext | Stats]
        private void BufferingDone(bool success, double diff)
        {
            if (success)
            {
                bitSwarmOpt.PieceTimeout = config.TimeoutGlobal;
                bitSwarmOpt.PieceRetries = config.RetriesGlobal;

                player.renderer.ClearMessages(OSDMessage.Type.Buffering);
                BufferingDoneClbk?.BeginInvoke(success, null, null);
            }
            // Disabled: Not accurate [should calculate BitSwarm's downloading progress & pieces required]
            //else
            //{
            //    if (diff < 0) diff = 0; else if (diff > 1) diff = 1;
            //    player.renderer.NewMessage(OSDMessage.Type.Buffering, $"Loading {(int)(diff * 100)}%", null, 40000);
            //    player.Render();
            //} 
        }
        private void MetadataReceived(object source, BitSwarm.MetadataReceivedArgs e)
        {
            Log("Metadata Received");
            torrent = e.Torrent;

            List<string>    paths   = new List<string>();
            List<long>      lengths = new List<long>();

            foreach (string path in torrent.file.paths)
                paths.Add(path);

            foreach (long length in torrent.file.lengths)
                lengths.Add(length);

            MediaFilesClbk?.BeginInvoke(paths, lengths, null, null);
        }
        private void DownloadCompleted(object source, BitSwarm.FinishingArgs e) { Log("Download of " + torrent.file.paths[fileIndexNext == -1 ? fileIndex : fileIndexNext] + " finished"); e.Cancel = DownloadNext(); }
        private bool DownloadNext()
        {
            if (config.DownloadNext && !downloadNextStarted && streamType == StreamType.TORRENT && torrent != null && fileIndex > -1 && (torrent.data.files[fileIndex] == null || torrent.data.files[fileIndex].Created))
            {
                downloadNextStarted = true;

                var fileIndex = sortedPaths.IndexOf(torrent.file.paths[this.fileIndex]) + 1;
                if (fileIndex > sortedPaths.Count - 1) return false;

                var fileIndex2 = torrent.file.paths.IndexOf(sortedPaths[fileIndex]);
                if (fileIndex2 == -1 || torrent.data.files[fileIndex2] == null || torrent.data.files[fileIndex2].Created) return false;

                Log("Downloading next " + torrent.file.paths[fileIndex2]);

                bitSwarm.IncludeFiles(new List<string>() { torrent.file.paths[fileIndex2] });

                if (!bitSwarm.isRunning) bitSwarm.Start();

                fileIndexNext = fileIndex2;
                return true;
            }

            return false;
        }
        private void Stats(object source, BitSwarm.StatsUpdatedArgs e) { player.renderer.NewMessage(OSDMessage.Type.TorrentStats, $"{(downloadNextStarted ? "(N) " : "")}D: {e.Stats.PeersDownloading} | W: {e.Stats.PeersChoked}/{e.Stats.PeersInQueue} | {String.Format("{0:n0}", (e.Stats.DownRate / 1024))} KB/s | {e.Stats.Progress}%"); }
        #endregion

        #region Misc
        private int FilePosToPiece(long pos)
        {
            int piece = (int)((fileDistance + pos) / torrent.file.pieceLength);

            if (piece >= torrent.data.pieces) piece = torrent.data.pieces - 1;

            return piece;
        }
        private void Log(string msg) { if (player.verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [STREAMER] {msg}"); }

        Options bitSwarmOpt = new Options();
        public Settings.Torrent config;     // public until we fix settings generally
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
        #endregion
    }
}