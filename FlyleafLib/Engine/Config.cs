using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Data;

using Vortice.Direct3D11;

using ID2D1DeviceContext = Vortice.Direct2D1.ID2D1DeviceContext;

using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;

using Filter = FlyleafLib.MediaFramework.MediaDecoder.Filter;

namespace FlyleafLib;

/// <summary>Player settings saved by <see cref="Save(string)"/> or <see cref="Serialize"/>. Current playback adjustments belong to <see cref="Player"/> and its components.</summary>
public class Config : NotifyPropertyChanged
{
    public const string USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";

    public static JsonSerializerOptions jsonOpts = new() { WriteIndented = true };

    /// <summary>Creates player settings and initializes plugin options from the running engine.</summary>
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

        Playback.config = this;
        Demuxer.config  = this;
    }
    /// <summary>Copies the configuration sections, rebuilding plugin defaults and leaving shortcut entries empty. Some mutable collections remain shared.</summary>
    public Config Clone()
    {
        Config config = new()
        {
            Audio       = Audio.Clone(),
            Video       = Video.Clone(),
            Subtitles   = Subtitles.Clone(),
            Demuxer     = Demuxer.Clone(),
            Playback    = Playback.Clone(),
            Data        = Data.Clone()
        };

        config.Playback.config = config;
        config.Demuxer.config = config;

        return config;
    }
    /// <summary>Loads the current player JSON format from a file and records the source path.</summary>
    /// <param name="path">Absolute or relative JSON file path.</param>
    public static Config Load(string path)
    {
        var config = Deserialize(File.ReadAllText(path));
        config.LoadedPath = path;
        return config;
    }

    /// <summary>Loads the current player JSON format, validates sections, and reconciles options with the installed plugins.</summary>
    /// <param name="json">Player configuration JSON in the current schema.</param>
    public static Config Deserialize(string json)
    {
        var config = JsonSerializer.Deserialize<Config>(json) ?? throw new JsonException("Player configuration must be an object.");
        if (config.Playback?.KeyBindings == null || config.Demuxer == null || config.Video == null ||
            config.Audio == null || config.Subtitles == null || config.Data == null ||
            config.Video.FLFilters == null || config.Video.D3Filters == null || config.Plugins == null ||
            config.Plugins.Values.Any(options => options == null))
            throw new JsonException("Player configuration contains a null section.");

        config.Playback.config = config;
        config.Demuxer.config = config;
        config.Video.EnableFilterCollectionSynchronization();
        config.Loaded = true;

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
    /// <summary>Saves the current player settings as JSON. An omitted path uses <see cref="LoadedPath"/> when available.</summary>
    /// <param name="path">Absolute or relative JSON file path, or null to reuse <see cref="LoadedPath"/>.</param>
    public void Save(string path = null)
    {
        if (path == null)
        {
            if (string.IsNullOrEmpty(LoadedPath))
                return;

            path = LoadedPath;
        }

        File.WriteAllText(path, Serialize());
    }

    /// <summary>Serializes player settings as JSON and first synchronizes compatible filter values when enabled on an attached renderer.</summary>
    public string Serialize()
    {
        // Serialize the effective values for both video processors.
        if (Video.player != null && Video.player.Renderer != null)
            Video.player.Renderer.SyncFilters();

        lock (Video.lockFLFilters)
        lock (Video.lockD3Filters)
            return JsonSerializer.Serialize(this, jsonOpts);
    }

    internal void SetPlayer(Player player)
    {
        Playback.config = this;
        Demuxer.config  = this;
        Playback.player = player;
        Playback.KeyBindings.SetPlayer(player);
        Demuxer.player  = player;
        Audio.player    = player;
        Video.player    = player;
        Subtitles.player= player;
    }

    /// <summary>Whether <see cref="Load"/> or <see cref="Deserialize"/> created this instance. This metadata is excluded from JSON.</summary>
    [JsonIgnore]
    public bool             Loaded      { get; private set; }

    /// <summary>File path recorded by <see cref="Load"/> and reused by <see cref="Save(string)"/> when no path is supplied.</summary>
    [JsonIgnore]
    public string           LoadedPath  { get; private set; }

    /// <summary>Playback, buffering, capture and shortcut preferences.</summary>
    public PlaybackConfig   Playback    { get; set; } = new();
    /// <summary>Input discovery, buffering, interruption and FFmpeg demuxer options.</summary>
    public DemuxerConfig    Demuxer     { get; set; } = new();
    /// <summary>Video output, picture filters and video decoder preferences.</summary>
    public VideoConfig      Video       { get; set; } = new();
    /// <summary>Audio defaults, filter definitions and audio decoder preferences.</summary>
    public AudioConfig      Audio       { get; set; } = new();
    /// <summary>Subtitle selection, search and decoder preferences.</summary>
    public SubtitlesConfig  Subtitles   { get; set; } = new();
    /// <summary>Data decoder limits. The current data enablement is set on <see cref="Player.Data"/>.</summary>
    public DataConfig       Data        { get; set; } = new();

    /// <summary>Saved option dictionaries keyed by plugin name. Loading reconciles entries with installed plugin defaults.</summary>
    public Dictionary<string, ObservableDictionary<string, string>>
                            Plugins     { get; set; } = [];
    private
           Dictionary<string, ObservableDictionary<string, string>>
                            defaultPlugins;

    /// <summary>Saved playback preferences. Current speed, position, repeat, reverse playback and statistics collection belong to <see cref="Player"/>.</summary>
    public class PlaybackConfig : NotifyPropertyChanged
    {
        public PlaybackConfig Clone()
        {
            PlaybackConfig player = (PlaybackConfig) MemberwiseClone();
            player.player = null;
            player.config = null;
            player.KeyBindings = KeyBindings.Clone();
            return player;
        }

        internal Player player;
        internal Config config;

        /// <summary>Automatically starts playback after a successful open or when seeking after playback ends.</summary>
        public bool     AutoPlay                    { get; set; } = true;

        /// <summary>Minimum buffered duration in ticks before playback starts. Assignment can raise <see cref="DemuxerConfig.BufferDuration"/>.</summary>
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

        /// <summary>Saved shortcut definitions. Applications attach custom action delegates at runtime.</summary>
        public KeysConfig
                        KeyBindings                 { get; set; } = new();

        /// <summary>Requests playback of the latest available live video frame and updates <see cref="VideoConfig.LowDelay"/> when linked to a configuration.</summary>
        public bool     ZeroLatency                 { get => _ZeroLatency; set { Set(ref _ZeroLatency, value); if (config != null) config.Video.LowDelay = value; if (player != null && player.IsPlaying) { player.Pause(); player.Play(); } } }
        bool _ZeroLatency;

        /// <summary>Maximum live-stream latency in ticks. A positive value enables speed correction and adjusts buffering and decoder low-delay settings. Zero disables correction.</summary>
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
                        config.Video.LowDelay = false;

                    return;
                }

                // Large max buffer so we ensure the actual latency distance
                if (config != null)
                {
                    if (config.Demuxer.BufferDuration < value * 2)
                        config.Demuxer.BufferDuration = value * 2;

                    config.Video.LowDelay = true;
                }

                // Small min buffer to avoid enabling latency speed directly
                if (_MinBufferDuration > value / 10)
                    MinBufferDuration = value / 10;
            }
        }
        long _MaxLatency = 0;

        /// <summary>Lower live-stream latency target in ticks when <see cref="MaxLatency"/> controls playback speed.</summary>
        public long     MinLatency                  { get => _MinLatency; set => Set(ref _MinLatency, value); }
        long _MinLatency = 0;

        /// <summary>Minimum interval in ticks between speed changes made by the live-latency controller.</summary>
        public long     LatencySpeedChangeInterval  { get; set; } = TimeSpan.FromMilliseconds(700).Ticks;

        /// <summary>Destination directory for recordings created without an explicit filename.</summary>
        public string   FolderRecordings            { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings");

        /// <summary>Destination directory for snapshots created without an explicit filename.</summary>
        public string   FolderSnapshots             { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Snapshots");

        /// <summary>Uses frame-accurate video seeking for position changes and seek shortcuts that follow this preference.</summary>
        public bool     SeekAccurate                { get => _SeekAccurate; set => Set(ref _SeekAccurate, value); }
        bool _SeekAccurate;

        /// <summary>Keeps a video frame available for snapshots when the swapchain cannot present, at an additional rendering cost.</summary>
        public bool     SnapshotAlways              { get; set; }

        /// <summary>Default snapshot file format when no filename is supplied. Supported values are bmp, png, jpg and jpeg.</summary>
        public string   SnapshotFormat              { get ;set; } = "bmp";

        /// <summary>Priority assigned when creating the playback thread.</summary>
        public ThreadPriority
                        ThreadPriority              { get; set; } = ThreadPriority.AboveNormal;

        /// <summary>Cadence for updating <see cref="Player.CurTime"/> in the UI.</summary>
        public UIRefreshType
                        UICurTime                   { get; set; } = UIRefreshType.PerFrameSecond;

        /// <summary>Primary seek shortcut step in ticks.</summary>
        public long     SeekOffset                  { get; set; } = 5 * (long)1000 * 10000;
        /// <summary>Secondary seek shortcut step in ticks.</summary>
        public long     SeekOffset2                 { get; set; } = 15 * (long)1000 * 10000;
        /// <summary>Tertiary seek shortcut step in ticks.</summary>
        public long     SeekOffset3                 { get; set; } = 30 * (long)1000 * 10000;
        /// <summary>Primary shortcut step for changing the playback speed multiplier.</summary>
        public double   SpeedOffset                 { get; set; } = 0.10;
        /// <summary>Secondary shortcut step for changing the playback speed multiplier.</summary>
        public double   SpeedOffset2                { get; set; } = 0.25;
    }
    /// <summary>Saved input, packet-buffering and FFmpeg format preferences.</summary>
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

        /// <summary>Runs FFmpeg stream probing during open to discover codec and stream information.</summary>
        public bool             AllowFindStreamInfo { get; set; } = true;

        /// <summary>Installs the demuxer interrupt callback for cancellation and timeouts.</summary>
        public bool             AllowInterrupts     { get; set; } = true;

        /// <summary>Allows interruption of FFmpeg frame reads through the demuxer callback.</summary>
        public bool             AllowReadInterrupts { get; set; } = true;

        /// <summary>Applies the configured timeouts through the demuxer interrupt callback.</summary>
        public bool             AllowTimeouts       { get; set; } = true;

        /// <summary>FFmpeg input formats exempt from read interruption.</summary>
        public List<string>     ExcludeInterruptFmts{ get; set; } = ["rtsp"];

        /// <summary>Maximum buffered duration in ticks. Assignment can lower <see cref="PlaybackConfig.MinBufferDuration"/>.</summary>
        public long             BufferDuration      {
            get => _BufferDuration;
            set
            {
                if (!Set(ref _BufferDuration, value)) return;
                if (config != null && value < config.Playback.MinBufferDuration)
                   config.Playback.MinBufferDuration = value;
            }
        }
        long _BufferDuration = 3 * (long)1000 * 10000; // At least 3 sec. required for weird/delayed interleaved audio with video

        /// <summary>Additional packet-count limit for the demuxer buffer. Zero disables the packet limit.</summary>
        public long             BufferPackets       { get; set; }

        /// <summary>Audio packet queue limit. Excess packets are dropped and the demuxer raises its AudioLimit event. Zero disables the limit.</summary>
        public long             MaxAudioPackets     { get; set; }

        /// <summary>Maximum demuxing errors before stopping.</summary>
        public int              MaxErrors           { get; set; } = 30;

        /// <summary>FFmpeg AVIO buffer size in bytes for custom streams.</summary>
        public int              IOStreamBufferSize  { get; set; } = 0x200000;

        /// <summary>Timeout in ticks for closing an input when the protocol supports interruption.</summary>
        public long             CloseTimeout        { get => closeTimeout; set { closeTimeout = value; closeTimeoutMs = value / 10000; } }
        private long closeTimeout = 10 * 1000 * 10000;
        internal long closeTimeoutMs = 10 * 1000;

        /// <summary>Timeout in ticks for opening an input and discovering stream information when interruption is supported.</summary>
        public long             OpenTimeout         { get => openTimeout; set { openTimeout = value; openTimeoutMs = value / 10000; } }
        private long openTimeout = 5 * 60 * (long)1000 * 10000;
        internal long openTimeoutMs = 5 * 60 * 1000;

        /// <summary>Timeout in ticks for reading a non-live input when interruption is supported.</summary>
        public long             ReadTimeout         { get => readTimeout; set { readTimeout = value; readTimeoutMs = value / 10000; } }
        private long readTimeout = 30 * 1000 * 10000;
        internal long readTimeoutMs = 30 * 1000;

        /// <summary>Timeout in ticks for reading a live input when interruption is supported.</summary>
        public long             ReadLiveTimeout     { get => readLiveTimeout; set { readLiveTimeout = value; readLiveTimeoutMs = value / 10000; } }
        private long readLiveTimeout = 60 * 1000 * 10000;
        internal long readLiveTimeoutMs = 60 * 1000;

        /// <summary>Timeout in ticks for seeking when the protocol supports interruption.</summary>
        public long             SeekTimeout         { get => seekTimeout; set { seekTimeout = value; seekTimeoutMs = value / 10000; } }
        private long seekTimeout = 30 * 1000 * 10000;
        internal long seekTimeoutMs = 30 * 1000;

        /// <summary>Overrides FFmpeg input format detection with the specified format name.</summary>
        public string           ForceFormat         { get; set; }

        /// <summary>Overrides the video stream frame rate when positive, including for raw inputs without timestamps.</summary>
        public double           ForceFPS            { get; set; }

        /// <summary>Saved FFmpeg format flags applied to the demuxer.</summary>
        public DemuxerFlags     FormatFlags         { get; set; } = DemuxerFlags.DiscardCorrupt;// FFmpeg.AutoGen.ffmpeg.AVFMT_FLAG_DISCARD_CORRUPT;

        /// <summary>Forwards format options and HTTP query parameters to nested FFmpeg inputs.</summary>
        public bool             FormatOptToUnderlying
                                                    { get; set; }

        /// <summary>Includes the original input URL query parameters when constructing queries for nested HTTP inputs.</summary>
        public bool             DefaultHTTPQueryToUnderlying
                                                    { get; set; }

        /// <summary>Additional query parameters for nested HTTP inputs.</summary>
        public Dictionary<string, string>
                                ExtraHTTPQueryParamsToUnderlying
                                                    { get; set; } = [];

        /// <summary>FFmpeg demuxer options for the main video input.</summary>
        public Dictionary<string, string>
                                FormatOpt           { get; set; } = DefaultFormatOpt();
        /// <summary>FFmpeg demuxer options for a separately opened audio input.</summary>
        public Dictionary<string, string>
                                AudioFormatOpt      { get; set; } = DefaultFormatOpt();

        /// <summary>FFmpeg demuxer options for a separately opened subtitle input.</summary>
        public Dictionary<string, string>
                                SubtitlesFormatOpt  { get; set; } = DefaultFormatOpt();

        /// <summary>Creates the default demuxer option dictionary.</summary>
        public static Dictionary<string, string> DefaultFormatOpt() => new()
        {
            // HTTP
            ["user_agent"]      = USER_AGENT,
            ["reconnect"]       = "1"           // Recover premature disconnections (YT external audio)
        };

        /// <summary>Returns the configured format option dictionary for the requested input type.</summary>
        public Dictionary<string, string> GetFormatOptPtr(MediaType type)
            => type == MediaType.Video ? FormatOpt : type == MediaType.Audio ? AudioFormatOpt : SubtitlesFormatOpt;
    }
    /// <summary>Saved video preferences and renderer integration hooks. Current framing and processing controls are on <see cref="Renderer"/>.</summary>
    public class VideoConfig : NotifyPropertyChanged
    {
        public VideoConfig() => EnableFilterCollectionSynchronization();

        internal void EnableFilterCollectionSynchronization()
        {
            UIInvokeIfRequired(() =>
            {
                BindingOperations.EnableCollectionSynchronization(FLFilters, lockFLFilters);
                BindingOperations.EnableCollectionSynchronization(D3Filters, lockD3Filters);
            });
        }

        public VideoConfig Clone()
        {
            VideoConfig video = (VideoConfig) MemberwiseClone();
            video.player = null;

            video.CodecOpt = CodecOpt == null ? null : new(CodecOpt);

            return video;
        }

        internal Player player;

        /// <summary>SDR pixel format used when creating the video swapchain.</summary>
        public SwapChainFormat  SwapChainFormat         { get; set; } = SwapChainFormat.BGRA;

        /// <summary>Allows FP16 scRGB output for supported HDR content on a display with Windows HDR enabled.</summary>
        public bool             AllowHDRSwapchain       { get => allowHDRSwapchain; set { if (Set(ref allowHDRSwapchain, value)) player?.Renderer?.VPRequest(VPRequestType.UpdateSwapChain); } }
        bool allowHDRSwapchain = true;

        /// <summary>Video presentation sync interval. Zero disables vertical synchronization.</summary>
        public uint             VSync                   { get; set; } = 1;

        /// <summary>Color used to clear the renderer and fill areas outside the video.</summary>
        public System.Windows.Media.Color
                                BackColor               { get => VorticeToWPFColor(flBackColor);  set { if (Set(ref flBackColor, WPFToVorticeColor(value))) { d3BackColor = WPFToVideoColor(value); player?.Renderer?.VPRequest(VPRequestType.BackColor); } } }
        internal Vortice.Mathematics.Color flBackColor = new(0, 0, 0, 255);
        internal VideoColor d3BackColor = new() { Rgba = new() { R = 0, G = 0, B = 0, A = 1 } };

        /// <summary>Step in percentage points used by the renderer’s zoom commands.</summary>
        public int              ZoomOffset              { get => zoomOffset;            set { Set(ref zoomOffset,       value); } }
        int zoomOffset = 10;

        /// <summary>Renderer adapter preference by description, selected when the graphics device is created.</summary>
        public string           GPUAdapter                  { get; set; }

        /// <summary>Clears the video surface on open, stop and close.</summary>
        public bool             ClearScreen                 { get; set; } = true;

        /// <summary>Limits rendered frames per second, including during increased playback speed.</summary>
        public double           MaxOutputFps                { get; set; } = 60;

        /// <summary>Detected stream-selection resolution limit in pixels. Excluded from JSON.</summary>
        [JsonIgnore]
        public int              MaxVerticalResolutionAuto   { get; internal set; }

        /// <summary>Saved vertical resolution limit in pixels for stream selection. Zero uses automatic detection.</summary>
        public int              MaxVerticalResolutionCustom { get => _MaxVerticalResolutionCustom;  set => Set(ref _MaxVerticalResolutionCustom, value); }
        int _MaxVerticalResolutionCustom;

        /// <summary>Effective stream-selection limit in pixels, using the override, detected value or 1080 fallback. Excluded from JSON.</summary>
        [JsonIgnore]
        public int              MaxVerticalResolution       => MaxVerticalResolutionCustom == 0 ? (MaxVerticalResolutionAuto != 0 ? MaxVerticalResolutionAuto : 1080) : MaxVerticalResolutionCustom;

        /// <summary>Minimum SDR display luminance in nits used for display mapping. Minus one selects the automatic value.</summary>
        public float            TargetSDRMinNits            { get => _targetSDRMinNits;    set {  if (Set(ref _targetSDRMinNits, value)) player?.Renderer?.FLUpdateTargetNitsConfig(false); } }
        float _targetSDRMinNits = -1;

        /// <summary>Peak SDR display luminance in nits used for display mapping. Minus one selects the automatic value.</summary>
        public float            TargetSDRPeakNits           { get => _targetSDRPeakNits;    set {  if (Set(ref _targetSDRPeakNits, value)) player?.Renderer?.FLUpdateTargetNitsConfig(false); } }
        float _targetSDRPeakNits = -1;

        /// <summary>Minimum HDR display luminance in nits used for display mapping. Minus one selects the automatic value.</summary>
        public float            TargetHDRMinNits            { get => _targetHDRMinNits;    set {  if (Set(ref _targetHDRMinNits, value)) player?.Renderer?.FLUpdateTargetNitsConfig(true); } }
        float _targetHDRMinNits = -1;

        /// <summary>Peak HDR display luminance in nits used for display mapping. Minus one selects the automatic value.</summary>
        public float            TargetHDRPeakNits           { get => _targetHDRPeakNits;    set {  if (Set(ref _targetHDRPeakNits, value)) player?.Renderer?.FLUpdateTargetNitsConfig(true); } }
        float _targetHDRPeakNits = -1;

        /// <summary>Requests Super Resolution for eligible video on the D3D11 processor. <see cref="Renderer.SuperResolution"/> reports renderer state.</summary>
        public bool             SuperResolution             { get => _SuperResolution;  set { if (Set(ref _SuperResolution, value)) player?.Renderer?.VPRequest(VPRequestType.Viewport); } }
        internal bool _SuperResolution;

        /// <summary>Requests hardware video decoding on decoder setup. <see cref="MediaPlayer.Video.HWAcceleration"/> reports the effective result.</summary>
        public bool             HWAcceleration           { get; set; } = true;
        /// <summary>Toggles the saved hardware decoding preference for the next decoder setup.</summary>
        public void ToggleHWAcceleration() => HWAcceleration = !HWAcceleration;

        /// <summary>Runtime factory for custom GPU post-processing. Assign before renderer device creation. Excluded from JSON.</summary>
        [JsonIgnore]
        public IVideoPostProcessorFactory
                                PostProcessorFactory        { get; set; }

        /// <summary>Presents both output fields during supported deinterlacing. False presents one output per input frame.</summary>
        public bool             DoubleRate                  { get => _DoubleRate;       set => Set(ref _DoubleRate, value); }
        bool _DoubleRate = true;

        /// <summary>Enables Direct2D setup when the renderer creates its device. Register drawing callbacks separately.</summary>
        public bool             Use2DGraphics               { get; set; }

        /// <summary>Scaling flags used for bitmap subtitle rendering.</summary>
        public SwsFlags         BitmapSubsScaleQuality      { get; set; } = SwsFlags.Bilinear | SwsFlags.Bitexact;

        /// <summary>Runtime notification that the renderer created its Direct2D drawing context.</summary>
        public event EventHandler<ID2D1DeviceContext> D2DInitialized;
        /// <summary>Runtime notification before the renderer releases its Direct2D drawing context.</summary>
        public event EventHandler<ID2D1DeviceContext> D2DDisposing;
        /// <summary>Runtime hook for drawing with Direct2D over a playback frame.</summary>
        public event EventHandler<ID2D1DeviceContext> D2DDraw;

        internal void OnD2DInitialized(Renderer renderer, ID2D1DeviceContext context)
            => D2DInitialized?.Invoke(renderer, context);

        internal void OnD2DDisposing(Renderer renderer, ID2D1DeviceContext context)
            => D2DDisposing?.Invoke(renderer, context);

        internal void OnD2DDraw(Renderer renderer, ID2D1DeviceContext context)
            => D2DDraw?.Invoke(renderer, context);

        /// <summary>Synchronizes compatible picture filter values across processors when switching or serializing.</summary>
        public bool             SyncVPFilters               { get => _SyncVPFilters; set => Set(ref _SyncVPFilters, value); }
        bool _SyncVPFilters = true;
        /// <summary>Saved Flyleaf processor filter entries and their configured values.</summary>
        public ObservableDictionary<FLFilters, FLFilter>
                                FLFilters                   { get ; set; } = [];
        /// <summary>Saved D3D11 processor filter entries and their configured values. The renderer refreshes device capabilities.</summary>
        public ObservableDictionary<VideoProcessorFilter, D3Filter>
                                D3Filters                   { get ; set; } = [];

        // These locks belong to the config-owned dictionaries and their WPF registrations.
        internal readonly object lockFLFilters  = new();
        internal readonly object lockD3Filters  = new();

        // Decoder

        /// <summary>Requested number of video decoder threads.</summary>
        public int              DecoderThreads      { get; set; } = Environment.ProcessorCount;

        /// <summary>Video frame budget for decoding and rendering. Hardware allocation can reduce the stored value.</summary>
        public int              MaxFrames           { get => _MaxFrames;   set { if (Set(ref _MaxFrames, value)) { player?.RefreshMaxVideoFrames(); } } }
        int _MaxFrames = 4; // At least 4 required for smooth playback (can cause audio crackling)
        internal void SetMaxFrames(int maxVideoFrames) { _MaxFrames = maxVideoFrames; RaiseUI(nameof(MaxFrames)); } // can be updated by video decoder if fails to allocate them

        /// <summary>Portion of the video frame budget reserved for previously decoded frames.</summary>
        public int              MaxFramesPrev       { get => _MaxFramesPrev;   set { if (Set(ref _MaxFramesPrev, value)) { player?.RefreshMaxVideoFrames(); } } }
        int _MaxFramesPrev = 0;

        /// <summary>Allows attempting hardware decoding when the stream profile differs from the hardware decoder’s advertised profile.</summary>
        public bool             AllowProfileMismatch{ get => _AllowProfileMismatch; set => Set(ref _AllowProfileMismatch, value); }
        bool _AllowProfileMismatch = false;

        /// <summary>Requests decoded output from frames marked corrupt.</summary>
        public bool             ShowCorrupted       { get => _ShowCorrupted;        set => Set(ref _ShowCorrupted, value); }
        bool _ShowCorrupted;

        /// <summary>Requests low-delay video decoding on setup. Playback latency settings can update this value.</summary>
        public bool             LowDelay            { get => _LowDelay;             set => Set(ref _LowDelay, value); }
        bool _LowDelay;

        /// <summary>Allows the decoder to skip frames and affects the low-delay flags applied during setup.</summary>
        public bool             AllowDropFrames     { get => _AllowDropFrames;      set => Set(ref _AllowDropFrames, value); }
        bool _AllowDropFrames;

        /// <summary>Maximum video decoding errors before stopping.</summary>
        public int              MaxErrors           { get; set; } = 200;

        /// <summary>Forced FFmpeg video decoder name. Null or empty selects automatically.</summary>
        public string Codec { get => _Codec; set => Set(ref _Codec, value); }
        internal string _Codec;

        /// <summary>FFmpeg option dictionary for opening the video decoder.</summary>
        public Dictionary<string, string> CodecOpt { get; set; } = [];

    }
    /// <summary>Saved audio selection, gain, filters and decoder preferences. Current volume, mute, device and synchronization are on <see cref="Player.Audio"/>.</summary>
    public class AudioConfig : NotifyPropertyChanged
    {
        public AudioConfig Clone()
        {
            AudioConfig audio = (AudioConfig) MemberwiseClone();
            audio.player = null;

            audio.CodecOpt = CodecOpt == null ? null : new(CodecOpt);

            return audio;
        }

        internal Player player;

        /// <summary>Primary audio delay shortcut step in ticks.</summary>
        public long             DelayOffset         { get; set; } =  100 * 10000;
        /// <summary>Secondary audio delay shortcut step in ticks.</summary>
        public long             DelayOffset2        { get; set; } = 1000 * 10000;

        /// <summary>Volume shortcut step in percentage points.</summary>
        public int              VolumeOffset        { get; set; } = 5;

        /// <summary>Audio amplification ceiling as a percentage of the original signal level.</summary>
        public int              VolumeMax           { get => _VolumeMax; set { Set(ref _VolumeMax, value); if (player != null && player.Audio.masteringVoice != null) player.Audio.masteringVoice.Volume = value / 100f;  } }
        int _VolumeMax = 150;

        /// <summary>Audio filter chain definitions used when building or reloading the FFmpeg graph. Runtime filter commands leave these definitions intact.</summary>
        public List<Filter>     Filters             { get; set; }

        /// <summary>Preferred audio languages in order. Defaults to the system language preferences.</summary>
        public List<Language>   Languages           { get { _Languages ??= GetSystemLanguages(); return _Languages; } set => _Languages = value; }
        List<Language> _Languages;
        // Decoder

        /// <summary>Audio decoder frame queue limit. Recording can temporarily override this value.</summary>
        public int              MaxFrames      { get; set; } = 5;

        /// <summary>Maximum audio decoding errors before stopping.</summary>
        public int              MaxErrors           { get; set; } = 200;

        /// <summary>Forced FFmpeg audio decoder name. Null or empty selects automatically.</summary>
        public string Codec { get => _Codec; set => Set(ref _Codec, value); }
        internal string _Codec;

        /// <summary>FFmpeg option dictionary for opening the audio decoder.</summary>
        public Dictionary<string, string> CodecOpt { get; set; } = [];

    }
    /// <summary>Saved subtitle selection, search and decoder preferences. Current synchronization and parsing callbacks are on <see cref="Player.Subtitles"/>.</summary>
    public class SubtitlesConfig : NotifyPropertyChanged
    {
        public SubtitlesConfig Clone()
        {
            SubtitlesConfig subs = (SubtitlesConfig) MemberwiseClone();

            subs.Languages = [];
            if (Languages != null) foreach(var lang in Languages) subs.Languages.Add(lang);

            subs.player = null;

            subs.CodecOpt = CodecOpt == null ? null : new(CodecOpt);

            return subs;
        }

        internal Player player;

        /// <summary>Primary subtitle delay shortcut step in ticks.</summary>
        public long             DelayOffset         { get; set; } =  100 * 10000;
        /// <summary>Secondary subtitle delay shortcut step in ticks.</summary>
        public long             DelayOffset2        { get; set; } = 1000 * 10000;

        /// <summary>Preferred subtitle languages in order. Defaults to the system language preferences.</summary>
        public List<Language>   Languages           { get { _Languages ??= GetSystemLanguages(); return _Languages; } set => _Languages = value; }
        List<Language> _Languages;

        /// <summary>Enables local subtitle discovery for the configured input types.</summary>
        public bool             SearchLocal         { get => _SearchLocal; set => Set(ref _SearchLocal, value); }
        bool _SearchLocal = false;

        /// <summary>Input types eligible for local subtitle discovery. An empty list allows all types.</summary>
        public List<InputType>  SearchLocalOnInputType
                                                    { get; set; } = [InputType.File, InputType.UNC, InputType.Torrent];

        /// <summary>Enables online subtitle discovery for the configured input types. Assignment can trigger discovery for the open video.</summary>
        public bool             SearchOnline        { get => _SearchOnline; set { Set(ref _SearchOnline, value); if (player != null && player.Video.isOpened) Task.Run(() => { if (player != null && player.Video.isOpened) player.decoder.SearchOnlineSubtitles(); }); } }
        bool _SearchOnline = false;

        /// <summary>Input types eligible for online subtitle discovery. An empty list allows all types.</summary>
        public List<InputType>  SearchOnlineOnInputType
                                                    { get; set; } = [InputType.File, InputType.UNC, InputType.Torrent];

        // Decoder

        /// <summary>Subtitle decoder frame queue limit.</summary>
        public int              MaxFrames           { get; set; } = 1;

        /// <summary>Maximum subtitle decoding errors before stopping.</summary>
        public int              MaxErrors           { get; set; } = 200;

        /// <summary>Forced FFmpeg subtitle decoder name. Null or empty selects automatically.</summary>
        public string Codec { get => _Codec; set => Set(ref _Codec, value); }
        internal string _Codec;

        /// <summary>FFmpeg option dictionary for opening the subtitle decoder.</summary>
        public Dictionary<string, string> CodecOpt  { get; set; } = [];

    }
    /// <summary>Saved data decoder limits. Current data stream enablement is runtime state.</summary>
    public class DataConfig : NotifyPropertyChanged
    {
        public DataConfig Clone() => new() { MaxFrames = MaxFrames, MaxErrors = MaxErrors };

        // Decoder

        /// <summary>Data decoder frame queue limit.</summary>
        public int              MaxFrames           { get; set; } = 100;

        /// <summary>Maximum data decoding errors before stopping.</summary>
        public int              MaxErrors           { get; set; } = 200;

    }

}

