using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Reflection;
using System.Windows;

using Newtonsoft.Json; // Required for Plugins (PreLoad here)

using FlyleafLib.Controls;
using FlyleafLib.MediaFramework;
using FlyleafLib.MediaRenderer;
using FlyleafLib.Plugins;
using FlyleafLib.Plugins.MediaStream;

using static FlyleafLib.Utils;
using static FlyleafLib.Utils.NativeMethods;
using FlyleafLib.Controls.WPF;

namespace FlyleafLib.MediaPlayer
{
    public unsafe class Player : NotifyPropertyChanged, IDisposable
    {
        #region Properties

        /// <summary>
        /// VideoView wil be set once from the Binding OneWayToSource and then we can Initialize our ViewModel (Normally, this should be called only once)
        /// IsFullScreen/FullScreen/NormalScreen
        /// </summary>
        public VideoView    VideoView       { get ; set; }

        /// <summary>
        /// Flyleaf Control (WinForms)
        /// </summary>
        public Flyleaf      Control         { get => _Control; set { if (value.Handle == _Control?.Handle) return; InitializeControl1(_Control, value); } }
        Flyleaf _Control;

        /// <summary>
        /// Foreground window with overlay content (to catch events and resolve airspace issues)
        /// </summary>
        public Window       WindowFront     => VideoView.WindowFront;

        /// <summary>
        /// Background/Main window
        /// </summary>
        public Window       WindowBack      => VideoView.WindowFront.WindowBack;

        /// <summary>
        /// Player's Configuration
        /// </summary>
        public Config       Config          { get => _Config; set { _Config = value; _Config.SetPlayer(this); } }
        Config _Config;

        /// <summary>
        /// Player's Session
        /// </summary>
        public Session      Session         { get; private set; }

        /// <summary>
        /// Player's Incremental Unique Id
        /// </summary>
        public int          PlayerId        { get; private set; }

        /// <summary>
        /// Player's Name
        /// </summary>
        public string       PlayerName      { get; private set; } = "";

        public bool         IsSeeking       { get; private set; }
        public bool         IsPlaying       => Status == Status.Playing;

        /// <summary>
        /// Whether the video has ended
        /// </summary>
        public bool         HasEnded        => decoder.vDecoder.status == Status.Ended;

        /// <summary>
        /// Whether the video has audio and it is configured
        /// </summary>
        public bool         HasAudio        => decoder.aDecoder.status != Status.None;

        /// <summary>
        /// Whether the video has subtitles and it is configured
        /// </summary>
        public bool         HasSubs         => decoder.sDecoder.status != Status.None;
        
        /// <summary>
        /// Dictionary of available Plugins and their AVS Streams
        /// </summary>
        public Dictionary<string, PluginBase> Plugins {get; private set; }  = new Dictionary<string, PluginBase>();

        /// <summary>
        /// Player's Status
        /// </summary>
        public Status Status { get => _Status; private set => Set(ref _Status, value); }
        Status _Status = Status.Stopped;
        #endregion

        #region Properties Internal
        static int curPlayerId = 0;

        public AudioPlayer      audioPlayer;
        public Renderer         renderer;
        public DecoderContext   decoder;

        Thread tOpenVideo, tOpenAudio, tOpenSubs, tSeek, tPlay;
        object lockOpen = new object();
        object lockSeek = new object();

        bool        playAfterSeek;
        Stopwatch   seekWatch;
        int         lastSeekMs;
        ConcurrentStack<SeekData> seeks;

        class SeekData
        {
            public int  ms;
            public bool foreward;

            public SeekData(int ms, bool foreward) { this.ms = ms; this.foreward = foreward; }
        }

        MediaFrame  aFrame, sFramePrev, vFrame;
        internal MediaFrame sFrame;
        long        startedAtTicks;
        long        videoStartTicks;

        public List<PluginBase> audioPlugins        { get; private set; }
        public List<PluginBase> videoPlugins        { get; private set; }
        public List<PluginBase> subtitlePlugins     { get; private set; }

