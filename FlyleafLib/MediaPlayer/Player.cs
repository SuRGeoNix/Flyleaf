using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using FlyleafLib.Controls;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.Plugins;

using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;
using static FlyleafLib.Utils;
using static FlyleafLib.Utils.NativeMethods;

namespace FlyleafLib.MediaPlayer
{
    public unsafe class Player : AudioPlayer, IDisposable
    {
        #region Properties
        /// <summary>
        /// The Content Control which hosts WindowsFormsHost (useful for airspace issues &amp; change to fullscreen mode)
        /// (WinForms: not set)
        /// </summary>
        public VideoView        VideoView       { get ; set; }

        /// <summary>
        /// Flyleaf Control (WinForms)
        /// </summary>
        public Flyleaf          Control         { get => _Control; set { InitializeControl1(_Control, value); } }
        internal Flyleaf _Control;

        /// <summary>
        /// Information about the current opened audio
        /// </summary>
        public AudioInfo        Audio           { get; private set; }

        /// <summary>
        /// Information about the current opened video
        /// </summary>
        public VideoInfo        Video           { get; private set; }

        /// <summary>
        /// Information about the current opened subtitles
        /// </summary>
        public SubtitlesInfo    Subtitles       { get; private set; }

        /// <summary>
        /// Whether the input has ended
        /// </summary>
        public bool         HasEnded            => decoder != null && VideoDecoder.Status == MediaFramework.Status.Ended;
        public bool         IsBuffering         { get; private set; }
        public bool         IsSeeking           { get; private set; }
        public bool         IsOpening           { get; private set; }
        public bool         IsOpeningInput      { get; private set; }
        public bool         IsPlaying           => Status == Status.Playing;
        public bool         IsPlaylist          =>  decoder != null && decoder.OpenedPlugin != null && decoder.OpenedPlugin.IsPlaylist;

        /// <summary>
        /// Player's Status
        /// </summary>
        public Status       Status              { get => _Status;           private set => Set(ref _Status, value); }
        Status _Status = Status.Stopped;

        /// <summary>
        /// Whether the player's status is capable of accepting playback commands
        /// </summary>
        public bool         CanPlay             { get => _CanPlay;          private set => Set(ref _CanPlay, value); }
        bool _CanPlay;

        /// <summary>
        /// Player's current time or user's current seek time (uses forward direction)
        /// </summary>
        public long         CurTime             { get => _CurTime;          set { Set(ref _CurTime, value); Seek((int) (value/10000), true); } }
        long _CurTime;
        internal void SetCurTime(long curTime) { Set(ref _CurTime, curTime, false, nameof(CurTime)); }

        /// <summary>
        /// Input's duration
        /// </summary>
        public long         Duration            { get => _Duration;         private set => Set(ref _Duration, value); }
        long _Duration;

        /// <summary>
        /// The current buffered duration in the demuxer
        /// </summary>
        public long         BufferedDuration    { get => _BufferedDuration; set => Set(ref _BufferedDuration, value); }
        long _BufferedDuration;

        /// <summary>
        /// Whether the input is live or not (duration might not be 0 on live sessions to allow live seek, eg. hls)
        /// </summary>
        public bool         IsLive              { get => _IsLive;           private set => Set(ref _IsLive, value); }
        bool _IsLive;

        ///// <summary>
        ///// Total bitrate (Kbps)
        ///// </summary>
        public double       BitRate             { get => _BitRate;          private set => Set(ref _BitRate, value); }
        double _BitRate;

        /// <summary>
        /// Input's folder which might be used from plugins (eg. load / save subtitles)
        /// </summary>
        public string       Folder              { get => _Folder;           private set => Set(ref _Folder, value); }
        string _Folder;

        /// <summary>
        /// Input's size which might be used from plugins (eg. calculate movie hash for subtitles)
        /// </summary>
        public long         FileSize            { get => _FileSize;         private set => Set(ref _FileSize, value); }
        long _FileSize;

        /// <summary>
        /// Input's title
        /// </summary>
        public string       Title               { get => _Title;            private set => Set(ref _Title, value); }
        string _Title;

        /// <summary>
        /// Whether is recording or not
        /// </summary>
        public bool         IsRecording
        {
            get => VideoDemuxer.IsRecording;
            set => Set(ref _IsRecording, value);
        }
        bool _IsRecording;

        /// <summary>
        /// Starts recording
        /// </summary>
        /// <param name="filename">Path of the new recording file</param>
        /// <param name="useRecommendedExtension">You can force the output container's format or use the recommended one to avoid incompatibility</param>
        public void StartRecording(ref string filename, bool useRecommendedExtension = true)
        {
            decoder.StartRecording(ref filename, useRecommendedExtension);
            IsRecording = VideoDemuxer.IsRecording;
        }

        /// <summary>
        /// Stops recording
        /// </summary>
        public void StopRecording()
        {
            decoder.StopRecording();
            IsRecording = VideoDemuxer.IsRecording;
        }

        /// <summary>
        /// Renderer's adapter attached screen width
        /// </summary>
        public int          ScreenWidth         { get => _ScreenWidth;      private set => Set(ref _ScreenWidth, value); }
        int _ScreenWidth;

        /// <summary>
        /// Renderer's adapter attached screen height
        /// </summary>
        public int          ScreenHeight        { get => _ScreenHeight;     private set => Set(ref _ScreenHeight, value); }
        int _ScreenHeight;


        /// <summary>
        /// Saves the current video frame to bitmap file
        /// </summary>
        /// <param name="filename"></param>
        public void TakeSnapshot(string filename)
        {
            renderer?.TakeSnapshot(filename);
        }

        /// <summary>
        /// Pan X Offset to change the X location
        /// </summary>
        public int PanXOffset                   { get => renderer.PanXOffset; set => renderer.PanXOffset = value; }

        /// <summary>
        /// Pan Y Offset to change the Y location
        /// </summary>
        public int PanYOffset                   { get => renderer.PanYOffset; set => renderer.PanYOffset = value; }

        /// <summary>
        /// Pan zoom in/out per pixel of each side (should be based on Control's width/height)
        /// </summary>
        public int Zoom
        {
            get => renderer.Zoom;
            set {  renderer.Zoom = value; Set(ref _Zoom, renderer.Zoom); }
        }
        int _Zoom;
        #endregion

        #region DecoderContext Properties / Events Exposure
        /// <summary>
        /// Player's Decoder Context. Normally you should not access this directly.
        /// </summary>
        public DecoderContext       decoder;
        /// <summary>
        /// Player's Renderer. Normally you should not access this directly.
        /// </summary>
        public Renderer             renderer => decoder?.VideoDecoder?.Renderer;

        public Demuxer              AudioDemuxer        => decoder.AudioDemuxer;
        public Demuxer              VideoDemuxer        => decoder.VideoDemuxer;
        public Demuxer              SubtitlesDemuxer    => decoder.SubtitlesDemuxer;

        public VideoDecoder         VideoDecoder        => decoder.VideoDecoder;
        public AudioDecoder         AudioDecoder        => decoder.AudioDecoder;
        public SubtitlesDecoder     SubtitlesDecoder    => decoder.SubtitlesDecoder;
        #endregion

        #region Properties Internal
        Thread tSeek, tPlay;
        object lockOpen         = new object();
        object lockSeek         = new object();
        object lockPlayPause    = new object();
        object lockSubtitles    = new object();

        ConcurrentStack<SeekData> seeks = new ConcurrentStack<SeekData>();
        ConcurrentStack<OpenData> opens = new ConcurrentStack<OpenData>();
        ConcurrentStack<OpenInputData> inputopens = new ConcurrentStack<OpenInputData>();

        class OpenData
        {
            public object url_iostream;
            public bool defaultInput;
            public bool defaultAudio;
            public bool defaultVideo;
            public bool defaultSubtitles;
            public OpenData(object url_iostream, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
                { this.url_iostream = url_iostream; this.defaultInput = defaultInput; this.defaultVideo = defaultVideo; this.defaultAudio = defaultAudio; this.defaultSubtitles = defaultSubtitles; }
        }

        class OpenInputData
        {
            public InputBase input;
            public bool resync;
            public bool defaultAudio;
            public bool defaultVideo;
            public bool defaultSubtitles;
            public OpenInputData(InputBase input, bool resync = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
                { this.input = input; this.resync = resync; this.defaultVideo = defaultVideo; this.defaultAudio = defaultAudio; this.defaultSubtitles = defaultSubtitles; }
        }

