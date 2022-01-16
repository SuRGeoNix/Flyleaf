using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;

namespace FlyleafLib
{
    /// <summary>
    /// Player's main configuration
    /// </summary>
    public unsafe class Config : NotifyPropertyChanged
    {
        public Config()
        {
            // Parse default plugin options to Config.Plugins (Creates instances until fix with statics in interfaces)
            foreach (var plugin in PluginHandler.PluginTypes.Values)
            {
                var tmpPlugin = PluginHandler.CreatePluginInstance(plugin);
                var defaultOptions = tmpPlugin.GetDefaultOptions();
                tmpPlugin.Dispose();

                if (defaultOptions == null || defaultOptions.Count == 0) continue;

                Plugins.Add(plugin.Name, new SerializableDictionary<string, string>());
                foreach (var opt in defaultOptions)
                    Plugins[plugin.Name].Add(opt.Key, opt.Value);
            }

            Player.config = this;
            Demuxer.config = this;
        }
        public Config Clone()
        {
            Config config   = new Config();
            config          = (Config) MemberwiseClone();

            config.Audio    = Audio.Clone();
            config.Video    = Video.Clone();
            config.Subtitles= Subtitles.Clone();
            config.Demuxer  = Demuxer.Clone();
            config.Decoder  = Decoder.Clone();

            return config;
        }
        public static Config Load(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Config));
                Config config       = (Config) xmlSerializer.Deserialize(fs);
                config.Loaded       = true;
                config.LoadedPath   = path;