        public PluginBase       curAudioPlugin      { get; private set; }
        public PluginBase       curVideoPlugin      { get; private set; }
        public PluginBase       curSubtitlePlugin   { get; private set; }
        #endregion

        #region Initialize / Dispose
		public Player(Config config = null)
        {
            Log("Constructor");
            Config = config == null ? new Config() : config; // Possible clone for multiple players?
        }

        private void InitializeControl1(Flyleaf oldValue, Flyleaf newValue)
        {
            Interlocked.Increment(ref curPlayerId);
            PlayerId = curPlayerId; 
            Log("[Starting]");

            Session         = new Session(this);
            seeks           = new ConcurrentStack<SeekData>();
            seekWatch       = new Stopwatch();
            audioPlayer     = new AudioPlayer(this);
            decoder         = new DecoderContext(this, audioPlayer);

            Master.Players.Add(this);

            if (newValue.Handle != null)
                InitializeControl2(newValue);
            else
                newValue.HandleCreated += (o, e) => { InitializeControl2(newValue); };
        }
        private void InitializeControl2(Flyleaf newValue)
        {
            _Control = newValue;
            _Control.Player = this;
            renderer = new Renderer(this);
            decoder.Init(renderer, Config);
            renderer.PresentFrame();
            LoadPlugins();
            Log("[Started]");
        }
        private void LoadPlugins()
        {
            // Load Plugins
            foreach (var type in Master.Plugins)
                try
                {
                    var plugin = (PluginBase) Activator.CreateInstance(type);
                    plugin.Player = this;
                    plugin.OnLoad();
                    Plugins.Add(plugin.PluginName, plugin);
                } catch (Exception e) { Log($"[Plugins] [Error] Failed to load plugin ... ({e.Message} {Utils.GetRecInnerException(e)}"); }

            // Fix Priorities etc
            videoPlugins    = new List<PluginBase>();
            audioPlugins    = new List<PluginBase>();
            subtitlePlugins = new List<PluginBase>();

            audioPlugins.Add(Plugins["Default"]);
            subtitlePlugins.Add(Plugins["Default"]);
            foreach(var plugin in Plugins)
            {
                if (plugin.Key != "Default" && plugin.Value is IPluginVideo)      videoPlugins.Add(plugin.Value);
                if (plugin.Key != "Default" && plugin.Value is IPluginAudio)      audioPlugins.Add(plugin.Value);
                if (plugin.Key != "Default" && plugin.Value is IPluginSubtitles)  subtitlePlugins.Add(plugin.Value);
            }
            videoPlugins.Add(Plugins["Default"]);
        }

        private void Initialize()
        {
            var t1 = Environment.Version;
            Log($"[Initializing]");

            // Prevent Seek Process
            Session.CanPlay = false;
            seeks.Clear();
            EnsureThreadDone(tSeek);

            // Stop Screamer / MediaBuffer
            Status = Status.Stopped;
            EnsureThreadDone(tPlay);

            // Inform Plugins (OnInitializing)
            foreach(var plugin in Plugins.Values) plugin.OnInitializing();
            
            // Reset Rest
            decoder.Stop();
            decoder.interrupt = 1;
            EnsureThreadDone(tOpenVideo);
            EnsureThreadDone(tOpenAudio);
            EnsureThreadDone(tOpenSubs);
            decoder.interrupt = 0;
            Session.Reset();
            curVideoPlugin = null;
            curAudioPlugin = null;

            // Inform Plugins (OnInitialized)
            foreach(var plugin in Plugins.Values) plugin.OnInitialized();
            
            //InitializeAudio();
            //InitializeSubs();
            Log($"[Initialized]");
        }
        private void InitializeSwitch()
        {
            Log($"[Initializing Switch]");
            // Prevent Seek Process
            Session.CanPlay = false;
            seeks.Clear();
            EnsureThreadDone(tSeek);

            // Stop Screamer / MediaBuffer
            Status = Status.Stopped;
            EnsureThreadDone(tPlay);

            // Inform Plugins (OnInitializing)
            foreach(var plugin in Plugins.Values) plugin.OnInitializingSwitch();

            // Reset Rest
            decoder.Pause();
            decoder.interrupt = 1;
            EnsureThreadDone(tOpenVideo);
            EnsureThreadDone(tOpenAudio);
            EnsureThreadDone(tOpenSubs);
            decoder.interrupt = 0;

            // Inform Plugins (OnInitialized)
            foreach(var plugin in Plugins.Values) plugin.OnInitializedSwitch();
            Session.Reset(true);

            if (Session.CurAudioStream != null) Session.CurAudioStream.InUse = false;
            if (Session.CurVideoStream != null) Session.CurVideoStream.InUse = false;

            Log($"[Initialized Switch]");
        }
        private void InitializeEnv()
        {
            Log($"[Initializing Env]");

            Status = Status.Paused;
            Session.CanPlay = true;
            Session.Movie.Duration = decoder.vDecoder.info.Duration;

            OnOpenCompleted(MediaType.Video, true);
            Log($"[Initialized  Env]");
        }

