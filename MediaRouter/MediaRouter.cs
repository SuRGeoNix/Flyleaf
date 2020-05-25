using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

using static PartyTime.Codecs.FFmpeg;
using System.Diagnostics;
using System.Media;
using System.IO;

namespace PartyTime
{
    public class MediaRouter
    {
        #region Declaration

        Codecs.FFmpeg           decoder;
        MediaStreamer           streamer;
        
        Thread                  screamer, aScreamer, vScreamer, sScreamer;
        Thread                  openOrBuffer;

        Stopwatch               videoClock    = new Stopwatch();
        long                    videoStartTicks    =-1;

        public int              _RATE { get { if (decoder != null ) return decoder._RATE; else return -1; } }
        long                    audioExternalDelay = 0;
        long                    subsExternalDelay  = 0;

        // Queues
        ConcurrentQueue<MediaFrame>       aFrames;
        ConcurrentQueue<MediaFrame>       vFrames;
        ConcurrentQueue<MediaFrame>       sFrames;

        int AUDIO_MIX_QUEUE_SIZE = 50;  int AUDIO_MAX_QUEUE_SIZE =  60;
        int VIDEO_MIX_QUEUE_SIZE =  2;  int VIDEO_MAX_QUEUE_SIZE =   3;
        int  SUBS_MIN_QUEUE_SIZE =  5;  int  SUBS_MAX_QUEUE_SIZE =  10;

        // Status
        enum Status
        {
            OPENING,
            FAILED,
            OPENED,
            BUFFERING,
            PLAYING,
            PAUSED,
            SEEKING,
            STOPPED
        }
        Status status;
        Status beforeSeeking = Status.STOPPED;
        
        // Callbacks
        public Action                       AudioResetClbk;
        public Action<byte[], int, int>     AudioFrameClbk;
        public Action<byte[], long, long>   VideoFrameClbk;
        public Action<string, int>          SubFrameClbk;

        public Action<bool>                 OpenTorrentSuccessClbk;
        public Action<bool, string>         OpenStreamSuccessClbk;
        public Action<bool>                 BufferSuccessClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;

        private static readonly object  lockOpening  = new object();

        // Properties (Public)
        public bool isReady         { get; private set; }
        public bool isPlaying       { get { return (status == Status.PLAYING); } }
        public bool isPaused        { get { return (status == Status.PAUSED); } }
        public bool isSeeking       { get { return (status == Status.SEEKING); } }
        public bool isStopped       { get { return (status == Status.STOPPED); } }
        public bool isFailed        { get { return (status == Status.FAILED); } }
        public bool isOpened        { get { return (status == Status.OPENED); } }
        public bool isTorrent       { get; private set; }
        public bool hasAudio        { get; private set; }
        public bool hasVideo        { get; private set; }
        public bool hasSubs         { get; private set; }
        public int  Width           { get; private set; }
        public int  Height          { get; private set; }
        public long Duration        { get; private set; }
        public long CurTime         { get; private set; }
        public int  verbosity       { get; set; }
        public bool HighQuality     { get { return decoder.HighQuality; } set { decoder.HighQuality = value; } }
        public bool HWAcceleration  { get; set; }
        public long AudioExternalDelay
        {
            get { return audioExternalDelay; }

            set
            {
                if (!decoder.isReady || !isReady || !decoder.hasAudio) return;
                //if (CurTime + audioExternalDelay < decoder.aStreamInfo.startTimeTicks || CurTime + audioExternalDelay > decoder.aStreamInfo.durationTicks) return;
             
                audioExternalDelay = value;
                streamer.AudioExternalDelay = (int) (value / 10000);
                AudioResetClbk.Invoke();
            }
        }
        public long SubsExternalDelay
        {
            get { return subsExternalDelay; }

            set
            {
                if (!decoder.isReady || !isReady || !decoder.hasSubs) return;
                //if (CurTime + subsExternalDelay < decoder.vStreamInfo.startTimeTicks || CurTime + subsExternalDelay > decoder.vStreamInfo.durationTicks) return;

                subsExternalDelay = value;
                streamer.SubsExternalDelay = (int)(value / 10000);                
            }
        }

