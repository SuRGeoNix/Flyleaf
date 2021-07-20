using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using static FlyleafLib.Utils;
using static FlyleafLib.Utils.NativeMethods;

using FlyleafLib.Controls;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaRenderer;
using FlyleafLib.MediaStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer
{
    public unsafe class Player : NotifyPropertyChanged, IDisposable
    {
        #region Properties

        /// <summary>
        /// The Content Control which hosts WindowsFormsHost (useful for airspace issues &amp; change to fullscreen mode)
        /// (WinForms: not set)
        /// </summary>
        public VideoView    VideoView       { get ; set; }

        /// <summary>
        /// Flyleaf Control (WinForms)
        /// </summary>
        public Flyleaf      Control         { get => _Control; set { if (value.Handle == _Control?.Handle) return; InitializeControl1(_Control, value); } }
        Flyleaf _Control;

        /// <summary>
        /// Player's Configuration (set once in the constructor)
        /// </summary>
        public Config       Config          { get; private set; }

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
        public bool         HasEnded        => decoder != null && decoder.VideoDecoder.Status == MediaFramework.MediaDecoder.Status.Ended;

        public bool         HasVideo        => decoder != null && decoder.VideoDecoder.Status != MediaFramework.MediaDecoder.Status.Stopped;

        /// <summary>
        /// Whether the video has audio and it is configured
        /// </summary>
        public bool         HasAudio        => decoder != null && decoder.AudioDecoder.Status != MediaFramework.MediaDecoder.Status.Stopped;// .status != Status.None;

        /// <summary>
        /// Whether the video has subtitles and it is configured
        /// </summary>
        public bool         HasSubs         => decoder != null && decoder.SubtitlesDecoder.Status != MediaFramework.MediaDecoder.Status.Stopped;// .status != Status.None;
        
        /// <summary>
        /// Dictionary of available Plugins and their AVS Streams
        /// </summary>
        public Dictionary<string, PluginBase> Plugins {get; private set; }  = new Dictionary<string, PluginBase>();

        /// <summary>
        /// Actual Frames rendered per second (FPS)
        /// </summary>
        public int          FPS             { get => _FPS; private set => Set(ref _FPS, value); }
        int _FPS = 0;

        /// <summary>
        /// Total Dropped Frames
        /// </summary>
        public int          DroppedFrames   { get => _DroppedFrames; private set => Set(ref _DroppedFrames, value); }
        int _DroppedFrames = 0;

        /// <summary>
        /// Total bitrate (Kbps)
        /// </summary>
        public double       TBR             { get => _TBR; private set => Set(ref _TBR, value); }
        double _TBR = 0;

        /// <summary>
        /// Video bitrate (Kbps)
        /// </summary>
        public double       VBR             { get => _VBR; private set => Set(ref _VBR, value); }
        double _VBR = 0;

        
        /// <summary>
        /// Audio bitrate (Kbps)
        /// </summary>
        public double       ABR             { get => _ABR; private set => Set(ref _ABR, value); }
        double _ABR = 0;

        /// <summary>
        /// Player's Status
        /// </summary>
        public Status       Status          { get => _Status; private set => Set(ref _Status, value); }
        Status _Status = Status.Stopped;

        public List<PluginBase> audioPlugins        { get; private set; }
        public List<PluginBase> videoPlugins        { get; private set; }
        public List<PluginBase> subtitlePlugins     { get; private set; }

        public PluginBase       curAudioPlugin      { get; private set; }
        public PluginBase       curVideoPlugin      { get; private set; }
        public PluginBase       curSubtitlePlugin   { get; private set; }
        #endregion

        #region Properties Internal
        static int curPlayerId = 0;

        public AudioPlayer      audioPlayer;
        public Renderer         renderer;
        public DecoderContext   decoder;

        Thread tOpenVideo, tOpenAudio, tOpenSubs, tSeek, tPlay;
        object lockOpen = new object();
        object lockSeek = new object();

        ConcurrentStack<SeekData> seeks;

        class SeekData
        {
            public int  ms;
            public bool foreward;

            public SeekData(int ms, bool foreward) { this.ms = ms; this.foreward = foreward; }
        }

        VideoFrame vFrame;
        AudioFrame aFrame;
        long        resyncFrameTs;
        bool        requiresResync;

        bool        isVideoSwitch;

        internal SubtitlesFrame sFrame, sFramePrev;
        long        elapsedTicks;
        long        startedAtTicks;
        long        videoStartTicks;
        #endregion

        #region Initialize / Dispose
		public Player(Config config = null)
        {
            Config = config == null ? new Config() : config;
            Config.SetPlayer(this);
        }

        private void InitializeControl1(Flyleaf oldValue, Flyleaf newValue)
        {
            lock (this)
            {
                Interlocked.Increment(ref curPlayerId);
                PlayerId = curPlayerId; 
                Log("[Starting]");

                Session         = new Session(this);
                seeks           = new ConcurrentStack<SeekData>();
                audioPlayer     = new AudioPlayer(this);

                Master.Players.Add(PlayerId, this);

                if (newValue.Handle != null)
                    InitializeControl2(newValue);
                else
                    newValue.HandleCreated += (o, e) => { InitializeControl2(newValue); };
            }
            
        }
        private void InitializeControl2(Flyleaf newValue)
        {
            lock (this)
            {
                _Control = newValue;
                _Control.Player = this;
                renderer = new Renderer(this);
                decoder = new DecoderContext(this);
                renderer.PresentFrame();
                LoadPlugins();
                Log("[Started]");
            }
            
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
            Utils.NativeMethods.TimeBeginPeriod(1);
            Log($"[Initializing]");

            // Prevent Seek Process
            Session.CanPlay = false;
            seeks.Clear();
            EnsureThreadDone(tSeek, 30000, 2);

            // Stop Screamer / MediaBuffer
            Status = Status.Stopped;
            EnsureThreadDone(tPlay, 30000, 2);

            // Inform Plugins (OnInitializing)
            foreach(var plugin in Plugins.Values) plugin.OnInitializing();
            
            // Reset Rest
            decoder.Stop();
            EnsureThreadDone(tOpenVideo, 30000, 2);
            EnsureThreadDone(tOpenAudio, 30000, 2);
            EnsureThreadDone(tOpenSubs, 30000, 2);
            Session.Reset();
            curVideoPlugin = null;
            curAudioPlugin = null;

            // Inform Plugins (OnInitialized)
            foreach(var plugin in Plugins.Values) plugin.OnInitialized();

            Log($"[Initialized]");
            Utils.NativeMethods.TimeEndPeriod(1);
        }
        private void InitializeSwitch()
        {
            Utils.NativeMethods.TimeBeginPeriod(1);
            Log($"[Initializing Switch]");
            // Prevent Seek Process
            Session.CanPlay = false;
            seeks.Clear();
            EnsureThreadDone(tSeek, 30000, 2);

            // Stop Screamer / MediaBuffer
            Status = Status.Stopped;
            EnsureThreadDone(tPlay, 30000, 2);

            // Inform Plugins (OnInitializing)
            foreach(var plugin in Plugins.Values) plugin.OnInitializingSwitch();

            // Reset Rest
            decoder.Stop();
            EnsureThreadDone(tOpenVideo, 30000, 2);
            EnsureThreadDone(tOpenAudio, 30000, 2);
            EnsureThreadDone(tOpenSubs, 30000, 2);

            // Inform Plugins (OnInitialized)
            foreach(var plugin in Plugins.Values) plugin.OnInitializedSwitch();
            Session.Reset(true);

            if (Session.CurAudioStream != null) Session.CurAudioStream.InUse = false;
            if (Session.CurVideoStream != null) Session.CurVideoStream.InUse = false;

            Log($"[Initialized Switch]");
            Utils.NativeMethods.TimeEndPeriod(1);
        }
        private void InitializeEnv()
        {
            Log($"[Initializing Env]");

            Status = Status.Paused;
            Session.CanPlay = true;
            Session.Movie.Duration = decoder.VideoDemuxer.Duration;

            OnOpenCompleted(MediaType.Video, true);
            Log($"[Initialized  Env]");
        }

        /// <summary>
        /// Access this only from Plugins
        /// </summary>
        public void OpenFailed()
        {
            if (Status == Status.Stopped) return;
            if (curVideoPlugin != null) Log($"[VideoPlugin] {curVideoPlugin} Failed");

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
            Log($"Opening {url}");

            if (Utils.SubsExts.Contains(Utils.GetUrlExtention(url)))
            {
                if (Config.subs.Enabled == false) Config.subs.SetEnabled();
                Open(((IPluginExternal)Plugins["DefaultExternal"]).OpenSubtitles(url));
                return;
            }

            Initialize();

            tOpenVideo = new Thread(() =>
            {
                lock (lockOpen)
                {
                    Status = Status.Opening;
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
                lock (lockOpen)
                {
                    Status = Status.Opening;
                    curVideoPlugin = Plugins["DefaultExternal"];
                    Open(((IPluginExternal)Plugins["DefaultExternal"]).OpenVideo(stream));
                }
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
                if (inputStream.StreamIndex != -1)
                {
                    tOpenVideo = new Thread(() =>
                    {
                        isVideoSwitch = true;
                        if (decoder.Open(inputStream) != 0) { isVideoSwitch = false; OpenFailed(); return; }
                        isVideoSwitch = false;
                    });
                    tOpenVideo.Name = "OpenVideo"; tOpenVideo.IsBackground = true; tOpenVideo.Start();
                }
                else
                {
                    InitializeSwitch();
                    tOpenVideo = new Thread(() =>
                    {
                        Status = Status.Opening;
                        OpenVideo((VideoStream)inputStream); 
                    });
                    tOpenVideo.Name = "OpenVideo"; tOpenVideo.IsBackground = true; tOpenVideo.Start();
                }
            }
            else if (inputStream is AudioStream)
            {
                Utils.EnsureThreadDone(tOpenAudio);
                if (Session.CurAudioStream != null) Session.CurAudioStream.InUse = false;
                if (Config.audio.Enabled == false) Config.audio.SetEnabled();

                tOpenAudio = new Thread(() => { OpenAudio((AudioStream)inputStream); });
                tOpenAudio.Name = "OpenAudio"; tOpenAudio.IsBackground = true; tOpenAudio.Start();
            }
            else if (inputStream is SubtitlesStream)
            {
                Utils.EnsureThreadDone(tOpenSubs);
                if (Session.CurSubtitleStream != null) Session.CurSubtitleStream.InUse = false;
                Session.SubsText = null; sFrame = null;
                if (Config.subs.Enabled == false) Config.subs.SetEnabled();

                tOpenSubs = new Thread(() => { OpenSubs(null, (SubtitlesStream)inputStream); });
                tOpenSubs.Name = "OpenSubtitles"; tOpenSubs.IsBackground = true; tOpenSubs.Start();
            }
        }

        private void OpenVideo(VideoStream vStream)
        {
            var stream = ((IPluginVideo)curVideoPlugin).OpenVideo(vStream);            
            if (stream == null) { OpenFailed(); return; }

            int ret = -1;

            if (stream.Stream != null)
                ret = decoder.Open(stream.Stream);

            else if (!string.IsNullOrEmpty(stream.Url))
                ret = decoder.Open(stream.Url);

            if (ret != 0) { OpenFailed(); return; }

            Session.CurVideoStream      = stream;
            Session.CurVideoStream.InUse= true;
            Log($"[VideoPlugin] {curVideoPlugin?.PluginName}");

            foreach(var plugin in Plugins.Values) plugin.OnVideoOpened();
            if (!HasVideo) { OpenFailed(); return; }

            if (!HasAudio && Config.audio.Enabled)
                OpenAudio();

            if (!HasSubs && Config.subs.Enabled)
                foreach(var lang in Config.subs.Languages) if (OpenSubs(lang)) break; // Probably in tOpenThread (check torrent stream for messing with position)

            if (Session.CurTime != 0 && Session.Movie.Duration != 0) { decoder.Seek(Session.CurTime/10000, true); requiresResync = true; }

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
                if (stream.StreamIndex == -1 && string.IsNullOrEmpty(stream.Url)) continue;

                if (stream.StreamIndex != -1)
                    { if (decoder.Open(stream) != 0) { failed = true; continue; } }
                else if (!string.IsNullOrEmpty(stream.Url) && decoder.OpenAudio(stream.Url, aStream == null ? -1 : elapsedTicks / 10000) != 0)
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
        private bool OpenSubs(Language lang, SubtitlesStream sStream = null)
        {
            foreach(var plugin in subtitlePlugins)
            {
                if (lang != null) ((IPluginSubtitles)plugin).Search(lang);

                if (plugin.SubtitlesStreams.Count == 0) continue;

                var stream = sStream == null && lang != null ? ((IPluginSubtitles)plugin).OpenSubtitles(lang) : ((IPluginSubtitles)plugin).OpenSubtitles(sStream);
                if (stream == null) continue;

                if (!stream.Downloaded)
                    if (((IPluginSubtitles)plugin).Download(stream)) stream.Downloaded = true; else continue;

                if (!stream.Converted)
                {
                    Encoding subsEnc = SubtitleConverter.Detect(stream.Url);

                    if (subsEnc != Encoding.UTF8)
                    {
                        FileInfo fi = new FileInfo(stream.Url);
                        var newUrl = Path.Combine(Session.Movie.Folder, "Subs", fi.Name.Remove(fi.Name.Length - fi.Extension.Length) + ".utf8.srt");
                        Directory.CreateDirectory(Path.Combine(Session.Movie.Folder, "Subs"));
                        SubtitleConverter.Convert(stream.Url, newUrl, subsEnc, new UTF8Encoding(false));
                        stream.Url = newUrl;
                    }

                    stream.Converted = true;
                }

                if (stream.StreamIndex == -1 && string.IsNullOrEmpty(stream.Url)) continue; // Failed

                if (stream.StreamIndex != -1)
                    { if (decoder.Open(stream) != 0) continue; } // Failed
                else if (!string.IsNullOrEmpty(stream.Url) && decoder.OpenSubs(stream.Url, elapsedTicks / 10000) != 0) continue; // Failed

                Session.SubsText = null; sFrame = null;
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
            if (!IsPlaying) { requiresResync = true; return; }

            decoder.SeekAudio(elapsedTicks/10000);
        }
        internal void SetSubsDelay()
        {
            if (!Session.CanPlay) return;
            if (!IsPlaying) { requiresResync = true; return; }

            decoder.SeekSubtitles(elapsedTicks/10000);
            sFrame = null; Session.SubsText = "";
        }
        internal void DisableAudio()
        {
            if (!Session.CanPlay) return;

            decoder.AudioDecoder.Stop();
            if (Session.CurAudioStream == null) return;

            Session.CurAudioStream.InUse = false;
            Session.LastAudioStream = Session.CurAudioStream;
        }
        internal void DisableSubs()
        {
            if (!Session.CanPlay) return;

            decoder.SubtitlesDecoder.Stop();
            sFrame = null; Session.SubsText = "";
            if (Session.CurSubtitleStream == null) return;

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

            Status = Status.Playing;
            Utils.EnsureThreadDone(tSeek);
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
                    if (Status == Status.Stopped) decoder?.Stop(); else decoder?.Pause();
                    audioPlayer?.ClearBuffer();
                    VideoDecoder.DisposeFrame(vFrame); vFrame = null;
                    Utils.NativeMethods.TimeEndPeriod(1);
                    Utils.NativeMethods.SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                    Status = HasEnded ? Status.Ended  : Status.Paused;
                    if (HasEnded) OnPlaybackCompleted();
                }
            });
            tPlay.Name = "Play"; tPlay.IsBackground = true; tPlay.Start();
        }

        /// <summary>
        /// Pauses AVS streams
        /// </summary>
        public void Pause()
        {
            if (!Session.CanPlay) return;

            Status = Status.Paused;
            Utils.EnsureThreadDone(tPlay);
            //decoder.Pause();
        }

        /// <summary>
        /// Seeks backwards or forewards based on the specified ms to the nearest keyframe
        /// </summary>
        /// <param name="ms"></param>
        /// <param name="foreward"></param>
        public void Seek(int ms, bool foreward = false)
        {
            if (!Session.CanPlay) return;

            Session.SetCurTime(ms * (long)10000);
            seeks.Push(new SeekData(ms, foreward));

            if (Status == Status.Playing) return;

            lock (lockSeek) { if (IsSeeking) return; IsSeeking = true; }

            tSeek = new Thread(() =>
            {
                try
                {
                    TimeBeginPeriod(1);
                    while (seeks.TryPop(out SeekData seekData) && Session.CanPlay && Status != Status.Playing)
                    {
                        seeks.Clear();

                        if (decoder.Seek(seekData.ms, seekData.foreward) >= 0)
                        {
                            decoder.GetVideoFrame();
                            ShowOneFrame();
                        }
                        else
                            Log("[SEEK] Failed");

                        Thread.Sleep(20);
                    }

                } catch (Exception e)
                {
                    Log($"[SEEK] Error {e.Message}");
                } finally
                {
                    requiresResync = true;
                    TimeEndPeriod(1);
                    lock (lockSeek) IsSeeking = false;
                }
            });
            tSeek.Name = "Seek"; tSeek.IsBackground = true; tSeek.Start();
        }

        /// <summary>
        /// Stops and Closes AVS streams
        /// </summary>
        public void Stop()
        {
            lock (this)
            {
                Pause();
                if (disposed || decoder == null) return;
                decoder.Stop();
                lock (lockSeek)
                    lock (lockOpen)
                        Initialize();
            }
        }

        /// <summary>
        /// Disposes the Player (might will cause issues if you call it from a UI thread)
        /// </summary>
        public void Dispose()
        {
            Master.DisposePlayer(this);
        }

        internal void DisposeInternal()
        {
            Console.WriteLine("Player_Dispose");

            lock (this)
            {
                if (disposed) return;

                Stop();

                audioPlayer?.Dispose(); 
                decoder.Dispose();
                renderer.Dispose();

                audioPlayer = null;
                renderer = null;
                decoder = null;
                Session = null;
                Config = null;

                disposed = true;
                VideoView?.Dispose();
                VideoView = null;

                _Control.Player = null;
                _Control = null;

                GC.Collect();
            }

            Console.WriteLine("Player_Disposed");
        }
        bool disposed = false;
        #endregion

        #region Scream
        private bool MediaBuffer()
        {
            audioPlayer?.ClearBuffer();

            decoder.VideoDemuxer.Start();
            decoder.VideoDecoder.Start();
            if (decoder.AudioDecoder.OnVideoDemuxer)
                decoder.AudioDecoder.Start();
            else if (!requiresResync)
            {
                decoder.AudioDemuxer.Start();
                decoder.AudioDecoder.Start();
            }
            if (decoder.SubtitlesDecoder.OnVideoDemuxer)
                decoder.SubtitlesDecoder.Start();
            else if (!requiresResync)
            {
                decoder.SubtitlesDemuxer.Start();
                decoder.SubtitlesDecoder.Start();
            }

            Log("[SCREAMER] Buffering ...");
            VideoDecoder.DisposeFrame(vFrame);
            if (aFrame != null) aFrame.audioData = new byte[0];
            vFrame = null;
            aFrame = null;
            sFrame = null;
            Session.SubsText = "";

            bool gotAudio       = !HasAudio;
            bool gotVideo       = false;
            bool shouldStop     = false;
            bool showOneFrame   = true;

            // Wait 1: Ensure we have enough video/audio frames
            do
            {
                if (showOneFrame && decoder.VideoDecoder.Frames.Count != 0)
                {
                    showOneFrame = false; ShowOneFrame();
                    if (seeks.Count != 0) return false; 

                    if (requiresResync)
                    {
                        requiresResync = false;
                        decoder.SeekAudio(resyncFrameTs/10000);
                        decoder.SeekSubtitles(resyncFrameTs/10000);
                    }
                }

                if (!showOneFrame && vFrame == null && decoder.VideoDecoder.Frames.Count != 0)
                    decoder.VideoDecoder.Frames.TryDequeue(out vFrame);

                if (!gotAudio && aFrame == null && !requiresResync && decoder.AudioDecoder.Frames.Count != 0)
                    decoder.AudioDecoder.Frames.TryDequeue(out aFrame);

                if (vFrame != null)
                {
                    if (!gotVideo && decoder.VideoDecoder.Frames.Count >= Config.decoder.MinVideoFrames) gotVideo = true;

                    if (!gotAudio && aFrame != null)
                    {
                        if (vFrame.timestamp - aFrame.timestamp > Config.audio.LatencyTicks)
                            { aFrame.audioData = new byte[0]; decoder.AudioDecoder.Frames.TryDequeue(out aFrame); }
                        else if (decoder.AudioDecoder.Frames.Count >= Config.decoder.MinAudioFrames)
                            gotAudio = true;
                    }
                }

                if (!IsPlaying || HasEnded)
                    shouldStop = true;
                else
                {
                    if (!decoder.VideoDecoder.IsRunning && !isVideoSwitch) { Log("[SCREAMER] Video Exhausted"); shouldStop= true; }
                    if (vFrame != null && !gotAudio && (!decoder.AudioDecoder.IsRunning || decoder.AudioDecoder.Demuxer.Status == MediaFramework.MediaDemuxer.Status.QueueFull)) { 
                        Log("[SCREAMER] Audio Exhausted"); gotAudio  = true; }
                }

                Thread.Sleep(10);

            } while (!shouldStop && (!gotVideo || !gotAudio));

            if (shouldStop && !(HasEnded && IsPlaying && vFrame != null)) { Log("[SCREAMER] Stopped"); return false; }
            if (vFrame == null) { Log("[SCREAMER] [ERROR] No Frames!"); return false; }

            // Wait 1: Ensure we have enough buffering packets to play (mainly for network streams)
            while (decoder.VideoDemuxer.VideoPackets.Count < Config.demuxer.MinQueueSize && IsPlaying && !HasEnded) Thread.Sleep(15);
            Log("[SCREAMER] Buffering Done");

            if (sFrame == null) decoder.SubtitlesDecoder.Frames.TryDequeue(out sFrame);

            if (aFrame != null && aFrame.timestamp < vFrame.timestamp) 
                videoStartTicks = Math.Max(aFrame.timestamp, vFrame.timestamp - Config.audio.LatencyTicks);
            else
                videoStartTicks = vFrame.timestamp - Config.audio.LatencyTicks;

            startedAtTicks  = DateTime.UtcNow.Ticks;
            Session.SetCurTime(videoStartTicks);

            Log($"[SCREAMER] Started -> {Utils.TicksToTime(videoStartTicks)} | [V: {Utils.TicksToTime(vFrame.timestamp)}]" + (aFrame == null ? "" : $" [A: {Utils.TicksToTime(aFrame.timestamp)}]"));

            return true;
        }    
        private void ShowOneFrame()
        {
            Session.SubsText = null; sFrame = null;

            if (decoder.VideoDecoder.Frames.Count > 0)
            {
                VideoFrame vFrame = null;
                decoder.VideoDecoder.Frames.TryDequeue(out vFrame);
                resyncFrameTs = vFrame.timestamp;
                if (seeks.Count == 0) Session.SetCurTime(vFrame.timestamp);
                renderer.PresentFrame(vFrame);
            }
            return;
        }
        private void Screamer()
        {
            int     vDistanceMs;
            int     aDistanceMs;
            int     sDistanceMs;
            int     sleepMs;
            int     actualFps  = 0;
            long    totalBytes = 0;
            long    videoBytes = 0;
            long    audioBytes = 0;

            bool    requiresBuffering = true;

            while (Status == Status.Playing)
            {
                if (seeks.TryPop(out SeekData seekData))
                {
                    seeks.Clear();
                    requiresBuffering = true;
                    requiresResync = true;
                    if (decoder.Seek(seekData.ms, seekData.foreward) < 0)
                        Log("[SCREAMER] Seek failed");
                }

                if (requiresBuffering)
                {
                    totalBytes = decoder.VideoDemuxer.TotalBytes + decoder.AudioDemuxer.TotalBytes + decoder.SubtitlesDemuxer.TotalBytes;
                    videoBytes = decoder.VideoDemuxer.VideoBytes + decoder.AudioDemuxer.VideoBytes + decoder.SubtitlesDemuxer.VideoBytes;
                    audioBytes = decoder.VideoDemuxer.AudioBytes + decoder.AudioDemuxer.AudioBytes + decoder.SubtitlesDemuxer.AudioBytes;

                    MediaBuffer();
                    requiresBuffering = false;
                    if (seeks.Count != 0) continue;
                    if (vFrame == null) { Log("MediaBuffer() no video frame"); break; }
                }

                if (vFrame == null)
                {
                    if (decoder.VideoDecoder.Status == MediaFramework.MediaDecoder.Status.Ended)
                    {
                        Status = Status.Ended;
                        Session.SetCurTime(videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks));
                    }
                    if (Status != Status.Playing) break;

                    Log("[SCREAMER] No video frames");
                    requiresBuffering = true;
                    continue;
                }

                if (Status != Status.Playing) break;

                if (aFrame == null) decoder.AudioDecoder.Frames.TryDequeue(out aFrame);
                if (sFrame == null) decoder.SubtitlesDecoder.Frames.TryDequeue(out sFrame);

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
                        VideoDecoder.DisposeFrame(vFrame); vFrame = null; aFrame = null;
                        Thread.Sleep(10);
                        MediaBuffer();
                        continue; 
                    }

                    // Informs the application with CurTime when the second changes
                    if ((int)(Session.CurTime / 10000000) != (int)(elapsedTicks / 10000000))
                    {
                        TBR = (decoder.VideoDemuxer.TotalBytes + decoder.AudioDemuxer.TotalBytes + decoder.SubtitlesDemuxer.TotalBytes - totalBytes) * 8 / 1000.0;
                        VBR = (decoder.VideoDemuxer.VideoBytes + decoder.AudioDemuxer.VideoBytes + decoder.SubtitlesDemuxer.VideoBytes - videoBytes) * 8 / 1000.0;
                        ABR = (decoder.VideoDemuxer.AudioBytes + decoder.AudioDemuxer.AudioBytes + decoder.SubtitlesDemuxer.AudioBytes - audioBytes) * 8 / 1000.0;
                        totalBytes = decoder.VideoDemuxer.TotalBytes + decoder.AudioDemuxer.TotalBytes + decoder.SubtitlesDemuxer.TotalBytes;
                        videoBytes = decoder.VideoDemuxer.VideoBytes + decoder.AudioDemuxer.VideoBytes + decoder.SubtitlesDemuxer.VideoBytes;
                        audioBytes = decoder.VideoDemuxer.AudioBytes + decoder.AudioDemuxer.AudioBytes + decoder.SubtitlesDemuxer.AudioBytes;

                        FPS = actualFps;
                        actualFps = 0;

                        //Log($"Total bytes: {TBR}");
                        //Log($"Video bytes: {VBR}");
                        //Log($"Audio bytes: {ABR}");
                        //Log($"Current FPS: {FPS}");

                        Session.SetCurTime(elapsedTicks);
                    }

                    Thread.Sleep(sleepMs);
                }

                if (Math.Abs(vDistanceMs - sleepMs) <= 2)
                {
                    //Log($"[V] Presenting {Utils.TicksToTime(vFrame.timestamp)}");
                    
                    if (renderer.PresentFrame(vFrame)) actualFps++; else DroppedFrames++;
                    decoder.VideoDecoder.Frames.TryDequeue(out vFrame);
                }
                else if (vDistanceMs < -2)
                {
                    DroppedFrames++;
                    VideoDecoder.DisposeFrame(vFrame);
                    decoder.VideoDecoder.Frames.TryDequeue(out vFrame);
                    Log($"vDistanceMs 2 |-> {vDistanceMs}");
                }

                if (aFrame != null) // Should use different thread for better accurancy (renderer might delay it on high fps) | also on high offset we will have silence between samples
                {
                    if (Math.Abs(aDistanceMs - sleepMs) <= 10)
                    {
                        //Log($"[A] Presenting {Utils.TicksToTime(aFrame.timestamp)}");
                        audioPlayer?.FrameClbk(aFrame.audioData);
                        decoder.AudioDecoder.Frames.TryDequeue(out aFrame);
                    }
                    else if (aDistanceMs < -10) // Will be transfered back to decoder to drop invalid timestamps
                    {
                        Log("-=-=-=-=-=-=");
                        for (int i=0; i<25; i++)
                        {
                            Log($"aDistanceMs 2 |-> {aDistanceMs}");
                            decoder.AudioDecoder.Frames.TryDequeue(out aFrame);
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
                        decoder.SubtitlesDecoder.Frames.TryDequeue(out sFrame);
                    }
                    else if (sDistanceMs < -30)
                    {
                        if (sFrame.duration + sDistanceMs > 0)
                        {
                            Session.SubsText = sFrame.text;
                            sFramePrev = sFrame;
                            decoder.SubtitlesDecoder.Frames.TryDequeue(out sFrame);
                        }
                        else
                        {
                            Log($"sDistanceMs 2 |-> {sDistanceMs}");
                            decoder.SubtitlesDecoder.Frames.TryDequeue(out sFrame);
                        }
                    }
                }
            }
            
            Log($"[SCREAMER] Finished -> {Utils.TicksToTime(Session.CurTime)}");
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

        /// <summary>
        /// Fires on Playback completed
        /// </summary>
        public event EventHandler PlaybackCompleted;
        protected virtual void OnPlaybackCompleted() { Task.Run(() => PlaybackCompleted?.Invoke(this, new EventArgs())); }
        #endregion

        private void Log(string msg)
        { 
            #if DEBUG
                Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{PlayerId}] [Player] {msg}");
            #endif
        }
    }

    public enum Status
    {
        None,

        Opening,    // Opening Video (from Initialized state - not embedded/switch)
        OpenFailed,
        Opened,

        Playing,
        //Seeking,
        Stopping,

        Paused,
        Stopped,
        Ended,
        Failed
    }
}