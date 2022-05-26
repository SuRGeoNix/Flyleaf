using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaRemuxer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

using static FlyleafLib.Logger;
using static FlyleafLib.Utils;

namespace FlyleafLib.MediaFramework.MediaContext
{
    public unsafe class DecoderContext : PluginHandler
    {
        /* TODO
         * 
         * 1) Lock delay on demuxers' Format Context (for network streams)
         *      Ensure we interrupt if we are planning to seek
         *      Merge Seek witih GetVideoFrame (+SeekAccurate)
         *      Long delay on Enable/Disable demuxer's streams (lock might not required)
         * 
         * 2) Resync implementation / CurTime
         *      Transfer player's resync implementation here
         *      Ensure we can trust CurTime on lower level (eg. on decoders - demuxers using dts)
         * 
         * 3) Timestamps / Memory leak
         *      If we have embedded audio/video and the audio decoder will stop/fail for some reason the demuxer will keep filling audio packets
         *      Should also check at lower level (demuxer) to prevent wrong packet timestamps (too early or too late)
         *      This is normal if it happens on live HLS (probably an ffmpeg bug)
         */

        #region Properties
        public bool                 EnableDecoding      { get ; set; }
        public new bool             Interrupt
        { 
            get => base.Interrupt;
            set
            {
                base.Interrupt = value;

                if (value)
                {
                    VideoDemuxer.Interrupter.ForceInterrupt = 1;
                    AudioDemuxer.Interrupter.ForceInterrupt = 1;
                    SubtitlesDemuxer.Interrupter.ForceInterrupt = 1;
                }
                else
                {
                    VideoDemuxer.Interrupter.ForceInterrupt = 0;
                    AudioDemuxer.Interrupter.ForceInterrupt = 0;
                    SubtitlesDemuxer.Interrupter.ForceInterrupt = 0;
                }
            }
        }

        /// <summary>
        /// It will not resync by itself. Requires manual call to ReSync()
        /// </summary>
        public bool                 RequiresResync      { get; set; }

        public string               Extension           => VideoDemuxer.Disposed ? AudioDemuxer.Extension : VideoDemuxer.Extension;

        // Demuxers
        public Demuxer              MainDemuxer         { get; private set; }
        public Demuxer              AudioDemuxer        { get; private set; }
        public Demuxer              VideoDemuxer        { get; private set; }
        public Demuxer              SubtitlesDemuxer    { get; private set; }
        public Demuxer      GetDemuxerPtr(MediaType type)   { return type == MediaType.Audio ? AudioDemuxer : (type == MediaType.Video ? VideoDemuxer : SubtitlesDemuxer); }

        // Decoders
        public AudioDecoder         AudioDecoder        { get; private set; }
        public VideoDecoder         VideoDecoder        { get; internal set;}
        public SubtitlesDecoder     SubtitlesDecoder    { get; private set; }
        public DecoderBase  GetDecoderPtr(MediaType type)   { return type == MediaType.Audio ? (DecoderBase)AudioDecoder : (type == MediaType.Video ?  (DecoderBase)VideoDecoder : (DecoderBase)SubtitlesDecoder); }

        // Streams
        public AudioStream          AudioStream         => VideoDemuxer?.AudioStream != null ? VideoDemuxer?.AudioStream : AudioDemuxer.AudioStream;
        public VideoStream          VideoStream         => VideoDemuxer?.VideoStream;
        public SubtitlesStream      SubtitlesStream     => VideoDemuxer?.SubtitlesStream != null ? VideoDemuxer?.SubtitlesStream : SubtitlesDemuxer.SubtitlesStream;

        public Tuple<ExternalAudioStream, int>      ClosedAudioStream       { get; private set; }
        public Tuple<ExternalVideoStream, int>      ClosedVideoStream       { get; private set; }
        public Tuple<ExternalSubtitlesStream, int>  ClosedSubtitlesStream   { get; private set; }
        #endregion

        #region Initialize
        LogHandler Log;
        public DecoderContext(Config config = null, int uniqueId = -1, bool enableDecoding = true) : base(config, uniqueId)
        {
            Log = new LogHandler($"[#{UniqueId}] [DecoderContext] ");
            Playlist.decoder    = this;

            EnableDecoding      = enableDecoding;

            AudioDemuxer        = new Demuxer(Config.Demuxer, MediaType.Audio, UniqueId, EnableDecoding);
            VideoDemuxer        = new Demuxer(Config.Demuxer, MediaType.Video, UniqueId, EnableDecoding);
            SubtitlesDemuxer    = new Demuxer(Config.Demuxer, MediaType.Subs,  UniqueId, EnableDecoding);

            Recorder            = new Remuxer(UniqueId);

            VideoDecoder        = new VideoDecoder(Config, UniqueId);
            AudioDecoder        = new AudioDecoder(Config, UniqueId, VideoDecoder);
            SubtitlesDecoder    = new SubtitlesDecoder(Config, UniqueId);

            if (EnableDecoding && config.Player.Usage != MediaPlayer.Usage.Audio)
                VideoDecoder.CreateRenderer();

            VideoDecoder.recCompleted = RecordCompleted;
            AudioDecoder.recCompleted = RecordCompleted;
        }

        public void Initialize()
        {
            RequiresResync = false;

            OnInitializing();
            Stop();
            OnInitialized();
        }
        public void InitializeSwitch()
        {
            RequiresResync = false;
            ClosedAudioStream = null;
            ClosedVideoStream = null;
            ClosedSubtitlesStream = null;

            OnInitializingSwitch();
            Stop();
            OnInitializedSwitch();
        }
        #endregion

        #region Events
        public event EventHandler<OpenCompletedArgs>                        OpenCompleted;
        public event EventHandler<OpenSessionCompletedArgs>                 OpenSessionCompleted;
        public event EventHandler<OpenSubtitlesCompletedArgs>               OpenSubtitlesCompleted;
        public event EventHandler<OpenPlaylistItemCompletedArgs>            OpenPlaylistItemCompleted;

        public event EventHandler<OpenAudioStreamCompletedArgs>             OpenAudioStreamCompleted;
        public event EventHandler<OpenVideoStreamCompletedArgs>             OpenVideoStreamCompleted;
        public event EventHandler<OpenSubtitlesStreamCompletedArgs>         OpenSubtitlesStreamCompleted;

        public event EventHandler<OpenExternalAudioStreamCompletedArgs>     OpenExternalAudioStreamCompleted;
        public event EventHandler<OpenExternalVideoStreamCompletedArgs>     OpenExternalVideoStreamCompleted;
        public event EventHandler<OpenExternalSubtitlesStreamCompletedArgs> OpenExternalSubtitlesStreamCompleted;

