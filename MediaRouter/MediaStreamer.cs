using FFmpeg.AutoGen;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using SuRGeoNix;
using SuRGeoNix.TorSwarm;

using static PartyTime.Codecs.FFmpeg;

namespace PartyTime
{
    public class MediaStreamer
    {
        #region Declaration
        Codecs.FFmpeg           decoder;

        FileStream              fsStream; // For Debugging
        TorSwarm                tsStream;
        Torrent                 torrent;

        Thread                  aDecoder,   vDecoder,   sDecoder;
        bool                    aDone,      vDone,      sDone;

        public int              AudioExternalDelay  { get; set; }
        public int              SubsExternalDelay   { get; set; }
        public bool             IsSubsExternal      { get; set; }

        public Action<List<string>, List<long>> MediaFilesClbk;
        public Action<bool>                 BufferingDoneClbk;
        public Action                       BufferingAudioDoneClbk { set { if (decoder != null) decoder.BufferingAudioDone   = value; } }
        public Action                       BufferingSubsDoneClbk  { set { if (decoder != null) decoder.BufferingSubsDone    = value; } }

        private static readonly object      lockerBufferDone    = new object();
        private static readonly object      lockerBuffering     = new object();
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
        public MediaStreamer(int verbosity = 0)
        {
            this.verbosity              = verbosity;

            decoder                     = new Codecs.FFmpeg(null, verbosity);
            decoder.HWAcceleration      = false;
            decoder.BufferingDone       = BufferingDone;
        }
        private void Initialize()
        {
            fileIndex       = -1;
            fileDistance    = -1;
            status          = Status.STOPPED;
            IsSubsExternal  = false;

            try
            {
                if (vDecoder != null) vDecoder.Abort();
                if (aDecoder != null) aDecoder.Abort();
                if (sDecoder != null) sDecoder.Abort();
                Thread.Sleep(40);
                if (torrent  != null){torrent.Dispose(); torrent = null;}
            }
            catch (Exception) { }
        }

        // Main Communication with MediaRouter / UI
        public void Pause()
        {
            lock (lockerBufferDone)
            {
                status = Status.STOPPED;
                try
                {
                    if (vDecoder != null) vDecoder.Abort();
                    if (aDecoder != null) aDecoder.Abort();
                    if (sDecoder != null) sDecoder.Abort();
                    Thread.Sleep(40);
                }
                catch (Exception) { }
            }
        }
        public void Stop()
        {
            if ( tsStream != null )
            {
                tsStream.Stop();
                tsStream = null;
            }

            decoder.Pause();
            Initialize();
        }
        public int Open(string url, StreamType streamType = StreamType.TORRENT, bool isMagnetLink = true)
        {
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
                TorSwarm.OptionsStruct  opt = TorSwarm.GetDefaultsOptions();
                opt.FocusPointCompleted = FocusPointCompleted;
                opt.TorrentCallback     = MetadataReceived;
                opt.PieceTimeout        = 4300;

                try
                {
                    if (isMagnetLink)
                        tsStream = new TorSwarm(new Uri(url), opt);
                    else
                        tsStream = new TorSwarm(url, opt);
                } catch (Exception e) { Log($"[MS] TorSwarm Failed Opening Url {e.Message}\r\n{e.StackTrace}"); Initialize(); status = Status.FAILED; return -1; }

                try
                {
                    tsStream.Start();
                } catch (Exception e) { Log($"[MS] TorSwarm is Dead, What should I Do? {e.Message}\r\n{e.StackTrace}"); Initialize(); status = Status.FAILED; return -1;}
                
                status = Status.OPENED;
            }

