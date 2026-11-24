using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using FlyleafLib.MediaFramework;
using FlyleafLib.MediaFramework.MediaContext;

namespace Flyleaf.Controls.WPF;

public enum FlyleafDownloadState
{
    Queued, Opening, Downloading, Pausing, Paused, Resuming, Stopping, Completed, Stopped, Failed
}

/// <summary>UI-thread-owned download job. A paused job retains its concurrency slot.</summary>
public sealed class FlyleafDownloadJob : NotifyPropertyChanged
{
    internal Config Config { get; }
    internal Downloader? Worker;
    internal bool StopRequested, Scheduled;
    internal bool RecordingLimitReached;
    internal DateTimeOffset? RecordingStartedAt, FinishedAt;
    public DateTimeOffset? StartAt => Request.StartAt;
    public TimeSpan? RecordingDuration => Request.RecordingDuration;
    internal DateTimeOffset? RecordUntil => RecordingDuration is { } length && (StartAt ?? RecordingStartedAt) is { } from ? from + length : null;
    internal Task Control = Task.CompletedTask;
    internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Input { get; }
    internal FlyleafDownloadRequest Request { get; }
    public string Folder => Request.Folder;
    string outputPath = "", resolvedTitle = "";
    public string OutputPath { get => outputPath; private set { if (Set(ref outputPath, value)) Raise(nameof(Name)); } }
    public string Name => OutputPath.Length != 0 ? Path.GetFileName(OutputPath) : Request.Name ?? (resolvedTitle.Length != 0 ? resolvedTitle : Input);
    internal void SetOutput(string path, string title) { resolvedTitle = title; OutputPath = path; Raise(nameof(Name)); }
    FlyleafDownloadState state;
    public FlyleafDownloadState State { get => state; internal set { if (Set(ref state, value)) { Raise(nameof(Detail)); Raise(nameof(IsIndeterminate)); } } }
    string error = "";
    public string Error { get => error; internal set { if (Set(ref error, value)) Raise(nameof(Detail)); } }
    double percentage;
    public double Percentage { get => percentage; private set => Set(ref percentage, value); }
    long duration, curTime, size, packetWriteErrors;
    double rate;
    public bool HasOutput { get; internal set; }
    public bool IsIndeterminate => State == FlyleafDownloadState.Downloading && duration <= 0 && !RecordingDuration.HasValue;
    public string Detail
    {
        get
        {
            if (State == FlyleafDownloadState.Failed) return "Failed · " + Error;
            if (State == FlyleafDownloadState.Queued && StartAt is { } scheduled)
                return (Scheduled ? (scheduled > DateTimeOffset.UtcNow ? "Scheduled" : "Waiting for download slot") : "Queued") + " · " + scheduled.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                    (RecordUntil is { } until ? " – " + until.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "");
            if (State is FlyleafDownloadState.Queued or FlyleafDownloadState.Opening) return State.ToString();
            var time = FormatTime(curTime) + (duration > 0 ? " / " + FormatTime(duration) : "") +
                (RecordingDuration.HasValue ? $" · {Percentage:0.0}% of recording window" : duration > 0 ? $" · {Percentage:0.0}%" : "");
            return $"{State} · {time} · {size / 1048576.0:0.0} MiB" +
                (State == FlyleafDownloadState.Downloading ? $" · {rate / 1048576.0:0.0} MiB/s" : "") +
                (!Terminal && RecordingDuration.HasValue && RecordUntil is { } end ? $" · Ends {end.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "") +
                (packetWriteErrors > 0 ? $" · {packetWriteErrors} packet write error(s)" : "") +
                (Error.Length > 0 ? " · " + Error : "");
        }
    }
    internal bool Terminal => State is FlyleafDownloadState.Completed or FlyleafDownloadState.Stopped or FlyleafDownloadState.Failed;
    internal FlyleafDownloadJob(FlyleafDownloadRequest request, Config config)
    {
        Input = request.Input; Request = request; resolvedTitle = request.Title ?? ""; Config = config;
        if (request.Name != null && request.Extension != null) outputPath = Path.Combine(request.Folder, request.Name + "." + request.Extension);
    }

    internal void Snapshot(Downloader d)
    {
        duration = d.Duration; curTime = d.CurTime;
        Percentage = RecordingDuration is { } length && (StartAt ?? RecordingStartedAt) is { } from
            ? Math.Clamp(((FinishedAt ?? DateTimeOffset.UtcNow) - from).TotalSeconds / length.TotalSeconds * 100, 0, 100) : d.DownloadPercentage;
        size = d.OutputFileSize; rate = d.BytesPerSecond; packetWriteErrors = d.PacketWriteErrors;
        Raise(nameof(Detail)); Raise(nameof(IsIndeterminate));
    }
    internal void RefreshSchedule() => Raise(nameof(Detail));
    static string FormatTime(long ticks)
    {
        var t = TimeSpan.FromTicks(Math.Max(0, ticks));
        return $"{(long)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }
}

/// <summary>Share one instance across player windows. All public operations run on its owning dispatcher.</summary>
public sealed class FlyleafDownloadManager : NotifyPropertyChanged
{
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly ObservableCollection<FlyleafDownloadJob> jobs = [];
    readonly DispatcherTimer refresh;
    int concurrency = 2;
    public static string DefaultDownloadFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    string downloadFolder = DefaultDownloadFolder;
    public string DownloadFolder
    {
        get => downloadFolder;
        set { dispatcher.VerifyAccess(); Set(ref downloadFolder, Path.GetFullPath(value)); }
    }
    bool shuttingDown;
    public ReadOnlyObservableCollection<FlyleafDownloadJob> Jobs { get; }
    public event EventHandler? Changed;
    public int Concurrency
    {
        get => concurrency;
        set { dispatcher.VerifyAccess(); if (Set(ref concurrency, Math.Clamp(value, 1, 8))) { Pump(); Notify(); } }
    }
    public bool HasPending => jobs.Any(j => !j.Terminal);
    public bool CanAdd => !shuttingDown;
    public string Summary => $"{jobs.Count} downloads · {jobs.Count(j => j.Scheduled && !j.Terminal && j.State != FlyleafDownloadState.Queued)} active (including paused) · {jobs.Count(j => j.State == FlyleafDownloadState.Queued)} queued";

    public FlyleafDownloadManager()
    {
        Jobs = new(jobs);
        refresh = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        refresh.Tick += (_, _) => Tick();
    }

    /// <summary>Prepare independent configuration. Do not attach the player or share its plugin dictionaries.</summary>
    public static Config CreateConfig(Config? source = null)
    {
        var config = new Config();
        if (source != null)
        {
            foreach (var plugin in source.Plugins)
            {
                var options = new Utils.ObservableDictionary<string, string>();
                foreach (var option in plugin.Value) options.Add(option.Key, option.Value);
                config.Plugins[plugin.Key] = options;
            }
            config.Demuxer.FormatOpt = new(source.Demuxer.FormatOpt);
            config.Demuxer.AudioFormatOpt = new(source.Demuxer.AudioFormatOpt);
            config.Demuxer.DefaultHTTPQueryToUnderlying = source.Demuxer.DefaultHTTPQueryToUnderlying;
            config.Demuxer.ExtraHTTPQueryParamsToUnderlying = new(source.Demuxer.ExtraHTTPQueryParamsToUnderlying);
            config.Video.MaxVerticalResolutionCustom = source.Video.MaxVerticalResolutionCustom;
        }
        config.Demuxer.FormatOptToUnderlying = true;
        config.Demuxer.BufferDuration = TimeSpan.FromSeconds(60).Ticks;
        config.Demuxer.ReadTimeout = TimeSpan.FromSeconds(60).Ticks;
        return config;
    }

    public FlyleafDownloadJob Add(string input, string output, Config? source = null, bool start = true)
    {
        dispatcher.VerifyAccess();
        output = Path.GetFullPath(output);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) throw new ArgumentException("Specify an output file extension.");
        return Add(new(input, Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output), Path.GetExtension(output).TrimStart('.')), source, start);
    }

