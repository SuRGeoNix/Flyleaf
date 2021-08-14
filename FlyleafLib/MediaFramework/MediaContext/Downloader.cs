using System;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaRemuxer;

namespace FlyleafLib.MediaFramework.MediaContext
{
    /// <summary>
    /// Downloads or remuxes to different format any ffmpeg valid input url
    /// </summary>
    public unsafe class Downloader : RunThreadBase
    {
        /// <summary>
        /// The backend demuxer. Access this to enable the required streams for downloading
        /// </summary>
        public Demuxer      Demuxer             { get; private set; }

        /// <summary>
        /// The backend remuxer. Normally you shouldn't access this
        /// </summary>
        public Remuxer      Remuxer             { get; private set; }

        /// <summary>
        /// The current timestamp of the frame starting from 0 (Ticks)
        /// </summary>
        public long         CurTime             { get => _CurTime; private set => Set(ref _CurTime,  value); }
        long _CurTime;

        /// <summary>
        /// The total duration of the input (Ticks)
        /// </summary>
        public long         Duration            { get => _Duration; private set => Set(ref _Duration, value); }
        long _Duration;

        /// <summary>
        /// The percentage of the current download process (0 for live streams)
        /// </summary>
        public double       DownloadPercentage  { get => _DownloadPercentage;     set => Set(ref _DownloadPercentage,  value); }
        double _DownloadPercentage;
        double downPercentageFactor;

        public Downloader(Config.Demuxer config, int uniqueId = -1) : base(uniqueId)
        {
            Demuxer = new Demuxer(config, MediaType.Video, UniqueId, false);
            Remuxer = new Remuxer(UniqueId);
            threadName = "Downloader";
        }

        /// <summary>
        /// Fires on download completed or failed
        /// </summary>
        public event EventHandler<bool> DownloadCompleted;
        protected virtual void OnDownloadCompleted(bool success)
        {
            Task.Run(() =>
            {
                Dispose();
                DownloadCompleted?.Invoke(this, success);
            });
        }

        /// <summary>
        /// Opens the demuxer and fills streams info
        /// </summary>
        /// <param name="url">The filename or url to open</param>
        /// <returns></returns>
        public int Open(string url)
        {
            lock (lockActions)
            {
                Dispose();

                Status = Status.Opening;
                int ret = Demuxer.Open(url);
                if (ret != 0) return ret;

                CurTime = 0;
                DownloadPercentage = 0;
                Duration = Demuxer.IsLive ? 0 : Demuxer.Duration;
                downPercentageFactor = Duration / 100.0;
                Disposed = false;

                return 0;
            }
        }

        /// <summary>
        /// Downloads the currently configured AVS streams
        /// </summary>
        /// <param name="filename">The filename for the downloaded video. The file extension will let the demuxer to choose the output format (eg. mp4). If you useRecommendedExtension will be updated with the extension.</param>
        /// <param name="useRecommendedExtension">Will try to match the output container with the input container</param>
        public void Download(ref string filename, bool useRecommendedExtension = true)
        {
            lock (lockActions)
            {
                if (Status != Status.Opening || Disposed)
                    { OnDownloadCompleted(false); return; }

                if (useRecommendedExtension)
                    filename = $"{filename}.{Demuxer.Extension}";

                int ret = Remuxer.Open(filename);
                if (ret != 0)
                    { OnDownloadCompleted(false); return; }

                for(int i=0; i<Demuxer.EnabledStreams.Count; i++)
                    if (Remuxer.AddStream(Demuxer.AVStreamToStream[Demuxer.EnabledStreams[i]].AVStream) != 0)
                        Log($"Failed to add stream {Demuxer.AVStreamToStream[Demuxer.EnabledStreams[i]].Type} {Demuxer.AVStreamToStream[Demuxer.EnabledStreams[i]].StreamIndex}");

                if (!Remuxer.HasStreams || Remuxer.WriteHeader() != 0)
                    { OnDownloadCompleted(false); return; }

                Start();
            }
        }

        /// <summary>
        /// Stops and disposes the downloader
        /// </summary>
        public void Dispose()
        {
            if (Disposed) return;

            lock (lockActions)
            {
                if (Disposed) return;

                Stop();
            
                Demuxer.Dispose();
                Remuxer.Dispose();
                
                Status = Status.Stopped;
                Disposed = true;
            }
        }

        protected override void RunInternal()
        {
            if (!Remuxer.HasStreams) { OnDownloadCompleted(false); return; }

            Demuxer.Start();

            long startTime = Demuxer.hlsCtx == null ? Demuxer.StartTime : Demuxer.hlsCtx->first_timestamp * 10;

            do
            {
                // While Packets Queue Empty (Ended | Quit if Demuxer stopped | Wait until we get packets)
                if (Demuxer.Packets.Count == 0)
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueEmpty;

                    while (Demuxer.Packets.Count == 0 && Status == Status.QueueEmpty)
                    {
                        if (Demuxer.Status == Status.Ended)
                        {
                            Status = Status.Ended;
                            if (Demuxer.Interrupter.Interrupted == 0) DownloadPercentage = 100;
                            break;
                        }
                        else if (!Demuxer.IsRunning)
                        {
                            Log($"Demuxer is not running [Demuxer Status: {Demuxer.Status}]");

                            lock (Demuxer.lockStatus)
                            lock (lockStatus)
                            {
                                if (Demuxer.Status == Status.Pausing || Demuxer.Status == Status.Paused)
                                    Status = Status.Pausing;
                                else if (Demuxer.Status != Status.Ended)
                                    Status = Status.Stopping;
                                else
                                    continue;
                            }

                            break;
                        }
                        
                        Thread.Sleep(20);
                    }

                    lock (lockStatus)
                    {
                        if (Status != Status.QueueEmpty) break;
                        Status = Status.Running;
                    }
                }

                Demuxer.Packets.TryDequeue(out IntPtr pktPtr);
                AVPacket* packet = (AVPacket*) pktPtr;

                if (packet->dts > 0)
                {
                    double curTime = (packet->dts * Demuxer.AVStreamToStream[packet->stream_index].Timebase) - startTime;
                    if (_Duration > 0) DownloadPercentage = curTime / downPercentageFactor;
                    CurTime = (long) curTime;
                }

                Remuxer.Write(packet);

            } while (Status == Status.Running);

            if (Status != Status.Pausing && Status != Status.Paused)
                OnDownloadCompleted(Remuxer.WriteTrailer() == 0);
            else
                Demuxer.Pause();
        }
    }
}
