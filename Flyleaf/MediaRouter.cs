/* Media Player Synching & Control Flow - the "Mediator"
 * 
 *              [UI - Flyleaf UserControl]
 *          
 *                          /\
 *                          ||
 *                          
 * [MediaStreamer]  <-  [MediaRouter]   ->  [MediaRenderer]
 * 
 *                          ||
 *                          \/
 *                          
 *                     [MediaDecoder]
 * 
 * by John Stamatakis
 */

/* Draft Notes  [Speed / Resources / Timing / Streaming & Buffering Control]
 * 
 * Queues       | (small queues faster seeking/starting | less resources) - (large queues better buffering/streaming & hd, ensures avs frames will be found within the range)
 * Screamer     | with less (Math.Abs(vDistanceMs - sleepMs) < 4) distance can handle better high fps (x8 speed etc)
 * Sleeps       | Screamer, DecodeFrames & BufferPackets | less sleep faster buffering and playing but more resources
 * Decoder      | Threads need more testing / required for HD videos (should also check codec's support)
 * Torrent      | Check TorSwarm Options for more connection / peers etc. timeouts / limits
 * Contexts     | Single vs Multi - Single gives more speed especially for seeking / Multi gives more freedom mainly for delays possible issues with matroska demuxing (index entries built based on video frames) ** For Single you might run a 2nd decoder in the same context to grap the late frames use 2 pkts?
 * Delays       | NAudio default latency was 300ms dropped to 200ms means we push subs/video +200ms | on single format context has risk to loose avs frames in the same range (same for subs if we want delay) | on multi context should be no problem
 * Web Streaming| Requires more testing, by default get the best quality, should give options for lower | check also format context's options for http (and abort)
 * 
 * TimeBeginPeriod(1) should TimeEndPeriod(1) - also consider TimeBeginPeriod(>1) for some other cases
 */