        #endregion

        // Constructors
        public MediaRouter(int verbosity = 0)
        {
            this.verbosity = verbosity;
            decoder     = new Codecs.FFmpeg(GetFrame, verbosity);
            streamer    = new MediaStreamer(verbosity);

            aFrames     = new ConcurrentQueue<MediaFrame>();
            vFrames     = new ConcurrentQueue<MediaFrame>();
            sFrames     = new ConcurrentQueue<MediaFrame>();

            Initialize();
        }
        private void Initialize()
        {
            if (openOrBuffer!= null) openOrBuffer.Abort();
            if (streamer    != null && isTorrent) streamer.Pause();
            if (decoder     != null)   decoder.Pause();
            if (screamer    != null)  screamer.Abort();
            if (aScreamer   != null) aScreamer.Abort();
            if (vScreamer   != null) vScreamer.Abort();
            if (sScreamer   != null) sScreamer.Abort();

            isReady = false;
            status  = Status.STOPPED;
            decoder.HWAcceleration = HWAcceleration;

            lock (aFrames) aFrames = new ConcurrentQueue<MediaFrame>();
            lock (vFrames) vFrames = new ConcurrentQueue<MediaFrame>();
            lock (sFrames) sFrames = new ConcurrentQueue<MediaFrame>();

            if (streamer != null)
            {
                streamer.MediaFilesClbk         = MediaFilesClbk;
                streamer.BufferingDoneClbk      = BufferingDone;
                streamer.BufferingAudioDoneClbk = BufferingAudioDone;
                streamer.BufferingSubsDoneClbk  = BufferingSubsDone;
                streamer.Stop();
            }
        }
        private void InitializeEnv()
        {
            //audioBytesPerSecond = (int)(_RATE * (_BITS / 8.0) * _CHANNELS);

            CurTime             = 0;
            audioExternalDelay  = 0;
            subsExternalDelay   = 0;

            hasAudio            = decoder.hasAudio; 
            hasVideo            = decoder.hasVideo; 
            hasSubs             = decoder.hasSubs;

            Width               = (hasVideo) ? decoder.vStreamInfo.width         : 0;
            Height              = (hasVideo) ? decoder.vStreamInfo.height        : 0;
            Duration            = (hasVideo) ? decoder.vStreamInfo.durationTicks : decoder.aStreamInfo.durationTicks;

            isReady = true;
        }