    internal FlyleafDownloadJob Add(FlyleafDownloadRequest request, Config? source = null, bool start = true)
    {
        dispatcher.VerifyAccess();
        if (shuttingDown) throw new InvalidOperationException("Downloads are shutting down.");
        request = request.Validate();
        if (request.Name != null && request.Extension != null)
        {
            string output = Path.Combine(request.Folder, request.Name + "." + request.Extension);
            if (File.Exists(output) || Directory.Exists(output) || jobs.Any(j => !j.Terminal &&
                (StringComparer.OrdinalIgnoreCase.Equals(j.OutputPath, output) ||
                 j.Request.Name != null && j.Request.Extension != null && StringComparer.OrdinalIgnoreCase.Equals(Path.Combine(j.Folder, j.Request.Name + "." + j.Request.Extension), output))))
                throw new IOException("The output file already exists or is reserved by another download.");
        }
        var job = new FlyleafDownloadJob(request, CreateConfig(source));
        jobs.Add(job);
        if (start || request.StartAt.HasValue) { job.Scheduled = true; Pump(); }
        Notify(); return job;
    }

    public void StartAll()
    {
        dispatcher.VerifyAccess(); if (shuttingDown) return;
        foreach (var job in jobs.ToArray()) Start(job);
    }
    public void Start(FlyleafDownloadJob job)
    {
        dispatcher.VerifyAccess(); if (shuttingDown || !jobs.Contains(job)) return;
        if (job.State == FlyleafDownloadState.Queued) { job.Scheduled = true; Pump(); Notify(); }
        else if (job.State == FlyleafDownloadState.Paused && job.Worker is { } d)
            job.Control = ChangeStateAsync(job, d, FlyleafDownloadState.Resuming, FlyleafDownloadState.Downloading, d.Start);
    }
    public void Pause(FlyleafDownloadJob job)
    {
        dispatcher.VerifyAccess();
        if (jobs.Contains(job) && job.State == FlyleafDownloadState.Downloading && job.Worker is { } d)
            job.Control = ChangeStateAsync(job, d, FlyleafDownloadState.Pausing, FlyleafDownloadState.Paused, d.Pause);
    }
    async Task ChangeStateAsync(FlyleafDownloadJob job, Downloader d, FlyleafDownloadState transition, FlyleafDownloadState target, Action action)
    {
        job.Error = ""; job.State = transition; Notify();
        try
        {
            await Task.Run(action);
            if (!job.Terminal && !job.StopRequested && d.Result == DownloadResult.None)
            {
                job.State = target == FlyleafDownloadState.Paused && d.Status != Status.Paused ? FlyleafDownloadState.Downloading :
                    target == FlyleafDownloadState.Downloading && d.Status == Status.Paused ? FlyleafDownloadState.Paused : target;
                job.Snapshot(d);
            }
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
            if (!job.Terminal && !job.StopRequested) job.State = d.Status == Status.Paused ? FlyleafDownloadState.Paused : FlyleafDownloadState.Downloading;
        }
        Notify();
    }