        /// <summary>
        /// Access this only from Plugins
        /// </summary>
        public void OpenFailed()
        {
            Initialize();
            Status = Status.Failed;
            OnOpenCompleted(MediaType.Video, false);

            return;
        }
        #endregion

        #region Open / Close
        /// <summary>
        /// Opens a new external Video or Subtitle url
        /// </summary>
        /// <param name="url"></param>
        public void Open(string url)
        {
            if (Utils.SubsExts.Contains(Utils.GetUrlExtention(url)))
            {
                if (Config.subs.Enabled == false) Config.subs.SetEnabled();
                Open(((IPluginExternal)Plugins["DefaultExternal"]).OpenSubtitles(url));
                return;
            }

            Initialize();

            tOpenVideo = new Thread(() =>
            {
                Session.InitialUrl = url;

                foreach(var plugin in videoPlugins)
                {
                    var res = ((IPluginVideo)plugin).OpenVideo();
                    if (res == null) continue;

                    if (res.forceFailure)   { curVideoPlugin = plugin; OpenFailed(); return; }
                    if (res.runAsync)       { curVideoPlugin = plugin; Log($"[VideoPlugin*] {curVideoPlugin?.PluginName}"); if (!Session.IsPlaylist) Session.SingleMovie.Url = Session.InitialUrl; return; }

                    if (res.stream == null) continue;
                    curVideoPlugin = plugin;
                    if (!Session.IsPlaylist) Session.SingleMovie.Url = Session.InitialUrl;
                    OpenVideo(res.stream);
                    break;
                }
            });
            tOpenVideo.Name = "OpenVideo"; tOpenVideo.IsBackground = true; tOpenVideo.Start();
        }
        
        /// <summary>
        /// Opens a new external Video from a custom System.IO.Stream
        /// </summary>
        /// <param name="stream"></param>
        public void Open(Stream stream)
        {
            Initialize();

            tOpenVideo = new Thread(() => 
            {
                curVideoPlugin = Plugins["DefaultExternal"];
                Open(((IPluginExternal)Plugins["DefaultExternal"]).OpenVideo(stream));

            });
            tOpenVideo.Name = "OpenVideo"; tOpenVideo.IsBackground = true; tOpenVideo.Start();
        }