            return 0;
        }
        private void MetadataReceived(Torrent torrent)
        {
            this.torrent = torrent;
            MediaFilesClbk?.BeginInvoke(torrent.file.paths, torrent.file.lengths, null, null);
        }
        public int SetMediaFile(string fileName)
        {
            Log($"File Selected {fileName}");

            if ( streamType == StreamType.FILE )
            {
                int ret = decoder.Open(null, DecoderRequestsBuffer, fileSize);
                return ret;
            }
            else if ( streamType == StreamType.TORRENT )
            {
                tsStream.IncludeFiles(new List<string>() { fileName });

                fileIndex       = torrent.file.paths.IndexOf(fileName);
                fileSize        = torrent.file.lengths[fileIndex];
                fileDistance    = 0;

                for (int i=0; i<fileIndex; i++)
                    fileDistance += torrent.file.lengths[i];

                // Decoder - Opening Format Contexts (cancellation?) -> DecoderRequests Feed with null?
                Log($"[BB OPENING 1]");
                int ret = decoder.Open(null, DecoderRequestsBuffer, fileSize);
                return ret;
            }

            return -1;
        }
        private void BufferingDone(AVMediaType mType)
        {
            lock (lockerBufferDone)
            {
                if (status != Status.BUFFERING) return;

                if (     mType  == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    aDone = true;
                else if (mType  == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    vDone = true;
                else if (mType  == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    sDone = true;

                if (vDone && (!decoder.hasAudio || aDone) && (!decoder.hasSubs || IsSubsExternal || sDone))
                {
                    Log($"[BUFFER] Done");
                    status = Status.BUFFERED;
                    BufferingDoneClbk?.BeginInvoke(true, null, null);
                }
            }
        }

        // Starts Internal Decoders for Seekings & Buffering
        public void SeekSubs(int ms)
        {
            if ( !decoder.isReady || !decoder.hasSubs || IsSubsExternal ) return;
            if ( streamType == StreamType.TORRENT && torrent != null && torrent.data.files[fileIndex].FileCreated ) { decoder.BufferingSubsDone?.BeginInvoke(null, null); return; }

            try
            {
                if (sDecoder != null) sDecoder.Abort();
                Thread.Sleep(40);
            } catch (Exception) { }
                
            if ( decoder.hasSubs )
            {
                sDecoder = new Thread(() =>
                {
                    decoder.sStatus = Codecs.FFmpeg.Status.RUNNING;
                    if ( decoder.SeekAccurate2((ms - SubsExternalDelay) - 500, AVMediaType.AVMEDIA_TYPE_SUBTITLE) != 0) Log("[SUBS  STREAMER] Error Seeking");
                    decoder.DecodeSilent2(AVMediaType.AVMEDIA_TYPE_SUBTITLE, ((ms -SubsExternalDelay) + 15000) * (long)10000, true);
                    decoder.sStatus = Codecs.FFmpeg.Status.STOPPED;
                });
                sDecoder.SetApartmentState(ApartmentState.STA);
                sDecoder.Start();
            }
        }
        public void SeekAudio(int ms)
        {
            if ( !decoder.isReady || !decoder.hasAudio) return;
            if ( streamType == StreamType.TORRENT && torrent != null && torrent.data.files[fileIndex].FileCreated ) { decoder.BufferingAudioDone?.BeginInvoke(null, null); return; }

            try
            {
                if (aDecoder != null) aDecoder.Abort();
                Thread.Sleep(40);
            } catch (Exception) { }
                
            aDecoder = new Thread(() =>
            {
                decoder.aStatus = Codecs.FFmpeg.Status.RUNNING;
                if ( decoder.SeekAccurate2((ms - AudioExternalDelay) - 400, AVMediaType.AVMEDIA_TYPE_AUDIO) != 0) Log("[AUDIO STREAMER] Error Seeking");
                decoder.DecodeSilent2(AVMediaType.AVMEDIA_TYPE_AUDIO, ((ms - AudioExternalDelay) + 2000) * (long)10000, true);
                decoder.aStatus = Codecs.FFmpeg.Status.STOPPED;
            });
            aDecoder.SetApartmentState(ApartmentState.STA);
            aDecoder.Start();
        }
        public void Seek(int ms)
        {
            if ( streamType == StreamType.TORRENT && torrent != null && torrent.data.files[fileIndex].FileCreated ) { BufferingDoneClbk?.BeginInvoke(true, null, null); return; }
            if ( !decoder.isReady ) return;

            try
            {
                if (vDecoder != null) vDecoder.Abort();
                if (aDecoder != null) aDecoder.Abort();
                if (sDecoder != null) sDecoder.Abort();
                Thread.Sleep(40);
            } catch (Exception) { }
            
            aDone = false; vDone = false; sDone = false;
            status = Status.BUFFERING;

            lock (localFocusPoints)
            {
                foreach (KeyValuePair<long, Tuple<long, int>> curLFPKV in localFocusPoints)
                    tsStream.DeleteFocusPoint(curLFPKV.Key);

                localFocusPoints.Clear();
            }

            vDecoder = new Thread(() =>
            {
                decoder.vStatus = Codecs.FFmpeg.Status.RUNNING;
                if ( decoder.SeekAccurate2(ms - 100, AVMediaType.AVMEDIA_TYPE_VIDEO) != 0) Log("VIDEO STREAMER] Error Seeking");
                decoder.DecodeSilent2(AVMediaType.AVMEDIA_TYPE_VIDEO, (ms + 5500) * (long)10000);
                decoder.vStatus = Codecs.FFmpeg.Status.STOPPED;
            });
            vDecoder.SetApartmentState(ApartmentState.STA);
            vDecoder.Start();

            if (decoder.hasAudio)
            {
                aDecoder = new Thread(() =>
                {
                    decoder.aStatus = Codecs.FFmpeg.Status.RUNNING;
                    if ( decoder.SeekAccurate2((ms - AudioExternalDelay) - 400, AVMediaType.AVMEDIA_TYPE_AUDIO) != 0) Log("[AUDIO STREAMER] Error Seeking");
                    decoder.DecodeSilent2(AVMediaType.AVMEDIA_TYPE_AUDIO, ((ms - AudioExternalDelay) + 2000) * (long)10000);
                    decoder.aStatus = Codecs.FFmpeg.Status.STOPPED;
                });
                aDecoder.SetApartmentState(ApartmentState.STA);
                aDecoder.Start();
            }
            
            if ( decoder.hasSubs && !IsSubsExternal)
            {
                sDecoder = new Thread(() =>
                {
                    decoder.sStatus = Codecs.FFmpeg.Status.RUNNING;
                    if ( decoder.SeekAccurate2((ms - SubsExternalDelay) - 1000, AVMediaType.AVMEDIA_TYPE_SUBTITLE)  != 0) Log("[SUBS  STREAMER] Error Seeking");
                    decoder.DecodeSilent2(AVMediaType.AVMEDIA_TYPE_SUBTITLE, ((ms -SubsExternalDelay) + 15000) * (long)10000);
                    decoder.sStatus = Codecs.FFmpeg.Status.STOPPED;
                });
                sDecoder.SetApartmentState(ApartmentState.STA);
                sDecoder.Start();
            }
        }

        // External Decoder        | (FFmpeg AVIO)
        public byte[]   DecoderRequests(long pos, int len, AVMediaType mType)
        {
            //Log($"[DD] [REQUEST] [POS: {pos}] [LEN: {len}] {isAudio}");

            byte[] data = null;
            if ( streamType == StreamType.FILE )
            {
                //Log($"[DD] [REQUEST] [POS: {pos}] [LEN: {len}] {mType}");

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
                    Thread.Sleep(10);

                Log($"[DD] [REQUEST] [POS: {pos}] [LEN: {len}] {mType}");

                data = torrent.data.files[fileIndex].Read(pos, len);

                return data;
            }

            return data;
        }

        // Internal Decoder Buffer | (FFmpeg AVIO)
        private byte[]  DecoderRequestsBuffer(long pos, int len, AVMediaType mType)
        {
            byte[] data = null;
            if ( streamType == StreamType.FILE )
            {
                Log($"[BB] [REQUEST] [POS: {pos}] [LEN: {len}] {mType}");
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

                    if (mType == AVMediaType.AVMEDIA_TYPE_SUBTITLE) Log($"[BB] [REQUEST] [POS: {pos}] [LEN: {len}] {mType}");
                    if ( UpdateFocusPoints(pos, len) == -1 ) CreateFocusPoint(pos, len);

                    while ( torrent.data.progress.GetFirst0(FilePosToPiece(pos), FilePosToPiece(pos + len)) != -1 )
                        Thread.Sleep(10);

                    //Log($"[BB] [REQUEST] [POS: {pos}] [LEN: {len}] {isAudio}");

                    data = torrent.data.files[fileIndex].Read(pos, len);

                    return data;
                }
                
            }

            return data;
        }

        // Local / TorSwarm Focus Points
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
                    if ( (mergedByteRange = Utils.MergeByteRanges(checkByteRangesMerge, 3)).Count == 1)
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
            tsStream.CreateFocusPoint(new TorSwarm.FocusPoint(pos, FilePosToPiece(pos), FilePosToPiece(pos + len)));
        }
        private void DeleteFocusPoint(long pos)
        {
            tsStream.DeleteFocusPoint(pos);
            lock (localFocusPoints) localFocusPoints.Remove(pos);
            //Log($"[FP] [DELETE] [POS: {pos}] [LEN: {localFocusPoints[pos].Item2}] [PIECE_FROM: {filePosToPiece(pos)}] [PIECE_TO: {filePosToPiece(pos + localFocusPoints[pos].Item2)}]");
        }
        private void FocusPointCompleted(long id) { lock (localFocusPoints) localFocusPoints.Remove(id); }

        
        // Misc
        private int FilePosToPiece(long pos)
        {
            return (int)((fileDistance + pos) / torrent.file.pieceLength);
        }
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.ffff")}] {msg}"); }
    }
}