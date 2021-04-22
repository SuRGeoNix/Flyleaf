using System;
using System.Collections.Generic;
using System.Linq;

using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace FlyleafLib.MediaPlayer
{
    public class AudioMaster : NotifyPropertyChanged, IMMNotificationClient, IAudioSessionEventsHandler, IDisposable
    {
        #region Properties (Public)
        /// <summary>
        /// Whether to use application's session volume manager or master volume
        /// </summary>
        public VolumeHandler VolumeHandler  { get; set; } = VolumeHandler.Session;

        /// <summary>
        /// Default device name
        /// </summary>
        public string   DefaultDeviceName   { get; private set; } = DirectSoundOut.Devices.ToList()[0].Description;

        /// <summary>
        /// 
        /// </summary>
        public List<string> Devices
        {
            get
            {
                List<string> devices = new List<string>();
                foreach (var device in DirectSoundOut.Devices.ToList()) devices.Add(device.Description);
                return devices;
            }
        }

        /// <summary>
        /// Current audio device name (see Devices for valid input names)
        /// </summary>
        public string Device
        {
            get => _Device;
            set
            {
                bool found = false;
                foreach (var device in DirectSoundOut.Devices.ToList())
                    if (device.Description == value)
                    {
                        _Device = value;
                        DeviceIdNaudio = device.Guid;
                        DeviceId = device.ModuleName;
                        found = true;
                    }

                if (!found) throw new Exception("The specified audio device doesn't exist");
            }
        }

        /// <summary>
        /// Gets or sets the volume (valid values 0-100)
        /// </summary>
        public int      Volume      { get { return GetVolume(); } set { SetVolume(value); } }

        /// <summary>
        /// Gets or sets the volume mute
        /// </summary>
        public bool     Mute        { get { return GetMute();   } set { SetMute  (value); } }
        #endregion

        #region Declaration
        string _Device = DirectSoundOut.Devices.ToList()[0].Description;
        string  DeviceId;
        internal Guid       DeviceIdNaudio;
        MMDeviceEnumerator  deviceEnum;
        MMDevice            device;     // Current Master  Audio
        AudioSessionControl session;    // Current Session Audio
        readonly object     locker = new object();
        #endregion

        #region Initialize / Dispose
        public AudioMaster()
        {
            deviceEnum = new MMDeviceEnumerator();
            deviceEnum.RegisterEndpointNotificationCallback(this);
            
            foreach(var device in deviceEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                Log($"{device.ID} | {device.InstanceId} | {device.FriendlyName} ({device.FriendlyName})");

            Initialize();
        }
        public void Initialize()
        {
            lock (locker)
            {
                foreach(var player in Master.Players)
                    player.audioPlayer.Initialize();

                if (Device == DefaultDeviceName)
                    device = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                else
                    device = deviceEnum.GetDevice(DeviceId);

                device.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;
                device.AudioSessionManager.OnSessionCreated += (o, newSession) =>
                {
                    if (session != null) { session.UnRegisterEventClient(this); }
                    session = new AudioSessionControl(newSession);
                    session.RegisterEventClient(this);
                    if (VolumeHandler == VolumeHandler.Master) session.SimpleAudioVolume.Volume = 1;
                    Raise(nameof(Volume));
                    Raise(nameof(Mute));
                };

                Raise(nameof(Volume));
                Raise(nameof(Mute));
            }
        }
        public void Dispose()
        {
            lock (locker)
            {
                if (device  != null) device.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
                if (session != null) session.UnRegisterEventClient(this);
                device = null;
                session = null;
            } 
        }
        #endregion

        #region Volume / Mute
        private int  GetVolume()
        {
            lock (locker)
                return VolumeHandler == VolumeHandler.Master || session == null ? (int)(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100) : (int)(session.SimpleAudioVolume.Volume * 100);
                
        }
        private void SetVolume(int volume)
        {
            volume = volume > 100 ? 100 : (volume < 0 ? 0 : volume);

            lock (locker)
            {
                if (VolumeHandler == VolumeHandler.Master || session == null)
                {
                    device.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100f;
                    device.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;
                }
                else
                    session.SimpleAudioVolume.Volume = volume / 100f;
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
        }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string newDeviceId)
        {
            if (DeviceId == newDeviceId) return;
            Log($"OnDefaultDeviceChanged {newDeviceId}");
            Initialize();
        }
        public void OnDeviceRemoved(string deviceId)
        {
            if (DeviceId != DefaultDeviceName && DeviceId != deviceId) return;
            Log($"OnDeviceRemoved {deviceId}");
            Initialize();
        }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { Log($"OnPropertyValueChanged {pwstrDeviceId} - {key.formatId}"); }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { Log($"OnDeviceStateChanged {newState.ToString()}");}
        public void OnDeviceAdded(string pwstrDeviceId) { Log($"OnDeviceAdded {pwstrDeviceId}");}
        #endregion

        #region Application (Session) Audio Events | AudioSessionControl
        public void OnVolumeChanged(float volume2, bool isMuted2)
        {
            Raise(nameof(Volume));
            Raise(nameof(Mute));
        }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state) { Log("OnStateChanged"); }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) { Log("OnSessionDisconnected"); }
        #endregion

        private void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [AudioMaster] {msg}"); }
    }
    public class AudioPlayer : IDisposable
    {
        
        #region Declaration
        Player videoPlayer;
        Config cfg => videoPlayer.Config;

        int                     CHANNELS        = 2;        // Currently fixed
        int                     Rate            = 48000;    // Will be set from Input Format
        DirectSoundOut          player;
        WaveFormat              format;
        BufferedWaveProvider    buffer;

        readonly object         locker       = new object();
        #endregion

        #region Initialize / Dispose
        public AudioPlayer(Player player) { videoPlayer = player; }
        public void Initialize(int rate = -1) // Rate should be saved in case of internal Initialize during device reset etc.
        {
            lock (locker)
            {
                if (rate != -1) Rate = rate;

                Dispose();

                format = WaveFormatExtensible.CreateIeeeFloatWaveFormat(Rate, CHANNELS);
                buffer = new BufferedWaveProvider(format);
                buffer.BufferLength = 1000 * 1024;
                if (Master.AudioMaster.Device == Master.AudioMaster.DefaultDeviceName)
                    player = new DirectSoundOut((int)(cfg.audio.LatencyTicks / 10000));
                else
                    player = new DirectSoundOut(Master.AudioMaster.DeviceIdNaudio, (int)(cfg.audio.LatencyTicks / 10000));
                player.Init(buffer);
                player.Play();
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
                    if (buffer.BufferedDuration.Milliseconds > (cfg.audio.LatencyTicks / 10000) + 50) // We will see this happen on HLS streams that change source (eg. ads - like two streams playing audio in parallel)
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