        public class OpenCompletedArgs
        {
            public string       Url;
            public Stream       IOStream;
            public string       Error;
            public bool         Success => Error == null;
            public OpenCompletedArgs(string url = null, Stream iostream = null, string error = null) { Url = url; IOStream = iostream; Error = error; }
        }
        public class OpenSubtitlesCompletedArgs
        {
            public string       Url;
            public string       Error;
            public bool         Success => Error == null;
            public OpenSubtitlesCompletedArgs(string url = null, string error = null) { Url = url; Error = error; }
        }
        public class OpenSessionCompletedArgs
        {
            public Session      Session;
            public string       Error;
            public bool         Success => Error == null;
            public OpenSessionCompletedArgs(Session session = null, string error = null) { Session = session; Error = error; }
        }
        public class OpenPlaylistItemCompletedArgs
        {
            public PlaylistItem Item;
            public PlaylistItem OldItem;
            public string       Error;
            public bool         Success => Error == null;
            public OpenPlaylistItemCompletedArgs(PlaylistItem item = null, PlaylistItem oldItem = null, string error = null) {  Item = item; OldItem = oldItem; Error = error; }
        }
        public class StreamOpenedArgs
        {
            public StreamBase   Stream;
            public StreamBase   OldStream;
            public string       Error;
            public bool         Success => Error == null;
            public StreamOpenedArgs(StreamBase stream = null, StreamBase oldStream = null, string error = null) { Stream = stream; OldStream= oldStream; Error = error; }
        }
        public class OpenAudioStreamCompletedArgs : StreamOpenedArgs 
        {
            public new AudioStream Stream   => (AudioStream)base.Stream;
            public new AudioStream OldStream=> (AudioStream)base.OldStream;
            public OpenAudioStreamCompletedArgs(AudioStream stream = null, AudioStream oldStream = null, string error = null): base(stream, oldStream, error) { }
        }
        public class OpenVideoStreamCompletedArgs : StreamOpenedArgs
        {
            public new VideoStream Stream   => (VideoStream)base.Stream;
            public new VideoStream OldStream=> (VideoStream)base.OldStream;
            public OpenVideoStreamCompletedArgs(VideoStream stream = null, VideoStream oldStream = null, string error = null): base(stream, oldStream, error) { }
        }
        public class OpenSubtitlesStreamCompletedArgs : StreamOpenedArgs
        {
            public new SubtitlesStream Stream   => (SubtitlesStream)base.Stream;
            public new SubtitlesStream OldStream=> (SubtitlesStream)base.OldStream;
            public OpenSubtitlesStreamCompletedArgs(SubtitlesStream stream = null, SubtitlesStream oldStream = null, string error = null): base(stream, oldStream, error) { }
        }
        public class ExternalStreamOpenedArgs : EventArgs
        {
            public ExternalStream   ExtStream;
            public ExternalStream   OldExtStream;
            public string           Error;
            public bool             Success => Error == null;
            public ExternalStreamOpenedArgs(ExternalStream extStream = null, ExternalStream oldExtStream = null, string error = null) { ExtStream = extStream; OldExtStream= oldExtStream; Error = error; }
        } 
        public class OpenExternalAudioStreamCompletedArgs : ExternalStreamOpenedArgs
        {
            public new ExternalAudioStream ExtStream   => (ExternalAudioStream)base.ExtStream;
            public new ExternalAudioStream OldExtStream=> (ExternalAudioStream)base.OldExtStream;
            public OpenExternalAudioStreamCompletedArgs(ExternalAudioStream extStream = null, ExternalAudioStream oldExtStream = null, string error = null) : base(extStream, oldExtStream, error) { } 
        }
        public class OpenExternalVideoStreamCompletedArgs : ExternalStreamOpenedArgs
        {
            public new ExternalVideoStream ExtStream   => (ExternalVideoStream)base.ExtStream;
            public new ExternalVideoStream OldExtStream=> (ExternalVideoStream)base.OldExtStream;
            public OpenExternalVideoStreamCompletedArgs(ExternalVideoStream extStream = null, ExternalVideoStream oldExtStream = null, string error = null) : base(extStream, oldExtStream, error) { }
        }
        public class OpenExternalSubtitlesStreamCompletedArgs : ExternalStreamOpenedArgs
        {
            public new ExternalSubtitlesStream ExtStream   => (ExternalSubtitlesStream)base.ExtStream;
            public new ExternalSubtitlesStream OldExtStream=> (ExternalSubtitlesStream)base.OldExtStream;
            public OpenExternalSubtitlesStreamCompletedArgs(ExternalSubtitlesStream extStream = null, ExternalSubtitlesStream oldExtStream = null, string error = null) : base(extStream, oldExtStream, error) { }
        }

