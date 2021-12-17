using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.Plugins;

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

        public bool                 Exists          { get => exists;        internal set => Set(ref _Exists, value); }
        internal bool   _Exists, exists;

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
            get => volumeSampleProvider == null ? 50 : (int) (volumeSampleProvider.Volume * 100);
            set
            {
                if (volumeSampleProvider == null) return;

                volumeSampleProvider.Volume = Math.Max(0, value / 100.0f);
                Raise(nameof(Volume));

                if (value == 0)
                {
                    Mute = true;
                    prevVolume = 0.15f;
                }
                else if (mute)
                {
                    mute = false;
                    Raise(nameof(Mute));
                }
            }
        }
        private float prevVolume = 0.5f;

        /// <summary>
        /// Audio player's mute
        /// </summary>
        public bool Mute
        {
            get => mute;
            set
            {
                if (volumeSampleProvider == null) return;

                if (value)
                {
                    prevVolume = volumeSampleProvider.Volume;
                    volumeSampleProvider.Volume = 0; // no raise
                }
                else
                {
                    volumeSampleProvider.Volume = prevVolume;
                    Raise(nameof(Volume));
                }

                Set(ref mute, value);
            }
        }
        private bool mute = false;

        /// <summary>
        /// Audio player's current device (see Master.AudioMaster.Devices for valid input names)
        /// Set to null to use AudioMaster's Device which handles all instances (Default)
        /// </summary>
        public string Device
        {
            get => _Device == null ? Master.AudioMaster.Device : _Device;
            set
            {
                if (value == null) { _Device = null; Initialize(); return; } // Let the user to change back to AudioMaster's Device

                bool found = false;
                foreach (var device in DirectSoundOut.Devices.ToList())
                    if (device.Description == value)
                    {
                        _Device = value;
                        DeviceIdNaudio = device.Guid;
                        //DeviceId = device.ModuleName;
                        found = true;

                        Initialize();
                    }

                if (!found) throw new Exception("The specified audio device doesn't exist");
            }
        }
        string _Device;
        internal Guid DeviceIdNaudio;
        #endregion

        #region Declaration
        Player player;
        Config Config => player.Config;
        DecoderContext decoder => player?.decoder;
        AudioStream     disabledStream;

        Action uiAction;

        DirectSoundOut          directSoundOut;
        WaveFormat              audioWaveFormat;
        BufferedWaveProvider    audioBuffer;
        VolumeSampleProvider    volumeSampleProvider;

        readonly object         lockerAudioPlayer = new object();
        #endregion

        public Audio(Player player)
        {
            this.player = player;

            uiAction = () =>
            {
                Exists          = Exists;
                IsOpened        = IsOpened;
                Codec           = Codec;
                BitRate         = BitRate;
                Bits            = Bits;
                Channels        = Channels;
                ChannelLayout   = ChannelLayout;
                SampleFormat    = SampleFormat;
                SampleRate      = SampleRate;
            };

            Initialize();
        }
        internal void Initialize(int sampleRate = -1)
        {
            if (Master.AudioMaster.Failed)
            {
                Config.Audio.Enabled = false;
                return;
            }

            lock (lockerAudioPlayer)
            {
                if (sampleRate != -1)
                {
                    if (SampleRate == sampleRate)
                    {
                        ClearBuffer();
                        return;
                    }

                    this.sampleRate = sampleRate;
                }

                prevVolume = volumeSampleProvider == null ? 0.5f : volumeSampleProvider.Volume;
                Dispose();
                Log($"Initializing at {SampleRate}Hz");

                audioWaveFormat = WaveFormatExtensible.CreateIeeeFloatWaveFormat(SampleRate, ChannelsOut);
                audioBuffer = new BufferedWaveProvider(audioWaveFormat);
                audioBuffer.BufferLength = 1000 * 1024;
                volumeSampleProvider= new VolumeSampleProvider(audioBuffer.ToSampleProvider());

                directSoundOut = new DirectSoundOut(_Device == null ? Master.AudioMaster.DeviceIdNaudio : DeviceIdNaudio, -30 + (int)(Config.Audio.Latency / 10000));

                directSoundOut.Init(volumeSampleProvider);
                directSoundOut.Play();
                volumeSampleProvider.Volume = prevVolume;
            }
        }
        internal void Dispose()
        {
            lock (lockerAudioPlayer)
            {
                directSoundOut?.Dispose();
                volumeSampleProvider = null;
                directSoundOut = null;
                audioBuffer = null;
                Log("Disposed");
            }
        }

        internal void AddSamples(byte[] data, bool checkSync = true)
        {
            lock (lockerAudioPlayer)
            {
                try
                {
                    if (checkSync && audioBuffer.BufferedDuration.Milliseconds > (Config.Audio.Latency / 10000) + 150) // We will see this happen on HLS streams that change source (eg. ads - like two streams playing audio in parallel)
                    {
                        Log("Resynch !!! | " + audioBuffer.BufferedBytes + " | " + audioBuffer.BufferedDuration);
                        audioBuffer.ClearBuffer();
                    }

                    audioBuffer.AddSamples(data, 0, data.Length);
                }
                catch (Exception e)
                {
                    Log(e.Message + " " + e.StackTrace);
                }
            }
        }
        internal void ClearBuffer()
        {
            lock (lockerAudioPlayer) audioBuffer?.ClearBuffer();
        }

        internal void Reset()
        {
            codec           = null;
            bitRate         = 0;
            bits            = 0;
            channels        = 0;
            channelLayout   = null;
            sampleFormat    = null;
            //sampleRate     = 0;
            exists          = false;
            isOpened        = false;

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
            
            //lastAudioStream= decoder.AudioStream;
            Initialize(decoder.AudioStream.SampleRate);
            exists         = decoder.AudioStream != null;

            if (decoder.AudioDecoder != null)
                isOpened   = decoder.AudioDecoder == null || decoder.AudioDecoder.Disposed ? false : true;

            player.UIAdd(uiAction);
        }
        internal void Enable()
        {
            if (!player.CanPlay) return;

            AudioInput suggestedInput = null;

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
            player.UI();
        }
        internal void Disable()
        {
            if (!IsOpened) return;

            disabledStream = decoder.AudioStream;
            player.AudioDecoder.Dispose(true);

            player.aFrame = null;

            Reset();
            player.UI();
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

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{player.PlayerId}] [Audio] {msg}"); }
    }
}
