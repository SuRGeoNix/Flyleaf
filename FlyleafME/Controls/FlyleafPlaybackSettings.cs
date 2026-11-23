using System.Windows;
using System.Windows.Controls;
using FlyleafLib.MediaPlayer;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>Playback preferences for the current player; no runtime command state.</summary>
public sealed class FlyleafPlaybackSettings : FlyleafSettingsEditor
{
    protected override NotifyPropertyChanged SelectConfig(Player target) => target.Config.Playback;
    protected override void Build()
    {
        BeginPage("Playback");
        if (config is not Config.PlaybackConfig c) return;
        Config Defaults() => FlyleafSettingsReset.GetDefaults(this);
        var general = Group("Opening & seeking", () => { var defaults = Defaults().Playback; c.AutoPlay = defaults.AutoPlay; c.SeekAccurate = defaults.SeekAccurate; });
        Toggle(general, "Play automatically", "Starts playback after opening media or seeking from the end.", () => c.AutoPlay, v => c.AutoPlay = v);
        Toggle(general, "Accurate seeking", "Applies to subsequent seeks. Decodes up to the requested video position.", () => c.SeekAccurate, v => c.SeekAccurate = v);
        var d = player!.Config.Demuxer; Observe(d);
        FlyleafDurationRangeEditor? bufferRange = null;
        var buffering = Group("Buffering", () =>
        {
            var defaults = Defaults();
            d.BufferDuration = defaults.Demuxer.BufferDuration; c.MinBufferDuration = defaults.Playback.MinBufferDuration;
            bufferRange?.RefreshValues(true);
        });
        bufferRange = new(() => (c.MinBufferDuration / (double)TimeSpan.TicksPerSecond, d.BufferDuration / (double)TimeSpan.TicksPerSecond),
            (minimum, maximum) => { d.BufferDuration = Ticks(maximum); c.MinBufferDuration = Ticks(minimum); }, action => Run(action));
        buffering.Children.Add(bufferRange); RefreshWith(() => bufferRange.RefreshValues());
        buffering.Children.Add(Text("Playback starts or re-buffers after the minimum is available; the maximum caps buffered media. Live latency control can retune this range.", true));
        var steps = Group("Seek & speed steps", () =>
        {
            var defaults = Defaults().Playback;
            c.SeekOffset = defaults.SeekOffset; c.SeekOffset2 = defaults.SeekOffset2; c.SeekOffset3 = defaults.SeekOffset3;
            c.SpeedOffset = defaults.SpeedOffset; c.SpeedOffset2 = defaults.SpeedOffset2;
        });
        steps.Children.Add(Text("Step sizes used by the library's seek and speed actions. Keyboard and mouse assignments belong in Bindings.", true));
        Duration(steps, "Seek step", "seconds", () => c.SeekOffset, v => c.SeekOffset = v, positive: true);
        Duration(steps, "Seek step 2", "seconds", () => c.SeekOffset2, v => c.SeekOffset2 = v, positive: true);
        Duration(steps, "Seek step 3", "seconds", () => c.SeekOffset3, v => c.SeekOffset3 = v, positive: true);
        Number(steps, "Speed step", "×", () => c.SpeedOffset, v => c.SpeedOffset = v, 0.001, 15.875);
        Number(steps, "Speed step 2", "×", () => c.SpeedOffset2, v => c.SpeedOffset2 = v, 0.001, 15.875);
        var capture = Group("Snapshots & recordings", () =>
        {
            var defaults = Defaults().Playback;
            c.FolderSnapshots = defaults.FolderSnapshots; c.FolderRecordings = defaults.FolderRecordings;
            c.SnapshotFormat = defaults.SnapshotFormat;
        });
        Folder(capture, "Snapshot folder", () => c.FolderSnapshots, v => c.FolderSnapshots = v);
        Choice(capture, "Snapshot format", () => c.SnapshotFormat, v => c.SnapshotFormat = v,
            [("bmp", "BMP"), ("png", "PNG"), ("jpg", "JPEG"), ("jpeg", "JPEG (.jpeg)")]);
        Folder(capture, "Recording folder", () => c.FolderRecordings, v => c.FolderRecordings = v);
        capture.Children.Add(Text("Used when no output filename is supplied. A save dialog can choose a different location and format.", true));
        var advancedBody = new StackPanel();
        var advanced = new Expander { Header = Text("Advanced"), Content = advancedBody, Margin = new Thickness(0, 22, 0, 0) };
        advanced.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush");
        Children.Add(advanced);
        // Do not offer an opaque blanket reset: MaxLatency=0 resets runtime speed in the core.
        var latency = Group("Live latency", null, advancedBody);
        Toggle(latency, "Zero latency", "Live video only. Briefly restarts playback if playing. Also updates the low-delay preference for the next video decoder open.", () => c.ZeroLatency, v => c.ZeroLatency = v, background: true);
        var latencyRange = new FlyleafDurationRangeEditor(
            () => (c.MinLatency / (double)TimeSpan.TicksPerSecond, c.MaxLatency / (double)TimeSpan.TicksPerSecond),
            (minimum, maximum) => { if (c.MaxLatency != Ticks(maximum)) c.MaxLatency = Ticks(maximum); c.MinLatency = Ticks(minimum); },
            action => Run(action), maximumOff: true);
        latency.Children.Add(latencyRange); RefreshWith(() => latencyRange.RefreshValues());
        Duration(latency, "Speed change interval", "seconds", () => c.LatencySpeedChangeInterval, v => c.LatencySpeedChangeInterval = v, positive: true);
        latency.Children.Add(Text("Maximum latency enables catch-up speed, may enlarge demuxer buffering and reduce the minimum buffer. Disabling it sets playback speed to 1×. It also updates the low-delay preference for the next video decoder open; zero latency writes that same preference. Buffer changes are not automatically undone.", true));
        var performance = Group("Scheduling & time display", () => { var defaults = Defaults().Playback; c.ThreadPriority = defaults.ThreadPriority; c.UICurTime = defaults.UICurTime; }, advancedBody);
        Choice(performance, "Playback thread priority", () => c.ThreadPriority, v => c.ThreadPriority = v,
            Enum.GetValues<ThreadPriority>().Select(v => (v, v.ToString())).ToArray());
        performance.Children.Add(Text("Applies when the next playback thread starts; changing this does not restart playback.", true));
        Choice(performance, "Position updates", () => c.UICurTime, v => c.UICurTime = v,
            [(UIRefreshType.PerFrame, "Every frame"), (UIRefreshType.PerFrameSecond, "Every media second (frame clock)"),
             (UIRefreshType.PerUIRefreshInterval, "Every engine UI interval"), (UIRefreshType.PerUISecond, "Every media second (UI clock)")]);
        performance.Children.Add(Text("Applies during playback. Every-frame updates can increase UI work.", true));
        Refresh();
    }
    static long Ticks(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
}
