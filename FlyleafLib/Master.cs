using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;

namespace FlyleafLib
{
    /// <summary>
    /// Manages library's static configuration
    /// </summary>
    public static class Master
    {
        /// <summary>
        /// Manages audio devices, volume &amp; mute
        /// </summary>
        public static AudioMaster                   AudioMaster             { get; }

        /// <summary>
        /// List of GPU Adpaters luid/description <see cref="Config.VideoConfig.GPUAdapteLuid"/>
        /// </summary>
        public static Dictionary<long, GPUAdapter>  GPUAdapters             { get; }

        /// <summary>
        /// Disable this to use high performance timers only when required (useful for single player)
        /// Must be set before calling RegisterFFmpeg
        /// </summary>
        public static bool                          HighPerformaceTimers    { get; set; } = true;

        /// <summary>
        /// Holds player instances
        /// </summary>
        public static List<Player>                  Players                 { get; }

        /// <summary>
        /// Activates Master Thread to monitor all the players and perform the required updates
        /// (Required for Activity Mode, Buffered Duration on Pause &amp; Stats)
        /// </summary>
        public static bool                          UIRefresh               { get => _UIRefresh; set { _UIRefresh = value; if (value) StartThread(); } }
        static bool _UIRefresh;

        /// <summary>
        /// How often should update the UI in ms (low values can cause performance issues)
        /// (Should UIRefreshInterval &lt; 1000ms and 1000 % UIRefreshInterval == 0 for accurate per second stats)
        /// </summary>
        public static int                           UIRefreshInterval       { get ; set; } = 250;

        /// <summary>
        /// Updates CurTime when the second changes otherwise every UIRefreshInterval
        /// </summary>
        public static bool                          UICurTimePerSecond      { get; set; } = true;

        static Thread tMaster;
        static bool isCursorHidden;
        static bool alreadyRegister = false;

        static Master()
        {
            // Create a UI dispatcher if not already exists (mainly for Winforms)
            if (System.Windows.Application.Current == null)
                new System.Windows.Application();

            Players     = new List<Player>();

            AudioMaster = new AudioMaster();
            GPUAdapters = Renderer.GetAdapters();

            PluginHandler.LoadAssemblies();
        }

        /// <summary>
        /// Registers Plugins (ensure you provide x86 or x64 and the right framework based on your project)
        /// </summary>
        /// <param name="absolutePath">Provide your custom absolute path or :1 for current\Plugins\ or :2 for Plugins\ from current to base</param>
        public static void RegisterPlugins(string absolutePath = ":1")
        {
            if (absolutePath == ":1")
                absolutePath = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");
            else if (absolutePath == ":2")
                absolutePath = Utils.FindFolderBelow("Plugins");

            if (string.IsNullOrEmpty(absolutePath))
                return;

            PluginHandler.LoadAssemblies(absolutePath);
        }

        /// <summary>
        /// Registers FFmpeg libraries (ensure you provide x86 or x64 based on your project)
        /// </summary>
        /// <param name="absolutePath">Provide your custom absolute path or :1 for current or :2 for Libs\(x86 or x64 dynamic)\FFmpeg from current to base</param>
        /// <param name="verbosity">FFmpeg's verbosity (24: Warning, 64: Max offset ...) (used only in DEBUG)</param>
        public static void RegisterFFmpeg(string absolutePath = ":1", int verbosity = AV_LOG_WARNING) //AV_LOG_MAX_OFFSET
        {
            if (alreadyRegister) return;

            if (HighPerformaceTimers)
                Utils.NativeMethods.TimeBeginPeriod(1);

            alreadyRegister = true;
            RootPath        = null;

            if (absolutePath == ":1") 
                RootPath = Directory.GetCurrentDirectory();
            else if (absolutePath != ":2")
                RootPath = absolutePath;
            else
            {
                var current = Directory.GetCurrentDirectory();
                var probe   = Path.Combine("Libs", Environment.Is64BitProcess ? "x64" : "x86", "FFmpeg");

                while (current != null)
                {
                    var ffmpegBinaryPath = Path.Combine(current, probe);
                    if (Directory.Exists(ffmpegBinaryPath)) { RootPath = ffmpegBinaryPath; break; }
                    current = Directory.GetParent(current)?.FullName;
                }
            }

            if (RootPath == null) throw new Exception("Failed to register FFmpeg libraries");

            try
            {
                #if DEBUG
                    av_log_set_level(verbosity);
                    av_log_set_callback(Utils.FFmpeg.ffmpegLogCallback);
                #endif

                uint ver = avformat_version();
                Log($"[FFmpegLoader] [Version: {ver >> 16}.{ver >> 8 & 255}.{ver & 255}] [Location: {RootPath}]");
            } catch (Exception e) { throw new Exception("Failed to register FFmpeg libraries", e); }
        }

