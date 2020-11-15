/* Torrent Streaming implementation based on BitSwarm [https://github.com/SuRGeoNix/BitSwarm]
 * 
 * by John Stamatakis
 */

using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

using SuRGeoNix.BEP;

namespace SuRGeoNix.Flyleaf
{
    public class MediaStreamer
    {
        #region Declaration
        MediaDecoder            decoder;
        MediaRouter             player;

        FileStream              fsStream;   // For Debugging
        BitSwarm                bitSwarm;
        BitSwarm.DefaultOptions bitSwarmOpt;
        Torrent                 torrent;
        Thread                  threadBuffer;
        public Settings.Torrent config;     // public until we fix settings generally

        public void ParseSettingsToBitSwarm()
        {
            if (bitSwarmOpt == null)    bitSwarmOpt = new BitSwarm.DefaultOptions();

            bitSwarmOpt.SleepModeLimit  = config.SleepMode;
            bitSwarmOpt.MinThreads      = config.MinThreads;
            bitSwarmOpt.MaxThreads      = config.MaxThreads;
            bitSwarmOpt.BlockRequests   = config.BlockRequests;
            //bitSwarmOpt.PieceTimeout    = config.TimeoutGlobal;
            //bitSwarmOpt.PieceRetries    = config.RetriesGlobal;
        }

        public int              AudioExternalDelay  { get; set; }
        public int              SubsExternalDelay   { get; set; }
        public bool             isBuffering => status == Status.BUFFERING;

        public Action<bool>                     BufferingDoneClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;
        public Action<int, int, int, int>       StatsClbk;

        public long fileSize;
        long        fileSizeNext;
        int         fileIndex;
        int         fileLastPiece;
        long        fileDistance;
        int         verbosity;
        List<string> sortedPaths;
        bool        downloadNextStarted;

        public enum Status
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
        private StreamType streamType;
        #endregion

        #region Initialize
        public MediaStreamer(MediaRouter player, int verbosity = 0)
        {
            this.player                 = player;
            this.verbosity              = verbosity;

            decoder                     = new MediaDecoder(player, verbosity);
            decoder.isForBuffering      = true;
            decoder.HWAccel             = false;
            decoder.Threads             = 1;
            decoder.BufferingDone       = BufferingDone;
        }
        public void Initialize()
        {
            Pause();

            fileIndex       = -1;
            fileDistance    = -1;
            status          = Status.STOPPED;

            bitSwarm?.Dispose(); bitSwarm = null;
        }
        public void Dispose()  { Initialize(); }
        public void Pause()
        {
            decoder.Pause();
            status = Status.STOPPED;
            Utils.EnsureThreadDone(threadBuffer);
        }
        #endregion

        #region Main Actions
        public int Open(string url, StreamType streamType = StreamType.TORRENT, bool isMagnetLink = true)
        {
            this.streamType = streamType;
            status          = Status.OPENING;
            Initialize();

            if (streamType == StreamType.FILE)
            { 
                fsStream = new FileStream(url, FileMode.Open, FileAccess.Read);
                fileSize = fsStream.Length;

                Torrent torrent     = new Torrent("whatever");
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
                ParseSettingsToBitSwarm();
                bitSwarmOpt.DownloadPath    = config.DownloadPath;
                bitSwarmOpt.PieceTimeout    = config.TimeoutOpen;
                bitSwarmOpt.PieceRetries    = config.RetriesOpen;

                //bitSwarmOpt = new BitSwarm.DefaultOptions();
                //bitSwarmOpt.SleepModeLimit  = config.SleepMode;
                //bitSwarmOpt.MinThreads      = config.MinThreads;
                //bitSwarmOpt.MaxThreads      = config.MaxThreads;
                //bitSwarmOpt.BlockRequests   = config.BlockRequests;

                

                //bitSwarmOpt.LogPeer = true;
                //bitSwarmOpt.LogStats = true;
                //bitSwarmOpt.Verbosity = 4;
                //bitSwarmOpt.DownloadPath = @"c:\root\_fly\01";

                bitSwarm = new BitSwarm(bitSwarmOpt);

                bitSwarm.MetadataReceived   += MetadataReceived;
                bitSwarm.OnFinishing        += FileReceived;
                bitSwarm.StatsUpdated       += Stats;

                try
                {
                    if (isMagnetLink)
                        bitSwarm.Initiliaze(new Uri(url));
                    else
                        bitSwarm.Initiliaze(url);
                } catch (Exception e) { Log($"[MS] BitSwarm Failed Opening Url {e.Message}\r\n{e.StackTrace}"); Initialize(); status = Status.FAILED; return -1; }

                bitSwarm.Start();
                sortedPaths = null;
                status      = Status.OPENED;
            }

            return 0;
        }
        public int SetMediaFile(string fileName)
        {
            Log($"File Selected {fileName}");
            
            if (streamType == StreamType.FILE)
            {
                Log($"[BB OPENING]");
                int ret = decoder.Open(null, "", "", "", DecoderRequestsBuffer, fileSize);
                if (ret != 0) return ret;

                Log($"[DD OPENING]");
                ret = player.decoder.Open(null, "", "", "", DecoderRequests, fileSize);
                if (ret != 0) return ret;

                player.decoder.video.url = fileName;
                return 0;
            }
            else if (streamType == StreamType.TORRENT)
            {
                Pause();

                fileIndex       = torrent.file.paths.IndexOf(fileName);
                fileSize        = torrent.file.lengths[fileIndex];
                fileDistance    = 0;
                for (int i=0; i<fileIndex; i++) fileDistance += torrent.file.lengths[i];
                fileLastPiece   = FilePosToPiece(fileSize);

                if (!torrent.data.files[fileIndex].FileCreated)
                    bitSwarm.IncludeFiles(new List<string>() { fileName });

                if (!torrent.data.files[fileIndex].FileCreated && !bitSwarm.isRunning)
                    bitSwarm.Start();

                status = Status.BUFFERING;

                if (sortedPaths == null) sortedPaths = Utils.GetMoviesSorted(torrent.file.paths);
                downloadNextStarted = false;

                Log($"[BB OPENING]");
                int ret = decoder.Open(null, "", "", "", DecoderRequestsBuffer, fileSize);
                if (ret != 0) return ret;

                Log($"[DD OPENING]");
                ret = player.decoder.Open(null, "", "", "", DecoderRequests, fileSize);
                player.decoder.video.url = fileName;

                // Download Subtitles
                if (ret == 0 && (player.DownloadSubs == MediaRouter.DownloadSubsMode.FilesAndTorrents || player.DownloadSubs == MediaRouter.DownloadSubsMode.Torrents))
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        ms.Write(FileRead(0, 65536), 0, 65536);
                        ms.Write(FileRead(fileSize - 65536, 65536), 0, 65536);
                        ms.Position = 0;

                        string hash = Utils.ToHexadecimal(OpenSubtitles.ComputeMovieHash(ms, fileSize));
                        player.FindAvailableSubs(fileName, hash, fileSize);
                    }

                } else if (ret == 0) player.OpenNextAvailableSub();