        class SeekData
        {
            public int  ms;
            public bool foreward;
            public SeekData(int ms, bool foreward)
                { this.ms = ms; this.foreward = foreward; }
        }

        VideoFrame      vFrame;
        AudioFrame      aFrame;
        SubtitlesFrame  sFrame, sFramePrev;
        AudioStream     lastAudioStream;
        SubtitlesStream lastSubtitlesStream;

        bool requiresBuffering;
        int  droppedFrames;

        bool isVideoSwitch;
        bool isAudioSwitch;
        bool isSubsSwitch;

        long elapsedTicks;
        long startedAtTicks;
        long videoStartTicks;
        #endregion

        #region  Constructor / Initialize Control/Decoder
		public Player(Config config = null) : base(config)
        {
            Config.SetPlayer(this);

            Audio       = new AudioInfo(this);
            Video       = new VideoInfo(this);
            Subtitles   = new SubtitlesInfo(this);

            if (Config.Player.Usage == Usage.Audio)
            {
                Config.Video.Enabled = false;
                Config.Subtitles.Enabled = false;
                Log($"Creating (Usage = {Config.Player.Usage}) ... (initializing the decoder)");
                InitializeDecoder();
            }
            else
                Log($"Creating (Usage = {Config.Player.Usage}) ... (1/3 waiting for control to set)");
        }
        private void InitializeControl1(Flyleaf oldValue, Flyleaf newValue)
        {
            lock (this)
            {
                if (newValue == null) return;
                if (oldValue != null && newValue != null && oldValue.Handle == newValue?.Handle) return;

                Log($"Creating (Usage = {Config.Player.Usage}) ... (2/3 waiting for handle to be created)");

                if (newValue.Handle != IntPtr.Zero)
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
                
                Log($"Creating (Usage = {Config.Player.Usage}) ... (3/3 initializing the decoder)");
                InitializeDecoder();
            }
            
        }
        private void InitializeDecoder()
        {
            if (Master.Players.ContainsKey(PlayerId))
            {
                if (Control == null || decoder == null || Config.Player.Usage != Usage.Audio)
                    throw new Exception("PlayerId already exists");
                else
                {
                    VideoDecoder.Dispose();
                    VideoDecoder.DisposeVA();
                    decoder.VideoDecoder = new VideoDecoder(decoder.Config, Control, decoder.UniqueId);
                    VideoDecoder.CodecChanged = Decoder_VideoCodecChanged;
                    Log("Created");
                    return;
                }
            }

            Master.Players.Add(PlayerId, this);
            decoder = new DecoderContext(Config, Control, PlayerId);

            decoder.VideoInputOpened        += Decoder_VideoInputOpened;
            decoder.AudioInputOpened        += Decoder_AudioInputOpened;
            decoder.SubtitlesInputOpened    += Decoder_SubtitlesInputOpened;
            decoder.VideoStreamOpened       += Decoder_VideoStreamOpened;
            decoder.AudioStreamOpened       += Decoder_AudioStreamOpened;
            decoder.SubtitlesStreamOpened   += Decoder_SubtitlesStreamOpened;

            AudioDecoder.CodecChanged        = Decoder_AudioCodecChanged;
            VideoDecoder.CodecChanged        = Decoder_VideoCodecChanged;
            VideoDemuxer.RecordingCompleted += (o, e) => { IsRecording = false; };

            Reset();
            InitializeAudio();

            if (Config.Player.Usage != Usage.Audio)
                renderer.Present();

            Log("Created");
        }
        #endregion

        #region Events
        public class OpenCompletedArgs : EventArgs
        {
            public MediaType    Type            { get; }
            public InputBase    Input           { get; }
            public string       Error           { get; }
            public bool         Success         { get; }
            
            public OpenCompletedArgs(MediaType type, InputBase input, string error)
            {
                Type    = type;
                Input   = input;
                Error   = error;
                Success = Error == null;
            }
        }
        public class OpenInputCompletedArgs : OpenCompletedArgs
        {
            public InputBase    OldInput        { get; }
            public bool         IsUserInput     { get; }
            
            public OpenInputCompletedArgs(MediaType type, InputBase input, InputBase oldInput, string error, bool isUserInput) : base(type, input, error)
            {
                OldInput    = oldInput;
                IsUserInput = isUserInput;
            }
        }
        public class OpenStreamCompletedArgs : EventArgs
        {
            public MediaType    Type            { get; }
            public StreamBase   Stream          { get; }
            public StreamBase   OldStream       { get; }
            public string       Error           { get; }
            public bool         Success         { get; }

            public OpenStreamCompletedArgs(MediaType type, StreamBase stream, StreamBase oldStream, string error)
            {
                Type        = type;
                Stream      = stream;
                OldStream   = oldStream;
                Error       = error;
                Success     = Error == null;
            }
        }

        /// <summary>
        /// Fires on open completed of new media input (success or failure)
        /// </summary>
        public event EventHandler<OpenCompletedArgs> OpenCompleted;

        /// <summary>
        /// Fires on open completed of an existing media input (success or failure)
        /// </summary>
        public event EventHandler<OpenInputCompletedArgs> OpenInputCompleted;

        /// <summary>
        /// Fires on open completed of an existing media stream (success or failure)
        /// </summary>
        public event EventHandler<OpenStreamCompletedArgs> OpenStreamCompleted;

        /// <summary>
        /// Fires on playback ended successfully
        /// </summary>
        public event EventHandler PlaybackCompleted;

        protected virtual void OnOpenCompleted(OpenCompletedArgs e) { OnOpenCompleted(e.Type, e.Input, e.Error); }
        protected virtual void OnOpenCompleted(MediaType type, InputBase input, string error) { OpenCompleted?.Invoke(this, new OpenCompletedArgs(type, input, error)); }

        protected virtual void OnOpenInputCompleted(OpenInputCompletedArgs e) { OnOpenInputCompleted(e.Type, e.Input, e.OldInput, e.Error, e.IsUserInput); }
        protected virtual void OnOpenInputCompleted(MediaType type, InputBase input, InputBase oldInput, string error, bool isUserInput) { OpenInputCompleted?.Invoke(this, new OpenInputCompletedArgs(type, input, oldInput, error, isUserInput)); }

        protected virtual void OnOpenStreamCompleted(OpenStreamCompletedArgs e) { OnOpenStreamCompleted(e.Type, e.Stream, e.OldStream, e.Error); }
        protected virtual void OnOpenStreamCompleted(MediaType type, StreamBase stream, StreamBase oldStream, string error) { OpenStreamCompleted?.Invoke(this, new OpenStreamCompletedArgs(type, stream, oldStream, error)); }

        protected virtual void OnPlaybackCompleted() { Task.Run(() => PlaybackCompleted?.Invoke(this, new EventArgs())); }
        #endregion

        #region Decoder Events
        private void Decoder_AudioCodecChanged(DecoderBase x)
        {
            InitializeAudio(AudioDecoder.CodecCtx->sample_rate);
        }
        private void Decoder_VideoCodecChanged(DecoderBase x)
        {
            Video.VideoAcceleration = VideoDecoder.VideoAccelerated;
        }

        private void Decoder_AudioStreamOpened(object sender, AudioStreamOpenedArgs e)
        {
            Config.Audio.SetDelay(0);
            Audio.Refresh();
            lastAudioStream = e.Stream;
            CanPlay = Video.IsOpened || Audio.IsOpened ? true : false;

            OnOpenStreamCompleted(MediaType.Audio, e.Stream, e.OldStream, e.Error);
        }
        private void Decoder_VideoStreamOpened(object sender, VideoStreamOpenedArgs e)
        {
            Video.Refresh();
            CanPlay = Video.IsOpened || Audio.IsOpened ? true : false;

            OnOpenStreamCompleted(MediaType.Video, e.Stream, e.OldStream, e.Error);
        }
        private void Decoder_SubtitlesStreamOpened(object sender, SubtitlesStreamOpenedArgs e)
        {
            Config.Subtitles.SetDelay(0);
            Subtitles.Refresh();
            lastSubtitlesStream = e.Stream;

            OnOpenStreamCompleted(MediaType.Subs, e.Stream, e.OldStream, e.Error);
        }

