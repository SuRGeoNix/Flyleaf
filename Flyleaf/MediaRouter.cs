using System;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;

using static SuRGeoNix.Flyleaf.MediaDecoder;
using static SuRGeoNix.Flyleaf.OSDMessage;

namespace SuRGeoNix.Flyleaf
{
    public class MediaRouter
    {
        #region Declaration

        public AudioPlayer      audioPlayer;
        public MediaDecoder     decoder;
        public MediaRenderer    renderer;
        public MediaStreamer    streamer;

        Thread                  screamer, aScreamer, vScreamer, sScreamer;
        Thread                  openOrBuffer;
        Thread                  seekRequest         = new Thread(() => { });

        Stopwatch               videoClock          = new Stopwatch();
        long                    videoStartTicks     =-1;

        long                    audioExternalDelay  = 0;
        long                    subsExternalDelay   = 0;

        // Seeking
        ConcurrentStack<int>    seekStack;
        int                     seekLastValue = -1;
        readonly object         lockSeek            = new object();
        readonly object         lockOpening         = new object();
        
        // Queues
        ConcurrentQueue<MediaFrame>         aFrames;
        ConcurrentQueue<MediaFrame>         vFrames;
        ConcurrentQueue<MediaFrame>         sFrames;

        int AUDIO_MIX_QUEUE_SIZE = 50;  int AUDIO_MAX_QUEUE_SIZE =  60;
        int VIDEO_MIX_QUEUE_SIZE =  4;  int VIDEO_MAX_QUEUE_SIZE =   6;
        int  SUBS_MIN_QUEUE_SIZE =  5;  int  SUBS_MAX_QUEUE_SIZE =  10;

        // Idle [Activity / Visibility Mode]
        public enum VisibilityMode
        {
            Always,
            Never,
            OnIdle,
            OnActive,
            OnFullActive,
            Other
        }
        public static bool ShouldVisible(ActivityMode act, VisibilityMode vis)
        {
            if (vis == VisibilityMode.Never ) return false;
            if (vis == VisibilityMode.Always) return true;

            if      ( act == ActivityMode.Idle          &&  vis != VisibilityMode.OnIdle )  return false;
            else if ( act == ActivityMode.Active        &&  vis != VisibilityMode.OnActive) return false;
            else if ( act == ActivityMode.FullActive    &&  vis == VisibilityMode.OnIdle )  return false;

            return true;
        }
        public enum ActivityMode
        {
            Idle,
            Active,
            FullActive
        }

        // Status
        public enum Status
        {
            OPENING,
            FAILED,
            OPENED,
            BUFFERING,
            PLAYING,
            PAUSED,
            SEEKING,
            STOPPING,
            STOPPED
        }
        Status status, aStatus, vStatus, sStatus;
        Status beforeSeeking = Status.STOPPED;

        // Callbacks (TODO events?)
        public Action<bool>                     OpenTorrentSuccessClbk;
        public Action<bool, string>             OpenFinishedClbk;
        public Action<bool>                     BufferSuccessClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;
        public Action<int, int, int, int>       StatsClbk;

        public event StatusChangedHandler StatusChanged;
        public delegate void StatusChangedHandler(object source, StatusChangedArgs e);
        public class StatusChangedArgs : EventArgs
        {
            public Status status;
            public StatusChangedArgs(Status status) { this.status = status; }
        }
        #endregion

        #region Properties

        public enum ViewPorts
        {
            KEEP,
            FILL,
            CUSTOM
        }
        public ViewPorts ViewPort   { get; set; } = ViewPorts.KEEP;
        public float DecoderRatio   { get; set; } = 2;
        public float CustomRatio    { get; set; } = 2;
        public bool isReady         { get; private set; }
        public bool isPlaying       { get { return (status == Status.PLAYING);  } }
        public bool isPaused        { get { return (status == Status.PAUSED);   } }
        public bool isSeeking       { get { return (status == Status.SEEKING);  } }
        public bool isStopped       { get { return (status == Status.STOPPED);  } }
        public bool isFailed        { get { return (status == Status.FAILED);   } }
        public bool isOpened        { get { return (status == Status.OPENED);   } }
        public bool isTorrent       { get; private set; }
        public bool hasAudio        { get { return decoder.hasAudio;            } }
        public bool hasVideo        { get { return decoder.hasVideo;            } }
        public bool hasSubs         { get { return decoder.hasSubs;             } }
        public bool doAudio         { get { return decoder.doAudio;             } set { decoder.doAudio = value; if (!isPlaying) return; if (!value) { aStatus = Status.STOPPED; Utils.EnsureThreadDone(aScreamer); decoder.PauseAudio();} else RestartAudio();} }
        public bool doSubs          { get { return decoder.doSubs;              } set { decoder.doSubs  = value; if (!isPlaying) return; if (!value) { sStatus = Status.STOPPED; Utils.EnsureThreadDone(sScreamer); decoder.PauseSubs(); } else RestartSubs(); } }
        public int  Width           { get { return decoder.vStreamInfo.width;   } }
        public int  Height          { get { return decoder.vStreamInfo.height;  } }
        public long Duration        { get { return hasVideo ? decoder.vStreamInfo.durationTicks : decoder.aStreamInfo.durationTicks; } }
        public long CurTime         { get; private set; }