                status = Status.OPENED;
                DownloadNext();

                return ret;
            }

            return -1;
        }

        public int Buffer(int fromMs, bool foreward = false)
        {
            if (streamType != StreamType.TORRENT || (torrent != null && fileIndex > -1 && torrent.data.files[fileIndex].FileCreated))
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

                    player.renderer.NewMessage(OSDMessage.Type.Buffering, $"Loading 0%");
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
        private void BufferingDone(bool success, double diff)
        {
            if (success)
            {
                bitSwarmOpt.PieceTimeout = config.TimeoutGlobal;
                bitSwarmOpt.PieceRetries = config.RetriesGlobal;

                player.renderer.ClearMessages(OSDMessage.Type.Buffering);
                BufferingDoneClbk?.BeginInvoke(success, null, null);
            }
            else
            {
                // Not accurate [should calculate BitSwarm's downloading progress & pieces required]
                if (diff < 0) diff = 0; else if (diff > 1) diff = 1;
                player.renderer.NewMessage(OSDMessage.Type.Buffering, $"Loading {(int)(diff * 100)}%", null, 40000);
                player.Render();
            } 
        }

        private void FileReceived(object source, BitSwarm.FinishingArgs e) { Log("Download of " + torrent.file.paths[this.fileIndex] + " finished"); e.Cancel = DownloadNext(); }
        private bool DownloadNext()
        {
            if (config.DownloadNext && !downloadNextStarted && streamType == StreamType.TORRENT && torrent != null && fileIndex > -1 && torrent.data.files[fileIndex].FileCreated)
            {
                downloadNextStarted = true;

                var fileIndex = sortedPaths.IndexOf(torrent.file.paths[this.fileIndex]) + 1;
                if (fileIndex > sortedPaths.Count - 1) return false;

                var fileIndex2 = torrent.file.paths.IndexOf(sortedPaths[fileIndex]);
                if (fileIndex2 == -1 || torrent.data.files[fileIndex2].FileCreated) return false;

                fileSizeNext = torrent.file.lengths[fileIndex2];
                Log("Downloading next " + torrent.file.paths[fileIndex2]);

                bitSwarm.IncludeFiles(new List<string>() { torrent.file.paths[fileIndex2] });

                if (!bitSwarm.isRunning) 
                {
                    bitSwarm.Start();
                    return false;
                }

                return true;
            }

            return false;
        }
        private void Stats(object source, BitSwarm.StatsUpdatedArgs e)
        {
            //long curFileSize = !downloadNextStarted ? fileSize : fileSizeNext;
            //if (curFileSize == 0) return;

            var downPercentage = e.Stats.Progress; // (int)(((curFileSize - (torrent.data.progress.GetAll0().Count * (long)torrent.file.pieceLength)) / (decimal)curFileSize) * 100);
            if (downPercentage < 0) downPercentage = 0; else if (downPercentage > 100) downPercentage = 100;

            player.renderer.NewMessage(OSDMessage.Type.TorrentStats, $"{(downloadNextStarted ? "(N) " : "")}D: {e.Stats.PeersDownloading} | W: {e.Stats.PeersChoked}/{e.Stats.PeersInQueue} | {String.Format("{0:n0}", (e.Stats.DownRate / 1024))} KB/s | {downPercentage}%");
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

                return FileRead(pos, len);
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
            {
                return FileRead(pos, len);
            }
        }
        #endregion

        #region Misc
        private byte[] FileRead(long pos, long len)
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

            do { Thread.Sleep(40); } 
            while (torrent.data.progress.GetFirst0(pieceFrom, pieceTo) != -1 && isBuffering);

            return isBuffering ? torrent.data.files[fileIndex].Read(pos, len) : null;
        }
        private int FilePosToPiece(long pos)
        {
            int piece = (int)((fileDistance + pos) / torrent.file.pieceLength);

            if (piece >= torrent.data.pieces) piece = torrent.data.pieces - 1;

            return piece;
        }
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [STREAMER] {msg}"); }
        #endregion
    }
}