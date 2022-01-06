using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.AVMediaType;
using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaRemuxer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

using static FlyleafLib.Utils;

namespace FlyleafLib.MediaFramework.MediaContext
{
    public unsafe class DecoderContext : PluginHandler
    {
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
        #endregion

        #region Initialize
        public DecoderContext(Config config = null, Control control = null, int uniqueId = -1, bool enableDecoding = true) : base(config, uniqueId)
        {
            Master.RegisterFFmpeg();

            EnableDecoding      = enableDecoding;

            AudioDemuxer        = new Demuxer(Config.Demuxer, MediaType.Audio, UniqueId, EnableDecoding);
            VideoDemuxer        = new Demuxer(Config.Demuxer, MediaType.Video, UniqueId, EnableDecoding);
            SubtitlesDemuxer    = new Demuxer(Config.Demuxer, MediaType.Subs,  UniqueId, EnableDecoding);

            Recorder            = new Remuxer(UniqueId);

            // TBR: Dont initialize them if Decoding is not enabled (ensure all instances are safe - checked for not null)
            VideoDecoder        = new VideoDecoder(Config, control, UniqueId, EnableDecoding);
            AudioDecoder        = new AudioDecoder(Config, UniqueId, VideoDecoder);
            SubtitlesDecoder    = new SubtitlesDecoder(Config, UniqueId);

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

            OnInitializingSwitch();
            Stop();
            OnInitializedSwitch();
        }
        #endregion

        #region Events
        public event EventHandler<AudioInputOpenedArgs>         AudioInputOpened;
        public event EventHandler<VideoInputOpenedArgs>         VideoInputOpened;
        public event EventHandler<SubtitlesInputOpenedArgs>     SubtitlesInputOpened;
        public event EventHandler<AudioStreamOpenedArgs>        AudioStreamOpened;
        public event EventHandler<VideoStreamOpenedArgs>        VideoStreamOpened;
        public event EventHandler<SubtitlesStreamOpenedArgs>    SubtitlesStreamOpened;

        public class InputOpenedArgs : EventArgs
        {
            public InputBase    Input;
            public InputBase    OldInput;
            public string       Error;
            public bool         Success => Error == null;
            public bool         IsUserInput;
            public InputOpenedArgs(InputBase input = null, InputBase oldInput = null, string error = null, bool isUserInput = false) { Input = input; OldInput= oldInput; Error = error; IsUserInput = isUserInput; }
        }
        public class AudioInputOpenedArgs : InputOpenedArgs { public AudioInputOpenedArgs(AudioInput input = null, AudioInput oldInput = null, string error = null, bool isUserInput = false) : base(input, oldInput, error, isUserInput) { } }
        public class VideoInputOpenedArgs : InputOpenedArgs { public VideoInputOpenedArgs(VideoInput input = null, VideoInput oldInput = null, string error = null, bool isUserInput = false) : base(input, oldInput, error, isUserInput) { } }
        public class SubtitlesInputOpenedArgs : InputOpenedArgs { public SubtitlesInputOpenedArgs(SubtitlesInput input = null, SubtitlesInput oldInput = null, string error = null, bool isUserInput = false) : base(input, oldInput, error, isUserInput) { } }

        public class StreamOpenedArgs
        {
            public StreamBase   Stream;
            public StreamBase   OldStream;
            public string       Error;
            public bool         Success => Error == null;
            public StreamOpenedArgs(StreamBase stream = null, StreamBase oldStream = null, string error = null) { Stream = stream; OldStream= oldStream; Error = error; }
        }
        public class AudioStreamOpenedArgs : StreamOpenedArgs 
        {
            public new AudioStream Stream   => (AudioStream)base.Stream;
            public new AudioStream OldStream=> (AudioStream)base.OldStream;
            public AudioStreamOpenedArgs(AudioStream stream = null, AudioStream oldStream = null, string error = null): base(stream, oldStream, error) { }
        }
        public class VideoStreamOpenedArgs : StreamOpenedArgs
        {
            public new VideoStream Stream   => (VideoStream)base.Stream;
            public new VideoStream OldStream=> (VideoStream)base.OldStream;
            public VideoStreamOpenedArgs(VideoStream stream = null, VideoStream oldStream = null, string error = null): base(stream, oldStream, error) { }
        }
        public class SubtitlesStreamOpenedArgs : StreamOpenedArgs
        {
            public new SubtitlesStream Stream   => (SubtitlesStream)base.Stream;
            public new SubtitlesStream OldStream=> (SubtitlesStream)base.OldStream;
            public SubtitlesStreamOpenedArgs(SubtitlesStream stream = null, SubtitlesStream oldStream = null, string error = null): base(stream, oldStream, error) { }
        }