        /// <summary>
        /// Opens an existing AVS stream from Plugins[_PLUGIN_NAME_].[AVS]Stream
        /// </summary>
        /// <param name="inputStream"></param>
        public void Open(StreamBase inputStream)
        {
            if (inputStream is VideoStream)
            {
                InitializeSwitch();
                tOpenVideo = new Thread(() => { OpenVideo((VideoStream)inputStream); });
                tOpenVideo.Name = "OpenVideo"; tOpenVideo.IsBackground = true; tOpenVideo.Start();
            }
            else if (inputStream is AudioStream)
            {
                decoder.StopAudio();
                Utils.EnsureThreadDone(tOpenAudio);
                Session.CurAudioStream.InUse = false;
                if (Config.audio.Enabled == false) Config.audio.SetEnabled();

                tOpenAudio = new Thread(() => { OpenAudio((AudioStream)inputStream); });
                tOpenAudio.Name = "OpenAudio"; tOpenAudio.IsBackground = true; tOpenAudio.Start();
            }
            else if (inputStream is SubtitleStream)
            {
                decoder.StopSubs();
                Utils.EnsureThreadDone(tOpenSubs);
                Session.CurSubtitleStream.InUse = false;
                Session.SubsText = null; sFrame = null;
                if (Config.subs.Enabled == false) Config.subs.SetEnabled();

                tOpenSubs = new Thread(() => { OpenSubs(null, (SubtitleStream)inputStream); });
                tOpenSubs.Name = "OpenSubtitles"; tOpenSubs.IsBackground = true; tOpenSubs.Start();
            }
        }

        private void OpenVideo(VideoStream vStream)
        {
            var stream = ((IPluginVideo)curVideoPlugin).OpenVideo(vStream);            
            if (stream == null) { OpenFailed(); return; }

            int ret = -1;

            if (stream.DecoderInput.Stream != null)
                ret = decoder.Open(stream.DecoderInput.Stream);

            else if (!string.IsNullOrEmpty(stream.DecoderInput.Url))
                ret = decoder.Open(stream.DecoderInput.Url);

            // TODO: Select best embedded video stream similarly with Youtube-DL plugin
            else if (stream.DecoderInput.StreamIndex != -1)
                ret = decoder.OpenVideo(stream.DecoderInput.StreamIndex);

            if (ret != 0) { OpenFailed(); return; }

            Session.CurVideoStream      = stream;
            Session.CurVideoStream.InUse= true;
            Log($"[VideoPlugin] {curVideoPlugin?.PluginName}");

            foreach(var plugin in Plugins.Values) plugin.OnVideoOpened();

            if (!HasAudio && Config.audio.Enabled)
                OpenAudio();

            if (!HasSubs && Config.subs.Enabled)
                foreach(var lang in Config.subs.Languages) if (OpenSubs(lang)) break; // Probably in tOpenThread (check torrent stream for messing with position)

            if (Session.CurTime != 0 && Session.Movie.Duration != 0) decoder.Seek(Session.CurTime/10000, true);

            // if playafterseek play?

            InitializeEnv();
        }
        private void OpenAudio(AudioStream aStream = null)
        {
            bool failed = false;
            foreach(var plugin in audioPlugins)
            {
                if (plugin.AudioStreams.Count == 0) continue;

                var stream = aStream == null ? ((IPluginAudio)plugin).OpenAudio() : ((IPluginAudio)plugin).OpenAudio(aStream);
                if (stream == null) continue;
                if (stream.DecoderInput.StreamIndex == -1 && string.IsNullOrEmpty(stream.DecoderInput.Url)) continue;

                if (stream.DecoderInput.StreamIndex != -1)
                    { if (decoder.OpenAudio(stream.DecoderInput.StreamIndex) != 0) { failed = true; continue; } }
                else if (!string.IsNullOrEmpty(stream.DecoderInput.Url) && decoder.OpenAudio(stream.DecoderInput.Url, aStream == null ? -1 : Session.CurTime/10000, true) != 0)
                    { failed = true; continue; }

                curAudioPlugin              = plugin;
                Session.CurAudioStream      = stream;
                Session.CurAudioStream.InUse= true;
                break;
            }

            if (failed)
                OnOpenCompleted(MediaType.Audio, false);
            else
                OnOpenCompleted(MediaType.Audio, true);

            Log($"[AudioPlugin] {curAudioPlugin?.PluginName}");
        }
        private bool OpenSubs(Language lang, SubtitleStream sStream = null)
        {
            foreach(var plugin in subtitlePlugins)
            {
                if (lang != null) ((IPluginSubtitles)plugin).Search(lang);

                if (plugin.SubtitleStreams.Count == 0) continue;

                var stream = sStream == null && lang != null ? ((IPluginSubtitles)plugin).OpenSubtitles(lang) : ((IPluginSubtitles)plugin).OpenSubtitles(sStream);
                if (stream == null) continue;

                if (!stream.Downloaded)
                    if (((IPluginSubtitles)plugin).Download(stream)) stream.Downloaded = true; else continue;

                if (!stream.Converted)
                {
                    Encoding subsEnc = SubtitleConverter.Detect(stream.DecoderInput.Url);

                    if (subsEnc != Encoding.UTF8)
                    {
                        FileInfo fi = new FileInfo(stream.DecoderInput.Url);
                        var newUrl = Path.Combine(Session.Movie.Folder, "Subs", fi.Name.Remove(fi.Name.Length - fi.Extension.Length) + ".utf8.ext.srt");
                        Directory.CreateDirectory(Path.Combine(Session.Movie.Folder, "Subs"));
                        SubtitleConverter.Convert(stream.DecoderInput.Url, newUrl, subsEnc, new UTF8Encoding(false));
                        stream.DecoderInput.Url = newUrl;
                    }
                }

                if (stream.DecoderInput.StreamIndex == -1 && string.IsNullOrEmpty(stream.DecoderInput.Url)) continue; // Failed

                if (stream.DecoderInput.StreamIndex != -1)
                    { if (decoder.OpenSubs(stream.DecoderInput.StreamIndex) != 0) continue; } // Failed
                else if (!string.IsNullOrEmpty(stream.DecoderInput.Url) && decoder.OpenSubs(stream.DecoderInput.Url, Session.CurTime/10000) != 0) continue; // Failed

                curSubtitlePlugin               = plugin;
                Session.CurSubtitleStream       = stream;
                Session.CurSubtitleStream.InUse = true;

                Log($"[SubtitlePlugin] {curSubtitlePlugin?.PluginName}");
                return true;
            }

            return false;
        }
        #endregion

