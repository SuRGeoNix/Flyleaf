using System.ComponentModel;
using FlyleafLib.MediaPlayer;
using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    readonly FlyleafPlaybackStatusOverlay playbackDisplay = new();
    Status previousFeedbackStatus = Status.Stopped;
    long seekFeedbackGeneration;
    string lastFeedbackError = "";
    long lastFeedbackErrorAt;

    void ObservePlaybackFeedback(bool attach)
    {
        if (overlayPlayer is not { } p) return;
        if (attach)
        {
            p.Activity.PropertyChanged += DisplayActivityChanged;
            p.OperationCancellationRequested += DisplayOpenInterruptionRequested;
            p.Opening += DisplayOpening; p.OpenCompleted += DisplayOpenCompleted;
            p.OpenPlaylistItemCompleted += DisplayOpenCompleted; p.OpenSessionCompleted += DisplayOpenCompleted;
            p.OpenAudioStreamCompleted += DisplayOpenCompleted; p.OpenVideoStreamCompleted += DisplayOpenCompleted; p.OpenSubtitlesStreamCompleted += DisplayOpenCompleted;
            p.OpenExternalAudioStreamCompleted += DisplayOpenCompleted; p.OpenExternalVideoStreamCompleted += DisplayOpenCompleted; p.OpenExternalSubtitlesStreamCompleted += DisplayOpenCompleted;
            p.BufferingStarted += DisplayBuffering; p.BufferingCompleted += DisplayBuffered; p.PlaybackStopped += DisplayStopped;
            p.SeekStarted += DisplaySeekStarted; p.SeekCompleted += DisplaySeekFinished; p.SeekResultCompleted += DisplaySeekCompleted;
            p.SnapshotCompleted += DisplaySnapshot;
            Queue.PropertyChanged += DisplayQueueChanged;
            previousFeedbackStatus = p.Status; lastFeedbackError = "";
            UpdateQueueDisplay(p);
        }
        else
        {
            p.Activity.PropertyChanged -= DisplayActivityChanged;
            p.OperationCancellationRequested -= DisplayOpenInterruptionRequested;
            p.Opening -= DisplayOpening; p.OpenCompleted -= DisplayOpenCompleted;
            p.OpenPlaylistItemCompleted -= DisplayOpenCompleted; p.OpenSessionCompleted -= DisplayOpenCompleted;
            p.OpenAudioStreamCompleted -= DisplayOpenCompleted; p.OpenVideoStreamCompleted -= DisplayOpenCompleted; p.OpenSubtitlesStreamCompleted -= DisplayOpenCompleted;
            p.OpenExternalAudioStreamCompleted -= DisplayOpenCompleted; p.OpenExternalVideoStreamCompleted -= DisplayOpenCompleted; p.OpenExternalSubtitlesStreamCompleted -= DisplayOpenCompleted;
            p.BufferingStarted -= DisplayBuffering; p.BufferingCompleted -= DisplayBuffered; p.PlaybackStopped -= DisplayStopped;
            p.SeekStarted -= DisplaySeekStarted; p.SeekCompleted -= DisplaySeekFinished; p.SeekResultCompleted -= DisplaySeekCompleted;
            p.SnapshotCompleted -= DisplaySnapshot;
            Queue.PropertyChanged -= DisplayQueueChanged;
        }
    }
    void PostPlaybackFeedback(object? sender, Action<Player> update)
    {
        if (Dispatcher.HasShutdownStarted) return;
        var expected = overlayPlayer; int generation = Volatile.Read(ref textPlayerGeneration);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (disposed || !IsLoaded || generation != Volatile.Read(ref textPlayerGeneration) || expected is not { IsDisposed: false } || expected != Player || !ReferenceEquals(sender, expected)) return;
            update(expected);
        }));
    }
    void UpdatePlaybackDisplay()
    {
        if (overlayPlayer is not { IsDisposed: false } p) { playbackDisplay.Reset(); return; }
        bool bar = osdBarVisible;
        bool image = FlyleafQueueProgression.IsImageFormat(p.MainDemuxer?.Name);
        bool singleFrameImage = image && p.Video.FramesTotal == 1 && !p.Audio.IsOpened;
        // Match slideshow progression: synthetic one-frame durations do not establish timed playback.
        bool clockAvailable = !image || p.Audio.IsOpened || p.CurTime > 0 || p.Duration > 0 && !singleFrameImage;
        playbackDisplay.Update(new(p.Status, p.CanPlay, p.Activity.Mode, p.CurTime, p.Duration, p.IsLive && !image, p.IsRecording, bar, clockAvailable && !Queue.IsOpening));
    }
    void DisplayPlayerProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Player.Status) && sender is Player player && player.Status is Status.Opening or Status.Stopped or Status.Failed)
            Interlocked.Increment(ref seekFeedbackGeneration);
        if (e.PropertyName is nameof(Player.CurTime) or nameof(Player.Duration) or nameof(Player.IsLive) or nameof(Player.CanPlay) or nameof(Player.Status) or nameof(Player.IsRecording))
            PostPlaybackFeedback(sender, p =>
            {
                if (p.Status == Status.Opening && previousFeedbackStatus != Status.Opening)
                { playbackDisplay.NewInput(); osd.Clear(); statusOsd.Clear(); lastFeedbackError = ""; }
                if (p.Status is Status.Opening or Status.Stopped or Status.Failed) playbackDisplay.CancelOperations();
                UpdatePlaybackDisplay();
                if (p.Status != previousFeedbackStatus)
                {
                    previousFeedbackStatus = p.Status;
                    statusOsd.ClearStatusMessage();
                    if (p.Status == Status.Paused) statusOsd.ShowMessage("Paused", status: true);
                    if (p.Status == Status.Ended && !FlyleafQueueProgression.IsImageFormat(p.MainDemuxer?.Name) && !(Queue.AutoAdvance && Queue.CanNext) && !p.LoopPlayback) statusOsd.ShowMessage("Ended", status: true);
                }
            });
        if (e.PropertyName == nameof(Player.LastError))
            PostPlaybackFeedback(sender, p =>
            {
                if (string.IsNullOrWhiteSpace(p.LastError))
                {
                    playbackDisplay.ClearError();
                    osd.ClearError();
                    lastFeedbackError = "";
                }
                else
                    ReportPlaybackError(p.LastError, p.Status == Status.Failed || !p.CanPlay);
            });
        if (e.PropertyName is nameof(Player.LoopPlayback) or nameof(Player.ReversePlayback))
            PostPlaybackFeedback(sender, p => osd.ShowMessage(e.PropertyName == nameof(Player.LoopPlayback) ? p.LoopPlayback ? "Repeat item on" : "Repeat item off" : p.ReversePlayback ? "Reverse playback on" : "Reverse playback off"));
    }
    void DisplayActivityChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Activity.Mode)) PostPlaybackFeedback(overlayPlayer, _ => UpdatePlaybackDisplay()); }
    void DisplayOpenInterruptionRequested(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref seekFeedbackGeneration);
        PostPlaybackFeedback(sender, _ => playbackDisplay.CancelOperations());
    }
    void DisplayOpening(object? sender, OpeningArgs e)
    {
        if (!e.IsSubtitles) Interlocked.Increment(ref seekFeedbackGeneration);
        PostPlaybackFeedback(sender, _ => { if (!e.IsSubtitles) { playbackDisplay.NewInput(); osd.Clear(); statusOsd.Clear(); lastFeedbackError = ""; } UpdatePlaybackDisplay(); });
    }
    void DisplayBuffering(object? sender, EventArgs e) => PostPlaybackFeedback(sender, _ => playbackDisplay.SetBuffering(true));
    void DisplayBuffered(object? sender, BufferingCompletedArgs e) => PostPlaybackFeedback(sender, p => { playbackDisplay.SetBuffering(false); ReportPlaybackError(e.Error, !p.CanPlay || p.Status == Status.Failed); UpdatePlaybackDisplay(); });
    void DisplayStopped(object? sender, PlaybackStoppedArgs e)
    {
        Interlocked.Increment(ref seekFeedbackGeneration);
        PostPlaybackFeedback(sender, _ => { playbackDisplay.CancelOperations(); ReportPlaybackError(e.Error, blocking: true); UpdatePlaybackDisplay(); });
    }
    void DisplaySeekStarted(object? sender, int ms)
    {
        long generation = Interlocked.Increment(ref seekFeedbackGeneration);
        PostPlaybackFeedback(sender, p =>
        {
            if (generation != Volatile.Read(ref seekFeedbackGeneration) || !p.CanPlay || p.Status is Status.Opening or Status.Stopped or Status.Failed) return;
            playbackDisplay.Seek(true, ms);
        });
    }
    void DisplaySeekFinished(object? sender, int ms)
    {
        Interlocked.Increment(ref seekFeedbackGeneration);
        PostPlaybackFeedback(sender, _ => playbackDisplay.Seek(false, ms));
    }
    void DisplaySeekCompleted(object? sender, FlyleafSeekResult result)
    {
        Interlocked.Increment(ref seekFeedbackGeneration);
        PostPlaybackFeedback(sender, p =>
        {
            int ms = result.Milliseconds;
            playbackDisplay.Seek(false, ms); UpdatePlaybackDisplay();
            if (ms < 0 && !result.Cancelled) ReportPlaybackError("Seek failed.", false);
            else if (ms >= 0 && p.Chapters?.LastOrDefault(c => c.StartTime <= ms * TimeSpan.TicksPerMillisecond && ms * TimeSpan.TicksPerMillisecond - c.StartTime < TimeSpan.TicksPerSecond / 2) is { } chapter)
                osd.ShowMessage("Chapter · " + chapter.Title);
        });
    }
    void DisplayOpenCompleted(object? sender, object e)
    {
        // Capture this operation's result before the next open changes player state.
        bool cancelled = sender is Player player && player.WasOpenCancelled(e);
        PostPlaybackFeedback(sender, p =>
        {
            string? error = e switch
            {
                FlyleafLib.MediaPlayer.OpenCompletedArgs r => r.Error, OpenPlaylistItemCompletedArgs r => r.Error, OpenSessionCompletedArgs r => r.Error,
                StreamOpenedArgs r => r.Error, ExternalStreamOpenedArgs r => r.Error, _ => null
            };
            if (!cancelled && !string.IsNullOrWhiteSpace(error)) ReportPlaybackError(error, p.Status == Status.Failed || !p.CanPlay);
            else if (!cancelled && p.Status != Status.Opening && e is OpenAudioStreamCompletedArgs or OpenExternalAudioStreamCompletedArgs)
                osd.ShowMessage("Audio · " + StreamCaption(e));
            else if (!cancelled && p.Status != Status.Opening && e is OpenSubtitlesStreamCompletedArgs or OpenExternalSubtitlesStreamCompletedArgs)
                osd.ShowMessage("Subtitles · " + StreamCaption(e));
            UpdatePlaybackDisplay();
        });
    }
    static string StreamCaption(object result) => result switch
    {
        StreamOpenedArgs { Stream: { } stream } => string.Join(" · ", new[] { stream.Language?.ToString(), string.IsNullOrWhiteSpace(stream.Title) ? $"Track {stream.StreamIndex + 1}" : stream.Title }.Where(s => !string.IsNullOrWhiteSpace(s))),
        OpenExternalAudioStreamCompletedArgs { ExtStream: { } stream } => $"External · {stream.Language}",
        OpenExternalSubtitlesStreamCompletedArgs { ExtStream: { Title: { Length: > 0 } title } } => title,
        ExternalStreamOpenedArgs => "External track",
        _ => "Track selected"
    };
    void ReportPlaybackError(string? error, bool blocking)
    {
        if (string.IsNullOrWhiteSpace(error)) return;
        long now = Environment.TickCount64;
        if (lastFeedbackError == error && now - lastFeedbackErrorAt < 500) { if (blocking) playbackDisplay.Error(error); return; }
        lastFeedbackError = error; lastFeedbackErrorAt = now;
        Information.ReportOperationError(error);
        playbackDisplay.Error(error, blocking);
    }
    void DisplaySnapshot(object? sender, SnapshotCompletedArgs e) => PostPlaybackFeedback(sender, _ =>
    {
        if (!e.Success) ReportPlaybackError(e.Error, false);
        else { Information.ReportCapture(e.Filename); osd.ShowMessage("Snapshot saved · " + System.IO.Path.GetFileName(e.Filename)); }
    });
    void UpdateQueueDisplay(Player p)
    {
        int number = Queue.CurrentItemNumber, total = Queue.TotalItems;
        bool opening = Queue.IsOpening;
        var source = number > 0 && total > 1 ? Queue.Sources.FirstOrDefault(s => number >= s.ItemNumber && number - s.ItemNumber < Math.Max(1, s.Children.Count)) : null;
        if (source == null)
        {
            playbackDisplay.UpdateQueue("", opening);
            return;
        }
        int position = number - source.ItemNumber;
        var item = (source.Id, position);
        string? title = source.Children.Count > 0 ? source.Children[position].Title : source.Title;
        if (!opening && ReferenceEquals(source, Queue.CurrentSource) && source.Children.Count == 0 && !source.HasCustomTitle && p.Playlist.Selected is { Title: { Length: > 0 } resolvedTitle })
            title = resolvedTitle;
        string text = $"{number} / {total}";
        if (!string.IsNullOrWhiteSpace(title)) text += " · " + title.Trim();
        playbackDisplay.UpdateQueue(text, opening);
    }
    void DisplayQueueChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((e.PropertyName is nameof(FlyleafQueue.ItemCountText) or nameof(FlyleafQueue.IsOpening)) && overlayPlayer is { IsDisposed: false } p)
            UpdateQueueDisplay(p);
        if (e.PropertyName is nameof(FlyleafQueue.IsAutoAdvancing) or nameof(FlyleafQueue.IsOpening)) UpdatePlaybackDisplay();
        if (e.PropertyName == nameof(FlyleafQueue.Repeat))
            ShowOSD(Queue.Repeat ? "Repeat queue on" : "Repeat queue off");
        else if (e.PropertyName == nameof(FlyleafQueue.AutoAdvance))
            ShowOSD(Queue.AutoAdvance ? "Auto-next on" : "Auto-next off");
    }
}