/// <summary>Process-wide settings used by <see cref="Engine"/> and saved separately from player configuration.</summary>
public class EngineConfig
{
    /// <summary>Skips audio engine initialization at startup and disables audio process-wide.</summary>
    public bool     DisableAudio            { get; set; }

    /// <summary>Directory containing architecture-matched FFmpeg libraries. A leading colon searches from the application directory through its ancestors.</summary>
    public string   FFmpegPath              { get; set; } = "FFmpeg";

    /// <summary>Selects the FFmpeg library set loaded at engine startup.</summary>
    public LoadProfile
                    FFmpegLoadProfile       { get; set; } = LoadProfile.All;

    /// <summary>Enables live HLS seeking through FFmpeg internals. It requires a compatible FFmpeg build.</summary>
    public bool     FFmpegHLSLiveSeek       { get; set; }

    /// <summary>FFmpeg diagnostic level. Assignment refreshes FFmpeg logging when the engine is running.</summary>
    public FF.LogLevel
                    FFmpegLogLevel          { get => _FFmpegLogLevel; set { _FFmpegLogLevel = value; if (Engine.IsLoaded) FFmpegEngine.SetLogLevel(); } }
    FF.LogLevel _FFmpegLogLevel = FF.LogLevel.Quiet;

    /// <summary>Whether <see cref="Load"/> created this instance. This metadata is excluded from JSON.</summary>
    [JsonIgnore]
    public bool     Loaded                  { get; private set; }

