using System;
using System.Collections.Generic;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer
{
    public class Video : NotifyPropertyChanged
    {
        public List<VideoInput>     Inputs          => ((IProvideVideo)decoder?.OpenedPlugin)?.VideoInputs;
        public Dictionary<string, IProvideVideo>
                                    Plugins         => decoder?.PluginsProvideVideo;
        public List<VideoStream>    Streams         => decoder?.VideoDemuxer.VideoStreams;
        

        public bool                 Exists          { get => exists;            internal set => Set(ref _Exists, value); }
        internal bool   _Exists, exists;

        /// <summary>
        /// Whether the input has video and it is configured
        /// </summary>
        public bool                 IsOpened        { get => isOpened;          internal set => Set(ref _IsOpened, value); }
        internal bool   _IsOpened, isOpened;

        public string               Codec           { get => codec;             internal set => Set(ref _Codec, value); }
        internal string _Codec, codec;

        ///// <summary>
        ///// Video bitrate (Kbps)
        ///// </summary>
        public double               BitRate         { get => bitRate;           internal set => Set(ref _BitRate, value); }
        internal double _BitRate, bitRate;

        public AspectRatio          AspectRatio     { get => aspectRatio;       internal set => Set(ref _AspectRatio, value); }
        internal AspectRatio 
                        _AspectRatio, aspectRatio;

        ///// <summary>
        ///// Total Dropped Frames
        ///// </summary>
        public int                  FramesDropped   { get => framesDropped;     internal set => Set(ref _FramesDropped, value); }
        internal int    _FramesDropped, framesDropped;

        /// <summary>
        /// Total Frames
        /// </summary>
        public int                  FramesTotal     { get => framesTotal;       internal set => Set(ref _FramesTotal, value); }
        internal int    _FramesTotal, framesTotal;

        public int                  FramesDisplayed { get => framesDisplayed;   internal set => Set(ref _FramesDisplayed, value); }
        internal int    _FramesDisplayed, framesDisplayed;

        public double               FPS             { get => fps;               internal set => Set(ref _FPS, value); }
        internal double _FPS, fps;

        /// <summary>
        /// Actual Frames rendered per second (FPS)
        /// </summary>
        public double               FPSCurrent      { get => fpsCurrent;        internal set => Set(ref _FPSCurrent, value); }
        internal double _FPSCurrent, fpsCurrent;

        public string               PixelFormat     { get => pixelFormat;       internal set => Set(ref _PixelFormat, value); }
        internal string _PixelFormat, pixelFormat;

        public int                  Width           { get => width;             internal set => Set(ref _Width, value); }
        internal int    _Width, width;

        public int                  Height          { get => height;            internal set => Set(ref _Height, value); }
        internal int    _Height, height;

        public bool                 VideoAcceleration
                                                    { get => _VideoAcceleration;internal set => Set(ref _VideoAcceleration, value); }
        internal bool   _VideoAcceleration, videoAcceleration;

        Action uiAction;
        Player player;
        DecoderContext decoder => player.decoder;
        Config Config => player.Config;

        public Video(Player player)
        {
            this.player = player;

            uiAction = () =>
            {
                Exists              = Exists;
                IsOpened            = IsOpened;
                Codec               = Codec;
                //BitRate             = BitRate;
                AspectRatio         = AspectRatio;
                //DroppedFrames       = DroppedFrames;
                FramesTotal         = FramesTotal;
                FPS                 = FPS;
                //CurrentFps          = CurrentFps;
                PixelFormat         = PixelFormat;
                Width               = Width;
                Height              = Height;
                VideoAcceleration   = VideoAcceleration;
            };
        }

        internal void Reset()
        {
            codec              = null;
            //AspectRatio        = ;
            bitRate            = 0;
            fps                = 0;
            pixelFormat        = null;
            width              = 0;
            height             = 0;
            framesTotal        = 0;
            videoAcceleration  = false;
            exists             = false;
            isOpened           = false;
            player.renderer.DisableRendering = true;

            player.UIAdd(uiAction);
        }
        internal void Refresh()
        {
            if (decoder.VideoStream == null) { Reset(); return; }

            codec       = decoder.VideoStream.Codec;
            aspectRatio = decoder.VideoStream.AspectRatio;
            fps         = decoder.VideoStream.FPS;
            pixelFormat = decoder.VideoStream.PixelFormatStr;
            width       = decoder.VideoStream.Width;
            height      = decoder.VideoStream.Height;
            exists      = decoder.VideoStream != null;
            framesTotal = decoder.VideoStream.TotalFrames;

            if (decoder.VideoDecoder != null)
            {
                player.renderer.DisableRendering = false;
                videoAcceleration  = decoder.VideoDecoder.VideoAccelerated;
                isOpened           = decoder.VideoDecoder.VideoStream != null;
            }

            player.UIAdd(uiAction);
        }

        internal void Enable()
        {
            if (!player.CanPlay || Config.Player.Usage == Usage.Audio) return;

            bool wasPlaying = player.IsPlaying;
            int curTime = decoder.GetCurTimeMs();
            player.Pause();
            decoder.OpenSuggestedVideo();
            Refresh();
            player.UI();
            decoder.Seek(curTime, false, false);
            if (wasPlaying) player.Play();
        }
        internal void Disable()
        {
            if (!IsOpened || Config.Player.Usage == Usage.Audio) return;

            bool wasPlaying = player.IsPlaying;
            player.Pause();
            player.VideoDecoder.Dispose(true);
            if (!player.AudioDecoder.OnVideoDemuxer) player.VideoDemuxer.Dispose();
            player.Subtitles.subsText = "";
            player.UIAdd(() => player.Subtitles.SubsText = player.Subtitles.SubsText);
            Refresh();
            player.UI();
            if (wasPlaying) player.Play();
        }

        public void Toggle()
        {
            Config.Video.Enabled = !Config.Video.Enabled;
        }
    }
}