        private void OnAudioInputOpened(AudioInputOpenedArgs args = null)
        {
            Log($"[AudioInput] {(args.OldInput != null ? args.OldInput.Url : "None")} => {(args.Input != null ? args.Input.Url : "None")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            AudioInputOpened?.Invoke(this, args);
        }
        private void OnVideoInputOpened(VideoInputOpenedArgs args = null)
        {
            Log($"[VideoInput] {(args.OldInput != null ? args.OldInput.Url : "None")} => {(args.Input != null ? args.Input.Url : "None")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            VideoInputOpened?.Invoke(this, args);
        }
        private void OnSubtitlesInputOpened(SubtitlesInputOpenedArgs args = null)
        {
            Log($"[SubtitlesInput] {(args.OldInput != null ? args.OldInput.Url : "None")} => {(args.Input != null ? args.Input.Url : "None")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            SubtitlesInputOpened?.Invoke(this, args);
        }
        private void OnAudioStreamOpened(AudioStreamOpenedArgs args = null)
        {
            if (args != null) Log($"[AudioStream] #{(args.OldStream != null ? args.OldStream.StreamIndex.ToString() : "_")} => #{(args.Stream != null ? args.Stream.StreamIndex.ToString() : "_")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            AudioStreamOpened?.Invoke(this, args);
        }
        private void OnVideoStreamOpened(VideoStreamOpenedArgs args = null)
        {
            if (args != null) Log($"[VideoStream] #{(args.OldStream != null ? args.OldStream.StreamIndex.ToString() : "_")} => #{(args.Stream != null ? args.Stream.StreamIndex.ToString() : "_")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            VideoStreamOpened?.Invoke(this, args);
        }
        private void OnSubtitlesStreamOpened(SubtitlesStreamOpenedArgs args = null)
        {
            if (args != null) Log($"[SubtitlesStream] #{(args.OldStream != null ? args.OldStream.StreamIndex.ToString() : "_")} => #{(args.Stream != null ? args.Stream.StreamIndex.ToString() : "_")}{(!args.Success ? " [Error: " + args.Error  + "]": "")}");
            SubtitlesStreamOpened?.Invoke(this, args);
        }
        #endregion

        #region Open
        public SubtitlesInputOpenedArgs OpenSubtitles(string url, bool defaultSubtitles = true)
        {
            SubtitlesInputOpenedArgs result = null;
            SubtitlesInput curInput = null;

            if (!Config.Subtitles.Enabled)
                return result = new SubtitlesInputOpenedArgs(null, null, $"Subtitles are disabled", true);

            try
            {
                Log($"Opening subs {url}");

                OpenResults res = base.OpenSubtitles(url);

                if (res == null)
                    return result = new SubtitlesInputOpenedArgs(null, null, $"No plugin found for {url}", true);

                if (res.Error != null)
                    return result = new SubtitlesInputOpenedArgs(null, null, res.Error, true);

                foreach(var input in ((IProvideSubtitles)OpenedSubtitlesPlugin).SubtitlesInputs)
                    if (input.Url == url) curInput = input;

            } catch (Exception e)
            {
                return result = new SubtitlesInputOpenedArgs(null, null, e.Message, true);
            } finally
            {
                if (result != null) OnSubtitlesInputOpened(result);
            }

            return OpenSubtitlesInput(curInput, defaultSubtitles, true);
        }
        public SubtitlesInputOpenedArgs OpenSubtitlesInput(SubtitlesInput input, bool defaultSubtitles = true)
        {
            return OpenSubtitlesInput(input, defaultSubtitles, false);
        }
        private SubtitlesInputOpenedArgs OpenSubtitlesInput(SubtitlesInput input, bool defaultSubtitles, bool isUserInput)
        {
            SubtitlesInputOpenedArgs result = null;

            try
            {
                SubtitlesInput oldInput = SubtitlesInput;

                if (input == null)
                    return result = new SubtitlesInputOpenedArgs(input, oldInput, $"Invalid subtitles input", isUserInput);

                OpenResults res = OnOpen(input);
                if (res != null && res.Error != null)
                    return result = new SubtitlesInputOpenedArgs(input, oldInput, res.Error, isUserInput);

                string ret = Open(input);
                if (ret != null)
                    return result = new SubtitlesInputOpenedArgs(input, oldInput, $"Failed to open subtitles input {(input.Url != null ? input.Url : "(custom)")}\r\n{ret}", isUserInput);

                input.Enabled = true;

                if (defaultSubtitles)
                {
                    SubtitlesStream subtitlesStream = SuggestSubtitles(SubtitlesDemuxer.SubtitlesStreams);

                    // External Subtitles will have undefined language
                    if (subtitlesStream == null)
                        subtitlesStream = SuggestSubtitles(SubtitlesDemuxer.SubtitlesStreams, Language.Get("und"));

                    if (subtitlesStream != null)
                    {
                        subtitlesStream.SubtitlesInput = input;
                        Open(subtitlesStream);
                    }
                }

                return result = new SubtitlesInputOpenedArgs(input, oldInput, null, isUserInput);

            } catch (Exception e)
            {
                return result = new SubtitlesInputOpenedArgs(null, null, e.Message, isUserInput);

            } finally
            {
                OnSubtitlesInputOpened(result);
            }
        }

        public VideoInputOpenedArgs OpenVideo(Stream iostream, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            return OpenVideo((object)iostream, defaultInput, defaultVideo, defaultAudio, defaultSubtitles);
        }
        public VideoInputOpenedArgs OpenVideo(string url, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            return OpenVideo((object)url, defaultInput, defaultVideo, defaultAudio, defaultSubtitles);
        }
        private VideoInputOpenedArgs OpenVideo(object input, bool defaultInput = true, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            Initialize();

            VideoInputOpenedArgs result = null;

            if (!Config.Video.Enabled && !Config.Audio.Enabled)
                return result = new VideoInputOpenedArgs(null, null, $"Both audio and video are disabled", true);

            try
            {
                if (input == null)
                    return result = new VideoInputOpenedArgs(null, null, $"Null input", true);

                Log($"Opening {input.ToString()}");

                OpenResults res;
                if (input is Stream)
                    res = Open((Stream)input);
                else
                    res = Open(input.ToString());

                if (res == null)
                    return result = new VideoInputOpenedArgs(null, null, $"No plugin found for input", true);

                if (res.Error != null)
                    return result = new VideoInputOpenedArgs(null, null, res.Error, true);

                if (!defaultInput)
                    return result = new VideoInputOpenedArgs(null, null, null, true);

            } catch (Exception e)
            {
                return result = new VideoInputOpenedArgs(null, null, e.Message, true);
            } finally
            {
                if (result != null) OnVideoInputOpened(result);
            }

            return OpenVideoInput(SuggestVideo(), defaultVideo, defaultAudio, defaultSubtitles, true);
        }
        public VideoInputOpenedArgs OpenVideoInput(VideoInput input, bool defaultVideo = true, bool defaultAudio = true, bool defaultSubtitles = true)
        {
            return OpenVideoInput(input, defaultVideo, defaultAudio, defaultSubtitles, false);
        }
        private VideoInputOpenedArgs OpenVideoInput(VideoInput input, bool defaultVideo, bool defaultAudio, bool defaultSubtitles, bool isUserInput)
        {
            if (!isUserInput && VideoInput != null && EnableDecoding) InitializeSwitch(); // EnableDecoding required cause it disposes the decoders/demuxers (TBR)

            VideoInputOpenedArgs result = null;
            VideoInput oldInput = VideoInput;

            try
            {
                if (input == null)
                    return result = new VideoInputOpenedArgs(input, oldInput, $"Invalid video input", isUserInput);

                OpenResults res = OnOpen(input);
                if (res != null && res.Error != null)
                    return result = new VideoInputOpenedArgs(input, oldInput, res.Error, isUserInput);

                OpenedPlugin?.OnBuffering();
                string ret = Open(input);
                OpenedPlugin?.OnBufferingCompleted();

                if (ret != null)
                    return result = new VideoInputOpenedArgs(input, oldInput, $"Failed to open video input {(input.Url != null ? input.Url : "(custom)")}\r\n{ret}", isUserInput);

                input.Enabled = true;

                VideoStream videoStream = null;

                if (defaultVideo && Config.Video.Enabled) // Audio player disables video // We allow to continue without Video Stream to play just audio
                {
                    videoStream = SuggestVideo(VideoDemuxer.VideoStreams);
                    if (videoStream != null) Open(videoStream);
                }

                if (defaultAudio && Config.Audio.Enabled)
                    OpenSuggestedAudio(); // Could be the same audiodemuxer input (no need to re-open)

                if (defaultSubtitles && Config.Subtitles.Enabled && videoStream != null)
                    OpenSuggestedSubtitles(); // Could be the same subtitlesdemuxer input (no need to re-open)

                return result = new VideoInputOpenedArgs(input, oldInput, null, isUserInput);

            } catch (Exception e)
            {
                return result = new VideoInputOpenedArgs(null, null, e.Message, isUserInput);
            } finally
            {
                OnVideoInputOpened(result);
            }
        }

        public object OpenAudio(object input, bool defaultInput = true, bool defaultAudio = true)
        {
            Initialize();

            AudioInputOpenedArgs result = null;

            try
            {
                Log($"Opening {input.ToString()}");

                OpenResults res;
                if (input is Stream)
                    res = Open((Stream)input);
                else
                    res = Open(input.ToString());

                if (res == null)
                    return result = new AudioInputOpenedArgs(null, null, $"No plugin found for input", true);

                if (res.Error != null)
                    return result = new AudioInputOpenedArgs(null, null, res.Error, true);

                if (!defaultInput)
                    return result = new AudioInputOpenedArgs(null, null, null, true);

            } catch (Exception e)
            {
                return result = new AudioInputOpenedArgs(null, null, e.Message, true);
            } finally
            {
                if (result != null) OnAudioInputOpened(result);
            }

            AudioInput audioInput = SuggestAudio(); // TBR: No default plugins currently suggest audio inputs (should we identify by file extension?)
            if (audioInput != null)
                return OpenAudioInput(audioInput, defaultAudio, true);
            else
                return OpenVideoInput(SuggestVideo(), false, defaultAudio, false, true);
        }
        public AudioInputOpenedArgs OpenAudioInput(AudioInput input, bool defaultAudio = true)
        {
            return OpenAudioInput(input, defaultAudio, false);
        }
        private AudioInputOpenedArgs OpenAudioInput(AudioInput input, bool defaultAudio, bool isUserInput)
        {
            AudioInputOpenedArgs result = null;
            AudioInput oldInput = AudioInput;

            try
            {
                if (input == null)
                    return result = new AudioInputOpenedArgs(input, oldInput, $"Invalid audio input", isUserInput);

                OpenResults res = OnOpen(input);
                if (res != null && res.Error != null)
                    return result = new AudioInputOpenedArgs(input, oldInput, res.Error, isUserInput);

                string ret = Open(input);
                if (ret != null)
                    return result = new AudioInputOpenedArgs(input, oldInput, $"Failed to open audio input {(input.Url != null ? input.Url : "(custom)")}\r\n{ret}", isUserInput);

                input.Enabled = true;

                if (defaultAudio)
                {
                    AudioStream audioStream = SuggestAudio(AudioDemuxer.AudioStreams);
                    if (audioStream != null)
                    {
                        audioStream.AudioInput = input;
                        Open(audioStream);
                    }
                }

                return result = new AudioInputOpenedArgs(input, oldInput, null, isUserInput);

            } catch (Exception e)
            {
                return result = new AudioInputOpenedArgs(null, null, e.Message, isUserInput);
            } finally
            {
                OnAudioInputOpened(result);
            }
        }
        
        public bool OpenSuggestedVideo()
        {
            VideoStream stream = SuggestVideo(VideoDemuxer.VideoStreams);
            if (stream != null)
            {
                Open(stream);
                return true;
            }
            else
            {
                VideoInput input = SuggestVideo();
                if (input != null)
                {
                    OpenVideoInput(input);
                    return true;
                }
            }

            return false;
        }
        public void OpenSuggestedAudio()
        {
            AudioStream stream = SuggestAudio(VideoDemuxer.AudioStreams);
            if (stream != null) 
                Open(stream);
            else
            {
                AudioInput input = SuggestAudio();
                if (input != null) OpenAudioInput(input);
            }
        }
        public void OpenSuggestedSubtitles()
        {
            Task.Run(() =>
            {
                SuggestSubtitles(out SubtitlesStream stream, out SubtitlesInput input, VideoDemuxer.SubtitlesStreams);

                if (stream != null)
                    Open(stream);
                else if (input != null)
                    OpenSubtitlesInput(input);
            });
        }

        private string Open(InputBase input)
        {
            string res;

            Demuxer demuxer = input is VideoInput ? VideoDemuxer : (input is AudioInput ? AudioDemuxer : SubtitlesDemuxer);

            if (input.IOStream != null)
                res = demuxer.Open(input.IOStream);
            else
            {
                res = demuxer.Open(input.Url);
                if (res != null && !string.IsNullOrEmpty(input.UrlFallback))
                {
                    Log($"Fallback to {input.UrlFallback}");
                    res = demuxer.Open(input.UrlFallback);
                }
            }

            return res;
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
            StreamOpenedArgs result = null;

            try
            {
                lock (stream.Demuxer.lockFmtCtx)
                {
                    StreamBase oldStream = stream.Type == MediaType.Video ? (StreamBase)VideoStream : (stream.Type == MediaType.Audio ? (StreamBase)AudioStream : (StreamBase)SubtitlesStream);

                    // onClose | Inform plugins for closing audio/subs external input in case of embedded switch
                    if (stream.Demuxer.Type == MediaType.Video)
                    {
                        if (stream.Type == MediaType.Audio && VideoStream != null)
                        {
                            if (!EnableDecoding) AudioDemuxer.Dispose();
                            onClose(AudioInput);
                        }
                        else if (stream.Type == MediaType.Subs)
                        {
                            if (!EnableDecoding) SubtitlesDemuxer.Dispose();
                            onClose(SubtitlesInput);
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
                                return result = new VideoStreamOpenedArgs((VideoStream)stream, (VideoStream)oldStream, $"Failed to open video stream #{stream.StreamIndex}\r\n{ret}");
                            else if (stream.Type == MediaType.Audio)
                                return result = new AudioStreamOpenedArgs((AudioStream)stream, (AudioStream)oldStream, $"Failed to open audio stream #{stream.StreamIndex}\r\n{ret}");
                            else
                                return result = new SubtitlesStreamOpenedArgs((SubtitlesStream)stream, (SubtitlesStream)oldStream, $"Failed to open subtitles stream #{stream.StreamIndex}\r\n{ret}");
                        }
                    }
                    else
                        stream.Demuxer.EnableStream(stream);

                    // Re-suggest audio/(subs)? and re-open if required (mainly same programs with video to avoid additional bandwidth)
                    if (defaultAudio && stream.Type == MediaType.Video)
                    {
                        if (Config.Audio.Enabled)
                        {
                            AudioStream audioStream = SuggestAudio(VideoDemuxer.AudioStreams);
                            if (audioStream != null && (VideoDemuxer.AudioStream == null || audioStream.StreamIndex != VideoDemuxer.AudioStream.StreamIndex)) 
                                Open(audioStream, true);
                            else if (audioStream != null)
                                Log($"Audio no need to follow video");
                        }
                    }

                    // Resync/Restart Demuxers (if we have large demuxer buffer would be really slow to auto resync)
                    //if (VideoDemuxer.CurTime > 0 && (!defaultAudio || stream.Type == MediaType.Video))
                    //{
                    //    if (stream.Demuxer.Type == MediaType.Video)
                    //        Seek();
                    //    else if (stream.Demuxer.Type == MediaType.Audio)
                    //        SeekAudio();
                    //    else
                    //        SeekSubtitles();
                    //}

                    //if (VideoDemuxer.IsRunning) { stream.Demuxer.Start(); if (EnableDecoding) GetDecoderPtr(stream.Type).Start(); }

                    if (stream.Type == MediaType.Video)
                        return result = new VideoStreamOpenedArgs((VideoStream)stream, (VideoStream)oldStream);
                    else if (stream.Type == MediaType.Audio)
                        return result = new AudioStreamOpenedArgs((AudioStream)stream, (AudioStream)oldStream);
                    else
                        return result = new SubtitlesStreamOpenedArgs((SubtitlesStream)stream, (SubtitlesStream)oldStream);
                }
            } catch(Exception e)
            {
                return result = new StreamOpenedArgs(null, null, e.Message);
            } finally
            {
                if (stream.Type == MediaType.Video)
                    OnVideoStreamOpened((VideoStreamOpenedArgs)result);
                else if (stream.Type == MediaType.Audio)
                    OnAudioStreamOpened((AudioStreamOpenedArgs)result);
                else
                    OnSubtitlesStreamOpened((SubtitlesStreamOpenedArgs)result);
            }
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
                if (OpenedPlugin.Name == "BitSwarm" || !seekInQueue || VideoDemuxer.SeekInQueue(seekTimestamp, forward) != 0)
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
                Log($"Resync audio to {TicksToTime(timestamp)}");

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
                Log($"Resync subs to {TicksToTime(timestamp)}");

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
                Log($"Resync subs to {TicksToTime(timestamp)}");

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
            //bool wasRunning = VideoDecoder.IsRunning;
            //Pause();
            
            VideoDemuxer.DisposePackets();
            AudioDemuxer.DisposePackets();
            SubtitlesDemuxer.DisposePackets();

            VideoDecoder.Flush();
            AudioDecoder.Flush();
            SubtitlesDecoder.Flush();

            //if (wasRunning) Start();
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
                    VideoDemuxer.VideoPackets.TryDequeue(out IntPtr packetPtr);
                    packet = (AVPacket*) packetPtr;
                }

                if (!VideoDemuxer.EnabledStreams.Contains(packet->stream_index)) { av_packet_unref(packet); continue; }

                if (packet->dts != AV_NOPTS_VALUE)
                {
                    VideoDemuxer.lastPacketTs = (long)(packet->dts * VideoDemuxer.AVStreamToStream[packet->stream_index].Timebase);
                    VideoDemuxer.UpdateHLSTime();
                }

                switch (VideoDemuxer.FormatContext->streams[packet->stream_index]->codecpar->codec_type)
                {
                    case AVMEDIA_TYPE_AUDIO:
                        if (!VideoDecoder.keyFrameRequired && (timestamp == -1 || (long)(frame->pts * AudioStream.Timebase) - VideoDemuxer.StartTime > timestamp))
                            VideoDemuxer.AudioPackets.Enqueue((IntPtr)packet);
                        packet = av_packet_alloc();

                        continue;

                    case AVMEDIA_TYPE_SUBTITLE:
                        if (!VideoDecoder.keyFrameRequired && (timestamp == -1 || (long)(frame->pts * SubtitlesStream.Timebase) - VideoDemuxer.StartTime > timestamp))
                            VideoDemuxer.SubtitlesPackets.Enqueue((IntPtr)packet);
                        packet = av_packet_alloc();

                        continue;

                    case AVMEDIA_TYPE_VIDEO:
                        ret = avcodec_send_packet(VideoDecoder.CodecCtx, packet);
                        av_packet_free(&packet);
                        packet = av_packet_alloc();

                        if (ret != 0) return -1;
                        
                        VideoDemuxer.UpdateCurTime();

                        while (VideoDemuxer.VideoStream != null && !Interrupt)
                        {
                            ret = avcodec_receive_frame(VideoDecoder.CodecCtx, frame);
                            if (ret != 0) { av_frame_unref(frame); break; }

                            frame->pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                            if (frame->pts == AV_NOPTS_VALUE) { av_frame_unref(frame); continue; }

                            if (VideoDecoder.keyFrameRequired && frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                            {
                                Log($"Seek to keyframe failed [{frame->pict_type} | {frame->key_frame}]");
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

                            //Log($"Asked for {Utils.TicksToTime(timestamp)} and got {Utils.TicksToTime((long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime)} | Diff {Utils.TicksToTime(timestamp - ((long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime))}");
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
            VideoDecoder.Dispose(true);
            AudioDecoder.Dispose(true);
            SubtitlesDecoder.Dispose(true);
            AudioDemuxer.Dispose();
            SubtitlesDemuxer.Dispose();
            VideoDemuxer.Dispose();
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

            Log(dump);
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

            Log("Record Start");

            recHasVideo = !VideoDecoder.Disposed && VideoDecoder.Stream != null;

            if (useRecommendedExtension)
                filename = $"{filename}.{(recHasVideo ? VideoDecoder.Stream.Demuxer.Extension : AudioDecoder.Stream.Demuxer.Extension)}";

            Recorder.Open(filename);
            if (recHasVideo)
                Log(Recorder.AddStream(VideoDecoder.Stream.AVStream).ToString());
                
            if (!AudioDecoder.Disposed && AudioDecoder.Stream != null)
                Log(Recorder.AddStream(AudioDecoder.Stream.AVStream, !AudioDecoder.OnVideoDemuxer).ToString());

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
            Log("Record Completed");
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

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [DecoderContext] {msg}"); }
    }
}