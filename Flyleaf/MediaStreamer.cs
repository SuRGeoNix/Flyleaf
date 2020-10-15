/* Torrent Streaming implementation based on TorSwarm [https://github.com/SuRGeoNix/TorSwarm]
 * 
 * by John Stamatakis
 */

using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

using SuRGeoNix.TorSwarm;

using BitSwarm = SuRGeoNix.TorSwarm.TorSwarm;

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
        Thread                  openSubs;

        public int              AudioExternalDelay  { get; set; }
        public int              SubsExternalDelay   { get; set; }
        public int              BufferMs            { get; set; } = 5500;

        public Action<bool>                     BufferingDoneClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;
        public Action<int, int, int, int>       StatsClbk;

        readonly object      lockerBuffering     = new object();
        Dictionary<long, Tuple<long, int>>  localFocusPoints    = new Dictionary<long, Tuple<long, int>>();

        public long fileSize;
        int         fileIndex;
        long        fileDistance;
        int         verbosity;

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

        // Constructors
        public MediaStreamer(MediaRouter player, int verbosity = 0)
        {
            this.player                 = player;
            this.verbosity              = verbosity;

            decoder                     = new MediaDecoder(player, verbosity);
            decoder.HWAccel             = false;
            //decoder.doSubs              = false;
            decoder.Threads             = 1; // Useless for non decoding?
            decoder.BufferingDone       = BufferingDone;
        }
        private void Initialize()
        {
            fileIndex       = -1;
            fileDistance    = -1;
            status          = Status.STOPPED;

            try
            {
                decoder.Pause();
                if (torrent  != null) { torrent.Dispose(); torrent = null; }
            }
            catch (Exception) { }
        }

        // Main Communication with MediaRouter / UI
        public void Pause() { decoder.Pause(); }
        public void Stop()  { tsStream?.Stop(); tsStream = null; decoder.Pause(); Initialize(); }

        public int Open(string url, StreamType streamType = StreamType.TORRENT, bool isMagnetLink = true)
        {
            Log("Opening");
            this.streamType = streamType;

            status = Status.OPENING;
            Initialize();

            if ( streamType == StreamType.FILE )
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
            else if ( streamType == StreamType.TORRENT )
            {
                if ( tsStream != null ) tsStream.Dispose();

                BitSwarm.OptionsStruct  opt = BitSwarm.GetDefaultsOptions();
                opt.FocusPointCompleted = FocusPointCompleted;
                opt.TorrentCallback     = MetadataReceived;
                opt.StatsCallback       = Stats;
                opt.PieceTimeout        = 4300;
                //opt.LogStats            = true;
                //opt.Verbosity           = 1;

                try
                {
                    if (isMagnetLink)
                        tsStream = new BitSwarm(new Uri(url), opt);
                    else
                        tsStream = new BitSwarm(url, opt);
                } catch (Exception e) { Log($"[MS] BitSwarm Failed Opening Url {e.Message}\r\n{e.StackTrace}"); Initialize(); status = Status.FAILED; return -1; }

                try
                {
                    tsStream.Start();
                } catch (Exception e) { Log($"[MS] BitSwarm is Dead, What should I Do? {e.Message}\r\n{e.StackTrace}"); Initialize(); status = Status.FAILED; return -1;}
                
                status = Status.OPENED;
            }

            return 0;
        }
        private void MetadataReceived(Torrent torrent)
        {
            Log("Metadata Received");
            this.torrent            = torrent;

            // Clone
            List<string>    paths   = new List<string>();
            List<long>      lengths = new List<long>();

            foreach (string path in torrent.file.paths)
                paths.Add(path);

            foreach (long length in torrent.file.lengths)
                lengths.Add(length);

            MediaFilesClbk?.BeginInvoke(paths, lengths, null, null);
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

                lock (localFocusPoints)
                {
                    foreach (KeyValuePair<long, Tuple<long, int>> curLFPKV in localFocusPoints)
                        tsStream.DeleteFocusPoint(curLFPKV.Key);
                     
                    localFocusPoints.Clear();
                }

                fileIndex       = torrent.file.paths.IndexOf(fileName);
                fileSize        = torrent.file.lengths[fileIndex];
                fileDistance    = 0;

                for (int i=0; i<fileIndex; i++)
                    fileDistance += torrent.file.lengths[i];

                if (!torrent.data.files[fileIndex].FileCreated) tsStream.IncludeFiles(new List<string>() { fileName });
                if ( !torrent.data.files[fileIndex].FileCreated && !tsStream.isRunning ) tsStream.Start();

                Log($"[BB OPENING]");
                int ret = decoder.Open(null, "", "", "", DecoderRequestsBuffer, fileSize);
                if (ret != 0) return ret;
                Log($"[DD OPENING]");
                ret = player.decoder.Open(null, "", "", "", DecoderRequests, fileSize);
                player.decoder.video.url = fileName;
                if (ret == 0 && (player.DownloadSubs == MediaRouter.DownloadSubsMode.FilesAndTorrents || player.DownloadSubs == MediaRouter.DownloadSubsMode.Torrents))
                {
                    Utils.EnsureThreadDone(openSubs);

                    openSubs = new Thread(() =>
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            string              hash;
                            List<OpenSubtitles> subs;

                            while (torrent.data.progress.GetFirst0(FilePosToPiece(0), FilePosToPiece(0 + 65536)) != -1 || torrent.data.progress.GetFirst0(FilePosToPiece(fileSize - 65536), FilePosToPiece(fileSize - 65536 + 65536)) != -1)
                            {
                                if ( UpdateFocusPoints(0, 65536) == -1 ) CreateFocusPoint(0, 65536);
                                if ( UpdateFocusPoints(fileSize - 65536, 65536) == -1 ) CreateFocusPoint(fileSize - 65536, 65536);
                                Thread.Sleep(50);
                            }
                            ms.Write(torrent.data.files[fileIndex].Read(0, 65536), 0, 65536);
                            ms.Write(torrent.data.files[fileIndex].Read(fileSize - 65536, 65536), 0, 65536);
                            ms.Position = 0;

                            hash = Utils.ToHexadecimal(OpenSubtitles.ComputeMovieHash(ms, fileSize));
                            player.FindAvailableSubs(fileName, hash, fileSize);
                        }
                    });
                    openSubs.SetApartmentState(ApartmentState.STA);
                    openSubs.Start();
                }

                return ret;
            }

            return -1;
        }
        private void Stats(BitSwarm.StatsStructure stats)
        {
            var downPercentage = (int)(((fileSize - (torrent.data.progress.GetAll0().Count * (long)torrent.file.pieceLength)) / (decimal)fileSize) * 100);
            if (downPercentage < 0) downPercentage = 0; else if (downPercentage > 100) downPercentage = 100;

            player.renderer.NewMessage(OSDMessage.Type.TorrentStats, $"D: {stats.PeersDownloading} | W: {stats.PeersChoked}/{stats.PeersInQueue} | {String.Format("{0:n0}", (stats.DownRate / 1024))} KB/s | {downPercentage}%");
        }


        public void Seek(int ms)
        {
            if ( streamType == StreamType.TORRENT && torrent != null && fileIndex > -1 && torrent.data.files[fileIndex].FileCreated ) { BufferingDoneClbk?.BeginInvoke(true, null, null); return; }
            if ( !decoder.isReady ) return;

            decoder.Pause();

            status = Status.BUFFERING;

            lock (localFocusPoints)
            {
                foreach (KeyValuePair<long, Tuple<long, int>> curLFPKV in localFocusPoints)
                    tsStream.DeleteFocusPoint(curLFPKV.Key);

                localFocusPoints.Clear();
            }

            player.renderer.NewMessage(OSDMessage.Type.Buffering, $"Loading 0%");
            decoder.video.BufferPackets(ms, BufferMs); // TODO: Expose as property
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
                if (diff < 0) diff = 0; else if (diff > 100) diff = 100;
                player.renderer.NewMessage(OSDMessage.Type.Buffering, $"Loading {(int)(diff * 100)}%", null, 40000);
                player.Render(); // Not the best in here TBR
            } 
        }


        // External Decoder        | (FFmpeg AVIO)
        public byte[] DecoderRequests(long pos, int len)
        {
            byte[] data = null;
            if ( streamType == StreamType.FILE )
            {
                Log($"[DD] [REQUEST] [POS: {pos}] [LEN: {len}]");

                data = new byte[len];
                
                lock (fsStream)
                {
                    fsStream.Seek(pos, SeekOrigin.Begin);
                    fsStream.Read(data, 0, len);
                }
            }
            else if ( streamType == StreamType.TORRENT )
            {
                if ( torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) == -1 ) return torrent.data.files[fileIndex].Read(pos, len);

                if ( UpdateFocusPoints(pos, len) == -1 ) CreateFocusPoint(pos, len);

                while ( torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) != -1 )
                    Thread.Sleep(20);

                //Log($"[DD] [REQUEST] [POS: {pos}] [LEN: {len}] {mType}");

                data = torrent.data.files[fileIndex].Read(pos, len);

                return data;
            }

            return data;
        }

        // Internal Decoder Buffer | (FFmpeg AVIO)
        private byte[] DecoderRequestsBuffer(long pos, int len)
        {
            byte[] data = null;
            if ( streamType == StreamType.FILE )
            {
                //Log($"[BB] [REQUEST] [POS: {pos}] [LEN: {len}]");
                data = new byte[len];

                lock (fsStream)
                {
                    fsStream.Seek(pos, SeekOrigin.Begin);
                    fsStream.Read(data, 0, len);
                }
            }
            else if ( streamType == StreamType.TORRENT )
            {
                lock (lockerBuffering)
                {
                    if ( torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) == -1 ) return torrent.data.files[fileIndex].Read(pos, len);

                    //if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE) Log($"[BB] [REQUEST] [POS: {pos}] [LEN: {len}] {mType}");
                    if ( UpdateFocusPoints(pos, len) == -1 ) CreateFocusPoint(pos, len);

                    while ( torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) != -1 )
                        Thread.Sleep(20);

                    //Log($"[BB] [REQUEST] [POS: {pos}] [LEN: {len}] {mType}");

                    data = torrent.data.files[fileIndex].Read(pos, len);

                    return data;
                }
                
            }

            return data;
        }

        // Local / BitSwarm Focus Points
        private long UpdateFocusPoints(long pos, int len)
        {
            // -1 Not Merged | -2 Not Processed (Sub-Range) | > 0 Processed LFP Key

            List<Tuple<long,int>> checkByteRangesMerge = new List<Tuple<long, int>>();
            List<Tuple<long,int>> mergedByteRange;
            checkByteRangesMerge.Add(new Tuple<long, int>(pos, len));
            long changedLFP = -1;

            lock ( localFocusPoints )
            {
                foreach (KeyValuePair<long, Tuple<long, int>> curLFPKV in localFocusPoints)
                {
                    Tuple<long, int> curLFP = curLFPKV.Value;

                    checkByteRangesMerge.Add(curLFP);

                    // Skiping Sub-Range (Set Currently to -2)
                    if ( pos >= curLFP.Item1 && pos + len <= curLFP.Item1 + curLFP.Item2 )
                    {
                        //Log($"[FP] [SKIPPING] [POS1: {pos}] [LEN1: {len}] [POS2: {curLFP.Item1}] [LEN2: {curLFP.Item2}]");
                        changedLFP = -2;
                        checkByteRangesMerge.RemoveAt(1);
                        continue; // In case of next merge
                    }

                    // Merging Byte Ranges
                    if ( (mergedByteRange = SuRGeoNix.Utils.MergeByteRanges(checkByteRangesMerge, 3)).Count == 1)
                    {
                        //Log($"[FP] [UPDATE] [POS: {mergedByteRange[0].Item1}] [LEN: {mergedByteRange[0].Item2}] [POS1: {pos}] [LEN1: {len}] [POS2: {curLFP.Item1}] [LEN2: {curLFP.Item2}]");
                        DeleteFocusPoint(curLFPKV.Key);
                        CreateFocusPoint(mergedByteRange[0].Item1, mergedByteRange[0].Item2);

                        changedLFP = curLFPKV.Key;
                        break;
                    }

                    checkByteRangesMerge.RemoveAt(1);
                }
            }

            return changedLFP;
        }
        private void CreateFocusPoint(long pos, int len)
        {
            //Log($"[FP] [CREATE] [POS: {pos}] [LEN: {len}] [PIECE_FROM: {filePosToPiece(pos)}] [PIECE_TO: {filePosToPiece(pos + len)}]");
            //Log($"[DEBUG002] Requestin Focus Point from {filePosToPiece(pos)} to {filePosToPiece(pos + len)}");
            lock (localFocusPoints) localFocusPoints[pos] = new Tuple<long, int>(pos, len);
            tsStream.CreateFocusPoint(new BitSwarm.FocusPoint(pos, FilePosToPiece(pos), FilePosToPiece(pos + len)));
        }
        private void DeleteFocusPoint(long pos)
        {
            tsStream.DeleteFocusPoint(pos);
            lock (localFocusPoints) localFocusPoints.Remove(pos);
            //Log($"[FP] [DELETE] [POS: {pos}] [LEN: {localFocusPoints[pos].Item2}] [PIECE_FROM: {filePosToPiece(pos)}] [PIECE_TO: {filePosToPiece(pos + localFocusPoints[pos].Item2)}]");
        }
        private void FocusPointCompleted(long id) { lock (localFocusPoints) localFocusPoints.Remove(id); }

        // Misc
        private int FilePosToPiece(long pos) { return (int)((fileDistance + pos) / torrent.file.pieceLength); }
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [STREAMER] {msg}"); }
    }
}