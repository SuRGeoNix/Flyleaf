using System;
using System.Collections.Generic;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer
{
    public class AudioInfo : NotifyPropertyChanged
    {
        Player player;
        DecoderContext decoder => player?.decoder;

        public AudioInfo(Player player)
        {
            this.player = player;
        }

        public List<AudioInput>     Inputs          => ((IProvideAudio)decoder?.OpenedPlugin)?.AudioInputs; //   decoder != null && decoder.OpenedPlugin != null && decoder.OpenedPlugin is IProvideAudio ? ((IProvideAudio)decoder.OpenedPlugin).AudioInputs : null;
        public List<AudioStream>    Streams         => decoder?.VideoDemuxer.AudioStreams;
        public List<AudioStream>    ExternalStreams => decoder?.AudioDemuxer.AudioStreams;

        public bool                 Exists          { get => _Exists;       internal set => Set(ref _Exists, value); }
        bool _Exists;

        /// <summary>
        /// Whether the input has audio and it is configured
        /// </summary>
        public bool                 IsOpened        { get => _IsOpened;     internal set => Set(ref _IsOpened, value); }
        bool _IsOpened;

        public string               Codec           { get => _Codec;        internal set => Set(ref _Codec, value); }
        string _Codec;

        ///// <summary>
        ///// Audio bitrate (Kbps)
        ///// </summary>
        public double               BitRate         { get => _BitRate;      internal set => Set(ref _BitRate, value); }
        double _BitRate;

        public int                  Bits            { get => _Bits;         internal set => Set(ref _Bits, value); }
        int _Bits;

        public int                  Channels        { get => _Channels;     internal set => Set(ref _Channels, value); }
        int _Channels;

        public string               ChannelLayout   { get => _ChannelLayout;internal set => Set(ref _ChannelLayout, value); }
        string _ChannelLayout;

        public string               SampleFormat    { get => _SampleFormat; internal set => Set(ref _SampleFormat, value); }
        string _SampleFormat;

        public int                  SampleRate      { get => _SampleRate;   internal set => Set(ref _SampleRate, value); }
        int _SampleRate;

        public void Refresh()
        {
            if (decoder.AudioStream == null) { Reset(); return; }

            Codec           = decoder.AudioStream.Codec;
            Bits            = decoder.AudioStream.Bits;
            Channels        = decoder.AudioStream.Channels;
            ChannelLayout   = decoder.AudioStream.ChannelLayoutStr;
            SampleFormat    = decoder.AudioStream.SampleFormatStr;
            SampleRate      = decoder.AudioStream.SampleRate;
            Exists          = decoder.AudioStream != null;

            if (decoder.AudioDecoder == null) return;
            IsOpened        = decoder.AudioDecoder.AudioStream != null;
        }

        public void Reset()
        {
            Codec           = null;
            BitRate         = 0;
            Bits            = 0;
            Channels        = 0;
            ChannelLayout   = null;
            SampleFormat    = null;
            SampleRate      = 0;
            Exists          = false;
            IsOpened        = false;
        }
    }
}
