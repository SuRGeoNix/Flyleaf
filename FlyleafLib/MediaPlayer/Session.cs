using FlyleafLib.MediaFramework;
using FlyleafLib.Plugins;
using FlyleafLib.MediaStream;

namespace FlyleafLib.MediaPlayer
{
    /// <summary>
    /// Player's active Session (partially notifies, should re-request attributes on OpenCompleted - raise null property)
    /// </summary>
    public class Session : NotifyPropertyChanged
    {
        Player player;
        public Session(Player player ) { this.player = player; }
        internal void Reset(bool switching = false)
        {
            CanPlay = false;   

            if (!switching || (player.curVideoPlugin != null && ((IPluginVideo)player.curVideoPlugin).IsPlaylist))
            {
                LastSubtitleStream = null;
                LastAudioStream = null;
                SubsText = null; player.sFrame = null;
                SingleMovie = new Movie();
                _CurTime = 0;
            }
        }

        /// <summary>
        /// Holds the decocer's current Video stream
        /// </summary>
        public VideoStream      VideoInfo          => player.decoder?.VideoDecoder?.VideoStream;

        /// <summary>
        /// Holds the decoder's current Audio stream
        /// </summary>
        public AudioStream      AudioInfo          => player.decoder?.AudioDecoder?.AudioStream;

        /// <summary>
        /// Holds the decoder's current Subtitle stream
        /// </summary>
        public SubtitlesStream  SubsInfo            => player.decoder?.SubtitlesDecoder?.SubtitlesStream;

        /// <summary>
        /// Holds the current Video stream
        /// </summary>
        public VideoStream      CurVideoStream      { get; internal set; }

        /// <summary>
        /// Holds the current Audio stream
        /// </summary>
        public AudioStream      CurAudioStream      { get; internal set; }

        /// <summary>
        /// Holds the current Subtitle stream
        /// </summary>
        public SubtitlesStream   CurSubtitleStream   { get; internal set; }

        /// <summary>
        /// Holds the last enabled Audio stream in case of disabling
        /// </summary>
        public AudioStream      LastAudioStream     { get; internal set; }

        /// <summary>
        /// Holds the last enabled Subtitle stream in case of disabling
        /// </summary>
        public SubtitlesStream  LastSubtitleStream  { get; internal set; }

        /// <summary>
        /// Holds the initial user's video input
        /// </summary>
        public string           InitialUrl          { get; internal set; }

        /// <summary>
        /// Whether the input is Single Movie or Multiple (eg. in case of torrent with multiple video files)
        /// </summary>
        public bool             IsPlaylist          => player.curVideoPlugin != null && ((IPluginVideo)player.curVideoPlugin).IsPlaylist && CurVideoStream != null;

        /// <summary>
        /// Holds Movie's info
        /// </summary>
        public  Movie           Movie               { get => IsPlaylist ? CurVideoStream.Movie : SingleMovie; }
        public Movie          SingleMovie = new Movie();
      
        /// <summary>
        /// Whether the player's status is capable of accepting playback commands
        /// </summary>
        public bool             CanPlay             { get => _CanPlay;      set => Set(ref _CanPlay,   value); }
        bool        _CanPlay;

        /// <summary>
        /// Subtitles text auto set for the actual displaying duration
        /// </summary>
        public string           SubsText            { get => _SubsText;     set => Set(ref _SubsText,  value); }
        string      _SubsText;

        /// <summary>
        /// Player's current time or user's current seek time (if set from here seek's direction will be foreward)
        /// </summary>
        public long             CurTime             { get => _CurTime;      set { Set(ref _CurTime, value); player?.Seek((int) ((long)((object)value)/10000)); } }
        long        _CurTime;

        internal void SetCurTime(long curTime) { Set(ref _CurTime, curTime, false, nameof(CurTime)); }
    }
}