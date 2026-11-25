using System.Diagnostics;
using System.Windows;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib;

/// <summary>
/// Flyleaf Engine
/// </summary>
public static class Engine
{
    /// <summary>
    /// Engine has been loaded and is ready for use
    /// </summary>
    public static bool              IsLoaded        { get; private set; }

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
    public static List<Player>      Players         { get; private set; } = [];

    public static event EventHandler
                    Loaded;

    internal static LogHandler
                    Log;

    static Thread   tMaster;
    static object   lockEngine = new();
    static bool     isLoading;
    static int      timePeriod;
    static int      threadExecutionState; // ES_CONTINUOUS

    /// <summary>
    /// Initializes Flyleaf's Engine (Must be called from UI thread)
    /// </summary>
    /// <param name="config">Engine's configuration</param>
    public static void Start(EngineConfig config = null) => StartInternal(config);

    /// <summary>
    /// Initializes Flyleaf's Engine Async (Must be called from UI thread)
    /// </summary>
    /// <param name="config">Engine's configuration</param>
    public static void StartAsync(EngineConfig config = null) => StartInternal(config, true);

    /// <summary>
    /// Requests timeBeginPeriod(1)
    /// </summary>
    public static void TimeBeginPeriod1()
    {
        lock (lockEngine)
        {
            timePeriod++;

            if (timePeriod == 1)
            {
                #if DEBUG
                Log.Trace("timeBeginPeriod(1)");
                #endif

                _ = NativeMethods.TimeBeginPeriod(1);
            }
        }
    }

    /// <summary>
    /// Stops previously requested timeBeginPeriod(1)
    /// </summary>
    public static void TimeEndPeriod1()
    {
        lock (lockEngine)
        {
            timePeriod--;

            if (timePeriod == 0)
            {
                #if DEBUG
                Log.Trace("timeEndPeriod(1)");
                #endif

                _ = NativeMethods.TimeEndPeriod(1);
            }
        }
    }

