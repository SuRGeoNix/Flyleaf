using System;
using System.Collections.Generic;

using Vortice.MediaFoundation;

namespace FlyleafLib
{
    public class AudioMaster //: CallbackBase, IMMNotificationClient//, INotifyPropertyChanged
    {
        /* TODO
         * 
         * 1) IMMNotificationClient probably not required
         *      XAudio2 handles just fine the default audio device change and device removal if we use the specific device goes back to default
         * 
         * 2) Master/Session Volume/Mute probably not required
         *      Possible only to ensure that we have session's volume unmuted and to 1? (probably the defaults?)
         *      
         * 3) Property changed notifications / or IMMNotificationClient forward events (Devices changed)?
         */

        #region Properties (Public)
        /// <summary>
        /// Default audio device name
        /// </summary>
        public string       DefaultDeviceName   { get; private set; } = "Default";

        /// <summary>
        /// Default audio device id
        /// </summary>
        public string       DefaultDeviceId     { get; private set; } = "0";

        /// <summary>
        /// Whether no audio devices were found or audio failed to initialize
        /// </summary>
        public bool         Failed              { get; private set; }

        //public string       CurrentDeviceName   { get; private set; } = "Default";
        //public string       CurrentDeviceId     { get; private set; } = "0";

        /// <summary>
        /// List with of the available audio devices
        /// </summary>
        public List<string> Devices
        {
            get
            {
                List<string> devices = new List<string>();

                devices.Add(DefaultDeviceName);

                foreach(var device in deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active))
                    devices.Add(device.FriendlyName);

                return devices;
            }
        }

        public string GetDeviceId(string deviceName)
        {
            if (deviceName == DefaultDeviceName)
                return DefaultDeviceId;

            foreach(var device in deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active))
            {
                if (device.FriendlyName.ToLower() != deviceName.ToLower())
                    continue;

                return device.Id;
            }

            throw new Exception("The specified audio device doesn't exist");
        }
        public string GetDeviceName(string deviceId)
        {
            if (deviceId == DefaultDeviceId)
                return DefaultDeviceName;

            foreach(var device in deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active))
            {
                if (device.Id.ToLower() != deviceId.ToLower())
                    continue;

                return device.FriendlyName;
            }

            throw new Exception("The specified audio device doesn't exist");
        }
        #endregion

        IMMDeviceEnumerator  deviceEnum;
        //private object locker = new object();
        public AudioMaster()
        {
            try
            {
                deviceEnum = new IMMDeviceEnumerator();
                //deviceEnum.RegisterEndpointNotificationCallback(this);
            
                var defaultDevice =  deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice == null)
                {
                    Failed = true;
                    return;
                }

                //CurrentDeviceId = defaultDevice.Id;

                #if DEBUG
                string dump = "Audio devices ...\r\n";
                foreach(var device in deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active))
                    dump += $"{device.Id} | {device.FriendlyName} {(defaultDevice.Id == device.Id ? "*" : "")}\r\n";

                Log(dump);
                #endif
            } catch { Failed = true; }
        }

        //public void OnDeviceStateChanged(string pwstrDeviceId, int newState) { }
        //public void OnDeviceAdded(string pwstrDeviceId)
        //{
        //    Log($"OnDeviceAdded | {GetDeviceName(pwstrDeviceId)}");
        //}
        //public void OnDeviceRemoved(string pwstrDeviceId)
        //{
        //    Log($"OnDeviceRemoved | {GetDeviceName(pwstrDeviceId)}");

        //    // XAudio2 will handle this automatically
        //    //Task.Run(() =>
        //    //{
        //    //    lock (locker)
        //    //        foreach(var player in Master.Players)
        //    //        {
        //    //            if (player.Audio.DeviceId != pwstrDeviceId)
        //    //                continue;

        //    //            player.Audio.DeviceId = DefaultDeviceId;
        //    //        }

        //    //    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => Raise(null)));
        //    //});
        //}
        //public void OnDefaultDeviceChanged(DataFlow flow, Role role, string pwstrDefaultDeviceId)
        //{
        //    if (CurrentDeviceId == pwstrDefaultDeviceId)
        //        return;

        //    Log($"OnDefaultDeviceChanged | {GetDeviceName(CurrentDeviceId)} => {GetDeviceName(pwstrDefaultDeviceId)}");
            
        //    CurrentDeviceId = pwstrDefaultDeviceId;
        //    CurrentDeviceName = GetDeviceName(CurrentDeviceId);

        //    return;

        //    // XAudio2 will handle this automatically
        //    //Task.Run(() =>
        //    //{
        //    //    lock (locker)
        //    //        foreach(var player in Master.Players)
        //    //        {
        //    //            if (player.Audio.DeviceId != DefaultDeviceId)
        //    //                continue;

        //    //            player.Audio.Initialize();
        //    //        }
        //    //});
        //}
        //public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        private void Log(string msg) { Utils.Log($"[AudioMaster] {msg}"); }
    }
}