        private void Decoder_AudioInputOpened(object sender, AudioInputOpenedArgs e)
        {
            if (decoder.VideoStream == null)
            {
                if (e.Input != null && e.Input.InputData != null)
                    Title = e.Input.InputData.Title;

                if (e.Success)
                {
                    var curDemuxer = !VideoDemuxer.Disposed ? VideoDemuxer : AudioDemuxer;
                    Duration= curDemuxer.Duration;
                    IsLive  = curDemuxer.IsLive;
                }
                else
                {
                    if (!CanPlay) Status = Status.Failed;
                    ResetMe();
                }
            }

            if (e.IsUserInput)
                OnOpenCompleted(new OpenInputCompletedArgs(MediaType.Audio, e.Input, e.OldInput, e.Error, e.IsUserInput));
            else
                OnOpenInputCompleted(MediaType.Audio, e.Input, e.OldInput, e.Error, e.IsUserInput);
        }
        private void Decoder_VideoInputOpened(object sender, VideoInputOpenedArgs e)
        {
            if (e.Input != null && e.Input.InputData != null)
                Title = e.Input.InputData.Title;

            if (e.Success)
            {
                Duration= VideoDemuxer.Duration;
                IsLive  = VideoDemuxer.IsLive;
            }
            else
            {
                if (!CanPlay) Status = Status.Failed;
                ResetMe();
            }

            if (e.IsUserInput)
                OnOpenCompleted(new OpenInputCompletedArgs(MediaType.Video, e.Input, e.OldInput, e.Error, e.IsUserInput));
            else
                OnOpenInputCompleted(MediaType.Video, e.Input, e.OldInput, e.Error, e.IsUserInput);
        }
        private void Decoder_SubtitlesInputOpened(object sender, SubtitlesInputOpenedArgs e)
        {
            if (e.Success)
                lock (lockSubtitles) ReSync(decoder.SubtitlesStream, decoder.GetCurTimeMs());

            if (e.IsUserInput)
                OnOpenCompleted(new OpenInputCompletedArgs(MediaType.Subs, e.Input, e.OldInput, e.Error, e.IsUserInput));
            else
                OnOpenInputCompleted(MediaType.Subs, e.Input, e.OldInput, e.Error, e.IsUserInput);
        }
        #endregion

        #region Initialize Open/Switch
        private void ResetMe()
        {
            ClearAudioBuffer();
            if (renderer != null)
            {
                renderer.DisableRendering = true;
                renderer.Present();
            }

            CurTime     = 0;
            Duration    = 0;
            IsLive      = false;
            BitRate     = 0;
            BufferedDuration = 0;
            Folder      = "";
            FileSize    = 0;
        }
        private void Reset()
        {
            ResetMe();
            Video.Reset();
            Audio.Reset();
            Subtitles.Reset();

            lastAudioStream = null;
            lastSubtitlesStream = null;
        }
        private void Initialize()
        {
            try
            {
                Log($"[Initializing]");

                TimeBeginPeriod(1);

                Status  = Status.Stopped;
                CanPlay = false;
                seeks.Clear();
                EnsureThreadDone(tSeek);
                EnsureThreadDone(tPlay);
                decoder.Initialize();
                Title = "";
                Reset();

                Log($"[Initialized]");

            } catch (Exception e)
            {
                Log($"Initialize() Error: {e.Message} - check TimeBeginPeriod / TimeEndPeriod");

            } finally
            {
                TimeEndPeriod(1);
            }

        }
        #endregion

        #region Open
        private OpenCompletedArgs OpenInternal(object url_iostream, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            InputOpenedArgs args = null;

            try
            {
                Log($"Opening {url_iostream.ToString()}");

                if ((url_iostream is string) && SubsExts.Contains(GetUrlExtention(url_iostream.ToString())))
                {
                    //if (!Video.IsOpened) return "Cannot open subtitles without having video";
                    Config.Subtitles.SetEnabled(true);
                    args = decoder.OpenSubtitles(url_iostream.ToString(), defaultSubtitles);
                    ReSync(decoder.SubtitlesStream);
                    return new OpenInputCompletedArgs(args is VideoInputOpenedArgs ? MediaType.Video : (args is AudioInputOpenedArgs ? MediaType.Audio : MediaType.Subs), args.Input, args.OldInput, args.Error, args.IsUserInput);
                }

                Initialize();
                Status = Status.Opening;

                if (Config.Player.Usage == Usage.Audio)
                {
                    if (url_iostream is Stream)
                        args = (InputOpenedArgs) decoder.OpenAudio((Stream)url_iostream, defaultInput, defaultAudio);
                    else
                        args = (InputOpenedArgs) decoder.OpenAudio(url_iostream.ToString(), defaultInput, defaultAudio);
                }
                else
                {
                    if (url_iostream is Stream)
                        args = decoder.OpenVideo((Stream)url_iostream, defaultInput, defaultVideo, defaultAudio, defaultSubtitles);
                    else
                        args = decoder.OpenVideo(url_iostream.ToString(), defaultInput, defaultVideo, defaultAudio, defaultSubtitles);

                    // Video Fails try Audio Input
                    if (!args.Success && defaultInput && decoder.OpenedPlugin != null && decoder.OpenedPlugin.IsPlaylist == false)
                    {
                        if (url_iostream is Stream)
                            args = (InputOpenedArgs) decoder.OpenAudio((Stream)url_iostream, defaultInput, defaultAudio);
                        else
                            args = (InputOpenedArgs) decoder.OpenAudio(url_iostream.ToString(), defaultInput, defaultAudio);
                    }
                }

            } catch (Exception e)
            {
                Log($"[OPEN] Error {e.Message}");
                return new OpenInputCompletedArgs(args is VideoInputOpenedArgs ? MediaType.Video : (args is AudioInputOpenedArgs ? MediaType.Audio : MediaType.Subs), args.Input, args.OldInput, e.Message + "\r\n" + args.Error, args.IsUserInput);
            }

            return new OpenInputCompletedArgs(args is VideoInputOpenedArgs ? MediaType.Video : (args is AudioInputOpenedArgs ? MediaType.Audio : MediaType.Subs), args.Input, args.OldInput, args.Error, args.IsUserInput);
        }
        private void OpenAsync(object url_iostream, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            Task.Run(() =>
            {
                lock(lockOpen) 
                {
                    opens.Push(new OpenData(url_iostream, defaultInput, defaultVideo, defaultAudio, defaultSubtitles));
                    if (IsOpening || IsOpeningInput)
                    {
                        // Interrupt only subs?
                        if (!((url_iostream is string) && SubsExts.Contains(GetUrlExtention(url_iostream.ToString()))))
                            decoder.Interrupt = true;

                        if (IsOpening) return;
                    }
                    IsOpening = true; 
                }

                while (opens.TryPop(out OpenData openData))
                {
                    lock (lockPlayPause)
                    {
                        opens.Clear();
                        OpenInternal(openData.url_iostream, openData.defaultInput, openData.defaultVideo, openData.defaultAudio, openData.defaultSubtitles);
                    }
                }

                lock(lockOpen) IsOpening = false;
            });
        }
        
        /// <summary>
        /// Opens a new media file (audio/subtitles/video)
        /// </summary>
        /// <param name="url">Media file's url</param>
        /// <param name="defaultInput">Whether to open the default input (in case of multiple inputs eg. from bitswarm/youtube-dl, you might want to choose yours)</param>
        /// <param name="defaultVideo">Whether to open the default video stream from plugin suggestions</param>
        /// <param name="defaultAudio">Whether to open the default audio stream from plugin suggestions</param>
        /// <param name="defaultSubtitles">Whether to open the default subtitles stream from plugin suggestions</param>
        /// <returns></returns>
        public OpenCompletedArgs Open(string url, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            return OpenInternal(url, defaultInput, defaultVideo, defaultAudio, defaultSubtitles);
        }

