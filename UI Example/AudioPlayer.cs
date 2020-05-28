using System;
using System.Threading;

using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace PartyTime.UI_Example
{
    class AudioPlayer : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        WaveOut                 player;
        WaveFormat              format;
        BufferedWaveProvider    buffer;
        Thread                  threadOpen;

        MMDeviceEnumerator      deviceEnum = new MMDeviceEnumerator();

        private static readonly object  locker  = new object();

        // Audio Output Configuration
        int _BITS = 16; int _CHANNELS = 2; 
        public int _RATE = 48000; // Will be set from Input Format

        public int Volume { get { return (int) (player.Volume * 100); } set { player.Volume = (float)(value / 100.0);} }

        // Constructors
        public AudioPlayer()
        {
            deviceEnum.RegisterEndpointNotificationCallback(this);
            Initialize();
        }
        public void Initialize()
        {
            lock (locker)
            {   
                bool reseted = false;

                format = new WaveFormatExtensible(_RATE, _BITS, _CHANNELS);
                buffer = new BufferedWaveProvider(format);
                buffer.BufferLength = 1500 * 1024;

                threadOpen = new Thread(() => { 
                    lock (locker)
                    {
                        try
                        {
                            player = new WaveOut();
                            player.DeviceNumber = 0;
                            player.Init(buffer);
                            player.Play();
                            reseted = true;
                        } catch (Exception) { reseted = true; }
                    }
                });
                threadOpen.SetApartmentState(ApartmentState.STA);
                threadOpen.Start();

                while (reseted);
                Thread.Sleep(20);
            }
        }

        // Public Exposure
        public void Play()
        {
            lock (locker)
            {
                if (player.PlaybackState != PlaybackState.Paused) player.Stop();
                player.Play();
            }
        }
        public void Pause()
        {
            lock (locker) player.Pause();
        }
        public void Stop()
        {
            lock (locker)
            {
                player.Stop();
                Initialize();
            }
        }
        public void VolUp(int v)
        {
            lock (locker)
                if ((player.Volume * 100) + v > 99) player.Volume = 1; else player.Volume += v / 100f;
        }
        public void VolDown(int v)
        {
            lock (locker)
                if ((player.Volume * 100) - v < 1) player.Volume = 0; else player.Volume -= v / 100f;
        }
        
        // Callbacks
        public void FrameClbk(byte[] buffer, int offset, int count)
        {
            try
            {
                //if (this.buffer.BufferedBytes == 0) Log("[AUDIO] Buffer was empty ");
                //Log($"[AUDIO PLAYER] [BufferedBytes: {this.buffer.BufferedBytes}]");

                int fixBlock = 0;
                while ((count - fixBlock) % format.BlockAlign != 0) fixBlock++;

                this.buffer.AddSamples(buffer, offset + fixBlock, count - fixBlock);
            }
            catch (Exception e)
            {
                Log("[NAUDIO] " + e.Message + " " + e.StackTrace);
            }
            
        }
        public void ResetClbk() { lock (locker) buffer.ClearBuffer(); }

        // Audio Devices Events
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { Initialize(); }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }

        // Logging
        private void Log(string msg) { Console.WriteLine(msg); }
    }
}