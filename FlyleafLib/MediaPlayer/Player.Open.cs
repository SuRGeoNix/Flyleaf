using System;
using System.IO;
using System.Threading.Tasks;

using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.Plugins;

using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;
using static FlyleafLib.Utils;

namespace FlyleafLib.MediaPlayer
{
    unsafe partial class Player
    {
        /// <summary>
        /// Fires on open completed of new media input (success or failure)
        /// </summary>
        public event EventHandler<OpenCompletedArgs> OpenCompleted;
        protected virtual void OnOpenCompleted(OpenCompletedArgs e) { OnOpenCompleted(e.Type, e.Input, e.Error); }
        protected virtual void OnOpenCompleted(MediaType type, InputBase input, string error) { OpenCompleted?.Invoke(this, new OpenCompletedArgs(type, input, error)); }

        /// <summary>
        /// Fires on open completed of an existing media input (success or failure)
        /// </summary>
        public event EventHandler<OpenInputCompletedArgs> OpenInputCompleted;
        protected virtual void OnOpenInputCompleted(OpenInputCompletedArgs e) { OnOpenInputCompleted(e.Type, e.Input, e.OldInput, e.Error, e.IsUserInput); }
        protected virtual void OnOpenInputCompleted(MediaType type, InputBase input, InputBase oldInput, string error, bool isUserInput) { OpenInputCompleted?.Invoke(this, new OpenInputCompletedArgs(type, input, oldInput, error, isUserInput)); }

        /// <summary>
        /// Fires on open completed of an existing media stream (success or failure)
        /// </summary>
        public event EventHandler<OpenStreamCompletedArgs> OpenStreamCompleted;
        protected virtual void OnOpenStreamCompleted(OpenStreamCompletedArgs e) { OnOpenStreamCompleted(e.Type, e.Stream, e.OldStream, e.Error); }
        protected virtual void OnOpenStreamCompleted(MediaType type, StreamBase stream, StreamBase oldStream, string error) { OpenStreamCompleted?.Invoke(this, new OpenStreamCompletedArgs(type, stream, oldStream, error)); }

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
                VideoDemuxer.DisableReversePlayback();
                ReversePlayback = false;
                status = Status.Opening;
                UI(() => Status = Status);

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

                    // TBR: Video Fails try Audio Input (this is wrong, works on every failure. Should do that only for No Video Stream error and maybe back to the decoder with general Open instead of OpenAV?)
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
                        decoder.PauseDecoders();
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

            long delay = DateTime.UtcNow.Ticks;
            long fromEnd = (Duration - CurTime);

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

            if (resync)
            {
                // Wait for at least on package before seek to update the HLS context first_time
                if (stream.Demuxer.HLSPlaylist != null)
                {
                    while (stream.Demuxer.IsRunning && stream.Demuxer.GetPacketsPtr(stream.Type).Count < 3)
                        System.Threading.Thread.Sleep(20);

                    ReSync(stream, ((Duration - fromEnd) - (DateTime.UtcNow.Ticks - delay))/ 10000);
                }
                else
                    ReSync(stream, CurTime / 10000, true);
            }
            else
                isVideoSwitch = false;

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

        internal void ReSync(StreamBase stream, long syncMs = -1, bool accurate = false)
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

                if (accurate && Video.IsOpened)
                {
                    decoder.PauseDecoders();
                    decoder.Seek(syncMs, false, false);
                    decoder.GetVideoFrame(syncMs * 10000);
                }
                else
                    decoder.Seek(syncMs, false, false);

                aFrame = null;
                isAudioSwitch = false;
                isVideoSwitch = false;
                sFrame = null;
                isSubsSwitch = false;

                if (!IsPlaying)
                {
                    decoder.PauseDecoders();
                    decoder.GetVideoFrame();
                    ShowOneFrame();
                }
                else
                {
                    Subtitles.subsText = "";
                    if (Subtitles._SubsText != "")
                        UI(() => Subtitles.SubsText = Subtitles.SubsText);
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
                    Subtitles.subsText = "";
                    if (Subtitles._SubsText != "")
                        UI(() => Subtitles.SubsText = Subtitles.SubsText);
                    isSubsSwitch = false;
                }

