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
 * Torrent      | Check BitSwarm Options for more connection / peers etc. timeouts / limits
 * Contexts     | Single vs Multi - Single gives more speed especially for seeking / Multi gives more freedom mainly for delays possible issues with matroska demuxing (index entries built based on video frames) ** For Single you might run a 2nd decoder in the same context to grap the late frames use 2 pkts?
 * Delays       | NAudio default latency was 300ms dropped to 200ms means we push subs/video +200ms | on single format context has risk to loose avs frames in the same range (same for subs if we want delay) | on multi context should be no problem
 * Web Streaming| Requires more testing, by default get the best quality, should give options for lower | check also format context's options for http (and abort)
 * 
 * TimeBeginPeriod(1) should TimeEndPeriod(1) - also consider TimeBeginPeriod(>1) for some other cases
 */

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

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

        //Stopwatch               videoClock          = new Stopwatch();
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
        public int VIDEO_MIX_QUEUE_SIZE = 20;  public int VIDEO_MAX_QUEUE_SIZE = 25;
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
            OnFullActive
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

        public enum InputType
        {
            File,
            TorrentPart,
            TorrentFile,
            Web,
            WebLive
        }
        public enum ActivityMode
        {
            Idle,
            Active,
            FullActive
        }
        public enum ViewPorts
        {
            KEEP,
            FILL,
            CUSTOM
        }
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

        public Action<bool, string>             OpenFinishedClbk;
        public Action<bool>                     BufferSuccessClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;

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
        public bool isStopping      => status == Status.STOPPING;
        public bool isStopped       => status == Status.STOPPED;
        public bool isFailed        => status == Status.FAILED;
        public bool isOpened        => status == Status.OPENED;
        public bool isReady         { get; private set; }
        public bool isTorrent       => UrlType == InputType.TorrentFile || UrlType == InputType.TorrentPart;
        public bool isLive          => Duration == 0; // || UrlType == InputType.WebLive;

        public string       Url             { get; internal set; }
        public InputType    UrlType         { get; internal set; }
        public string       UrlFolder       { get { return isTorrent ? streamer.FolderComplete : (new FileInfo(Url)).DirectoryName; } }
        public string       UrlName         { get { return isTorrent ? streamer.FileName : (File.Exists(Url) ? (new FileInfo(Url)).Name : Url); } }
        
        public History      History         { get; internal set; }
        public bool         HistoryEnabled  { get; set; } = true;
        public int          HistoryEntries  { get { return History != null ? History.MaxEntries : 30; } set { if (History == null) History = new History(Path.Combine(Directory.GetCurrentDirectory(), "History"), value); History.MaxEntries = value; } }

        public long CurTime         { get; private set; }
        public long SeekTime        = -1;
        public int  verbosity       { get; set; }

        public ActivityMode                 Activity    { get; set;         } = ActivityMode.FullActive;
        public Dictionary<string, string>   PluginsList { get; private set; } = new Dictionary<string, string>();
        public List<string>                 Plugins     { get; private set; } = new List<string>();

        // Video
        public ViewPorts ViewPort       { get; set; } = ViewPorts.KEEP;
        public float    DecoderRatio    { get; set; } = 16f/9f;
        public float    CustomRatio     { get; set; } = 16f/9f;
        public bool     hasVideo        { get { return decoder.hasVideo;            } }
        public long     Duration        { get { return hasVideo ? decoder.vStreamInfo.durationTicks : decoder.aStreamInfo.durationTicks; } }
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
                if (!decoder.isReady || !isReady || !decoder.hasAudio || audioExternalDelay == value) return;

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
                // TODO: Unexplained delay during Torrent Streaming | To be reviewed

                if (!decoder.isReady || !isReady || !decoder.hasSubs || subsExternalDelay == value) return;

                subsExternalDelay = value;

                if (decoder.subs != null) decoder.subs.ReSync();
                sFrames = new ConcurrentQueue<MediaFrame>();

                renderer.ClearMessages(OSDMessage.Type.Subtitles);
                renderer.NewMessage(OSDMessage.Type.SubsDelay);
            }
        }
        public int      SubsPosition    { get { return renderer.SubsPosition;       } set { renderer.SubsPosition = value; renderer?.NewMessage(OSDMessage.Type.SubsHeight); } }
        public float    SubsFontSize    { get { return renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].FontSize; } set { renderer.osd[renderer.msgToSurf[OSDMessage.Type.Subtitles]].FontSize = value; renderer?.NewMessage(OSDMessage.Type.SubsFontSize); } }

        // Online Subs
        private int     curSubId        = -1;
        public int      CurSubId        { get { return curSubId; } set { if (curSubId == value) return; PrevSubId = curSubId; curSubId = value; Log($"Sub Id Changed From {PrevSubId} to {curSubId}"); } }
        public int      PrevSubId       { get; private set; } = -1;
        public List<Language>       Languages       { get; set; } = new List<Language>();
        public List<SubAvailable>   AvailableSubs   { get; set; }
        public DownloadSubsMode     DownloadSubs    { get; set; } = DownloadSubsMode.FilesAndTorrents;
        public class SubAvailable
        {
            public Language      lang;
            public string        path;
            public string        pathUTF8;
            public int           streamIndex;
            public OpenSubtitles sub;
            public bool          used;

            public SubAvailable() { streamIndex = -1; }
            public SubAvailable(Language lang, string path)      {this.lang = lang; this.streamIndex = -1;           this.sub = null; this.path = path; used = true;  this.pathUTF8 = null; }
            public SubAvailable(Language lang, int streamIndex)  {this.lang = lang; this.streamIndex = streamIndex;  this.sub = null; this.path = null; used = false; this.pathUTF8 = null; }
            public SubAvailable(Language lang, OpenSubtitles sub){this.lang = lang; this.streamIndex = -1;           this.sub = sub;  this.path = null; used = false; this.pathUTF8 = null; }
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
        public  void InitHandle(IntPtr handle, bool designMode = false)
        {
            renderer.InitHandle(handle);

            if (!designMode)
            {
                audioPlayer = new AudioPlayer();
                decoder.Init(verbosity);

                streamer    = new MediaStreamer(this);
                streamer.BufferingDoneClbk  = BufferingDone;
                streamer.MediaFilesClbk     = MediaFilesClbk;

                TimeBeginPeriod(5);

                Initialize();
            }
        }
        private void Initialize(bool andStreamer = true)
        {
            Log($"[Initializing]");

            PauseThreads();
            if (andStreamer) streamer.Initialize();
            if (openOrBuffer != null) openOrBuffer.Abort(); 

            isReady         = false;
            beforeSeeking   = Status.STOPPED;

            AvailableSubs       = new List<SubAvailable>();
            CurSubId            = -1;
            PrevSubId           = -1;
            CurTime             =  0;
            subsExternalDelay   =  0;

            ClearMediaFrames();
            renderer.ClearMessages();
            seekStack.Clear();

            Log($"[Initialized]");
        }
        private void InitializeEnv()
        {
            Log($"[Initializing Evn]");

            if (!decoder.isReady)
            {
                renderer.NewMessage(OSDMessage.Type.Failed, $"Failed");
                status = Status.FAILED;
                OpenFinishedClbk?.BeginInvoke(false, UrlName, null, null);
                return; 
            }

            if (hasAudio)
            {
                audioPlayer._RATE   = decoder._RATE;
                audioPlayer.Initialize();
            }

            DecoderRatio = (float)decoder.vStreamInfo.width / (float)decoder.vStreamInfo.height;
            renderer.FrameResized(decoder.vStreamInfo.width, decoder.vStreamInfo.height);

            if (UrlType == InputType.Web && Duration == 0) UrlType = InputType.WebLive;

            isReady = true; // Must be here for Audio/Subs Delays

            if (HistoryEnabled && History.Add(Url, UrlType, (UrlType == InputType.TorrentFile || UrlType == InputType.TorrentPart ? streamer.FileName : null)))
            {
                History.Entry curHistory = History.GetCurrent();
                AvailableSubs = curHistory.AvailableSubs;
                if (AvailableSubs != null && AvailableSubs.Count > 0) OpenSubs(curHistory.CurSubId);

                AudioExternalDelay  = curHistory.AudioExternalDelay;
                SubsExternalDelay   = curHistory.SubsExternalDelay;
            }

            // In case of history entry & already previously success Opensubtitles results dont search again
            bool hasAlreadyOnlineSubs = false;
            if (AvailableSubs != null && AvailableSubs.Count > 0)
                for (int i=0; i<AvailableSubs.Count-1; i++)
                    if (AvailableSubs[i].sub != null) { hasAlreadyOnlineSubs = true; break; }

            if (!hasAlreadyOnlineSubs)
            {
                if      (UrlType == InputType.File          && (DownloadSubs == DownloadSubsMode.FilesAndTorrents || DownloadSubs == DownloadSubsMode.Files))
                {
                    FileInfo file = new FileInfo(Url);
                    string hash = Utils.ToHexadecimal(OpenSubtitles.ComputeMovieHash(file.FullName));
                    FindAvailableSubs(file.Name, hash, file.Length);
                }
                else if (UrlType == InputType.TorrentFile   && (DownloadSubs == DownloadSubsMode.FilesAndTorrents || DownloadSubs == DownloadSubsMode.Torrents))
                {
                    string hash = Utils.ToHexadecimal(OpenSubtitles.ComputeMovieHash(Path.Combine(streamer.FolderComplete, streamer.FileName)));
                    FindAvailableSubs(streamer.FileName, hash, streamer.FileSize);
                }
                else if (UrlType == InputType.TorrentPart   && (DownloadSubs == DownloadSubsMode.FilesAndTorrents || DownloadSubs == DownloadSubsMode.Torrents))
                {
                    FindAvailableSubs(streamer.FileName, streamer.GetMovieHash(), streamer.FileSize);
                }
                else
                {
                    FixSortSubs();
                    OpenNextAvailableSub();
                }
            }

            status = Status.OPENED;

            // ShowOneFrame(); ?

            renderer.NewMessage(OSDMessage.Type.Open, $"Opened");
            renderer.NewMessage(OSDMessage.Type.HardwareAcceleration);
            OpenFinishedClbk?.BeginInvoke(true, UrlName, null, null);

            Log($"[Initialized Evn]");
        }
        private void LoadPlugins()
        {
            PluginsList.Add("Torrent Streaming", "Plugins\\BitSwarm\\BitSwarmLib.dll");
            PluginsList.Add("Web Streaming", YoutubeDL.plugin_path);

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
            renderer.ClearMessages(OSDMessage.Type.Subtitles);

            if (!decoder.isRunning || decoder.video.requiresResync) decoder.Run();
            audioPlayer.ResetClbk();
            audioPlayer.Play();

            bool framePresented = false;

            while (isPlaying && vFrames.Count < (Duration == 0 ? VIDEO_MIX_QUEUE_SIZE * 3 : VIDEO_MIX_QUEUE_SIZE) && !decoder.Finished)
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
      
            long    startedAtTicks;

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
                startedAtTicks = DateTime.UtcNow.Ticks;
            }
            
            Log($"[SCREAMER] Started -> {Utils.TicksToTime(videoStartTicks)}");

            while (isPlaying)
            {
                if (vFrames.Count == 0)//|| (hasAudio && aFrames.Count < 1) )
                {
                    if (decoder.Finished) break;

                    lock (lockSeek)
                    {
                        Log("Screamer Restarting ........................");

                        if (isTorrent && streamer.RequiresBuffering)
                        {
                            audioPlayer.ResetClbk();
                            streamer.Buffer((int)(CurTime / 10000));
                            break;
                        }

                        MediaBuffer();
                        vFrames.TryDequeue(out vFrame); if (vFrame == null) return;
                        aFrames.TryDequeue(out aFrame);
                        sFrames.TryDequeue(out sFrame);
                        SeekTime        = -1;
                        CurTime         = vFrame.timestamp;
                        Log($"[SCREAMER] Restarted -> {Utils.TicksToTime(CurTime)}");
                        videoStartTicks = aFrame != null && aFrame.timestamp < vFrame.timestamp - (AudioPlayer.NAUDIO_DELAY_MS * (long)10000) ? aFrame.timestamp : vFrame.timestamp - (AudioPlayer.NAUDIO_DELAY_MS * (long)10000);
                        startedAtTicks = DateTime.UtcNow.Ticks;
                    }

                    continue; 
                }

                if (aFrame == null) aFrames.TryDequeue(out aFrame);
                if (sFrame == null) sFrames.TryDequeue(out sFrame);

                //elapsedTicks = videoStartTicks + videoClock.ElapsedTicks;
                elapsedTicks = videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks);

                vDistanceMs   = (int) (((vFrame.timestamp) - elapsedTicks) / 10000);
                aDistanceMs   = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                sDistanceMs   = sFrame != null ? (int) ((sFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;

                sleepMs = Math.Min(vDistanceMs, aDistanceMs) - 2;

                if (sleepMs < 0) sleepMs = 0;
                if (sleepMs > 1)
                {
                    if (sleepMs > 1000)
                    {
                        lock (lockSeek)
                        {
                            Log("Screamer Restarting ........................ (Testing HLS)");

                            if (isTorrent && streamer.RequiresBuffering)
                            {
                                audioPlayer.ResetClbk();
                                streamer.Buffer((int)(CurTime / 10000));
                                break;
                            }

                            MediaBuffer();
                            vFrames.TryDequeue(out vFrame); if (vFrame == null) return;
                            aFrames.TryDequeue(out aFrame);
                            sFrames.TryDequeue(out sFrame);
                            SeekTime        = -1;
                            CurTime         = vFrame.timestamp;
                            Log($"[SCREAMER] Restarted -> {Utils.TicksToTime(CurTime)}");
                            videoStartTicks = aFrame != null && aFrame.timestamp < vFrame.timestamp - (AudioPlayer.NAUDIO_DELAY_MS * (long)10000) ? aFrame.timestamp : vFrame.timestamp - (AudioPlayer.NAUDIO_DELAY_MS * (long)10000);

                            startedAtTicks = DateTime.UtcNow.Ticks;
                        }

                        continue; 
                    }

                    Thread.Sleep(sleepMs);
                }

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
        public void Open(string url, bool isTorrentFile = false)
        {
            /* Open [All in One]
             * 
             * 1. Subtitles (OpenSubs)                                                      [By extension eg. .srt, .sub etc]
             * 2. Torrent Streaming (Includes selected file on already opened BitSwarm)     [By isTorrentFile parameter]
             * 3. Web Streaming (Youtube-dl) with FFmpeg fallback for http(s)               [By scheme http(s)]
             * 4. Torrent Streaming (BitSwarm: Opens)                                       [By BitSwarm's valid inputs]
             * 5. Rest (Local Files + FFmpeg supported)                                     [Else]
             */

            if (url == null || url.Trim() == "") { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed"); status = Status.FAILED; /*OpenTorrentSuccessClbk?.BeginInvoke(false, null, null);*/ return; }

            string ext      = url.LastIndexOf(".")  > 0 ? url.Substring(url.LastIndexOf(".") + 1) : "";
            string scheme   = url.IndexOf(":")      > 0 ? url.Substring(0, url.IndexOf(":")) : "";

            // Open Subs
            if (Utils.SubsExts.Contains(ext)) { OpenSubs(url); return; }

            Initialize(!isTorrentFile);

            lock (lockOpening)
            {
                Log($"Opening {url}");
                renderer.NewMessage(OSDMessage.Type.Open, $"Opening {url}");

                int ret;
                status = Status.OPENING;

                if (isTorrentFile)
                {
                    openOrBuffer = new Thread(() =>
                    {
                        streamer.SetMediaFile(url);
                        InitializeEnv();
                    });
                    openOrBuffer.SetApartmentState(ApartmentState.STA);
                    openOrBuffer.Start();
                    return;
                }

                Url = url;

                if (Plugins.Contains("Torrent Streaming") && BitSwarmLib.BitSwarm.ValidateInput(url) != BitSwarmLib.BitSwarm.InputType.Unkown) //ext.ToLower() == "torrent" || scheme.ToLower() == "magnet")
                {
                    if (MediaFilesClbk == null || !Plugins.Contains("Torrent Streaming")) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed: Torrents are disabled"); status = Status.FAILED; /*OpenTorrentSuccessClbk?.BeginInvoke(false, null, null);*/ return; }

                    UrlType = InputType.TorrentPart; // will be re-set in initialEnv

                    openOrBuffer = new Thread(() =>
                    {
                        try
                        {
                            ret = streamer.Open(url);
                            if (ret != 0) { renderer.NewMessage(OSDMessage.Type.Failed, $"Failed"); status = Status.FAILED; /*OpenTorrentSuccessClbk?.BeginInvoke(false, null, null);*/ return; }

                            status = Status.OPENED;
                            renderer.NewMessage(OSDMessage.Type.Open, $"Opened");
                            renderer.NewMessage(OSDMessage.Type.HardwareAcceleration);

                        } catch (ThreadAbortException) { return; }
                    });
                    openOrBuffer.SetApartmentState(ApartmentState.STA);
                    openOrBuffer.Start();
                }
                else
                {
                    openOrBuffer = new Thread(() =>
                    {
                        // Web Streaming URLs (youtube-dl)
                        // TODO: Subtitles | List formats (-F | -f <code>) | Truncate query parameters from youtube URL to ensure youtube-dl will accept it
                        if (Plugins.Contains("Web Streaming") && (scheme.ToLower() == "http" || scheme.ToLower() == "https"))
                        {
                            string aUrl = null, vUrl = null;

                            UrlType = InputType.Web;
                            YoutubeDL ytdl = YoutubeDL.Get(url, out aUrl, out vUrl);
                            
                            if (vUrl != null)
                                decoder.Open(vUrl, aUrl != null && vUrl != aUrl  ? aUrl : "", "", scheme + "://" + (new Uri(url)).Host.ToLower());
                            InitializeEnv();
                        }
                        else
                        {
                            UrlType = InputType.File;
                            decoder.Open(url);
                            InitializeEnv();
                        }
                    });
                    openOrBuffer.SetApartmentState(ApartmentState.STA);
                    openOrBuffer.Start();
                }
            }
        }
        public void Play(bool foreward = false)
        { 
            if (!decoder.isReady || decoder.isRunning || isPlaying) { StatusChanged?.Invoke(this, new StatusChangedArgs(status)); return; }

            renderer.ClearMessages(OSDMessage.Type.Paused);
            if (beforeSeeking != Status.PLAYING) renderer.NewMessage(OSDMessage.Type.Play, "Play");
            beforeSeeking = Status.PLAYING;

            if (isTorrent && streamer.RequiresBuffering)
            {
                status = Status.BUFFERING;
                streamer.Buffer((int)(CurTime / 10000), foreward);
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
                    TimeBeginPeriod(1);
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED);
                    Screamer();
                } catch (Exception e) { Log(e.Message + " - " + e.StackTrace); }

                TimeEndPeriod(1);
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                status = Status.STOPPED;
                StatusChanged?.Invoke(this, new StatusChangedArgs(status));
            });
            screamer.SetApartmentState(ApartmentState.STA);
            screamer.Start();
        }

        int prioritySeek = 0;
        public void Seek(int ms2, bool priority = false, bool foreward = false, bool forcePlay = false)
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
                            renderer.ClearMessages(OSDMessage.Type.Buffering);

                            if (!seekStack.TryPop(out int ms)) return;
                            seekStack.Clear();

                            if (Interlocked.Equals(prioritySeek, 1)) return;

                            CurTime = (long)ms * 10000;
                            decoder.Seek(ms, true, foreward);
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

			        if (beforeSeeking == Status.PLAYING || forcePlay)
                    {
                        if (Interlocked.Equals(prioritySeek, 1)) return; 
                        Play(foreward);
                    }
                    else if (isTorrent && streamer.RequiresBuffering) streamer.Buffer((int)(CurTime / 10000), foreward);
		        }
            });
	        seekRequest.SetApartmentState(ApartmentState.STA);
            Utils.EnsureThreadDone(seekRequest);
	        seekRequest.Start();
        }
        public void Pause()
        {
            audioPlayer.ResetClbk();

            if (!decoder.isReady || !decoder.isRunning || !isPlaying) StatusChanged(this, new StatusChangedArgs(status));

            renderer.ClearMessages(OSDMessage.Type.Play);
            renderer.NewMessage(OSDMessage.Type.Paused, "Paused");

            PauseThreads();
            status          = Status.PAUSED;
            beforeSeeking   = Status.PAUSED;
        }
        public void Close()
        {
            PauseThreads();
            Initialize();

            if (renderer    != null) renderer.Dispose();
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

                /* TODO:
                 * 
                 * Subs encoding uses Opensubtitles functionality to retrieve them in utf8
                 * However any non utf8 will be tried by BOM and then the system default codepage

                 * Sorting should give priority to match by movie hash to ensure we dont use high rating but for different moview subittles
                */

                // Search By MovieHash (all langs) -> Get IMDBid -> Search By IMDBid (each lang) -> Search by FileName (each lang)
                renderer.NewMessage(OSDMessage.Type.TopLeft2, "Downloading Subtitles ...");

                List<OpenSubtitles> subs =  OpenSubtitles.SearchByHash2(hash, length);

                bool imdbExists = subs != null && subs.Count > 0 && subs[0].IDMovieImdb != null && subs[0].IDMovieImdb.Trim() != "";
                bool isEpisode  = imdbExists && subs[0].SeriesSeason != null && subs[0].SeriesSeason.Trim() != "" && subs[0].SeriesSeason.Trim() != "0" && subs[0].SeriesEpisode != null && subs[0].SeriesEpisode.Trim() != "" && subs[0].SeriesEpisode.Trim() != "0"; // Probably MovieKind episode/movie should be fine

                foreach (Language lang in Languages)
                {
                    if (imdbExists)
                    {
                        if (isEpisode)
                            subs.AddRange(OpenSubtitles.SearchByIMDB(subs[0].IDMovieImdb, lang, subs[0].SeriesSeason, subs[0].SeriesEpisode));
                        else
                            subs.AddRange(OpenSubtitles.SearchByIMDB(subs[0].IDMovieImdb, lang));
                    }
                    
                    subs.AddRange(OpenSubtitles.SearchByName(filename, lang)); // TODO: Search with more efficient movie title
                }

                for (int i=0; i<subs.Count; i++)
                    AvailableSubs.Add(new SubAvailable(Language.Get(subs[i].LanguageName), subs[i]));

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


            // Order by Language [MovieHash & Rating | Non-MovieHash & Rating | Rest]
            AvailableSubs = new List<SubAvailable>();
            foreach (Language lang in Languages)
            {
                IEnumerable<SubAvailable> movieHashRating = 
                    from sub in uniqueList
                    where sub.sub != null && sub.lang != null && sub.lang.ISO639 == lang.ISO639 && sub.sub.MatchedBy.ToLower() == "moviehash"
                    orderby float.Parse(sub.sub.SubRating) descending
                    select sub;

                IEnumerable<SubAvailable> rating = 
                    from sub in uniqueList
                    where sub.sub != null && sub.lang != null && sub.lang.ISO639 == lang.ISO639 && sub.sub.MatchedBy.ToLower() != "moviehash"
                    orderby float.Parse(sub.sub.SubRating) descending
                    select sub;

                IEnumerable<SubAvailable> rest = 
                    from sub in uniqueList
                    where sub.sub == null && sub.lang != null && sub.lang.ISO639 == lang.ISO639
                    select sub;

                AvailableSubs.AddRange(movieHashRating);
                AvailableSubs.AddRange(rating);
                AvailableSubs.AddRange(rest);
            }

            IEnumerable<SubAvailable> langUndefined = 
                from sub in uniqueList
                where sub.lang == null && sub.sub == null
                select sub;

            AvailableSubs.AddRange(langUndefined);
        }
        public void OpenNextAvailableSub()
        {
            if (AvailableSubs.Count == 0) { renderer.NewMessage(OSDMessage.Type.TopLeft2, $"No Subtitles Found"); return; }

            bool allused = true;

            // Find best match (lang priority - not already used - rating?)
            foreach (Language lang in Languages)
            {
                for (int i=0; i<AvailableSubs.Count; i++)
                {
                    if (!AvailableSubs[i].used) allused = false;
                    if (!AvailableSubs[i].used && AvailableSubs[i].lang?.LanguageName == lang.LanguageName)
                    {
                        renderer.NewMessage(OSDMessage.Type.TopLeft2, $"Found {AvailableSubs.Count} Subtitles (Using {(AvailableSubs[i].streamIndex > 0 ? "Embedded" : "")} {lang.LanguageName})");
                        OpenSubs(i);
                        return;
                    }
                }
            }

            // Check also the non-lang (external subs)
            for (int i=0; i<AvailableSubs.Count; i++)
            {
                if (!AvailableSubs[i].used) allused = false;

                if (!AvailableSubs[i].used && AvailableSubs[i].lang == null)
                {
                    renderer.NewMessage(OSDMessage.Type.TopLeft2, $"Found {AvailableSubs.Count} Subtitles (Using {(AvailableSubs[i].streamIndex > 0 ? "Embedded" : "")} Unknown)");
                    OpenSubs(i);
                    return;
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
            else
            {
                if (sub.pathUTF8 == null)
                {
                    if (sub.sub != null && sub.sub.AvailableAt == null)
                    {
                        sub.sub.Download();

                        if (sub.sub.AvailableAt == null) return; // Failed to download
                        sub.path = sub.sub.AvailableAt; // do we need that?
                        
                        Directory.CreateDirectory(Path.Combine(UrlFolder, "Subs"));
                        sub.pathUTF8 = Path.Combine(UrlFolder, "Subs", sub.sub.SubFileName + ".utf8." + sub.sub.SubLanguageID + ".srt");

                        Encoding subsEnc = Subtitles.Detect(sub.sub.AvailableAt);
                        if (subsEnc != Encoding.UTF8)
                        {
                            if (!Subtitles.Convert(sub.sub.AvailableAt, sub.pathUTF8, subsEnc, new UTF8Encoding(false))) return; // Failed to convert
                        }
                        else // Copy File to directory?
                            File.Copy(sub.sub.AvailableAt, sub.pathUTF8, true);
                    }
                    else // Shouldnt be here
                        return;
                }

                subsExternalDelay = 0;
                sFrames = new ConcurrentQueue<MediaFrame>();
                renderer.ClearMessages(OSDMessage.Type.Subtitles);
                decoder.video.DisableEmbeddedSubs();
                if (decoder.Open("", "", sub.pathUTF8) != 0) { renderer.NewMessage(OSDMessage.Type.Failed, $"Subtitles Failed"); return; }
            }

            sub.used = true;
            AvailableSubs[availableIndex] = sub;
            CurSubId = availableIndex;

            History.Update(AvailableSubs, CurSubId);
        }
        public void OpenSubs(string url)
        {
            if (!decoder.isReady) return;

            for (int i=0; i<AvailableSubs.Count; i++)
                if ((AvailableSubs[i].path != null && AvailableSubs[i].path.ToLower() == url.ToLower()) || (AvailableSubs[i].pathUTF8 != null && AvailableSubs[i].pathUTF8.ToLower() == url.ToLower()))
                {
                    OpenSubs(i);
                    return;
                }

            SubAvailable sub = new SubAvailable(null, url);

            Encoding subsEnc = Subtitles.Detect(url);
            if (subsEnc != Encoding.UTF8)
            {
                FileInfo fi = new FileInfo(url);
                Directory.CreateDirectory(Path.Combine(UrlFolder, "Subs"));
                sub.pathUTF8 = Path.Combine(UrlFolder, "Subs", fi.Name.Remove(fi.Name.Length - fi.Extension.Length) + ".utf8.ext.srt");
                if (!Subtitles.Convert(url, sub.pathUTF8, subsEnc, new UTF8Encoding(false))) return; // Failed to convert
            }
            else
                sub.pathUTF8 = sub.path;

            decoder.video.DisableEmbeddedSubs();
            sFrames = new ConcurrentQueue<MediaFrame>();
            subsExternalDelay = 0;
            renderer.ClearMessages(OSDMessage.Type.Subtitles);

            if (decoder.Open("", "", sub.pathUTF8) != 0) { renderer.NewMessage(OSDMessage.Type.Failed, $"Subtitles Failed"); return; }

            AvailableSubs.Add(sub);
            CurSubId = AvailableSubs.Count - 1;

            History.Update(AvailableSubs, CurSubId);
        }
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
        private void BufferingDone(bool done)
        { 
            if (!done) return;

            if (beforeSeeking == Status.PLAYING) Play2();
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

        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] {msg}"); }

        [SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        public static extern uint TimeBeginPeriod(uint uMilliseconds);

        [SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        public static extern uint TimeEndPeriod(uint uMilliseconds);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto,SetLastError = true)]
        public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
        [FlagsAttribute]
        public enum EXECUTION_STATE :uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }
        #endregion
    }
}