                return config;
            }
        }
        public void Save(string path = null)
        {
            if (path == null)
            {
                if (string.IsNullOrEmpty(LoadedPath))
                    return;

                path = LoadedPath;
            }

            using (FileStream fs = new FileStream(path, FileMode.Create))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(GetType());
                xmlSerializer.Serialize(fs, this);
            }
        }

        internal void SetPlayer(Player player)
        {
            Player.player   = player;
            Player.KeyBindings.SetPlayer(player);
            Demuxer.player  = player;
            Decoder.player  = player;
            Audio.player    = player;
            Video.player    = player;
            Subtitles.player= player;

            if (Video.Filters != null)
            {
                foreach(var filter in Video.Filters.Values)
                    filter.player = player;
            }
        }

        /// <summary>
        /// Whether configuration has been loaded from file
        /// </summary>
        [XmlIgnore]
        public bool             Loaded      { get; private set; }

        /// <summary>
        /// The path that this configuration has been loaded from
        /// </summary>
        [XmlIgnore]
        public string           LoadedPath  { get; private set; }

        public PlayerConfig     Player      { get; set; } = new PlayerConfig();
        public DemuxerConfig    Demuxer     { get; set; } = new DemuxerConfig();
        public DecoderConfig    Decoder     { get; set; } = new DecoderConfig();
        public VideoConfig      Video       { get; set; } = new VideoConfig();
        public AudioConfig      Audio       { get; set; } = new AudioConfig();
        public SubtitlesConfig  Subtitles   { get; set; } = new SubtitlesConfig();

        public SerializableDictionary<string, SerializableDictionary<string, string>>
                                Plugins     = new SerializableDictionary<string, SerializableDictionary<string, string>>();
        public class PlayerConfig : NotifyPropertyChanged
        {
            public PlayerConfig Clone()
            {
                PlayerConfig player = (PlayerConfig) MemberwiseClone();
                return player;
            }

            internal Player player;
            internal Config config;

            /// <summary>
            /// Whether to use Activity Mode
            /// </summary>
            public bool     ActivityMode                { get => _ActivityMode; set { _ActivityMode = value; if (value) player?.Activity.ForceFullActive(); } }
            bool _ActivityMode = false;

            /// <summary>
            /// Idle Timeout (ms)
            /// </summary>
            public int      ActivityTimeout             { get => _ActivityTimeout; set => Set(ref _ActivityTimeout, value); }
            int _ActivityTimeout = 6000;

            /// <summary>
            /// It will automatically start playing after open or seek after ended
            /// </summary>
            public bool     AutoPlay                    { get; set; } = true;

            /// <summary>
            /// Required buffered duration ticks before playing
            /// </summary>
            public long     MinBufferDuration {
                get => _MinBufferDuration;
                set
                {
                    if (!Set(ref _MinBufferDuration, value)) return;
                    if (config != null && value > config.Demuxer.BufferDuration)
                        config.Demuxer.BufferDuration = value;
                }
            }
            long _MinBufferDuration = 500 * 10000;

            /// <summary>
            /// Key bindings configuration
            /// </summary>
            public KeysConfig
                            KeyBindings                 { get; set; } = new KeysConfig();

            /// <summary>
            /// Mouse bindings configuration
            /// </summary>
            public MouseConfig  
                            MouseBindings               { get; set; } = new MouseConfig();

            /// <summary>
            /// Fps while the player is not playing
            /// </summary>
            public double   IdleFps                     { get; set; } = 60.0;

            /// <summary>
            /// Limit before dropping frames. Lower value means lower latency (>=1)
            /// </summary>
            public int      LowLatencyMaxVideoFrames    { get; set; } = 2;

            /// <summary>
            /// Limit before dropping frames. Lower value means lower latency (>=0)
            /// </summary>
            public int      LowLatencyMaxVideoPackets   { get; set; } = 2;

            /// <summary>
            /// Folder to save recordings (when filename is not specified)
            /// </summary>
            public string   FolderRecordings            { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "Recordings");

            /// <summary>
            /// Folder to save snapshots (when filename is not specified)
            /// </summary>

            public string   FolderSnapshots             { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "Snapshots");

            /// <summary>
            /// Forces CurTime/SeekBackward/SeekForward to seek accurate on video
            /// </summary>
            public bool     SeekAccurate                { get => _SeekAccurate; set => Set(ref _SeekAccurate, value); }
            bool _SeekAccurate;

            /// <summary>
            /// Snapshot encoding will be used (valid formats bmp, png, jpg/jpeg)
            /// </summary>
            public string   SnapshotFormat              { get ;set; } = "bmp";

            /// <summary>
            /// Whether to refresh statistics about bitrates/fps/drops etc.
            /// </summary>
            public bool     Stats                       { get => _Stats; set => Set(ref _Stats, value); }
            bool _Stats = false;

            /// <summary>
            /// Refreshes CurTime in UI on every frame (can cause performance issues)
            /// </summary>
            public bool     UICurTimePerFrame           { get; set; } = false;

            /// <summary>
            /// The upper limit of the volume amplifier
            /// </summary>
            public int      VolumeMax                   { get => _VolumeMax; set { Set(ref _VolumeMax, value); if (player != null && player.Audio.masteringVoice != null) player.Audio.masteringVoice.Volume = value / 100f;  } }
            int _VolumeMax = 150;

            /// <summary>
            /// The purpose of the player
            /// </summary>
            public Usage    Usage                       { get; set; } = Usage.AVS;

            // Offsets

            public long     AudioDelayOffset            { get; set; } =  100 * 10000;
            public long     AudioDelayOffset2           { get; set; } = 1000 * 10000;
            public long     SubtitlesDelayOffset        { get; set; } =  100 * 10000;
            public long     SubtitlesDelayOffset2       { get; set; } = 1000 * 10000;
            public long     SeekOffset                  { get; set; } = 5 * (long)1000 * 10000;
            public long     SeekOffset2                 { get; set; } = 15 * (long)1000 * 10000;
            public int      ZoomOffset                  { get; set; } = 50;
            public int      VolumeOffset                { get; set; } = 5;
        }
        public class DemuxerConfig : NotifyPropertyChanged
        {
            internal Player player;
            internal Config config;

            public DemuxerConfig Clone()
            {
                DemuxerConfig demuxer = (DemuxerConfig) MemberwiseClone();

                demuxer.FormatOpt       = new SerializableDictionary<string, string>();
                demuxer.AudioFormatOpt  = new SerializableDictionary<string, string>();
                demuxer.SubtitlesFormatOpt = new SerializableDictionary<string, string>();

                foreach (var kv in FormatOpt) demuxer.FormatOpt.Add(kv.Key, kv.Value);
                foreach (var kv in AudioFormatOpt) demuxer.AudioFormatOpt.Add(kv.Key, kv.Value);
                foreach (var kv in SubtitlesFormatOpt) demuxer.SubtitlesFormatOpt.Add(kv.Key, kv.Value);

                return demuxer;
            }

            /// <summary>
            /// Whether to enable demuxer's custom interrupt callback (for timeouts and interrupts)
            /// </summary>
            public bool             AllowInterrupts     { get; set; } = true;

            /// <summary>
            /// Whether to allow interrupts during av_read_frame
            /// </summary>
            public bool             AllowReadInterrupts { get; set; } = true;

            /// <summary>
            /// Whether to allow timeouts checks within the interrupts callback
            /// </summary>
            public bool             AllowTimeouts       { get; set; } = true;


            /// <summary>
            /// List of FFmpeg formats to be excluded from interrupts
            /// </summary>
            public List<string>     ExcludeInterruptFmts{ get; set; } = new List<string>() { "rtsp" };

            /// <summary>
            /// Maximum allowed duration ticks for buffering
            /// </summary>
            public long             BufferDuration      {
                get => _BufferDuration;
                set
                {
                    if (!Set(ref _BufferDuration, value)) return;
                    if (config != null && value < config.Player.MinBufferDuration)
                       config.Player.MinBufferDuration = value;
                }
            }
            long _BufferDuration = 2 * 60 * (long)1000 * 10000;

            /// <summary>
            /// Maximum allowed errors before stopping
            /// </summary>
            public int              MaxErrors       { get; set; } = 30;


            /// <summary>
            /// avformat_close_input timeout (ticks) for protocols that support interrupts
            /// </summary>
            public long             CloseTimeout    { get; set; } =  1 * 1000 * 10000;

            /// <summary>
            /// avformat_open_input + avformat_find_stream_info timeout (ticks) for protocols that support interrupts (should be related to probesize/analyzeduration)
            /// </summary>
            public long             OpenTimeout     { get; set; } = 5 * 60 * (long)1000 * 10000;

            /// <summary>
            /// av_read_frame timeout (ticks) for protocols that support interrupts
            /// </summary>
            public long             ReadTimeout     { get; set; } = 10 * 1000 * 10000;

            /// <summary>
            /// av_seek_frame timeout (ticks) for protocols that support interrupts
            /// </summary>
            public long             SeekTimeout     { get; set; } =  8 * 1000 * 10000;

            /// <summary>
            /// FFmpeg's format flags for demuxer (see https://ffmpeg.org/doxygen/trunk/avformat_8h.html)
            /// eg. FormatFlags |= 0x40; // For AVFMT_FLAG_NOBUFFER
            /// </summary>
            public int              FormatFlags     { get; set; } = FFmpeg.AutoGen.ffmpeg.AVFMT_FLAG_DISCARD_CORRUPT;

            /// <summary>
            /// FFmpeg's format options for demuxer
            /// </summary>
            public SerializableDictionary<string, string>
                                    FormatOpt       { get; set; } = DefaultVideoFormatOpt();
            public SerializableDictionary<string, string>
                                    AudioFormatOpt  { get; set; } = DefaultVideoFormatOpt();

            public SerializableDictionary<string, string>
                                    SubtitlesFormatOpt  { get; set; } = DefaultVideoFormatOpt();

            public static SerializableDictionary<string, string> DefaultVideoFormatOpt()
            {
                SerializableDictionary<string, string> defaults = new SerializableDictionary<string, string>();

                defaults.Add("probesize",           (50 * (long)1024 * 1024).ToString());      // (Bytes) Default 5MB | Higher for weird formats (such as .ts?) and 4K/Hevc
                defaults.Add("analyzeduration",     (10 * (long)1000 * 1000).ToString());      // (Microseconds) Default 5 seconds | Higher for network streams
                defaults.Add("reconnect",           "1");                                       // auto reconnect after disconnect before EOF
                defaults.Add("reconnect_streamed",  "1");                                       // auto reconnect streamed / non seekable streams
                defaults.Add("reconnect_delay_max", "5");                                       // max reconnect delay in seconds after which to give up
                defaults.Add("rtsp_transport",      "tcp");                                     // Seems UDP causing issues (use this by default?)
                defaults.Add("user_agent",          "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.159 Safari/537.36");

                //defaults.Add("live_start_index",   "-1");
                //defaults.Add("timeout",           (2 * (long)1000 * 1000).ToString());      // (Bytes) Default 5MB | Higher for weird formats (such as .ts?)
                //defaults.Add("rw_timeout",     (2 * (long)1000 * 1000).ToString());      // (Microseconds) Default 5 seconds | Higher for network streams

                return defaults;
            }
        }
        public class DecoderConfig : NotifyPropertyChanged
        {
            internal Player player;

            public DecoderConfig Clone() { return (DecoderConfig) MemberwiseClone(); }

            /// <summary>
            /// Threads that will be used from the decoder
            /// </summary>
            public int              VideoThreads    { get; set; } = Environment.ProcessorCount;

            /// <summary>
            /// Maximum video frames to be decoded and processed for rendering
            /// </summary>
            public int              MaxVideoFrames  { get => _MaxVideoFrames; set { if (Set(ref _MaxVideoFrames, value)) { player?.Video?.Disable(); player?.Video?.Enable(); } } }
            int _MaxVideoFrames = 4;

            /// <summary>
            /// Maximum audio frames to be decoded and processed for playback
            /// </summary>
            public int              MaxAudioFrames  { get; set; } = 10;

            /// <summary>
            /// Maximum subtitle frames to be decoded
            /// </summary>
            public int              MaxSubsFrames   { get; set; } = 2;

            /// <summary>
            /// Maximum allowed errors before stopping
            /// </summary>
            public int              MaxErrors       { get; set; } = 200;

            /// <summary>
            /// Whether or not to use decoder's textures directly as shader resources
            /// (TBR: Better performance but might need to be disabled while video input has padding or not supported by older Direct3D versions)
            /// </summary>
            public ZeroCopy         ZeroCopy        { get => _ZeroCopy; set { if (SetUI(ref _ZeroCopy, value) && player != null && player.Video.isOpened) player.VideoDecoder?.RecalculateZeroCopy(); } }
            ZeroCopy _ZeroCopy = ZeroCopy.Auto;

            /// <summary>
            /// Allows video accceleration even in codec's profile mismatch
            /// </summary>
            public bool             AllowProfileMismatch { get; set; } = true;
        }
        public class VideoConfig : NotifyPropertyChanged
        {
            internal Player player;

            public VideoConfig Clone() { return (VideoConfig) MemberwiseClone(); }

            /// <summary>
            /// Forces the decoder/renderer to use the specified GPU adapter / device luid <see cref="Master.GPUAdapters"/>
            /// Should be set before the decoder's initialization and it cannot be changed after
            /// </summary>
            public long             GPUAdapteLuid               { get; set; } = -1;

            /// <summary>
            /// Video aspect ratio
            /// </summary>
            public AspectRatio      AspectRatio                 { get => _AspectRatio;  set { if (Set(ref _AspectRatio, value)) player?.renderer?.SetViewport(); } }
            AspectRatio    _AspectRatio = AspectRatio.Keep;

            /// <summary>
            /// Custom aspect ratio (AspectRatio must be set to Custom to have an effect)
            /// </summary>
            public AspectRatio      CustomAspectRatio           { get => _CustomAspectRatio;  set { if (Set(ref _CustomAspectRatio, value)) AspectRatio = AspectRatio.Custom; } }
            AspectRatio    _CustomAspectRatio = new AspectRatio(16, 9);

            /// <summary>
            /// Background color of the player's control
            /// </summary>
            public System.Windows.Media.Color
                                    BackgroundColor             { get => Utils.WinFormsToWPFColor(_BackgroundColor);  set { Set(ref _BackgroundColor, Utils.WPFToWinFormsColor(value)); player?.renderer?.UpdateBackgroundColor(); } }
            internal System.Drawing.Color _BackgroundColor = System.Drawing.Color.Black;

            /// <summary>
            /// Whether video should be allowed
            /// </summary>
            public bool             Enabled                     { get => _Enabled;          set { if (Set(ref _Enabled, value)) if (value) player?.Video.Enable(); else player?.Video.Disable(); } }
            bool    _Enabled = true;
            internal void SetEnabled(bool enabled) { Set(ref _Enabled, enabled, true, nameof(Enabled)); }

            /// <summary>
            /// The max resolution that the current system can achieve and will be used from the input/stream suggester plugins
            /// </summary>
            [XmlIgnore]
            public int              MaxVerticalResolutionAuto   { get; internal set; }

            /// <summary>
            /// Custom max vertical resolution that will be used from the input/stream suggester plugins
            /// </summary>
            public int              MaxVerticalResolutionCustom { get; set; }

            /// <summary>
            /// The max resolution that is currently used (based on Auto/Custom)
            /// </summary>
            public int              MaxVerticalResolution       => MaxVerticalResolutionCustom == 0 ? (MaxVerticalResolutionAuto != 0 ? MaxVerticalResolutionAuto : 1080) : MaxVerticalResolutionCustom;

            /// <summary>
            /// In case of no hardware accelerated or post process accelerated pixel formats will use FFmpeg's SwsScale
            /// </summary>
            public bool             SwsHighQuality              { get; set; } = false;

            /// <summary>
            /// Activates Direct3D video acceleration (decoding)
            /// </summary>
            public bool             VideoAcceleration           { get; set; } = true;

            /// <summary>
            /// Whether to use embedded video processor with custom pixel shaders or D3D11
            /// (Currently D3D11 works only on video accelerated / hardware surfaces)
            /// * FLVP supports HDR to SDR, D3D11 does not
            /// * FLVP supports Pan Move/Zoom, D3D11 does not
            /// * D3D11 possible performs better with color conversion and filters, FLVP supports only brightness/contrast filters
            /// * D3D11 supports deinterlace (bob)
            /// </summary>
            public VideoProcessors  VideoProcessor              { get => _VideoProcessor; set { if (Set(ref _VideoProcessor, value)) player?.renderer?.UpdateVideoProcessor(); } }
            VideoProcessors _VideoProcessor = VideoProcessors.Auto;

            /// <summary>
            /// Whether Vsync should be enabled (0: Disabled, 1: Enabled)
            /// </summary>
            public short            VSync                       { get; set; }

            /// <summary>
            /// Enables the video processor to perform post process deinterlacing
            /// (D3D11 video processor should be enabled and support bob deinterlace method)
            /// </summary>
            public bool             Deinterlace                 { get=> _Deinterlace;   set { if (Set(ref _Deinterlace, value)) player?.renderer?.UpdateDeinterlace(); } }
            bool _Deinterlace;

            public bool             DeinterlaceBottomFirst      { get=> _DeinterlaceBottomFirst; set { if (Set(ref _DeinterlaceBottomFirst, value)) player?.renderer?.UpdateDeinterlace(); } }
            bool _DeinterlaceBottomFirst;

            /// <summary>
            /// The HDR to SDR method that will be used by the pixel shader
            /// </summary>
            public HDRtoSDRMethod   HDRtoSDRMethod              { get => _HDRtoSDRMethod; set { if (Set(ref _HDRtoSDRMethod, value)) player?.renderer?.UpdateHDRtoSDR(); }}
            HDRtoSDRMethod _HDRtoSDRMethod = HDRtoSDRMethod.Hable;

            /// <summary>
            /// The HDR to SDR Tone float correnction (not used by Reinhard) 
            /// </summary>
            public float            HDRtoSDRTone                { get => _HDRtoSDRTone; set { if (Set(ref _HDRtoSDRTone, value)) player?.renderer?.UpdateHDRtoSDR(); } }
            float _HDRtoSDRTone = 1.4f;

            public SerializableDictionary<VideoFilters, VideoFilter> Filters {get ; set; } = DefaultFilters();

            public static SerializableDictionary<VideoFilters, VideoFilter> DefaultFilters()
            {
                var filters = new SerializableDictionary<VideoFilters, VideoFilter>();

                var available = Enum.GetValues(typeof(VideoFilters));

                foreach(var filter in available)
                    filters.Add((VideoFilters)filter, new VideoFilter((VideoFilters)filter));

                return filters;
            }
        }
        public class AudioConfig : NotifyPropertyChanged
        {
            internal Player player;

            public AudioConfig Clone() { return (AudioConfig) MemberwiseClone(); }

            /// <summary>
            /// Audio delay ticks (will be reseted to 0 for every new audio stream)
            /// </summary>
            public long             Delay               { get => _Delay;            set { if (player != null && !player.Audio.IsOpened) return;  if (Set(ref _Delay, value)) player?.ReSync(player.decoder.AudioStream); } }
            long _Delay;
            internal void SetDelay(long delay) { Set(ref _Delay, delay, true, nameof(Delay)); }

            /// <summary>
            /// Whether audio should allowed
            /// </summary>
            public bool             Enabled             { get => _Enabled;          set { if (Set(ref _Enabled, value)) if (value) player?.Audio.Enable(); else player?.Audio.Disable(); } }
            bool    _Enabled = true;
            internal void SetEnabled(bool enabled) { Set(ref _Enabled, enabled, true, nameof(Enabled)); }

            /// <summary>
            /// Audio languages preference by priority
            /// </summary>
            public List<Language>   Languages           { get { if (_Languages == null) _Languages = Utils.GetSystemLanguages();  return _Languages; } set { _Languages = value;} }
            List<Language> _Languages;
        }
        public class SubtitlesConfig : NotifyPropertyChanged
        {
            internal Player player;

            public SubtitlesConfig Clone()
            {
                SubtitlesConfig subs = new SubtitlesConfig();
                subs = (SubtitlesConfig) MemberwiseClone();

                subs.Languages = new List<Language>();
                if (Languages != null) foreach(var lang in Languages) subs.Languages.Add(lang);

                return subs;
            }

            /// <summary>
            /// Subtitle delay ticks (will be reseted to 0 for every new subtitle stream)
            /// </summary>
            public long             Delay               { get => _Delay;            set { if (player != null && !player.Subtitles.IsOpened) return; if (Set(ref _Delay, value)) player?.ReSync(player.decoder.SubtitlesStream); } }
            long _Delay;
            internal void SetDelay(long delay) { Set(ref _Delay, delay, true, nameof(Delay)); }

            /// <summary>
            /// Whether subtitles should be allowed
            /// </summary>
            public bool             Enabled             { get => _Enabled;          set { if(Set(ref _Enabled, value)) if (value) player?.Subtitles.Enable(); else player?.Subtitles.Disable(); } }
            bool    _Enabled = true;
            internal void SetEnabled(bool enabled) { Set(ref _Enabled, enabled, true, nameof(Enabled)); }

            /// <summary>
            /// Subtitle languages preference by priority
            /// </summary>
            public List<Language>   Languages           { get { if (_Languages == null) _Languages = Utils.GetSystemLanguages();  return _Languages; } set { _Languages = value;} }
            List<Language> _Languages;

            /// <summary>
            /// Whether to use offline (local) search plugins
            /// </summary>
            public bool             UseLocalSearch  { get => _UseLocalSearch;set { Set(ref _UseLocalSearch, value); } }
            bool    _UseLocalSearch = false;

            /// <summary>
            /// Whether to use online search plugins
            /// </summary>
            public bool             UseOnlineDatabases  { get => _UseOnlineDatabases;set { Set(ref _UseOnlineDatabases, value); } }
            bool    _UseOnlineDatabases = false;
        }
    }
}