        internal static void AddPlayer(Player player)
        {
            lock (Players)
                Players.Add(player);
        }
        internal static int GetPlayerPos(int playerId)
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
                Log($"Disposing {playerId}");
                int pos = GetPlayerPos(playerId);
                if (pos == -1) return;

                Player player = Players[pos];
                player.DisposeInternal();
                Players.RemoveAt(pos);
                Log($"Disposed {playerId}");
            }
        }

        static void StartThread()
        {
            if (tMaster != null && tMaster.IsAlive)
                return;

            tMaster = new Thread(() => { MasterThread(); });
            tMaster.Name = "Master";
            tMaster.IsBackground = true;
            tMaster.Start();
        }
        static void MasterThread()
        {
            Log("MasterThread Started");

            int curLoop = 0;
            int secondLoops = 1000 / UIRefreshInterval; 

            Dictionary<int, PlayerStats> stats = new Dictionary<int, PlayerStats>();

            do
            {
                try
                {
                    if (Players.Count == 0)
                    {
                        Thread.Sleep(UIRefreshInterval);
                        continue;
                    }

                    curLoop++;

                    lock (Players)
                        foreach (Player player in Players)
                        {
                            /* Every UIRefreshInterval */
                            if (player.Config.Player.ActivityMode)
                                player.Activity.mode = player.Activity.Check();

                            /* Every Second */
                            if (curLoop == secondLoops)
                            {
                                if (player.Config.Player.Stats)
                                {
                                    if (!stats.ContainsKey(player.PlayerId))
                                        stats.Add(player.PlayerId, new PlayerStats());

                                    var curStats = stats[player.PlayerId];

                                    long curTotalBytes  = player.VideoDemuxer.TotalBytes + player.AudioDemuxer.TotalBytes + player.SubtitlesDemuxer.TotalBytes;
                                    long curVideoBytes  = player.VideoDemuxer.VideoBytes + player.AudioDemuxer.VideoBytes + player.SubtitlesDemuxer.VideoBytes;
                                    long curAudioBytes  = player.VideoDemuxer.AudioBytes + player.AudioDemuxer.AudioBytes + player.SubtitlesDemuxer.AudioBytes;

                                    player.bitRate      = (curTotalBytes - curStats.TotalBytes) * 8 / 1000.0;
                                    player.Video.bitRate= (curVideoBytes - curStats.VideoBytes) * 8 / 1000.0;
                                    player.Audio.bitRate= (curAudioBytes - curStats.AudioBytes) * 8 / 1000.0;

                                    curStats.TotalBytes = curTotalBytes;
                                    curStats.VideoBytes = curVideoBytes;
                                    curStats.AudioBytes = curAudioBytes;

                                    if (player.IsPlaying)
                                    {
                                        player.Video.fpsCurrent = player.Video.FramesDisplayed - curStats.FramesDisplayed;
                                        curStats.FramesDisplayed = player.Video.FramesDisplayed;
                                    }
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
                                if (player.Activity.mode != player.Activity._Mode && (player.Config.Player.ActivityMode || isCursorHidden))
                                {
                                    player.Activity.Mode = player.Activity.Mode;

                                    if (player.IsFullScreen && player.Activity.Mode == ActivityMode.Idle && player.Config.Player.MouseBindigns.HideCursorOnFullScreenIdle)
                                    {
                                        while (Utils.NativeMethods.ShowCursor(false) >= 0) { }
                                        isCursorHidden = true;
                                    }    
                                    else if (isCursorHidden && player.Activity.Mode == ActivityMode.FullActive)
                                    {
                                        while (Utils.NativeMethods.ShowCursor(true) < 0) { }
                                        isCursorHidden = false;
                                    }   
                                }

                                // CurTime / Buffered Duration (+Duration for HLS)
                                if (!UICurTimePerSecond || (!player.IsPlaying && player.MainDemuxer.IsRunning && player.CanPlay))
                                    player.UpdateCurTime();

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
                    Thread.Sleep(UIRefreshInterval);

                } catch { curLoop = 0; }

            } while (UIRefresh);

            Log("MasterThread Stopped");
        }
        
        private static void Log(string msg) { Utils.Log($"[Master] {msg}"); }
    }
}