        /// <summary>
        /// Opens a new media stream (audio/video)
        /// </summary>
        /// <param name="iostream">Media stream</param>
        /// <param name="defaultInput">Whether to open the default input (in case of multiple inputs eg. from bitswarm/youtube-dl, you might want to choose yours)</param>
        /// <param name="defaultVideo">Whether to open the default video stream from plugin suggestions</param>
        /// <param name="defaultAudio">Whether to open the default audio stream from plugin suggestions</param>
        /// <param name="defaultSubtitles">Whether to open the default subtitles stream from plugin suggestions</param>
        /// <returns></returns>
        public OpenCompletedArgs Open(Stream iostream, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            return OpenInternal(iostream, defaultInput, defaultVideo, defaultAudio, defaultSubtitles);
        }
        
        /// <summary>
        /// Opens a new media file (audio/subtitles/video) without blocking
        /// You can get the results from <see cref="OpenCompleted"/>
        /// </summary>
        /// <param name="url">Media file's url</param>
        /// <param name="defaultInput">Whether to open the default input (in case of multiple inputs eg. from bitswarm/youtube-dl, you might want to choose yours)</param>
        /// <param name="defaultVideo">Whether to open the default video stream from plugin suggestions</param>
        /// <param name="defaultAudio">Whether to open the default audio stream from plugin suggestions</param>
        /// <param name="defaultSubtitles">Whether to open the default subtitles stream from plugin suggestions</param>
        public void OpenAsync(string url, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            OpenAsync((object)url, defaultInput, defaultVideo, defaultAudio, defaultSubtitles);
        }

        /// <summary>
        /// Opens a new media I/O stream (audio/video) without blocking
        /// You can get the results from <see cref="OpenCompleted"/>
        /// </summary>
        /// <param name="iostream">Media stream</param>
        /// <param name="defaultInput">Whether to open the default input (in case of multiple inputs eg. from bitswarm/youtube-dl, you might want to choose yours)</param>
        /// <param name="defaultVideo">Whether to open the default video stream from plugin suggestions</param>
        /// <param name="defaultAudio">Whether to open the default audio stream from plugin suggestions</param>
        /// <param name="defaultSubtitles">Whether to open the default subtitles stream from plugin suggestions</param>
        public void OpenAsync(Stream iostream, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            OpenAsync((object)iostream, defaultInput, defaultVideo, defaultAudio, defaultSubtitles);
        }

        /// <summary>
        /// Opens an existing media input (audio/subtitles/video)
        /// </summary>
        /// <param name="input">An existing Player's media input</param>
        /// <param name="resync">Whether to force resync with other streams</param>
        /// <param name="defaultVideo">Whether to open the default video stream from plugin suggestions</param>
        /// <param name="defaultAudio">Whether to open the default audio stream from plugin suggestions</param>
        /// <param name="defaultSubtitles">Whether to open the default subtitles stream from plugin suggestions</param>
        /// <returns></returns>
        public OpenInputCompletedArgs Open(InputBase input, bool resync = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            InputOpenedArgs args;
            long syncMs = decoder.GetCurTimeMs();

            if (input is AudioInput)
            {
                if (decoder.VideoStream == null) requiresBuffering = true;
                isAudioSwitch = true;
                Config.Audio.SetEnabled(true);
                args = decoder.OpenAudioInput((AudioInput)input, defaultAudio);
                if (resync) ReSync(decoder.AudioStream, syncMs);
                isAudioSwitch = false;
            }
            else if (input is VideoInput)
            {
                // Going from AudioOnly to Video
                bool shouldPlay = false;
                if (IsPlaying && !Video.IsOpened)
                {
                    shouldPlay = true;
                    Pause();
                }

                isVideoSwitch = true;
                requiresBuffering = true;

                decoder.Stop();
                args = decoder.OpenVideoInput((VideoInput)input, defaultVideo, defaultAudio, defaultSubtitles);

                if (!((IOpen)input.Plugin).IsPlaylist)
                {
                    if (resync) ReSync(decoder.VideoStream, syncMs); else isVideoSwitch = false;
                }
                else
                {
                    isVideoSwitch = false;

                    if (!IsPlaying && resync)
                    {
                        decoder.GetVideoFrame();
                        ShowOneFrame();
                    }
                }

                if (shouldPlay) Play();
            }
            else
            {
                if (!Video.IsOpened) return new OpenInputCompletedArgs(MediaType.Subs, input, null, "Subtitles require opened video stream", false); // Could be closed?
                Config.Subtitles.SetEnabled(true);
                args = decoder.OpenSubtitlesInput((SubtitlesInput)input, defaultSubtitles);
            }

            return new OpenInputCompletedArgs(args is VideoInputOpenedArgs ? MediaType.Video : (args is AudioInputOpenedArgs ? MediaType.Audio : MediaType.Subs), args.Input, args.OldInput, args.Error, args.IsUserInput);
        }

        /// <summary>
        /// Opens an existing media input (audio/subtitles/video) without blocking
        /// You can get the results from <see cref="OpenInputCompleted"/>
        /// </summary>
        /// <param name="input">An existing Player's media input</param>
        /// <param name="resync">Whether to force resync with other streams</param>
        /// <param name="defaultVideo">Whether to open the default video stream from plugin suggestions</param>
        /// <param name="defaultAudio">Whether to open the default audio stream from plugin suggestions</param>
        /// <param name="defaultSubtitles">Whether to open the default subtitles stream from plugin suggestions</param>
        public void OpenAsync(InputBase input, bool resync = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            Task.Run(() =>
            {
                lock(lockOpen) 
                { 
                    inputopens.Push(new OpenInputData(input, resync, defaultVideo, defaultAudio, defaultSubtitles));
                    if (IsOpening || IsOpeningInput)
                    {
                        // Interrupt only subs?
                        if (!(input is SubtitlesInput))
                            decoder.Interrupt = true;

                        if (IsOpeningInput) return;
                    }
                    IsOpeningInput = true; 
                }

                while (inputopens.TryPop(out OpenInputData openData))
                {
                    lock (lockPlayPause)
                    {
                        inputopens.Clear();
                        Open(openData.input, openData.resync, openData.defaultVideo, openData.defaultAudio, openData.defaultSubtitles);
                    }
                }

                lock(lockOpen) IsOpeningInput = false;
            });
        }

        /// <summary>
        /// Opens an existing media stream (audio/subtitles/video)
        /// </summary>
        /// <param name="stream">An existing Player's media stream</param>
        /// <param name="resync">Whether to force resync with other streams</param>
        /// <param name="defaultAudio">Whether to re-suggest audio based on the new video stream (has effect only on VideoStream)</param>
        /// <returns></returns>
        public OpenStreamCompletedArgs Open(StreamBase stream, bool resync = true, bool defaultAudio = true)
        {
            StreamOpenedArgs args = new StreamOpenedArgs();
            long syncMs = decoder.GetCurTimeMs();

            if (stream.Demuxer.Type == MediaType.Video) { isVideoSwitch = true; requiresBuffering = true; }

            if (stream is AudioStream)
            {
                Config.Audio.SetEnabled(true);
                args = decoder.OpenAudioStream((AudioStream)stream);
            }
            else if (stream is VideoStream)
                args = decoder.OpenVideoStream((VideoStream)stream, defaultAudio);
            else if (stream is SubtitlesStream)
            {
                Config.Subtitles.SetEnabled(true);
                args = decoder.OpenSubtitlesStream((SubtitlesStream)stream);
            }

            if (resync) ReSync(stream, syncMs); else isVideoSwitch = false;

            return new OpenStreamCompletedArgs(stream.Type, args.Stream, args.OldStream, args.Error);
        }

        /// <summary>
        /// Opens an existing media stream (audio/subtitles/video) without blocking
        /// You can get the results from <see cref="OpenStreamCompleted"/>
        /// </summary>
        /// <param name="stream">An existing Player's media stream</param>
        /// <param name="resync">Whether to force resync with other streams</param>
        /// <param name="defaultAudio">Whether to re-suggest audio based on the new video stream (has effect only on VideoStream)</param>
        public void OpenAsync(StreamBase stream, bool resync = true, bool defaultAudio = true)
        {
            Task.Run(() =>
            {
                Open(stream, resync, defaultAudio);
            });
        }
        #endregion

        #region Dynamic Config Commands
        internal void SetAudioDelay()
        {
            ReSync(decoder.AudioStream);
        }
        internal void SetSubsDelay()
        {
            ReSync(decoder.SubtitlesStream);
        }

