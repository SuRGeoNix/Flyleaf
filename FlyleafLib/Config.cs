using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

using SharpDX;

namespace FlyleafLib
{
    /// <summary>
    /// Player's main configuration
    /// </summary>
    public class Config : NotifyPropertyChanged
    {
        public static Config Load(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Config));
                return (Config) xmlSerializer.Deserialize(fs);
            }
        }
        public void Save(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(GetType());
                xmlSerializer.Serialize(fs, this);
            }
        }
        public Config() { }
        internal void SetPlayer(MediaPlayer.Player player)
        {
            decoder.player  = player;
            audio.player    = player;
            video.player    = player;
            subs.player     = player;
        }
        public Config Clone()
        {
            Config config   = new Config();
            config          = (Config) MemberwiseClone();

            config.audio    = audio.Clone();
            config.video    = video.Clone();
            config.subs     = subs.Clone();
            config.demuxer  = demuxer.Clone();
            config.decoder  = decoder.Clone();

            return config;
        }

        public Player   player  = new Player();
        public Demuxer  demuxer = new Demuxer();
        public Decoder  decoder = new Decoder();
        public Video    video   = new Video();
        public Audio    audio   = new Audio();
        public Subs     subs    = new Subs();

        public class Player : NotifyPropertyChanged
        {
            /// <summary>
            /// Sets playback mode to low latency (video only)
            /// </summary>
            public bool     LowLatency                  { get; set; } = false;

            /// <summary>
            /// Limit before dropping frames. Lower value means lower latency (>=1)
            /// </summary>
            public int      LowLatencyMaxVideoFrames    { get; set; } = 4;

            /// <summary>
            /// Limit before dropping frames. Lower value means lower latency (>=0)
            /// </summary>
            public int      LowLatencyMaxVideoPackets   { get; set; } = 2;
        }

        public class Demuxer
        {
            public Demuxer Clone()
            {
                Demuxer demuxer = (Demuxer) MemberwiseClone();

                demuxer.FormatOpt = new SerializableDictionary<string, string>();
                foreach (var kv in FormatOpt) demuxer.FormatOpt.Add(kv.Key, kv.Value);

                return demuxer;
            }

            /// <summary>
            /// Start/Seek/Pause/Stop will be faster but might not work properly with some protocols
            /// </summary>
            public bool             AllowInterrupts { get; set; } = true;

            /// <summary>
            /// List of FFmpeg formats to be excluded from interrupts (if AllowInterrupts = true)
            /// </summary>
            public List<string>     ExcludeInterruptFmts { get; set; } = new List<string>() { "rtsp" };

            /// <summary>
            /// Minimum required demuxed packets before playing
            /// </summary>
            public int              MinQueueSize    { get; set; } = 0;

            /// <summary>
            /// Maximum allowed packets for buffering
            /// </summary>
            public int              MaxQueueSize    { get; set; } = 100;

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
            public int              FormatFlags{ get; set; } = FFmpeg.AutoGen.ffmpeg.AVFMT_FLAG_DISCARD_CORRUPT;

            /// <summary>
            /// FFmpeg's format options for demuxer
            /// </summary>
            public SerializableDictionary<string, string>
                                    FormatOpt  { get; set; } = DefaultVideoFormatOpt();

            public static SerializableDictionary<string, string> DefaultVideoFormatOpt()
            {
                SerializableDictionary<string, string> defaults = new SerializableDictionary<string, string>();

                //defaults.Add("probesize",           (116 * (long)1024 * 1024).ToString());      // (Bytes) Default 5MB | Higher for weird formats (such as .ts?)
                //defaults.Add("analyzeduration",     (333 * (long)1000 * 1000).ToString());      // (Microseconds) Default 5 seconds | Higher for network streams
                //defaults.Add("live_start_index",   "-1");
                defaults.Add("reconnect",           "1");                                       // auto reconnect after disconnect before EOF
                defaults.Add("reconnect_streamed",  "1");                                       // auto reconnect streamed / non seekable streams
                defaults.Add("reconnect_delay_max", "5");                                       // max reconnect delay in seconds after which to give up
                defaults.Add("rtsp_transport",      "tcp");                                     // Seems UDP causing issues (use this by default?)

                //defaults.Add("timeout",           (2 * (long)1000 * 1000).ToString());      // (Bytes) Default 5MB | Higher for weird formats (such as .ts?)
                //defaults.Add("rw_timeout",     (2 * (long)1000 * 1000).ToString());      // (Microseconds) Default 5 seconds | Higher for network streams
                return defaults;
            }
        }


        public class Decoder : NotifyPropertyChanged
        {
            internal MediaPlayer.Player player;

            public Decoder Clone() { return (Decoder) MemberwiseClone(); }

            /// <summary>
            /// Activates Direct3D video acceleration (decoding)
            /// </summary>
            public bool             HWAcceleration  { get; set; } = true;

            /// <summary>
            /// Threads that will be used from the decoder
            /// </summary>
            public int              VideoThreads    { get; set; } = Environment.ProcessorCount;

            /// <summary>
            /// Minimum required video frames before playing
            /// </summary>
            public int              MinVideoFrames  { get; set; } = 10;

            /// <summary>
            /// Minimum required audio frames before playing
            /// </summary>
            public int              MinAudioFrames  { get; set; } = 6;

            /// <summary>
            /// Maximum video frames to be decoded and processed for rendering
            /// </summary>
            public int              MaxVideoFrames  { get; set; } = Math.Max(10, Environment.ProcessorCount);

            /// <summary>
            /// Maximum audio frames to be decoded and processed for playback
            /// </summary>
            public int              MaxAudioFrames  { get; set; } = 30;

            /// <summary>
            /// Maximum subtitle frames to be decoded
            /// </summary>
            public int              MaxSubsFrames   { get; set; } = 10;

            /// <summary>
            /// Maximum allowed errors before stopping
            /// </summary>
            public int              MaxErrors       { get; set; } = 200;
        }

        public class Video : NotifyPropertyChanged
        {
            internal MediaPlayer.Player player;

            public Video Clone() { return (Video) MemberwiseClone(); }

            /// <summary>
            /// Custom aspect ratio (AspectRatio must be set to Custom to have an effect)
            /// </summary>
            public AspectRatio      CustomAspectRatio     { get => _CustomAspectRatio;  set { bool changed = _CustomAspectRatio != value; Set(ref _CustomAspectRatio, value); if (changed) AspectRatio = AspectRatio.Custom; } }
            AspectRatio    _CustomAspectRatio = new AspectRatio(16, 9);

            /// <summary>
            /// Video aspect ratio
            /// </summary>
            public AspectRatio      AspectRatio     { get => _AspectRatio;  set { Set(ref _AspectRatio, value); player?.renderer?.SetViewport(); } }
            AspectRatio    _AspectRatio = AspectRatio.Keep;
            
            /// <summary>
            /// Backcolor of the player's surface
            /// </summary>
            public System.Windows.Media.Color
                                    ClearColor      { get => Utils.SharpDXToWpfColor(_ClearColor);  set { Set(ref _ClearColor, Utils.WpfToSharpDXColor(value)); player?.renderer?.PresentFrame(); } }
            internal Color _ClearColor = Color.Black;

            public bool             SwsHighQuality  { get; set; } = false;
            public short            VSync           { get; set; }
        }

        public class Audio : NotifyPropertyChanged
        {
            internal MediaPlayer.Player player;

            public Audio Clone() { return (Audio) MemberwiseClone(); }

            /// <summary>
            /// Whether audio should be enabled or not
            /// </summary>
            public bool             Enabled         { get => _Enabled;      set { bool changed = _Enabled != value;  Set(ref _Enabled, value); if (changed) if (value) player?.EnableAudio(); else player?.DisableAudio(); } }
            bool    _Enabled = true;

            internal void SetEnabled(bool enabled = true) { _Enabled = enabled; Raise(nameof(Enabled)); }

            /// <summary>
            /// NAudio's latency (required buffered duration before playing)
            /// </summary>
            public long             LatencyTicks    { get; set; } = 70 * 10000;

            /// <summary>
            /// Audio languages preference by priority
            /// </summary>
            public List<Language>   Languages       { get { if (_Languages == null) _Languages = Utils.GetSystemLanguages();  return _Languages; } set { _Languages = value;} }
            List<Language> _Languages;

            /// <summary>
            /// Audio delay ticks (will be parsed in current active stream and will be resetted on each new input)
            /// </summary>
            public long             DelayTicks      { get { if (player != null && player.Session.CurAudioStream != null) return player.Session.CurAudioStream.Delay; else return _DelayTicks; }   set { if (player != null && player.Session.CurAudioStream != null) player.Session.CurAudioStream.Delay = value; Set(ref _DelayTicks, value); player?.SetAudioDelay(); } }
            long    _DelayTicks = 0;
        }

        public class Subs : NotifyPropertyChanged
        {
            internal MediaPlayer.Player player;

            public Subs Clone()
            {
                Subs subs = new Subs();
                subs = (Subs) MemberwiseClone();

                subs.Languages = new List<Language>();
                if (Languages != null) foreach(var lang in Languages) subs.Languages.Add(lang);

                return subs;
            }

            /// <summary>
            /// Subtitle languages preference by priority
            /// </summary>
            public List<Language>   Languages           { get { if (_Languages == null) _Languages = Utils.GetSystemLanguages();  return _Languages; } set { _Languages = value;} }
            List<Language> _Languages;

            /// <summary>
            /// Whether subtitles should be enabled or not
            /// </summary>
            public bool             Enabled             { get => _Enabled;      set { bool changed = _Enabled != value; Set(ref _Enabled, value); if (changed) if (value) player?.EnableSubs(); else player?.DisableSubs(); } }
            bool    _Enabled = true;

            /// <summary>
            /// Whether to use online search plugins or not
            /// </summary>
            public bool             UseOnlineDatabases  { get => _UseOnlineDatabases; set { Set(ref _UseOnlineDatabases, value); } }
            bool    _UseOnlineDatabases = false;

            internal void SetEnabled(bool enabled = true) { _Enabled = enabled; Raise(nameof(Enabled)); }
            
            /// <summary>
            /// Subtitle delay ticks (will be parsed in current active stream and will be resetted on each new input)
            /// </summary>
            public long             DelayTicks          { get { if (player != null && player.Session.CurSubtitleStream != null) return player.Session.CurSubtitleStream.Delay; else return _DelayTicks; }   set { if (player != null && player.Session.CurSubtitleStream != null) player.Session.CurSubtitleStream.Delay = value; Set(ref _DelayTicks, value); player?.SetSubsDelay(); } }
            long    _DelayTicks = 0;
        }
    }
}