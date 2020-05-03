﻿using System;

using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace PartyTime.UI_Example
{
    class AudioPlayer : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        WaveOut player;
        WaveFormat format;
        BufferedWaveProvider buffer;

        MMDeviceEnumerator deviceEnum = new MMDeviceEnumerator();

        // Audio Output Configuration
        int _BITS = 16; int _CHANNELS = 1; int _RATE = 48000;

        public int Volume { get { return (int) (player.Volume * 100); } set { player.Volume = (float)(value / 100.0);} }

        // Constructors
        public AudioPlayer()
        {
            format = new WaveFormatExtensible(_RATE, _BITS, _CHANNELS);
            player = new WaveOut();
            deviceEnum.RegisterEndpointNotificationCallback(this);

            Initialize();
        }
        public void Initialize()
        {
            lock (player)
            {
                player = new WaveOut();
                player.DeviceNumber = 0;
                buffer = new BufferedWaveProvider(format);
                buffer.BufferLength = 500 * 1024;
                player.Init(buffer);
            }
        }

        // Public Exposure
        public void Play()
        {
            lock (player)
            {
                if (player.PlaybackState != PlaybackState.Paused) player.Stop();
                player.Play();
            }
        }
        public void Pause()
        {
            lock (player) player.Pause();
        }
        public void Stop()
        {
            lock (player)
            {
                player.Stop();
                Initialize();
            }
        }
        public void VolUp(int v)
        {
            lock (player)
            {
                if ((player.Volume * 100) + v > 99) player.Volume = 1; else player.Volume += v / 100f;
            }
        }
        public void VolDown(int v)
        {
            lock (player)
            {
                if ((player.Volume * 100) - v < 1) player.Volume = 0; else player.Volume -= v / 100f;
            }
        }
        
        // Callbacks
        public void FrameClbk(byte[] buffer, int offset, int count)
        {
            try
            {
                if (this.buffer.BufferedBytes == 0) Log("[AUDIO] Buffer was empty");
                if (count < format.BlockAlign) return;

                int fixBlock = 0;
                while ((count - fixBlock) % format.BlockAlign != 0) fixBlock++;
                if (count - fixBlock < 1) return;

                this.buffer.AddSamples(buffer, offset + fixBlock, count - fixBlock);
                //player.Play();
            } catch (Exception e)
            {
                Log("[NAUDIO] " + e.Message + " " + e.StackTrace);
            }
            
        }
        public void ResetClbk()
        {
            lock (player)
            {
                PlaybackState oldState = player.PlaybackState;
                Initialize();
                //buffer.ClearBuffer();
                if (oldState == PlaybackState.Playing) player.Play();
            }
        }

        // Audio Devices Events
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { ResetClbk(); }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }

        // Logging
        private void Log(string msg) { Console.WriteLine(msg); }
    }
}