        private void OnOpenCompleted(OpenCompletedArgs args = null)
        {
            if (CanInfo) Log.Info($"[Open] {(args.Url != null ? args.Url : "None")} {(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenCompleted?.Invoke(this, args);
        }
        private void OnOpenSessionCompleted(OpenSessionCompletedArgs args = null)
        {
            if (CanInfo) Log.Info($"[OpenSession] {(args.Session.Url != null ? args.Session.Url : "None")} - Item: {args.Session.PlaylistItem} {(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenSessionCompleted?.Invoke(this, args);
        }
        private void OnOpenSubtitles(OpenSubtitlesCompletedArgs args = null)
        {
            if (CanInfo) Log.Info($"[OpenSubtitles] {(args.Url != null ? args.Url : "None")} {(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenSubtitlesCompleted?.Invoke(this, args);
        }
        private void OnOpenPlaylistItemCompleted(OpenPlaylistItemCompletedArgs args = null)
        {
            if (CanInfo) Log.Info($"[OpenPlaylistItem] {(args.OldItem != null ? args.OldItem.Title : "None")} => {(args.Item != null ? args.Item.Title : "None")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenPlaylistItemCompleted?.Invoke(this, args);
        }
        private void OnOpenAudioStreamCompleted(OpenAudioStreamCompletedArgs args = null)
        {
            ClosedAudioStream = null;
            MainDemuxer = !VideoDemuxer.Disposed ? VideoDemuxer : AudioDemuxer;

            if (CanInfo) Log.Info($"[OpenAudioStream] #{(args.OldStream != null ? args.OldStream.StreamIndex.ToString() : "_")} => #{(args.Stream != null ? args.Stream.StreamIndex.ToString() : "_")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenAudioStreamCompleted?.Invoke(this, args);
        }
        private void OnOpenVideoStreamCompleted(OpenVideoStreamCompletedArgs args = null)
        {
            ClosedVideoStream = null;
            MainDemuxer = !VideoDemuxer.Disposed ? VideoDemuxer : AudioDemuxer;

            if (CanInfo) Log.Info($"[OpenVideoStream] #{(args.OldStream != null ? args.OldStream.StreamIndex.ToString() : "_")} => #{(args.Stream != null ? args.Stream.StreamIndex.ToString() : "_")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenVideoStreamCompleted?.Invoke(this, args);
        }
        private void OnOpenSubtitlesStreamCompleted(OpenSubtitlesStreamCompletedArgs args = null)
        {
            ClosedSubtitlesStream = null;

            if (CanInfo) Log.Info($"[OpenSubtitlesStream] #{(args.OldStream != null ? args.OldStream.StreamIndex.ToString() : "_")} => #{(args.Stream != null ? args.Stream.StreamIndex.ToString() : "_")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenSubtitlesStreamCompleted?.Invoke(this, args);
        }
        private void OnOpenExternalAudioStreamCompleted(OpenExternalAudioStreamCompletedArgs args = null)
        {
            ClosedAudioStream = null;
            MainDemuxer = !VideoDemuxer.Disposed ? VideoDemuxer : AudioDemuxer;

            if (CanInfo) Log.Info($"[OpenExternalAudioStream] {(args.OldExtStream != null ? args.OldExtStream.Url : "None")} => {(args.ExtStream != null ? args.ExtStream.Url : "None")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenExternalAudioStreamCompleted?.Invoke(this, args);
        }
        private void OnOpenExternalVideoStreamCompleted(OpenExternalVideoStreamCompletedArgs args = null)
        {
            ClosedVideoStream = null;
            MainDemuxer = !VideoDemuxer.Disposed ? VideoDemuxer : AudioDemuxer;

            if (CanInfo) Log.Info($"[OpenExternalVideoStream] {(args.OldExtStream != null ? args.OldExtStream.Url : "None")} => {(args.ExtStream != null ? args.ExtStream.Url : "None")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenExternalVideoStreamCompleted?.Invoke(this, args);
        }
        private void OnOpenExternalSubtitlesStreamCompleted(OpenExternalSubtitlesStreamCompletedArgs args = null)
        {
            ClosedSubtitlesStream = null;

            if (CanInfo) Log.Info($"[OpenExternalSubtitlesStream] {(args.OldExtStream != null ? args.OldExtStream.Url : "None")} => {(args.ExtStream != null ? args.ExtStream.Url : "None")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            OpenExternalSubtitlesStreamCompleted?.Invoke(this, args);
        }
        #endregion

        #region Open
        public OpenCompletedArgs Open(string url, bool defaultPlaylistItem = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            return Open((object)url, defaultPlaylistItem, defaultVideo, defaultAudio, defaultSubtitles);
        }
        public OpenCompletedArgs Open(Stream iostream, bool defaultPlaylistItem = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            return Open((object)iostream, defaultPlaylistItem, defaultVideo, defaultAudio, defaultSubtitles);
        }

        internal OpenCompletedArgs Open(object input, bool defaultPlaylistItem = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            OpenCompletedArgs args = new OpenCompletedArgs();

            try
            {
                Initialize();

                if (input is Stream)
                {
                    Playlist.IOStream = (Stream)input;
                }
                else
                    Playlist.Url = input.ToString(); // TBR: check UI update

                args.Url = Playlist.Url;
                args.IOStream = Playlist.IOStream;
                args.Error = Open().Error;

                if (Playlist.Items.Count == 0 && args.Success)
                    args.Error = "No playlist items were found";

                if (!args.Success)
                    return args;

                if (!defaultPlaylistItem)
                    return args;

                args.Error = Open(SuggestItem(), defaultVideo, defaultAudio, defaultSubtitles).Error;

                return args;

            } catch (Exception e)
            {
                args.Error = !args.Success ? args.Error + "\r\n" + e.Message : e.Message;
                return args;
            }
            finally
            {
                OnOpenCompleted(args);
            }
        }
        public new OpenSubtitlesCompletedArgs OpenSubtitles(string url)
        {
            OpenSubtitlesCompletedArgs args = new OpenSubtitlesCompletedArgs();

            try
            {
                OpenSubtitlesResults res = base.OpenSubtitles(url);
                args.Error = res == null ? "No external subtitles stream found" : res.Error;

                if (args.Success)
                    args.Error = Open(res.ExternalSubtitlesStream).Error;

                return args;

            } catch (Exception e)
            {
                args.Error = !args.Success ? args.Error + "\r\n" + e.Message : e.Message;
                return args;
            }
            finally
            {
                OnOpenSubtitles(args);
            }
        }
        public OpenSessionCompletedArgs Open(Session session)
        {
            OpenSessionCompletedArgs args = new OpenSessionCompletedArgs(session);

            try
            {
                // Open
                if (session.Url != null && session.Url != Playlist.Url) // && session.Url != Playlist.DirectUrl)
                {
                    args.Error = Open(session.Url, false, false, false, false).Error;
                    if (!args.Success)
                        return args;
                }

                // Open Item
                if (session.PlaylistItem != -1)
                {
                    args.Error = Open(Playlist.Items[session.PlaylistItem], false, false, false).Error;
                    if (!args.Success)
                        return args;
                }

                // Open Streams
                if (session.ExternalVideoStream != -1)
                {
                    args.Error = Open(Playlist.Selected.ExternalVideoStreams[session.ExternalVideoStream], false, session.VideoStream).Error;
                    if (!args.Success)
                        return args;
                }
                else if (session.VideoStream != -1)
                {
                    args.Error = Open(VideoDemuxer.AVStreamToStream[session.VideoStream], false).Error;
                    if (!args.Success)
                        return args;
                }

                string tmpErr = null;
                if (session.ExternalAudioStream != -1)
                    tmpErr = Open(Playlist.Selected.ExternalAudioStreams[session.ExternalAudioStream], false, session.AudioStream).Error;
                else if (session.AudioStream != -1)
                    tmpErr = Open(VideoDemuxer.AVStreamToStream[session.AudioStream], false).Error;

                if (tmpErr != null & VideoStream == null)
                {
                    args.Error = tmpErr;
                    return args;
                }

                if (session.ExternalSubtitlesUrl != null)
                    OpenSubtitles(session.ExternalSubtitlesUrl);
                else if (session.SubtitlesStream != -1)
                    Open(VideoDemuxer.AVStreamToStream[session.SubtitlesStream]);

                Config.Audio.SetDelay(session.AudioDelay);
                Config.Subtitles.SetDelay(session.SubtitlesDelay);

                if (session.CurTime > 1 * (long)1000 * 10000)
                    Seek(session.CurTime / 10000);

                return args;
            } catch (Exception e)
            {
                args.Error = !args.Success ? args.Error + "\r\n" + e.Message : e.Message;
                return args;
            } finally
            {
                OnOpenSessionCompleted(args);
            }
        }
        public OpenPlaylistItemCompletedArgs Open(PlaylistItem item, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            OpenPlaylistItemCompletedArgs args = new OpenPlaylistItemCompletedArgs(item);
            
            try
            {
                long stoppedTime = GetCurTime();
                InitializeSwitch();

                // Disables old item
                if (Playlist.Selected != null)
                {
                    args.OldItem = Playlist.Selected;
                    Playlist.Selected.Enabled = false;
                }

                Playlist.Selected = item;
                Playlist.Selected.Enabled = true;

                // We reset external streams of the current item and not the old one
                if (Playlist.Selected.ExternalAudioStream != null)
                {
                    Playlist.Selected.ExternalAudioStream.Enabled = false;
                    Playlist.Selected.ExternalAudioStream = null;
                }

                if (Playlist.Selected.ExternalVideoStream != null)
                {
                    Playlist.Selected.ExternalVideoStream.Enabled = false;
                    Playlist.Selected.ExternalVideoStream = null;
                }

                if (Playlist.Selected.ExternalSubtitlesStream != null)
                {
                    Playlist.Selected.ExternalSubtitlesStream.Enabled = false;
                    Playlist.Selected.ExternalSubtitlesStream = null;
                }

                args.Error = OpenItem().Error;

                if (!args.Success)
                    return args;

                if (Playlist.Selected.Url != null || Playlist.Selected.IOStream != null)
                    args.Error = OpenDemuxerInput(VideoDemuxer, Playlist.Selected);

                if (!args.Success)
                    return args;

                if (defaultVideo && Config.Video.Enabled)
                    OpenSuggestedVideo(defaultAudio);
                else if (defaultAudio && Config.Audio.Enabled)
                    OpenSuggestedAudio();

                if ((defaultVideo || defaultAudio) && AudioStream == null && VideoStream == null)
                {
                    args.Error = "No audio/video found";
                    return args;
                }

                if (defaultSubtitles && Config.Subtitles.Enabled)
                {
                    if (Playlist.Selected.ExternalSubtitlesStream != null)
                        Open(Playlist.Selected.ExternalSubtitlesStream);
                    else
                        OpenSuggestedSubtitles();
                }

                return args;
            } catch (Exception e)
            {
                args.Error = !args.Success ? args.Error + "\r\n" + e.Message : e.Message;
                return args;
            } finally
            {
                OnOpenPlaylistItemCompleted(args);
            }
        }
        public ExternalStreamOpenedArgs Open(ExternalStream extStream, bool defaultAudio = false, int streamIndex = -1) // -2: None, -1: Suggest, >=0: specified
        {
            ExternalStreamOpenedArgs args = null;

            try
            {
                Demuxer demuxer;

                if (extStream is ExternalVideoStream)
                {
                    args = new OpenExternalVideoStreamCompletedArgs((ExternalVideoStream) extStream, Playlist.Selected.ExternalVideoStream);

                    if (args.OldExtStream != null)
                        args.OldExtStream.Enabled = false;

                    Playlist.Selected.ExternalVideoStream = (ExternalVideoStream) extStream;

                    foreach(var plugin in Plugins.Values)
                        plugin.OnOpenExternalVideo();

                    demuxer = VideoDemuxer;
                }
                else if (extStream is ExternalAudioStream)
                {
                    args = new OpenExternalAudioStreamCompletedArgs((ExternalAudioStream) extStream, Playlist.Selected.ExternalAudioStream);

                    if (args.OldExtStream != null)
                        args.OldExtStream.Enabled = false;

                    Playlist.Selected.ExternalAudioStream = (ExternalAudioStream) extStream;

                    foreach(var plugin in Plugins.Values)
                        plugin.OnOpenExternalAudio();

                    demuxer = AudioDemuxer;
                }
                else
                {
                    args = new OpenExternalSubtitlesStreamCompletedArgs((ExternalSubtitlesStream) extStream, Playlist.Selected.ExternalSubtitlesStream);

                    if (args.OldExtStream != null)
                        args.OldExtStream.Enabled = false;

                    Playlist.Selected.ExternalSubtitlesStream = (ExternalSubtitlesStream) extStream;

                    if (!Playlist.Selected.ExternalSubtitlesStream.Downloaded)
                        DownloadSubtitles(Playlist.Selected.ExternalSubtitlesStream);
                        
                    foreach(var plugin in Plugins.Values)
                        plugin.OnOpenExternalSubtitles();

                    demuxer = SubtitlesDemuxer;
                }

                // Open external stream
                args.Error = OpenDemuxerInput(demuxer, extStream);

                if (!args.Success)
                    return args;

                // Update embedded streams with the external stream pointer
                foreach (var embStream in demuxer.VideoStreams)
                    embStream.ExternalStream = extStream;
                foreach (var embStream in demuxer.AudioStreams)
                    embStream.ExternalStream = extStream;
                foreach (var embStream in demuxer.SubtitlesStreams)
                    embStream.ExternalStream = extStream;

                // Open embedded stream
                if (streamIndex != -2)
                {
                    StreamBase suggestedStream = null;
                    if (streamIndex != -1 && (streamIndex >= demuxer.AVStreamToStream.Count || streamIndex < 0 || demuxer.AVStreamToStream[streamIndex].Type != extStream.Type))
                    {
                        args.Error = $"Invalid stream index {streamIndex}";
                        demuxer.Dispose();
                        return args;
                    }

                    if (demuxer.Type == MediaType.Video)
                        suggestedStream = streamIndex == -1 ? SuggestVideo(demuxer.VideoStreams) : demuxer.AVStreamToStream[streamIndex];
                    else if (demuxer.Type == MediaType.Audio)
                        suggestedStream = streamIndex == -1 ? SuggestAudio(demuxer.AudioStreams) : demuxer.AVStreamToStream[streamIndex];
                    else
                    {
                        var langs = Config.Subtitles.Languages.ToList();
                        langs.Add(Language.Get("und"));
                        suggestedStream = streamIndex == -1 ? SuggestSubtitles(demuxer.SubtitlesStreams, langs) : demuxer.AVStreamToStream[streamIndex];
                    }

                    if (suggestedStream == null)
                    {
                        demuxer.Dispose();
                        args.Error = "No embedded streams found";
                        return args;
                    }

                    args.Error = Open(suggestedStream, defaultAudio).Error;
                    if (!args.Success)
                        return args;
                }

                extStream.Enabled = true;

                return args;
            } catch (Exception e)
            {
                args.Error = !args.Success ? args.Error + "\r\n" + e.Message : e.Message;
                return args;
            } finally
            {
                if (extStream is ExternalVideoStream)
                    OnOpenExternalVideoStreamCompleted((OpenExternalVideoStreamCompletedArgs)args);
                else if (extStream is ExternalAudioStream)
                    OnOpenExternalAudioStreamCompleted((OpenExternalAudioStreamCompletedArgs)args);
                else
                    OnOpenExternalSubtitlesStreamCompleted((OpenExternalSubtitlesStreamCompletedArgs)args);
            }
        }

        public StreamOpenedArgs OpenVideoStream(VideoStream stream, bool defaultAudio = true)
        {
            return Open(stream, defaultAudio);
        }
        public StreamOpenedArgs OpenAudioStream(AudioStream stream)
        {
            return Open(stream);
        }
        public StreamOpenedArgs OpenSubtitlesStream(SubtitlesStream stream)
        {
            return Open(stream);
        }
        private StreamOpenedArgs Open(StreamBase stream, bool defaultAudio = false)
        {
            StreamOpenedArgs args = null;

            try
            {
                lock (stream.Demuxer.lockFmtCtx)
                {
                    StreamBase oldStream = stream.Type == MediaType.Video ? (StreamBase)VideoStream : (stream.Type == MediaType.Audio ? (StreamBase)AudioStream : (StreamBase)SubtitlesStream);

                    // Close external demuxers when opening embedded
                    if (stream.Demuxer.Type == MediaType.Video)
                    {
                        // TBR: if (stream.Type == MediaType.Video) | We consider that we can't have Embedded and External Video Streams at the same time
                        if (stream.Type == MediaType.Audio) // TBR: && VideoStream != null)
                        {
                            if (!EnableDecoding) AudioDemuxer.Dispose();
                            if (Playlist.Selected.ExternalAudioStream != null)
                            {
                                Playlist.Selected.ExternalAudioStream.Enabled = false;
                                Playlist.Selected.ExternalAudioStream = null;
                            }
                        }
                        else if (stream.Type == MediaType.Subs)
                        {
                            if (!EnableDecoding) SubtitlesDemuxer.Dispose();
                            if (Playlist.Selected.ExternalSubtitlesStream != null)
                            {
                                Playlist.Selected.ExternalSubtitlesStream.Enabled = false;
                                Playlist.Selected.ExternalSubtitlesStream = null;
                            }
                        }
                    }
                    else if (!EnableDecoding)
                    {
                        // Disable embeded audio when enabling external audio (TBR)
                        if (stream.Demuxer.Type == MediaType.Audio && stream.Type == MediaType.Audio && AudioStream != null && AudioStream.Demuxer.Type == MediaType.Video)
                        {
                            foreach (var aStream in VideoDemuxer.AudioStreams)
                                VideoDemuxer.DisableStream(aStream);
                        }
                    }

                    // Open Codec / Enable on demuxer
                    if (EnableDecoding)
                    {
                        string ret = GetDecoderPtr(stream.Type).Open(stream);

                        if (ret != null)
                        {
                            if (stream.Type == MediaType.Video)
                                return args = new OpenVideoStreamCompletedArgs((VideoStream)stream, (VideoStream)oldStream, $"Failed to open video stream #{stream.StreamIndex}\r\n{ret}");
                            else if (stream.Type == MediaType.Audio)
                                return args = new OpenAudioStreamCompletedArgs((AudioStream)stream, (AudioStream)oldStream, $"Failed to open audio stream #{stream.StreamIndex}\r\n{ret}");
                            else
                                return args = new OpenSubtitlesStreamCompletedArgs((SubtitlesStream)stream, (SubtitlesStream)oldStream, $"Failed to open subtitles stream #{stream.StreamIndex}\r\n{ret}");
                        }
                    }
                    else
                        stream.Demuxer.EnableStream(stream);

                    // Open Audio based on new Video Stream (if not the same suggestion)
                    if (defaultAudio && stream.Type == MediaType.Video && Config.Audio.Enabled)
                    {
                        bool requiresChange = true;
                        SuggestAudio(out AudioStream aStream, out ExternalAudioStream aExtStream, VideoDemuxer.AudioStreams);

                        if (AudioStream != null)
                        {
                            // External audio streams comparison
                            if (Playlist.Selected.ExternalAudioStream != null && aExtStream != null && aExtStream.Index == Playlist.Selected.ExternalAudioStream.Index)
                                requiresChange = false;
                            // Embedded audio streams comparison
                            else if (Playlist.Selected.ExternalAudioStream == null && aStream != null && aStream.StreamIndex == AudioStream.StreamIndex)
                                requiresChange = false;
                        }

                        if (!requiresChange)
                        {
                            if (CanDebug) Log.Debug($"Audio no need to follow video");
                        }
                        else
                        {
                             if (aStream != null)
                                Open(aStream);
                            else if (aExtStream != null)
                                Open(aExtStream);

                             //RequiresResync = true;
                        }
                    }

                    if (stream.Type == MediaType.Video)
                        return args = new OpenVideoStreamCompletedArgs((VideoStream)stream, (VideoStream)oldStream);
                    else if (stream.Type == MediaType.Audio)
                        return args = new OpenAudioStreamCompletedArgs((AudioStream)stream, (AudioStream)oldStream);
                    else
                        return args = new OpenSubtitlesStreamCompletedArgs((SubtitlesStream)stream, (SubtitlesStream)oldStream);
                }
            } catch(Exception e)
            {
                return args = new StreamOpenedArgs(null, null, e.Message);
            } finally
            {
                if (stream.Type == MediaType.Video)
                    OnOpenVideoStreamCompleted((OpenVideoStreamCompletedArgs)args);
                else if (stream.Type == MediaType.Audio)
                    OnOpenAudioStreamCompleted((OpenAudioStreamCompletedArgs)args);
                else
                    OnOpenSubtitlesStreamCompleted((OpenSubtitlesStreamCompletedArgs)args);
            }
        }

        public void OpenSuggestedVideo(bool defaultAudio = false)
        {
            VideoStream stream;
            ExternalVideoStream extStream;

            if (ClosedVideoStream != null)
            {
                Log.Debug("[Video] Found previously closed stream");

                extStream = ClosedVideoStream.Item1;
                if (extStream != null)
                {
                    Open(extStream, false, ClosedVideoStream.Item2 >= 0 ? ClosedVideoStream.Item2 : -1);
                    return;
                }

                stream = ClosedVideoStream.Item2 >= 0 ? (VideoStream)VideoDemuxer.AVStreamToStream[ClosedVideoStream.Item2] : null;
            }
            else
                SuggestVideo(out stream, out extStream, VideoDemuxer.VideoStreams);

            if (stream != null)
                Open(stream, defaultAudio);
            else if (extStream != null)
                Open(extStream, defaultAudio);
            else if (defaultAudio)
                OpenSuggestedAudio(); // We still need audio if no video exists
        }
        public void OpenSuggestedAudio()
        {
            AudioStream stream = null;
            ExternalAudioStream extStream = null;

            if (ClosedAudioStream != null)
            {
                Log.Debug("[Audio] Found previously closed stream");

                extStream = ClosedAudioStream.Item1;
                if (extStream != null)
                {
                    Open(extStream, false, ClosedAudioStream.Item2 >= 0 ? ClosedAudioStream.Item2 : -1);
                    return;
                }

                stream = ClosedAudioStream.Item2 >= 0 ? (AudioStream)VideoDemuxer.AVStreamToStream[ClosedAudioStream.Item2] : null;
            }
            else
                SuggestAudio(out stream, out extStream, VideoDemuxer.AudioStreams);

            if (stream != null)
                Open(stream);
            else if (extStream != null)
                Open(extStream);
        }
        public void OpenSuggestedSubtitles()
        {
            long sessionId = OpenItemCounter;

            try
            {
                // 1. Closed / History / Remember last user selection? Probably application must handle this
                if (ClosedSubtitlesStream != null)
                {
                    Log.Debug("[Subtitles] Found previously closed stream");

                    ExternalSubtitlesStream extStream = ClosedSubtitlesStream.Item1;
                    if (extStream != null)
                    {
                        Open(extStream, false, ClosedSubtitlesStream.Item2 >= 0 ? ClosedSubtitlesStream.Item2 : -1);
                        return;
                    }

                    SubtitlesStream stream = ClosedSubtitlesStream.Item2 >= 0 ? (SubtitlesStream)VideoDemuxer.AVStreamToStream[ClosedSubtitlesStream.Item2] : null;

                    if (stream != null)
                    {
                        Open(stream);
                        return;
                    }
                    else if (extStream != null)
                    {
                        Open(extStream);
                        return;
                    }
                    else
                        ClosedSubtitlesStream = null;
                }

                // High Suggest (first lang priority + high rating + already converted/downloaded)
                // 2. Check embedded steams for high suggest
                if (Config.Subtitles.Languages.Count > 0)
                {
                    foreach (var stream in VideoDemuxer.SubtitlesStreams)
                        if (stream.Language == Config.Subtitles.Languages[0])
                        {
                            Log.Debug("[Subtitles] Found high suggested embedded stream");
                            Open(stream);
                            return;
                        }
                }

                // 3. Check external streams for high suggest
                if (Playlist.Selected.ExternalSubtitlesStreams.Count > 0)
                {
                    ExternalSubtitlesStream extStream = SuggestBestExternalSubtitles();
                    if (extStream != null)
                    {
                        Log.Debug("[Subtitles] Found high suggested external stream");
                        Open(extStream);
                        return;
                    }
                }

            } catch (Exception e)
            {
                Log.Debug($"OpenSuggestedSubtitles canceled? [{e.Message}]");
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    if (sessionId != OpenItemCounter)
                    {
                        Log.Debug("OpenSuggestedSubtitles canceled");
                        return;
                    }

                    ExternalSubtitlesStream extStream;

                    // 4. Search offline if allowed (not async)
                    if (!Playlist.Selected.SearchedLocal && Config.Subtitles.SearchLocal && (Config.Subtitles.SearchLocalOnInputType == null || Config.Subtitles.SearchLocalOnInputType.Count == 0 || Config.Subtitles.SearchLocalOnInputType.Contains(Playlist.InputType)))
                    {
                        Log.Debug("[Subtitles] Searching Local");
                        SearchLocalSubtitles();

                        // 4.1 Check external streams for high suggest (again for the new additions if any)
                        extStream = SuggestBestExternalSubtitles();
                        if (extStream != null)
                        {
                            Log.Debug("[Subtitles] Found high suggested external stream");
                            Open(extStream);
                            return;
                        }
                    }

                    if (sessionId != OpenItemCounter)
                    {
                        Log.Debug("OpenSuggestedSubtitles canceled");
                        return;
                    }

                    // 5. Search online if allowed (not async)
                    if (!Playlist.Selected.SearchedOnline && Config.Subtitles.SearchOnline && (Config.Subtitles.SearchOnlineOnInputType == null || Config.Subtitles.SearchOnlineOnInputType.Count == 0 || Config.Subtitles.SearchOnlineOnInputType.Contains(Playlist.InputType)))
                    {
                        Log.Debug("[Subtitles] Searching Online");
                        SearchOnlineSubtitles();
                    }

                    if (sessionId != OpenItemCounter)
                    {
                        Log.Debug("OpenSuggestedSubtitles canceled");
                        return;
                    }

                    // 6. (Any) Check embedded/external streams for config languages (including 'undefined')
                    SuggestSubtitles(out SubtitlesStream stream, out extStream);

                    if (stream != null)
                        Open(stream);
                    else if (extStream != null)
                        Open(extStream);
                } catch (Exception e)
                {
                    Log.Debug($"OpenSuggestedSubtitles canceled? [{e.Message}]");
                }
            });
        }

        public string OpenDemuxerInput(Demuxer demuxer, DemuxerInput demuxerInput)
        {
            OpenedPlugin?.OnBuffering();

            string error = null;

            SerializableDictionary<string, string> formatOpt = null;
            SerializableDictionary<string, string> copied = null;

            try
            {
                // Set HTTP Config
                if (Playlist.InputType == InputType.Web)
                {
                    formatOpt = Config.Demuxer.GetFormatOptPtr(demuxer.Type);
                    copied = new SerializableDictionary<string, string>();

                    foreach (var opt in formatOpt)
                        copied.Add(opt.Key, opt.Value);

                    if (demuxerInput.UserAgent != null)
                        formatOpt["user_agent"] = demuxerInput.UserAgent;

                    if (demuxerInput.Referrer != null)
                        formatOpt["referer"] = demuxerInput.Referrer;
                    else if (!formatOpt.ContainsKey("referer") && Playlist.Url != null)
                        formatOpt["referer"] = Playlist.Url;

                    if (demuxerInput.HTTPHeaders != null)
                    {
                        formatOpt["headers"] = "";
                        foreach(var header in demuxerInput.HTTPHeaders)
                            formatOpt["headers"] += header.Key + ": " + header.Value + "\r\n";
                    }
                }

                // Open Demuxer Input
                if (demuxerInput.Url != null)
                {
                    error = demuxer.Open(demuxerInput.Url);
                    if (error != null)
                        return error;

                    if (!string.IsNullOrEmpty(demuxerInput.UrlFallback))
                    {
                        Log.Warn($"Fallback to {demuxerInput.UrlFallback}");
                        error = demuxer.Open(demuxerInput.UrlFallback);
                    }
                }
                else if (demuxerInput.IOStream != null)
                    error = demuxer.Open(demuxerInput.IOStream);

                return error;
            } finally
            {
                // Restore HTTP Config
                if (Playlist.InputType == InputType.Web)
                {
                    formatOpt.Clear();
                    foreach(var opt in copied)
                        formatOpt.Add(opt.Key, opt.Value);
                }

                OpenedPlugin?.OnBufferingCompleted();
            }
        }
        #endregion

        #region Close (Only For EnableDecoding)
        public void CloseAudio()
        {
            ClosedAudioStream = new Tuple<ExternalAudioStream, int>(Playlist.Selected.ExternalAudioStream, AudioStream != null ? AudioStream.StreamIndex : -1);

            if (Playlist.Selected.ExternalAudioStream != null)
            {
                Playlist.Selected.ExternalAudioStream.Enabled = false;
                Playlist.Selected.ExternalAudioStream = null;
            }

            AudioDecoder.Dispose(true);
        }
        public void CloseVideo()
        {
            ClosedVideoStream = new Tuple<ExternalVideoStream, int>(Playlist.Selected.ExternalVideoStream, VideoStream != null ? VideoStream.StreamIndex : -1);

            if (Playlist.Selected.ExternalVideoStream != null)
            {
                Playlist.Selected.ExternalVideoStream.Enabled = false;
                Playlist.Selected.ExternalVideoStream = null;
            }

            VideoDecoder.Dispose(true);
        }
        public void CloseSubtitles()
        {
            ClosedSubtitlesStream = new Tuple<ExternalSubtitlesStream, int>(Playlist.Selected.ExternalSubtitlesStream, SubtitlesStream != null ? SubtitlesStream.StreamIndex : -1);

            if (Playlist.Selected.ExternalSubtitlesStream != null)
            {
                Playlist.Selected.ExternalSubtitlesStream.Enabled = false;
                Playlist.Selected.ExternalSubtitlesStream = null;
            }

            SubtitlesDecoder.Dispose(true);
        }
        #endregion

        #region Seek
        public int Seek(long ms = -1, bool forward = false, bool seekInQueue = true)
        {
            int ret = 0;

            if (ms == -1) ms = GetCurTimeMs();

            // Review decoder locks (lockAction should be added to avoid dead locks with flush mainly before lockCodecCtx)
            AudioDecoder.keyFrameRequired = false; // Temporary to avoid dead lock on AudioDecoder.lockCodecCtx
            lock (VideoDecoder.lockCodecCtx)
            lock (AudioDecoder.lockCodecCtx)
            lock (SubtitlesDecoder.lockCodecCtx)
            {
                long seekTimestamp = CalcSeekTimestamp(VideoDemuxer, ms, ref forward);

                // Should exclude seek in queue for all "local/fast" files
                lock (VideoDemuxer.lockActions)
                if (Playlist.InputType == InputType.Torrent || !seekInQueue || VideoDemuxer.SeekInQueue(seekTimestamp, forward) != 0)
                {
                    VideoDemuxer.Interrupter.ForceInterrupt = 1;
                    OpenedPlugin.OnBuffering();
                    lock (VideoDemuxer.lockFmtCtx)
                    {
                        if (VideoDemuxer.Disposed) { VideoDemuxer.Interrupter.ForceInterrupt = 0; return -1; }
                        ret = VideoDemuxer.Seek(seekTimestamp, forward);
                    }
                }

                VideoDecoder.Flush();
                if (AudioStream != null && AudioDecoder.OnVideoDemuxer)
                    AudioDecoder.Flush();

                if (SubtitlesStream != null && SubtitlesDecoder.OnVideoDemuxer)
                    SubtitlesDecoder.Flush();
            }

            if (AudioStream != null && !AudioDecoder.OnVideoDemuxer)
            {
                AudioDecoder.Pause();
                AudioDecoder.Flush();
                AudioDemuxer.PauseOnQueueFull = true;
                RequiresResync = true;
            }

            if (SubtitlesStream != null && !SubtitlesDecoder.OnVideoDemuxer)
            {
                SubtitlesDecoder.Pause();
                SubtitlesDecoder.Flush();
                SubtitlesDemuxer.PauseOnQueueFull = true;
                RequiresResync = true;
            }
            
            return ret;
        }
        public int SeekAudio(long ms = -1, bool forward = false)
        {
            int ret = 0;

            if (AudioDemuxer.Disposed || AudioDecoder.OnVideoDemuxer || !Config.Audio.Enabled) return -1;

            if (ms == -1) ms = GetCurTimeMs();

            long seekTimestamp = CalcSeekTimestamp(AudioDemuxer, ms, ref forward);

            AudioDecoder.keyFrameRequired = false; // Temporary to avoid dead lock on AudioDecoder.lockCodecCtx
            lock (AudioDecoder.lockActions)
            lock (AudioDecoder.lockCodecCtx)
            {
                lock (AudioDemuxer.lockActions)
                    if (AudioDemuxer.SeekInQueue(seekTimestamp, forward) != 0)
                        ret = AudioDemuxer.Seek(seekTimestamp, forward);

                AudioDecoder.Flush();
                if (VideoDecoder.IsRunning)
                {
                    AudioDemuxer.Start();
                    AudioDecoder.Start();
                }
            }

            return ret;
        }
        public int SeekSubtitles(long ms = -1, bool forward = false)
        {
            int ret = 0;

            if (SubtitlesDemuxer.Disposed || SubtitlesDecoder.OnVideoDemuxer || !Config.Subtitles.Enabled) return -1;

            if (ms == -1) ms = GetCurTimeMs();

            long seekTimestamp = CalcSeekTimestamp(SubtitlesDemuxer, ms, ref forward);

            lock (SubtitlesDecoder.lockActions)
            lock (SubtitlesDecoder.lockCodecCtx)
            {
                // Currently disabled as it will fail to seek within the queue the most of the times
                //lock (SubtitlesDemuxer.lockActions)
                    //if (SubtitlesDemuxer.SeekInQueue(seekTimestamp, forward) != 0)
                ret = SubtitlesDemuxer.Seek(seekTimestamp, forward);

                SubtitlesDecoder.Flush();
                if (VideoDecoder.IsRunning)
                {
                    SubtitlesDemuxer.Start();
                    SubtitlesDecoder.Start();
                }
            }

            return ret;
        }

        public long GetCurTime()
        {
            return !VideoDemuxer.Disposed ? VideoDemuxer.CurTime : !AudioDemuxer.Disposed ? AudioDemuxer.CurTime: 0;
        }
        public int GetCurTimeMs()
        {
            return !VideoDemuxer.Disposed ? (int)(VideoDemuxer.CurTime / 10000) : (!AudioDemuxer.Disposed ? (int)(AudioDemuxer.CurTime / 10000): 0);
        }

        private long CalcSeekTimestamp(Demuxer demuxer, long ms, ref bool forward)
        {
            long startTime = demuxer.hlsCtx == null ? demuxer.StartTime : demuxer.hlsCtx->first_timestamp * 10;
            long ticks = (ms * 10000) + startTime;

            if (demuxer.Type == MediaType.Audio) ticks -= Config.Audio.Delay;
            if (demuxer.Type == MediaType.Subs ) ticks -= Config.Subtitles.Delay + (2 * 1000 * 10000); // We even want the previous subtitles

            if (ticks < startTime) 
            {
                ticks = startTime;
                forward = true;
            }
            else if (ticks > startTime + (!VideoDemuxer.Disposed ? VideoDemuxer.Duration : AudioDemuxer.Duration) - (50 * 10000))
            {
                ticks = startTime + demuxer.Duration - (50 * 10000);
                forward = false;
            }

            return ticks;
        }
        #endregion

        #region Start/Pause/Stop
        public void Pause()
        {
            VideoDecoder.Pause();
            AudioDecoder.Pause();
            SubtitlesDecoder.Pause();

            VideoDemuxer.Pause();
            AudioDemuxer.Pause();
            SubtitlesDemuxer.Pause();
        }
        public void PauseDecoders()
        {
            VideoDecoder.Pause();
            AudioDecoder.Pause();
            SubtitlesDecoder.Pause();
        }
        public void PauseOnQueueFull()
        {
            VideoDemuxer.PauseOnQueueFull = true;
            AudioDemuxer.PauseOnQueueFull = true;
            SubtitlesDemuxer.PauseOnQueueFull = true;
        }
        public void Start()
        {
            //if (RequiresResync) Resync();

            if (Config.Audio.Enabled)
            {
                AudioDemuxer.Start();
                AudioDecoder.Start();
            }

            if (Config.Video.Enabled)
            {
                VideoDemuxer.Start();
                VideoDecoder.Start();
            }
            
            if (Config.Subtitles.Enabled)
            {
                SubtitlesDemuxer.Start();
                SubtitlesDecoder.Start();
            }
        }
        public void Stop()
        {
            Interrupt = true;

            VideoDecoder.Dispose();
            AudioDecoder.Dispose();
            SubtitlesDecoder.Dispose();
            AudioDemuxer.Dispose();
            SubtitlesDemuxer.Dispose();
            VideoDemuxer.Dispose();

            Interrupt = false;
        }
        public void StopThreads()
        {
            Interrupt = true;

            VideoDecoder.Stop();
            AudioDecoder.Stop();
            SubtitlesDecoder.Stop();
            AudioDemuxer.Stop();
            SubtitlesDemuxer.Stop();
            VideoDemuxer.Stop();

            Interrupt = false;
        }
        #endregion

        public void Resync(long timestamp = -1)
        {
            bool isRunning = VideoDemuxer.IsRunning;

            if (AudioStream != null && AudioStream.Demuxer.Type != MediaType.Video && Config.Audio.Enabled)
            {
                if (timestamp == -1) timestamp = VideoDemuxer.CurTime;
                if (CanInfo) Log.Info($"Resync audio to {TicksToTime(timestamp)}");

                SeekAudio(timestamp / 10000);
                if (isRunning)
                {
                    AudioDemuxer.Start();
                    AudioDecoder.Start();
                }
            }

            if (SubtitlesStream != null && SubtitlesStream.Demuxer.Type != MediaType.Video && Config.Subtitles.Enabled)
            {
                if (timestamp == -1) timestamp = VideoDemuxer.CurTime;
                if (CanInfo) Log.Info($"Resync subs to {TicksToTime(timestamp)}");

                SeekSubtitles(timestamp / 10000);
                if (isRunning)
                {
                    SubtitlesDemuxer.Start();
                    SubtitlesDecoder.Start();
                }
            }

            RequiresResync = false;
        }

        public void ResyncSubtitles(long timestamp = -1)
        {
            if (SubtitlesStream != null && Config.Subtitles.Enabled)
            {
                if (timestamp == -1) timestamp = VideoDemuxer.CurTime;
                if (CanInfo) Log.Info($"Resync subs to {TicksToTime(timestamp)}");

                if (SubtitlesStream.Demuxer.Type != MediaType.Video)
                    SeekSubtitles(timestamp / 10000);
                else
                    
                if (VideoDemuxer.IsRunning)
                {
                    SubtitlesDemuxer.Start();
                    SubtitlesDecoder.Start();
                }
            }
        }
        public void Flush()
        {
            VideoDemuxer.DisposePackets();
            AudioDemuxer.DisposePackets();
            SubtitlesDemuxer.DisposePackets();

            VideoDecoder.Flush();
            AudioDecoder.Flush();
            SubtitlesDecoder.Flush();
        }
        public long GetVideoFrame(long timestamp = -1)
        {
            // TBR: Between seek and GetVideoFrame lockCodecCtx is lost and if VideoDecoder is running will already have decoded some frames (Currently ensure you pause VideDecoder before seek)

            int ret;
            AVPacket* packet = av_packet_alloc();
            AVFrame*  frame  = av_frame_alloc();

            lock (VideoDemuxer.lockFmtCtx)
            lock (VideoDecoder.lockCodecCtx)
            while (VideoDemuxer.VideoStream != null && !Interrupt)
            {
                if (VideoDemuxer.VideoPackets.Count == 0)
                {
                    VideoDemuxer.Interrupter.Request(Requester.Read);
                    ret = av_read_frame(VideoDemuxer.FormatContext, packet);
                    if (ret != 0) return -1;
                }
                else
                {
                    packet = VideoDemuxer.VideoPackets.Dequeue();
                }

                if (!VideoDemuxer.EnabledStreams.Contains(packet->stream_index)) { av_packet_unref(packet); continue; }

                VideoDemuxer.UpdateHLSTime();

                switch (VideoDemuxer.FormatContext->streams[packet->stream_index]->codecpar->codec_type)
                {
                    case AVMEDIA_TYPE_AUDIO:
                        if (!VideoDecoder.keyFrameRequired && (timestamp == -1 || (long)(frame->pts * AudioStream.Timebase) - VideoDemuxer.StartTime > timestamp))
                            VideoDemuxer.AudioPackets.Enqueue(packet);
                        
                        packet = av_packet_alloc();

                        continue;

                    case AVMEDIA_TYPE_SUBTITLE:
                        if (!VideoDecoder.keyFrameRequired && (timestamp == -1 || (long)(frame->pts * SubtitlesStream.Timebase) - VideoDemuxer.StartTime > timestamp))
                            VideoDemuxer.SubtitlesPackets.Enqueue(packet);

                        packet = av_packet_alloc();

                        continue;

                    case AVMEDIA_TYPE_VIDEO:
                        ret = avcodec_send_packet(VideoDecoder.CodecCtx, packet);
                        av_packet_free(&packet);
                        packet = av_packet_alloc();

                        if (ret != 0) return -1;
                        
                        //VideoDemuxer.UpdateCurTime();

                        while (VideoDemuxer.VideoStream != null && !Interrupt)
                        {
                            ret = avcodec_receive_frame(VideoDecoder.CodecCtx, frame);
                            if (ret != 0) { av_frame_unref(frame); break; }

                            if (frame->best_effort_timestamp != AV_NOPTS_VALUE)
                                frame->pts = frame->best_effort_timestamp;
                            else if (frame->pts == AV_NOPTS_VALUE)
                                { av_frame_unref(frame); continue; }

                            if (VideoDecoder.keyFrameRequired && frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                            {
                                if (CanWarn) Log.Warn($"Seek to keyframe failed [{frame->pict_type} | {frame->key_frame}]");
                                av_frame_unref(frame);
                                continue;
                            }

                            VideoDecoder.keyFrameRequired = false;

                            // Accurate seek with +- half frame distance
                            if (timestamp != -1 && (long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime + VideoStream.FrameDuration / 2 < timestamp)
                            {
                                av_frame_unref(frame);
                                continue;
                            }

                            //if (CanInfo) Info($"Asked for {Utils.TicksToTime(timestamp)} and got {Utils.TicksToTime((long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime)} | Diff {Utils.TicksToTime(timestamp - ((long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime))}");
                            VideoDecoder.StartTime = (long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime;

                            VideoFrame mFrame = VideoDecoder.ProcessVideoFrame(frame);
                            if (mFrame == null) return -1;

                            if (mFrame != null)
                            {
                                VideoDecoder.Frames.Enqueue(mFrame);
                                
                                while (!VideoDemuxer.Disposed && !Interrupt)
                                {
                                    frame = av_frame_alloc();
                                    ret = avcodec_receive_frame(VideoDecoder.CodecCtx, frame);
                                    if (ret != 0) break;
                                    VideoFrame mFrame2 = VideoDecoder.ProcessVideoFrame(frame);
                                    if (mFrame2 != null) VideoDecoder.Frames.Enqueue(mFrame);
                                }

                                av_packet_free(&packet);
                                av_frame_free(&frame);
                                return mFrame.timestamp;
                            }
                        }

                        break; // Switch break

                } // Switch

            } // While

            av_packet_free(&packet);
            av_frame_free(&frame);
            return -1;
        }
        public new void Dispose()
        {
            Stop();
            VideoDecoder.DisposeVA();
            base.Dispose();
        }

        public void PrintStats()
        {
            string dump = "\r\n-===== Streams / Packets / Frames =====-\r\n";
            dump += $"\r\n AudioPackets      ({VideoDemuxer.AudioStreams.Count}): {VideoDemuxer.AudioPackets.Count}";
            dump += $"\r\n VideoPackets      ({VideoDemuxer.VideoStreams.Count}): {VideoDemuxer.VideoPackets.Count}";
            dump += $"\r\n SubtitlesPackets  ({VideoDemuxer.SubtitlesStreams.Count}): {VideoDemuxer.SubtitlesPackets.Count}";
            dump += $"\r\n AudioPackets      ({AudioDemuxer.AudioStreams.Count}): {AudioDemuxer.AudioPackets.Count} (AudioDemuxer)";
            dump += $"\r\n SubtitlesPackets  ({SubtitlesDemuxer.SubtitlesStreams.Count}): {SubtitlesDemuxer.SubtitlesPackets.Count} (SubtitlesDemuxer)";

            dump += $"\r\n Video Frames         : {VideoDecoder.Frames.Count}";
            dump += $"\r\n Audio Frames         : {AudioDecoder.Frames.Count}";
            dump += $"\r\n Subtitles Frames     : {SubtitlesDecoder.Frames.Count}";

            if (CanInfo) Log.Info(dump);
        }

        #region Recorder
        Remuxer Recorder;
        public event EventHandler RecordingCompleted;
        public bool IsRecording
        {
            get => VideoDecoder.isRecording || AudioDecoder.isRecording;
        }
        int oldMaxAudioFrames;
        bool recHasVideo;
        public void StartRecording(ref string filename, bool useRecommendedExtension = true)
        {
            if (IsRecording) StopRecording();

            oldMaxAudioFrames = -1;
            recHasVideo = false;

            if (CanInfo) Log.Info("Record Start");

            recHasVideo = !VideoDecoder.Disposed && VideoDecoder.Stream != null;

            if (useRecommendedExtension)
                filename = $"{filename}.{(recHasVideo ? VideoDecoder.Stream.Demuxer.Extension : AudioDecoder.Stream.Demuxer.Extension)}";

            Recorder.Open(filename);

            bool failed;

            if (recHasVideo)
            {
                failed = Recorder.AddStream(VideoDecoder.Stream.AVStream) != 0;
                if (CanInfo) Log.Info(failed ? "Failed to add video stream" : "Video stream added to the recorder");
            }

            if (!AudioDecoder.Disposed && AudioDecoder.Stream != null)
            {
                failed = Recorder.AddStream(AudioDecoder.Stream.AVStream, !AudioDecoder.OnVideoDemuxer) != 0;
                if (CanInfo) Log.Info(failed ? "Failed to add audio stream" : "Audio stream added to the recorder");
            }

            if (!Recorder.HasStreams || Recorder.WriteHeader() != 0) return; //throw new Exception("Invalid remuxer configuration");

            // Check also buffering and possible Diff of first audio/video timestamp to remuxer to ensure sync between each other (shouldn't be more than 30-50ms)
            oldMaxAudioFrames = Config.Decoder.MaxAudioFrames;
            //long timestamp = Math.Max(VideoDemuxer.CurTime + VideoDemuxer.BufferedDuration, AudioDemuxer.CurTime + AudioDemuxer.BufferedDuration) + 1500 * 10000;
            Config.Decoder.MaxAudioFrames = Config.Decoder.MaxVideoFrames;

            VideoDecoder.StartRecording(Recorder);
            AudioDecoder.StartRecording(Recorder);
        }
        public void StopRecording()
        {
            if (oldMaxAudioFrames != -1) Config.Decoder.MaxAudioFrames = oldMaxAudioFrames;

            VideoDecoder.StopRecording();
            AudioDecoder.StopRecording();
            Recorder.Dispose();
            oldMaxAudioFrames = -1;
            if (CanInfo) Log.Info("Record Completed");
        }
        internal void RecordCompleted(MediaType type)
        {
            if (!recHasVideo || (recHasVideo && type == MediaType.Video))
            {
                StopRecording();
                RecordingCompleted?.Invoke(this, new EventArgs());
            }
        }
        #endregion
    }
}