        internal void DisableAudio()
        {
            if (!Audio.IsOpened) return;

            lastAudioStream = decoder.AudioStream;
            AudioDecoder.Dispose(true);

            aFrame = null;
            ClearAudioBuffer();
            Audio.Reset();
        }
        internal void DisableVideo()
        {
            if (!Video.IsOpened || Config.Player.Usage == Usage.Audio) return;

            bool wasPlaying = IsPlaying;
            Pause();
            VideoDecoder.Dispose(true);
            if (!AudioDecoder.OnVideoDemuxer) VideoDemuxer.Dispose();
            Video.Refresh();
            if (wasPlaying) Play();
        }
        internal void DisableSubs()
        {
            if (!Subtitles.IsOpened || Config.Player.Usage != Usage.AVS) return;

            lastSubtitlesStream = decoder.SubtitlesStream;
            SubtitlesDecoder.Dispose(true);

            sFrame = null;
            Subtitles.Reset();
        }
        internal void EnableAudio()
        {
            if (!CanPlay) return;

            AudioInput suggestedInput = null;

            if (lastAudioStream == null)
                decoder.SuggestAudio(out lastAudioStream, out suggestedInput, VideoDemuxer.AudioStreams);

            if (lastAudioStream != null)
            {
                if (lastAudioStream.AudioInput != null)
                    Open(lastAudioStream.AudioInput);
                else
                    Open(lastAudioStream);
            }
            else if (suggestedInput != null)
                Open(suggestedInput);
        }
        internal void EnableVideo()
        {
            if (!CanPlay || Config.Player.Usage == Usage.Audio) return;

            bool wasPlaying = IsPlaying;
            int curTime = decoder.GetCurTimeMs();
            Pause();
            decoder.OpenSuggestedVideo();
            Video.Refresh();
            decoder.Seek(curTime);
            if (wasPlaying) Play();

        }
        internal void EnableSubs()
        {
            if (!CanPlay || Config.Player.Usage != Usage.AVS) return;

            SubtitlesInput suggestedInput = null;

            if (lastSubtitlesStream == null)
                decoder.SuggestSubtitles(out lastSubtitlesStream, out suggestedInput, VideoDemuxer.SubtitlesStreams);

            if (lastSubtitlesStream != null)
            {
                if (lastSubtitlesStream.SubtitlesInput != null)
                    Open(lastSubtitlesStream.SubtitlesInput);
                else
                    Open(lastSubtitlesStream);
            }
            else if (suggestedInput != null)
                Open(suggestedInput);
        }

        private void ReSync(StreamBase stream, long syncMs = -1)
        {
            /* TODO
             * 
             * HLS live resync on stream switch should be from the end not from the start (could have different cache/duration)
             */

            if (stream == null) return;
            //if (stream == null || (syncMs == 0 || (syncMs == -1 && decoder.GetCurTimeMs() == 0))) return; // Avoid initial open resync?

            if (stream.Demuxer.Type == MediaType.Video)
            {
                isVideoSwitch = true;
                isAudioSwitch = true;
                isSubsSwitch = true;
                requiresBuffering = true;

                decoder.Seek(syncMs);

                aFrame = null;
                isAudioSwitch = false;
                isVideoSwitch = false;
                sFrame = null;
                Subtitles.SubsText = "";
                isSubsSwitch = false;

                if (!IsPlaying)
                {
                    decoder.GetVideoFrame();
                    ShowOneFrame();
                }
            }
            else
            {
                if (stream.Demuxer.Type == MediaType.Audio)
                {
                    isAudioSwitch = true;
                    decoder.SeekAudio();
                    aFrame = null;
                    isAudioSwitch = false;
                }
                else
                {
                    isSubsSwitch = true;
                    decoder.SeekSubtitles();
                    sFrame = null;
                    Subtitles.SubsText = "";
                    isSubsSwitch = false;
                }

                if (IsPlaying)
                {
                    stream.Demuxer.Start();
                    decoder.GetDecoderPtr(stream.Type).Start();
                }
            }    
        }

        internal void SetSpeed()
        {
            isVideoSwitch = true;
            requiresBuffering = true;
            VideoDecoder.Speed      = Config.Player.Speed;
            AudioDecoder.Speed      = Config.Player.Speed;
            SubtitlesDecoder.Speed  = Config.Player.Speed;

            VideoDecoder.Flush();
            AudioDecoder.Flush();
            SubtitlesDecoder.Flush();
            isVideoSwitch = false;
        }
        #endregion

        #region Playback
        /// <summary>
        /// Plays AVS streams
        /// </summary>
        public void Play()
        {
            lock (lockPlayPause)
            {
                if (!CanPlay || Status == Status.Playing) return;

                Status = Status.Playing;
                EnsureThreadDone(tSeek);
                EnsureThreadDone(tPlay);

                tPlay = new Thread(() =>
                {
                    try
                    {
                        TimeBeginPeriod(1);
                        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED);

                        if (Config.Player.Usage == Usage.LowLatencyVideo)
                            ScreamerLowLatency();
                        else if (Config.Player.Usage == Usage.Audio || !Video.IsOpened)
                            ScreamerAudioOnly();
                        else
                            Screamer();

                    } catch (Exception e) { Log(e.Message + " - " + e.StackTrace); }

                    finally
                    {
                        if (Status == Status.Stopped) decoder?.Stop(); else decoder?.PauseOnQueueFull();
                        ClearAudioBuffer();
                        VideoDecoder.DisposeFrame(vFrame); vFrame = null;
                        TimeEndPeriod(1);
                        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                        Status = HasEnded ? Status.Ended : Status.Paused;
                        if (HasEnded) OnPlaybackCompleted();
                    }
                });
                tPlay.Name = "Play";
                tPlay.IsBackground = true;
                //tPlay.SetApartmentState(ApartmentState.STA);
                //tPlay.Priority = ThreadPriority.Highest;
                tPlay.Start();
            }
        }

        /// <summary>
        /// Pauses AVS streams
        /// </summary>
        public void Pause()
        {
            lock (lockPlayPause)
            {
                if (!CanPlay || Status == Status.Ended) return;

                Status = Status.Paused;
                EnsureThreadDone(tPlay);
            }
        }

        /// <summary>
        /// Seeks backwards or forewards based on the specified ms to the nearest keyframe
        /// </summary>
        /// <param name="ms"></param>
        /// <param name="foreward"></param>
        public void Seek(int ms, bool foreward = false)
        {
            if (!CanPlay) return;

            SetCurTime(ms * (long)10000);
            seeks.Push(new SeekData(ms, foreward));

            BufferedDuration = 0;
            decoder.OpenedPlugin?.OnBuffering();

            if (Status == Status.Playing) return;

            lock (lockSeek) { if (IsSeeking) return; IsSeeking = true; }

            tSeek = new Thread(() =>
            {
                try
                {
                    TimeBeginPeriod(1);

                    while (seeks.TryPop(out SeekData seekData) && CanPlay && !IsPlaying)
                    {
                        seeks.Clear();

                        if (!Video.IsOpened)
                        {
                            if (AudioDecoder.OnVideoDemuxer)
                            {
                                if (decoder.Seek(seekData.ms, seekData.foreward) < 0)
                                    Log("[SEEK] Failed 2");

                                VideoDemuxer.Start();
                            }
                            else
                            {
                                if (decoder.SeekAudio(seekData.ms, seekData.foreward) < 0)
                                    Log("[SEEK] Failed 3");

                                AudioDemuxer.Start();
                            }

                            decoder.PauseOnQueueFull();
                        }
                        else
                        {
                            VideoDecoder.Pause();
                            if (decoder.Seek(seekData.ms, seekData.foreward) >= 0)
                            {
                                if (CanPlay)
                                    decoder.GetVideoFrame();
                                if (CanPlay)
                                {
                                    ShowOneFrame();
                                    VideoDemuxer.Start();
                                    AudioDemuxer.Start();
                                    SubtitlesDemuxer.Start();
                                    decoder.PauseOnQueueFull();
                                }
                            }
                            else
                                Log("[SEEK] Failed");
                        }

                        Thread.Sleep(20);
                    }
                } catch (Exception e)
                {
                    Log($"[SEEK] Error {e.Message}");
                } finally
                {
                    decoder.OpenedPlugin?.OnBufferingCompleted();
                    TimeEndPeriod(1);
                    lock (lockSeek) IsSeeking = false;
                }
            });

            tSeek.Name = "Seek";
            tSeek.IsBackground = true;
            tSeek.Start();
        }