        // Implementation
        private void GetFrame(MediaFrame frame, FFmpeg.AutoGen.AVMediaType mType)
        {
            ConcurrentQueue<MediaFrame> curQueue = null;
            int curMaxSize = 0;
            bool mTypeIsRunning = false;

            // CHOOSE QUEUE
            switch (mType)
            {
                case FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO:
                    curQueue        = aFrames;
                    curMaxSize      = AUDIO_MAX_QUEUE_SIZE;
                    mTypeIsRunning  = decoder.isAudioRunning;
                    break;

                case FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO:
                    curQueue        = vFrames;
                    curMaxSize      = VIDEO_MAX_QUEUE_SIZE;
                    mTypeIsRunning  = decoder.isVideoRunning;
                    break;

                case FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    curQueue        = sFrames;
                    curMaxSize      = SUBS_MAX_QUEUE_SIZE;
                    mTypeIsRunning  = decoder.isSubsRunning;
                    break;
            }
            if (curQueue == null) return;

            // IF MAX SLEEP UNTIL 3/4 OF QUEUE LEFT
            if (curQueue.Count == curMaxSize)
            {
                int escapeInfinity = 0;
                int sleepMs = (int)((decoder.vStreamInfo.frameAvgTicks / 10000) * (curMaxSize * (1.0 / 4.0)));
                if (sleepMs > 5000) sleepMs = 5000;
                if (mType == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE) sleepMs *= 10;

                while (mTypeIsRunning && curQueue.Count > (curMaxSize * (3.0 / 4.0)))
                {
                    Thread.Sleep(sleepMs);
                    escapeInfinity++;
                    if (escapeInfinity > 1000)
                    {
                        Log($"[ERROR EI1] {mType} Frames Queue is full ... [{curQueue.Count}/{curMaxSize}]"); decoder.Stop();
                        return;
                    }
                    mTypeIsRunning = false;
                    if      (mType == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO)    mTypeIsRunning = decoder.isAudioRunning;
                    else if (mType == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO)    mTypeIsRunning = decoder.isVideoRunning;
                    else if (mType == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE) mTypeIsRunning = decoder.isSubsRunning;
                }
            }
            if (!mTypeIsRunning && (mType != FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO && status != Status.SEEKING) ) return;

            // FILL QUEUE | Queue possible not thread-safe
            try
            {
                switch (mType)
                {
                    case FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO:
                        lock (aFrames) curQueue.Enqueue(frame);
                        break;
                    case FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO:
                        lock (vFrames) curQueue.Enqueue(frame);
                        break;
                    case FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                        lock (sFrames) curQueue.Enqueue(frame);
                        break;
                }
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Func: GetFrame(), Msg: " + e.StackTrace); }
        }

        private void VideoScreamer()
        {
            MediaFrame vFrame;
            long distanceTicks;
            long offDistanceMsBackwards = -7;
            long offDistanceMsForewards = 5000;
            int  offDistanceCounter     = 0;
            int  distanceMs;

            videoClock.Restart(); // CurTime Video Clock

            Log($"[VIDEO SCREAMER] Started  -> {videoStartTicks/10000}");

            while (isPlaying)
            {
                if ( vFrames.Count < 1 )
                { 
                    if (decoder.isVideoFinish) { status = Status.STOPPED; return; }

                    Thread restart = new Thread(() => {
                        Seek((int) (CurTime/10000));
                    });
                    restart.SetApartmentState(ApartmentState.STA);
                    restart.Start();
                    return;
                }

                if ( offDistanceCounter > VIDEO_MAX_QUEUE_SIZE )
                {
                    Thread restart = new Thread(() => {
                        Seek((int) (CurTime/10000));
                    });
                    restart.SetApartmentState(ApartmentState.STA);
                    restart.Start();
                    return;
                }

                lock (vFrames) vFrames.TryDequeue(out vFrame);

                distanceTicks   = vFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks);
                distanceMs      = (int) (distanceTicks/10000);

                if ( distanceMs < offDistanceMsBackwards || distanceMs > offDistanceMsForewards )
                {
                    Log($"[VIDEO SCREAMER] Frame Drop   [CurTS: {vFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {distanceMs}] [DiffTicks: {vFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                    offDistanceCounter ++;
                    continue;
                }

                if ( distanceMs > 1 ) Thread.Sleep(distanceMs);

                // Video Frames         [Callback]
                CurTime = vFrame.timestamp;
                Log($"[VIDEO SCREAMER] Frame Scream [CurTS: {vFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {(vFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {vFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, distanceTicks, null, null);
            }

            Log($"[VIDEO SCREAMER] Finished -> {videoStartTicks/10000}");
        }
        private void AudioScreamer()
        {
            MediaFrame aFrame;
            
            long distanceTicks;
            int  distanceMs;

            long offDistanceMsBackwards = -7;
            long offDistanceMsForewards = 1000;
            int  offDistanceCounter     = 0;

            Log($"[AUDIO SCREAMER] Started  -> {videoStartTicks/10000}");

            while (isPlaying)
            {
                if ( aFrames.Count < 1 )
                { 
                    if (decoder.isAudioFinish) return;

                    if (!decoder.isAudioRunning)
                    {
                        Thread.Sleep(100);

                        Thread restart = new Thread(() => {
                            if (isTorrent)
                                streamer.SeekAudio((int) ((CurTime - audioExternalDelay)/10000) - 50);
                            else
                                RestartAudio();
                        });
                        restart.SetApartmentState(ApartmentState.STA);
                        restart.Start();

                        return;
                    }

                    return;
                }
                else
                {
                    if ( offDistanceCounter > AUDIO_MIX_QUEUE_SIZE )
                    {
                        Thread.Sleep(100);

                        Thread restart = new Thread(() => {
                            if (isTorrent)
                                streamer.SeekAudio((int) ((CurTime - audioExternalDelay)/10000) - 50);
                            else
                                RestartAudio();
                        });
                        restart.SetApartmentState(ApartmentState.STA);
                        restart.Start();

                        return;
                    }
                }

                lock (aFrames) aFrames.TryDequeue(out aFrame);

                distanceTicks   = (aFrame.timestamp + audioExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks);
                distanceMs      = (int) (distanceTicks/10000);
                
                if ( distanceMs < offDistanceMsBackwards || distanceMs > offDistanceMsForewards )
                {
                    Log($"[AUDIO SCREAMER] Frame Drop   [CurTS: {aFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {((aFrame.timestamp + audioExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {aFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                    offDistanceCounter ++;
                    continue;
                }

                if ( distanceMs > 3 ) Thread.Sleep(distanceMs - 2);

                // Audio Frames         [Callback]
                Log($"[AUDIO SCREAMER] Frame Scream [CurTS: {aFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {((aFrame.timestamp + audioExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {aFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                AudioFrameClbk(aFrame.data, 0, aFrame.data.Length);
            }

            Log($"[AUDIO SCREAMER] Finished  -> {videoStartTicks/10000}");
        }
        private void SubsScreamer()
        {
            MediaFrame sFrame;
            
            long distanceTicks;
            int  distanceMs;

            long offDistanceMsBackwards = -800;
            int  offDistanceCounter     = 0;

            Log($"[SUBS  SCREAMER] Started  -> {videoStartTicks/10000}");

            while (isPlaying)
            {
                if ( sFrames.Count < 1 )
                { 
                    if (decoder.isSubsFinish) return;

                    if (!decoder.isSubsRunning)
                    {
                        Thread.Sleep(100);

                        Thread restart = new Thread(() => {
                            if (isTorrent)
                                streamer.SeekSubs((int) ((CurTime - subsExternalDelay)/10000) - 100);
                            else
                                RestartSubs();
                        });
                        restart.SetApartmentState(ApartmentState.STA);
                        restart.Start();

                        return;
                    }

                    return;
                }
                else
                {
                    if ( offDistanceCounter > SUBS_MIN_QUEUE_SIZE )
                    {
                        Thread.Sleep(100);

                        Thread restart = new Thread(() => {
                            if (isTorrent)
                                streamer.SeekSubs((int) ((CurTime - subsExternalDelay)/10000) - 100);
                            else
                                RestartSubs();
                        });
                        restart.SetApartmentState(ApartmentState.STA);
                        restart.Start();

                        return;
                    }
                }

                lock (sFrames) sFrames.TryDequeue(out sFrame);

                distanceTicks   = (sFrame.timestamp + subsExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks);
                distanceMs      = (int) (distanceTicks/10000);
                
                if ( distanceMs < offDistanceMsBackwards ) //|| distanceMs > offDistanceMsForewards )
                {
                    Log($"[SUBS  SCREAMER] Frame Drop   [CurTS: {sFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {((sFrame.timestamp + subsExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {sFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                    offDistanceCounter ++;
                    continue;
                }

                if ( distanceMs > 3 ) Thread.Sleep(distanceMs - 2);

                // Audio Frames         [Callback]
                Log($"[SUBS  SCREAMER] Frame Scream [CurTS: {sFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {((sFrame.timestamp + subsExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {sFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                SubFrameClbk.BeginInvoke(sFrame.text, sFrame.duration, null, null);
            }

            Log($"[SUBS  SCREAMER] Finished  -> {videoStartTicks/10000}");
        }

        private void BufferingDone(bool done) { if ( done ) Play2(); }
        private void BufferingAudioDone()
        {
            Log("[AUDIO SCREAMER] Restarting0 ...");
            RestartAudio();
        }
        private void BufferingSubsDone()
        {
            Log("[SUBS  SCREAMER] Restarting0 ...");
            RestartSubs();
        }

        private void RestartSubs()
        {
            if ( sScreamer != null ) sScreamer.Abort();

            if (decoder.hasSubs)
            {
                Log("[SUBS  SCREAMER] Restarting ...");
                
                sScreamer = new Thread(() => {
                    decoder.PauseSubs();
                    if (!isPlaying || videoStartTicks == -1) return;

                    Thread.Sleep(20);

                    lock (sFrames) sFrames = new ConcurrentQueue<MediaFrame>();

                    decoder.SeekAccurate((int) ((CurTime - subsExternalDelay)/10000) - 100, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                    decoder.RunSubs();

                    while (isPlaying && decoder.hasSubs && sFrames.Count < SUBS_MIN_QUEUE_SIZE && !decoder.isSubsFinish ) 
                        Thread.Sleep(20);

                    try
                    {
                        SubsScreamer();
                    } catch (Exception) { }
                });
                sScreamer.SetApartmentState(ApartmentState.STA);
                sScreamer.Start();
            }
        }
        private void RestartAudio()
        {
            if ( aScreamer != null ) aScreamer.Abort();

            if (decoder.hasAudio)
            {
                Log("[AUDIO SCREAMER] Restarting ...");

                aScreamer = new Thread(() => {
                    decoder.PauseAudio();
                    if (!isPlaying || videoStartTicks == -1) return;

                    Thread.Sleep(20);

                    lock (aFrames) aFrames = new ConcurrentQueue<MediaFrame>();
                    AudioResetClbk.Invoke();

                    decoder.SeekAccurate((int) ((CurTime - audioExternalDelay)/10000) - 50, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO);
                    decoder.RunAudio();

                    while (isPlaying && (decoder.hasAudio && aFrames.Count < AUDIO_MIX_QUEUE_SIZE) ) 
                        Thread.Sleep(20);

                    try
                    {
                        AudioScreamer();
                    } catch (Exception) { }
                });
                aScreamer.SetApartmentState(ApartmentState.STA);
                aScreamer.Start();
            }
        }

        public void Open(string url)
        {
            int ret;

            lock (lockOpening)
            {
                if ( decoder.status == Codecs.FFmpeg.Status.OPENING) Thread.Sleep(50);
                if (openOrBuffer!=null) openOrBuffer.Abort();

                if (decoder != null) decoder.Pause();
                Initialize();

                status = Status.OPENING;
            }

            string ext      = url.Substring(url.LastIndexOf(".") + 1);
            string scheme   = url.Substring(0, url.IndexOf(":"));

            if (ext.ToLower() == "torrent" || scheme.ToLower() == "magnet")
            {
                isTorrent = true;
                openOrBuffer = new Thread(() =>
                {
                    try
                    {
                        if (scheme.ToLower() == "magnet")
                            ret = streamer.Open(url, MediaStreamer.StreamType.TORRENT);
                        else
                            ret = streamer.Open(url, MediaStreamer.StreamType.TORRENT, false);

                        if (ret != 0) { status = Status.FAILED; OpenTorrentSuccessClbk?.BeginInvoke(false, null, null); return; }

                        status = Status.OPENED;
                        OpenTorrentSuccessClbk?.BeginInvoke(true, null, null);

                    } catch (ThreadAbortException) { return; }
                });
                openOrBuffer.SetApartmentState(ApartmentState.STA);
                openOrBuffer.Start();
            }
            else
            {
                lock (lockOpening)
                {
                    if ( decoder.status == Codecs.FFmpeg.Status.OPENING) Thread.Sleep(50);
                    isTorrent = false;
                    decoder.Open(url);
                    if (!decoder.isReady) { status = Status.FAILED; return; }
                    InitializeEnv();
                    status = Status.OPENED;
                }
                
            }
        }
        public int OpenSubs(string url)
        {
            int ret;

            if ( sScreamer != null ) sScreamer.Abort();
            
            if ( !decoder.hasVideo)                     return -1;
            if ( (ret = decoder.OpenSubs(url)) != 0 )   { hasSubs = false; return ret; }

            hasSubs             = decoder.hasSubs;
            subsExternalDelay   = 0;

            RestartSubs();

            return 0;
        }

        public void Play()
        {
            if (!decoder.isReady || decoder.isRunning || isPlaying) return;

            if ( isTorrent )
            {
                status = Status.BUFFERING;
                streamer.Seek((int)(CurTime/10000));
                return;
            }

            Play2();
        }
        private void Play2()
        {
            if (screamer    != null)  screamer.Abort();
            if (aScreamer   != null) aScreamer.Abort();
            if (vScreamer   != null) vScreamer.Abort();
            if (sScreamer   != null) sScreamer.Abort();

            lock (aFrames) aFrames = new ConcurrentQueue<MediaFrame>();
            lock (vFrames) vFrames = new ConcurrentQueue<MediaFrame>();
            lock (sFrames) sFrames = new ConcurrentQueue<MediaFrame>();

            status = Status.PLAYING;

            screamer = new Thread(() =>
            {
                // Reset AVS Decoders to CurTime

                // Video | Seek | Set First Video Timestamp | Run Decoder
                videoStartTicks = -1;
                if (vFrames.Count < 1)
                    decoder.SeekAccurate((int) (CurTime/10000), FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO);
                if (vFrames.Count < 1) return;
                
                MediaFrame vFrame;
                lock (vFrames) vFrames.TryPeek(out vFrame);
                CurTime         = vFrame.timestamp;
                videoStartTicks = vFrame.timestamp;
                TimeBeginPeriod(1);

                if (decoder.RunVideo() != 0) return;

                // Audio | Seek | Run Decoder
                if (decoder.hasAudio)
                {
                    AudioResetClbk.Invoke();
                    decoder.SeekAccurate((int) ((CurTime - audioExternalDelay)/10000) - 50, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO);
                    decoder.RunAudio();
                }

                // Audio | Seek | Run Decoder
                if (decoder.hasSubs)
                {
                    decoder.SeekAccurate((int) ((CurTime - subsExternalDelay)/10000) - 100, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                    decoder.RunSubs();
                }

                // Fill Queues at least with Min
                while (isPlaying && vFrames.Count < VIDEO_MIX_QUEUE_SIZE || (decoder.hasAudio && aFrames.Count < AUDIO_MIX_QUEUE_SIZE) || (decoder.hasSubs && sFrames.Count < SUBS_MIN_QUEUE_SIZE && !decoder.isSubsFinish ) ) 
                    Thread.Sleep(20);

                vScreamer = new Thread(() => {
                    try
                    {
                        VideoScreamer();
                    } catch (Exception) { }

                    TimeEndPeriod(1);
                });
                vScreamer.SetApartmentState(ApartmentState.STA);
                
                if (decoder.hasAudio)
                {
                    aScreamer = new Thread(() => {
                        try
                        {
                            AudioScreamer();
                        } catch (Exception) { }
                    });
                    aScreamer.SetApartmentState(ApartmentState.STA);
                }

                if (decoder.hasSubs)
                {
                    sScreamer = new Thread(() =>
                    {
                        try
                        {
                            SubsScreamer();
                        } catch (Exception) { }
                        
                    });
                    sScreamer.SetApartmentState(ApartmentState.STA);
                }

                vScreamer.Start();
                if (decoder.hasAudio) aScreamer.Start();
                if (decoder.hasSubs ) sScreamer.Start();

            });
            screamer.SetApartmentState(ApartmentState.STA);
            screamer.Priority = ThreadPriority.AboveNormal;
            screamer.Start();
        }

        public void Seek(int ms, bool curPos = false)
        {
            try
            {
                if (!isReady) return;
                if (openOrBuffer != null) openOrBuffer.Abort();
                if (!isSeeking) beforeSeeking = status;

                if (streamer    != null && isTorrent) streamer.Pause();
                if (decoder     != null)   decoder.Pause();
                if (screamer    != null)  screamer.Abort();
                if (aScreamer   != null) aScreamer.Abort();
                if (vScreamer   != null) vScreamer.Abort();
                if (sScreamer   != null) sScreamer.Abort();
            } catch (Exception) { }

            openOrBuffer = new Thread(() =>
            {
                try
                {
                    status = Status.SEEKING;

                    while (!decoder.isStopped) Thread.Sleep(10);
                    Thread.Sleep(20);

                    if (curPos) ms += (int)(CurTime / 10000);

                    if ((long)ms * 10000 < decoder.vStreamInfo.startTimeTicks)
                        CurTime = decoder.vStreamInfo.startTimeTicks;
                    else if ((long)ms * 10000 > decoder.vStreamInfo.durationTicks)
                        CurTime = decoder.vStreamInfo.durationTicks;
                    else
                        CurTime = (long)ms * 10000;

                    lock (aFrames) aFrames = new ConcurrentQueue<MediaFrame>();
                    lock (vFrames) vFrames = new ConcurrentQueue<MediaFrame>();
                    lock (sFrames) sFrames = new ConcurrentQueue<MediaFrame>();

                    // Scream 1st Seek Frame
                    videoStartTicks = -1;
                    decoder.SeekAccurate((int)(CurTime/10000), FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO);
                    if (vFrames.Count > 0)
                    {
                        MediaFrame vFrame;
                        lock (vFrames) vFrames.TryPeek(out vFrame);
                        CurTime         = vFrame.timestamp;
                        videoStartTicks = vFrame.timestamp;
                        VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, 0, null, null);
                    }

                    if (beforeSeeking == Status.PLAYING) Play();

                } catch (Exception) { }
            });
            openOrBuffer.SetApartmentState(ApartmentState.STA);
            openOrBuffer.Start();
        }

        public int Pause()
        {
            status = Status.PAUSED;

            if (decoder  != null) decoder.Pause();
            if (screamer != null) screamer.Abort();
            if (aScreamer != null) aScreamer.Abort();
            if (vScreamer != null) vScreamer.Abort();
            if (sScreamer != null) sScreamer.Abort();

            return 0;
        }
        public int Stop()
        {
            if (decoder != null) decoder.Stop();

            Initialize();
            CurTime = 0;

            return 0;
        }

        public void StopMediaStreamer() { if ( streamer != null ) { streamer.Stop(); streamer = null; } }
        public void SetMediaFile(string fileName)
        {
            if (openOrBuffer != null) { openOrBuffer.Abort(); Thread.Sleep(20); }

            openOrBuffer = new Thread(() => {

                // Open Decoder Buffer
                int ret = streamer.SetMediaFile(fileName);
                if (ret != 0) { status = Status.FAILED; OpenStreamSuccessClbk?.BeginInvoke(false, fileName, null, null); }

                // Open Decoder
                ret = decoder.Open(null, streamer.DecoderRequests, streamer.fileSize);
                if (ret != 0) { status = Status.FAILED; OpenStreamSuccessClbk?.BeginInvoke(false, fileName, null, null); }

                if (!decoder.isReady) { status = Status.FAILED; OpenStreamSuccessClbk?.BeginInvoke(false, fileName, null, null); }

                InitializeEnv();
                OpenStreamSuccessClbk?.BeginInvoke(true, fileName, null, null);
            });
            openOrBuffer.SetApartmentState(ApartmentState.STA);
            openOrBuffer.Start();
        }
        
        // Misc
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.ffff")}] {msg}"); }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        private static extern uint TimeEndPeriod(uint uMilliseconds);
    }
}