    /// <summary>File path recorded by <see cref="Load"/> and reused by <see cref="Save(string)"/> when no path is supplied.</summary>
    [JsonIgnore]
    public string   LoadedPath              { get; private set; }

    /// <summary>Log destination. Use a file path, :console, :debug, :custom, or an empty string to discard output. Assignment on the active engine configuration updates the sink.</summary>
    public string   LogOutput
    {
        get => _LogOutput;
        set
        {
            if (Engine.IsLoaded && ReferenceEquals(Engine.Config, this)) SetOutput(value);
            _LogOutput = value;
        }
    }
    string _LogOutput = "";

    /// <summary>Verbosity of Flyleaf logging.</summary>
    public LogLevel LogLevel                { get; set; } = LogLevel.Quiet;

    /// <summary>Appends to an existing log when opening a file sink. File rotation applies when append is disabled.</summary>
    public bool     LogAppend               { get; set; }

    /// <summary>Buffered line threshold for scheduling a file log flush.</summary>
    public int      LogCachedLines          { get; set; } = 20;

    /// <summary>Size threshold in bytes for rotating a file log when rotation is enabled.</summary>
    public long     LogRollMaxFileSize      { get; set; } = 10 * 1024 * 1024;

    /// <summary>Number of rotated log files to keep. Zero disables rotation.</summary>
    public long     LogRollMaxFiles         { get; set; } = 0;