        /// <summary>
        /// Stops and Closes AVS streams
        /// </summary>
        public void Stop()
        {
            lock (this)
            {
                Status = Status.Stopped;
                EnsureThreadDone(tPlay);
                if (disposed || decoder == null) return;
                decoder.Stop();
                lock (lockSeek)
                    lock (lockOpen)
                        Initialize();
            }
        }

        /// <summary>
        /// Disposes the Player and the hosted VideoView if any
        /// </summary>
        public void Dispose() { Master.DisposePlayer(this); }

        internal void DisposeInternal()
        {
            lock (this)
            {
                if (disposed) return;

                try
                {
                    DisableNotifications = true;
                    Stop();

                    DisposeAudio(); 
                    decoder.Dispose();

                    decoder = null;
                    Config = null;

                    disposed = true;

                    if (_Control != null && VideoView != null && VideoView.WindowFront != null && !VideoView.WindowFront.Disposing)
                    {
                        if (VideoView.WindowFront.Disposed) return;

                        _Control.BeginInvoke(new Action(() => { VideoView?.WindowFront?.Close(); GC.Collect(); } ));
                        return;
                    }
                    else
                        GC.Collect();
                } catch (Exception) { }
            }
        }
        bool disposed = false;
        #endregion

        #region Scream
        private void ShowOneFrame()
        {
            Subtitles.SubsText = ""; sFrame = null;

            if (VideoDecoder.Frames.Count > 0)
            {
                VideoFrame vFrame = null;
                VideoDecoder.Frames.TryDequeue(out vFrame);

                long tmpTimestamp = vFrame.timestamp;
                renderer.Present(vFrame);

                // Required for buffering on paused
                if (decoder.RequiresResync && !IsPlaying && seeks.Count == 0)
                    decoder.Resync(vFrame.timestamp);

                Action refresh = new Action(() =>
                {
                    if (seeks.Count == 0)
                    {
                        BufferedDuration = VideoDemuxer.BufferedDuration;
                        if (VideoDemuxer.HLSPlaylist != null)
                            SetCurTimeHLS();
                        else
                            SetCurTime(tmpTimestamp * Config.Player.Speed);
                    }
                });
                if (seeks.Count == 0) _Control?.BeginInvoke(refresh);
            }
            return;
        }

