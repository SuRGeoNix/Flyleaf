using System;
using System.Collections.Generic;
using System.Threading;

using SharpGen.Runtime;
using Vortice.MediaFoundation;

using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib
{
    /// <summary>
    /// Flyleaf Engine
    /// </summary>
    public static class Engine
    {
        /// <summary>
        /// Engine has been started and is ready for use
        /// </summary>
        public static bool              Started         { get; private set; }

        /// <summary>
        /// Engine's configuration
        /// </summary>
        public static EngineConfig      Config          { get; private set; }

        /// <summary>
        /// Audio Engine
        /// </summary>
        public static AudioEngine       Audio           { get; private set; }

        /// <summary>
        /// Video Engine
        /// </summary>
        public static VideoEngine       Video           { get; private set; }

        /// <summary>
        /// Plugins Engine
        /// </summary>
        public static PluginsEngine     Plugins         { get; private set; }

        /// <summary>
        /// FFmpeg Engine
        /// </summary>
        public static FFmpegEngine      FFmpeg          { get; private set; }

        /// <summary>
        /// List of active Players
        /// </summary>
        public static List<Player>      Players         { get; private set; }

        internal static LogHandler Log;

        static Thread   tMaster;
        static object   lockEngine      = new object();

        /// <summary>
        /// Initializes Flyleaf's Engine
        /// </summary>
        /// <param name="config">Engine's configuration</param>
        public static void Start(EngineConfig config = null)
        {
            lock (lockEngine)
            {
                if (Started)
                    throw new Exception("Engine has already been started");

                if (System.Windows.Application.Current == null)
                    new System.Windows.Application();

                Config = config == null ? new EngineConfig() : config;

                if (Config.HighPerformaceTimers)
                    Utils.NativeMethods.TimeBeginPeriod(1);

                Logger.SetOutput();

                Log     = new LogHandler("[FlyleafEngine      ] ");
                FFmpeg  = new FFmpegEngine();
                Video   = new VideoEngine();
                Audio   = new AudioEngine();
                Plugins = new PluginsEngine();
                Players = new List<Player>();

                if (Config.FFmpegDevices)
                    EnumerateCapDevices();

                Renderer.Start();

                Started = true;

                if (Config.UIRefresh)
                    StartThread();
            }
        }

        private unsafe static void EnumerateCapDevices()
        {
            try
            {
                IMFAttributes capAttrs = MediaFactory.MFCreateAttributes(1);

                IntPtr      capDevicesPtrs;
                IntPtr*     capDevicesPtr;
                IMFActivate capDevice;
                string      capDeviceName;
                int         capsCount;

                Result res;
                object tmp;
                string dump;

                dump = "Audio Cap Devices\r\n";
                capAttrs.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeAudcap);
                res = MediaFactory.MFEnumDeviceSources(capAttrs, out capDevicesPtrs, out capsCount);

                if (res == Result.Ok && capDevicesPtrs != IntPtr.Zero && capsCount > 0)
                {
                    capDevicesPtr = (IntPtr*)capDevicesPtrs;
                    for (int i=1; i<=capsCount; i++)
                    {
                        capDevice = new IMFActivate(*capDevicesPtr);
                        tmp = capDevice.Get(CaptureDeviceAttributeKeys.FriendlyName);
                        if (tmp == null) continue;
                        capDeviceName = tmp.ToString();
                        Audio.CapDevices.Add(capDeviceName);
                        dump += $"[#{i}] {capDeviceName}\r\n";
                        capDevice.Release();
                        capDevicesPtr++;
                    }

                    Log.Debug(dump);
                }

                dump = "Video Cap Devices\r\n";
                capAttrs.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
                res = MediaFactory.MFEnumDeviceSources(capAttrs, out capDevicesPtrs, out capsCount);

                if (res == Result.Ok && capDevicesPtrs != IntPtr.Zero && capsCount > 0)
                {
                    capDevicesPtr = (IntPtr*)capDevicesPtrs;
                    for (int i=1; i<=capsCount; i++)
                    {
                        capDevice = new IMFActivate(*capDevicesPtr);
                        tmp = capDevice.Get(CaptureDeviceAttributeKeys.FriendlyName);
                        if (tmp == null) continue;
                        capDeviceName = tmp.ToString();
                        Audio.CapDevices.Add(capDeviceName);
                        dump += $"[#{i}] {capDeviceName}\r\n";
                        capDevice.Release();
                        capDevicesPtr++;
                    }

                    Log.Debug(dump);
                }

                capAttrs.Release();
            } catch (Exception e)
            {
                Log.Error($"Failed to enumerate capture devices ({e.Message})");
            }
        }

        internal static void AddPlayer(Player player)
        {
            lock (Players)
                Players.Add(player);
        }
        internal static int  GetPlayerPos(int playerId)
        {
            for (int i=0; i<Players.Count; i++)
                if (Players[i].PlayerId == playerId)
                    return i;

            return -1;
        }
        internal static void DisposePlayer(Player player)
        {
            if (player == null) return;

            DisposePlayer(player.PlayerId);
        }
        internal static void DisposePlayer(int playerId)
        {
            lock (Players)
            {
                Log.Trace($"Disposing {playerId}");
                int pos = GetPlayerPos(playerId);
                if (pos == -1) return;

                Player player = Players[pos];
                player.DisposeInternal();
                Players.RemoveAt(pos);
                Log.Trace($"Disposed {playerId}");
            }
        }

        internal static void StartThread()
        {
            if (!Config.UIRefresh || (tMaster != null && tMaster.IsAlive))
                return;

            tMaster = new Thread(() => { MasterThread(); });
            tMaster.Name = "FlyleafEngine";
            tMaster.IsBackground = true;
            tMaster.Start();
        }
        internal static void MasterThread()
        {
            Log.Info("Thread started");

            int curLoop = 0;
            int secondLoops = 1000 / Config.UIRefreshInterval;
            long secondCorrection = DateTime.UtcNow.Ticks;

            do
            {
                try
                {
                    if (Players.Count == 0)
                    {
                        Thread.Sleep(Config.UIRefreshInterval);
                        continue;
                    }

                    curLoop++;

                    lock (Players)
                        foreach (Player player in Players)
                        {
                            /* Every UIRefreshInterval */
                            player.Activity.RefreshMode();

                            /* Every Second */
                            if (curLoop == secondLoops)
                            {
                                if (player.Config.Player.Stats)
                                {
                                    var now = DateTime.UtcNow.Ticks;
                                    secondCorrection = DateTime.UtcNow.Ticks - secondCorrection;

                                    var curStats = player.stats;
                                    long curTotalBytes  = player.VideoDemuxer.TotalBytes + player.AudioDemuxer.TotalBytes + player.SubtitlesDemuxer.TotalBytes;
                                    long curVideoBytes  = player.VideoDemuxer.VideoPackets.Bytes + player.AudioDemuxer.VideoPackets.Bytes + player.SubtitlesDemuxer.VideoPackets.Bytes;
                                    long curAudioBytes  = player.VideoDemuxer.AudioPackets.Bytes + player.AudioDemuxer.AudioPackets.Bytes + player.SubtitlesDemuxer.AudioPackets.Bytes;

                                    player.bitRate      = (curTotalBytes - curStats.TotalBytes) * 8 / 1000.0;
                                    player.Video.bitRate= (curVideoBytes - curStats.VideoBytes) * 8 / 1000.0;
                                    player.Audio.bitRate= (curAudioBytes - curStats.AudioBytes) * 8 / 1000.0;

                                    curStats.TotalBytes = curTotalBytes;
                                    curStats.VideoBytes = curVideoBytes;
                                    curStats.AudioBytes = curAudioBytes;

                                    if (player.IsPlaying)
                                    {
                                        player.Video.fpsCurrent = (player.Video.FramesDisplayed - curStats.FramesDisplayed) / (secondCorrection / 10000000.0);
                                        curStats.FramesDisplayed = player.Video.FramesDisplayed;
                                    }

                                    secondCorrection = now;
                                }
                            }
                        }

                    if (curLoop == secondLoops)
                        curLoop = 0;

                    Action action = () =>
                    {
                        try
                        {
                            foreach (Player player in Players)
                            {
                                /* Every UIRefreshInterval */

                                // Activity Mode Refresh & Hide Mouse Cursor (FullScreen only)
                                if (player.Activity.mode != player.Activity._Mode)
                                    player.Activity.SetMode();                                    

                                // CurTime / Buffered Duration (+Duration for HLS)
                                if (!Config.UICurTimePerSecond)
                                    player.UpdateCurTime();
                                else if (player.Status == Status.Paused)
                                {
                                    if (player.MainDemuxer.IsRunning)
                                        player.UpdateCurTime();
                                    else
                                        player.UpdateBufferedDuration();
                                }

                                /* Every Second */
                                if (curLoop == 0)
                                {
                                    // Stats Refresh (BitRates / FrameDisplayed / FramesDropped / FPS)
                                    if (player.Config.Player.Stats)
                                    {
                                        player.BitRate = player.BitRate;
                                        player.Video.BitRate = player.Video.BitRate;
                                        player.Audio.BitRate = player.Audio.BitRate;

                                        if (player.IsPlaying)
                                        {
                                            player.Video.FramesDisplayed= player.Video.FramesDisplayed;
                                            player.Video.FramesDropped  = player.Video.FramesDropped;
                                            player.Video.FPSCurrent     = player.Video.FPSCurrent;
                                        }
                                    }
                                }
                            }
                        } catch { }
                    };

                    Utils.UI(action);
                    Thread.Sleep(Config.UIRefreshInterval);

                } catch { curLoop = 0; }

            } while (Config.UIRefresh);

            Log.Info("Thread stopped");
        }
    }
}