        #region Dynamic Config Commands
        internal void SetAudioDelay()
        {
            if (!Session.CanPlay) return;

            if (!decoder.aDecoder.isEmbedded)
                decoder.aDemuxer.ReSync(Session.CurTime/10000);
        }
        internal void SetSubsDelay()
        {
            if (!Session.CanPlay) return;

            if (!decoder.sDecoder.isEmbedded)
                decoder.sDemuxer.ReSync(Session.CurTime/10000);
        }
        internal void DisableAudio()
        {
            if (!Session.CanPlay) return;

            decoder.StopAudio();
            Utils.EnsureThreadDone(tOpenAudio);
            Session.CurAudioStream.InUse = false;
            Session.LastAudioStream = Session.CurAudioStream;
        }
        internal void DisableSubs()
        {
            if (!Session.CanPlay) return;

            decoder.StopSubs();
            Utils.EnsureThreadDone(tOpenSubs);
            sFrame = null; Session.SubsText = "";
            Session.CurSubtitleStream.InUse = false;
            Session.LastSubtitleStream = Session.CurSubtitleStream;
        }
        internal void EnableAudio()
        {
            if (!Session.CanPlay) return;

            OpenAudio(Session.LastAudioStream);
        }
        internal void EnableSubs()
        {
            if (!Session.CanPlay) return;

            if (Session.LastSubtitleStream == null)
                { foreach(var lang in Config.subs.Languages) if (OpenSubs(lang)) break; }
            else
                OpenSubs(null, Session.LastSubtitleStream);
        }
        #endregion

        #region Playback

