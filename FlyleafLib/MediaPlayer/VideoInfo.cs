using System;
using System.Collections.Generic;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer
{
    public class VideoInfo : NotifyPropertyChanged
    {
        Player player;
        DecoderContext decoder => player?.decoder;
        public VideoInfo(Player player)
        {
            this.player = player;
        }

        public List<VideoInput>     Inputs  => ((IProvideVideo)decoder?.OpenedPlugin)?.VideoInputs;
        public List<VideoStream>    Streams => decoder?.VideoDemuxer.VideoStreams;

        public bool                 Exists          { get => _Exists;           internal set => Set(ref _Exists, value); }
        bool _Exists;

        /// <summary>
        /// Whether the input has video and it is configured
        /// </summary>
        public bool                 IsOpened        { get => _IsOpened;         internal set => Set(ref _IsOpened, value); }
        bool _IsOpened;

        public string               Codec           { get => _Codec;            internal set => Set(ref _Codec, value); }
        string _Codec;

        ///// <summary>
        ///// Video bitrate (Kbps)
        ///// </summary>
        public double               BitRate         { get => _BitRate;          internal set => Set(ref _BitRate, value); }
        double _BitRate;

        public AspectRatio          AspectRatio     { get => _AspectRatio;      internal set => Set(ref _AspectRatio, value); }
        AspectRatio _AspectRatio;

        ///// <summary>
        ///// Total Dropped Frames
        ///// </summary>
        public int                  DroppedFrames   { get => _DroppedFrames;    internal set => Set(ref _DroppedFrames, value); }
        int _DroppedFrames;

        public double               Fps             { get => _Fps;              internal set => Set(ref _Fps, value); }
        double _Fps;

        /// <summary>
        /// Actual Frames rendered per second (FPS)
        /// </summary>
        public double               CurrentFps      { get => _CurrentFps;       internal set => Set(ref _CurrentFps, value); }
        double _CurrentFps;

        public string               PixelFormat     { get => _PixelFormat;      internal set => Set(ref _PixelFormat, value); }
        string _PixelFormat;

        public int                  Width           { get => _Width;            internal set => Set(ref _Width, value); }
        int _Width;

        public int                  Height          { get => _Height;           internal set => Set(ref _Height, value); }
        int _Height;

        public bool                 VideoAcceleration
                                                    { get => _VideoAcceleration;internal set => Set(ref _VideoAcceleration, value); }
        bool _VideoAcceleration;

        public void Refresh()
        {
            if (decoder.VideoStream == null) { Reset(); return; }

            Codec               = decoder.VideoStream.Codec;
            AspectRatio         = decoder.VideoStream.AspectRatio;
            Fps                 = decoder.VideoStream.Fps;
            PixelFormat         = decoder.VideoStream.PixelFormatStr;
            Width               = decoder.VideoStream.Width;
            Height              = decoder.VideoStream.Height;
            Exists              = decoder.VideoStream != null;

            if (decoder.VideoDecoder == null) return;
            player.renderer.DisableRendering = false;
            VideoAcceleration   = decoder.VideoDecoder.VideoAccelerated;
            IsOpened            = decoder.VideoDecoder.VideoStream != null;
        }

        public void Reset()
        {
            Codec               = null;
            //AspectRatio         = ;
            BitRate             = 0;
            Fps                 = 0;
            PixelFormat         = null;
            Width               = 0;
            Height              = 0;
            VideoAcceleration   = false;
            Exists              = false;
            IsOpened            = false;
            player.renderer.DisableRendering = true;
        }


    }
}
