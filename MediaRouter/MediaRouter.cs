using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

using static PartyTime.Codecs.FFmpeg;

namespace PartyTime
{
    public class MediaRouter
    {
        Codecs.FFmpeg           decoder;
        Thread                  screamer;

        // Audio Output Configuration
        int _BITS = 16; int _CHANNELS = 1; int _RATE = 48000;

        long audioFlowTicks     = 0;
        long audioFlowBytes     = 0;
        long audioLastSyncTicks = 0;
        long audioExternalDelay = 0;
        int  audioBytesPerSecond= 0;

        long subsExternalDelay  = 0;

        // Queues
        Queue<MediaFrame>       aFrames;
        Queue<MediaFrame>       vFrames;
        Queue<MediaFrame>       sFrames;

        int AUDIO_MIX_QUEUE_SIZE = 40;  int AUDIO_MAX_QUEUE_SIZE = 140;
        int VIDEO_MIX_QUEUE_SIZE =  3;  int VIDEO_MAX_QUEUE_SIZE =   4;
        int  SUBS_MIN_QUEUE_SIZE =  3;  int  SUBS_MAX_QUEUE_SIZE = 200;

        // Status
        enum Status
        {
            PLAYING = 1,
            PAUSED  = 2,
            SEEKING = 3,
            STOPPED = 4
        }
        Status status;
        
        // Callbacks
        public Action                       AudioResetClbk;
        public Action<byte[], int, int>     AudioFrameClbk;
        public Action<byte[], long, long>   VideoFrameClbk;
        public Action<string, int>            SubFrameClbk;
        
        // Properties (Public)
        public bool isReady         { get; private set; }
        public bool isPlaying       { get { return (status == Status.PLAYING); } }
        public bool isPaused        { get { return (status == Status.PAUSED); } }
        public bool isSeeking       { get { return (status == Status.SEEKING); } }
        public bool isStopped       { get { return (status == Status.STOPPED); } }
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
                audioExternalDelay = value;
                if (!decoder.isReady || !isReady || !isPlaying) return;