        /// <summary>
        /// Plays AVS streams
        /// </summary>
        public void Play()
        {
            if (!Session.CanPlay || Status == Status.Playing) return;

            playAfterSeek = true;
            Status = Status.Playing;
            Utils.EnsureThreadDone(tPlay);
            tPlay = new Thread(() =>
            {
                try
                {
                    Utils.NativeMethods.TimeBeginPeriod(1);
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED);
                    Screamer();

                } catch (Exception e) { Log(e.Message + " - " + e.StackTrace); }

                finally
                {
                    //audioPlayer.Pause();
                    Utils.DisposeVideoFrame(vFrame); vFrame = null;
                    Utils.NativeMethods.TimeEndPeriod(1);
                    Utils.NativeMethods.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                    if (Status != Status.Seeking)
                    { 
                        if (HasEnded)
                            Status = Status.Ended; 
                        else
                            Status = Status.Paused;
                    }
                }
            });
            tPlay.IsBackground = true;
            tPlay.Name = "Play";
            tPlay.Start();
        }

        /// <summary>
        /// Pauses AVS streams
        /// </summary>
        public void Pause()
        {
            if (!Session.CanPlay) return;

            playAfterSeek = false;
            Status = Status.Paused;
            Utils.EnsureThreadDone(tPlay);
            decoder.Pause();
        }

