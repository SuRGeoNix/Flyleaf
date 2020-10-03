using System;
using System.Diagnostics;

using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace SuRGeoNix.Flyleaf
{
    // TODO: Static Player for All FlyLeaf Objects and Concatate Samples?
    public class AudioPlayer : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        public const int        NAUDIO_DELAY_MS = 270;

        WaveOut                 player;
        WaveFormat              format;
        BufferedWaveProvider    buffer;

        MMDeviceEnumerator      deviceEnum;
        MMDevice                device;     // Holds Current Master Audio
        MMDevice                deviceInit; // Holds Initial NAudio's Session
        AudioEndpointVolume     deviceVol;

        int                     processId;

        readonly object                 locker       = new object();
        public static readonly object   lockerStatic = new object();

        // Audio Output Configuration
        int _BITS = 16; int _CHANNELS = 2; 
        public int _RATE = 48000; // Will be set from Input Format

        public bool     isPlaying   { get { return player.PlaybackState == PlaybackState.Playing ? true : false; } }
        public int      Volume      { get { return (int) (player.Volume * device.AudioEndpointVolume.MasterVolumeLevelScalar * 100); }  set { SetVolume(value); } }
        public bool     Mute        { get { return GetSetMute(); }                                                                      set { GetSetMute(false, value); } }


        //public event EventHandler VolumeChanged;

        public event VolumeChangedHandler VolumeChanged;
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

        private void OnMasterVolumeChanged(AudioVolumeNotificationData data) { VolumeChanged?.BeginInvoke(this, new VolumeChangedArgs((int) (player.Volume * data.MasterVolume * 100), data.Muted), null, null); }

        // Constructors
        public AudioPlayer()
        {
            deviceEnum = new MMDeviceEnumerator();
            deviceEnum.RegisterEndpointNotificationCallback(this);
            deviceInit = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            processId = Process.GetCurrentProcess().Id;
            
            Initialize();
        }
        public void Initialize()
        {
            lock (locker)
            {   
                format = new WaveFormatExtensible(_RATE, _BITS, _CHANNELS);
                buffer = new BufferedWaveProvider(format);
                buffer.BufferLength = 1500 * 1024;

                player = new WaveOut();
                player.DeviceNumber = 0;
                player.DesiredLatency = NAUDIO_DELAY_MS - 70;
                player.Init(buffer);
                player.Play();

                if (device != null) device.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
                device = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;
            }
        }

        // Public Exposure
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
                        deviceVol.MasterVolumeLevelScalar = volumef;
                        player.Volume = volumef / deviceVol.MasterVolumeLevelScalar;
                        device.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;
                    }
                    else
                        player.Volume = volumef / deviceVol.MasterVolumeLevelScalar;
                } catch (Exception e) { Log(e.Message + " - " + e.StackTrace); }
            }
        }
        public void Play()
        {
            lock (locker)
            {
                //if (player.PlaybackState != PlaybackState.Paused) player.Stop();
                buffer.ClearBuffer();
                player.Play();
            }
        }
        public void Pause()
        {
            lock (locker) { player.Pause(); buffer.ClearBuffer(); }
        }
        public void Stop()
        {
            lock (locker)
            {
                player.Stop();
                Initialize();
            }
        }
        public void Close()
        {
            lock (locker)
                if (player != null) player.Dispose();
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


        // Helpers
        private bool GetSetMute(bool Get = true, bool mute = false)
        {
            lock (locker)
            {
                try
                {
                    for (int i = 0; i < deviceInit.AudioSessionManager.Sessions.Count; i++)
                    {
                        AudioSessionControl session = deviceInit.AudioSessionManager.Sessions[i];
                        if (processId == session.GetProcessID)
                        {
                            deviceVol = device.AudioEndpointVolume;

                            if (Get) return (session.SimpleAudioVolume.Mute | deviceVol.Mute);
                            session.SimpleAudioVolume.Mute = mute;
                            if (deviceVol.Mute) deviceVol.Mute = false;

                            break;
                        }
                    }
                } catch (Exception) { }                
            }

            return false;
        }

        // Audio Devices Events
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { Initialize(); Log($"OnDefaultDeviceChanged {defaultDeviceId}"); }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { Log($"OnPropertyValueChanged {pwstrDeviceId} - {key.formatId}");}
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { Log($"OnDeviceStateChanged {newState.ToString()}");}
        public void OnDeviceAdded(string pwstrDeviceId) { Log($"OnDeviceAdded {pwstrDeviceId}");}
        public void OnDeviceRemoved(string deviceId) { Initialize(); Log($"OnDeviceRemoved {deviceId}");}

        // Logging
        private void Log(string msg) { Console.WriteLine("[AUDIO]" + msg); }
    }
}
