using System;
using System.Linq;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FlyleafLib.MediaPlayer
{
    public class AudioPlayer : NotifyPropertyChanged, IDisposable
    {
        #region Properties
        /// <summary>
        /// Current audio player's volume / amplifier (valid values 0 - no upper limit)
        /// </summary>
        public int Volume
        {
            get => volumeSampleProvider == null ? 50 : (int) (volumeSampleProvider.Volume * 100);
            set
            {
                volumeSampleProvider.Volume = value / 100.0f;
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
        /// Current audio player's mute
        /// </summary>
        public bool Mute
        {
            get => mute;
            set
            {
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
        /// Current audio player's device name (see Master.AudioMaster.Devices for valid input names)
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
        string                  _Device;
        internal Guid           DeviceIdNaudio;
        #endregion

        #region Declaration
        Player                  videoPlayer;
        Config                  cfg => videoPlayer.Config;

        int                     CHANNELS        = 2;        // Currently fixed
        int                     Rate            = 48000;    // Will be set from Input Format
        DirectSoundOut          player;
        WaveFormat              format;
        BufferedWaveProvider    buffer;
        VolumeSampleProvider    volumeSampleProvider;

        readonly object         locker       = new object();
        #endregion

        #region Initialize / Dispose
        public AudioPlayer(Player player) { videoPlayer = player; Initialize(); }
        public void Initialize(int rate = -1) // Rate should be saved in case of internal Initialize during device reset etc.
        {
            prevVolume = volumeSampleProvider == null ? 0.5f : volumeSampleProvider.Volume;

            lock (locker)
            {
                if (rate != -1) Rate = rate;

                Dispose();

                format = WaveFormatExtensible.CreateIeeeFloatWaveFormat(Rate, CHANNELS);
                buffer = new BufferedWaveProvider(format);
                buffer.BufferLength = 1000 * 1024;
                volumeSampleProvider= new VolumeSampleProvider(buffer.ToSampleProvider());

                if (Device == Master.AudioMaster.DefaultDeviceName)
                    player = new DirectSoundOut((int)(cfg.audio.LatencyTicks / 10000));
                else
                    player = new DirectSoundOut(_Device == null ? Master.AudioMaster.DeviceIdNaudio : DeviceIdNaudio, (int)(cfg.audio.LatencyTicks / 10000));

                player.Init(volumeSampleProvider);
                player.Play();
                volumeSampleProvider.Volume = prevVolume;
            }
        }
        public void Dispose()
        {
            player?.Dispose();
            player = null;
            buffer = null;
        }
        #endregion

        #region Buffer Fill Callback
        internal void FrameClbk(byte[] data)//, int offset, int count)
        {
            lock (locker)
            {
                try
                {
                    if (buffer.BufferedDuration.Milliseconds > (cfg.audio.LatencyTicks / 10000) + 150) // We will see this happen on HLS streams that change source (eg. ads - like two streams playing audio in parallel)
                    {
                        Log("Resynch !!! | " + buffer.BufferedBytes + " | " + buffer.BufferedDuration);
                        buffer.ClearBuffer();
                    }

                    buffer.AddSamples(data, 0, data.Length);
                }
                catch (Exception e)
                {
                    Log(e.Message + " " + e.StackTrace);
                }
            }
        }
        #endregion

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [AudioPlayer] {msg}"); }
    }
}