    /// <summary>
    /// Requests SetThreadExecutionState
    /// </summary>
    public static void ThreadExecutionStateBegin()
    {
        lock (lockEngine)
        {
            threadExecutionState++;

            if (threadExecutionState == 1)
            {
                #if DEBUG
                Log.Trace("ThreadExecutionStateBegin");
                #endif

                _ = NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS | NativeMethods.EXECUTION_STATE.ES_SYSTEM_REQUIRED | (Config.KeepDisplayActive ? NativeMethods.EXECUTION_STATE.ES_DISPLAY_REQUIRED : 0));
            }
        }
    }

    /// <summary>
    /// Stops previously requested SetThreadExecutionState
    /// </summary>
    public static void ThreadExecutionStateEnd()
    {
        lock (lockEngine)
        {
            threadExecutionState--;

            if (threadExecutionState == 0)
            {
                #if DEBUG
                Log.Trace("ThreadExecutionStateEnd");
                #endif

                _ = NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS);
            }
        }
    }

    private static void StartInternal(EngineConfig config = null, bool async = false)
    {
        if (Application.Current == null)
            _ = new Application();

        uiDispatcher = Application.Current.Dispatcher;

        UIInvokeIfRequired(() =>
        {
            lock (lockEngine)
            {
                if (isLoading)
                    return;

                isLoading = true;

                Config = config ?? new EngineConfig();

                StartInternalUI();

                if (async)
                    Task.Run(() => StartInternalNonUI());
                else
                    StartInternalNonUI();
            }
        });
    }

    private static void StartInternalUI()
    {
        Application.Current.Exit += (o, e) =>
        {
            Config.UIRefresh = false;
            Config.UIRefreshInterval = 1;

            while (Players.Count != 0)
                Players[0].Dispose();
        };

        SetOutput();
        Log     = new("[FlyleafEngine] ");
        Audio   = new();
        Video   = new();
    }

    private static void StartInternalNonUI()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Log.Info($"FlyleafLib {version.Major }.{version.Minor}.{version.Build}");

        FFmpeg  = new();
        Plugins = new();
        IsLoaded= true;
        Loaded?.Invoke(null, null);

        if (Config.UIRefresh)
            StartThread();
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
        Player player;
        Log.Trace($"Disposing {playerId}");

        lock (Players)
        {
            int pos = GetPlayerPos(playerId);
            if (pos == -1) return;

            player = Players[pos];
            Players.RemoveAt(pos);
        }

        player.DisposeInternal();
        Log.Trace($"Disposed {playerId}");
    }

    internal static void StartThread()
    {
        if (!Config.UIRefresh || (tMaster != null && tMaster.IsAlive))
            return;

        tMaster = new Thread(() => { MasterThread(); })
        {
            Name = "FlyleafEngine",
            IsBackground = true
        };
        tMaster.Start();
    }
    internal static void MasterThread()
    {
        // TBR: Auto Stop/Start instead of UIRefresh config (based on current Players status/mode/bufferduration/stats etc)
        Log.Info("Thread started");

        int curLoop = 0;
        int secondLoops = Math.Max(1, 1000 / Math.Max(1, Config.UIRefreshInterval));

        do
        {
            try
            {
                if (Players.Count == 0)
                {
                    Thread.Sleep(Config.UIRefreshInterval);
                    continue;
                }

                bool refreshSecond = ++curLoop == secondLoops;
                if (refreshSecond) curLoop = 0;

                lock (Players)
                    foreach (var player in Players)
                    {
                        /* Every UIRefreshInterval */
                        player.Activity.RefreshMode();

                        if (refreshSecond && player.stats is { } stats)
                            SampleStatistics(player, stats);
                    }

                void UIAction()
                {
                    try
                    {
                        foreach (var player in Players)
                        {
                            var isPlaying = player.status == Status.Playing;
                            /* Every UIRefreshInterval */
                            var config = player.Config.Playback;

                            // Activity Mode Refresh & Hide Mouse Cursor (FullScreen only)
                            if (player.Activity.mode != player.Activity._Mode)
                                player.Activity.SetMode();

                            // Buffered Duration Refresh from Demuxer (TBR: RefreshType?)
                            player.BufferedDuration = player.MainDemuxer.BufferedDuration;

                            if ((isPlaying && config.UICurTime == UIRefreshType.PerUIRefreshInterval) ||
                                (refreshSecond && config.UICurTime == UIRefreshType.PerUISecond))
                                player.SetCurTime();

                            // Stats Refresh (BitRates / FrameDisplayed / FramesDropped / FPS)
                            if (refreshSecond && player.stats is { } stats)
                            {
                                player.BitRate = player.bitRate = stats.TotalBitRate;
                                player.Video.BitRate = player.Video.bitRate = stats.VideoBitRate;
                                player.Audio.BitRate = player.Audio.bitRate = stats.AudioBitRate;
                                player.Video.FPSCurrent = player.Video.fpsCurrent = stats.FramesPerSecond;

                                if (isPlaying) // Otherwise Screamers should fire the last update
                                {
                                    player.Audio.FramesDisplayed= player.Audio.FramesDisplayed;
                                    player.Audio.FramesDropped  = player.Audio.FramesDropped;
                                    if (player.Renderer?.SwapChain is { Disposed: false })
                                        (player.Video.FramesDisplayed,player.Video.FramesDropped) = player.FramesDisplayedDropped(); // dynamic update to be closer to 'now'
                                }
                            }
                        }
                    }
                    catch { }
                }

                UI(UIAction);
                Thread.Sleep(Config.UIRefreshInterval);

            } catch { curLoop = 0; }

        } while (Config.UIRefresh);

        Log.Info("Thread stopped");
    }

    static void SampleStatistics(Player player, PlayerStats stats)
    {
        long timestamp = Stopwatch.GetTimestamp();
        var video = player.VideoDemuxer;
        var audio = player.AudioDemuxer;
        var subtitles = player.SubtitlesDemuxer;
        double seconds = stats.SampleBitRates(timestamp,
            video.LifetimeBytesRead + audio.LifetimeBytesRead + subtitles.LifetimeBytesRead + player.DataDemuxer.LifetimeBytesRead,
            video.VideoPackets.LifetimeBytesEnqueued + audio.VideoPackets.LifetimeBytesEnqueued + subtitles.VideoPackets.LifetimeBytesEnqueued,
            video.AudioPackets.LifetimeBytesEnqueued + audio.AudioPackets.LifetimeBytesEnqueued + subtitles.AudioPackets.LifetimeBytesEnqueued);

        if (!ReferenceEquals(player.stats, stats)) return;
        if (player.Renderer?.SwapChain is { Disposed: false } swapChain)
        {
            var presentCount = swapChain.GetFrameStatistics().PresentCount;
            stats.FramesPerSecond = seconds > 0 && stats.FramesDisplayed is long previous && presentCount >= previous
                ? (presentCount - previous) / seconds : 0;
            stats.FramesDisplayed = presentCount;
        }
        else
        {
            stats.FramesPerSecond = 0;
            stats.FramesDisplayed = null;
        }
    }
}
