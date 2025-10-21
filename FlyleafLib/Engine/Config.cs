﻿using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Data;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;

namespace FlyleafLib;

/// <summary>
/// Player's configuration
/// </summary>
public class Config : NotifyPropertyChanged
{
    public static JsonSerializerOptions jsonOpts = new() { WriteIndented = true };

    public Config()
    {
        // Parse default plugin options to Config.Plugins (Creates instances until fix with statics in interfaces)
        foreach (var plugin in Engine.Plugins.Types.Values)
        {
            var tmpPlugin = PluginHandler.CreatePluginInstance(plugin);
            var defaultOptions = tmpPlugin.GetDefaultOptions();
            tmpPlugin.Dispose();

            if (defaultOptions == null || defaultOptions.Count == 0) continue;

            Plugins.Add(plugin.Name, []);
            foreach (var opt in defaultOptions)
                Plugins[plugin.Name].Add(opt.Key, opt.Value);
        }
        // save default plugin options for later
        defaultPlugins = Plugins;

        Player.config = this;
        Demuxer.config = this;
    }
    public Config Clone()
    {
        Config config = new()
        {
            Audio       = Audio.Clone(),
            Video       = Video.Clone(),
            Subtitles   = Subtitles.Clone(),
            Demuxer     = Demuxer.Clone(),
            Decoder     = Decoder.Clone(),
            Player      = Player.Clone()
        };

        config.Player.config = config;
        config.Demuxer.config = config;

        return config;
    }
    public static Config Load(string path)
    {
        Config config       = JsonSerializer.Deserialize<Config>(File.ReadAllText(path));
        config.Loaded       = true;
        config.LoadedPath   = path;

        if (config.Audio.FiltersEnabled && Engine.Config.FFmpegLoadProfile == LoadProfile.Main)
            config.Audio.FiltersEnabled = false;

        // Restore the plugin options initialized by the constructor, as they are overwritten after deserialization.

        // Remove removed plugin options
        foreach (var plugin in config.Plugins)
        {
            // plugin deleted
            if (!config.defaultPlugins.ContainsKey(plugin.Key))
            {
                config.Plugins.Remove(plugin.Key);
                continue;
            }

            // plugin option deleted
            foreach (var opt in plugin.Value)
                if (!config.defaultPlugins[plugin.Key].ContainsKey(opt.Key))
                    config.Plugins[plugin.Key].Remove(opt.Key);
        }

        // Restore added plugin options
        foreach (var plugin in config.defaultPlugins)
        {
            // plugin added
            if (!config.Plugins.ContainsKey(plugin.Key))
            {
                config.Plugins[plugin.Key] = plugin.Value;
                continue;
            }

            // plugin option added
            foreach (var opt in plugin.Value)
                if (!config.Plugins[plugin.Key].ContainsKey(opt.Key))
                    config.Plugins[plugin.Key][opt.Key] = opt.Value;
        }

        return config;
    }
    public void Save(string path = null)
    {
        if (path == null)
        {
            if (string.IsNullOrEmpty(LoadedPath))
                return;

            path = LoadedPath;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, jsonOpts));
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
    }

    /// <summary>
    /// Whether configuration has been loaded from file
    /// </summary>
    [JsonIgnore]
    public bool             Loaded      { get; private set; }

    /// <summary>
    /// The path that this configuration has been loaded from
    /// </summary>
    [JsonIgnore]
    public string           LoadedPath  { get; private set; }

    public PlayerConfig     Player      { get; set; } = new();
    public DemuxerConfig    Demuxer     { get; set; } = new();
    public DecoderConfig    Decoder     { get; set; } = new();
    public VideoConfig      Video       { get; set; } = new();
    public AudioConfig      Audio       { get; set; } = new();
    public SubtitlesConfig  Subtitles   { get; set; } = new();
    public DataConfig       Data        { get; set; } = new();

    public Dictionary<string, ObservableDictionary<string, string>>
                            Plugins     { get; set; } = [];
    private
           Dictionary<string, ObservableDictionary<string, string>>
                            defaultPlugins;

    public class PlayerConfig : NotifyPropertyChanged
    {
        public PlayerConfig Clone()
        {
            PlayerConfig player = (PlayerConfig) MemberwiseClone();
            player.player = null;
            player.config = null;
            player.KeyBindings = KeyBindings.Clone();
            return player;
        }

        internal Player player;
        internal Config config;

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
        /// Zero Latency forces playback at the very last frame received (Live Video Only)
        /// </summary>
        public bool     ZeroLatency                 { get => _ZeroLatency; set { Set(ref _ZeroLatency, value); if (config != null) config.Decoder.LowDelay = value; if (player != null && player.IsPlaying) { player.Pause(); player.Play(); } } }
        bool _ZeroLatency;

        /// <summary>
        /// Max Latency (ticks) forces playback (with speed x1+) to stay at the end of the live network stream (default: 0 - disabled)
        /// </summary>
        public long     MaxLatency {
            get => _MaxLatency;
            set
            {
                if (value < 0)
                    value = 0;

                if (!Set(ref _MaxLatency, value)) return;

                if (value == 0)
                {
                    if (player != null)
                        player.Speed = 1;

                    if (config != null)
                        config.Decoder.LowDelay = false;

                    return;
                }

                // Large max buffer so we ensure the actual latency distance
                if (config != null)
                {
                    if (config.Demuxer.BufferDuration < value * 2)
                        config.Demuxer.BufferDuration = value * 2;

                    config.Decoder.LowDelay = true;
                }

                // Small min buffer to avoid enabling latency speed directly
                if (_MinBufferDuration > value / 10)
                    MinBufferDuration = value / 10;
            }
        }
        long _MaxLatency = 0;

        /// <summary>
        /// Min Latency (ticks) prevents MaxLatency to go (with speed x1) less than this limit (default: 0 - as low as possible)
        /// </summary>
        public long     MinLatency                  { get => _MinLatency; set => Set(ref _MinLatency, value); }
        long _MinLatency = 0;

        /// <summary>
        /// Prevents frequent speed changes when MaxLatency is enabled (to avoid audio/video gaps and desyncs)
        /// </summary>
        public long     LatencySpeedChangeInterval  { get; set; } = TimeSpan.FromMilliseconds(700).Ticks;

        /// <summary>
        /// Folder to save recordings (when filename is not specified)
        /// </summary>
        public string   FolderRecordings            { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings");

        /// <summary>
        /// Folder to save snapshots (when filename is not specified)
        /// </summary>

        public string   FolderSnapshots             { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Snapshots");

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
        /// Sets playback's thread priority
        /// </summary>
        public ThreadPriority
                        ThreadPriority              { get; set; } = ThreadPriority.AboveNormal;

        /// <summary>
        /// Refreshes CurTime in UI on every frame (can cause performance issues)
        /// </summary>
        public UIRefreshType
                        UICurTime                   { get; set; } = UIRefreshType.PerFrameSecond;

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
        public long     SeekOffset3                 { get; set; } = 30 * (long)1000 * 10000;
        public double   SpeedOffset                 { get; set; } = 0.10;
        public double   SpeedOffset2                { get; set; } = 0.25;
        public int      ZoomOffset                  { get => _ZoomOffset; set { if (SetUI(ref _ZoomOffset, value)) player?.ResetAll(); } }
        int _ZoomOffset = 10;

        public int      VolumeOffset                { get; set; } = 5;
    }
    public class DemuxerConfig : NotifyPropertyChanged
    {
        public DemuxerConfig Clone()
        {
            DemuxerConfig demuxer = (DemuxerConfig) MemberwiseClone();

            demuxer.FormatOpt           = [];
            demuxer.AudioFormatOpt      = [];
            demuxer.SubtitlesFormatOpt  = [];

            foreach (var kv in FormatOpt) demuxer.FormatOpt.Add(kv.Key, kv.Value);
            foreach (var kv in AudioFormatOpt) demuxer.AudioFormatOpt.Add(kv.Key, kv.Value);
            foreach (var kv in SubtitlesFormatOpt) demuxer.SubtitlesFormatOpt.Add(kv.Key, kv.Value);

            demuxer.player = null;
            demuxer.config = null;

            return demuxer;
        }

        internal Player player;
        internal Config config;

        /// <summary>
        /// Whethere to allow avformat_find_stream_info during open (avoiding this can open the input faster but it could cause other issues)
        /// </summary>
        public bool             AllowFindStreamInfo { get; set; } = true;

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
        public List<string>     ExcludeInterruptFmts{ get; set; } = ["rtsp"];

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
        long _BufferDuration = 30 * (long)1000 * 10000;

        /// <summary>
        /// Maximuim allowed packets for buffering (as an extra check along with BufferDuration)
        /// </summary>
        public long             BufferPackets       { get; set; }

        /// <summary>
        /// Maximuim allowed audio packets (when reached it will drop the extra packets and will fire the AudioLimit event)
        /// </summary>
        public long             MaxAudioPackets     { get; set; }

        /// <summary>
        /// Maximum allowed errors before stopping
        /// </summary>
        public int              MaxErrors           { get; set; } = 30;

        /// <summary>
        /// Custom IO Stream buffer size (in bytes) for the AVIO Context
        /// </summary>
        public int              IOStreamBufferSize  { get; set; } = 0x200000;

        /// <summary>
        /// avformat_close_input timeout (ticks) for protocols that support interrupts
        /// </summary>
        public long             CloseTimeout        { get => closeTimeout; set { closeTimeout = value; closeTimeoutMs = value / 10000; } }
        private long closeTimeout = 1 * 1000 * 10000;
        internal long closeTimeoutMs = 1 * 1000;

        /// <summary>
        /// avformat_open_input + avformat_find_stream_info timeout (ticks) for protocols that support interrupts (should be related to probesize/analyzeduration)
        /// </summary>
        public long             OpenTimeout         { get => openTimeout; set { openTimeout = value; openTimeoutMs = value / 10000; } }
        private long openTimeout = 5 * 60 * (long)1000 * 10000;
        internal long openTimeoutMs = 5 * 60 * 1000;

        /// <summary>
        /// av_read_frame timeout (ticks) for protocols that support interrupts
        /// </summary>
        public long             ReadTimeout         { get => readTimeout; set { readTimeout = value; readTimeoutMs = value / 10000; } }
        private long readTimeout = 10 * 1000 * 10000;
        internal long readTimeoutMs = 10 * 1000;

        /// <summary>
        /// av_read_frame timeout (ticks) for protocols that support interrupts (for Live streams)
        /// </summary>
        public long             ReadLiveTimeout     { get => readLiveTimeout; set { readLiveTimeout = value; readLiveTimeoutMs = value / 10000; } }
        private long readLiveTimeout = 20 * 1000 * 10000;
        internal long readLiveTimeoutMs = 20 * 1000;

        /// <summary>
        /// av_seek_frame timeout (ticks) for protocols that support interrupts
        /// </summary>
        public long             SeekTimeout         { get => seekTimeout; set { seekTimeout = value; seekTimeoutMs = value / 10000; } }
        private long seekTimeout = 8 * 1000 * 10000;
        internal long seekTimeoutMs = 8 * 1000;

        /// <summary>
        /// Forces Input Format
        /// </summary>
        public string           ForceFormat         { get; set; }

        /// <summary>
        /// Forces FPS for NoTimestamp formats (such as h264/hevc)
        /// </summary>
        public double           ForceFPS            { get; set; }

        /// <summary>
        /// FFmpeg's format flags for demuxer (see https://ffmpeg.org/doxygen/trunk/avformat_8h.html)
        /// eg. FormatFlags |= 0x40; // For AVFMT_FLAG_NOBUFFER
        /// </summary>
        public DemuxerFlags     FormatFlags         { get; set; } = DemuxerFlags.DiscardCorrupt;// FFmpeg.AutoGen.ffmpeg.AVFMT_FLAG_DISCARD_CORRUPT;

        /// <summary>
        /// Certain muxers and demuxers do nesting (they open one or more additional internal format contexts). This will pass the FormatOpt and HTTPQuery params to the underlying contexts)
        /// </summary>
        public bool             FormatOptToUnderlying
                                                    { get; set; }

        /// <summary>
        /// Passes original's Url HTTP Query String parameters to underlying
        /// </summary>
        public bool             DefaultHTTPQueryToUnderlying
                                                    { get; set; } = true;

        /// <summary>
        /// HTTP Query String parameters to pass to underlying
        /// </summary>
        public Dictionary<string, string>
                                ExtraHTTPQueryParamsToUnderlying
                                                    { get; set; } = [];

        /// <summary>
        /// FFmpeg's format options for demuxer
        /// </summary>
        public Dictionary<string, string>
                                FormatOpt           { get; set; } = DefaultVideoFormatOpt();
        public Dictionary<string, string>
                                AudioFormatOpt      { get; set; } = DefaultVideoFormatOpt();

        public Dictionary<string, string>
                                SubtitlesFormatOpt  { get; set; } = DefaultVideoFormatOpt();

        public static Dictionary<string, string> DefaultVideoFormatOpt()
        {
            // TODO: Those should be set based on the demuxer format/protocol (to avoid false warnings about invalid options and best fit for the input, eg. live stream)

            Dictionary<string, string> defaults = new()
            {
                // General
                { "probesize",          (50 * (long)1024 * 1024).ToString() },      // (Bytes) Default 5MB | Higher for weird formats (such as .ts?) and 4K/Hevc
                { "analyzeduration",    (10 * (long)1000 * 1000).ToString() },      // (Microseconds) Default 5 seconds | Higher for network streams

                // HTTP
                { "reconnect",          "1" },                                       // auto reconnect after disconnect before EOF
                { "reconnect_streamed", "1" },                                       // auto reconnect streamed / non seekable streams (this can cause issues with HLS ts segments - disable this or http_persistent)
                { "reconnect_delay_max","7" },                                       // max reconnect delay in seconds after which to give up
                { "user_agent",         "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.159 Safari/537.36" },

                { "extension_picky",    "0" },                                       // Added in ffmpeg v7.1.1 and causes issues when enabled with allowed extentions #577

                // HLS
                { "http_persistent",    "0" },                                       // Disables keep alive for HLS - mainly when use reconnect_streamed and non-live HLS streams

                // RTSP
                { "rtsp_transport",     "tcp" },                                     // Seems UDP causing issues
            };

            //defaults.Add("live_start_index",   "-1");
            //defaults.Add("timeout",           (2 * (long)1000 * 1000).ToString());      // (Bytes) Default 5MB | Higher for weird formats (such as .ts?)
            //defaults.Add("rw_timeout",     (2 * (long)1000 * 1000).ToString());      // (Microseconds) Default 5 seconds | Higher for network streams

            return defaults;
        }

        public Dictionary<string, string> GetFormatOptPtr(MediaType type)
            => type == MediaType.Video ? FormatOpt : type == MediaType.Audio ? AudioFormatOpt : SubtitlesFormatOpt;
    }
    public class DecoderConfig : NotifyPropertyChanged
    {
        internal Player player;

        public DecoderConfig Clone()
        {
            DecoderConfig decoder = (DecoderConfig) MemberwiseClone();
            decoder.player = null;

            return decoder;
        }

        /// <summary>
        /// Threads that will be used from the decoder
        /// </summary>
        public int              VideoThreads        { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Maximum video frames to be decoded and processed for rendering
        /// </summary>
        public int              MaxVideoFrames      { get => _MaxVideoFrames;   set { if (Set(ref _MaxVideoFrames, value)) { player?.RefreshMaxVideoFrames(); } } }
        int _MaxVideoFrames = 4;
        internal void SetMaxVideoFrames(int maxVideoFrames) { _MaxVideoFrames = maxVideoFrames; RaiseUI(nameof(MaxVideoFrames)); } // can be updated by video decoder if fails to allocate them

        /// <summary>
        /// Maximum audio frames to be decoded and processed for playback
        /// </summary>
        public int              MaxAudioFrames      { get; set; } = 5;

        /// <summary>
        /// Maximum subtitle frames to be decoded
        /// </summary>
        public int              MaxSubsFrames       { get; set; } = 1;

        /// <summary>
        /// Maximum data frames to be decoded
        /// </summary>
        public int              MaxDataFrames       { get; set; } = 100;

        /// <summary>
        /// Maximum allowed errors before stopping
        /// </summary>
        public int              MaxErrors           { get; set; } = 200;

        /// <summary>
        /// Allows video accceleration even in codec's profile mismatch
        /// </summary>
        public bool             AllowProfileMismatch{ get => _AllowProfileMismatch; set => SetUI(ref _AllowProfileMismatch, value); }
        bool _AllowProfileMismatch = true;

        /// <summary>
        /// Allows corrupted frames (Parses AV_CODEC_FLAG_OUTPUT_CORRUPT to AVCodecContext)
        /// </summary>
        public bool             ShowCorrupted       { get => _ShowCorrupted;        set => SetUI(ref _ShowCorrupted, value); }
        bool _ShowCorrupted;

        /// <summary>
        /// Ensures that after a key packet receives a also key frame (can create artifacts on some broken formats when disabled and seek issues when enabled)
        /// </summary>
        public bool             KeyFrameValidation  { get => _KeyFrameValidation;   set => SetUI(ref _KeyFrameValidation, value); }
        internal bool _KeyFrameValidation;

        /// <summary>
        /// Forces low delay (Parses AV_CODEC_FLAG2_FAST or AV_CODEC_FLAG_LOW_DELAY -based on DropFrames- to AVCodecContext) (auto-enabled with MaxLatency)
        /// </summary>
        public bool             LowDelay            { get => _LowDelay;             set => SetUI(ref _LowDelay, value); }
        bool _LowDelay;

        /// <summary>
        /// Sets AVCodecContext skip_frame to None or Default (affects also LowDelay)
        /// </summary>
        public bool             AllowDropFrames     { get => _AllowDropFrames;      set => SetUI(ref _AllowDropFrames, value); }
        bool _AllowDropFrames;

        public string           AudioCodec          { get => _AudioCodec;           set => SetUI(ref _AudioCodec, value); }
        string _AudioCodec;

        public string           VideoCodec          { get => _VideoCodec;           set => SetUI(ref _VideoCodec, value); }
        string _VideoCodec;

        public string           SubtitlesCodec      { get => _SubtitlesCodec;           set => SetUI(ref _SubtitlesCodec, value); }
        string _SubtitlesCodec;

        public string GetCodecPtr(MediaType type)
            => type == MediaType.Video ? _VideoCodec : type == MediaType.Audio ? _AudioCodec : _SubtitlesCodec;

        public Dictionary<string, string>
                                AudioCodecOpt       { get; set; } = [];
        public Dictionary<string, string>
                                VideoCodecOpt       { get; set; } = [];
        public Dictionary<string, string>
                                SubtitlesCodecOpt   { get; set; } = [];

        public Dictionary<string, string> GetCodecOptPtr(MediaType type)
            => type == MediaType.Video ? VideoCodecOpt : type == MediaType.Audio ? AudioCodecOpt : SubtitlesCodecOpt;
    }
    public class VideoConfig : NotifyPropertyChanged
    {
        public VideoConfig()
        {
            BindingOperations.EnableCollectionSynchronization(Filters,  lockFilters);
            BindingOperations.EnableCollectionSynchronization(D3Filters,lockD3Filters);
        }

        public VideoConfig Clone()
        {
            VideoConfig video = (VideoConfig) MemberwiseClone();
            video.player = null;

            return video;
        }

        internal Player player;

        /// <summary>
        /// <para>Forces a specific GPU Adapter to be used by the renderer</para>
        /// <para>GPUAdapter must match with the description of the adapter eg. rx 580 (available adapters can be found in Engine.Video.GPUAdapters)</para>
        /// </summary>
        public string           GPUAdapter                  { get; set; }

        /// <summary>
        /// Video aspect ratio
        /// </summary>
        public AspectRatio      AspectRatio                 { get => _AspectRatio;          set { if (Set(ref _AspectRatio, value))     player?.renderer?.UpdateAspectRatio(); } }
        AspectRatio    _AspectRatio = AspectRatio.Keep;

        /// <summary>
        /// Custom aspect ratio (AspectRatio must be set to Custom to have an effect)
        /// </summary>
        public AspectRatio      CustomAspectRatio           { get => _CustomAspectRatio;    set { if (Set(ref _CustomAspectRatio, value) && AspectRatio == AspectRatio.Custom) { _AspectRatio = AspectRatio.Fill; AspectRatio = AspectRatio.Custom; } } }
        AspectRatio    _CustomAspectRatio = new(16, 9);

        /// <summary>
        /// Background color of the player's control
        /// </summary>
        public System.Windows.Media.Color
                                BackgroundColor             { get => VorticeToWPFColor(_BackgroundColor);  set { Set(ref _BackgroundColor, WPFToVorticeColor(value)); player?.renderer?.UpdateBackgroundColor(); } }
        internal Vortice.Mathematics.Color _BackgroundColor = (Vortice.Mathematics.Color)Vortice.Mathematics.Colors.Black;

        /// <summary>
        /// Clears the screen on stop/close/open
        /// </summary>
        public bool             ClearScreen                 { get; set; } = true;

        /// <summary>
        /// Cropping rectagle to crop the output frame (based on frame size)
        /// </summary>
        [JsonIgnore]
        public CropRect         Crop                        { get => _Crop;             set { _Crop = value; HasUserCrop = _Crop != CropRect.Empty; player?.renderer?.UpdateCropping(); } }
        internal CropRect _Crop = CropRect.Empty;
        internal bool HasUserCrop = false;

        /// <summary>
        /// Whether video should be allowed
        /// </summary>
        public bool             Enabled                     { get => _Enabled;          set { if (Set(ref _Enabled, value)) if (value) player?.Video.Enable(); else player?.Video.Disable(); } }
        bool _Enabled = true;
        internal void SetEnabled(bool enabled)              => Set(ref _Enabled, enabled, true, nameof(Enabled));

        /// <summary>
        /// Used to limit the number of frames rendered, particularly at increased speed
        /// </summary>
        public double           MaxOutputFps                { get; set; } = 60;

        /// <summary>
        /// DXGI Maximum Frame Latency (1 - 16)
        /// </summary>
        public uint             MaxFrameLatency             { get; set; } = 1;

        /// <summary>
        /// The max resolution that the current system can achieve and will be used from the input/stream suggester plugins
        /// </summary>
        [JsonIgnore]
        public int              MaxVerticalResolutionAuto   { get; internal set; }

        /// <summary>
        /// Custom max vertical resolution that will be used from the input/stream suggester plugins
        /// </summary>
        public int              MaxVerticalResolutionCustom { get => _MaxVerticalResolutionCustom;  set => Set(ref _MaxVerticalResolutionCustom, value); }
        int _MaxVerticalResolutionCustom;

        /// <summary>
        /// The max resolution that is currently used (based on Auto/Custom)
        /// </summary>
        [JsonIgnore]
        public int              MaxVerticalResolution       => MaxVerticalResolutionCustom == 0 ? (MaxVerticalResolutionAuto != 0 ? MaxVerticalResolutionAuto : 1080) : MaxVerticalResolutionCustom;

        /// <summary>
        /// Sets Super Resolution (Nvidia / Intel - D3D11VP only)
        /// </summary>
        public bool             SuperResolution             { get => _SuperResolution;  set { if (Set(ref _SuperResolution, value)) player?.renderer?.UpdateSuperRes(); } }
        internal bool _SuperResolution;

        /// <summary>
        /// Forces SwsScale instead of FlyleafVP for non HW decoded frames
        /// </summary>
        public bool             SwsForce                    { get; set; } = false;

        /// <summary>
        /// Activates Direct3D video acceleration (decoding)
        /// </summary>
        public bool             VideoAcceleration           { get; set; } = true;

        /// <summary>
        /// Whether to use embedded video processor with custom pixel shaders or D3D11<br/>
        /// * FLVP supports HDR to SDR, hardware/software frames and more formats<br/>
        /// * D3D11 uses less power, supports only hardware frames, deinterlace, super resolution and more accurate filters based on gpu
        /// </summary>
        public VideoProcessors  VideoProcessor              { get => _VideoProcessor;   set { if (Set(ref _VideoProcessor, value))  player?.renderer?.UpdateVideoProcessor(); } }
        VideoProcessors _VideoProcessor = VideoProcessors.Auto;

        /// <summary>
        /// Whether Vsync should be enabled (0: Disabled, 1: Enabled)
        /// </summary>
        public uint             VSync                       { get; set; } = 1;

        /// <summary>
        /// Sets DeInterlace (D3D11VP only)
        /// </summary>
        public DeInterlace      DeInterlace                 { get => _DeInterlace;      set { if (Set(ref _DeInterlace, value))     player?.renderer?.UpdateDeinterlace(); } }
        DeInterlace _DeInterlace = DeInterlace.Auto;

        /// <summary>
        /// Whether to use double rate (Bob) for DeInterlace instead normal (Weave/Blend)
        /// </summary>
        public bool             DoubleRate                  { get => _DoubleRate;       set => Set(ref _DoubleRate, value); }
        bool _DoubleRate = true;

        /// <summary>
        /// The HDR to SDR method that will be used by the pixel shader
        /// </summary>
        public HDRtoSDRMethod   HDRtoSDRMethod              { get => _HDRtoSDRMethod;   set { if (Set(ref _HDRtoSDRMethod, value))  player?.renderer?.UpdateHDRtoSDR(); } }
        HDRtoSDRMethod _HDRtoSDRMethod = HDRtoSDRMethod.Hable;

       /// <summary>
        /// SDR Display Peak Luminance - tonemap for HDR to SDR (based on Auto/Custom)
        /// </summary>
        [JsonIgnore]
        public float SDRDisplayNits
        {
            get => SDRDisplayNitsCustom == 0 ? (SDRDisplayNitsAuto != 0 ? SDRDisplayNitsAuto : 200) : SDRDisplayNitsCustom;
            set => SDRDisplayNitsCustom = value;
        }

        /// <summary>
        /// SDR Display Peak Luminance - tonemap for HDR to SDR (Recommended)
        /// </summary>
        [JsonIgnore]
        public float            SDRDisplayNitsAuto          { get => _SDRDisplayNitsAuto;   set { if (Set(ref _SDRDisplayNitsAuto, value) && _SDRDisplayNitsCustom == 0) { player?.renderer?.UpdateHDRtoSDR(); RaiseUI(nameof(SDRDisplayNits)); } } }
        float _SDRDisplayNitsAuto;

        /// <summary>
        /// SDR Display Peak Luminance - tonemap for HDR to SDR (Custom)
        /// </summary>
        public float            SDRDisplayNitsCustom        { get => _SDRDisplayNitsCustom; set { if (Set(ref _SDRDisplayNitsCustom, value)) { player?.renderer?.UpdateHDRtoSDR(); RaiseUI(nameof(SDRDisplayNits)); } } }
        float _SDRDisplayNitsCustom;

        /// <summary>
        /// Whether the renderer will use 10-bit swap chaing or 8-bit output
        /// </summary>
        public bool             Swap10Bit                   { get; set; }

        /// <summary>
        /// The number of buffers to use for the renderer's swap chain
        /// </summary>
        public uint             SwapBuffers                 { get; set; } = 2;

        /// <summary>
        /// <para>
        /// Whether the renderer will use R8G8B8A8_UNorm instead of B8G8R8A8_UNorm format for the swap chain (experimental)<br/>
        /// (TBR: causes slightly different colors with D3D11VP)
        /// </para>
        /// </summary>
        public bool             SwapForceR8G8B8A8           { get; set; } // TBR: Causes issues with NVidia Super Resolution (with some formats)

        /// <summary>
        /// Enables custom Direct2D drawing over playback frames
        /// </summary>
        public bool             Use2DGraphics               { get; set; }

        public event EventHandler<Vortice.Direct2D1.ID2D1DeviceContext> D2DInitialized;
        public event EventHandler<Vortice.Direct2D1.ID2D1DeviceContext> D2DDisposing;
        public event EventHandler<Vortice.Direct2D1.ID2D1DeviceContext> D2DDraw;

        internal void OnD2DInitialized(Renderer renderer, Vortice.Direct2D1.ID2D1DeviceContext context)
            => D2DInitialized?.Invoke(renderer, context);

        internal void OnD2DDisposing(Renderer renderer, Vortice.Direct2D1.ID2D1DeviceContext context)
            => D2DDisposing?.Invoke(renderer, context);

        internal void OnD2DDraw(Renderer renderer, Vortice.Direct2D1.ID2D1DeviceContext context)
            => D2DDraw?.Invoke(renderer, context);

        /// <summary>
        /// When you change a filter value from one VP will update also the other if exists (however might not exact same picture output)
        /// </summary>
        public bool             SyncVPFilters               { get; set; } = true;
        public ObservableDictionary<VideoFilters, FLVideoFilter> Filters
                                                            { get ; set; } = [];
        public ObservableDictionary<VideoFilters, D3VideoFilter> D3Filters
                                                            { get ; set; } = [];
        internal readonly object lockFilters    = new();
        internal readonly object lockD3Filters  = new();
    }
    public class AudioConfig : NotifyPropertyChanged
    {
        public AudioConfig Clone()
        {
            AudioConfig audio = (AudioConfig) MemberwiseClone();
            audio.player = null;

            return audio;
        }

        internal Player player;

        /// <summary>
        /// Audio delay ticks (will be reseted to 0 for every new audio stream)
        /// </summary>
        public long             Delay               { get => _Delay;            set { if (player != null && !player.Audio.IsOpened) return;  if (Set(ref _Delay, value)) player?.ReSync(player.decoder.AudioStream); } }
        long _Delay;
        internal void SetDelay(long delay)          => Set(ref _Delay, delay, true, nameof(Delay));

        /// <summary>
        /// Whether audio should allowed
        /// </summary>
        public bool             Enabled             { get => _Enabled;          set { if (Set(ref _Enabled, value)) if (value) player?.Audio.Enable(); else player?.Audio.Disable(); } }
        bool _Enabled = true;
        internal void SetEnabled(bool enabled)      => Set(ref _Enabled, enabled, true, nameof(Enabled));

        /// <summary>
        /// Uses FFmpeg filters instead of Swr (better speed quality and support for extra filters, requires avfilter-X.dll)
        /// </summary>
        public bool             FiltersEnabled      { get => _FiltersEnabled; set { if (Set(ref _FiltersEnabled, value && Engine.Config.FFmpegLoadProfile != LoadProfile.Main)) player?.AudioDecoder.SetupFiltersOrSwr(); } }
        bool _FiltersEnabled = true;

        /// <summary>
        /// List of filters for post processing the audio samples (experimental)<br/>
        /// (Requires FiltersEnabled)
        /// </summary>
        public List<Filter>     Filters             { get; set; }

        /// <summary>
        /// Audio languages preference by priority
        /// </summary>
        public List<Language>   Languages           { get { _Languages ??= GetSystemLanguages(); return _Languages; } set => _Languages = value; }
        List<Language> _Languages;
    }
    public class SubtitlesConfig : NotifyPropertyChanged
    {
        public SubtitlesConfig Clone()
        {
            SubtitlesConfig subs = (SubtitlesConfig) MemberwiseClone();

            subs.Languages = [];
            if (Languages != null) foreach(var lang in Languages) subs.Languages.Add(lang);

            subs.player = null;

            return subs;
        }

        internal Player player;

        /// <summary>
        /// Subtitle delay ticks (will be reseted to 0 for every new subtitle stream)
        /// </summary>
        public long             Delay               { get => _Delay; set { if (player != null && !player.Subtitles.IsOpened) return; if (Set(ref _Delay, value)) player?.ReSync(player.decoder.SubtitlesStream); } }
        long _Delay;
        internal void SetDelay(long delay)          => Set(ref _Delay, delay, true, nameof(Delay));

        /// <summary>
        /// Whether subtitles should be allowed
        /// </summary>
        public bool             Enabled             { get => _Enabled; set { if(Set(ref _Enabled, value)) if (value) player?.Subtitles.Enable(); else player?.Subtitles.Disable(); } }
        bool _Enabled = true;
        internal void SetEnabled(bool enabled)      => Set(ref _Enabled, enabled, true, nameof(Enabled));

        /// <summary>
        /// Subtitle languages preference by priority
        /// </summary>
        public List<Language>   Languages           { get { _Languages ??= GetSystemLanguages(); return _Languages; } set => _Languages = value; }
        List<Language> _Languages;

        /// <summary>
        /// Whether to use local search plugins (see also <see cref="SearchLocalOnInputType"/>)
        /// </summary>
        public bool             SearchLocal         { get => _SearchLocal; set => Set(ref _SearchLocal, value); }
        bool _SearchLocal = false;

        /// <summary>
        /// Allowed input types to be searched locally for subtitles (empty list allows all types)
        /// </summary>
        public List<InputType>  SearchLocalOnInputType
                                                    { get; set; } = [InputType.File, InputType.UNC, InputType.Torrent];

        /// <summary>
        /// Whether to use online search plugins (see also <see cref="SearchOnlineOnInputType"/>)
        /// </summary>
        public bool             SearchOnline        { get => _SearchOnline; set { Set(ref _SearchOnline, value); if (player != null && player.Video.isOpened) Task.Run(() => { if (player != null && player.Video.isOpened) player.decoder.SearchOnlineSubtitles(); }); } }
        bool _SearchOnline = false;

        /// <summary>
        /// Allowed input types to be searched online for subtitles (empty list allows all types)
        /// </summary>
        public List<InputType>  SearchOnlineOnInputType
                                                    { get; set; } = [InputType.File, InputType.UNC, InputType.Torrent];

        /// <summary>
        /// Subtitles parser (can be used for custom parsing)
        /// </summary>
        [JsonIgnore]
        public Action<SubtitlesFrame>
                                Parser              { get; set; } = ParseSubtitles.Parse;
    }
    public class DataConfig : NotifyPropertyChanged
    {
        public DataConfig Clone()
        {
            DataConfig data = (DataConfig)MemberwiseClone();

            data.player = null;

            return data;
        }

        internal Player player;

        /// <summary>
        /// Whether data should be processed
        /// </summary>
        public bool             Enabled             { get => _Enabled; set { if (Set(ref _Enabled, value)) if (value) player?.Data.Enable(); else player?.Data.Disable(); } }
        bool _Enabled = false;
        internal void SetEnabled(bool enabled) => Set(ref _Enabled, enabled, true, nameof(Enabled));
    }
}

/// <summary>
/// Engine's configuration
/// </summary>
public class EngineConfig
{
    /// <summary>
    /// It will not initiallize audio and will be disabled globally
    /// </summary>
    public bool     DisableAudio            { get; set; }

    /// <summary>
    /// <para>Required to register ffmpeg libraries. Make sure you provide x86 or x64 based on your project.</para>
    /// <para>:&lt;path&gt; for relative path from current folder or any below</para>
    /// <para>&lt;path&gt; for absolute or relative path</para>
    /// </summary>
    public string   FFmpegPath              { get; set; } = "FFmpeg";

    /// <summary>
    /// <para>Can be used to choose which FFmpeg libs to load</para>
    /// All (Devices &amp; Filters)<br/>
    /// Filters<br/>
    /// Main<br/>
    /// </summary>
    public LoadProfile
                    FFmpegLoadProfile       { get; set; } = LoadProfile.All;

    /// <summary>
    /// Whether to allow HLS live seeking (this can cause segmentation faults in case of incompatible ffmpeg version with library's custom structures)
    /// </summary>
    public bool     FFmpegHLSLiveSeek       { get; set; }

    /// <summary>
    /// Sets FFmpeg logger's level
    /// </summary>
    public Flyleaf.FFmpeg.LogLevel
                    FFmpegLogLevel          { get => _FFmpegLogLevel; set { _FFmpegLogLevel = value; if (Engine.IsLoaded) FFmpegEngine.SetLogLevel(); } }
    Flyleaf.FFmpeg.LogLevel _FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Quiet;

    /// <summary>
    /// Whether configuration has been loaded from file
    /// </summary>
    [JsonIgnore]
    public bool     Loaded                  { get; private set; }

    /// <summary>
    /// The path that this configuration has been loaded from
    /// </summary>
    [JsonIgnore]
    public string   LoadedPath              { get; private set; }

    /// <summary>
    /// <para>Sets loggers output</para>
    /// <para>:debug -> System.Diagnostics.Debug</para>
    /// <para>:console -> System.Console</para>
    /// <para>&lt;path&gt; -> Absolute or relative file path</para>
    /// </summary>
    public string   LogOutput               { get => _LogOutput; set { _LogOutput = value; if (Engine.IsLoaded) SetOutput(); } }
    string _LogOutput = "";

    /// <summary>
    /// Sets logger's level
    /// </summary>
    public LogLevel LogLevel                { get; set; } = LogLevel.Quiet;

    /// <summary>
    /// When the output is file it will append instead of overwriting
    /// </summary>
    public bool     LogAppend               { get; set; }

    /// <summary>
    /// Lines to cache before writing them to file
    /// </summary>
    public int      LogCachedLines          { get; set; } = 20;

    /// <summary>
    /// Sets the logger's datetime string format
    /// </summary>
    public string   LogDateTimeFormat       { get; set; } = "HH.mm.ss.fff";

    /// <summary>
    /// <para>Required to register plugins. Make sure you provide x86 or x64 based on your project and same .NET framework.</para>
    /// <para>:&lt;path&gt; for relative path from current folder or any below</para>
    /// <para>&lt;path&gt; for absolute or relative path</para>
    /// </summary>
    public string   PluginsPath             { get; set; } = "Plugins";

    /// <summary>
    /// <para>Activates Master Thread to monitor all the players and perform the required updates</para>
    /// <para>Required for Activity Mode, Stats &amp; Buffered Duration on Pause</para>
    /// </summary>
    public bool     UIRefresh               { get => _UIRefresh; set { _UIRefresh = value; if (value && Engine.IsLoaded) Engine.StartThread(); } }
    static bool _UIRefresh = true;

    /// <summary>
    /// <para>How often should update the UI in ms (low values can cause performance issues)</para>
    /// <para>Should UIRefreshInterval &lt; 1000ms and 1000 % UIRefreshInterval == 0 for accurate per second stats</para>
    /// </summary>
    public int      UIRefreshInterval       { get; set; } = 250;

    /// <summary>
    /// Loads engine's configuration
    /// </summary>
    /// <param name="path">Absolute or relative path to load the configuraiton</param>
    /// <returns></returns>
    public static EngineConfig Load(string path)
    {
        EngineConfig config = JsonSerializer.Deserialize<EngineConfig>(File.ReadAllText(path));
        config.Loaded       = true;
        config.LoadedPath   = path;

        return config;
    }

    /// <summary>
    /// Saves engine's current configuration
    /// </summary>
    /// <param name="path">Absolute or relative path to save the configuration</param>
    public void Save(string path = null)
    {
        if (path == null)
        {
            if (string.IsNullOrEmpty(LoadedPath))
                return;

            path = LoadedPath;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Config.jsonOpts));
    }
}

public enum UIRefreshType
{
    PerFrame,
    PerFrameSecond,
    PerUIRefreshInterval,
    PerUISecond
}