        private bool MediaBuffer()
        {
            Log("[SCREAMER] Buffering ...");
            IsBuffering = true;

            while (isVideoSwitch && IsPlaying) Thread.Sleep(10);

            ClearAudioBuffer();

            VideoDemuxer.Start();
            VideoDecoder.Start();
             
            if (Config.Audio.Enabled)
            {
                if (AudioDecoder.OnVideoDemuxer)
                    AudioDecoder.Start();
                else if (!decoder.RequiresResync)
                {
                    AudioDemuxer.Start();
                    AudioDecoder.Start();
                }
            }

            if (Config.Subtitles.Enabled)
            {
                lock (lockSubtitles)
                if (SubtitlesDecoder.OnVideoDemuxer)
                    SubtitlesDecoder.Start();
                else if (!decoder.RequiresResync)
                {
                    SubtitlesDemuxer.Start();
                    SubtitlesDecoder.Start();
                }
            }

            VideoDecoder.DisposeFrame(vFrame);
            vFrame = null;
            aFrame = null;
            sFrame = null;
            sFramePrev = null;
            _Control?.BeginInvoke(new Action(() => Subtitles.SubsText = "" ));
            
            bool gotAudio       = !Audio.IsOpened;
            bool gotVideo       = false;
            bool shouldStop     = false;
            bool showOneFrame   = true;
            int  audioRetries   = 3;

            // Wait 1: Ensure we have video/audio frame
            do
            {
                if (showOneFrame && VideoDecoder.Frames.Count != 0)
                {
                    ShowOneFrame();
                    if (seeks.Count != 0) return false; 

                    showOneFrame = false; 
                }

                if (vFrame == null && VideoDecoder.Frames.Count != 0)
                {
                    VideoDecoder.Frames.TryDequeue(out vFrame);
                    if (!showOneFrame) gotVideo = true;
                }

                if (!gotAudio && aFrame == null && AudioDecoder.Frames.Count != 0)
                    AudioDecoder.Frames.TryDequeue(out aFrame);

                if (vFrame != null)
                {
                    if (decoder.RequiresResync)
                        decoder.Resync(vFrame.timestamp);

                    if (!gotAudio && aFrame != null)
                    {
                        for (int i=0; i<Math.Min(20, AudioDecoder.Frames.Count); i++)
                        {
                            if (aFrame == null || vFrame.timestamp - aFrame.timestamp < Config.Audio.Latency) { gotAudio = true; break; }

                            Log("Drop AFrame  " + TicksToTime(aFrame.timestamp));
                            AudioDecoder.Frames.TryDequeue(out aFrame);
                        }
                    }
                }

                if (!IsPlaying || HasEnded)
                    shouldStop = true;
                else
                {
                    if (!VideoDecoder.IsRunning && !isVideoSwitch) { Log("[SCREAMER] Video Exhausted"); shouldStop= true; }
                    if (vFrame != null && !gotAudio && audioRetries > 0 && (!AudioDecoder.IsRunning || AudioDecoder.Demuxer.Status == MediaFramework.Status.QueueFull)) { 
                        Log($"[SCREAMER] Audio Exhausted {audioRetries}"); audioRetries--; if (audioRetries < 1) 
                            gotAudio  = true; 
                    }
                }

                Thread.Sleep(10);

            } while (!shouldStop && (!gotVideo || !gotAudio));

            if (shouldStop && !(HasEnded && IsPlaying && vFrame != null)) { Log("[SCREAMER] Stopped"); return false; }
            if (vFrame == null) { Log("[SCREAMER] [ERROR] No Frames!"); return false; }

            while(VideoDemuxer.BufferedDuration < Config.Player.MinBufferDuration && IsPlaying && VideoDemuxer.IsRunning && VideoDemuxer.Status != MediaFramework.Status.QueueFull) Thread.Sleep(20);

            Log("[SCREAMER] Buffering Done");

            if (aFrame != null && aFrame.timestamp < vFrame.timestamp) 
                videoStartTicks = Math.Max(aFrame.timestamp, vFrame.timestamp - Config.Audio.Latency);
            else
                videoStartTicks = vFrame.timestamp - Config.Audio.Latency;

            startedAtTicks  = DateTime.UtcNow.Ticks;

            if (seeks.Count == 0)
            {
                if (VideoDemuxer.HLSPlaylist != null)
                    SetCurTimeHLS();
                else
                    SetCurTime(videoStartTicks * Config.Player.Speed);
            }

            decoder.OpenedPlugin.OnBufferingCompleted();
            Log($"[SCREAMER] Started -> {TicksToTime(videoStartTicks)} | [V: {TicksToTime(vFrame.timestamp)}]" + (aFrame == null ? "" : $" [A: {TicksToTime(aFrame.timestamp)}]"));

            return true;
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
            long    elapsedSec = startedAtTicks;

            requiresBuffering = true;

            while (Status == Status.Playing)
            {
                if (seeks.TryPop(out SeekData seekData))
                {
                    seeks.Clear();
                    requiresBuffering = true;
                    //decoder.OpenedPlugin.OnBuffering();
                    if (decoder.Seek(seekData.ms, seekData.foreward) < 0)
                        Log("[SCREAMER] Seek failed");
                }

                if (requiresBuffering)
                {
                    totalBytes = VideoDemuxer.TotalBytes + AudioDemuxer.TotalBytes + SubtitlesDemuxer.TotalBytes;
                    videoBytes = VideoDemuxer.VideoBytes + AudioDemuxer.VideoBytes + SubtitlesDemuxer.VideoBytes;
                    audioBytes = VideoDemuxer.AudioBytes + AudioDemuxer.AudioBytes + SubtitlesDemuxer.AudioBytes;

                    MediaBuffer();
                    elapsedSec = startedAtTicks;
                    requiresBuffering = false;
                    IsBuffering = false;
                    if (seeks.Count != 0) continue;
                    if (vFrame == null) { Log("MediaBuffer() no video frame"); break; }
                }

                if (vFrame == null)
                {
                    if (VideoDecoder.Status == MediaFramework.Status.Ended)
                    {
                        Status = Status.Ended;
                        if (VideoDemuxer.HLSPlaylist == null)
                            SetCurTime((videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks)) * Config.Player.Speed);
                    }
                    if (Status != Status.Playing) break;

                    Log("[SCREAMER] No video frames");
                    requiresBuffering = true;
                    continue;
                }

                if (Status != Status.Playing) break;

                if (aFrame == null && !isAudioSwitch) AudioDecoder.Frames.TryDequeue(out aFrame);
                if (sFrame == null && !isSubsSwitch ) SubtitlesDecoder.Frames.TryPeek(out sFrame);

                elapsedTicks    = videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks);
                vDistanceMs     = (int) ((vFrame.timestamp - elapsedTicks) / 10000);
                aDistanceMs     = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                sDistanceMs     = sFrame != null ? (int) ((sFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                sleepMs         = Math.Min(vDistanceMs, aDistanceMs) - 1;

                if (sleepMs < 0) sleepMs = 0;
                if (sleepMs > 2)
                {
                    if (sleepMs > 1000)
                    {   // Probably happens only on hls when it refreshes the m3u8 playlist / segments (and we are before the allowed cache)
                        Log($"[SCREAMER] Restarting ... (HLS?) | Distance: {TicksToTime(sleepMs * (long)10000)}");
                        requiresBuffering = true;
                        continue; 
                    }

                    // Every seconds informs the application with CurTime / Bitrates (invokes UI thread to ensure the updates will actually happen)
                    if (Math.Abs(elapsedTicks - elapsedSec) > 10000000)
                    {
                        elapsedSec  = elapsedTicks;

                        Action refresh = new Action(() =>
                        {
                            try
                            {
                                if (Config == null) return;

                                if (Config.Player.Stats)
                                {
                                    long curTotalBytes  =  VideoDemuxer.TotalBytes + AudioDemuxer.TotalBytes + SubtitlesDemuxer.TotalBytes;
                                    long curVideoBytes  =  VideoDemuxer.VideoBytes + AudioDemuxer.VideoBytes + SubtitlesDemuxer.VideoBytes;
                                    long curAudioBytes  =  VideoDemuxer.AudioBytes + AudioDemuxer.AudioBytes + SubtitlesDemuxer.AudioBytes;

                                    BitRate             = (curTotalBytes - totalBytes) * 8 / 1000.0;
                                    Video.BitRate       = (curVideoBytes - videoBytes) * 8 / 1000.0;
                                    Audio.BitRate       = (curAudioBytes - audioBytes) * 8 / 1000.0;
                                    totalBytes          =  curTotalBytes;
                                    videoBytes          =  curVideoBytes;
                                    audioBytes          =  curAudioBytes;

                                    Video.DroppedFrames = droppedFrames;
                                    Video.CurrentFps    = actualFps;
                                    actualFps   = 0;
                                }

                                BufferedDuration = VideoDemuxer.BufferedDuration;
                                if (VideoDemuxer.HLSPlaylist != null)
                                    SetCurTimeHLS();
                                else
                                    SetCurTime(elapsedTicks * Config.Player.Speed);
                            } catch (Exception) { }
                        });
                        _Control?.BeginInvoke(refresh);
                    }

                    Thread.Sleep(sleepMs);
                }

                if (Math.Abs(vDistanceMs - sleepMs) <= 2)
                {
                    //Log($"[V] Presenting {TicksToTime(vFrame.timestamp)}");
                    if (decoder.VideoDecoder.Renderer.Present(vFrame)) actualFps++; else droppedFrames++;
                    VideoDecoder.Frames.TryDequeue(out vFrame);
                }
                else if (vDistanceMs < -2)
                {
                    droppedFrames++;
                    VideoDecoder.DisposeFrame(vFrame);
                    VideoDecoder.Frames.TryDequeue(out vFrame);
                    Log($"vDistanceMs 2 |-> {vDistanceMs}");

                    if (vDistanceMs < -10)
                        requiresBuffering = true;
                    continue;
                }

                if (aFrame != null) // Should use different thread for better accurancy (renderer might delay it on high fps) | also on high offset we will have silence between samples
                {
                    if (Math.Abs(aDistanceMs - sleepMs) <= 10)
                    {
                        //Log($"[A] Presenting {TicksToTime(aFrame.timestamp)}");
                        AddAudioSamples(aFrame.audioData);
                        AudioDecoder.Frames.TryDequeue(out aFrame);
                    }
                    else if (aDistanceMs < -10) // Will be transfered back to decoder to drop invalid timestamps
                    {
                        if (aDistanceMs < -600)
                        {
                            Log($"aDistanceMs 2 |-> {aDistanceMs} | All audio frames disposed");
                            AudioDecoder.DisposeFrames();
                            aFrame = null;
                        }
                        else
                        {
                            int maxdrop = Math.Max(Math.Min((vDistanceMs - sleepMs) - 1, 20), 3);
                            Log($"-=-=-= {maxdrop} -=-=-=");
                            for (int i=0; i<maxdrop; i++)
                            {
                                Log($"aDistanceMs 2 |-> {aDistanceMs}");
                                AudioDecoder.Frames.TryDequeue(out aFrame);
                                aDistanceMs = aFrame != null ? (int) ((aFrame.timestamp - elapsedTicks) / 10000) : Int32.MaxValue;
                                if (aDistanceMs > -7) break;
                            }
                        }
                    }
                }

                if (sFramePrev != null)
                    if (elapsedTicks - sFramePrev.timestamp > (long)sFramePrev.duration * 10000)
                    {
                        _Control?.BeginInvoke(new Action(() => Subtitles.SubsText = ""));
                        sFramePrev = null;
                    }

                if (sFrame != null)
                {
                    if (Math.Abs(sDistanceMs - sleepMs) < 30 || (sDistanceMs < -30 && sFrame.duration + sDistanceMs > 0))
                    {
                        string tmpSubsText = sFrame.text;
                        _Control?.BeginInvoke(new Action(() => Subtitles.SubsText = tmpSubsText));
                        sFramePrev = new SubtitlesFrame();
                        sFramePrev.timestamp = sFrame.timestamp;
                        sFramePrev.duration = sFrame.duration;
                        sFrame = null;
                        SubtitlesDecoder.Frames.TryDequeue(out SubtitlesFrame devnull);
                    }
                    else if (sDistanceMs < -30)
                    {
                        Log($"sDistanceMs 2 |-> {sDistanceMs}");
                        sFrame = null;
                        SubtitlesDecoder.Frames.TryDequeue(out SubtitlesFrame devnull);
                    }
                }
            }
            
            Log($"[SCREAMER] Finished -> {TicksToTime(CurTime)}");
        }

