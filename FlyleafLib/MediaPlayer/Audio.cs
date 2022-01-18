using System;
using System.Collections.Generic;

using Vortice.Multimedia;
using Vortice.XAudio2;

using static Vortice.XAudio2.XAudio2;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

using static FlyleafLib.Logger;

namespace FlyleafLib.MediaPlayer
{
    public class Audio : NotifyPropertyChanged
    {
        #region Properties
        public List<AudioInput>     Inputs          => ((IProvideAudio)decoder?.OpenedPlugin)?.AudioInputs;
        public Dictionary<string, IProvideAudio>
                                    Plugins         => decoder?.PluginsProvideAudio;
        public List<AudioStream>    Streams         => decoder?.VideoDemuxer.AudioStreams;
        public List<AudioStream>    ExternalStreams => decoder?.AudioDemuxer.AudioStreams;

        /// <summary>
        /// Whether the input has audio and it is configured
        /// </summary>
        public bool                 IsOpened        { get => isOpened;      internal set => Set(ref _IsOpened, value); }
        internal bool   _IsOpened, isOpened;

        public string               Codec           { get => codec;         internal set => Set(ref _Codec, value); }
        internal string _Codec, codec;

        ///// <summary>
        ///// Audio bitrate (Kbps)
        ///// </summary>
        public double               BitRate         { get => bitRate;       internal set => Set(ref _BitRate, value); }
        internal double _BitRate, bitRate;

        public int                  Bits            { get => bits;          internal set => Set(ref _Bits, value); }
        internal int    _Bits, bits;

        public int                  Channels        { get => channels;      internal set => Set(ref _Channels, value); }
        internal int    _Channels, channels;

        /// <summary>
        /// Audio player's channels out (currently 2 channels supported only)
        /// </summary>
        public int                  ChannelsOut     { get; } = 2;

        public string               ChannelLayout   { get => channelLayout; internal set => Set(ref _ChannelLayout, value); }
        internal string _ChannelLayout, channelLayout;

        public string               SampleFormat    { get => sampleFormat;  internal set => Set(ref _SampleFormat, value); }
        internal string _SampleFormat, sampleFormat;

        /// <summary>
        /// Audio sample rate (in/out)
        /// </summary>
        public int                  SampleRate      { get => sampleRate;    internal set => Set(ref _SampleRate, value); }
        internal int    _SampleRate = 48000, sampleRate = 48000;

        /// <summary>
        /// Audio player's volume / amplifier (valid values 0 - no upper limit)
        /// </summary>
        public int Volume
        {
            get
            {
                lock (locker)
                    return sourceVoice == null || Mute ? _Volume : (int) ((decimal)sourceVoice.Volume * 100);
            }
            set
            {
                if (value > Config.Player.VolumeMax || value < 0)
                    return;

                if (value == 0)
                    Mute = true;
                else if (Mute)
                {
                    _Volume = value;
                    Mute = false;
                }
                else
                {
                    lock (locker)
                        if (sourceVoice != null)
                            sourceVoice.Volume = Math.Max(0, value / 100.0f);
                }

                Set(ref _Volume, value, false);
            }
        }
        int _Volume;

        /// <summary>
        /// Audio player's mute
        /// </summary>
        public bool Mute
        {
            get => mute;
            set
            {
                if (value)
                    sourceVoice.Volume = 0;
                else
                    sourceVoice.Volume = _Volume / 100.0f;

                Set(ref mute, value, false);
            }
        }
        private bool mute = false;

        /// <summary>
        /// <para>Audio player's current device (available devices can be found on Engine.Audio.Devices)/></para>
        /// </summary>
        public string Device
        {
            get => _Device;
            set
            {
                if (value == null || _Device == value)
                    return; 

                _Device     = value;
                _DeviceId   = Engine.Audio.GetDeviceId(value);

                Initialize();

                Utils.UI(() => Raise(nameof(Device)));
            }
        }
        internal string _Device = Engine.Audio.DefaultDeviceName;
        internal void RaiseDevice() { Utils.UI(() => Raise(nameof(Device))); } // Required for Selected Items on the Devices observation list (as we clear it everytime)

        public string DeviceId
        {
            get => _DeviceId;
            set
            {
                _DeviceId   = value;
                _Device     = Engine.Audio.GetDeviceName(value);

                Initialize();

                Utils.UI(() => Raise(nameof(DeviceId)));
            }
        }
        internal string _DeviceId = Engine.Audio.DefaultDeviceId;

        public int BuffersQueued {
            get
            {                
                lock (locker)
                {
                    if (sourceVoice == null)
                        return 0;

                    return sourceVoice.State.BuffersQueued;
                }
            }
        }
        #endregion

        #region Declaration
        Player                  player;
        Config                  Config => player.Config;
        DecoderContext          decoder => player?.decoder;
        AudioStream             disabledStream;

        Action                  uiAction;
        readonly object         locker = new object();

        IXAudio2                xaudio2;
        internal IXAudio2MasteringVoice  masteringVoice;
        IXAudio2SourceVoice     sourceVoice;
        WaveFormat              waveFormat = new WaveFormat(48000, 16, 2);
        #endregion
        public Audio(Player player)
        {
            this.player = player;

            uiAction = () =>
            {
                IsOpened        = IsOpened;
                Codec           = Codec;
                BitRate         = BitRate;
                Bits            = Bits;
                Channels        = Channels;
                ChannelLayout   = ChannelLayout;
                SampleFormat    = SampleFormat;
                SampleRate      = SampleRate;
            };

            Volume = Config.Player.VolumeMax / 2;
            Initialize();
        }

