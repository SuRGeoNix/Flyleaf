/* Media Player Synching & Control Flow - the "Mediator" | Old Notes from v2.4
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

using SuRGeoNix.Flyleaf.MediaFramework;
using SuRGeoNix.BitSwarmLib;
using System.Diagnostics;

namespace SuRGeoNix.Flyleaf
{
    public class MediaRouter
    {
        #region Declaration
        public AudioPlayer      audioPlayer;
        public DecoderContext   decoder;
        public MediaRenderer    renderer;
        public TorrentStreamer  torrentStreamer;

        public YoutubeDL        ytdl;

        public Status           beforeSeeking;
        Status                  status;

        readonly object         lockSeek            = new object();
        readonly object         lockOpening         = new object();

        Thread      screamer;
        Thread      openVideoThread;
        Thread      openAudioThread;
        Thread      openSubsThread;
        Thread      seekThread;

        MediaFrame  sFrame;
        MediaFrame  vFrame;
        MediaFrame  aFrame;

        long        startedAtTicks;
        long        videoStartTicks;

        Stopwatch   seekWatch;
        int         lastSeekMs;        
        ConcurrentStack<SeekData> seeks;

        class SeekData
        {
            public int  ms;
            public bool priority;
            public bool foreward;

            public SeekData(int ms, bool priority,  bool foreward) { this.ms = ms; this.priority = priority; this.foreward = foreward; }
        }
        
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
            Other,
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
            //SEEKING,
            STOPPING,
            STOPPED
        }

        public Action<bool, string>             OpenFinishedClbk;
        public Action<List<string>, List<long>> MediaFilesClbk;
        public event StatusChangedHandler       StatusChanged;
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
        public bool isStopping      => status == Status.STOPPING;
        public bool isStopped       => status == Status.STOPPED;
        public bool isFailed        => status == Status.FAILED;
        public bool isOpened        => status == Status.OPENED;
        public bool isSeeking       { get; private set; }
        public bool isReady         { get; private set; }
        public bool isTorrent       => UrlType == InputType.TorrentFile || UrlType == InputType.TorrentPart;
        public bool isLive          => Duration == 0;

        public string       Url             { get; internal set; }
        public InputType    UrlType         { get; internal set; }
        public string       UrlFolder       { get { return isTorrent ? torrentStreamer.FolderComplete : (new FileInfo(Url)).DirectoryName; } }
        public string       UrlName         { get; internal set; }
        public string       SubUrl          { get; internal set; }
        
        public History      History         { get; internal set; } = new History(Path.Combine(Directory.GetCurrentDirectory(), "History"), 30);
        public bool         HistoryEnabled  { get; set; } = true;
        public int          HistoryEntries  { get { return History.MaxEntries; } set { History.MaxEntries = value; } }

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
        public long     Duration        { get { if (decoder == null) return 0; else return decoder.vStreamInfo.DurationTicks; } }
        public int      Width           { get { return decoder.vStreamInfo.Width;   } }
        public int      Height          { get { return decoder.vStreamInfo.Height;  } }
        public bool     HighQuality     { get { return decoder.opt.video.SwsHighQuality; } set { decoder.opt.video.SwsHighQuality = value; } }
        public bool     HWAccel         { get { return decoder.opt.video.HWAcceleration; } set { decoder.opt.video.HWAcceleration = value; renderer?.NewMessage(OSDMessage.Type.HardwareAcceleration); } }
        public bool     iSHWAccelSuccess{ get { return decoder.vDecoder.hwAccelSuccess; } }

        // Audio
        public bool     hasAudio        { get { return decoder.hasAudio; } }
        public bool     doAudio // Dynamic only for embedded
        { 
            get { return decoder.opt.audio.Enabled; } 
            set
            {
                if (decoder.opt.audio.Enabled == value) return;
                
                decoder.opt.audio.Enabled = value;

                if (value)
                    decoder.OpenAudio(decoder.demuxer.defaultAudioStream, isLive ? -1 : CurTime/10000);
                else
                    decoder.StopAudio();
            } 
        }
        public int      Volume          { get { return audioPlayer == null ? 0 : audioPlayer.Volume; } set { audioPlayer.SetVolume(value); renderer?.NewMessage(OSDMessage.Type.Volume); } }
        public bool     Mute            { get { return !audioPlayer.isPlaying;      } set { if (value) audioPlayer.Pause(); else audioPlayer.Play(); renderer?.NewMessage(OSDMessage.Type.Mute, Mute ? "Muted" : "Unmuted"); } }
        public bool     Mute2           { get { return audioPlayer.Mute;            } set { audioPlayer.Mute = value; renderer?.NewMessage(OSDMessage.Type.Mute, Mute2 ? "Muted" : "Unmuted"); } }
        public long     AudioExternalDelay
        {
            get { return decoder.opt.audio.DelayTicks; }

            set
            {
                if (!isReady || !decoder.hasAudio || decoder.opt.audio.DelayTicks == value) return;

                decoder.opt.audio.DelayTicks = value;
                renderer.NewMessage(OSDMessage.Type.AudioDelay);
                decoder.aDemuxer.ReSync(CurTime/10000);
            }
        }

        // Subs
        public bool     hasSubs         { get { return decoder.hasSubs; } }
        public bool     doSubs          { 
            get { return decoder.opt.subs.Enabled; } 
            set
            {
                if (decoder.opt.subs.Enabled == value) return;
                
                decoder.opt.subs.Enabled = value;
                if (CurSubId == -1) return;

                if (value)
                    OpenSubs(CurSubId);
                else
                    decoder.StopSubs();

                sFrame = null;
                renderer.ClearMessages(OSDMessage.Type.Subtitles);
            } 
        }

        public long     SubsExternalDelay
        {
            get { return decoder.opt.subs.DelayTicks; }

            set
            {
                if (!isReady || !decoder.hasSubs || decoder.opt.subs.DelayTicks == value) return;

                decoder.opt.subs.DelayTicks = value;
                renderer.NewMessage(OSDMessage.Type.SubsDelay);
                decoder.sDemuxer.ReSync(CurTime/10000);
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
        #if DEBUG
        public DownloadSubsMode     DownloadSubs    { get; set; } = DownloadSubsMode.Never;
        #else
        public DownloadSubsMode     DownloadSubs    { get; set; } = DownloadSubsMode.FilesAndTorrents;
        #endif
        public class SubAvailable
        {
            public Language      lang;
            public string        path;
            public string        pathUTF8;
            public int           streamIndex;
            public OpenSubtitles sub;
            public bool          used;

            public long          subsExternalDelay  = 0;

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
            decoder     = new DecoderContext();
            seeks       = new ConcurrentStack<SeekData>();
            seekWatch   = new Stopwatch();
        }
        public  void InitHandle(IntPtr handle, bool designMode = false)
        {
            renderer.InitHandle(handle);

            if (!designMode)
            {
                audioPlayer     = new AudioPlayer(renderer.HookControl);
                torrentStreamer = new TorrentStreamer(this);
                decoder.Init(renderer.device);
                TimeBeginPeriod(5);
                Initialize();
            }
        }
        private void Initialize(bool isSubUrl = false)
        {
            Log($"[Initializing]");

            renderer.ClearMessages();
            seeks.Clear();

            StopThreads();

            if (!isSubUrl)
            {
                torrentStreamer.Dispose();
                ytdl    = null;
                UrlName = "";
            }

            AvailableSubs       = new List<SubAvailable>();
            CurSubId            = -1;
            PrevSubId           = -1;
            CurTime             =  0;
            beforeSeeking       = Status.STOPPED;
            lastSeekMs          = Int32.MinValue;
            isReady             = false;
            isSeeking           = false;
            sFrame              = null;
            SubUrl              = null;

            decoder.opt.audio.DelayTicks    = 0;
            decoder.opt.subs.DelayTicks     = 0;

            Log($"[Initialized]");
        }
        private void InitializeEnv(int ret = 0)
        {
            if (ret != 0)
            {
                renderer.NewMessage(OSDMessage.Type.Failed, $"Failed");
                status = Status.FAILED;
                OpenFinishedClbk?.BeginInvoke(false, (SubUrl != null ? SubUrl : Url), null, null);

                return; 
            }

            Log($"[Initializing Evn]");

            if (!hasAudio)
                OpenBestAudio();

            if (hasAudio)
                audioPlayer.Initialize(decoder.opt.audio.SampleRate);

            DecoderRatio = (float)decoder.vStreamInfo.Width / (float)decoder.vStreamInfo.Height;
            renderer.FrameResized(decoder.vStreamInfo.Width, decoder.vStreamInfo.Height);

            if (UrlType == InputType.Web && decoder.vStreamInfo.DurationTicks == 0) UrlType = InputType.WebLive;

            if (HistoryEnabled && History.Add(Url, UrlType, SubUrl, UrlName))
            {
                // Existing History Entry
                History.Entry curHistory = History.GetCurrent();
                AvailableSubs = curHistory.AvailableSubs;
                decoder.opt.audio.DelayTicks = curHistory.AudioExternalDelay;

                if (AvailableSubs != null && AvailableSubs.Count > 0)
                {
                    //foreach (var sub in AvailableSubs) sub.used = false; // Reset used from history?
                    CurSubId = curHistory.CurSubId;
                    OpenSubs(curHistory.CurSubId, true);
                }
            }
            else
            {
                // Add Embedded Subs
                foreach (StreamInfo si in decoder.demuxer.streams)
                {
                    if (si.Type == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    {
                        string lang = si.Metadata.ContainsKey("language") ? si.Metadata["language"] : (si.Metadata.ContainsKey("lang") ? si.Metadata["language"] : null);
                        AvailableSubs.Add(new SubAvailable(Language.Get(lang), si.StreamIndex));
                    }
                }
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
                    FindAvailableSubs(file.Name, hash, file.Length, true);
                }
                else if (UrlType == InputType.TorrentPart   && (DownloadSubs == DownloadSubsMode.FilesAndTorrents || DownloadSubs == DownloadSubsMode.Torrents))
                {
                    FindAvailableSubs(SubUrl, Utils.ToHexadecimal(OpenSubtitles.ComputeMovieHash(torrentStreamer.Torrent.GetTorrentStream(SubUrl))), torrentStreamer.FileSize, true);
                }
                else if (UrlType == InputType.TorrentFile   && (DownloadSubs == DownloadSubsMode.FilesAndTorrents || DownloadSubs == DownloadSubsMode.Torrents))
                {
                    string hash = Utils.ToHexadecimal(OpenSubtitles.ComputeMovieHash(Path.Combine(UrlFolder, SubUrl)));
                    FindAvailableSubs(SubUrl, hash, torrentStreamer.FileSize, true);
                }
                else
                {
                    FixSortSubs();
                    if (curSubId == -1) OpenNextAvailableSub(true);
                }
            }

            if (!doSubs && AvailableSubs.Count > 0 && CurSubId == -1) CurSubId = 0;

            status  = Status.OPENED;
            isReady = true;

            //decoder.Seek(0);
            //ShowOneFrame();
            
            renderer.NewMessage(OSDMessage.Type.Open, $"Opened");
            renderer.NewMessage(OSDMessage.Type.HardwareAcceleration);
            OpenFinishedClbk?.BeginInvoke(true, (SubUrl != null ? SubUrl : Url), null, null);

            Log($"[Initialized Evn]");
        }
        private void OpenBestAudio()
        {
            // Best match based on languages (todo codecs)
            foreach (Language lang in Languages)
            {
                for (int i=0; i<decoder.demuxer.streams.Length; i++)
                {
                    var stream = decoder.demuxer.streams[i];
                    if (stream.Type == FFmpeg.AutoGen.AVMediaType.AVMEDIA_TYPE_AUDIO && Language.Get(stream.Language) == lang)
                    {
                        Log($"[Audio] Found {lang} embedded audio #{stream.StreamIndex}");
                        decoder.OpenAudio(stream.StreamIndex);
                        decoder.demuxer.defaultAudioStream = stream.StreamIndex;
                        return;
                    }
                }
            }

            // Fall back to FFmpeg's best match
            if (decoder.demuxer.defaultAudioStream != -1)
            {
                Log($"[Audio] Found default embedded audio #{decoder.demuxer.defaultAudioStream}");
                decoder.OpenAudio(decoder.demuxer.defaultAudioStream);
            }
                
        }
        private void LoadPlugins()
        {
            PluginsList.Add("Torrent Streaming", "Plugins\\BitSwarm\\BitSwarmLib.dll");
            PluginsList.Add("Web Streaming", YoutubeDL.plugin_path);

            foreach (KeyValuePair<string, string> plugin in PluginsList)
                if (File.Exists(plugin.Value)) Plugins.Add(plugin.Key);
        }
        #endregion

        #region Screaming
        private bool MediaBuffer()
        {
            if (!decoder.isRunning)
            {
                if (decoder.Finished) decoder.Seek(0);
                decoder.Play();
            }
            audioPlayer.ResetClbk();
            audioPlayer.Play();

            Log("[SCREAMER] Buffering ...");
            torrentStreamer.bitSwarmOpt.EnableBuffering = true;
            renderer.NewMessage(OSDMessage.Type.Buffering, $"Loading ...", null, 999999);

            vFrame = null;
            aFrame = null;

            bool gotAudio = !hasAudio || !doAudio;
            bool gotVideo = false;
            bool shouldStop = false;

            // Wait 1: Ensure we have enough video/audio frames
            do
            {
                if (vFrame == null && decoder.vDecoder.frames.Count != 0)
                    decoder.vDecoder.frames.TryDequeue(out vFrame);

                if (!gotAudio && aFrame == null && decoder.aDecoder.frames.Count != 0)
                    decoder.aDecoder.frames.TryDequeue(out aFrame);

                if (vFrame != null)
                {
                    if (!gotVideo && decoder.vDecoder.frames.Count > 2) gotVideo = true;

                    if (!gotAudio && aFrame != null)
                    {
                        if (vFrame.timestamp - aFrame.timestamp > AudioPlayer.NAUDIO_DELAY_MS * (long)10000)
                            decoder.aDecoder.frames.TryDequeue(out aFrame);
                        else if (decoder.aDecoder.frames.Count > 5)
                            gotAudio = true;
                    }
                }

                if (!isPlaying || decoder.Finished)
                    shouldStop = true;
                else
                {
                    if (!decoder.vDecoder.isPlaying) { Log("[SCREAMER] Video Exhausted"); shouldStop= true; }
                    if (!decoder.aDecoder.isPlaying) { Log("[SCREAMER] Audio Exhausted"); gotAudio  = true; }
                }

                Thread.Sleep(10);

            } while (!shouldStop && (!gotVideo || !gotAudio));

            if (shouldStop)     { Log("[SCREAMER] Stopped"); return false; }
            if (vFrame == null) { Log("[SCREAMER] [ERROR] No Frames!"); return false; }

            // Wait 1: Ensure we have enough buffering packets to play (mainly for network streams)
            while (decoder.vDecoder.packets.Count < decoder.opt.demuxer.MinQueueSize && isPlaying && !decoder.Finished) Thread.Sleep(15);

            renderer.ClearMessages(OSDMessage.Type.Buffering);
            torrentStreamer.bitSwarmOpt.EnableBuffering = false;
            Log("[SCREAMER] Buffering Done");

            if (sFrame == null) decoder.sDecoder.frames.TryDequeue(out sFrame);
            SeekTime        = -1;

            if (aFrame != null && aFrame.timestamp < vFrame.timestamp) 
                videoStartTicks = Math.Max(aFrame.timestamp, vFrame.timestamp - ((long)10000 * AudioPlayer.NAUDIO_DELAY_MS));
            else
                videoStartTicks = vFrame.timestamp;

            startedAtTicks  = DateTime.UtcNow.Ticks;
            CurTime         = videoStartTicks;

            Log($"[SCREAMER] Started -> {Utils.TicksToTime(videoStartTicks)} | [V: {Utils.TicksToTime(vFrame.timestamp)}]" + (aFrame == null ? "" : $" [A: {Utils.TicksToTime(aFrame.timestamp)}]"));

            return true;
        }    
        private void Screamer()
        {
            long    elapsedTicks;
            int     vDistanceMs;
            int     aDistanceMs;
            int     sDistanceMs;
            int     sleepMs;

            if (!MediaBuffer()) return;

            while (isPlaying)
            {
                if (vFrame == null)
                {
                    if (decoder.Finished) break;

                    Log("[SCREAMER] Restarting ...");
                    aFrame = null;
                    Thread.Sleep(10);
                    if (!MediaBuffer()) return;
                }

                if (aFrame == null) decoder.aDecoder.frames.TryDequeue(out aFrame);
                if (sFrame == null) decoder.sDecoder.frames.TryDequeue(out sFrame);

                elapsedTicks    = videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks);
                vDistanceMs     = (int) (((vFrame.timestamp) - elapsedTicks) / 10000);
                aDistanceMs     = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                sDistanceMs     = sFrame != null ? (int) ((sFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                sleepMs         = Math.Min(vDistanceMs, aDistanceMs) - 1;

                if (sleepMs < 0) sleepMs = 0;
                if (sleepMs > 2)
                {
                    if (sleepMs > 1000)
                    {   // It will not allowed uncommon formats with slow frame rates to play (maybe check if fps = 1? means dynamic fps?)
                        Log("[SCREAMER] Restarting ... (HLS?) | + " + Utils.TicksToTime(sleepMs * (long)10000));
                        ClearVideoFrame(vFrame); vFrame = null; aFrame = null;
                        Thread.Sleep(10);
                        MediaBuffer();
                        continue; 
                    }

                    CurTime = elapsedTicks;
                    Thread.Sleep(sleepMs);
                }

                if (aFrame != null) // Should use different thread for better accurancy (renderer might delay it on high fps) | also on high offset we will have silence between samples
                {
                    if (Math.Abs(aDistanceMs - sleepMs) <= 10)
                    {
                        //Log($"[A] Presenting {Utils.TicksToTime(aFrame.timestamp)}");
                        audioPlayer.FrameClbk(aFrame.audioData, 0, aFrame.audioData.Length);
                        decoder.aDecoder.frames.TryDequeue(out aFrame);
                    }
                    else if (aDistanceMs < -10) // Will be transfered back to decoder to drop invalid timestamps
                    {
                        Log("-=-=-=-=-=-=");
                        for (int i=0; i<50; i++)
                        {
                            Log($"aDistanceMs 2 |-> {aDistanceMs}");
                            decoder.aDecoder.frames.TryDequeue(out aFrame);
                            aDistanceMs = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                            if (aDistanceMs > 0) break;
                        }
                    }
                }

                if (Math.Abs(vDistanceMs - sleepMs) <= 2)
                {
                    //Log($"[V] Presenting {Utils.TicksToTime(vFrame.timestamp)}");
                    renderer.PresentFrame(vFrame);
                    decoder.vDecoder.frames.TryDequeue(out vFrame);
                }
                else if (vDistanceMs < -2)
                {
                    ClearVideoFrame(vFrame);
                    decoder.vDecoder.frames.TryDequeue(out vFrame);
                    Log($"vDistanceMs 2 |-> {vDistanceMs}");
                }

                if (sFrame != null)
                {
                    if (Math.Abs(sDistanceMs - sleepMs) < 30) {
                        renderer.NewMessage(OSDMessage.Type.Subtitles, sFrame.text, sFrame.subStyles, sFrame.duration);
                        decoder.sDecoder.frames.TryDequeue(out sFrame);
                    }
                    else if (sDistanceMs < -30)
                    {
                        if (sFrame.duration + sDistanceMs > 0)
                        {
                            renderer.NewMessage(OSDMessage.Type.Subtitles, sFrame.text, sFrame.subStyles, sFrame.duration + sDistanceMs);
                            decoder.sDecoder.frames.TryDequeue(out sFrame);
                        }
                        else
                        {
                            Log($"sDistanceMs 2 |-> {sDistanceMs}");
                            decoder.sDecoder.frames.TryDequeue(out sFrame);
                        }
                    }
                }
            }

            Log($"[SCREAMER] Finished -> {Utils.TicksToTime(CurTime)}");
        }
        #endregion

        #region Main Actions
        public void Open(string url, bool isSubUrl = false)
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
            if (Utils.SubsExts.Contains(ext)) { if (isReady) OpenSubs(url); return; }

            long prevTime = decoder.Finished ? -1 : CurTime;
            Initialize(isSubUrl);
            int ret = -1;

            lock (lockOpening)
            {
                Log($"Opening {url}");
                renderer.NewMessage(OSDMessage.Type.Open, $"Opening {url}", null, 999999);
                status = Status.OPENING;

                openVideoThread = new Thread(() =>
                {
                    if (isSubUrl)
                    {
                        SubUrl  = url;

                        if (isTorrent)
                        {
                            UrlName = url;
                            InitializeEnv(torrentStreamer.OpenStream(url));
                        }
                        else if (UrlType == InputType.Web || UrlType == InputType.WebLive)
                        {
                            int subUrlIndex = -1;
                            for (int i=0; i<ytdl.formats.Count; i++)
                                if (ytdl.formats[i].url == url) { subUrlIndex=i; break; }

                            YoutubeDL.ParseHeaders(ytdl.formats[subUrlIndex].http_headers, decoder);
                            ret = decoder.Open(SubUrl, true);
                            if (ret != 0) { InitializeEnv(ret); return; }

                            //OpenBestAudio();

                            if (!hasAudio)
                            {
                                var aFmt = ytdl.GetAudioOnly();
                                if (aFmt != null) { YoutubeDL.ParseHeaders(aFmt.http_headers, decoder); decoder.OpenAudio(aFmt.url); }
                            }

                            if (!isLive && prevTime != -1) decoder.Seek(prevTime/10000, true);

                            InitializeEnv();
                        }
                    }
                    else
                    {
                        Url = url;

                        // Torrents
                        if (BitSwarm.ValidateInput(url) != BitSwarm.InputType.Unkown)
                        {
                            if (MediaFilesClbk == null || !Plugins.Contains("Torrent Streaming"))
                            {
                                renderer.NewMessage(OSDMessage.Type.Failed, $"Failed: Torrents are disabled"); 
                                status = Status.FAILED;
                                //OpenFinishedClbk?.BeginInvoke(false, Url, null, null);
                                return; 
                            }

                            UrlType = InputType.TorrentPart;

                            if (torrentStreamer.Open(url) != 0)
                            {
                                renderer.NewMessage(OSDMessage.Type.Failed, $"Failed"); 
                                status = Status.FAILED;
                                //OpenFinishedClbk?.BeginInvoke(false, Url, null, null);
                                
                            }
                        }

                        // Youtube-DL | HTTP(s)
                        else if (Plugins.Contains("Web Streaming") && (scheme.ToLower() == "http" || scheme.ToLower() == "https"))
                        {
                            UrlType = InputType.Web;
                            Uri uri = new Uri(url);

                            string url2 = url;

                            // Remove list/channels currently from youtube
                            if (System.Text.RegularExpressions.Regex.IsMatch(uri.DnsSafeHost, @"\.youtube\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            {
                                var t2 = System.Web.HttpUtility.ParseQueryString(uri.Query);
                                url2 = uri.Scheme + "://" + uri.Host + uri.AbsolutePath + "?v=" + t2["v"];
                            }

                            ytdl = YoutubeDL.New(url2, ref decoder.interrupt);
                            if (ytdl == null) { InitializeEnv(-1); return; }
                            var vFmt = ytdl.GetBestMatch(renderer.HookControl);
                            if (vFmt == null) { InitializeEnv(-1); return; }

                            UrlName = ytdl.title;
                            SubUrl  = vFmt.url;
                            YoutubeDL.ParseHeaders(vFmt.http_headers, decoder);
                            ret = decoder.Open(SubUrl, true);
                            if (ret != 0) { InitializeEnv(ret); return; }

                            //OpenBestAudio();

                            if (!hasAudio)
                            {
                                var aFmt = ytdl.GetAudioOnly();
                                if (aFmt != null) { YoutubeDL.ParseHeaders(aFmt.http_headers, decoder); decoder.OpenAudio(aFmt.url); }
                            }

                            InitializeEnv();
                        }

                        // Files & Rest Protocols
                        else
                        {
                            Uri uri = null;
                            try { uri = new Uri(url); } catch (Exception) { }

                            if (uri != null && uri.IsFile)
                            {
                                UrlType = InputType.File;
                                UrlName = File.Exists(Url) ? (new FileInfo(Url)).Name : Url;

                                // Testing avIOContext
                                //InitializeEnv(decoder.Open(new FileStream(url, FileMode.Open, FileAccess.Read))); return;
                            }
                            else
                            {
                                UrlType = InputType.Other;
                                UrlName = Url;
                            }

                            InitializeEnv(decoder.Open(url));
                        }
                    }
                });
                openVideoThread.IsBackground = true;
                openVideoThread.Start();
            }
        }
        public void OpenAudio(int streamIndex)
        {
            lock (lockOpening)
            {
                Utils.EnsureThreadDone(openAudioThread);
                openAudioThread = new Thread(() =>
                {
                    if (decoder.OpenAudio(streamIndex) != 0) return;

                    bool isPlaying = this.isPlaying;
                    audioPlayer.Initialize(decoder.opt.audio.SampleRate);
                    Pause();

                    if (isLive)
                        decoder.vDecoder.Flush();
                    else
                        decoder.Seek(CurTime/10000);
                    decoder.demuxer.defaultAudioStream = streamIndex;
                    audioPlayer.Initialize(decoder.opt.audio.SampleRate);
                    if (isPlaying) Play();
                });
                openAudioThread.IsBackground = true;
                openAudioThread.Start();
            }
        }
        public void OpenAudio(string url, bool isSubUrl = false)
        {
            if (!(isSubUrl && (UrlType == InputType.Web || UrlType == InputType.WebLive))) return;
            
            lock (lockOpening)
            {
                Utils.EnsureThreadDone(openAudioThread);
                openAudioThread = new Thread(() =>
                {
                    if (ytdl != null && ytdl.formats != null && isSubUrl && (UrlType == InputType.Web || UrlType == InputType.WebLive))
                    {
                        int subUrlIndex = -1;
                        for (int i=0; i<ytdl.formats.Count; i++)
                            if (ytdl.formats[i].url == url) { subUrlIndex=i; break; }
                        if (subUrlIndex == -1) return;

                        YoutubeDL.ParseHeaders(ytdl.formats[subUrlIndex].http_headers, decoder);
                        if (decoder.OpenAudio(url) != 0) return;

                        audioPlayer.Initialize(decoder.opt.audio.SampleRate);

                        bool isPlaying = this.isPlaying;
                        Pause();
                        decoder.Pause();

                        if (isLive)
                            decoder.vDecoder.Flush();
                        else
                            decoder.Seek(CurTime/10000);

                        if (isPlaying) Play();
                    }
                });
                openAudioThread.IsBackground = true;
                openAudioThread.Start();
            }
        }
        public void OpenVideo(int streamIndex)
        {
            lock (lockOpening)
            {
                Utils.EnsureThreadDone(openVideoThread);
                openVideoThread = new Thread(() =>
                {
                    bool isPlaying = this.isPlaying;
                    Pause();
                    decoder.Pause();
                    if (decoder.OpenVideo(streamIndex, true) != 0) return;
                    DecoderRatio = (float)decoder.vStreamInfo.Width / (float)decoder.vStreamInfo.Height;
                    renderer.FrameResized(decoder.vStreamInfo.Width, decoder.vStreamInfo.Height);
                    if (isLive)
                        decoder.aDecoder.Flush();
                    else
                        decoder.Seek(CurTime/10000);
                    if (isPlaying) Play();
                });
                openVideoThread.IsBackground = true;
                openVideoThread.Start();
            }
        }
        public void Play(bool todoPlay2 = false)
        { 
            if (isSeeking && !todoPlay2)
            {
                if (beforeSeeking == Status.PLAYING) beforeSeeking = Status.PAUSED; else beforeSeeking = Status.PLAYING;
                StatusChanged(this, new StatusChangedArgs(beforeSeeking));
                return;
            }

            if (!isReady || decoder.isRunning || isPlaying) { StatusChanged?.Invoke(this, new StatusChangedArgs(status)); return; }

            Interlocked.Exchange(ref SeekTime, -1);
            renderer.ClearMessages(OSDMessage.Type.Paused);
            if (beforeSeeking != Status.PLAYING) { renderer.NewMessage(OSDMessage.Type.Play, "Play"); beforeSeeking = Status.PLAYING; }
            PauseScreamer();
            status = Status.PLAYING;

            if (decoder.Finished) decoder.Seek(0);
            decoder.Play();

            StatusChanged?.Invoke(this, new StatusChangedArgs(status));

            screamer = new Thread(() =>
            {
                try
                {
                    TimeBeginPeriod(1);
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED);
                    Screamer();

                } catch (Exception e) { Log(e.Message + " - " + e.StackTrace); }

                finally
                {
                    ClearVideoFrame(vFrame); vFrame = null;
                    TimeEndPeriod(1);
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                    status = Status.STOPPED;
                    if (!isSeeking || decoder.Finished) StatusChanged?.Invoke(this, new StatusChangedArgs(status));
                }
            });
            screamer.SetApartmentState(ApartmentState.STA);
            screamer.Start();
        }

        public void Seek(int ms, bool priority = false, bool foreward = false)
        {
            if (!isReady || isLive) return;

            SeekData seekData2 = new SeekData(ms, priority, foreward);
            SeekTime = seekData2.ms * (long)10000;
            seeks.Push(seekData2);

            lock (lockSeek) { if (isSeeking) return; isSeeking = true; }
            //if (seekThread != null && seekThread.IsAlive) seekThread.Abort();

            seekThread = new Thread(() =>
            {
			    try
			    {
                    Log("Seek Thread Started!");
                    TimeBeginPeriod(1);

                    SeekData seekData;
                    bool     shouldPlay = false;
                    int      cancelOffsetMs = 1500; // lastSeekMs +-
                    int      stayAliveMs    = 5000; // Wait for more seek requests
                    int      networkAbortMs = isTorrent ? 300 : 3000; // Time to wait before aborting the seek request (fast abort for those which open fast - local files)

                    while(true)
                    {
                        bool seekPop = seeks.TryPop(out seekData); seeks.Clear();

                        // Ignore | Wait for more seeks before kill thread (to avoid stop/start)
                        if (!seekPop || (seekData.ms - cancelOffsetMs <= lastSeekMs && seekData.ms + cancelOffsetMs >= lastSeekMs))
                        {
                            if (seekPop) Log("Seek Ignored 1 " + Utils.TicksToTime(seekData.ms * (long)10000));

                            if (seeks.Count == 0)
                            {
                                //Log("Seek Waits | " + shouldPlay);
                                SeekTime = -1;
                                seekWatch.Restart();
                                Status beforeSeekingSaved = beforeSeeking;
                                if (beforeSeeking == Status.PLAYING && shouldPlay) { shouldPlay = false; Play(true); beforeSeekingSaved = Status.PLAYING; }

                                do
                                {
                                    if (beforeSeekingSaved != beforeSeeking)
                                    {
                                        if (beforeSeeking == Status.PLAYING)
                                            Play(true);
                                        else
                                        {
                                            Log("Seek Pause All Start");
                                            status = Status.STOPPING;
                                            Utils.EnsureThreadDone(screamer);
                                            decoder.Pause();
                                            Log("Seek Pause All Done");
                                        }

                                        beforeSeekingSaved = beforeSeeking;
                                    }

                                    if (seekWatch.ElapsedMilliseconds > stayAliveMs) { Log("Seek Exhausted"); return; }
                                    Render();
                                    Thread.Sleep(35);
                                } while (seeks.Count == 0);
                                seekWatch.Stop();
                            }

                            continue;
                        }

                        // Seek Preperation
                        if (isPlaying) shouldPlay = true;
                        status = Status.STOPPING;
                        decoder.status = MediaFramework.Status.PAUSE;
                        decoder.interrupt = 1;
                        Utils.EnsureThreadDone(screamer, 250, 3);
                        renderer.NewMessage(OSDMessage.Type.Buffering, $"Seeking ...", null, 999999);
                        decoder.interrupt = 0;

                        // Direct Seek | Abortable Seek (Local vs Network)
                        bool seekFailed = false;

                        if (UrlType != InputType.File && UrlType != InputType.TorrentFile) // Only "Slow" Network Streams (Web/RTSP/Torrent etc.)
                        {
                            //Thread.Sleep(networkDecideMs);
                            if (seeks.Count != 0) { /*Log("Seek Ignores");*/ continue; }
                            lastSeekMs = seekData.ms;

                            int decStatus = -1;
                            Thread decThread = new Thread(() =>
                            {
                                torrentStreamer.bitSwarmOpt.EnableBuffering = true;
                                decStatus = 0;

                                try { if (decoder.Seek(seekData.ms, seekData.foreward) < 0) seekFailed = true; }
                                catch (Exception) 
                                    { torrentStreamer.bitSwarmOpt.EnableBuffering = false; decStatus = 2; return; }

                                torrentStreamer.bitSwarmOpt.EnableBuffering = false;
                                decStatus = 1;
                            });
                            decThread.IsBackground = true;
                            decThread.Start();
                            seekWatch.Restart();

                            while (decStatus < 1)
                            {
                                if (seekWatch.ElapsedMilliseconds > networkAbortMs)
                                {
                                    seekPop = seeks.TryPeek(out seekData);

                                    if (!seekPop || (seekData.ms - cancelOffsetMs <= lastSeekMs && seekData.ms + cancelOffsetMs >= lastSeekMs))
                                    {
                                        //if (seekPop) Log("Seek Ignored 2 " + Utils.TicksToTime(seekData.ms * (long)10000));
                                        seeks.Clear();
                                    }
                                    else
                                    {
                                        seekWatch.Stop();

                                        Log("Seek Abort " + seekWatch.ElapsedMilliseconds);
                                        if (isTorrent && decoder.demuxer.ioStream != null) ((BitSwarmLib.BEP.TorrentStream) decoder.demuxer.ioStream).Cancel();

                                        decThread.Abort();
                                        while (decStatus < 1) { Render(); Thread.Sleep(20); }

                                        // Only No-index Seek Entries? TBR | Possible ReOpen in new thread
                                        //if ((UrlType == InputType.TorrentPart || UrlType == InputType.Other) && decoder.demuxer.fmtName != "QuickTime / MOV")
                                        if (!(UrlType == InputType.TorrentPart && decoder.demuxer.fmtName == "QuickTime / MOV"))
                                        {
                                            Log("Seek Crashed - Reopening");
                                            decoder.ReOpen();
                                        }
                                            
                                        Log("Seek Abort Done");
                                        if (torrentStreamer.bitSwarm != null) torrentStreamer.bitSwarm.FocusAreInUse = false; // Reset after abort thread
                                        break;
                                    }

                                    seekWatch.Restart();
                                }

                                if (decStatus < 1) { Render(); Thread.Sleep(20); } else break;
                            }

                            seekWatch.Stop();
                            if (decStatus == 2) continue;
                        }
                        else
                        {
                            lastSeekMs = seekData.ms;
                            if (decoder.Seek(seekData.ms, seekData.foreward) < 0) seekFailed = true;
                        }

                        if (!seekFailed)
                        {
                            ShowOneFrame();
                            shouldPlay = true;
                        }
                    }
                }
                catch (Exception) { }
                finally
                {
                    TimeEndPeriod(1);
                    SeekTime = -1;
                    lastSeekMs = Int32.MinValue;
                    seekWatch.Stop();
                    lock (lockSeek) isSeeking = false;
                    Log("Seek Thread Done!");
                }
            });
	        seekThread.Start();
        }
        public void Pause()
        {
            audioPlayer.ResetClbk();

            if (isSeeking)
            {
                if (beforeSeeking == Status.PLAYING) beforeSeeking = Status.PAUSED; else beforeSeeking = Status.PLAYING;
                StatusChanged(this, new StatusChangedArgs(beforeSeeking));
                return;
            }

            if (torrentStreamer.bitSwarmOpt.EnableBuffering) status = Status.STOPPING; // TODO force stop

            if (!isReady || !decoder.isRunning || !isPlaying) StatusChanged(this, new StatusChangedArgs(status));

            renderer.ClearMessages(OSDMessage.Type.Play);
            renderer.NewMessage(OSDMessage.Type.Paused, "Paused");

            PauseScreamer();
            status          = Status.PAUSED;
            beforeSeeking   = Status.PAUSED;
        }
        public void Close()
        {
            PauseScreamer();
            Initialize();

            if (decoder     != null) decoder.Stop();
            if (renderer    != null) renderer.Dispose();
            if (audioPlayer != null) audioPlayer.Close();
        }
        #endregion

        #region Subtitles
        public void FindAvailableSubs(string filename, string hash, long length, bool checkDoSubs = false)
        {
            if (Languages.Count < 1) return;
            Utils.EnsureThreadDone(openSubsThread);

            openSubsThread = new Thread(() =>
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

                if (subs.Count > 0)
                {
                    for (int i=0; i<subs.Count; i++)
                        AvailableSubs.Add(new SubAvailable(Language.Get(subs[i].LanguageName), subs[i]));

                    FixSortSubs();
                    //SubtitlesAvailable?.Invoke(this, EventArgs.Empty);
                    OpenNextAvailableSub(checkDoSubs);
                }
            });
            openSubsThread.SetApartmentState(ApartmentState.STA);
            openSubsThread.Start();
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
        public void OpenNextAvailableSub(bool checkDoSubs = false)
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
                        OpenSubs(i, checkDoSubs);
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
                    OpenSubs(i, checkDoSubs);
                    return;
                }
            }

            // Reset used and start from the beggining
            if (allused && AvailableSubs.Count > 0 && Languages.Count > 0)
            {
                for (int i=0; i<AvailableSubs.Count; i++)
                    AvailableSubs[i].used = false;

                OpenNextAvailableSub(checkDoSubs);
            }
        }
        public void OpenSubs(int availableIndex, bool checkDoSubs = false)
        {
            if (checkDoSubs && (!doSubs || availableIndex == -1)) return;

            SubAvailable sub = AvailableSubs[availableIndex];
            decoder.opt.subs.DelayTicks = sub.subsExternalDelay;
            decoder.opt.subs.Enabled    = true;

            sFrame = null;
            renderer.ClearMessages(OSDMessage.Type.Subtitles);

            if (sub.streamIndex > 0)
                decoder.OpenSubs(sub.streamIndex, isLive ? -1 : CurTime/10000);
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

                decoder.OpenSubs(sub.pathUTF8, CurTime/10000);
            }

            CurSubId = availableIndex;
            sub.used = true;
            sFrame   = null;
            renderer.ClearMessages(OSDMessage.Type.Subtitles);
            History.Update(AvailableSubs, CurSubId);
        }
        private void OpenSubs(string url, bool checkDoSubs = false)
        {
            if (checkDoSubs && doSubs) return;

            for (int i=0; i<AvailableSubs.Count; i++)
                if ((AvailableSubs[i].path != null && AvailableSubs[i].path.ToLower() == url.ToLower()) || (AvailableSubs[i].pathUTF8 != null && AvailableSubs[i].pathUTF8.ToLower() == url.ToLower()))
                {
                    OpenSubs(i);
                    return;
                }

            SubAvailable sub = new SubAvailable(null, url);
            decoder.opt.subs.DelayTicks = 0;
            decoder.opt.subs.Enabled    = true;

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

            sFrame = null;
            renderer.ClearMessages(OSDMessage.Type.Subtitles);
            decoder.OpenSubs(sub.pathUTF8, CurTime/10000);
            sFrame = null;
            renderer.ClearMessages(OSDMessage.Type.Subtitles);

            AvailableSubs.Add(sub);
            CurSubId = AvailableSubs.Count - 1;

            History.Update(AvailableSubs, CurSubId);
        }
        #endregion

        #region Misc
        private void ShowOneFrame()
        {
            sFrame = null;
            renderer.ClearMessages(OSDMessage.Type.Subtitles);

            if (decoder.vDecoder.frames.Count > 0)
            {
                MediaFrame vFrame = null;
                decoder.vDecoder.frames.TryDequeue(out vFrame);
                CurTime     = vFrame.timestamp;
                //SeekTime    = -1;
                renderer.PresentFrame(vFrame);
            }
            return;
        }
        public void Render() { renderer.PresentFrame(null); }
        private void PauseScreamer()
        {
            status = Status.STOPPING;
            Utils.EnsureThreadDone(screamer);
            decoder.Pause();
            if (hasAudio) audioPlayer.ResetClbk();
            status = Status.STOPPED;
        }
        private void StopThreads()
        {
            PauseScreamer();
            decoder.interrupt = 1;
            if (seekThread      != null && seekThread.IsAlive)      seekThread.Abort();
            decoder.interrupt = 1;
            if (openSubsThread  != null && openSubsThread.IsAlive)  openSubsThread.Abort();
            decoder.interrupt = 1;
            if (openAudioThread != null && openAudioThread.IsAlive) openAudioThread.Abort();
            decoder.interrupt = 1;
            //if (openVideoThread != null && openVideoThread.IsAlive) {  while (openVideoThread.IsAlive) Thread.Sleep(20); decoder.interrupt = 0; }

            int escape = 0;
            while ((openVideoThread != null && openVideoThread.IsAlive) || (seekThread != null && seekThread.IsAlive) || (openSubsThread != null && openSubsThread.IsAlive) || (openAudioThread != null && openAudioThread.IsAlive))
            {
                escape++;
                Thread.Sleep(10);
                if (escape > 200) break;
            }
            decoder.interrupt = 0;
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

        private void Log(string msg) { if (verbosity > 0) Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] {msg}"); }

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