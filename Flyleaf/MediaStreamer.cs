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

        FileStream              fsStream; // For Debugging
        BitSwarm                tsStream;
        Torrent                 torrent;
        Thread                  threadBuffer;

        public int              AudioExternalDelay  { get; set; }
        public int              SubsExternalDelay   { get; set; }
        public bool             isBuffering => status == Status.BUFFERING;

        public Action<bool>                     BufferingDoneClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;
        public Action<int, int, int, int>       StatsClbk;

        readonly object                         lockerBuffering     = new object();

        public long fileSize;
        int         fileIndex;
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

            tsStream?.Dispose(); tsStream = null;
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

                MetadataReceived(torrent);

                status = Status.OPENED;
            }
            else if (streamType == StreamType.TORRENT)
            {
                BitSwarm.OptionsStruct  opt = BitSwarm.GetDefaultsOptions();
                opt.TorrentCallback     = MetadataReceived;
                opt.StatsCallback       = Stats;
                opt.PieceTimeout        = 4300;
                //opt.LogStats            = true;
                //opt.Verbosity           = 2;

                try
                {
                    if (isMagnetLink)
                        tsStream = new BitSwarm(new Uri(url), opt);
                    else
                        tsStream = new BitSwarm(url, opt);
                } catch (Exception e) { Log($"[MS] BitSwarm Failed Opening Url {e.Message}\r\n{e.StackTrace}"); Initialize(); status = Status.FAILED; return -1; }

                tsStream.Start();
                sortedPaths = null;
                status = Status.OPENED;
            }

            return 0;
        }
        public int SetMediaFile(string fileName)
        {
            Log($"File Selected {fileName}");
            
            if ( streamType == StreamType.FILE )
            {
                int ret = decoder.Open(null, "", "", "", DecoderRequestsBuffer, fileSize);
                return ret;
            }
            else if ( streamType == StreamType.TORRENT )
            {
                Pause();

                fileIndex       = torrent.file.paths.IndexOf(fileName);
                fileSize        = torrent.file.lengths[fileIndex];
                fileDistance    = 0;
                for (int i=0; i<fileIndex; i++) fileDistance += torrent.file.lengths[i];

                if (!torrent.data.files[fileIndex].FileCreated)
                    tsStream.IncludeFiles(new List<string>() { fileName });

                if (!torrent.data.files[fileIndex].FileCreated && !tsStream.isRunning)
                    tsStream.Start();

                status = Status.BUFFERING;

                if (sortedPaths == null)
                {
                    sortedPaths = new List<string>();
                    foreach (var path in torrent.file.paths) sortedPaths.Add(path);
                    sortedPaths.Sort(new Utils.NaturalStringComparer());
                }
                downloadNextStarted = false;

                Log($"[BB OPENING]");
                int ret = decoder.Open(null, "", "", "", DecoderRequestsBuffer, fileSize);
                if (ret != 0) return ret;

                Log($"[DD OPENING]");
                ret = player.decoder.Open(null, "", "", "", DecoderRequests, fileSize);
                player.decoder.video.url = fileName;

                if (ret == 0 && (player.DownloadSubs == MediaRouter.DownloadSubsMode.FilesAndTorrents || player.DownloadSubs == MediaRouter.DownloadSubsMode.Torrents))
                {
                    if (!torrent.data.files[fileIndex].FileCreated)
                    {
                        while (torrent.data.progress.GetFirst0(FilePosToPiece(0), FilePosToPiece(0 + 65536)) != -1 || torrent.data.progress.GetFirst0(FilePosToPiece(fileSize - 65536), FilePosToPiece(fileSize - 65536 + 65536)) != -1)
                        {
                            CreateFocusPoint(0, 65536);
                            CreateFocusPoint(fileSize - 65536, 65536);
                            Thread.Sleep(50);
                        }
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        ms.Write(torrent.data.files[fileIndex].Read(0, 65536), 0, 65536);
                        ms.Write(torrent.data.files[fileIndex].Read(fileSize - 65536, 65536), 0, 65536);
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
        public int Buffer(int fromMs, int durationMs, bool foreward = false)
        {
            if (streamType == StreamType.TORRENT && torrent != null && fileIndex > -1 && torrent.data.files[fileIndex].FileCreated)
                { BufferingDoneClbk?.BeginInvoke(true, null, null); return 0; }
            
            if (!decoder.isReady ) return -1;

            Pause();
            status = Status.BUFFERING;

            threadBuffer = new Thread(() =>
            {
                try
                {
                    player.renderer.NewMessage(OSDMessage.Type.Buffering, $"Loading 0%");
                    decoder.video.RegisterStreams(player.decoder.video.GetRegisteredStreams());
                    decoder.video.activeStreamIds = player.decoder.video.activeStreamIds;
                    decoder.video.BufferPackets(fromMs, durationMs, foreward); // TODO: Expose as property

                } catch (Exception e) { Log("Seeking Error " + e.Message + " - " + e.StackTrace); }
                
            });
            threadBuffer.SetApartmentState(ApartmentState.STA);
            threadBuffer.Start();

            return 0;
        }

        private void DownloadNext()
        {
            if (player.DownloadNext && !downloadNextStarted && streamType == StreamType.TORRENT && torrent != null && fileIndex > -1 && torrent.data.files[fileIndex].FileCreated)
            {
                downloadNextStarted = true;

                Log("Download of " + torrent.file.paths[this.fileIndex] + " finished");
                var fileIndex = sortedPaths.IndexOf(torrent.file.paths[this.fileIndex]) + 1;
                if (fileIndex > sortedPaths.Count - 1) return;

                var fileIndex2 = torrent.file.paths.IndexOf(sortedPaths[fileIndex]);
                if (fileIndex2 == -1 || torrent.data.files[fileIndex2].FileCreated) return;

                Log("Downloading next " + torrent.file.paths[fileIndex2]);

                tsStream.IncludeFiles(new List<string>() { torrent.file.paths[fileIndex2] });
                if (!tsStream.isRunning) tsStream.Start();
            }
        }
        private void BufferingDone(bool success, double diff)
        {
            if (success)
            {
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
        private void Stats(BitSwarm.StatsStructure stats)
        {
            if (fileSize == 0) return;

            var downPercentage = (int)(((fileSize - (torrent.data.progress.GetAll0().Count * (long)torrent.file.pieceLength)) / (decimal)fileSize) * 100);
            if (downPercentage < 0) downPercentage = 0; else if (downPercentage > 100) downPercentage = 100;

            player.renderer.NewMessage(OSDMessage.Type.TorrentStats, $"D: {stats.PeersDownloading} | W: {stats.PeersChoked}/{stats.PeersInQueue} | {String.Format("{0:n0}", (stats.DownRate / 1024))} KB/s | {downPercentage}%");

            DownloadNext();
        }
        private void MetadataReceived(Torrent torrent)
        {
            Log("Metadata Received");
            this.torrent            = torrent;

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

                tsStream.DeleteFocusPoints();
                CreateFocusPoint(pos, len);

                while (torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) != -1)
                    Thread.Sleep(20);

                //Log($"[DD] [REQUEST] [POS: {pos}] [LEN: {len}] [PIECES: {FilePosToPiece(pos)} - {FilePosToPiece(pos + len)}]");

                return torrent.data.files[fileIndex].Read(pos, len);
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
                lock (lockerBuffering)
                {
                    if (!isBuffering) return null;

                    if (torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) == -1)
                        return torrent.data.files[fileIndex].Read(pos, len);
                    
                    //Log($"[BB] [REQUEST] [POS: {pos}] [LEN: {len}] [PIECES: {FilePosToPiece(pos)} - {FilePosToPiece(pos + len)}]");

                    tsStream.DeleteFocusPoints();
                    CreateFocusPoint(pos, (torrent.file.pieceLength * 5) + len);
                    
                    while (isBuffering && torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) != -1)
                        Thread.Sleep(20);

                    if (!isBuffering) return null;

                    return torrent.data.files[fileIndex].Read(pos, len);
                }
            }
        }
        #endregion

        #region Misc
        private int FilePosToPiece(long pos)
        {
            int piece = (int)((fileDistance + pos) / torrent.file.pieceLength);
            if (piece >= torrent.data.pieces) piece = torrent.data.pieces -1;
            return piece;
        }
        private void CreateFocusPoint(long pos, int len)
        {
            //Log($"[FP] [CREATE] [POS: {pos}] [LEN: {len}] [PIECE_FROM: {FilePosToPiece(pos)}] [PIECE_TO: {FilePosToPiece(pos + len)}]");
            tsStream.CreateFocusPoint(new BitSwarm.FocusPoint(pos, FilePosToPiece(pos), FilePosToPiece(pos + len)));
        }
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [STREAMER] {msg}"); }
        #endregion
    }
}