        public long SeekTime = -1;
        public ActivityMode Activity{ get; set; } = ActivityMode.FullActive;
        public int  verbosity       { get; set; }
        public bool HighQuality     { get { return decoder.HighQuality;         } set { decoder.HighQuality = value; } }
        public bool HWAccel         { get { return decoder.HWAccel;             } set { decoder.HWAccel = value; renderer?.NewMessage(OSDMessage.Type.HardwareAcceleration); } }
        public bool iSHWAccelSuccess{ get { return decoder.hwAccelSuccess; } }
        public int  Volume          { get { return audioPlayer == null ? 0 : audioPlayer.Volume; } set { audioPlayer.SetVolume(value); renderer?.NewMessage(OSDMessage.Type.Volume); } }
        public bool Mute            { get { return !audioPlayer.isPlaying;      } set { if (value) audioPlayer.Pause(); else audioPlayer.Play(); renderer?.NewMessage(OSDMessage.Type.Mute, Mute ? "Muted" : "Unmuted"); } }
        public bool Mute2           { get { return audioPlayer.Mute;            } set { audioPlayer.Mute = value; renderer?.NewMessage(OSDMessage.Type.Mute, Mute2 ? "Muted" : "Unmuted"); } }
        
        public long AudioExternalDelay
        {
            get { return audioExternalDelay; }

            set
            {
                if (!decoder.isReady || !isReady || !decoder.hasAudio) return;

                audioExternalDelay = value;
                streamer.AudioExternalDelay = (int) (value / 10000);
                audioPlayer.ResetClbk();
                renderer.NewMessage(OSDMessage.Type.AudioDelay);
            }
        }
        public long SubsExternalDelay
        {
            get { return subsExternalDelay; }

            set
            {
                if (!decoder.isReady || !isReady || !decoder.hasSubs) return;

                subsExternalDelay = value;
                streamer.SubsExternalDelay = (int)(value / 10000);
                renderer.NewMessage(OSDMessage.Type.SubsDelay);
            }
        }
        public int  SubsPosition     { get { return renderer.SubsPosition;       }   set { renderer.SubsPosition = value; renderer?.NewMessage(OSDMessage.Type.SubsHeight);} }
        public float SubsFontSize   { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].FontSize; } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].FontSize = value; } }
        #endregion

        #region Initialization
        public MediaRouter(int verbosity = 0)
        {
            this.verbosity = verbosity;
            renderer    = new MediaRenderer(this);
            
            decoder     = new MediaDecoder();

            aFrames     = new ConcurrentQueue<MediaFrame>();
            vFrames     = new ConcurrentQueue<MediaFrame>();
            sFrames     = new ConcurrentQueue<MediaFrame>();
            seekStack   = new ConcurrentStack<int>();
        }
        public void InitHandle(IntPtr handle, bool designMode = false)
        {
            renderer.InitHandle(handle);

            if (!designMode)
            {
                audioPlayer = new AudioPlayer();
                decoder.Init(GetFrame, verbosity);
                decoder.d3d11Device = renderer.device;
                streamer    = new MediaStreamer(this, verbosity);
                SuRGeoNix.Utils.TimeBeginPeriod(1);
            }

            Initialize();
        }
        public MediaRouter(IntPtr handle, int verbosity = 0)
        {
            SuRGeoNix.Utils.TimeBeginPeriod(1);

            this.verbosity = verbosity;
            renderer    = new MediaRenderer(this, handle);
            decoder     = new MediaDecoder(GetFrame, verbosity);
            decoder.d3d11Device = renderer.device;
            streamer    = new MediaStreamer(this, verbosity);

            audioPlayer = new AudioPlayer();

            aFrames     = new ConcurrentQueue<MediaFrame>();
            vFrames     = new ConcurrentQueue<MediaFrame>();
            sFrames     = new ConcurrentQueue<MediaFrame>();
            seekStack   = new ConcurrentStack<int>();

            Initialize();
        }
        private void Initialize()
        {
            PauseThreads();
            if (isTorrent && streamer != null) streamer.Pause();
            if (openOrBuffer != null) openOrBuffer.Abort(); 

            isReady         = false;
            beforeSeeking   = Status.STOPPED;

            ClearMediaFrames();
            renderer.ClearMessages();
            seekStack.Clear();

            if (streamer != null)
            {
                streamer.BufferingDoneClbk      = BufferingDone;
                streamer.BufferingAudioDoneClbk = BufferingAudioDone;
                streamer.BufferingSubsDoneClbk  = BufferingSubsDone;

                streamer.MediaFilesClbk         = MediaFilesClbk;
                streamer.StatsClbk              = StatsClbk;

                streamer.Stop();
            }
        }
        private void InitializeEnv()
        {
            CurTime             = 0;
            audioExternalDelay  = AudioPlayer.NAUDIO_DELAY_MS * -10000; // for some reason even if we set DesiredLatency = 200 it is not exactly what we expect (+70)
            streamer.AudioExternalDelay = AudioPlayer.NAUDIO_DELAY_MS * -1;
            subsExternalDelay   = 0;

            //Duration            = (hasVideo) ? decoder.vStreamInfo.durationTicks : decoder.aStreamInfo.durationTicks;

            if (hasAudio)
            {
                audioPlayer._RATE   = decoder._RATE;
                audioPlayer.Initialize();
            }

            DecoderRatio = (float)decoder.vStreamInfo.width / (float)decoder.vStreamInfo.height;
            //if (ViewPort == ViewPorts.KEEP) AspectRatio = (float)decoder.vStreamInfo.width / (float)decoder.vStreamInfo.height;
            renderer.FrameResized(decoder.vStreamInfo.width, decoder.vStreamInfo.height);

            isReady = true;
        }

        public void Render(string imageFile = "")
        {
            renderer.PresentFrame(null);
        }
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
            if (curQueue.Count > curMaxSize)
            {
                int escapeInfinity = 0;
                int sleepMs = (int)((decoder.vStreamInfo.frameAvgTicks / 10000) * (curMaxSize * (1.0 / 4.0)));
                if (sleepMs > 5000) sleepMs = 5000;
                //if (mType == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE) sleepMs *= 10;

                while (mTypeIsRunning && curQueue.Count > (curMaxSize * (3.0 / 4.0)))
                {
                    //Thread.Sleep(sleepMs);
                    Thread.Sleep(50);
                    escapeInfinity++;
                    if (escapeInfinity > 100000)
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
            } catch (ThreadAbortException e) { throw e;
            } catch (Exception e) { Log("Error[" + (-1).ToString("D4") + "], Func: GetFrame(), Msg: " + e.StackTrace); }
        }
        #endregion

        #region Screaming
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

            while (isPlaying && vStatus == Status.PLAYING)
            {
                if ( vFrames.Count < 1 )
                { 
                    if (decoder.isVideoFinish) { status = Status.STOPPED; Log($"[VIDEO SCREAMER] Finished"); return; }

                    Log($"[VIDEO SCREAMER] No Frames, Restarting ...");

                    Thread.Sleep(150);
                    if ( isTorrent && vFrames.Count > 0 || vFrames.Count > VIDEO_MIX_QUEUE_SIZE) continue;

                    Thread restart = new Thread(() => {
                        if (!isPlaying) return;
                        Seek((int) ((CurTime + decoder.vStreamInfo.frameAvgTicks *2)/10000));
                    });
                    restart.SetApartmentState(ApartmentState.STA);
                    restart.Start();
                    return;
                }

                if ( offDistanceCounter > VIDEO_MAX_QUEUE_SIZE )
                {
                    Log($"[VIDEO SCREAMER] Too Many Drops, Restarting ...");

                    Thread.Sleep(150);

                    Thread restart = new Thread(() => {
                        Seek((int) ((CurTime + decoder.vStreamInfo.frameAvgTicks *2)/10000));
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
                //SeekSeconds = vFrame.timestamp;
                //Log($"[VIDEO SCREAMER] Frame Scream [CurTS: {vFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {(vFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {vFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                //VideoFrameClbk.BeginInvoke(vFrame.data, vFrame.timestamp, vFrame.texture, null, null);
                
                renderer.PresentFrame(vFrame);
                //SharpDX.Utilities.Dispose(ref vFrame.texture);
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

            while (isPlaying && aStatus == Status.PLAYING)
            {
                if ( aFrames.Count < 1 )
                { 
                    if (decoder.isAudioFinish) { Log($"[AUDIO SCREAMER] Finished"); return; }

                    if (!decoder.isAudioRunning)
                    {
                        Log($"[AUDIO SCREAMER] No Samples, Restarting ...");

                        Thread.Sleep(80);

                        Thread restart = new Thread(() => {
                            if (!isPlaying) return;

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
                    if ( offDistanceCounter > AUDIO_MIX_QUEUE_SIZE)
                    {
                        Log($"[AUDIO SCREAMER] Too Many Drops, Restarting ...");

                        Thread.Sleep(80);

                        Thread restart = new Thread(() => {
                            if (!isPlaying) return;

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
                    Log($"[AUDIO SCREAMER] Sample Drop   [CurTS: {aFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {((aFrame.timestamp + audioExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {aFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                    offDistanceCounter ++;
                    continue;
                }

                if ( distanceMs > 3 ) Thread.Sleep(distanceMs - 2);

                // Audio Frames         [Callback]
                //Log($"[AUDIO SCREAMER] Sample Scream [CurTS: {aFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {((aFrame.timestamp + audioExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {aFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                audioPlayer.FrameClbk(aFrame.data, 0, aFrame.data.Length);
            }

            Log($"[AUDIO SCREAMER] Finished  -> {videoStartTicks/10000}");
        }
        private void SubsScreamer()
        {
            MediaFrame sFrame;
            
            long distanceTicks;
            int  distanceMs;

            //long offDistanceMsBackwards = -800;
            int  offDistanceCounter     = 0;

            Log($"[SUBS  SCREAMER] Started  -> {videoStartTicks/10000}");

            while (isPlaying && sStatus == Status.PLAYING)
            {
                if ( sFrames.Count < 1 )
                { 
                    if (decoder.isSubsFinish)
                    {
                        Thread.Sleep(1000);
                        Log($"[SUBS  SCREAMER] Finished");
                        return;
                    }

                    Log($"[SUBS  SCREAMER] No Subs, Restarting");

                    Thread.Sleep(80);

                    Thread restart = new Thread(() => {
                        if (!isPlaying) return;

                        if (isTorrent && !decoder.isSubsExternal)
                            streamer.SeekSubs((int) ((CurTime - subsExternalDelay)/10000) - 100);
                        else
                            RestartSubs();
                    });
                    restart.SetApartmentState(ApartmentState.STA);
                    restart.Start();

                    return;
                }
                else
                {
                    if ( offDistanceCounter > 5)
                    {
                        Log($"[SUBS  SCREAMER] Too Many Drops, Restarting ...");

                        Thread.Sleep(80);

                        Thread restart = new Thread(() => {
                            if (!isPlaying) return;

                            if (isTorrent && !decoder.isSubsExternal)
                                streamer.SeekSubs((int) ((CurTime - subsExternalDelay)/10000) - 100);
                            else
                                RestartSubs();
                        });
                        restart.SetApartmentState(ApartmentState.STA);
                        restart.Start();

                        return;
                    }
                }

                lock (sFrames) sFrames.TryPeek(out sFrame);

                distanceTicks   = (sFrame.timestamp + subsExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks);
                distanceMs      = (int) (distanceTicks/10000);

                if ( distanceMs < sFrame.duration * -1 )
                {
                    Log($"[SUBS  SCREAMER] Sub Drop   [CurTS: {sFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {((sFrame.timestamp + subsExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {sFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                    offDistanceCounter ++;
                    lock (sFrames) sFrames.TryDequeue(out sFrame);
                    continue;
                }

                if ( distanceMs > 80 )
                {
                    Thread.Sleep(80);
                    continue;
                }
                
                if ( distanceMs > 3 ) Thread.Sleep(distanceMs - 2);

                // Sub Frames         [Callback]
                //Log($"[SUBS  SCREAMER] Sub Scream [CurTS: {sFrame.timestamp/10000}] [Clock: {(videoStartTicks + videoClock.ElapsedTicks)/10000} | {CurTime/10000}] [Distance: {((sFrame.timestamp + subsExternalDelay) - (videoStartTicks + videoClock.ElapsedTicks))/10000}] [DiffTicks: {sFrame.timestamp - (videoStartTicks + videoClock.ElapsedTicks)}]");
                //SubFrameClbk.BeginInvoke(sFrame.text, sFrame.timestamp + subsExternalDelay, sFrame.duration, null, null);

                string subText = Subtitles.SSAtoSubStyles(sFrame.text, out List<SubStyle> subStyles);
                renderer.NewMessage(OSDMessage.Type.Subtitles, subText, subStyles, sFrame.duration);

                lock (sFrames) sFrames.TryDequeue(out sFrame);
            }

            Log($"[SUBS  SCREAMER] Finished  -> {videoStartTicks/10000}");
        }
        private void RestartSubs()
        {
            Utils.EnsureThreadDone(sScreamer);

            if (decoder.hasSubs && doSubs)
            {
                Log("[SUBS  SCREAMER] Restarting ...");
                
                sStatus = Status.PLAYING;
                sScreamer = new Thread(() => {
                    decoder.PauseSubs();
                    if (!isPlaying || videoStartTicks == -1) {sStatus = Status.STOPPED; return; }

                    lock (sFrames) sFrames = new ConcurrentQueue<MediaFrame>();

                    decoder.SeekAccurate((int) ((CurTime - subsExternalDelay)/10000) - 100, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                    decoder.RunSubs();

                    while (isPlaying && sStatus == Status.PLAYING && decoder.hasSubs && sFrames.Count < SUBS_MIN_QUEUE_SIZE && !decoder.isSubsFinish ) 
                        Thread.Sleep(20);

                    try
                    {
                        SubsScreamer();
                    } catch (Exception) { }
                    sStatus = Status.STOPPED;
                });
                sScreamer.SetApartmentState(ApartmentState.STA);
                sScreamer.Start();
            }
        }
        private void RestartAudio()
        {
            Utils.EnsureThreadDone(aScreamer);

            if (decoder.hasAudio && doAudio)
            {
                Log("[AUDIO SCREAMER] Restarting ...");

                aStatus = Status.PLAYING;
                aScreamer = new Thread(() => {
                    decoder.PauseAudio();
                    if (!isPlaying || videoStartTicks == -1) return;

                    Thread.Sleep(20);

                    lock (aFrames) aFrames = new ConcurrentQueue<MediaFrame>();
                    audioPlayer.ResetClbk();

                    decoder.SeekAccurate((int) ((CurTime - audioExternalDelay)/10000) - 50, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO);
                    decoder.RunAudio();

                    while (isPlaying && (decoder.hasAudio && aFrames.Count < AUDIO_MIX_QUEUE_SIZE) ) 
                        Thread.Sleep(20);

                    try
                    {
                        AudioScreamer();
                    } catch (Exception) { }
                    aStatus = Status.STOPPED;
                });
                aScreamer.SetApartmentState(ApartmentState.STA);
                aScreamer.Start();
            }
        }
        #endregion

        #region Main Actions
        public void Open(string url)
        {
            lock (lockOpening)
            {
                if (url == null || url.Trim() == "") { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed"); status = Status.FAILED; OpenTorrentSuccessClbk?.BeginInvoke(false, null, null); return; }

                Log($"Opening {url}");
                renderer.NewMessage(OSDMessage.Type.Open, $"Opening {url}");

                Initialize();
                CurTime = 0;
                status = Status.OPENING;

                int ret;
                string ext      = url.Substring(url.LastIndexOf(".") + 1);
                string scheme   = url.Substring(0, url.IndexOf(":"));

                if (ext.ToLower() == "torrent" || scheme.ToLower() == "magnet")
                {
                    if (MediaFilesClbk == null) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed: Torrents are disabled"); status = Status.FAILED; OpenTorrentSuccessClbk?.BeginInvoke(false, null, null); return; }

                    isTorrent = true;
                    openOrBuffer = new Thread(() =>
                    {
                        try
                        {
                            if (scheme.ToLower() == "magnet")
                                ret = streamer.Open(url, MediaStreamer.StreamType.TORRENT);
                            else
                                ret = streamer.Open(url, MediaStreamer.StreamType.TORRENT, false);

                            if (ret != 0) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed"); status = Status.FAILED; OpenTorrentSuccessClbk?.BeginInvoke(false, null, null); return; }

                            status = Status.OPENED;
                            renderer.NewMessage(OSDMessage.Type.Open, $"Opened");
                            renderer.NewMessage(OSDMessage.Type.HardwareAcceleration);
                            OpenTorrentSuccessClbk?.BeginInvoke(true, null, null);

                        } catch (ThreadAbortException) { return; }
                    });
                    openOrBuffer.SetApartmentState(ApartmentState.STA);
                    openOrBuffer.Start();
                }
                else
                {
                    isTorrent = false;
                    openOrBuffer = new Thread(() =>
                    {
                        decoder.Open(url);
                        if (!decoder.isReady) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed"); status = Status.FAILED; OpenFinishedClbk?.BeginInvoke(false, url, null, null); return; }
                        renderer.NewMessage(OSDMessage.Type.Open, $"Opened");
                        renderer.NewMessage(OSDMessage.Type.HardwareAcceleration);
                        InitializeEnv();
                        OpenFinishedClbk?.BeginInvoke(true, url, null, null);
                    });
                    openOrBuffer.SetApartmentState(ApartmentState.STA);
                    openOrBuffer.Start();
                }
            }
        }
        public int OpenSubs(string url)
        {
            int ret = 0;

            if ( !decoder.hasVideo) return -1;

            //while (sStatus == Status.STOPPING) Thread.Sleep(10);

            sStatus = Status.STOPPING;
            Utils.EnsureThreadDone(sScreamer);

            if ( (ret = decoder.OpenSubs(url)) != 0 ) { renderer.NewMessage(OSDMessage.Type.Failed, $"Subtitles Failed"); return ret; }

            subsExternalDelay = 0;
            if (streamer != null && decoder.hasSubs) streamer.IsSubsExternal = true;

            RestartSubs();

            return ret;
        }
        public void Play()
        {
            audioPlayer.Play();

            if (!decoder.isReady || decoder.isRunning || isPlaying) { StatusChanged?.Invoke(this, new StatusChangedArgs(vStatus)); return; }

            renderer.ClearMessages(OSDMessage.Type.Paused);
            if (beforeSeeking != Status.PLAYING) renderer.NewMessage(OSDMessage.Type.Play, "Play");

            beforeSeeking = Status.PLAYING;

            if ( isTorrent )
            {
                renderer.NewMessage(OSDMessage.Type.Buffering, $"Buffering ...");
                status = Status.BUFFERING;
                streamer.Seek((int)(CurTime/10000));
                return;
            }

            Play2();
        }
        private void Play2()
        {
            PauseThreads();
            status = Status.PLAYING;
            
            lock (aFrames) aFrames = new ConcurrentQueue<MediaFrame>();
            lock (sFrames) sFrames = new ConcurrentQueue<MediaFrame>();

            screamer = new Thread(() =>
            {
                // Reset AVS Decoders to CurTime

                // Video | Seek | Set First Video Timestamp | Run Decoder
                videoStartTicks = -1;
                if (!isPlaying) return;

                if (vFrames.Count < 1)
                    lock(decoder) decoder.SeekAccurate((int) (CurTime/10000), FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO);
                if (vFrames.Count < 1) return;
                
                MediaFrame vFrame;
                lock (vFrames) vFrames.TryPeek(out vFrame);
                CurTime         = vFrame.timestamp;
                videoStartTicks = vFrame.timestamp;

                if (!isPlaying) return;
                decoder.RunVideo();

                // Audio | Seek | Run Decoder
                if (decoder.hasAudio && doAudio)
                {
                    audioPlayer.ResetClbk();
                    decoder.SeekAccurate((int) ((CurTime - audioExternalDelay)/10000) - 50, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO);
                    decoder.RunAudio();

                    //audioPlayer.Play();
                }

                // Audio | Seek | Run Decoder
                if (decoder.hasSubs && doSubs)
                {
                    decoder.SeekAccurate((int) ((CurTime - subsExternalDelay)/10000) - 100, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                    decoder.RunSubs();
                }

                // Fill Queues at least with Min
                while (isPlaying && vFrames.Count < VIDEO_MIX_QUEUE_SIZE || (hasAudio && doAudio && aFrames.Count < AUDIO_MIX_QUEUE_SIZE && !decoder.isAudioFinish) || (hasSubs && doSubs && sFrames.Count < SUBS_MIN_QUEUE_SIZE && !decoder.isSubsFinish ) ) 
                    Thread.Sleep(20);

                vStatus = Status.PLAYING; 
                vScreamer = new Thread(() => {
                    StatusChanged?.Invoke(this, new StatusChangedArgs(vStatus));
                    try
                    {
                        VideoScreamer();
                    } catch (Exception) { }

                    //TimeEndPeriod(1);
                    vStatus = Status.STOPPED;
                    StatusChanged?.Invoke(this, new StatusChangedArgs(vStatus));
                });
                vScreamer.SetApartmentState(ApartmentState.STA);
                
                if (decoder.hasAudio && doAudio)
                {
                    aStatus = Status.PLAYING;
                    aScreamer = new Thread(() => {
                        try
                        {
                            AudioScreamer();
                        } catch (Exception) { }
                        aStatus = Status.STOPPED;
                    });
                    aScreamer.SetApartmentState(ApartmentState.STA);
                }

                if (decoder.hasSubs && doSubs)
                {
                    sStatus = Status.PLAYING;
                    sScreamer = new Thread(() =>
                    {
                        try
                        {
                            SubsScreamer();
                        } catch (Exception) { }
                        sStatus = Status.STOPPED;
                    });
                    sScreamer.SetApartmentState(ApartmentState.STA);
                }

                if ((vScreamer != null && vScreamer.IsAlive) || (aScreamer != null && aScreamer.IsAlive) || (sScreamer != null && sScreamer.IsAlive)) return;
                vScreamer.Start();
                if (hasAudio && doAudio) aScreamer.Start();
                if (hasSubs && doSubs ) sScreamer.Start();
            });
            screamer.SetApartmentState(ApartmentState.STA);
            //screamer.Priority = ThreadPriority.AboveNormal;
            screamer.Start();
        }
        public void Seek(int ms2, bool priority = false)
        {
	        if (!decoder.isReady || seekLastValue == ms2) return;
            
            Interlocked.Exchange(ref SeekTime, (long)ms2 * 10000);
	        seekStack.Push(ms2);

            if (seekRequest.IsAlive) return;

            seekRequest = new Thread(() =>
            {
                if (Monitor.TryEnter(lockSeek, 40) && Monitor.TryEnter(lockOpening, 5))
		        {
			        try
			        {
                        if (isTorrent && streamer != null) streamer.Pause();
                        PauseThreads();
                        
				        status = Status.SEEKING;

                        do
                        {
                            renderer.ClearMessages(OSDMessage.Type.Play);

                            if (!seekStack.TryPop(out int ms)) return;
                            seekStack.Clear();
                            seekLastValue = ms;

                            Render();

                            if (ms < decoder.vStreamInfo.startTimeTicks / 10000)
                                ms = (int)(decoder.vStreamInfo.startTimeTicks / 10000);
                            else if (ms > decoder.vStreamInfo.durationTicks / 10000)
                                ms = (int)(decoder.vStreamInfo.durationTicks / 10000);

                            ClearVideoFrames();

                            lock (decoder) decoder.SeekAccurate(ms, FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_VIDEO);

                            if (seekStack.Count > 5) continue;
                            if (vFrames.Count == 0) return;

                            MediaFrame vFrame;
                            lock (vFrames) vFrames.TryPeek(out vFrame);
                            CurTime = vFrame.timestamp;
                            renderer.PresentFrame(vFrame);

                        } while (!seekStack.IsEmpty && decoder.isReady);

                    } catch (Exception) { }

                    finally
			        {
                        Interlocked.Exchange(ref SeekTime, -1);
				        status = Status.STOPPED;
				        Monitor.Exit(lockSeek);
                        Monitor.Exit(lockOpening);
			        }

			        if (beforeSeeking == Status.PLAYING) Play();
                    else if ( isTorrent ) streamer.Seek((int)(CurTime/10000));
		        }
            });
	        seekRequest.SetApartmentState(ApartmentState.STA);
	        seekRequest.Start();
        }
        public void Pause()
        {
            audioPlayer.Pause();
            if (!decoder.isReady || !decoder.isRunning || !isPlaying) StatusChanged(this, new StatusChangedArgs(vStatus));

            renderer.ClearMessages(OSDMessage.Type.Play);
            renderer.NewMessage(OSDMessage.Type.Paused, "Paused");
            //AbortThreads();
            PauseThreads();
            status = Status.PAUSED;
            beforeSeeking = Status.PAUSED;
        }
        public void Stop()
        {
            audioPlayer.Pause();
            decoder.Stop();
            Initialize();
            CurTime = 0;
            audioPlayer.ResetClbk();
        }
        public void Close()
        {
            PauseThreads();
            StopMediaStreamer();
            Initialize();

            if (renderer != null) renderer.Dispose();
            if (audioPlayer != null) audioPlayer.Close();
            CurTime = 0;
        }
        #endregion

        #region Streamer
        private void BufferingDone(bool done)   { if ( done && beforeSeeking == Status.PLAYING) Play2(); }
        private void BufferingAudioDone()       { RestartAudio(); }
        private void BufferingSubsDone()        { RestartSubs(); }
        public void SetMediaFile(string fileName)
        {
            
            //if (decoder     != null)   decoder.Pause();
            //if (screamer    != null)  screamer.Abort();
            //if (aScreamer   != null) aScreamer.Abort();
            //if (vScreamer   != null) vScreamer.Abort();
            //if (sScreamer   != null) sScreamer.Abort();

            if (openOrBuffer!= null) openOrBuffer.Abort();
            if (streamer    != null && isTorrent) streamer.Pause();
            PauseThreads();

            isReady         = false;
            status          = Status.STOPPED;
            beforeSeeking   = Status.STOPPED;
            decoder.HWAccel = HWAccel;

            ClearMediaFrames();

            if (openOrBuffer != null) { openOrBuffer.Abort(); Thread.Sleep(20); }

            renderer.NewMessage(OSDMessage.Type.Open, $"Opening {fileName}");

            openOrBuffer = new Thread(() => {

                // Open Decoder Buffer
                int ret = streamer.SetMediaFile(fileName);
                if (ret != 0) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed {ret}"); status = Status.FAILED; OpenFinishedClbk?.BeginInvoke(false, fileName, null, null); return; }

                // Open Decoder
                ret = decoder.Open(null, streamer.DecoderRequests, streamer.fileSize);
                if (ret != 0) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed {ret}"); status = Status.FAILED; OpenFinishedClbk?.BeginInvoke(false, fileName, null, null); return; }

                if (!decoder.isReady) { status = Status.FAILED; OpenFinishedClbk?.BeginInvoke(false, fileName, null, null); return; }

                InitializeEnv();
                OpenFinishedClbk?.BeginInvoke(true, fileName, null, null);
            });
            openOrBuffer.SetApartmentState(ApartmentState.STA);
            openOrBuffer.Start();
        }
        public void StopMediaStreamer()         { if ( streamer != null ) { streamer.Stop(); streamer = null; } }
        #endregion

        #region Misc
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.ffff")}] {msg}"); }
        private void ClearMediaFrames()
        {
            ClearVideoFrames();
            lock (aFrames) aFrames = new ConcurrentQueue<MediaFrame>();
            lock (sFrames) sFrames = new ConcurrentQueue<MediaFrame>();

        }
        private void ClearVideoFrames()
        {
            lock (vFrames)
            {
                while (vFrames.Count > 0)
                {
                    MediaFrame m;
                    vFrames.TryDequeue(out m);
                    lock (renderer.device) renderer.device.ImmediateContext.Flush();
                    SharpDX.Utilities.Dispose(ref m.texture);
                    SharpDX.Utilities.Dispose(ref m.textureY);
                    SharpDX.Utilities.Dispose(ref m.textureU);
                    SharpDX.Utilities.Dispose(ref m.textureV);
                    SharpDX.Utilities.Dispose(ref m.textureRGB);
                }
                vFrames = new ConcurrentQueue<MediaFrame>();
            }
        }
        private void PauseThreads(bool andDecoder = true)
        {
            Log($"[Pausing All Threads] START");
            status = Status.STOPPING; aStatus = Status.STOPPING; vStatus = Status.STOPPING; sStatus = Status.STOPPING;
            Utils.EnsureThreadDone(vScreamer);
            Utils.EnsureThreadDone(aScreamer);
            Utils.EnsureThreadDone(sScreamer);
            Utils.EnsureThreadDone(screamer);
            if (andDecoder) decoder.Pause();
            if (hasAudio) audioPlayer.ResetClbk();
            status = Status.STOPPED; aStatus = Status.STOPPED; vStatus = Status.STOPPED; sStatus = Status.STOPPED;
            Log($"[Pausing All Threads] END");
        }
        private void AbortThreads(bool andDecoder = true)
        {
            Log($"[Aborting All Threads] START");
            status = Status.STOPPING; aStatus = Status.STOPPING; vStatus = Status.STOPPING; sStatus = Status.STOPPING;
            if (screamer != null) screamer.Abort();
            if (aScreamer != null) aScreamer.Abort();
            if (vScreamer != null) vScreamer.Abort();
            if (sScreamer != null) sScreamer.Abort();
            if (decoder != null) decoder.Pause();
            audioPlayer.ResetClbk();
            status = Status.STOPPED; aStatus = Status.STOPPED; vStatus = Status.STOPPED; sStatus = Status.STOPPED;
            Log($"[Aborting All Threads] END");
        }
        //internal static void EnsureThreadDone(Thread t, long maxMS = 250, int minMS = 10)
        //{
        //    if (t != null && !t.IsAlive) return;

        //    long escapeInfinity = maxMS / minMS;

        //    while (t != null && t.IsAlive && escapeInfinity > 0)
        //    {
        //        Thread.Sleep(minMS);
        //        escapeInfinity--;
        //    }

        //    if (t != null && t.IsAlive)
        //    {
        //        t.Abort();
        //        escapeInfinity = maxMS / minMS;
        //        while (t != null && t.IsAlive && escapeInfinity > 0)
        //        {
        //            Thread.Sleep(minMS);
        //            escapeInfinity--;
        //        }
        //        //if (escapeInfinity == 0) Console.WriteLine("[FAILED] Thread still alive!");
        //    }
        //}
        #endregion
    }
}