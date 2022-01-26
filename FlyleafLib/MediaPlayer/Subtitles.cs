using System;
using System.Collections.Generic;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

namespace FlyleafLib.MediaPlayer
{
    public class Subtitles : NotifyPropertyChanged
    {
        public List<SubtitlesInput>     Inputs          => decoder?.UserInput?.SubtitlesInputs;
        public Dictionary<string, IProvideSubtitles>
                                        Plugins         => decoder?.PluginsProvideSubtitles;
        public List<SubtitlesStream>    Streams         => decoder?.VideoDemuxer.SubtitlesStreams;
        public List<SubtitlesStream>    ExternalStreams => decoder?.SubtitlesDemuxer.SubtitlesStreams;


        /// <summary>
        /// Whether the input has subtitles and it is configured
        /// </summary>
        public bool                     IsOpened        { get => isOpened;     internal set => Set(ref _IsOpened, value); }
        internal bool   _IsOpened, isOpened;

        public string                   Codec           { get => codec;        internal set => Set(ref _Codec, value); }
        internal string _Codec, codec;

        /// <summary>
        /// Subtitles Text (updates dynamically while playing based on the duration that it should be displayed)
        /// </summary>
        public string                   SubsText        { get => subsText;     internal set => Set(ref _SubsText,  value); }
        internal string _SubsText = "", subsText = "";

        Action uiAction;
        Player player;
        DecoderContext decoder => player?.decoder;
        Config Config => player.Config;
        SubtitlesStream disabledStream;

        public Subtitles(Player player)
        {
            this.player = player;

            uiAction = () =>
            {
                IsOpened    = IsOpened;
                Codec       = Codec;
                SubsText    = SubsText;
            };
        }
        internal void Reset()
        {
            codec       = null;
            isOpened    = false;
            subsText    = "";
            disabledStream
                        = null;

            player.UIAdd(uiAction);
        }
        internal void Refresh()
        {
            if (decoder.SubtitlesStream == null) { Reset(); return; }

            codec       = decoder.SubtitlesStream.Codec;
            isOpened    =!decoder.SubtitlesDecoder.Disposed;
            //disabledStream = decoder.SubtitlesStream;

            player.UIAdd(uiAction);
        }
        internal void Enable()
        {
            if (!player.CanPlay || Config.Player.Usage != Usage.AVS) return;

            SubtitlesInput suggestedInput = null;

            if (disabledStream == null)
                decoder.SuggestSubtitles(out disabledStream, out suggestedInput, player.VideoDemuxer.SubtitlesStreams);

            if (disabledStream != null)
            {
                if (disabledStream.SubtitlesInput != null)
                    player.Open(disabledStream.SubtitlesInput);
                else
                    player.Open(disabledStream);
            }
            else if (suggestedInput != null)
                player.Open(suggestedInput);

            Refresh();
            player.UIAll();
        }
        internal void Disable()
        {
            if (!IsOpened || Config.Player.Usage != Usage.AVS) return;

            disabledStream = decoder.SubtitlesStream;
            player.SubtitlesDecoder.Dispose(true);

            player.sFrame = null;
            Reset();
            player.UIAll();
        }

        public void DelayRemove()
        {
            Config.Subtitles.Delay -= Config.Player.SubtitlesDelayOffset;
        }
        public void DelayAdd()
        {
            Config.Subtitles.Delay += Config.Player.SubtitlesDelayOffset;
        }
        public void DelayRemove2()
        {
            Config.Subtitles.Delay -= Config.Player.SubtitlesDelayOffset2;
        }
        public void DelayAdd2()
        {
            Config.Subtitles.Delay += Config.Player.SubtitlesDelayOffset2;
        }
        public void Toggle()
        {
            Config.Subtitles.Enabled = !Config.Subtitles.Enabled;
        }
    }
}
