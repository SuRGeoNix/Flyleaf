using System;
using System.Collections.Generic;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer
{
    public class SubtitlesInfo : NotifyPropertyChanged
    {
        Player player;
        DecoderContext decoder => player?.decoder;
        public SubtitlesInfo(Player player)
        {
            this.player = player;
        }

        public List<SubtitlesInput>     Inputs          => ((IProvideSubtitles)decoder?.OpenedSubtitlesPlugin)?.SubtitlesInputs;
        public List<SubtitlesStream>    Streams         => decoder?.VideoDemuxer.SubtitlesStreams;
        public List<SubtitlesStream>    ExternalStreams => decoder?.SubtitlesDemuxer.SubtitlesStreams;

        public bool                     Exists          { get => _Exists;       internal set => Set(ref _Exists, value); }
        bool _Exists;

        /// <summary>
        /// Whether the input has subtitles and it is configured
        /// </summary>
        public bool                     IsOpened        { get => _IsOpened;     internal set => Set(ref _IsOpened, value); }
        bool _IsOpened;

        public string                   Codec           { get => _Codec;        internal set => Set(ref _Codec, value); }
        string _Codec;

        public double                   BitRate         { get => _BitRate;      internal set => Set(ref _BitRate, value); }
        double _BitRate;

        public string                   SubsText        { get => _SubsText;     internal set => Set(ref _SubsText,  value); }
        string _SubsText;

        public void Refresh()
        {
            if (decoder.SubtitlesStream == null) { Reset(); return; }

            Codec       = decoder.SubtitlesStream.Codec;
            Exists      = decoder.SubtitlesStream != null;

            if (decoder.SubtitlesDecoder == null) return;
            IsOpened    = decoder.SubtitlesDecoder.SubtitlesStream != null;
        }

        public void Reset()
        {
            BitRate     = 0;
            Codec       = null;
            Exists      = false;
            IsOpened    = false;
            SubsText    = "";
        }
    }
}