    public async Task StopAsync(FlyleafDownloadJob job)
    {
        dispatcher.VerifyAccess();
        if (!jobs.Contains(job) || job.Terminal) return;
        if (job.StopRequested) { await job.Finished.Task; return; }
        job.StopRequested = true;
        if (job.State == FlyleafDownloadState.Queued) { Finish(job, FlyleafDownloadState.Stopped); return; }
        bool opening = job.State == FlyleafDownloadState.Opening;
        job.State = FlyleafDownloadState.Stopping; Notify();
        // Opening is not interruptible by this manager yet. RunAsync observes the request before/after starting output.
        if (!opening)
        {
            await job.Control;
            if (job.Worker is { } d && !job.Terminal)
                try { await Task.Run(d.Stop); }
                catch (Exception ex)
                {
                    job.Error = ex.Message;
                    if (!job.Terminal && d.Result == DownloadResult.None)
                    {
                        job.StopRequested = false; job.RecordingLimitReached = false;
                        job.State = d.Status == Status.Paused ? FlyleafDownloadState.Paused : FlyleafDownloadState.Downloading;
                        Notify(); return;
                    }
                }
        }
        await job.Finished.Task;
    }

    public async Task StopAllAsync()
    {
        dispatcher.VerifyAccess();
        // Prevent Pump from starting a queued job while another job is being stopped.
        if (shuttingDown) return;
        shuttingDown = true; Notify();
        try { await Task.WhenAll(jobs.Where(j => !j.Terminal).ToArray().Select(StopAsync)); }
        finally { shuttingDown = false; Notify(); }
    }
    public void Remove(FlyleafDownloadJob job)
    {
        dispatcher.VerifyAccess(); if (job.Terminal && jobs.Remove(job)) Notify();
    }
    public void ClearFinished()
    {
        dispatcher.VerifyAccess(); foreach (var job in jobs.Where(j => j.Terminal).ToArray()) jobs.Remove(job); Notify();
    }
    void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var job in jobs.ToArray())
        {
            if (job.Terminal) continue;
            if (job.Worker is { } d) job.Snapshot(d);
            else if (job.StartAt.HasValue) job.RefreshSchedule();
            if (job.State != FlyleafDownloadState.Queued && !job.StopRequested && job.RecordUntil is { } end && now >= end)
            {
                job.RecordingLimitReached = true;
                _ = StopAsync(job);
            }
        }
        Pump();
    }
    void UpdateRefresh()
    {
        if (jobs.Any(j => !j.Terminal && j.Scheduled)) refresh.Start();
        else refresh.Stop();
    }
    void Pump()
    {
        if (shuttingDown) { UpdateRefresh(); return; }
        var now = DateTimeOffset.UtcNow;
        bool changed = false;
        int running = jobs.Count(j => j.Scheduled && !j.Terminal && j.State != FlyleafDownloadState.Queued);
        // Expired windows are never started. Give due recordings priority over ordinary queued downloads.
        foreach (var job in jobs.Where(j => j.State == FlyleafDownloadState.Queued && j.Scheduled && j.RecordUntil is { } end && now >= end).ToArray())
        {
            job.Error = "The recording window ended before a download slot became available.";
            job.FinishedAt = now; job.State = FlyleafDownloadState.Failed; job.Finished.TrySetResult(); changed = true;
        }
        foreach (var job in jobs.Where(j => j.State == FlyleafDownloadState.Queued && j.Scheduled && (!j.StartAt.HasValue || j.StartAt.Value <= now))
            .OrderBy(j => j.StartAt ?? DateTimeOffset.MaxValue).ToArray())
        {
            if (running++ >= concurrency) break;
            job.State = FlyleafDownloadState.Opening;
            _ = RunAsync(job); changed = true;
        }
        UpdateRefresh();
        if (changed) Notify();
    }
    bool OpeningAborted(FlyleafDownloadJob job, out FlyleafDownloadState outcome)
    {
        if (!job.StopRequested && job.RecordUntil is { } end && DateTimeOffset.UtcNow >= end)
            job.StopRequested = job.RecordingLimitReached = true;
        outcome = job.RecordingLimitReached ? FlyleafDownloadState.Failed : FlyleafDownloadState.Stopped;
        if (job.RecordingLimitReached) job.Error = "The recording window ended before the input could be opened.";
        return job.StopRequested;
    }
    async Task RunAsync(FlyleafDownloadJob job)
    {
        await Task.Yield();
        Downloader? d = null;
        EventHandler<bool>? completed = null;
        var outcome = FlyleafDownloadState.Failed;
        try
        {
            if (OpeningAborted(job, out var abortOutcome)) { outcome = abortOutcome; return; }
            d = new Downloader(job.Config); job.Worker = d; refresh.Start();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            completed = (_, _) => completion.TrySetResult();
            d.DownloadCompleted += completed;
            var error = await Task.Run(() => job.Request.Target is { } target ? target.Open(d) : d.Open(job.Input));
            if (OpeningAborted(job, out abortOutcome)) { outcome = abortOutcome; return; }
            if (error != null) { job.Error = error; return; }
            var reservedPaths = jobs.Where(j => j != job && !j.Terminal && j.OutputPath.Length != 0)
                .Select(j => j.OutputPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var output = await Task.Run(() =>
            {
                job.Request.Selection?.Apply(d);
                if (job.Request.Selection == null)
                {
                    if (job.Request.AudioOnly) FlyleafDownloadSelection.ApplyAudioOnly(d);
                    else if (job.Request.VideoOnly) FlyleafDownloadSelection.ApplyVideoOnly(d);
                }
                string title = d.DecCtx.Playlist.Selected?.Title ?? "";
                string path = job.Request.ReserveOutput(d, title, reservedPaths);
                return (Path: path, Title: title);
            });
            job.SetOutput(output.Path, output.Title); job.HasOutput = true; Notify();
            if (OpeningAborted(job, out abortOutcome))
            {
                await Task.Run(() => File.Delete(output.Path)); job.HasOutput = false; outcome = abortOutcome; return;
            }
            await Task.Run(() =>
            {
                string path = output.Path;
                d.Download(ref path, false);
            });
            job.RecordingStartedAt = DateTimeOffset.UtcNow;
            if (!job.StopRequested && job.RecordUntil is { } end && job.RecordingStartedAt.Value >= end)
                job.StopRequested = job.RecordingLimitReached = true;
            if (job.StopRequested) await Task.Run(d.Stop);
            else if (d.Result == DownloadResult.None) { job.State = FlyleafDownloadState.Downloading; Notify(); }
            await completion.Task;
            outcome = d.Result switch
            {
                DownloadResult.Completed => FlyleafDownloadState.Completed,
                DownloadResult.Stopped => job.RecordingLimitReached ? FlyleafDownloadState.Completed : FlyleafDownloadState.Stopped,
                _ => FlyleafDownloadState.Failed
            };
            if (d.LastError != null) job.Error = d.LastError;
        }
        catch (Exception ex) { job.Error = ex.Message; }
        finally
        {
            if (d != null)
            {
                await job.Control;
                if (completed != null) d.DownloadCompleted -= completed;
                try { await Task.Run(d.Dispose); }
                catch (Exception ex) { job.Error = ex.Message; outcome = FlyleafDownloadState.Failed; }
                job.FinishedAt = DateTimeOffset.UtcNow; job.Snapshot(d); job.Worker = null;
            }
            Finish(job, outcome);
        }
    }
    void Finish(FlyleafDownloadJob job, FlyleafDownloadState outcome)
    {
        job.FinishedAt ??= DateTimeOffset.UtcNow;
        job.State = outcome; job.Finished.TrySetResult();
        Pump(); Notify();
    }
    void Notify() { Raise(nameof(Summary)); Raise(nameof(HasPending)); Raise(nameof(CanAdd)); Changed?.Invoke(this, EventArgs.Empty); }
}