                if (CurTime + audioExternalDelay < decoder.aStreamInfo.startTimeTicks || CurTime + audioExternalDelay > decoder.aStreamInfo.durationTicks) return;
                else
                    ResynchAudio(CurTime - audioExternalDelay);
            }
        }
        public long SubsExternalDelay
        {
            get { return subsExternalDelay; }

            set
            {
                subsExternalDelay = value;
                if (!decoder.isReady || !isReady || !isPlaying) return;

                if (CurTime + subsExternalDelay < decoder.vStreamInfo.startTimeTicks || CurTime + subsExternalDelay > decoder.vStreamInfo.durationTicks) return;
                else
                    ResynchSubs(CurTime - subsExternalDelay);
            }
        }

        // Constructors
        public MediaRouter(int verbosity = 0)
        {
            this.verbosity = verbosity;

            decoder = new Codecs.FFmpeg(GetFrame, verbosity);
            audioBytesPerSecond =(int)( _RATE * (_BITS / 8.0) * _CHANNELS);

            aFrames = new Queue<MediaFrame>();
            vFrames = new Queue<MediaFrame>();
            sFrames = new Queue<MediaFrame>();

            Initialize();
        }
        private void Initialize()
        {
            if (screamer != null) screamer.Abort();

            status = Status.STOPPED;
            decoder.HWAcceleration = HWAcceleration;

            lock (aFrames) aFrames = new Queue<MediaFrame>();
            vFrames = new Queue<MediaFrame>();
            lock (sFrames) sFrames = new Queue<MediaFrame>();
        }

        // Implementation
        private void GetFrame(MediaFrame frame, FFmpeg.AutoGen.AVMediaType mType)
        {
            Queue<MediaFrame> curQueue = null;
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
                    if (escapeInfinity > 30 && mType != FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE || escapeInfinity > 100)
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
            if (!mTypeIsRunning) return;

            // FILL QUEUE
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
        }
        private void Screamer()
        {
            try { 
                if (vFrames.Count < 1) return;

                Log("[SCREAMER] " + status);
                TimeBeginPeriod(1);

                MediaFrame vFrame;

                // Debugging
                int fpscount    = 1;
                int dropFrames  = 0;
                int lagFrames   = 0;

                // Video 
                lock(vFrames) vFrame = vFrames.Dequeue();
                long delayTicks      = vFrame.timestamp;

                // Subs
                if (hasSubs) ResynchSubs(vFrame.timestamp - subsExternalDelay);

                // Audio
                audioFlowTicks = DateTime.UtcNow.Ticks;
                long audioDelayTicks = delayTicks;
                if (decoder.hasAudio)
                    if (vFrame.timestamp + audioExternalDelay < decoder.aStreamInfo.startTimeTicks || vFrame.timestamp + audioExternalDelay > decoder.aStreamInfo.durationTicks) 
                        audioDelayTicks = -10000000; // Force Resync Later (Audio Paused)
                    else ResynchAudio(vFrame.timestamp - audioExternalDelay);

                // Timing
                long curTicks;
                long nowTicks;
                long startTicks;
                long distanceTicks;
                long onTimeTicks    = 3 * 10000;                                                     // -Y < [FRAME] < +Y ms            
                long offTimeTicks   = (decoder.vStreamInfo.frameAvgTicks * 2) + (onTimeTicks * 2);   // 2 Frames Distance + Allowed //long offTimeDistanceTicks = (decoder.vStreamInfo.frameAvgTicks) + (onTimeDistanceTicks * 2);   // 1 Frames Distance + Allowed
                int  sleepMs;

                nowTicks    = DateTime.UtcNow.Ticks;
                startTicks  = nowTicks;

                //long lastTicks = 0;
                //long tmp = 0;

                // Video Frames [Callback]
                CurTime = vFrame.timestamp;
                VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, 0, null, null);

                while (isPlaying)
                {
                    if (vFrames.Count < 1 && (decoder == null || !decoder.isRunning)) break;
                    if (vFrames.Count < 1) { Log(DateTime.UtcNow.Ticks + " [WAITING DECODER ...] " + vFrames.Count + " / " + VIDEO_MIX_QUEUE_SIZE); Thread.Sleep((int)(decoder.vStreamInfo.frameAvgTicks/10000) / 4); continue; }

                    nowTicks                = DateTime.UtcNow.Ticks;
                    lock (vFrames) vFrame   = vFrames.Peek();
                    curTicks                = (nowTicks - startTicks) + delayTicks;
                    distanceTicks           = vFrame.timestamp - curTicks;

                    // FPS DEBUGGING (FPS Calculation)
                    //if (nowTicks - lastTicks >= 10000 * 1000 - (10000 * 10))
                    //{
                    //    Log("[" + (new TimeSpan(vFrame.timestamp)).ToString(@"hh\:mm\:ss") + "/" + (new TimeSpan(decoder.vStreamInfo.durationTicks)).ToString(@"hh\:mm\:ss") + "]\t" +
                    //         "[SEC] \t" + ((nowTicks - lastTicks) / 10000).ToString() + "\t" +
                    //         "[FRAME]\t" + vFrame.pts + "\t" +
                    //         //"[FTIME]\t" + (long)(vFrame.timestamp * videoTimeBaseTicks) + "\t" +
                    //         "[QUEUE]\t" + vFrames.Count + "/" + VIDEO_MAX_QUEUE_SIZE + "\t" +
                    //         "[DROP]\t" + dropFrames + "\t" +
                    //         "[LAGS]\t" + lagFrames + "\t" +
                    //         "[FPS]\t" + fpscount
                    //         );

                    //    lastTicks = nowTicks;
                    //    fpscount = 0;
                    //    tmp++;
                    //    if (tmp > 0 && tmp % 5 == 0) {
                    //        int ms = 5000;

                    //        vFrames = new Queue<MediaFrame>();

                    //        //if (curPos) { ms += (int)(CurTime / 10000); CurTime += ms * 10000; }
                    //        CurTime = ms * 10000;
                    //        if (hasVideo) decoder.SeekAccurate(ms, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO);
                    //        decoder.RunVideo();

                    //        tmp = -100000; }
                    //    //if (tmp > 0 && tmp % 10 == 0) Thread.Sleep(1000);
                    //    //if (tmp > 0) distanceTicks = 5 * 1000 * 10000;
                    //}

                    // ******************* [WAITING] *******************
                    if (distanceTicks > onTimeTicks && distanceTicks < offTimeTicks)
                    {
                        sleepMs = (int)(distanceTicks / 10000) - 2;
                        if (sleepMs > 1) Thread.Sleep(sleepMs);
                    }
                    else if (Math.Abs(distanceTicks) < onTimeTicks)
                    // ******************* [ON TIME] *******************
                    {
                        //Log("[OnTime]\t\tCurTime: " + curTicks / 10000 + ", Distance: " + distanceTicks / 10000 + ", Frame-> " + vFrame.pts + ", FrameTime: " + vFrame.timestamp / 10000);

                        //Log(
                        //    "CurTicks: " + curTicks / 10000 +
                        //    "\t\tdelayTicks: " + delayTicks / 10000 +
                        //    //"\t\tFirstTS: " + aFirstTimestamp / 10000 +
                        //    "\t\tcurPos A: " + audioDelayTicks / 10000 +
                        //    "\t\tFinally A: " + (audioDelayTicks - delayTicks) / 10000
                        //    //"\t\tcurPos B: " + (nowTicks - aStartTicks) / 10000 +
                        //    //"\t\tFinally B: " + (curTicks - ((nowTicks - aStartTicks) + delayTicks)) / 10000
                        //    );

                        // Video Frames         [Callback]
                        lock (vFrames) vFrame = vFrames.Dequeue();
                        CurTime = vFrame.timestamp;
                        VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, distanceTicks, null, null);

                        // Audio
                        if (decoder.hasAudio)
                        {
                            // Audio Sync
                            if (Math.Abs((audioDelayTicks - delayTicks) / 10000) > 220)
                            {
                                if (curTicks + audioExternalDelay < decoder.aStreamInfo.startTimeTicks || curTicks + audioExternalDelay > decoder.aStreamInfo.durationTicks)
                                    audioDelayTicks = -10000000; // Force Resync Later (Audio Paused)
                                else if (ResynchAudio(curTicks - audioExternalDelay)) audioDelayTicks = delayTicks;
                            }

                            // Audio Frames     [Callback]
                            if ( aFrames.Count > 0) SendAudioFrames();
                        }

                        // Subtitiles Frames    [Callback]
                        if (decoder.hasSubs) SendSubFrame();
                        
                        fpscount++;
                    }
                    // ******************* [OFF TIME - FOREWARDS] *******************
                    else if (distanceTicks > offTimeTicks)
                    {
                        Log("[OffTime > 0]\t\tCurTime: " + curTicks / 10000 + ", Distance: " + distanceTicks / 10000 + ", Frame-> " + vFrame.pts + ", FrameTime: " + vFrame.timestamp / 10000);
                        lagFrames   ++;
                        delayTicks  += distanceTicks;
                        lock (vFrames) vFrame = vFrames.Dequeue();
                        VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, 0, null, null);
                    }
                    // ******************* [OFF TIME - BACKWARDS] *******************
                    else if (distanceTicks < onTimeTicks)
                    {
                        Log("[OffTime < 0]\t\tCurTime: " + curTicks / 10000 + ", Distance: " + distanceTicks / 10000 + ", Frame-> " + vFrame.pts + ", FrameTime: " + vFrame.timestamp / 10000);
                        lagFrames   ++;
                        dropFrames  ++;
                        delayTicks  += distanceTicks;
                        lock (vFrames) vFrame = vFrames.Dequeue();
                    }

                }

            } catch (ThreadAbortException) {
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Func: Screamer(), Msg: " + e.StackTrace); }

            TimeEndPeriod(1);
        }

        // Audio        [Send / Sync]
        private void SendAudioFrames()
        {
            double curRate = audioFlowBytes / ((DateTime.UtcNow.Ticks - audioFlowTicks) / 10000000.0);
            if (curRate > audioBytesPerSecond) return;
            
            lock (aFrames)
            {
                int count = 0;
                while (aFrames.Count > 0 && isPlaying && decoder.isRunning) //(limit < 1 || count < limit))
                {
                    MediaFrame aFrame = aFrames.Dequeue();
                    AudioFrameClbk(aFrame.data, 0, aFrame.data.Length);
                    audioFlowBytes += aFrame.data.Length;
                    count++;

                    // Check on every frame that we send to ensure the buffer will not be full
                    curRate = audioFlowBytes / ((DateTime.UtcNow.Ticks - audioFlowTicks) / 10000000.0);
                    if (curRate > audioBytesPerSecond) return;
                }
            }
        }
        private bool ResynchAudio(long syncTimestamp)
        {
            // Give it 1 Second
            if (DateTime.UtcNow.Ticks - audioLastSyncTicks < 10000000) return false;
            audioLastSyncTicks = DateTime.UtcNow.Ticks;

            lock (aFrames)
            {
                Log("[AUDIO] Resynch Request to -> " + syncTimestamp / 10000);

                // Initialize Audio Player / Clear Audio Frames
                AudioResetClbk.BeginInvoke(null, null);
                aFrames = new Queue<MediaFrame>();

                // Seek Audio Decoder (syncTimestamp - 2 Audio Frames) to ensure audioFirstTimeStamp < syncTimestamp (-50ms to make sure will get a previous Frame within QueueSize)
                decoder.SeekAccurate((int)((syncTimestamp / 10000) - 50), FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO);
                decoder.RunAudio();

                // Fill Audio Frames
                int escapeInfinity = 0;
                while (aFrames.Count < AUDIO_MIX_QUEUE_SIZE && isPlaying && decoder.isRunning)
                {
                    escapeInfinity++;
                    Thread.Sleep(10);
                    if (escapeInfinity > 50) { Log("[ERROR EI2] Audio Frames Queue will not be filled by decoder"); return false; }
                }

                // Validate We have AudioTimestamp < syncTimestamp
                MediaFrame aFrame = aFrames.Peek();
                MediaFrame aFramePrev = new MediaFrame();

                if (aFrame.timestamp > syncTimestamp)
                {
                    Log("[AUDIO] Failed to force aFrame.timestamp < syncTimestamp (" + ", audioTimestamp: " + aFrame.timestamp + ", syncTimestamp: " + syncTimestamp + ")");
                    audioFlowTicks = DateTime.UtcNow.Ticks;
                    audioFlowBytes = 0;
                    SendAudioFrames();
                    return false;
                }

                // Find Closest Audio Timestamp and Cut It precise
                while (aFrame.timestamp <= syncTimestamp && isPlaying)
                {
                    if (aFrames.Count < 2) return false;
                    aFramePrev = aFrames.Dequeue();
                    aFrame = aFrames.Peek();
                }
                if (!isPlaying) return false;

                int removeBytes = (int)Math.Round((syncTimestamp - aFramePrev.timestamp) / ((1.0 / audioBytesPerSecond) * 10000 * 1000));
                if (removeBytes > aFramePrev.data.Length) removeBytes = aFramePrev.data.Length;

                // Reset Timers & Fill Audio Player
                AudioFrameClbk(aFramePrev.data, removeBytes, aFramePrev.data.Length - removeBytes);
                audioFlowTicks = DateTime.UtcNow.Ticks;
                audioFlowBytes = 0;
                SendAudioFrames();

                Log("[AUDIO] Resynch Successfully to -> " + aFramePrev.timestamp / 10000 + " ++");

                return true;
            }
        }

        // Subtitles    [Send / Sync]
        private void SendSubFrame()
        {
            lock (sFrames)
            {
                if (sFrames.Count < 1) return;

                MediaFrame sFrame = sFrames.Peek();
                if (Math.Abs((sFrame.timestamp + subsExternalDelay) - CurTime) < decoder.vStreamInfo.frameAvgTicks * 2)
                {
                    SubFrameClbk.BeginInvoke(sFrame.text, sFrame.duration, null, null);
                    sFrames.Dequeue();
                }
            }
        }
        private bool ResynchSubs(long syncTimestamp)
        {
            lock (sFrames)
            {
                Log("[SUBS] Resynch Request to -> " + syncTimestamp / 10000);

                sFrames = new Queue<MediaFrame>();
                decoder.SeekAccurate((int)((syncTimestamp / 10000) - 50), FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                decoder.RunSubs();

                // Fill Sub Frames
                int escapeInfinity = 0;
                while (sFrames.Count < SUBS_MIN_QUEUE_SIZE && isPlaying && decoder.isRunning)
                {
                    escapeInfinity++;
                    Thread.Sleep(10);
                    if (escapeInfinity > 50) { Log("[ERROR EI2] Sub Frames Queue will not be filled by decoder"); return false; }
                }
                if (!isPlaying || !decoder.isRunning) return false;

                MediaFrame sFrame       = sFrames.Peek();
                MediaFrame sFrameNext   = sFrame;

                // Find Closest Subs Timestamp
                while (isPlaying)
                {
                    if (sFrameNext.timestamp > syncTimestamp) break;
                    sFrame = sFrames.Dequeue();
                    if (sFrames.Count < 1) return false;
                    sFrameNext = sFrames.Peek();
                }
                if (!isPlaying) return false;

                Log("[SUBS] Resynch Successfully to -> " + sFrame.timestamp / 10000 + " ++");

                SendSubFrame();

                return true;
            }
        }

        // Public Exposure
        public int Open(string url)
        {
            int ret;

            if (decoder != null) decoder.Pause();
            Initialize();

            if ((ret = decoder.Open(url)) != 0) return ret;
            if (!decoder.isReady)               return  -1;

            isReady             = true;
            CurTime             = 0;
            audioExternalDelay  = 0;
            subsExternalDelay   = 0;

            hasAudio            = decoder.hasAudio; 
            hasVideo            = decoder.hasVideo; 
            hasSubs             = decoder.hasSubs;

            Width               = (hasVideo) ? decoder.vStreamInfo.width         : 0;
            Height              = (hasVideo) ? decoder.vStreamInfo.height        : 0;
            Duration            = (hasVideo) ? decoder.vStreamInfo.durationTicks : decoder.aStreamInfo.durationTicks;

            return 0;
        }
        public int OpenSubs(string url)
        {
            int ret;

            if ( !decoder.hasVideo)                     return -1;
            if ( (ret = decoder.OpenSubs(url)) != 0 )   { hasSubs = false; return ret; }

            hasSubs             = decoder.hasSubs;
            subsExternalDelay   = 0;

            if (!isStopped && decoder.hasSubs)          ResynchSubs(CurTime); // - subsExternalDelay);

            return 0;
        }
        public int Play()
        {
            int ret;
            
            if (!decoder.isReady || decoder.isRunning || isPlaying) return -1;

            if ((ret = decoder.RunVideo()) != 0) return ret;
            //decoder.RunSubs();

            status = Status.PLAYING;

            if (decoder.hasVideo)
            {
                if (decoder.hasVideo) while (vFrames.Count < VIDEO_MIX_QUEUE_SIZE && isPlaying && decoder != null && decoder.isRunning) Thread.Sleep(10);

                screamer = new Thread(() =>
                {
                    Screamer();
                    TimeEndPeriod(1);
                    status = Status.STOPPED;
                    Log("[SCREAMER] " + status);
                });
                screamer.SetApartmentState(ApartmentState.STA);
                screamer.Priority = ThreadPriority.AboveNormal;
                screamer.Start();
            }

            return 0;
        }
        public int Pause()
        {
            status = Status.PAUSED;

            if (screamer != null) screamer.Abort();
            if (decoder  != null) decoder.Pause();

            return 0;
        }
        public int Seek(int ms, bool curPos)
        {
            if (isSeeking) return -1;

            Status old = status;

            if (curPos) ms         += (int)  (CurTime / 10000);

            if ( (long)ms * 10000 < decoder.vStreamInfo.startTimeTicks )
            {
                CurTime                 = decoder.vStreamInfo.startTimeTicks;
            } else if ( (long)ms * 10000 > decoder.vStreamInfo.durationTicks )
            {
                CurTime                 = decoder.vStreamInfo.durationTicks;
            } else
            {
                CurTime                 = (long) ms * 10000;
            }
            
            status                  = Status.SEEKING;

            if (decoder  != null)    decoder.Pause();
            if (screamer != null)   screamer.Abort();

            lock (aFrames)  aFrames = new Queue<MediaFrame>();
            lock (sFrames)  vFrames = new Queue<MediaFrame>();
            lock (sFrames)  sFrames = new Queue<MediaFrame>();

            if (hasVideo)   decoder.SeekAccurate(ms, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO);

            if (old == Status.PLAYING)      Play();

            return 0;
        }
        public int Stop()
        {
            if (decoder != null) decoder.Stop();

            Initialize();
            CurTime = 0;

            return 0;
        }
        
        // Misc
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine(msg); }
        
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        private static extern uint TimeEndPeriod(uint uMilliseconds);
    }
}