        /// <summary>
        /// Seeks backwards or forewards based on the specified ms to the nearest keyframe
        /// </summary>
        /// <param name="ms"></param>
        /// <param name="foreward"></param>
        public void Seek(int ms, bool foreward = false)
        {
            if (!Session.CanPlay) return; //|| Session.VideoInfo.Duration == 0 (Reopening will cause issues on this one, maybe use different variable to save Duration once)

            SeekData seekData2 = new SeekData(ms, foreward);
            Session.SetCurTime(ms * (long)10000);
            seeks.Push(seekData2);

            lock (lockSeek) { if (IsSeeking) return; IsSeeking = true; }

            tSeek = new Thread(() =>
            {
			    try
			    {
                    Log("Seek Thread Started!");
                    TimeBeginPeriod(1);

                    SeekData seekData;
                    bool     shouldPlay = false;
                    int      cancelOffsetMs = 1500; // lastSeekMs +-
                    int      stayAliveMs    = 850; // Wait for more seek requests
                    int      networkAbortMs = 300; // Time to wait before aborting the seek request

                    while(true)
                    {
                        bool seekPop = seeks.TryPop(out seekData); seeks.Clear();

                        // Ignore | Wait for more seeks before kill thread (to avoid stop/start)
                        if (!seekPop || (seekData.ms - cancelOffsetMs <= lastSeekMs && seekData.ms + cancelOffsetMs >= lastSeekMs))
                        {
                            if (seekPop) Log("Seek Ignored 1 " + Utils.TicksToTime(seekData.ms * (long)10000));

                            if (seeks.Count == 0)
                            {
                                seekWatch.Restart();
                                bool beforeSeekingSaved = playAfterSeek;
                                if (playAfterSeek && shouldPlay) { shouldPlay = false; Play(); /*true);*/ beforeSeekingSaved = true; }

                                do
                                {
                                    if (beforeSeekingSaved != playAfterSeek)
                                    {
                                        if (playAfterSeek)
                                            Play();
                                        else
                                        {
                                            Log("Seek Pause All Start");
                                            Status = Status.Paused;
                                            Utils.EnsureThreadDone(tPlay);
                                            decoder.Pause();
                                            Log("Seek Pause All Done");
                                        }

                                        beforeSeekingSaved = playAfterSeek;
                                    }

                                    if (seekWatch.ElapsedMilliseconds > stayAliveMs) { Log("Seek Exhausted"); return; }
                                    renderer.PresentFrame();
                                    Thread.Sleep(35);
                                } while (seeks.Count == 0);
                                seekWatch.Stop();
                            }

                            continue;
                        }

                        // Seek Preperation
                        if (IsPlaying) shouldPlay = true;
                        Status = Status.Seeking;
                        decoder.Pause();
                        Utils.EnsureThreadDone(tPlay, 250, 3);

                        // Direct Seek | Abortable Seek (Local vs Network)
                        bool seekFailed = false;

                        if (Session.Movie.UrlType != UrlType.File && !Master.PreventAborts) // Only "Slow" Network Streams (Web/RTSP/Torrent etc.)
                        {
                            //Thread.Sleep(networkDecideMs);
                            if (seeks.Count != 0) { /*Log("Seek Ignores");*/ continue; }
                            lastSeekMs = seekData.ms;

                            int decStatus = -1;
                            Thread decThread = new Thread(() =>
                            {
                                decStatus = 0;

                                try { if (decoder.Seek(seekData.ms, seekData.foreward) < 0) seekFailed = true; }
                                catch (Exception) { decStatus = 2; return; }

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

                                        decoder.interrupt = 1;
                                        decThread.Abort();
                                        while (decStatus < 1) Thread.Sleep(20);
                                        decoder.interrupt = 0;
                                        
                                        decoder.ReOpen();
                                        Log("Seek Abort Done");
                                        break;
                                    }

                                    seekWatch.Restart();
                                }

                                if (decStatus < 1) Thread.Sleep(20); else break;
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
                    lastSeekMs = Int32.MinValue;
                    seekWatch.Stop();
                    lock (lockSeek) IsSeeking = false;
                    if (Status == Status.Seeking) { Status = Status.Paused; }
                    Log("Seek Thread Done!");
                }
            });
            tSeek.IsBackground = true;
	        tSeek.Start();
        }

        /// <summary>
        /// Stops and Closes AVS streams
        /// </summary>
        public void Stop()
        {
            Initialize();
        }

        bool disposed = false;
        public void Dispose()
        {
            if (disposed) return;
            Stop();
            audioPlayer.Dispose(); 
            renderer.Dispose();

            audioPlayer = null;
            renderer = null;
            decoder = null;
            GC.Collect();
            disposed = true;
        }
        #endregion

        #region Scream
        private bool MediaBuffer()
        {
            //audioPlayer.Pause();
            if (!decoder.isRunning)
            {
                if (HasEnded) decoder.Seek(0);
                decoder.Play();
            }

            Log("[SCREAMER] Buffering ...");
            vFrame = null;
            aFrame = null;
            sFrame = null;
            Session.SubsText = "";

            bool gotAudio = !HasAudio;// || !DoAudio;
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
                    if (!gotVideo && decoder.vDecoder.frames.Count >= Config.decoder.MinVideoFrames) gotVideo = true;

                    if (!gotAudio && aFrame != null)
                    {
                        if (vFrame.timestamp - aFrame.timestamp > Config.audio.LatencyTicks)
                            decoder.aDecoder.frames.TryDequeue(out aFrame);
                        else if (decoder.aDecoder.frames.Count >= Config.decoder.MinAudioFrames)
                            gotAudio = true;
                    }
                }

                if (!IsPlaying || HasEnded)
                    shouldStop = true;
                else
                {
                    if (!decoder.vDecoder.isPlaying) { Log("[SCREAMER] Video Exhausted"); shouldStop= true; }
                    if (!decoder.aDecoder.isPlaying) { Log("[SCREAMER] Audio Exhausted"); gotAudio  = true; }
                }

                Thread.Sleep(10);

            } while (!shouldStop && (!gotVideo || !gotAudio));

            if (shouldStop && !(HasEnded && IsPlaying && vFrame != null)) { Log("[SCREAMER] Stopped"); return false; }
            if (vFrame == null) { Log("[SCREAMER] [ERROR] No Frames!"); return false; }

            // Wait 1: Ensure we have enough buffering packets to play (mainly for network streams)
            while (decoder.vDecoder.packets.Count < Config.demuxer.MinQueueSize && IsPlaying && !HasEnded) Thread.Sleep(15);
            Log("[SCREAMER] Buffering Done");

            if (sFrame == null) decoder.sDecoder.frames.TryDequeue(out sFrame);

            if (aFrame != null && aFrame.timestamp < vFrame.timestamp) 
                videoStartTicks = Math.Max(aFrame.timestamp, vFrame.timestamp - Config.audio.LatencyTicks);
            else
                videoStartTicks = vFrame.timestamp - Config.audio.LatencyTicks;

            startedAtTicks  = DateTime.UtcNow.Ticks;
            Session.SetCurTime(videoStartTicks);

            //audioPlayer.Play();
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

            while (IsPlaying)
            {
                if (vFrame == null)
                {
                    if (HasEnded) break;

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
                        Utils.DisposeVideoFrame(vFrame); vFrame = null; aFrame = null;
                        Thread.Sleep(10);
                        MediaBuffer();
                        continue; 
                    }

                    // Informs the application with CurTime when the second changes
                    if ((int)Session.CurTime / 10000000 != (int)elapsedTicks / 10000000)
                        Session.SetCurTime(elapsedTicks);

                    Thread.Sleep(sleepMs);
                }

                if (Math.Abs(vDistanceMs - sleepMs) <= 2)
                {
                    //Log($"[V] Presenting {Utils.TicksToTime(vFrame.timestamp)}");
                    renderer.PresentFrame(vFrame);
                    decoder.vDecoder.frames.TryDequeue(out vFrame);
                }
                else if (vDistanceMs < -2)
                {
                    Utils.DisposeVideoFrame(vFrame);
                    decoder.vDecoder.frames.TryDequeue(out vFrame);
                    Log($"vDistanceMs 2 |-> {vDistanceMs}");
                }

                if (aFrame != null) // Should use different thread for better accurancy (renderer might delay it on high fps) | also on high offset we will have silence between samples
                {
                    if (Math.Abs(aDistanceMs - sleepMs) <= 10)
                    {
                        //Log($"[A] Presenting {Utils.TicksToTime(aFrame.timestamp)}");
                        audioPlayer.FrameClbk(aFrame.audioData);
                        decoder.aDecoder.frames.TryDequeue(out aFrame);
                    }
                    else if (aDistanceMs < -10) // Will be transfered back to decoder to drop invalid timestamps
                    {
                        Log("-=-=-=-=-=-=");
                        for (int i=0; i<25; i++)
                        {
                            Log($"aDistanceMs 2 |-> {aDistanceMs}");
                            decoder.aDecoder.frames.TryDequeue(out aFrame);
                            aDistanceMs = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                            if (aDistanceMs > -7) break;
                        }
                    }
                }

                if (sFramePrev != null)
                    if (elapsedTicks - sFramePrev.timestamp > (long)sFramePrev.duration * 10000) { Session.SubsText = null; sFramePrev = null; }

                if (sFrame != null)
                {
                    if (Math.Abs(sDistanceMs - sleepMs) < 30)
                    {
                        Session.SubsText = sFrame.text;
                        sFramePrev = sFrame;
                        decoder.sDecoder.frames.TryDequeue(out sFrame);
                    }
                    else if (sDistanceMs < -30)
                    {
                        if (sFrame.duration + sDistanceMs > 0)
                        {
                            Session.SubsText = sFrame.text;
                            sFramePrev = sFrame;
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
            
            Log($"[SCREAMER] Finished -> {Utils.TicksToTime(Session.CurTime)}");
        }
        private void ShowOneFrame()
        {
            Session.SubsText = null; sFrame = null;

            if (decoder.vDecoder.frames.Count > 0)
            {
                MediaFrame vFrame = null;
                decoder.vDecoder.frames.TryDequeue(out vFrame);
                if (seeks.Count == 0) Session.SetCurTime(vFrame.timestamp);
                renderer.PresentFrame(vFrame);
            }
            return;
        }
        #endregion

        #region Events
        public class OpenCompletedArgs : EventArgs
        {

            public MediaType type;
            public bool success;
            public OpenCompletedArgs(MediaType type, bool success)
            {
                this.type = type;
                this.success = success;
            }
        }

        /// <summary>
        /// Fires on Audio / Video open success or failure
        /// </summary>
        public event EventHandler<OpenCompletedArgs> OpenCompleted;
        protected virtual void OnOpenCompleted(MediaType type, bool success) { Task.Run(() => OpenCompleted?.Invoke(this, new OpenCompletedArgs(type, success))); }
        #endregion

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{PlayerId}] [Player] {msg}"); }
    }
}