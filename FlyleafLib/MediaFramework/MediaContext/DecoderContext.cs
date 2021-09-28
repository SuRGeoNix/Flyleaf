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

            VideoDecoder        = new VideoDecoder(Config, control, UniqueId);
            AudioDecoder        = new AudioDecoder(Config, UniqueId, VideoDecoder);
            SubtitlesDecoder    = new SubtitlesDecoder(Config, UniqueId);
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

            AudioInput audioInput = SuggestAudio();
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
                res = demuxer.Open(input.Url);

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
        public int Seek(long ms = -1, bool foreward = false)
        {
            int ret = 0;

            if (ms == -1) ms = GetCurTimeMs();

            lock (VideoDecoder.lockCodecCtx)
            lock (AudioDecoder.lockCodecCtx)
            lock (SubtitlesDecoder.lockCodecCtx)
            {
                long seekTimestamp = CalcSeekTimestamp(VideoDemuxer, ms, ref foreward);

                // Should exclude seek in queue for all "local/fast" files
                lock (VideoDemuxer.lockActions)
                if (OpenedPlugin.Name == "BitSwarm" || VideoDemuxer.SeekInQueue(seekTimestamp, foreward) != 0)
                {
                    VideoDemuxer.Interrupter.ForceInterrupt = 1;
                    OpenedPlugin.OnBuffering();
                    lock (VideoDemuxer.lockFmtCtx)
                    {    
                        if (VideoDemuxer.Disposed) { VideoDemuxer.Interrupter.ForceInterrupt = 0; return -1; }
                        ret = VideoDemuxer.Seek(seekTimestamp, foreward);
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
                AudioDemuxer.PauseOnQueueFull = true; // Pause() will cause corrupted packets which causes av_read_frame to EOF
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
        public int SeekAudio(long ms = -1, bool foreward = false)
        {
            int ret = -1;

            if (AudioDemuxer.Disposed || AudioDecoder.OnVideoDemuxer || !Config.Audio.Enabled) return ret;

            if (ms == -1) ms = GetCurTimeMs();

            lock (AudioDecoder.lockCodecCtx)
            {
                ret = AudioDemuxer.Seek(CalcSeekTimestamp(AudioDemuxer, ms, ref foreward), foreward);
                AudioDecoder.Flush();
            }

            if (VideoDecoder.IsRunning)
            {
                AudioDemuxer.Start();
                AudioDecoder.Start();
            }

            return ret;
        }
        public int SeekSubtitles(long ms = -1, bool foreward = false)
        {
            int ret = -1;

            if (SubtitlesDemuxer.Disposed || SubtitlesDecoder.OnVideoDemuxer || !Config.Subtitles.Enabled) return ret;

            if (ms == -1) ms = GetCurTimeMs();

            lock (SubtitlesDecoder.lockCodecCtx)
            {
                ret = SubtitlesDemuxer.Seek(CalcSeekTimestamp(SubtitlesDemuxer, ms, ref foreward), foreward);
                SubtitlesDecoder.Flush();
            }

            if (VideoDecoder.IsRunning)
            {
                SubtitlesDemuxer.Start();
                SubtitlesDecoder.Start();
            }

            return ret;
        }

        public int GetCurTimeMs()
        {
            return !VideoDemuxer.Disposed ? (int)(VideoDemuxer.CurTime / 10000) : (!AudioDemuxer.Disposed ? (int)(AudioDemuxer.CurTime / 10000): 0);
        }

        private long CalcSeekTimestamp(Demuxer demuxer, long ms, ref bool foreward)
        {
            long startTime = demuxer.hlsCtx == null ? demuxer.StartTime : demuxer.hlsCtx->first_timestamp * 10;
            long ticks = (ms * 10000) + startTime;

            if (demuxer.Type == MediaType.Audio) ticks -= Config.Audio.Delay + Config.Audio.Latency;
            if (demuxer.Type == MediaType.Subs ) ticks -= Config.Subtitles.Delay + (2 * 1000 * 10000); // We even want the previous subtitles

            if (ticks < startTime) 
            {
                ticks = startTime;
                foreward = true;
            }
            else if (ticks > startTime + (!VideoDemuxer.Disposed ? VideoDemuxer.Duration : AudioDemuxer.Duration) - (50 * 10000))
            {
                ticks = startTime + demuxer.Duration - (50 * 10000);
                foreward = false;
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
        public long GetVideoFrame()
        {
            int ret;
            AVPacket* packet = av_packet_alloc();
            AVFrame*  frame  = av_frame_alloc();

            lock (VideoDemuxer.lockFmtCtx)
            lock (VideoDecoder.lockCodecCtx)
            while (!VideoDemuxer.Disposed && !Interrupt && VideoDemuxer.EnabledStreams.Count != 0)
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

                switch (VideoDemuxer.FormatContext->streams[packet->stream_index]->codecpar->codec_type)
                {
                    case AVMEDIA_TYPE_AUDIO:
                        VideoDemuxer.AudioPackets.Enqueue((IntPtr)packet);
                        packet = av_packet_alloc();

                        continue;

                    case AVMEDIA_TYPE_SUBTITLE:
                        VideoDemuxer.SubtitlesPackets.Enqueue((IntPtr)packet);
                        packet = av_packet_alloc();

                        continue;

                    case AVMEDIA_TYPE_VIDEO:
                        ret = avcodec_send_packet(VideoDecoder.CodecCtx, packet);
                        av_packet_free(&packet);
                        packet = av_packet_alloc();

                        if (ret != 0) return -1;
                        
                        while (!VideoDemuxer.Disposed && !Interrupt)
                        {
                            VideoDemuxer.UpdateCurTime();
                            ret = avcodec_receive_frame(VideoDecoder.CodecCtx, frame);
                            if (ret != 0) { av_frame_unref(frame); break; }

                            frame->pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                            if (frame->pts == AV_NOPTS_VALUE) { av_frame_unref(frame); continue; }

                            if (frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                            {
                                Log($"Seek to keyframe failed [{frame->pict_type} | {frame->key_frame}]");
                                av_frame_unref(frame);
                                continue;
                            }

                            VideoDecoder.StartTime = (long)(frame->pts * VideoStream.Timebase) - VideoDemuxer.StartTime;
                            VideoDecoder.keyFrameRequired = false;

                            VideoFrame mFrame = VideoDecoder.ProcessVideoFrame(frame);
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
            VideoDecoder.DisposeVA();
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
        Remuxer Recorder = new Remuxer();
        public bool IsRecording
        {
            get => VideoDemuxer.IsRecording || AudioDemuxer.IsRecording;
        }
        int oldMaxAudioFrames;
        public void StartRecording(ref string filename, bool useRecommendedExtension = true)
        {
            oldMaxAudioFrames = -1;

            if (AudioStream == null || AudioStream.Demuxer.Type == MediaType.Video)
            {
                VideoDemuxer.StartRecording(ref filename, useRecommendedExtension);
                return;
            }

            if (IsRecording) StopRecording();

            Log("Record Start");
            VideoDemuxer.RecordingCompleted += RecordingCompleted;

            if (useRecommendedExtension)
                filename = $"{filename}.{VideoDemuxer.Extension}";

            Recorder.Open(filename);
            for(int i=0; i<VideoDemuxer.EnabledStreams.Count; i++)
                Log(Recorder.AddStream(VideoDemuxer.AVStreamToStream[VideoDemuxer.EnabledStreams[i]].AVStream).ToString());

            for(int i=0; i<AudioDemuxer.EnabledStreams.Count; i++)
                Log(Recorder.AddStream(AudioDemuxer.AVStreamToStream[AudioDemuxer.EnabledStreams[i]].AVStream, true).ToString());

            if (!Recorder.HasStreams || Recorder.WriteHeader() != 0) return; //throw new Exception("Invalid remuxer configuration");

            // Check also buffering and possible Diff of first audio/video timestamp to remuxer to ensure sync between each other (shouldn't be more than 30-50ms)
            oldMaxAudioFrames = Config.Decoder.MaxAudioFrames;
            long timestamp = Math.Max(VideoDemuxer.CurTime + VideoDemuxer.BufferedDuration, AudioDemuxer.CurTime + AudioDemuxer.BufferedDuration) + 1500 * 10000;
            Config.Decoder.MaxAudioFrames = Config.Decoder.MaxVideoFrames;

            VideoDemuxer.StartRecording(Recorder, timestamp);
            AudioDemuxer.StartRecording(Recorder, timestamp);
        }
        public void StopRecording()
        {
            if (oldMaxAudioFrames != -1) Config.Decoder.MaxAudioFrames = oldMaxAudioFrames;

            VideoDemuxer.RecordingCompleted -= RecordingCompleted;
            VideoDemuxer.StopRecording();
            AudioDemuxer.StopRecording();
            Recorder.Dispose();
            oldMaxAudioFrames = -1;
            Log("Record Completed");
        }
        private void RecordingCompleted(object sender, EventArgs e) { StopRecording(); }
        #endregion

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [DecoderContext] {msg}"); }
    }
}