using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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

        Thread                  screamer;
        Thread                  openOrBuffer;
        Thread                  seekRequest         = new Thread(() => { });

        Stopwatch               videoClock          = new Stopwatch();
        long                    videoStartTicks     =-1;

        long                    audioExternalDelay  = 0;
        long                    subsExternalDelay   = 0;

        // Seeking
        ConcurrentStack<int>    seekStack;
        readonly object         lockSeek            = new object();
        readonly object         lockOpening         = new object();
        
        // Queues
        internal ConcurrentQueue<MediaFrame>         aFrames;
        internal ConcurrentQueue<MediaFrame>         vFrames;
        internal ConcurrentQueue<MediaFrame>         sFrames;

        public int AUDIO_MIX_QUEUE_SIZE =  0;  public int AUDIO_MAX_QUEUE_SIZE =200;
        public int VIDEO_MIX_QUEUE_SIZE = 10;  public int VIDEO_MAX_QUEUE_SIZE = 15;
        public int  SUBS_MIN_QUEUE_SIZE =  0;  public int  SUBS_MAX_QUEUE_SIZE = 50;

        // Idle [Activity / Visibility Mode]
        public enum DownloadSubsMode
        {
            Never,
            FilesAndTorrents,
            Files,
            Torrents
        }
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
        Status status;
        Status beforeSeeking = Status.STOPPED;

        // Callbacks (TODO events?)
        public Action<bool>                     OpenTorrentSuccessClbk;
        public Action<bool, string>             OpenFinishedClbk;
        public Action<bool>                     BufferSuccessClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;
        public Action<int, int, int, int>       StatsClbk;

        public event EventHandler SubtitlesAvailable;

        public event StatusChangedHandler StatusChanged;
        public delegate void StatusChangedHandler(object source, StatusChangedArgs e);
        public class StatusChangedArgs : EventArgs
        {
            public Status status;
            public StatusChangedArgs(Status status) { this.status = status; }
        }
        #endregion

        #region Properties
        public bool isPlaying       => status == Status.PLAYING;
        public bool isPaused        => status == Status.PAUSED;
        public bool isSeeking       => status == Status.SEEKING;
        public bool isStopped       => status == Status.STOPPED;
        public bool isFailed        => status == Status.FAILED;
        public bool isOpened        => status == Status.OPENED;
        public bool isReady         { get; private set; }
        public bool isTorrent       { get; private set; }
        public long CurTime         { get; private set; }
        public long SeekTime        = -1;
        public int  verbosity       { get; set; }

        public ActivityMode                 Activity    { get; set;         } = ActivityMode.FullActive;
        public Dictionary<string, string>   PluginsList { get; private set; } = new Dictionary<string, string>();
        public List<string>                 Plugins     { get; private set; } = new List<string>();

        // Video
        public enum ViewPorts
        {
            KEEP,
            FILL,
            CUSTOM
        }
        public ViewPorts ViewPort       { get; set; } = ViewPorts.KEEP;
        public float    DecoderRatio    { get; set; } = 16f/9f;
        public float    CustomRatio     { get; set; } = 16f/9f;
        public bool     hasVideo        { get { return decoder.hasVideo;            } }
        public long     Duration        { get { return hasVideo ? decoder.vStreamInfo.durationTicks : decoder.aStreamInfo.durationTicks; } }
        public int      BufferingDuration{get; set; } = 1800; // Related with Queue sizes
        public int      Width           { get { return decoder.vStreamInfo.width;   } }
        public int      Height          { get { return decoder.vStreamInfo.height;  } }
        public bool     HighQuality     { get { return decoder.HighQuality;         } set { decoder.HighQuality = value; } }
        public bool     HWAccel         { get { return decoder.HWAccel;             } set { decoder.HWAccel = value; renderer?.NewMessage(OSDMessage.Type.HardwareAcceleration); } }
        public bool     iSHWAccelSuccess{ get { return decoder.hwAccelSuccess; } }

        // Audio
        public bool     hasAudio        { get { return decoder.hasAudio;            } }
        public bool     doAudio         { get { return decoder.doAudio;             } set { decoder.doAudio = value; } }
        public int      Volume          { get { return audioPlayer == null ? 0 : audioPlayer.Volume; } set { audioPlayer.SetVolume(value); renderer?.NewMessage(OSDMessage.Type.Volume); } }
        public bool     Mute            { get { return !audioPlayer.isPlaying;      } set { if (value) audioPlayer.Pause(); else audioPlayer.Play(); renderer?.NewMessage(OSDMessage.Type.Mute, Mute ? "Muted" : "Unmuted"); } }
        public bool     Mute2           { get { return audioPlayer.Mute;            } set { audioPlayer.Mute = value; renderer?.NewMessage(OSDMessage.Type.Mute, Mute2 ? "Muted" : "Unmuted"); } }
        public long     AudioExternalDelay
        {
            get { return audioExternalDelay; }

            set
            {
                if (!decoder.isReady || !isReady || !decoder.hasAudio) return;

                audioExternalDelay = value;

                if (decoder.audio != null) decoder.audio.ReSync();
                aFrames = new ConcurrentQueue<MediaFrame>();

                renderer.NewMessage(OSDMessage.Type.AudioDelay);
            }
        }

        // Subs
        public bool     hasSubs         { get { return decoder.hasSubs;             } }
        public bool     doSubs          { get { return decoder.doSubs;              } set { if (!value) renderer.ClearMessages(OSDMessage.Type.Subtitles); decoder.doSubs  = value; } }
        public long     SubsExternalDelay
        {
            get { return subsExternalDelay; }

            set
            {
                if (!decoder.isReady || !isReady || !decoder.hasSubs) return;

                subsExternalDelay = value;

                if (decoder.subs != null) decoder.subs.ReSync();
                sFrames = new ConcurrentQueue<MediaFrame>();

                renderer.ClearMessages(OSDMessage.Type.Subtitles);
                renderer.NewMessage(OSDMessage.Type.SubsDelay);
            }
        }
        public int      SubsPosition    { get { return renderer.SubsPosition;       }   set { renderer.SubsPosition = value; renderer?.NewMessage(OSDMessage.Type.SubsHeight); } }
        public float    SubsFontSize    { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].FontSize; } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].FontSize = value; renderer?.NewMessage(OSDMessage.Type.SubsFontSize); } }

        // Online Subs
        private int     curSubId        = -1;
        public int      CurSubId        { get { return curSubId; } set { if (curSubId == value) return; PrevSubId = curSubId; curSubId = value; Log($"Sub Id Changed From {PrevSubId} to {curSubId}"); } }
        public int      PrevSubId       { get; private set; } = -1;
        public List<Language>       Languages       { get; set; } = new List<Language>();
        public List<SubAvailable>   AvailableSubs   { get; set; } = new List<SubAvailable>();
        public DownloadSubsMode     DownloadSubs    { get; set; } = DownloadSubsMode.Torrents;
        public struct SubAvailable
        {
            public Language      lang;
            public string        path;
            public int           streamIndex;
            public OpenSubtitles sub;
            public bool          used;

            public SubAvailable(Language lang, string path)      {this.lang = lang; this.streamIndex = -1;           this.sub = null; this.path = path; used = true; }
            public SubAvailable(Language lang, int streamIndex)  {this.lang = lang; this.streamIndex = streamIndex;  this.sub = null; this.path = null; used = false; }
            public SubAvailable(Language lang, OpenSubtitles sub){this.lang = lang; this.streamIndex = -1;           this.sub = sub;  this.path = null; used = false; }
        }
        #endregion

        #region Initialization
        public MediaRouter(int verbosity = 0)
        {
            LoadPlugins();

            this.verbosity = verbosity;
            renderer    = new MediaRenderer(this);
            decoder     = new MediaDecoder(this);

            aFrames     = new ConcurrentQueue<MediaFrame>();
            vFrames     = new ConcurrentQueue<MediaFrame>();
            sFrames     = new ConcurrentQueue<MediaFrame>();
            seekStack   = new ConcurrentStack<int>();

            LoadDefaultLanguages();
        }
        public void InitHandle(IntPtr handle, bool designMode = false)
        {
            renderer.InitHandle(handle);

            if (!designMode)
            {
                audioPlayer = new AudioPlayer();
                decoder.Init(verbosity);
                streamer    = new MediaStreamer(this, verbosity);
                TimeBeginPeriod(1);
            }

            Initialize();
        }
        public MediaRouter(IntPtr handle, int verbosity = 0)
        {
            LoadPlugins();
            TimeBeginPeriod(1);

            this.verbosity = verbosity;
            renderer    = new MediaRenderer(this, handle);
            decoder     = new MediaDecoder(this, verbosity);
            streamer    = new MediaStreamer(this, verbosity);

            audioPlayer = new AudioPlayer();

            aFrames     = new ConcurrentQueue<MediaFrame>();
            vFrames     = new ConcurrentQueue<MediaFrame>();
            sFrames     = new ConcurrentQueue<MediaFrame>();
            seekStack   = new ConcurrentStack<int>();

            LoadDefaultLanguages();
            Initialize();
        }
        private void Initialize()
        {
            PauseThreads();
            if (isTorrent && streamer != null) streamer.Pause();
            if (openOrBuffer != null) openOrBuffer.Abort(); 

            isReady         = false;
            beforeSeeking   = Status.STOPPED;

            CurSubId        = -1;
            PrevSubId       = -1;

            ClearMediaFrames();
            renderer.ClearMessages();
            seekStack.Clear();

            if (Plugins.Contains("Torrent Streaming") && streamer != null)
            {
                streamer.BufferingDoneClbk      = BufferingDone;
                streamer.MediaFilesClbk         = MediaFilesClbk;
                streamer.StatsClbk              = StatsClbk;

                streamer.Dispose();
            }
        }
        private void InitializeEnv()
        {
            CurTime             = 0;
            subsExternalDelay   = 0;

            if (hasAudio)
            {
                audioPlayer._RATE   = decoder._RATE;
                audioPlayer.Initialize();
            }

            DecoderRatio = (float)decoder.vStreamInfo.width / (float)decoder.vStreamInfo.height;
            renderer.FrameResized(decoder.vStreamInfo.width, decoder.vStreamInfo.height);

            isReady = true;
        }
        private void LoadPlugins()
        {
            PluginsList.Add("Torrent Streaming", "Plugins\\TorSwarm\\TorSwarm.dll");
            PluginsList.Add("Web Streaming", "Plugins\\Youtube-dl\\youtube-dl.exe");

            foreach (KeyValuePair<string, string> plugin in PluginsList)
                if (File.Exists(plugin.Value)) Plugins.Add(plugin.Key);
        }
        private void LoadDefaultLanguages()
        {
            Languages           = new List<Language>();
            Language systemLang = Language.Get(System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            if (systemLang.LanguageName != "English") Languages.Add(systemLang);

            foreach (System.Windows.Forms.InputLanguage lang in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
                if (Language.Get(lang.Culture.TwoLetterISOLanguageName).ISO639 != systemLang.ISO639 && Language.Get(lang.Culture.TwoLetterISOLanguageName).LanguageName != "English") Languages.Add(Language.Get(lang.Culture.TwoLetterISOLanguageName));

            Languages.Add(Language.Get("English"));
        }
        #endregion

        #region Screaming
        private void MediaBuffer()
        {
            //audioPlayer.Pause();
            
            renderer.ClearMessages(OSDMessage.Type.Subtitles);

            //ClearMediaFrames();

            //decoder.ReSync();
            if (!decoder.isRunning || decoder.video.requiresResync) decoder.Run();
            audioPlayer.ResetClbk();
            audioPlayer.Play();

            bool framePresented = false;

            while (isPlaying && vFrames.Count < VIDEO_MIX_QUEUE_SIZE && !decoder.Finished)
            {
                if (!framePresented && vFrames.Count > 0)
                {
                    MediaFrame vFrame = null;
                    vFrames.TryPeek(out vFrame);
                    renderer.PresentFrame(vFrame);
                    framePresented = true;
                }
                Thread.Sleep(5);
            }
        }        
        private void Screamer()
        {
            MediaFrame vFrame = null;
            MediaFrame aFrame = null;
            MediaFrame sFrame = null;
      
            long    elapsedTicks;
            int     vDistanceMs;
            int     aDistanceMs;
            int     sDistanceMs;
            int     sleepMs;

            lock (lockSeek)
            {
                MediaBuffer();
                vFrames.TryDequeue(out vFrame); if (vFrame == null) return;
                aFrames.TryDequeue(out aFrame);
                sFrames.TryDequeue(out sFrame);
                SeekTime        = -1;
                CurTime         = vFrame.timestamp;
                videoStartTicks = aFrame != null && aFrame.timestamp < vFrame.timestamp - (AudioPlayer.NAUDIO_DELAY_MS * (long)10000) ? aFrame.timestamp : vFrame.timestamp - (AudioPlayer.NAUDIO_DELAY_MS * (long)10000);
                videoClock.Restart();
            }
            
            Log($"[SCREAMER] Started -> {Utils.TicksToTime(videoStartTicks)}");

            while (isPlaying)
            {
                if ( vFrames.Count == 0 )//|| (hasAudio && aFrames.Count < 1) )
                {
                    if (decoder.Finished) break;

                    lock (lockSeek)
                    {
                        Log("Screamer Restarting ........................");
                        MediaBuffer();
                        vFrames.TryDequeue(out vFrame); if (vFrame == null) return;
                        aFrames.TryDequeue(out aFrame);
                        sFrames.TryDequeue(out sFrame);
                        SeekTime        = -1;
                        CurTime         = vFrame.timestamp;
                        Log($"[SCREAMER] Restarted -> {Utils.TicksToTime(CurTime)}");
                        videoStartTicks = aFrame != null && aFrame.timestamp < vFrame.timestamp - (AudioPlayer.NAUDIO_DELAY_MS * (long)10000) ? aFrame.timestamp : vFrame.timestamp - (AudioPlayer.NAUDIO_DELAY_MS * (long)10000);
                        videoClock.Restart();
                    }

                    continue; 
                }

                if (aFrame == null) aFrames.TryDequeue(out aFrame);
                if (sFrame == null) sFrames.TryDequeue(out sFrame);

                elapsedTicks = videoStartTicks + videoClock.ElapsedTicks;

                vDistanceMs   = (int) (((vFrame.timestamp) - elapsedTicks) / 10000);
                aDistanceMs   = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                sDistanceMs   = sFrame != null ? (int) ((sFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;

                sleepMs = Math.Min(vDistanceMs, aDistanceMs) - 2;
                
                if (sleepMs < 0) sleepMs = 0;
                if (sleepMs > 1) Thread.Sleep(sleepMs);


                if (Math.Abs(vDistanceMs - sleepMs) < 4)
                {
                    CurTime = vFrame.timestamp;
                    renderer.PresentFrame(vFrame);
                    vFrames.TryDequeue(out vFrame);
                }
                else if (vDistanceMs < -4)
                {
                    ClearVideoFrame(vFrame);
                    vFrames.TryDequeue(out vFrame);
                    Log($"vDistanceMs 2 |-> {vDistanceMs}");
                }

                if (aFrame != null)
                {
                    if (Math.Abs(aDistanceMs - sleepMs) < 35)
                    {
                        audioPlayer.FrameClbk(aFrame.audioData, 0, aFrame.audioData.Length);
                        aFrames.TryDequeue(out aFrame);
                        if (aFrame != null)
                        {
                            audioPlayer.FrameClbk(aFrame.audioData, 0, aFrame.audioData.Length);
                            aFrames.TryDequeue(out aFrame);
                        }
                    }
                    else if (aDistanceMs < -35)
                    {
                        Log($"aDistanceMs 2 |-> {aDistanceMs}");
                        aFrames.TryDequeue(out aFrame);
                    }
                }

                if (sFrame != null)
                {
                    if (Math.Abs(sDistanceMs - sleepMs) < 80) {
                        renderer.NewMessage(OSDMessage.Type.Subtitles, sFrame.text, sFrame.subStyles, sFrame.duration);
                        sFrames.TryDequeue(out sFrame);
                    }
                    else if (sDistanceMs < -80)
                    {
                        Log($"sDistanceMs 2 |-> {sDistanceMs}");
                        sFrames.TryDequeue(out sFrame);
                    }
                }
            }

            ClearVideoFrame(vFrame);
            Log($"[SCREAMER] Finished -> {Utils.TicksToTime(CurTime)}");
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
                string ext      = url.LastIndexOf(".")  > 0 ? url.Substring(url.LastIndexOf(".") + 1) : "";
                string scheme   = url.IndexOf(":")      > 0 ? url.Substring(0, url.IndexOf(":")) : "";

                if (ext.ToLower() == "torrent" || scheme.ToLower() == "magnet")
                {
                    if (MediaFilesClbk == null || !Plugins.Contains("Torrent Streaming")) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed: Torrents are disabled"); status = Status.FAILED; OpenTorrentSuccessClbk?.BeginInvoke(false, null, null); return; }

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
                        // Web Streaming URLs (youtube-dl)
                        // TODO: Subtitles | List formats (-f <code>)
                        if ( Plugins.Contains("Web Streaming") && (scheme.ToLower() == "http" || scheme.ToLower() == "https") ) //&& (new Uri(url)).Host.ToLower() == "www.youtube.com" || ((new Uri(url)).Host.ToLower() == "youtube.com") )
                        {
                            Process proc = new Process 
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "youtube-dl.exe",
                                    Arguments = "-g " + url,
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    //RedirectStandardError = true,
                                    WindowStyle = ProcessWindowStyle.Hidden,
                                    CreateNoWindow = true
                                }
                            };
                            proc.Start();

                            string line = "", aUrl = "", vUrl = "";

                            while (!proc.StandardOutput.EndOfStream)
                            {
                                line = proc.StandardOutput.ReadLine();
                                if ( !Regex.IsMatch(line, @"^http") ) continue;
                                
                                if ( Regex.IsMatch(line, @"mime=audio") )
                                    aUrl = line;
                                else if ( Regex.IsMatch(line, @"mime=video") )
                                    vUrl = line;
                                else if ( vUrl == "")
                                    vUrl = line;
                                else
                                    aUrl = line;
                            }

                            if (aUrl == "" && vUrl == "")
                            {
                                // Fall back to proper open?
                                decoder.Open(url);
                                //renderer.NewMessage(OSDMessage.Type.Failed, $"Failed"); status = Status.FAILED; OpenFinishedClbk?.BeginInvoke(false, url, null, null); return;
                            }
                            else
                            {
                                if (aUrl == "")
                                    decoder.Open(vUrl, "", "", scheme + "://" + (new Uri(url)).Host.ToLower());
                                else
                                    decoder.Open(vUrl, aUrl, "", scheme + "://" + (new Uri(url)).Host.ToLower());
                            }
                        }
                        else
                        {
                            decoder.Open(url);
                            if (DownloadSubs == DownloadSubsMode.Files || DownloadSubs == DownloadSubsMode.FilesAndTorrents)
                            {
                                FileInfo file = new FileInfo(url);
                                string hash = Utils.ToHexadecimal(OpenSubtitles.ComputeMovieHash(file.FullName));
                                FindAvailableSubs(file.Name, hash, file.Length);
                            }
                            else
                            {
                                OpenNextAvailableSub();
                            }
                        }

                        if (!decoder.isReady) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed"); status = Status.FAILED; OpenFinishedClbk?.BeginInvoke(false, url, null, null); return; }
                        renderer.NewMessage(OSDMessage.Type.Open, $"Opened");
                        renderer.NewMessage(OSDMessage.Type.HardwareAcceleration);
                        InitializeEnv();
                        ShowOneFrame();
                        OpenFinishedClbk?.BeginInvoke(true, url, null, null);
                    });
                    openOrBuffer.SetApartmentState(ApartmentState.STA);
                    openOrBuffer.Start();
                }
            }
        }
        public void Play()
        { 
            if (!decoder.isReady || decoder.isRunning || isPlaying) { StatusChanged?.Invoke(this, new StatusChangedArgs(status)); return; }

            renderer.ClearMessages(OSDMessage.Type.Paused);
            if (beforeSeeking != Status.PLAYING) renderer.NewMessage(OSDMessage.Type.Play, "Play");
            beforeSeeking = Status.PLAYING;

            if (isTorrent)
            {
                status = Status.BUFFERING;
                streamer.Buffer((int)(CurTime/10000), BufferingDuration);
                return;
            }

            Play2();
        }
        public void Play2()
        {
            renderer.ClearMessages(OSDMessage.Type.Buffering);

            PauseThreads();
            status = Status.PLAYING;

            if (decoder.Finished) decoder.Seek(0);

            StatusChanged?.Invoke(this, new StatusChangedArgs(status));

            screamer = new Thread(() =>
            {
                try
                {
                    Screamer();
                } catch (Exception e) { Log(e.Message + " - " + e.StackTrace); }

                status = Status.STOPPED;
                StatusChanged?.Invoke(this, new StatusChangedArgs(status));
            });
            screamer.SetApartmentState(ApartmentState.STA);
            screamer.Start();
        }

        int prioritySeek = 0;
        public void Seek(int ms2, bool priority = false)
        {
            //ClearMediaFrames(); return; | For Unseekable - Live Stream | FormatContext flush / reopen?

            if (!decoder.isReady) return;

            if (!priority)
            {
                Interlocked.Exchange(ref SeekTime, (long)ms2 * 10000);
	            seekStack.Push(ms2);

                if (seekRequest.IsAlive || Interlocked.Equals(prioritySeek, 1)) return;
            }
            else
            {
                Interlocked.Exchange(ref prioritySeek, 1);
                Interlocked.Exchange(ref SeekTime, (long)ms2 * 10000);
                PauseThreads();
                Utils.EnsureThreadDone(seekRequest, 1000, 20);
                seekStack.Push(ms2);
                Interlocked.Exchange(ref prioritySeek, 0);
            }
            
            int waitMs = priority ? 2000 : 40;

            seekRequest = new Thread(() =>
            {
                if (Monitor.TryEnter(lockSeek, waitMs) && Monitor.TryEnter(lockOpening, 5))
		        {
			        try
			        {
                        //TODO
                        //seekLastValue = screamer start time leave    

                        if (isTorrent && streamer != null) streamer.Pause();
                        PauseThreads();                        
				        status = Status.SEEKING;

                        do
                        {
                            renderer.ClearMessages(OSDMessage.Type.Play);

                            if (!seekStack.TryPop(out int ms)) return;
                            seekStack.Clear();

                            if (Interlocked.Equals(prioritySeek, 1)) return;

                            CurTime = (long)ms * 10000;
                            decoder.Seek(ms, true);
                            if (Interlocked.Equals(prioritySeek, 1)) return;

                            if (seekStack.Count > 5) continue;
                            if (beforeSeeking != Status.PLAYING) ShowOneFrame();
                        } while (!seekStack.IsEmpty && decoder.isReady);

                    } catch (Exception) { 
                    } finally
			        {
                        Interlocked.Exchange(ref SeekTime, -1);
				        status = Status.STOPPED;
				        Monitor.Exit(lockSeek);
                        Monitor.Exit(lockOpening);
			        }

			        if (beforeSeeking == Status.PLAYING)
                    {
                        if (Interlocked.Equals(prioritySeek, 1)) return; 
                        Play();
                    }
                    else if ( isTorrent ) streamer.Buffer((int)(CurTime/10000), BufferingDuration);
		        }
            });
	        seekRequest.SetApartmentState(ApartmentState.STA);
            Utils.EnsureThreadDone(seekRequest);
	        seekRequest.Start();
        }
        public void Pause()
        {
            //audioPlayer.Pause();
            audioPlayer.ResetClbk();
            if (!decoder.isReady || !decoder.isRunning || !isPlaying) StatusChanged(this, new StatusChangedArgs(status));

            renderer.ClearMessages(OSDMessage.Type.Play);
            renderer.NewMessage(OSDMessage.Type.Paused, "Paused");
            //AbortThreads();
            PauseThreads();
            status = Status.PAUSED;
            beforeSeeking = Status.PAUSED;
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

        #region Subtitles
        Thread openSubs;
        public void FindAvailableSubs(string filename, string hash, long length)
        {
            if (Languages.Count < 1) return;
            Utils.EnsureThreadDone(openSubs);

            openSubs = new Thread(() =>
            {
                // TODO: (Local Files Search || Torrent Files) should use movie title not file name
                //foreach (string curfile in Directory.GetFiles(file.DirectoryName))
                //{
                //    if ( Regex.IsMatch(curfile, $@"{file.Name.Remove(file.Name.Length - file.Extension.Length)}.*\.srt") )
                //    {
                //        Log(curfile);
                //    }
                //}

                renderer.NewMessage(OSDMessage.Type.TopLeft2, "Downloading Subtitles ...");

                foreach (Language lang in Languages)
                {
                    List<OpenSubtitles> subs =  OpenSubtitles.SearchByHash(hash, length, lang);
                    subs.AddRange(OpenSubtitles.SearchByName(filename, lang));

                    for (int i=0; i<subs.Count; i++)
                        AvailableSubs.Add(new SubAvailable(lang, subs[i]));
                }

                FixSortSubs();
                SubtitlesAvailable?.Invoke(this, EventArgs.Empty);
                OpenNextAvailableSub();
                
            });
            openSubs.SetApartmentState(ApartmentState.STA);
            openSubs.Start();
        }
        public void FixSortSubs()
        {
            // Unique by SubHashes (if any)
            List<SubAvailable> uniqueList = new List<SubAvailable>();
            List<int> removeIds = new List<int>();
            for (int i=0; i<AvailableSubs.Count-1; i++)
            {
                if (AvailableSubs[i].sub == null || removeIds.Contains(i)) continue;

                for (int l=i+1; l<AvailableSubs.Count; l++)
                {
                    if (AvailableSubs[l].sub == null || removeIds.Contains(l)) continue;

                    if (AvailableSubs[l].sub.SubHash == AvailableSubs[i].sub.SubHash)
                    {
                        if (AvailableSubs[l].sub.AvailableAt == null)
                            removeIds.Add(l);
                        else
                        { removeIds.Add(i); break; }
                    }
                }
            }
            for (int i=0; i<AvailableSubs.Count; i++)
                if (!removeIds.Contains(i)) uniqueList.Add(AvailableSubs[i]);

            AvailableSubs = uniqueList;

            // Sorty by Lang Priority && Rating (if any)
            List<int> ids = new List<int>();
            List<SubAvailable> sortedList = new List<SubAvailable>();
            foreach (Language lang in Languages)
            {
                for (int i=0; i<AvailableSubs.Count; i++)
                {
                    if (ids.Contains(i)) continue;
                    if (AvailableSubs[i].lang == null || AvailableSubs[i].sub == null || AvailableSubs[i].lang.ISO639 != lang.ISO639) continue;

                    int curMaxId = i;

                    for (int l=0; l<AvailableSubs.Count; l++)
                    {
                        if (ids.Contains(l)) continue;
                        if (AvailableSubs[l].lang == null || AvailableSubs[l].sub == null || AvailableSubs[l].lang.ISO639 != lang.ISO639) continue;

                        if (float.Parse(AvailableSubs[l].sub.SubRating) > float.Parse(AvailableSubs[curMaxId].sub.SubRating)) curMaxId = l;
                    }

                    ids.Add(curMaxId);
                }

                for (int i=0; i<AvailableSubs.Count; i++)
                    if (AvailableSubs[i].lang != null && AvailableSubs[i].lang.ISO639 == lang.ISO639 && !ids.Contains(i)) ids.Add(i);

            }

            for (int i=0; i<AvailableSubs.Count; i++)
                if (AvailableSubs[i].lang == null) ids.Add(i);

            for (int i=0; i<ids.Count; i++)
                sortedList.Add(AvailableSubs[ids[i]]);
            
            AvailableSubs = sortedList;
        }
        public void OpenNextAvailableSub()
        {
            bool allused = true;

            // Find best match (lang priority - not already used - rating?)
            foreach (Language lang in Languages)
            {
                for (int i=0; i<AvailableSubs.Count; i++)
                {
                    if (!AvailableSubs[i].used) allused = false;
                    if (!AvailableSubs[i].used && AvailableSubs[i].lang?.IdSubLanguage == lang.IdSubLanguage)
                    {
                        renderer.NewMessage(OSDMessage.Type.TopLeft2, $"Found {AvailableSubs.Count} Subtitles (Using {lang.LanguageName})");
                        OpenSubs(i);
                        return;
                    }
                }
            }

            // Reset used and start from the beggining
            if (allused && AvailableSubs.Count > 0 && Languages.Count > 0)
            {
                for (int i=0; i<AvailableSubs.Count; i++)
                {
                    SubAvailable sub = AvailableSubs[i];
                    sub.used = false;
                    AvailableSubs[i] = sub;
                }
                OpenNextAvailableSub();
            }
        }
        public void OpenSubs(int availableIndex)
        {
            SubAvailable sub = AvailableSubs[availableIndex];

            if (sub.streamIndex > 0)
            {
                sFrames = new ConcurrentQueue<MediaFrame>();
                decoder.video.EnableEmbeddedSubs(sub.streamIndex);
            }
            else if (sub.sub != null)
            {
                if (sub.sub.AvailableAt != null)
                    OpenSubs(sub.sub.AvailableAt);
                else
                {
                    sub.sub.Download();
                    OpenSubs(sub.sub.AvailableAt);
                }
            }
            else if (sub.path != null)
            {
                OpenSubs(sub.path);
            }

            sub.used = true;
            AvailableSubs[availableIndex] = sub;
            CurSubId = availableIndex;
        }
        public int  OpenSubs(string url)
        {
            if (!decoder.isReady) return -1;

            int ret = 0;
            subsExternalDelay = 0;
            sFrames = new ConcurrentQueue<MediaFrame>();

            renderer.ClearMessages(OSDMessage.Type.Subtitles);

            decoder.video.DisableEmbeddedSubs();

            foreach (SubAvailable sub in AvailableSubs)
                if (sub.path != null && sub.path.ToLower() == url.ToLower()) { AvailableSubs.Remove(sub); break; }

            if ((ret = decoder.Open("", "", url)) != 0) { renderer.NewMessage(OSDMessage.Type.Failed, $"Subtitles Failed"); return ret; }

            bool exists = false;
            //foreach (SubAvailable sub in availableSubs)
            for (int i=0; i<AvailableSubs.Count; i++)
                if (AvailableSubs[i].sub != null && AvailableSubs[i].sub.AvailableAt?.ToLower() == url.ToLower()) { exists = true; CurSubId=i; }
            
            if (!exists) { AvailableSubs.Add(new SubAvailable(null, url)); CurSubId = AvailableSubs.Count-1; } // TODO Detect

            return 0;
        }
        #endregion

        #region Streamer
        private void BufferingDone(bool done)
        { 
            if (!done) return;

            if (beforeSeeking == Status.PLAYING) Play2();
        }
        public void SetMediaFile(string fileName)
        {
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

                // Open Decoder & Buffer Decoder
                int ret = streamer.SetMediaFile(fileName);
                if (ret != 0 || !decoder.isReady) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed {ret}"); status = Status.FAILED; OpenFinishedClbk?.BeginInvoke(false, fileName, null, null); return; }

                InitializeEnv();
                OpenFinishedClbk?.BeginInvoke(true, fileName, null, null);
            });
            openOrBuffer.SetApartmentState(ApartmentState.STA);
            openOrBuffer.Start();
        }
        public void StopMediaStreamer() { if ( Plugins.Contains("Torrent Streaming") && streamer != null ) { streamer.Dispose(); streamer = null; } }
        #endregion

        #region Misc
        private void ShowOneFrame()
        {
            //ClearMediaFrames();
            renderer.ClearMessages(OSDMessage.Type.Subtitles);

            decoder.video.DecodeFrame();
            //decoder.subs.DecodeFrame(); requires resync | only embedded

            if (vFrames.Count > 0)
            {
                MediaFrame vFrame = null;
                vFrames.TryDequeue(out vFrame);
                renderer.PresentFrame(vFrame);
            }

            // should also check timestamp based on vFrame.timestamp
            //if (sFrames.Count > 0 && hasSubs && doSubs)
            //{
            //    MediaFrame sFrame = null;
            //    sFrames.TryPeek(out sFrame);
            //    renderer.NewMessage(OSDMessage.Type.Subtitles, sFrame.text, sFrame.subStyles, sFrame.duration);
            //}

            return;
        }
        public void Render() { renderer.PresentFrame(null); }
        internal void ClearMediaFrames()
        {
            ClearVideoFrames();
            lock (aFrames) aFrames = new ConcurrentQueue<MediaFrame>();
            lock (sFrames) sFrames = new ConcurrentQueue<MediaFrame>();
        }
        internal void ClearVideoFrames()
        {
            lock (vFrames)
            {
                while (vFrames.Count > 0)
                {
                    MediaFrame m;
                    vFrames.TryDequeue(out m);
                    lock (renderer.device) renderer.device.ImmediateContext.Flush();
                    ClearVideoFrame(m);
                }
                vFrames = new ConcurrentQueue<MediaFrame>();
            }
        }
        internal void ClearVideoFrame(MediaFrame m)
        {
            if (m == null) return;
            SharpDX.Utilities.Dispose(ref m.textureHW);
            SharpDX.Utilities.Dispose(ref m.textureY);
            SharpDX.Utilities.Dispose(ref m.textureU);
            SharpDX.Utilities.Dispose(ref m.textureV);
            SharpDX.Utilities.Dispose(ref m.textureRGB);
        }

        private void PauseThreads(bool andDecoder = true)
        {
            Log($"[Pausing All Threads] START");
            status = Status.STOPPING;
            Utils.EnsureThreadDone(screamer);
            if (andDecoder) decoder.Pause();
            if (hasAudio) audioPlayer.ResetClbk();
            status = Status.STOPPED;
            Log($"[Pausing All Threads] END");
        }
        private void AbortThreads(bool andDecoder = true)
        {
            Log($"[Aborting All Threads] START");
            status = Status.STOPPING;
            if (screamer != null) screamer.Abort();
            if (decoder != null) decoder.Pause();
            audioPlayer.ResetClbk();
            status = Status.STOPPED;
            Log($"[Aborting All Threads] END");
        }
        
        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] {msg}"); }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        public static extern uint TimeBeginPeriod(uint uMilliseconds);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        public static extern uint TimeEndPeriod(uint uMilliseconds);
        #endregion
    }
}