                if (IsPlaying)
                {
                    stream.Demuxer.Start();
                    decoder.GetDecoderPtr(stream.Type).Start();
                }
            }    
        }

        #region Decoder Events
        private void Decoder_AudioCodecChanged(DecoderBase x)
        {
            //Audio.Initialize(AudioDecoder.CodecCtx->sample_rate);
        }
        private void Decoder_VideoCodecChanged(DecoderBase x)
        {
            Video.videoAcceleration = VideoDecoder.VideoAccelerated;
            UI(() => { Video.VideoAcceleration = Video.VideoAcceleration; });
        }

        private void Decoder_AudioStreamOpened(object sender, AudioStreamOpenedArgs e)
        {
            Config.Audio.SetDelay(0);
            Audio.Refresh();
            canPlay = Video.IsOpened || Audio.IsOpened ? true : false;

            UIAdd(() => CanPlay = CanPlay);
            UI();
            OnOpenStreamCompleted(MediaType.Audio, e.Stream, e.OldStream, e.Error);
        }
        private void Decoder_VideoStreamOpened(object sender, VideoStreamOpenedArgs e)
        {
            Video.Refresh();
            canPlay = Video.IsOpened || Audio.IsOpened ? true : false;

            UIAdd(() => CanPlay = CanPlay);
            UI();
            OnOpenStreamCompleted(MediaType.Video, e.Stream, e.OldStream, e.Error);
        }
        private void Decoder_SubtitlesStreamOpened(object sender, SubtitlesStreamOpenedArgs e)
        {
            Config.Subtitles.SetDelay(0);
            Subtitles.Refresh();

            UI();
            OnOpenStreamCompleted(MediaType.Subs, e.Stream, e.OldStream, e.Error);
        }

        private void Decoder_AudioInputOpened(object sender, AudioInputOpenedArgs e)
        {
            if (decoder.VideoStream == null)
            {
                if (e.Success)
                {
                    if (e.Input != null && e.Input.InputData != null)
                        title = e.Input.InputData.Title;
                    
                    var curDemuxer = !VideoDemuxer.Disposed ? VideoDemuxer : AudioDemuxer;
                    duration    = curDemuxer.Duration;
                    isLive      = curDemuxer.IsLive;
                    isPlaylist  = decoder.OpenedPlugin.IsPlaylist;

                    UIAdd(() =>
                    {
                        Title       = Title;
                        Duration    = Duration;
                        IsLive      = IsLive;
                        IsPlaylist  = IsPlaylist;
                    });
                }
                else
                {
                    if (!CanPlay)
                    {
                        status = Status.Failed;
                        UIAdd(() => Status = Status);
                    }   

                    ResetMe();
                }
            }

            UI();

            if (CanPlay && Config.Player.AutoPlay)
                Play();

            if (e.IsUserInput)
                OnOpenCompleted(new OpenInputCompletedArgs(MediaType.Audio, e.Input, e.OldInput, e.Error, e.IsUserInput));
            else
                OnOpenInputCompleted(MediaType.Audio, e.Input, e.OldInput, e.Error, e.IsUserInput);
        }
        private void Decoder_VideoInputOpened(object sender, VideoInputOpenedArgs e)
        {
            if (e.Success)
            {
                if (e.Input != null && e.Input.InputData != null)
                    title = e.Input.InputData.Title;

                duration    = VideoDemuxer.Duration;
                isLive      = VideoDemuxer.IsLive;
                isPlaylist  = decoder.OpenedPlugin.IsPlaylist;

                UIAdd(() =>
                {
                    Title       = Title;
                    Duration    = Duration;
                    IsLive      = IsLive;
                    IsPlaylist  = IsPlaylist;
                });
            }
            else
            {
                if (!CanPlay)
                {
                    status = Status.Failed;
                    UIAdd(() => Status = Status);
                }

                ResetMe();
            }

            UI();

            if (CanPlay && Config.Player.AutoPlay)
                Play();

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
    }

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
}