    /// <summary>Date and time format used in log lines.</summary>
    public string   LogDateTimeFormat       { get; set; } = "HH.mm.ss.fff";

    /// <summary>Plugin directory loaded at startup. A leading colon searches from the application directory through its ancestors.</summary>
    public string   PluginsPath             { get; set; } = "Plugins";

    /// <summary>Enables engine polling for player activity, statistics and paused buffering updates.</summary>
    public bool     UIRefresh               { get => _UIRefresh; set { _UIRefresh = value; if (value && Engine.IsLoaded) Engine.StartThread(); } }
    bool _UIRefresh = true;

    /// <summary>Engine polling interval in milliseconds. Use a divisor of 1000 for accurate per-second statistics.</summary>
    public int      UIRefreshInterval       { get; set; } = 250;

    /// <summary>Requests that Windows keep the display powered on during playback.</summary>
    public bool     KeepDisplayActive       { get; set; } = true;

    /// <summary>Loads process-wide settings from a JSON file and records its path.</summary>
    /// <param name="path">Absolute or relative JSON file path.</param>
    public static EngineConfig Load(string path)
    {
        EngineConfig config = JsonSerializer.Deserialize<EngineConfig>(File.ReadAllText(path));
        config.Loaded       = true;
        config.LoadedPath   = path;

        return config;
    }

    /// <summary>Saves process-wide settings as JSON. An omitted path uses <see cref="LoadedPath"/> when available.</summary>
    /// <param name="path">Absolute or relative JSON file path, or null to reuse <see cref="LoadedPath"/>.</param>
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

