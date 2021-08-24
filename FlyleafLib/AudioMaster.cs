using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace FlyleafLib
{
    public class AudioMaster : NotifyPropertyChanged, IMMNotificationClient, IAudioSessionEventsHandler
    {
        #region Properties (Public)
        /// <summary>
        /// Default audio device name
        /// </summary>
        public string       DefaultDeviceName   { get; private set; }

        /// <summary>
        /// List with the names of the available audio devices (use these names to change current Device for all the players or for each player seperately)
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
        /// Audio device name which will be used for all the audio players (see Devices for valid input names)
        /// </summary>
        public string       Device
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

                        Initialize();
                    }

                if (!found) throw new Exception("The specified audio device doesn't exist");
            }
        }

        /// <summary>
        /// Gets or sets the master's volume (valid values 0 - 100)
        /// </summary>
        public int          VolumeMaster        { get { return GetVolumeMaster();   }   set { SetVolumeMaster(value);   } }

        /// <summary>
        /// Gets or sets the master's volume mute
        /// </summary>
        public bool         MuteMaster          { get { return GetMuteMaster();     }   set { SetMuteMaster(value);     } }

        /// <summary>
        /// Gets or sets the session's volume (valid values 0 - 100)
        /// </summary>
        public int          VolumeSession       { get { return GetVolumeSession();  }   set { SetVolumeSession(value);  } }

        /// <summary>
        /// Gets or sets the session's volume mute
        /// </summary>
        public bool         MuteSession         { get { return GetMuteSession();    }   set { SetMuteSession(value);    } }
        #endregion

        #region Declaration
        string _Device;
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
            DefaultDeviceName = DirectSoundOut.Devices.ToList()[0].Description;
            _Device = DefaultDeviceName;

            deviceEnum = new MMDeviceEnumerator();
            deviceEnum.RegisterEndpointNotificationCallback(this);
            
            #if DEBUG
            string dump = "Audio devices ...\r\n";
            foreach(var device in deviceEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                dump += $"{device.ID} | {device.FriendlyName}\r\n";

            Log(dump);
            #endif

            Initialize();
        }
        public void Initialize()
        {
            lock (locker)
            {
                if (device != null) device.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;

                foreach(var player in Master.Players.Values)
                    player.InitializeAudio();

                if (Device == DefaultDeviceName)
                    device = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                else
                    device = deviceEnum.GetDevice(DeviceId);

                device.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;
                device.AudioSessionManager.OnSessionCreated += (o, newSession) =>
                {
                    try
                    {
                        var tmpSession = new AudioSessionControl(newSession);
                        if (tmpSession == null || tmpSession.GetProcessID != Process.GetCurrentProcess().Id) return;
                        if (session != null) { session.UnRegisterEventClient(this); session.Dispose(); }
                        session = tmpSession;
                        session.RegisterEventClient(this);
                        session.SimpleAudioVolume.Volume = 1;
                    } catch (Exception) { }

                    Raise(nameof(VolumeSession));
                    Raise(nameof(MuteSession));
                };

                Raise(nameof(VolumeMaster));
                Raise(nameof(MuteMaster));
            }
        }
        #endregion

        #region Volume / Mute
        private int  GetVolumeMaster()
        {
            lock (locker) return (int)(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
        }
        private int  GetVolumeSession()
        {
            lock (locker) return session == null ? 100 : (int)(session.SimpleAudioVolume.Volume * 100);                
        }
        private void SetVolumeMaster(int volume)
        {
            volume = volume > 100 ? 100 : (volume < 0 ? 0 : volume);

            lock (locker)
            {
                device.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
                device.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100f;
                device.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;
            }

            if (MuteMaster) { MuteMaster = false; Raise(nameof(MuteMaster)); }

            Raise(nameof(VolumeMaster));
        }
        private void SetVolumeSession(int volume)
        {
            volume = volume > 100 ? 100 : (volume < 0 ? 0 : volume);

            lock (locker) session.SimpleAudioVolume.Volume = volume / 100f;

            if (MuteSession) { MuteSession = false; Raise(nameof(MuteSession)); }

            Raise(nameof(VolumeSession));
        }
        private bool GetMuteMaster()
        {
            lock (locker) return device.AudioEndpointVolume.Mute;
        }

        private bool GetMuteSession()
        {
            lock (locker) return session == null ? false : session.SimpleAudioVolume.Mute;
        }
        private void SetMuteMaster(bool value)
        {
            lock (locker) device.AudioEndpointVolume.Mute = value;

            if (!value && VolumeMaster == 0) VolumeMaster = 15;

            Raise(nameof(MuteMaster));
        }
        private void SetMuteSession(bool value)
        {
            lock (locker) session.SimpleAudioVolume.Mute = value;

            if (!value && VolumeSession == 0) VolumeSession = 15;

            Raise(nameof(MuteSession));
        }
        #endregion

        #region Master Audio Events | MMDeviceEnumerator
        public void OnMasterVolumeChanged(AudioVolumeNotificationData data)
        {
            Raise(nameof(VolumeMaster));
            Raise(nameof(MuteMaster));
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
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { /*Log($"OnPropertyValueChanged {pwstrDeviceId} - {key.formatId}");*/ }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { /*Log($"OnDeviceStateChanged {newState.ToString()}");*/ }
        public void OnDeviceAdded(string pwstrDeviceId) { /*Log($"OnDeviceAdded {pwstrDeviceId}");*/ }
        #endregion

        #region Application (Session) Audio Events | AudioSessionControl
        public void OnVolumeChanged(float volume2, bool isMuted2)
        {
            Raise(nameof(VolumeSession));
            Raise(nameof(MuteSession));
        }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state) { /*Log("OnStateChanged");*/ }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) { /*Log("OnSessionDisconnected");*/ }
        #endregion

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [Master] [AudioMaster] {msg}"); }
    }
}