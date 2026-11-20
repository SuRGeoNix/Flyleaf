using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlyleafLib;
using Flyleaf.Controls.WPF;
using FlyleafLib.MediaPlayer;

internal static class QueueImageChecks
{
    // Optional native probe. No host/window is created and normal editor checks
    // still run without starting the media engine.
    internal static void Run(string ffmpegDirectory, string? firstImage = null)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Review", "QueueMedia");
        Directory.CreateDirectory(directory);
        var pixels = Enumerable.Repeat((byte)96, 32 * 32 * 3).ToArray();
        var image = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgr24, null, pixels, 32 * 3);
        foreach (var (name, encoder) in new (string, BitmapEncoder)[]
            { ("first.jpg", new JpegBitmapEncoder()), ("second.png", new PngBitmapEncoder()), ("last.jpg", new JpegBitmapEncoder()) })
        {
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
        }
        Engine.Start(new EngineConfig
        {
            FFmpegPath = ffmpegDirectory, PluginsPath = ":Plugins", UIRefresh = true,
            LogOutput = Path.Combine(directory, "Playback.log"), LogLevel = LogLevel.Debug,
            FFmpegLoadProfile = Flyleaf.FFmpeg.LoadProfile.All
        });
        var watch = Stopwatch.StartNew();
        var player = new Player();
        long firstEndedAt = -1, firstClock = 0;
        // Capture completion before the queue's handler can dispatch its next open.
        player.PlaybackStopped += (_, e) =>
        {
            if (firstEndedAt < 0 && e.Success && player.Status == Status.Ended)
            { firstEndedAt = watch.ElapsedMilliseconds; firstClock = player.CurTime; }
        };
        var view = new FlyleafME { Player = player };
        var queue = view.Queue;
        try
        {
            queue.SetPlayer(player);
            queue.AutoAdvance = true;
            queue.SlideShowIntervalProvider = () => 700;
            player.OpenCompleted += (_, e) => Console.WriteLine($"{watch.ElapsedMilliseconds} open success={e.Success} error={e.Error}");
            player.BufferingStarted += (_, _) => Console.WriteLine($"{watch.ElapsedMilliseconds} buffering started");
            player.BufferingCompleted += (_, e) => Console.WriteLine($"{watch.ElapsedMilliseconds} buffering completed error={e.Error}");
            player.PlaybackStopped += (_, e) => Console.WriteLine($"{watch.ElapsedMilliseconds} stopped status={player.Status} duration={player.Duration} time={player.CurTime} format={player.MainDemuxer?.Name} error={e.Error}");
            var first = queue.Add(firstImage ?? Path.Combine(directory, "first.jpg"));
            var second = queue.Add(Path.Combine(directory, "second.png"));
            var last = queue.Add(Path.Combine(directory, "last.jpg"));
            long firstAt = -1, secondAt = -1;
            string? firstFormat = null;
            bool automaticTransition = false, automaticWake = false;
            queue.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FlyleafQueue.IsAutoAdvancing) && queue.IsAutoAdvancing)
                    automaticTransition = true;
                if (e.PropertyName != nameof(FlyleafQueue.CurrentSource)) return;
                if (ReferenceEquals(queue.CurrentSource, first) && firstAt < 0)
                { firstFormat = player.MainDemuxer?.Name; firstAt = watch.ElapsedMilliseconds; player.Activity.Timeout = 500; player.Activity.ForceIdle(); }
                if (ReferenceEquals(queue.CurrentSource, second) && secondAt < 0) secondAt = watch.ElapsedMilliseconds;
            };
            player.Activity.PropertyChanged += (_, e) => { if (automaticTransition && e.PropertyName == nameof(FlyleafLib.MediaPlayer.Activity.Mode) && player.Activity.Mode == ActivityMode.FullActive) automaticWake = true; };
            queue.Open(first);
            string previous = "";
            bool sawSecond = false, sawLast = false;
            while (watch.Elapsed < TimeSpan.FromSeconds(12))
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
                Dispatcher.PushFrame(frame);
                object? Field(string name) => typeof(FlyleafQueue).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue);
                var snapshot = $"source={queue.CurrentSource?.Title} status={player.Status} format={player.MainDemuxer?.Name} frames={player.Video.FramesTotal} duration={player.Duration} time={player.CurTime} live={player.IsLive} pending={queue.IsOpening} automatic={queue.IsAutoAdvancing} activity={player.Activity.Mode} error={Field("progressionError")} buffering={Field("progressionBuffering")} timer={((DispatcherTimer)Field("slideTimer")!).IsEnabled} message={queue.Message}";
                if (snapshot != previous) { Console.WriteLine($"{watch.ElapsedMilliseconds} {snapshot}"); previous = snapshot; }
                sawSecond |= ReferenceEquals(queue.CurrentSource, second);
                sawLast |= ReferenceEquals(queue.CurrentSource, last);
                if (sawSecond && sawLast) break;
            }
            if (!sawSecond || !sawLast) throw new Exception("Native JPEG/PNG queue progression failed. See the event trace and Review/QueueMedia/Playback.log.");
            Console.WriteLine($"First image display time: {secondAt - firstAt} ms (700 ms interval).");
            if (firstFormat is "webp_anim" or "apng" or "gif")
            {
                if (firstClock <= 0 || firstEndedAt < 0 || secondAt < firstEndedAt)
                    throw new Exception("Animated image advanced without finishing its progressing playback.");
                Console.WriteLine($"Animation ended at {firstClock / (double)TimeSpan.TicksPerSecond:0.###} s before the next item opened.");
            }
            else if (firstAt < 0 || secondAt - firstAt < 650) throw new Exception("JPEG bypassed the display interval.");
            if (!automaticTransition || automaticWake) throw new Exception("Automatic queue advancement woke player activity.");
            queue.AutoAdvance = false;
            bool keyboardWake = false;
            player.Activity.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FlyleafLib.MediaPlayer.Activity.Mode) && player.Activity.Mode == ActivityMode.FullActive) keyboardWake = true;
            };
            foreach (bool previousItem in new[] { true, false })
            {
                player.Activity.Timeout = 5000; player.Activity.ForceIdle();
                // Exercise the core's keyboard activity refresh without relying on OS modifier state.
                Player.KeyDown(player, System.Windows.Input.Key.LeftCtrl);
                if (previousItem) view.PreviousItem(); else view.NextItem();
                var until = Stopwatch.StartNew();
                while (until.ElapsedMilliseconds < 650)
                {
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
                    Dispatcher.PushFrame(frame);
                }
                Console.WriteLine($"Keyboard {(previousItem ? "previous" : "next")}: activity={player.Activity.Mode}");
                if (keyboardWake || player.Activity.Mode != ActivityMode.Active ||
                    !ReferenceEquals(queue.CurrentSource, previousItem ? second : last))
                    throw new Exception("Keyboard item navigation forced full activity or failed to keep keyboard Active.");
            }
            Console.WriteLine("Native JPEG → PNG → JPEG queue checks passed. No windows were shown.");
        }
        finally
        {
            view.Dispose(); player.Dispose(); Engine.Config.UIRefresh = false;
        }
    }
}