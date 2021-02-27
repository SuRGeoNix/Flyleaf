using System;
using System.Diagnostics;
using System.Windows.Forms;

using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Threading;

namespace SuRGeoNix.Flyleaf
{
    public class AudioPlayer : IMMNotificationClient, IAudioSessionEventsHandler //,UserControl (to avoid uiThread)
    {
        public bool     isPlaying   { get { return player.PlaybackState == PlaybackState.Playing ? true : false; } }
        public int      Volume      { get { try { lock (locker) return (int) (player.Volume * device.AudioEndpointVolume.MasterVolumeLevelScalar * 100); } catch (Exception e) { Log("ERROR " + e.Message); return -1; } }  set { SetVolume(value); } }
        public bool     Mute        { get { return GetSetMute(); }  set { GetSetMute(false, value); } }

        public Control          uiThread;                   // Requires to run on same thread (using UI thread) | Warning! Any access to audio device should be invoke ui thread (otherwise will hang on player.Dispose() | player.Init())

        public const int        NAUDIO_DELAY_MS = 300;      // Latency (buffer before play), consider same audio delay for video player in order to be sync
        public int              _RATE           = 48000;    // Will be set from Input Format
        int                     _CHANNELS       = 2;        // Currently fixed
        //int                     _BITS           = 16;       // Currently not used (CreateIeeeFloatWaveFormat 32bit, if we need to set it use WaveFormatExtensible)

        WaveOut                 player;
        WaveFormat              format;
        BufferedWaveProvider    buffer;

        MMDeviceEnumerator      deviceEnum;
        MMDevice                device;     // Current Master Audio
        //MMDevice                deviceInit; // Initial NAudio's Device/Session
        AudioEndpointVolume     deviceVol;
        AudioSessionControl     session;    // Current App Audio Session

        int                                 processId;
        readonly object                     locker       = new object();
        public static readonly object       lockerStatic = new object();

        public event VolumeChangedHandler   VolumeChanged;
        public delegate void VolumeChangedHandler(object source, VolumeChangedArgs e);
        public class VolumeChangedArgs : EventArgs
        {
            public int volume;
            public bool mute;
            public VolumeChangedArgs(int volume, bool mute)
            {
                this.volume = volume;
                this.mute = mute;
            }
        }
        
        // Constructors
        public AudioPlayer(Control uiControl)
        {
            uiThread = uiControl;
            deviceEnum = new MMDeviceEnumerator();
            deviceEnum.RegisterEndpointNotificationCallback(this);
            //deviceInit = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            processId = Process.GetCurrentProcess().Id;
            
            Initialize();
        }

        public void Initialize() // !!! Must be called from the same thread that was initialized
        {
            if (uiThread.InvokeRequired) { uiThread.BeginInvoke(new Action(() => Initialize())); return; }

            lock (locker)
            {
                // Dispose | UnRegister 
                if (device  != null) device.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
                if (session != null) session.UnRegisterEventClient(this);
                if (player  != null) player.Dispose();
                if (buffer  != null) buffer.ClearBuffer();
                if (session != null) session.UnRegisterEventClient(this);

                // Initialize
                format = WaveFormatExtensible.CreateIeeeFloatWaveFormat(_RATE, _CHANNELS);
                buffer = new BufferedWaveProvider(format);
                buffer.BufferLength = 1000 * 1024;
                player = new WaveOut();
                player.DeviceNumber = 0;
                player.DesiredLatency = NAUDIO_DELAY_MS;
                player.Init(buffer);
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

                VolumeChanged?.BeginInvoke(this, new VolumeChangedArgs((int) (player.Volume * device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), (session != null ? session.SimpleAudioVolume.Mute : false) || device.AudioEndpointVolume.Mute), null, null);
            }
        }

        // Main
        public void Play()  { lock (locker) { buffer.ClearBuffer(); player.Play(); } }
        public void Pause() { lock (locker) { player.Pause(); buffer.ClearBuffer(); } }
        public void Stop()  { lock (locker) { player.Stop(); Initialize(); } }
        public void Close() { lock (locker) { if (player != null) player.Dispose(); } }

        public void SetVolume(int volume)
        {
            lock (locker)
            {
                try
                {
                    deviceVol = device.AudioEndpointVolume;
                    
                    float volumef = volume / 100f;
                    float master = deviceVol.MasterVolumeLevelScalar;
                    if (volumef > master)
                    {
                        device.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
                        try
                        {
                            deviceVol.MasterVolumeLevelScalar = volumef;
                            player.Volume = volumef / deviceVol.MasterVolumeLevelScalar;
                        } catch (Exception) { }
                        device.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;
                    }
                    else
                        player.Volume = volumef / deviceVol.MasterVolumeLevelScalar;
                } catch (Exception e) { Log(e.Message + " - " + e.StackTrace); }
            }
        }
        private bool GetSetMute(bool Get = true, bool mute = false)
        {
            lock (locker)
            {
                try
                {
                    deviceVol = device.AudioEndpointVolume;
                    if (Get) return (session.SimpleAudioVolume.Mute | deviceVol.Mute);
                    session.SimpleAudioVolume.Mute = mute;
                    if (deviceVol.Mute) deviceVol.Mute = false;
                } catch (Exception) { }                
            }

            return false;
        }

        // Callbacks
        public void FrameClbk(byte[] buffer, int offset, int count)
        {
            try
            {
                if (player.PlaybackState != PlaybackState.Playing) return;

                this.buffer.AddSamples(buffer, offset, count);
            }
            catch (Exception e)
            {
                Log("[NAUDIO] " + e.Message + " " + e.StackTrace);
            }
        }
        public void ResetClbk() { lock (locker) buffer.ClearBuffer(); }

        // Master Audio Events | MMDeviceEnumerator
        public void OnMasterVolumeChanged(AudioVolumeNotificationData data)
        {
            VolumeChanged?.BeginInvoke(this, new VolumeChangedArgs((int) (player.Volume * data.MasterVolume * 100), data.Muted), null, null);
        }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { Initialize(); Log($"OnDefaultDeviceChanged {defaultDeviceId}"); }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { /*Log($"OnPropertyValueChanged {pwstrDeviceId} - {key.formatId}");*/ }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { Log($"OnDeviceStateChanged {newState.ToString()}");}
        public void OnDeviceAdded(string pwstrDeviceId) { Log($"OnDeviceAdded {pwstrDeviceId}");}
        public void OnDeviceRemoved(string deviceId) { Initialize(); Log($"OnDeviceRemoved {deviceId}");}

        // Application Audio Events | AudioSessionControl
        public void OnVolumeChanged(float volume2, bool isMuted2)
        {
            if (uiThread.InvokeRequired) { uiThread.BeginInvoke(new Action(() => OnVolumeChanged(volume2, isMuted2))); return; }

            VolumeChanged?.BeginInvoke(this, new VolumeChangedArgs((int) (player.Volume * device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), session.SimpleAudioVolume.Mute || device.AudioEndpointVolume.Mute), null, null);
        }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state) { }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) { }

        // Logging
        private void Log(string msg) { Console.WriteLine("[AUDIO]" + msg); }
    }
}