        internal void Initialize(int sampleRate = -1)
        {
            if (Engine.Audio.Failed)
            {
                Config.Audio.Enabled = false;
                return;
            }

            if (SampleRate == sampleRate)
                return;

            if (sampleRate != -1)
                this.sampleRate = sampleRate;

            lock (locker)
            {
                player.Log.Info($"Initialiazing audio ({Device} @ {SampleRate}Hz)");

                Dispose();

                xaudio2         = XAudio2Create();
                masteringVoice  = xaudio2.CreateMasteringVoice(0, 0, AudioStreamCategory.GameEffects, _Device == Engine.Audio.DefaultDeviceName ? null : Engine.Audio.GetDeviceId(_Device));
                sourceVoice     = xaudio2.CreateSourceVoice(waveFormat, true);
                sourceVoice.SetSourceSampleRate(SampleRate);
                sourceVoice.Start();

                masteringVoice.Volume = Config.Player.VolumeMax / 100.0f;
                Volume = _Volume;
            }
        }
        internal void Dispose()
        {
            lock (locker)
            {
                if (xaudio2 == null)
                    return;

                xaudio2.        Dispose();
                sourceVoice?.   Dispose();
                masteringVoice?.Dispose();
                xaudio2         = null;
                sourceVoice     = null;
                masteringVoice  = null;
            }
        }
        internal void AddSamples(AudioFrame aFrame)
        {
            try
            {
                sourceVoice.SubmitSourceBuffer(new AudioBuffer(aFrame.dataPtr, aFrame.dataLen));
            } catch (Exception e) // Happens on audio device changed/removed
            {
                if (CanDebug) player.Log.Debug($"[Audio] Add samples failed ({e.Message})");
            }
        }
        internal void ClearBuffer()
        {
            lock (locker)
                sourceVoice?.FlushSourceBuffers();
        }

        internal void Reset(bool andDisabledStream = true)
        {
            codec           = null;
            bitRate         = 0;
            bits            = 0;
            channels        = 0;
            channelLayout   = null;
            sampleRate      = 0;
            sampleFormat    = null;
            isOpened        = false;

            if (andDisabledStream)
                disabledStream  = null;

            ClearBuffer();
            player.UIAdd(uiAction);
        }
        internal void Refresh()
        {
            if (decoder.AudioStream == null) { Reset(); return; }

            codec          = decoder.AudioStream.Codec;
            bits           = decoder.AudioStream.Bits;
            channels       = decoder.AudioStream.Channels;
            channelLayout  = decoder.AudioStream.ChannelLayoutStr;
            sampleFormat   = decoder.AudioStream.SampleFormatStr;
            isOpened       =!decoder.AudioDecoder.Disposed;

            Initialize(decoder.AudioStream.SampleRate);
            player.UIAdd(uiAction);
        }
        internal void Enable()
        {
            if (disabledStream == null || (player.VideoDemuxer.Disposed && player.AudioDemuxer.Disposed))
                return;

            AudioInput suggestedInput = null;
            bool wasPlaying = player.IsPlaying;

            if (disabledStream == null)
                decoder.SuggestAudio(out disabledStream, out suggestedInput, player.VideoDemuxer.AudioStreams);

            if (disabledStream != null)
            {
                if (disabledStream.AudioInput != null)
                    player.Open(disabledStream.AudioInput);
                else
                    player.Open(disabledStream);
            }
            else if (suggestedInput != null)
                player.Open(suggestedInput);

            Refresh();
            player.UIAll();

            if (wasPlaying || Config.Player.AutoPlay)
                player.Play();
        }
        internal void Disable()
        {
            if (!IsOpened) return;

            disabledStream = decoder.AudioStream;
            player.AudioDecoder.Dispose(true);

            player.aFrame = null;

            if (!player.Video.IsOpened)
            {
                player.canPlay = false;
                player.UIAdd(() => player.CanPlay = player.CanPlay);
            }

            Reset(false);
            player.UIAll();
        }

        public void DelayAdd()
        {
            Config.Audio.Delay += Config.Player.AudioDelayOffset;
        }
        public void DelayAdd2()
        {
            Config.Audio.Delay += Config.Player.AudioDelayOffset2;
        }
        public void DelayRemove()
        {
            Config.Audio.Delay -= Config.Player.AudioDelayOffset;
        }
        public void DelayRemove2()
        {
            Config.Audio.Delay -= Config.Player.AudioDelayOffset2;
        }
        public void Toggle()
        {
            Config.Audio.Enabled = !Config.Audio.Enabled;
        }
        public void ToggleMute()
        {
            Mute = !Mute;
        }
        public void VolumeUp()
        {
            if (Volume == Config.Player.VolumeMax) return;
            Volume = Math.Min(Volume + Config.Player.VolumeOffset, Config.Player.VolumeMax);
        }
        public void VolumeDown()
        {
            if (Volume == 0) return;
            Volume = Math.Max(Volume - Config.Player.VolumeOffset, 0);
        }
    }
}
