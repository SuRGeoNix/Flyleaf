using System;
using System.Diagnostics;

using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace FlyleafLib.MediaPlayer
{
    public class AudioPlayer : NotifyPropertyChanged, IMMNotificationClient, IAudioSessionEventsHandler
    {
        #region Declaration
        public bool     isPlaying   { get { return player.PlaybackState == PlaybackState.Playing ? true : false; } }
        public int      Volume      { get { return GetVolume();  } set { SetVolume(value); } }
        public bool     Mute        { get { return GetMute(); } set { SetMute(value); } }

        public const int        NAUDIO_DELAY_MS = 200;      // Latency (buffer before play), consider same audio delay for video player in order to be sync
        int                     CHANNELS        = 2;        // Currently fixed
        int                     Rate            = 48000;    // Will be set from Input Format
        DirectSoundOut          player;
        WaveFormat              format;
        BufferedWaveProvider    buffer;

        MMDeviceEnumerator      deviceEnum;
        MMDevice                device;     // Current Master Audio
        AudioSessionControl     session;    // Current App Audio Session

        int                                 processId;
        readonly object                     locker       = new object();
        #endregion

        #region Initialize
        public AudioPlayer()
        {
            deviceEnum = new MMDeviceEnumerator();
            deviceEnum.RegisterEndpointNotificationCallback(this);
            processId = Process.GetCurrentProcess().Id;
            
            Initialize();
        }
        public void Initialize(int rate = -1) // Rate should be saved in case of internal Initialize during device reset etc.
        {
            lock (locker)
            {
                if (rate != -1) Rate = rate;

                Dispose();

                // Initialize
                format = WaveFormatExtensible.CreateIeeeFloatWaveFormat(Rate, CHANNELS);
                buffer = new BufferedWaveProvider(format);
                buffer.BufferLength = 1000 * 1024;
                player = new DirectSoundOut(NAUDIO_DELAY_MS);
                player.Init(buffer);
                player.Volume = 1; // Currently we change only Master volume to achieve constants change levels
                player.Play();

                device = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;

                for (int i = 0; i < device.AudioSessionManager.Sessions.Count; i++)
                {
                    if (processId == device.AudioSessionManager.Sessions[i].GetProcessID) // && !deviceInit.AudioSessionManager.Sessions[i].IsSystemSoundsSession)
                    {
                        session = device.AudioSessionManager.Sessions[i];
                        player.Volume = session.SimpleAudioVolume.Volume;
                        session.RegisterEventClient(this);
                    }
                }

                Raise(nameof(Volume));
                Raise(nameof(Mute));
            }
        }
        #endregion

        #region Main Implementation
        public void Play()  { lock (locker) { buffer.ClearBuffer(); player.Play(); } }
        public void Pause() { lock (locker) { player.Pause(); buffer.ClearBuffer(); } }
        public void Stop()  { lock (locker) { player.Stop(); Initialize(); } }
        public void Dispose()
        {
            lock (locker)
            {
                // Dispose | UnRegister 
                if (device  != null) device.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
                if (session != null) session.UnRegisterEventClient(this);
                if (player  != null) player.Dispose();
                if (buffer  != null) buffer.ClearBuffer();
            } 
        }

        public void FrameClbk(byte[] buffer)//, int offset, int count)
        {
            try
            {
                if (player.PlaybackState != PlaybackState.Playing) return;

                if (this.buffer.BufferedDuration.Milliseconds > NAUDIO_DELAY_MS + 50)
                {
                    // We will see this happen on HLS streams that change source (eg. ads - like two streams playing audio in parallel)
                    Log("Resynch !!! | " + this.buffer.BufferedBytes + " | " + this.buffer.BufferedDuration);
                    this.buffer.ClearBuffer();
                }

                this.buffer.AddSamples(buffer, 0, buffer.Length);
            }
            catch (Exception e)
            {
                Log(e.Message + " " + e.StackTrace);
            }
        }

        public int  GetVolume()
        {
            lock (locker)
                return (int)(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
        }
        public void SetVolume(int volume)
        {
            volume = volume > 100 ? 100 : (volume < 0 ? 0 : volume);

            lock (locker)
            {
                device.AudioEndpointVolume.OnVolumeNotification     -= OnMasterVolumeChanged;
                device.AudioEndpointVolume.MasterVolumeLevelScalar   = volume / 100f;
                device.AudioEndpointVolume.OnVolumeNotification     += OnMasterVolumeChanged;
            }

            if (Mute) Mute = false;

            Raise(nameof(Volume));
        }

        private bool GetMute()
        {
            lock (locker)
            {
                if (session != null) return (session.SimpleAudioVolume.Mute | device.AudioEndpointVolume.Mute);
                return device.AudioEndpointVolume.Mute;
            }
        }
        private void SetMute(bool value)
        {
            lock (locker)
            {
                if (session != null)
                {
                    session.SimpleAudioVolume.Mute = value;
                    if (!value && device.AudioEndpointVolume.Mute) device.AudioEndpointVolume.Mute = false;
                }
                else
                    device.AudioEndpointVolume.Mute = value;
            }

            if (!value && Volume == 0) Volume = 15;

            Raise(nameof(Mute));
        }
        #endregion

        #region Master Audio Events | MMDeviceEnumerator
        public void OnMasterVolumeChanged(AudioVolumeNotificationData data)
        {
            Raise(nameof(Volume));
            Raise(nameof(Mute));
            //VolumeChanged?.BeginInvoke(this, new VolumeChangedArgs((int) (player.Volume * data.MasterVolume * 100), data.Muted), null, null);
        }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            Log($"OnDefaultDeviceChanged {defaultDeviceId}");
            Initialize();
        }
        public void OnDeviceRemoved(string deviceId)
        {
            Log($"OnDeviceRemoved {deviceId}");
            Initialize();
        }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { /*Log($"OnPropertyValueChanged {pwstrDeviceId} - {key.formatId}");*/ }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { Log($"OnDeviceStateChanged {newState.ToString()}");}
        public void OnDeviceAdded(string pwstrDeviceId) { Log($"OnDeviceAdded {pwstrDeviceId}");}
        #endregion

        #region Application (Session) Audio Events | AudioSessionControl
        public void OnVolumeChanged(float volume2, bool isMuted2)
        {
            Raise(nameof(Volume));
            Raise(nameof(Mute));
            //VolumeChanged?.BeginInvoke(this, new VolumeChangedArgs((int) (player.Volume * device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), session.SimpleAudioVolume.Mute || device.AudioEndpointVolume.Mute), null, null);
        }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state) { }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) { }
        #endregion

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [NAUDIO] {msg}"); }
    }
}