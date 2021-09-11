using System;
using System.Diagnostics;
using System.Linq;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FlyleafLib.MediaPlayer
{
    public class AudioPlayer : NotifyPropertyChanged
    {
        #region Properties
        /// <summary>
        /// Player's incremental unique id
        /// </summary>
        public int          PlayerId        { get; private set; }

        /// <summary>
        /// Player's configuration (set once in the constructor)
        /// </summary>
        public Config       Config          { get; protected set; }

        /// <summary>
        /// Audio player's channels (currently 2 channels supported only)
        /// </summary>
        public int          Channels        { get; } = 2;

        /// <summary>
        /// Audio player's sample rate (ffmpeg will set this)
        /// </summary>
        public int          SampleRate      { get; private set; } = -1;

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
        public string AudioDevice
        {
            get => _Device == null ? Master.AudioMaster.Device : _Device;
            set
            {
                if (value == null) { _Device = null; InitializeAudio(); return; } // Let the user to change back to AudioMaster's Device

                bool found = false;
                foreach (var device in DirectSoundOut.Devices.ToList())
                    if (device.Description == value)
                    {
                        _Device = value;
                        DeviceIdNaudio = device.Guid;
                        //DeviceId = device.ModuleName;
                        found = true;

                        InitializeAudio();
                    }

                if (!found) throw new Exception("The specified audio device doesn't exist");
            }
        }
        string                  _Device;
        internal Guid           DeviceIdNaudio;
        #endregion

        #region Declaration
        DirectSoundOut          directSoundOut;
        WaveFormat              audioWaveFormat;
        BufferedWaveProvider    audioBuffer;
        VolumeSampleProvider    volumeSampleProvider;

        readonly object         lockerAudioPlayer = new object();
        #endregion

        public AudioPlayer(Config config)
        {
            Config = config == null ? new Config() : config;
            PlayerId = Utils.GetUniqueId();
            Log("Created");
        }
        internal void InitializeAudio(int sampleRate = -1)
        {
            lock (lockerAudioPlayer)
            {
                if (sampleRate != -1)
                {
                    if (SampleRate == sampleRate) { ClearAudioBuffer(); return; }
                    SampleRate = sampleRate;
                }

                Log("Initializing");

                prevVolume = volumeSampleProvider == null ? 0.5f : volumeSampleProvider.Volume;
                DisposeAudio();

                audioWaveFormat = WaveFormatExtensible.CreateIeeeFloatWaveFormat(SampleRate, Channels);
                audioBuffer = new BufferedWaveProvider(audioWaveFormat);
                audioBuffer.BufferLength = 1000 * 1024;
                volumeSampleProvider= new VolumeSampleProvider(audioBuffer.ToSampleProvider());

                directSoundOut = new DirectSoundOut(_Device == null ? Master.AudioMaster.DeviceIdNaudio : DeviceIdNaudio, -30 + (int)(Config.Audio.Latency / 10000));

                directSoundOut.Init(volumeSampleProvider);
                directSoundOut.Play();
                volumeSampleProvider.Volume = prevVolume;
            }
        }
        internal void DisposeAudio()
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

        internal void AddAudioSamples(byte[] data, bool checkSync = true)
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
        internal void ClearAudioBuffer()
        {
            lock (lockerAudioPlayer) audioBuffer?.ClearBuffer();
        }

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{PlayerId}] [AudioPlayer] {msg}"); }
    }
}