        private void ScreamerLowLatency()
        {
            int     actualFps  = 0;
            long    totalBytes = 0;
            long    videoBytes = 0;

            long    secondTime = DateTime.UtcNow.Ticks;
            long    avgFrameDuration = (int) (10000000.0 / 25.0);
            long    lastPresentTime = 0;

            VideoDecoder.DisposeFrame(vFrame);
            //decoder.Seek(0);
            decoder.Flush();
            VideoDemuxer.Start();
            VideoDecoder.Start();

            if (FFmpeg.AutoGen.ffmpeg.av_q2d(VideoDemuxer.FormatContext->streams[VideoDecoder.VideoStream.StreamIndex]->avg_frame_rate) > 0)
                avgFrameDuration = (long) (10000000 / FFmpeg.AutoGen.ffmpeg.av_q2d(VideoDemuxer.FormatContext->streams[VideoDecoder.VideoStream.StreamIndex]->avg_frame_rate));
            else if (VideoDecoder.VideoStream.Fps > 0)
                avgFrameDuration = (int) (10000000 / VideoDecoder.VideoStream.Fps);

            if (Config.Player.LowLatencyMaxVideoFrames < 1) Config.Player.LowLatencyMaxVideoFrames = 1;

            while (Status == Status.Playing)
            {
                if (vFrame == null)
                {
                    IsBuffering = true;
                    while (VideoDecoder.Frames.Count == 0 && Status == Status.Playing) Thread.Sleep(20);
                    IsBuffering = false;
                    if (Status != Status.Playing) break;

                    while (VideoDecoder.Frames.Count >= Config.Player.LowLatencyMaxVideoFrames && VideoDemuxer.VideoPackets.Count >= Config.Player.LowLatencyMaxVideoPackets)
                    {
                        droppedFrames++;
                        VideoDecoder.DisposeFrame(vFrame);
                        VideoDecoder.Frames.TryDequeue(out vFrame);
                    }

                    if (vFrame == null) VideoDecoder.Frames.TryDequeue(out vFrame);
                }
                else
                {
                    long curTime = DateTime.UtcNow.Ticks;

                    if (curTime - secondTime > 10000000 - avgFrameDuration)
                    {
                        secondTime = curTime;

                        Video.DroppedFrames = droppedFrames;
                        
                        BitRate = (VideoDemuxer.TotalBytes + AudioDemuxer.TotalBytes + SubtitlesDemuxer.TotalBytes - totalBytes) * 8 / 1000.0;
                        Video.BitRate = (VideoDemuxer.VideoBytes + AudioDemuxer.VideoBytes + SubtitlesDemuxer.VideoBytes - videoBytes) * 8 / 1000.0;
                        totalBytes = VideoDemuxer.TotalBytes + AudioDemuxer.TotalBytes + SubtitlesDemuxer.TotalBytes;
                        videoBytes = VideoDemuxer.VideoBytes + AudioDemuxer.VideoBytes + SubtitlesDemuxer.VideoBytes;

                        Video.CurrentFps = actualFps;
                        actualFps = 0;

                        SetCurTime(vFrame.timestamp);
                    }

                    int sleepMs = (int) ((avgFrameDuration - (curTime - lastPresentTime)) / 10000);
                    if (sleepMs < 11000 && sleepMs > 2) Thread.Sleep(sleepMs);
                    if (renderer.Present(vFrame)) actualFps++; else droppedFrames++;
                    lastPresentTime = DateTime.UtcNow.Ticks;
                    vFrame = null;
                }
            }
        }

        private bool AudioBuffer()
        {
            IsBuffering = true;
            while ((isVideoSwitch || isAudioSwitch) && IsPlaying) Thread.Sleep(10);
            if (!IsPlaying) return false;

            aFrame = null;
            ClearAudioBuffer();
            decoder.AudioStream.Demuxer.Start();
            AudioDecoder.Start();

            while(AudioDecoder.Frames.Count == 0 && IsPlaying && AudioDecoder.IsRunning) Thread.Sleep(10);
            AudioDecoder.Frames.TryDequeue(out aFrame);
            if (aFrame == null) 
                return false;

            while(decoder.AudioStream.Demuxer.BufferedDuration < Config.Player.MinBufferDuration && IsPlaying && decoder.AudioStream.Demuxer.IsRunning && decoder.AudioStream.Demuxer.Status != MediaFramework.Status.QueueFull) Thread.Sleep(20);

            if (!IsPlaying || AudioDecoder.Frames.Count == 0)
                return false;

            startedAtTicks  = DateTime.UtcNow.Ticks;
            videoStartTicks = aFrame.timestamp;

            return true;
        }
        private void ScreamerAudioOnly()
        {
            int aDistanceMs;
            long elapsedSec = startedAtTicks;

            long totalBytes = VideoDemuxer.TotalBytes + AudioDemuxer.TotalBytes + SubtitlesDemuxer.TotalBytes;
            long videoBytes = VideoDemuxer.VideoBytes + AudioDemuxer.VideoBytes + SubtitlesDemuxer.VideoBytes;
            long audioBytes = VideoDemuxer.AudioBytes + AudioDemuxer.AudioBytes + SubtitlesDemuxer.AudioBytes;

            requiresBuffering = true;

            while (IsPlaying)
            {
                if (seeks.TryPop(out SeekData seekData))
                {
                    seeks.Clear();
                    requiresBuffering = true;

                    if (AudioDecoder.OnVideoDemuxer)
                    {
                        if (decoder.Seek(seekData.ms, seekData.foreward) < 0)
                            Log("[SCREAMER] Seek failed 1");
                    }
                    else
                    {
                        if (decoder.SeekAudio(seekData.ms, seekData.foreward) < 0)
                            Log("[SCREAMER] Seek failed 2");
                    }
                }

                if (requiresBuffering)
                {
                    AudioBuffer();
                    elapsedSec = startedAtTicks;
                    requiresBuffering = false;
                    IsBuffering = false;
                    if (seeks.Count != 0) continue;
                    if (aFrame == null) { Log("MediaBuffer() no audio frame"); break; }
                }

                if (aFrame == null)
                {
                    if (AudioDecoder.Status == MediaFramework.Status.Ended)
                    {
                        Status = Status.Ended;
                        if (decoder.AudioStream != null && decoder.AudioStream.Demuxer.HLSPlaylist == null)
                            SetCurTime((videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks)) * Config.Player.Speed);
                    }

                    if (Status != Status.Playing) break;

                    Log("[SCREAMER] No audio frames");
                    requiresBuffering = true;
                    continue;
                }

                if (Status != Status.Playing) break;

                elapsedTicks    = videoStartTicks + (DateTime.UtcNow.Ticks - startedAtTicks);
                aDistanceMs     = (int) ((aFrame.timestamp - elapsedTicks) / 10000);

                if (aDistanceMs > 1000 || aDistanceMs < -100)
                {
                    requiresBuffering = true;
                    continue;
                }

                if (aDistanceMs > 100)
                {
                    if (Math.Abs(elapsedTicks - elapsedSec) > 10000000)
                    {
                        elapsedSec = elapsedTicks;

                        Action refresh = new Action(() =>
                        {
                            try
                            {
                                if (Config == null) return;

                                if (Config.Player.Stats)
                                {
                                    long curTotalBytes  =  VideoDemuxer.TotalBytes + AudioDemuxer.TotalBytes + SubtitlesDemuxer.TotalBytes;
                                    long curVideoBytes  =  VideoDemuxer.VideoBytes + AudioDemuxer.VideoBytes + SubtitlesDemuxer.VideoBytes;
                                    long curAudioBytes  =  VideoDemuxer.AudioBytes + AudioDemuxer.AudioBytes + SubtitlesDemuxer.AudioBytes;

                                    BitRate             = (curTotalBytes - totalBytes) * 8 / 1000.0;
                                    Video.BitRate       = (curVideoBytes - videoBytes) * 8 / 1000.0;
                                    Audio.BitRate       = (curAudioBytes - audioBytes) * 8 / 1000.0;
                                    totalBytes          =  curTotalBytes;
                                    videoBytes          =  curVideoBytes;
                                    audioBytes          =  curAudioBytes;
                                }

                                BufferedDuration = decoder.AudioStream.Demuxer.BufferedDuration;
                                if (decoder.AudioStream.Demuxer.HLSPlaylist != null)
                                    SetCurTimeHLS();
                                else
                                    SetCurTime(elapsedTicks * Config.Player.Speed);

                            } catch (Exception) { }
                        });

                        if (_Control != null)
                            _Control?.BeginInvoke(refresh);
                        else
                            refresh();
                    }

                    Thread.Sleep(aDistanceMs-15);
                }

                AddAudioSamples(aFrame.audioData, false);
                AudioDecoder.Frames.TryDequeue(out aFrame);
            }
        }

        private void SetCurTimeHLS()
        {
            var curDemuxer = !VideoDemuxer.Disposed ? VideoDemuxer : AudioDemuxer;
            Duration = curDemuxer.Duration;
            SetCurTime(curDemuxer.CurTime);

            #if DEBUG
            Log($"[First: {TicksToTime(curDemuxer.hlsCtx->first_timestamp * 10)}] [Cur: {TicksToTime(curDemuxer.CurTime)} / {TicksToTime(curDemuxer.CurTime - curDemuxer.StartTime)}] [BD: {TicksToTime(curDemuxer.BufferedDuration)}] [D: {curDemuxer.Duration}] | DT: {curDemuxer.Type}");
            #endif
            
        }
        #endregion

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{PlayerId}] [Player] {msg}"); }
    }

    public enum Status
    {
        Opening,
        Failed,
        Stopped,
        Paused,
        Playing,
        Ended
    }
    public enum Usage
    {
        AVS,
        Audio,
        LowLatencyVideo
    }
}