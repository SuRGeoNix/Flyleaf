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
        bool forceAudioSync     = false;

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

                if (CurTime + audioExternalDelay < decoder.aStreamInfo.startTimeTicks || CurTime + audioExternalDelay > decoder.aStreamInfo.durationTicks)
                    return;
                else
                    forceAudioSync = true;
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
                    if (escapeInfinity > 200 && mType != FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE || escapeInfinity > 200)
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
        private void Screamer()
        {
            try { 
                if (vFrames.Count < 1) return;

                Log("[SCREAMER] " + status);
                TimeBeginPeriod(1);

                MediaFrame vFrame;

                // Video 
                lock(vFrames) vFrame = vFrames.Dequeue();
                long delayTicks      = vFrame.timestamp;

                // Subs
                if (hasSubs) ResynchSubs(vFrame.timestamp - subsExternalDelay, true);

                // Audio
                long audioDelayTicks    = delayTicks;
                bool audioSyncPerformed = false;
                audioFlowTicks          = DateTime.UtcNow.Ticks;
                audioLastSyncTicks      = 0;

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

                // Video Frames [Callback]
                CurTime = vFrame.timestamp;
                VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, 0, null, null);

                while ( isPlaying )
                {
                    if (vFrames.Count < 1 && (decoder == null || !decoder.isRunning)) break;
                    if (vFrames.Count < 1) { Log("[WAITING DECODER ...] " + vFrames.Count + " / " + VIDEO_MIX_QUEUE_SIZE); Thread.Sleep((int)(decoder.vStreamInfo.frameAvgTicks/10000) / 4); continue; }

                    nowTicks                = DateTime.UtcNow.Ticks;
                    lock (vFrames) vFrame   = vFrames.Peek();
                    curTicks                = (nowTicks - startTicks) + delayTicks;
                    distanceTicks           = vFrame.timestamp - curTicks;

                    // ******************* [WAITING] *******************
                    if (distanceTicks > onTimeTicks && distanceTicks < offTimeTicks)
                    {
                        sleepMs = (int)(distanceTicks / 10000) - 2;
                        if (sleepMs > 1) Thread.Sleep(sleepMs);
                    }

                    // ******************* [ON TIME] *******************
                    else if (Math.Abs(distanceTicks) < onTimeTicks)
                    {
                        // Video Frames         [Callback]
                        lock (vFrames) vFrame = vFrames.Dequeue();
                        CurTime = vFrame.timestamp;
                        VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, distanceTicks, null, null);

                        // Subtitiles Frames    [Callback]
                        if (decoder.hasSubs) SendSubFrame();

                        // Audio Frames         [Internal Resync | External Resync | Callback]
                        if (decoder.hasAudio)
                        {
                            audioSyncPerformed = false; // Only in one frame distance its allowed to correct the distance

                            // Audio Sync (Currently will force Re-sync if is out from vFrames 100 ms)
                            if (Math.Abs((audioDelayTicks - delayTicks) / 10000) > 100)
                            {
                                if (curTicks + audioExternalDelay < decoder.aStreamInfo.startTimeTicks || curTicks + audioExternalDelay > decoder.aStreamInfo.durationTicks)
                                {
                                    audioDelayTicks = -10000000;    // Force Resync Later (Audio Paused)
                                }
                                else if ( ResynchAudio((vFrame.timestamp + decoder.vStreamInfo.frameAvgTicks) - audioExternalDelay) )
                                {
                                    audioSyncPerformed = true;
                                    audioDelayTicks = delayTicks;
                                }
                            }

                            // External Audio Sync
                            else if ( forceAudioSync && ResynchAudio((vFrame.timestamp + decoder.vStreamInfo.frameAvgTicks) - audioExternalDelay) )
                            {
                                forceAudioSync = false;
                                audioSyncPerformed = true;
                                audioDelayTicks = delayTicks;
                            }

                            // Audio Frames     [Callback]
                            else if ( aFrames.Count > 0) 
                            {
                                SendAudioFrames();
                            }
                        }
                    }

                    // ******************* [OFF TIME - FOREWARDS] *******************
                    else if (distanceTicks > offTimeTicks)
                    {
                        Log("[OffTime > 0]\t\tCurTime: " + curTicks / 10000 + ", Distance: " + distanceTicks / 10000 + ", Frame-> " + vFrame.pts + ", FrameTime: " + vFrame.timestamp / 10000);

                        delayTicks += distanceTicks;
                        lock (vFrames) vFrame = vFrames.Dequeue();
                        VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, 0, null, null);
                    }

                    // ******************* [OFF TIME - BACKWARDS] *******************
                    else if (distanceTicks < onTimeTicks)
                    {
                        // Expected Delay From Audio Resync [Scream the Video Frame that has the same timestamp with the new Synced Audio Frame]
                        if ( audioSyncPerformed )
                        {
                            Log("[OffTime < 0]\t\tCurTime: " + curTicks / 10000 + ", Distance: " + distanceTicks / 10000 + ", Frame-> " + vFrame.pts + ", FrameTime: " + vFrame.timestamp / 10000 + " Resynced Audio");

                            audioSyncPerformed = false;
                            delayTicks  += distanceTicks;
                            audioDelayTicks = delayTicks;

                            lock (vFrames) vFrame = vFrames.Dequeue();
                            CurTime = vFrame.timestamp;
                            VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, distanceTicks, null, null);
                        }

                        // Off Time | Drop Frame
                        else
                        {
                            Log("[OffTime < 0]\t\tCurTime: " + curTicks / 10000 + ", Distance: " + distanceTicks / 10000 + ", Frame-> " + vFrame.pts + ", FrameTime: " + vFrame.timestamp / 10000);

                            delayTicks += distanceTicks;
                            lock (vFrames) vFrame = vFrames.Dequeue();
                        }
                    }

                } // While

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
                while (aFrames.Count > 0 && isPlaying && decoder.isRunning)
                {
                    MediaFrame aFrame = aFrames.Dequeue();
                    if ( aFrame.data == null ) continue; // Queue possible not thread-safe
                    AudioFrameClbk(aFrame.data, 0, aFrame.data.Length);
                    audioFlowBytes += aFrame.data.Length;

                    // Check on every frame that we send to ensure the buffer will not be full
                    curRate = audioFlowBytes / ((DateTime.UtcNow.Ticks - audioFlowTicks) / 10000000.0);
                    if (curRate > audioBytesPerSecond) return;
                }
            }
        }
        private bool ResynchAudio(long syncTimestamp)
        {
            // Let Video Frames to Ensure New Position (Give It A Half Second for Retrying)
            if (DateTime.UtcNow.Ticks - audioLastSyncTicks < 5000000) return false;
            audioLastSyncTicks = DateTime.UtcNow.Ticks;

            lock (aFrames)
            {
                Log("[AUDIO] Resynch Request to -> " + syncTimestamp / 10000);

                // Clear Audio Player's Buffer & Wait For It
                AudioResetClbk.Invoke();
                aFrames = new Queue<MediaFrame>();

                // Seek Audio Decoder (syncTimestamp - 2 Audio Frames) to ensure audioFirstTimeStamp < syncTimestamp (-50ms to make sure will get a previous Frame within QueueSize)
                decoder.SeekAccurate((int) ((syncTimestamp / 10000) - 50), FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO);
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

                // Find Closest Audio Timestamp | Cut It in Pieces (Possible not required but whatever)
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
                
                // Fill the empty buffer with Size of Min Queue + Whatever the curRate allows
                int count = 0;
                while (aFrames.Count > 0 && isPlaying && decoder.isRunning && count < AUDIO_MIX_QUEUE_SIZE - 1)
                {
                    aFrame = aFrames.Dequeue();
                    AudioFrameClbk(aFrame.data, 0, aFrame.data.Length);
                    count++;
                }
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
                //if (Math.Abs((sFrame.timestamp + (sFrame.duration * 10000) + subsExternalDelay) - CurTime) < decoder.vStreamInfo.frameAvgTicks * 2)
                {
                    SubFrameClbk.BeginInvoke(sFrame.text, sFrame.duration, null, null);
                    sFrames.Dequeue();
                }
            }
        }
        private bool ResynchSubs(long syncTimestamp, bool force = false)
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
                    if ((sFrameNext.timestamp + (sFrameNext.duration * 10000) > syncTimestamp && force) || (sFrameNext.timestamp > syncTimestamp && !force)) break;
                    sFrame = sFrames.Dequeue();
                    if (sFrames.Count < 1) return false;
                    sFrameNext = sFrames.Peek();
                }
                if (!isPlaying) return false;

                Log("[SUBS] Resynch Successfully to -> " + sFrame.timestamp / 10000 + " ++");

                if ( force && sFrame.timestamp < syncTimestamp && sFrame.timestamp + (sFrame.duration * 10000) > syncTimestamp)
                {
                    SubFrameClbk.BeginInvoke(sFrame.text, (int)((sFrame.timestamp + (sFrame.duration * 10000) - syncTimestamp)/10000), null, null);
                    sFrames.Dequeue();
                }
                else
                {
                    SendSubFrame();
                }

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

            if (!isStopped && decoder.hasSubs)          ResynchSubs(CurTime, true); // - subsExternalDelay);

            return 0;
        }
        public int Play()
        {
            int ret;
            
            if (!decoder.isReady || decoder.isRunning || isPlaying) return -1;

            if ((ret = decoder.RunVideo()) != 0) return ret;

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
