using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins;

public class YoutubeDL : PluginBase, IOpen, ISuggestPlaylistItem, ISuggestExternalAudio, ISuggestExternalVideo
{
    public new int Priority { get; set; } = 1999;

    private const string Executable = "yt-dlp.exe";
    private static readonly JsonSerializerOptions JsonOptions = new();

    // Skip known direct media URLs without a network request.
    private static readonly HashSet<string> DirectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv",
        ".ts", ".m2ts", ".mts", ".mpg", ".mpeg", ".ogv", ".flv", ".3gp",
        ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wav",
        ".wma", ".ape", ".aiff", ".alac", ".ac3", ".dts",
        ".m3u8", ".mpd", ".m3u", ".pls", ".asx"
    };

    private readonly object sessionLock = new();
    private readonly ConcurrentDictionary<int, byte> terminatingPids = new();
    private ExtractionSession activeSession;

    private sealed class PlaylistEntry
    {
        public PlaylistEntry(string url, YoutubeDLJson info = null)
        {
            Url = url;
            Info = info;
        }

        public readonly string Url;
        public YoutubeDLJson Info;  // Only populated for items actually opened.
        public DateTimeOffset ResolvedAt;
    }

    private sealed class ExtractionSession
    {
        public ExtractionSession(long counter, string url)
        {
            Counter = counter;
            Url = url;
            RequestedItem = RequestedItemHint.FromUrl(url);
        }

        public readonly long Counter;
        public readonly string Url;
        public readonly RequestedItemHint RequestedItem;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource<bool> FirstItemOrDone =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> RequestedItemOrDone =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly HashSet<Process> Processes = new();

        public Process ItemProcess;
        public PlaylistItem FirstItem;
        public PlaylistItem PreferredItem;
        public int ItemsAdded;
        public string LastError;
    }

    private sealed class ProcessResult
    {
        public bool NeedsImpersonation;
        public string LastError;
        public int ItemsRead;
    }

    // Match the originally requested playlist item using its URL or ID.
    private sealed class RequestedItemHint
    {
        private readonly Uri input;
        private readonly Dictionary<string, string> query;
        private readonly HashSet<string> ids;

        public bool HasHint { get; }

        private RequestedItemHint(Uri input, Dictionary<string, string> query,
            HashSet<string> ids, bool hasHint)
        {
            this.input = input;
            this.query = query;
            this.ids = ids;
            HasHint = hasHint;
        }

        private static bool IsPlaylistKey(string key)
            => key.Equals("list", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("playlist", StringComparison.OrdinalIgnoreCase);

        private static Dictionary<string, string> ParseQuery(Uri uri)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = Uri.UnescapeDataString(part[..equals].Replace('+', ' '));
                string value = Uri.UnescapeDataString(part[(equals + 1)..].Replace('+', ' '));
                if (key.Length > 0 && value.Length > 0)
                    result.TryAdd(key, value);
            }
            return result;
        }

        public static RequestedItemHint FromUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return new RequestedItemHint(null, null, null, false);

            var parameters = ParseQuery(uri);
            bool isPlaylistContext = parameters.Keys.Any(IsPlaylistKey);
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pair in parameters)
                if (!IsPlaylistKey(pair.Key) && pair.Value.Length >= 5)
                    ids.Add(pair.Value);

            // Accept item IDs in paths without relying on a site-specific query name.
            string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && segments[^1].Length >= 5 &&
                !new[] { "playlist", "playlists", "collection", "collections", "album", "albums" }
                    .Contains(segments[^2], StringComparer.OrdinalIgnoreCase))
                ids.Add(Uri.UnescapeDataString(segments[^1]));

            // Playlist-only URLs do not imply an item preference.
            bool hasHint = isPlaylistContext && ids.Count > 0;
            return new RequestedItemHint(uri, parameters, ids, hasHint);
        }

        public bool Matches(YoutubeDLJson info, string entryUrl)
        {
            if (!HasHint)
                return false;

            if (!string.IsNullOrEmpty(info.id) && ids.Contains(info.id))
                return true;

            if (!Uri.TryCreate(entryUrl, UriKind.Absolute, out Uri candidate) ||
                !input.Host.Equals(candidate.Host, StringComparison.OrdinalIgnoreCase) ||
                !input.AbsolutePath.TrimEnd('/').Equals(candidate.AbsolutePath.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
                return false;

            // All media-identifying candidate query parameters must match.
            bool matched = false;
            foreach (var pair in ParseQuery(candidate))
            {
                if (IsPlaylistKey(pair.Key))
                    continue;
                if (!query.TryGetValue(pair.Key, out string value) ||
                    !value.Equals(pair.Value, StringComparison.Ordinal))
                    return false;
                matched = true;
            }
            return matched;
        }
    }

    public override Dictionary<string, string> GetDefaultOptions() => new()
    {
        { "ExtraArguments", "" },
        { "OpenTimeoutSeconds", "60" },
        { "ForceExtract", "false" },
        { "ForwardCookies", "true" },
        { "AllowTranslatedSubtitles", "false" },
        { "PlaylistBatchSize", "20" },
        { "PlaylistBatchDelayMs", "50" },
        { "PlaylistMaxItems", "100" },
        { "StreamRefreshMinutes", "15" },
        { "MaxAvailabilityWaitSeconds", "30" },
        { "PreferredItemWaitMs", "5000" }
    };

    public override void OnInitializing() => DisposeInternal();
    public override void Dispose() => DisposeInternal();

    private bool IsCurrent(ExtractionSession session)
        => ReferenceEquals(Volatile.Read(ref activeSession), session)
           && !session.Cancellation.IsCancellationRequested
           && !Handler.Interrupt
           && session.Counter == Handler.OpenCounter;

    private bool IsAttemptCurrent(ExtractionSession session, long itemCounter)
        => IsCurrent(session) && (itemCounter < 0 || Handler.OpenItemCounter == itemCounter);

    // Poll for interruption even when yt-dlp produces no output.
    private bool WaitForSignal(ExtractionSession session, Task signal, int timeoutMs)
    {
        var elapsed = Stopwatch.StartNew();
        while (IsCurrent(session) && !signal.IsCompleted)
        {
            int remaining = (int)Math.Min(int.MaxValue, timeoutMs - elapsed.ElapsedMilliseconds);
            if (remaining <= 0)
                break;
            signal.Wait(Math.Min(remaining, 100));
        }
        return IsCurrent(session) && signal.IsCompleted;
    }

    // Terminate asynchronously; the extraction worker owns Process disposal.
    private void Kill(Process process)
    {
        if (process == null)
            return;

        int pid;
        try
        {
            if (process.HasExited)
                return;
            pid = process.Id;
        }
        catch (InvalidOperationException) { return; } // Includes ObjectDisposedException.
        catch (Win32Exception) { return; }
        catch (COMException e) when (e.HResult == unchecked((int)0x80070006)) { return; }

        // Avoid duplicate taskkill launches during stop/switch races.
        if (!terminatingPids.TryAdd(pid, 0))
            return;

        _ = Task.Run(() =>
        {
            try
            {
                using var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {pid} /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (killer == null)
                    return;

                // Wait for taskkill only in its background task.
                if (!killer.WaitForExit(5000))
                {
                    try { killer.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    catch (COMException e) when (e.HResult == unchecked((int)0x80070006)) { }
                    Log.Warn($"yt-dlp: taskkill timed out for PID {pid}");
                }
                // The parent may already have exited; cleanup is best-effort.
            }
            catch (Win32Exception e)
            {
                Log.Warn($"yt-dlp: could not start taskkill for PID {pid}: {e.Message}");
            }
            catch (InvalidOperationException) { }
            catch (COMException e) when (e.HResult == unchecked((int)0x80070006)) { }
            finally
            {
                terminatingPids.TryRemove(pid, out _);
            }
        });
    }

    private void Stop(ExtractionSession session)
    {
        if (session == null)
            return;

        session.Cancellation.Cancel();
        session.FirstItemOrDone.TrySetResult(true);
        session.RequestedItemOrDone.TrySetResult(true);

        Process[] processes;
        lock (session.Processes)
            processes = session.Processes.ToArray();
        foreach (Process process in processes)
            Kill(process);
    }

    public override void OnInitializingSwitch()
    {
        // Cancel item extraction without stopping playlist discovery.
        ExtractionSession session = Volatile.Read(ref activeSession);
        if (session != null)
            Kill(Volatile.Read(ref session.ItemProcess));
    }

    private int GetIntOption(string name, int defaultValue, int min, int max)
        => Options != null && Options.TryGetValue(name, out string text) &&
           int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, min, max) : defaultValue;

    private void DisposeInternal()
    {
        ExtractionSession old;
        lock (sessionLock)
        {
            old = activeSession;
            activeSession = null;
            Disposed = true;
        }

        // Never hold sessionLock while terminating a process or invoking callbacks.
        Stop(old);
    }

    public bool CanOpen()
    {
        if (Playlist.IOStream != null ||
            !Uri.TryCreate(Playlist.Url, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        if (Options != null && Options.TryGetValue("ForceExtract", out string forced) &&
            bool.TryParse(forced, out bool enabled) && enabled)
            return true;

        string ext = Path.GetExtension(uri.AbsolutePath);
        return !DirectExtensions.Contains(ext);
    }

    public OpenResults Open()
    {
        ExtractionSession session;
        ExtractionSession previous;

        lock (sessionLock)
        {
            previous = activeSession;
            session = new ExtractionSession(Handler.OpenCounter, Playlist.Url);
            activeSession = session;
            Disposed = false;
            Playlist.InputType = InputType.Web;
        }
        Stop(previous);

        try
        {
            _ = Task.Run(() => Extract(session));

            int timeoutSeconds = GetIntOption("OpenTimeoutSeconds", 60, 5, 600);

            bool firstReady = WaitForSignal(session, session.FirstItemOrDone.Task,
                timeoutSeconds * 1000);
            if (!IsCurrent(session))
            {
                Stop(session);
                return null; // Flyleaf interrupted / opened another URL.
            }
            if (!firstReady)
            {
                Log.Warn($"yt-dlp timed out before producing a playable item ({timeoutSeconds}s)");
                Stop(session);
                return null; // Let the regular FFmpeg plugin try the URL.
            }

            if (Volatile.Read(ref session.ItemsAdded) == 0)
            {
                if (!string.IsNullOrEmpty(session.LastError))
                    Log.Warn($"yt-dlp: {session.LastError}");
                return null;
            }

            // Wait briefly to find an explicitly requested playlist entry.
            if (session.RequestedItem.HasHint &&
                Volatile.Read(ref session.PreferredItem) == null)
            {
                int waitMs = GetIntOption("PreferredItemWaitMs", 5000, 0, 30000);
                if (waitMs > 0)
                    WaitForSignal(session, session.RequestedItemOrDone.Task, waitMs);
            }

            if (!IsCurrent(session))
            {
                Stop(session);
                return null;
            }

            if (session.RequestedItem.HasHint && Volatile.Read(ref session.PreferredItem) == null &&
                Logger.CanDebug)
                Log.Debug("yt-dlp: requested playlist item not discovered within the initial window; using first entry");

            return new OpenResults();
        }
        catch (Exception e)
        {
            Log.Error($"yt-dlp Open: {e.Message}");
            Stop(session);
            return new OpenResults(e.Message);
        }
    }

    private void Extract(ExtractionSession session)
    {
        try
        {
            // Flat playlists emit metadata; single videos may already have formats.
            int batchSize = GetIntOption("PlaylistBatchSize", 20, 1, 500);
            int batchDelay = GetIntOption("PlaylistBatchDelayMs", 50, 0, 1000);
            RunWithRetry(session, session.Url, flatPlaylist: true, isItem: false,
                info => AddDiscoveredItem(session, info, batchSize, batchDelay));

            if (IsCurrent(session) && Volatile.Read(ref session.ItemsAdded) > 0)
            {
                // Replace the expected total with accepted items when discovery completes.
                Playlist.ExpectingItems = Playlist.Items.Count;
                Handler.OnPlaylistCompleted();
            }
        }
        catch (Exception e)
        {
            if (IsCurrent(session))
                Log.Warn($"yt-dlp discovery stopped: {e.Message}");
        }
        finally
        {
            session.FirstItemOrDone.TrySetResult(true);
            session.RequestedItemOrDone.TrySetResult(true);
        }
    }

    private ProcessResult RunWithRetry(ExtractionSession session, string url, bool flatPlaylist,
        bool isItem, Action<YoutubeDLJson> onItem, long itemCounter = -1)
    {
        ProcessResult result = null;
        for (int attempt = 0; attempt < 2 && IsAttemptCurrent(session, itemCounter); attempt++)
        {
            result = RunProcess(session, url, flatPlaylist, isItem, attempt == 1, onItem, itemCounter);
            if (result.ItemsRead > 0 || !result.NeedsImpersonation || attempt != 0)
                break;

            if (IsAttemptCurrent(session, itemCounter))
                Log.Warn("yt-dlp generic extractor requested impersonation; retrying once");
        }
        return result;
    }

    private ProcessResult RunProcess(ExtractionSession session, string url, bool flatPlaylist,
        bool isItem, bool impersonate, Action<YoutubeDLJson> onItem, long itemCounter)
    {
        var result = new ProcessResult();
        using var process = new Process
        {
            StartInfo = CreateStartInfo(url, impersonate, flatPlaylist)
        };

        lock (session.Processes)
        {
            if (!IsAttemptCurrent(session, itemCounter))
                return result;
            session.Processes.Add(process);
            if (isItem)
                Volatile.Write(ref session.ItemProcess, process);
        }

        // Read process output asynchronously so cancellation cannot block on a line.
        var stdoutDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int acceptingOutput = 1;

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data == null)
            {
                stdoutDone.TrySetResult(true);
                return;
            }
            if (Volatile.Read(ref acceptingOutput) == 0 ||
                !IsAttemptCurrent(session, itemCounter) || args.Data.Length == 0)
                return;

            try
            {
                YoutubeDLJson info = JsonSerializer.Deserialize<YoutubeDLJson>(args.Data, JsonOptions);
                if (info == null || info._type == "playlist")
                    return;
                result.ItemsRead++;
                onItem(info);
            }
            catch (JsonException e)
            {
                if (IsAttemptCurrent(session, itemCounter))
                    Log.Warn($"yt-dlp invalid JSON: {e.Message}");
            }
            catch (Exception e)
            {
                if (IsAttemptCurrent(session, itemCounter))
                    Log.Warn($"yt-dlp item processing: {e.Message}");
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data == null)
            {
                stderrDone.TrySetResult(true);
                return;
            }
            if (Volatile.Read(ref acceptingOutput) == 0 ||
                !IsAttemptCurrent(session, itemCounter) || args.Data.Length == 0)
                return;

            string line = args.Data;
            if (line.Contains("generic:impersonate", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase))
                result.NeedsImpersonation = true;
            if (line.Contains("ERROR:", StringComparison.OrdinalIgnoreCase))
                result.LastError = SanitizeDiagnostic(line);
            if (Logger.CanDebug)
                Log.Debug($"[yt-dlp stderr] {SanitizeDiagnostic(line)}");
        };

        try
        {
            if (!IsAttemptCurrent(session, itemCounter))
                return result;

            process.Start();
            if (!IsAttemptCurrent(session, itemCounter))
            {
                Kill(process);
                return result;
            }

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            // Poll interruption without delaying the next item/open.
            var elapsed = Stopwatch.StartNew();
            int timeoutSeconds = isItem
                ? GetIntOption("OpenTimeoutSeconds", 60, 5, 600)
                : 0; // Playlist discovery may run after Open() has returned.
            bool timedOut = false;
            while (IsAttemptCurrent(session, itemCounter))
            {
                bool exited = process.WaitForExit(100);
                if (exited && stdoutDone.Task.IsCompleted && stderrDone.Task.IsCompleted)
                    break;

                if (timeoutSeconds > 0 && elapsed.Elapsed >= TimeSpan.FromSeconds(timeoutSeconds))
                {
                    timedOut = true;
                    result.LastError = $"yt-dlp item extraction timed out ({timeoutSeconds}s)";
                    break;
                }
            }

            if (!IsAttemptCurrent(session, itemCounter) || timedOut)
            {
                Kill(process);
                return result;
            }

            if (process.ExitCode != 0 && Logger.CanDebug)
                Log.Debug($"yt-dlp exit code: {process.ExitCode}");
        }
        catch (IOException) when (!IsAttemptCurrent(session, itemCounter)) { }
        catch (InvalidOperationException) when (!IsAttemptCurrent(session, itemCounter)) { }
        catch (Win32Exception) when (!IsAttemptCurrent(session, itemCounter)) { }
        catch
        {
            // Process.Dispose() does not terminate a running child.
            Kill(process);
            throw;
        }
        finally
        {
            Volatile.Write(ref acceptingOutput, 0);
            lock (session.Processes)
            {
                session.Processes.Remove(process);
                if (ReferenceEquals(session.ItemProcess, process))
                    session.ItemProcess = null;
            }
            if (!isItem && IsCurrent(session) && !string.IsNullOrEmpty(result.LastError))
                session.LastError = result.LastError;
        }

        return result;
    }

    private ProcessStartInfo CreateStartInfo(string url, bool impersonate, bool flatPlaylist)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Engine.Plugins.Folder, Name, Executable),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        start.ArgumentList.Add("--ignore-config");
        start.ArgumentList.Add("--no-progress");
        if (flatPlaylist)
        {
            start.ArgumentList.Add("--yes-playlist");
            start.ArgumentList.Add("--lazy-playlist");
            start.ArgumentList.Add("--flat-playlist");
            // Avoid loading unavailable YouTube entries.
            start.ArgumentList.Add("--compat-options");
            start.ArgumentList.Add("no-youtube-unavailable-videos");

            int maxItems = GetIntOption("PlaylistMaxItems", 100, 0, 100000);
            if (maxItems > 0)
            {
                start.ArgumentList.Add("--playlist-end");
                start.ArgumentList.Add(maxItems.ToString(CultureInfo.InvariantCulture));
            }
        }
        else
            start.ArgumentList.Add("--no-playlist");

        // One JSON object per item; never emit a giant single playlist JSON.
        start.ArgumentList.Add("--dump-json");

        // Skip YouTube translations unless enabled.
        bool allowTranslations = Options != null &&
            Options.TryGetValue("AllowTranslatedSubtitles", out string translatedSetting) &&
            bool.TryParse(translatedSetting, out bool translated) && translated;
        if (!allowTranslations)
        {
            start.ArgumentList.Add("--extractor-args");
            start.ArgumentList.Add("youtube:skip=translated_subs");
        }

        if (impersonate)
        {
            start.ArgumentList.Add("--extractor-args");
            start.ArgumentList.Add("generic:impersonate");
        }

        if (Options != null && Options.TryGetValue("ExtraArguments", out string extra))
            foreach (string arg in SplitArguments(extra))
                start.ArgumentList.Add(arg);

        start.ArgumentList.Add("--");
        start.ArgumentList.Add(url);
        return start;
    }

    // Tokenize ExtraArguments using Windows quoting rules.
    private static IEnumerable<string> SplitArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            yield break;

        int i = 0;
        while (i < commandLine.Length)
        {
            while (i < commandLine.Length && char.IsWhiteSpace(commandLine[i])) i++;
            if (i >= commandLine.Length) yield break;

            var result = new StringBuilder();
            bool quoted = false;
            while (i < commandLine.Length)
            {
                if (!quoted && char.IsWhiteSpace(commandLine[i]))
                    break;

                int slashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    slashes++;
                    i++;
                }

                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    result.Append('\\', slashes / 2);
                    if (slashes % 2 != 0)
                        result.Append('"');
                    else if (quoted && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                    {
                        result.Append('"');
                        i++;
                    }
                    else
                        quoted = !quoted;
                    i++;
                    continue;
                }

                result.Append('\\', slashes);
                if (i < commandLine.Length)
                    result.Append(commandLine[i++]);
            }

            yield return result.ToString();
        }
    }

    private static string SanitizeDiagnostic(string line)
    {
        // Avoid logging signed media URLs or cookie-bearing query parameters.
        return Regex.Replace(line, @"https?://[^\s\""'<>]+", "[url]", RegexOptions.IgnoreCase);
    }

    private void AddDiscoveredItem(ExtractionSession session, YoutubeDLJson info,
        int batchSize, int batchDelay)
    {
        if (!IsCurrent(session))
            return;

        string url = GetEntryUrl(info);
        if (url == null)
        {
            if (Logger.CanDebug)
                Log.Debug($"yt-dlp: skipping entry without a usable webpage URL ({info.id})");
            return;
        }

        // Do not treat flat playlist webpage metadata as playable formats.
        bool hasFormats = info._type != "url" && info._type != "url_transparent" &&
            (info.formats?.Count > 0 || !string.IsNullOrWhiteSpace(info.protocol)) &&
            Formats(info).Any(f => f != null && IsPlayable(f) &&
                                    (HasVideo(f) || HasAudio(f)));
        var entry = new PlaylistEntry(url, hasFormats ? info : null);
        var item = new PlaylistItem
        {
            Title = info.title ?? info.id ?? url,
            DirectUrl = url
        };

        if (hasFormats)
        {
            if (!FillItemStreams(item, info))
                return;
            entry.ResolvedAt = DateTimeOffset.UtcNow;
        }

        // This may include unavailable entries; it is only an estimate while loading.
        if (Playlist.ExpectingItems == 0 && info.playlist_count.GetValueOrDefault() > 0)
            Playlist.ExpectingItems = info.playlist_count.Value;

        if (string.IsNullOrEmpty(Playlist.Title))
            Playlist.Title = !string.IsNullOrEmpty(info.playlist_title)
                ? info.playlist_title : info.playlist;

        if (!IsCurrent(session))
            return;

        AddPlaylistItem(item, entry);
        int count = Interlocked.Increment(ref session.ItemsAdded);
        if (count == 1)
            Volatile.Write(ref session.FirstItem, item);

        if (session.RequestedItem.Matches(info, url) &&
            Interlocked.CompareExchange(ref session.PreferredItem, item, null) == null)
            session.RequestedItemOrDone.TrySetResult(true);

        session.FirstItemOrDone.TrySetResult(true);

        // Pace playlist updates to avoid flooding WPF notifications.
        if (count % batchSize == 0 && batchDelay > 0)
            session.Cancellation.Token.WaitHandle.WaitOne(batchDelay);
    }

    private static string GetEntryUrl(YoutubeDLJson info)
    {
        if (IsHttpUrl(info.webpage_url)) return info.webpage_url;
        if (IsHttpUrl(info.url)) return info.url;
        if (IsHttpUrl(info.original_url)) return info.original_url;

        // Flat YouTube entries can contain only a bare video id and an ie_key.
        string key = info.ie_key ?? info.extractor_key ?? info.extractor;
        if (string.Equals(key, "Youtube", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "YouTube", StringComparison.OrdinalIgnoreCase))
        {
            string id = !string.IsNullOrWhiteSpace(info.id) ? info.id : info.url;
            if (!string.IsNullOrEmpty(id) &&
                Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$"))
                return "https://www.youtube.com/watch?v=" + id;
        }
        return null;
    }

    // Resolve formats only when the playlist item opens.
    private bool FillItemStreams(PlaylistItem item, YoutubeDLJson info)
    {
        var formats = info.formats ?? new List<Format> { info };
        RecoverMissingWidths(formats);
        int added = 0;

        var seenFormats = new HashSet<string>(StringComparer.Ordinal);
        foreach (Format fmt in formats)
        {
            if (fmt == null || !IsPlayable(fmt)) continue;

            bool video = HasVideo(fmt);
            bool audio = HasAudio(fmt);
            if (!video && !audio) continue;

            string key = string.Join("\u001f", fmt.url, fmt.protocol, fmt.vcodec,
                fmt.acodec, fmt.language, fmt.dynamic_range, fmt.width, fmt.height, fmt.fps);
            if (!seenFormats.Add(key)) continue;

            string fallback = GetManifestFallback(fmt, info);
            ExternalStream ext;

            if (video)
            {
                ext = new ExternalVideoStream
                {
                    Url         = fmt.url,
                    UrlFallback = fallback,
                    Protocol    = fmt.protocol,
                    HasAudio    = audio,
                    BitRate     = KbpsToBps(fmt.vbr > 0 ? fmt.vbr : fmt.tbr),
                    Codec       = fmt.vcodec,
                    Width       = (int)fmt.width.GetValueOrDefault(),
                    Height      = (int)fmt.height.GetValueOrDefault(),
                    FPS         = fmt.fps.GetValueOrDefault()
                };
            }
            else
            {
                ext = new ExternalAudioStream
                {
                    Url         = fmt.url,
                    UrlFallback = fallback,
                    Protocol    = fmt.protocol,
                    BitRate     = KbpsToBps(fmt.abr > 0 ? fmt.abr : fmt.tbr),
                    Codec       = fmt.acodec,
                    Language    = Language.Get(fmt.language)
                };
            }

            AddHeaders(ext, fmt, info);
            AddExternalStream(ext, fmt, item);
            added++;
        }

        if (added == 0)
            return false;

        AddSubtitles(item, info);
        return true;
    }

    private sealed class SubtitleChoice
    {
        public Language Language;
        public string LanguageCode;
        public SubtitlesFormat Format;
        public bool Automatic;
        public bool Translated;
        public int FormatRank;
    }

    // Use only FFmpeg-supported subtitle formats.
    private void AddSubtitles(PlaylistItem item, YoutubeDLJson info)
    {
        var choices = new List<SubtitleChoice>();
        CollectSubtitles(info.subtitles, automatic: false, choices);
        CollectSubtitles(info.automatic_captions, automatic: true, choices);

        bool allowTranslations = Options != null &&
            Options.TryGetValue("AllowTranslatedSubtitles", out string option) &&
            bool.TryParse(option, out bool enabled) && enabled;

        // Prefer manual captions, then original automatic captions, then translations.
        var addedLanguages = new HashSet<Language>();
        foreach (Language language in Config.Subtitles.Languages)
        {
            if (!addedLanguages.Add(language))
                continue;

            SubtitleChoice best = choices
                .Where(c => c.Language == language && (allowTranslations || !c.Translated))
                .OrderBy(c => c.Automatic ? (c.Translated ? 2 : 1) : 0)
                .ThenBy(c => c.FormatRank)
                // Prefer exact language tags before regional variants.
                .ThenBy(c => c.LanguageCode.Contains('-') ?
                    (c.LanguageCode.EndsWith("-orig", StringComparison.OrdinalIgnoreCase) ? 1 : 2) : 0)
                .FirstOrDefault();

            if (best == null)
                continue;

            var subtitle = best.Format;
            bool hls = IsHls(subtitle.protocol) || IsM3u8Url(subtitle.url);
            var stream = new ExternalSubtitlesStream
            {
                Downloaded = true,
                Converted = true,
                Protocol = hls ? "m3u8_native" : subtitle.ext,
                Language = language,
                Url = subtitle.url
            };

            // Preserve subtitle-specific headers/cookies, with root fallback.
            AddHeaders(stream, new Format
            {
                url = subtitle.url,
                http_headers = subtitle.http_headers,
                cookies = subtitle.cookies
            }, info);
            AddExternalStream(stream, null, item);
        }
    }

    private void CollectSubtitles(Dictionary<string, List<SubtitlesFormat>> tracks,
        bool automatic, List<SubtitleChoice> choices)
    {
        if (tracks == null) return;

        foreach (var entry in tracks)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null ||
                entry.Key.Equals("live_chat", StringComparison.OrdinalIgnoreCase))
                continue;

            Language language = GetSubtitleLanguage(entry.Key);
            if (language == null || language == Language.Unknown ||
                !Config.Subtitles.Languages.Contains(language))
                continue;

            foreach (SubtitlesFormat subtitle in entry.Value)
            {
                if (subtitle == null || !IsHttpUrl(subtitle.url))
                    continue;

                int rank = SubtitleFormatRank(subtitle.ext);
                if (rank == int.MaxValue)
                    continue;

                // HLS VTT tracks may point to an m3u8 manifest.
                if ((IsHls(subtitle.protocol) || IsM3u8Url(subtitle.url)) &&
                    !subtitle.ext.Equals("vtt", StringComparison.OrdinalIgnoreCase))
                    continue;

                choices.Add(new SubtitleChoice
                {
                    Language = language,
                    LanguageCode = entry.Key,
                    Format = subtitle,
                    Automatic = automatic,
                    Translated = IsTranslatedSubtitle(entry.Key, subtitle),
                    FormatRank = rank
                });
            }
        }
    }

    private static int SubtitleFormatRank(string ext)
        => ext?.ToLowerInvariant() switch
        {
            "vtt" => 0,
            "srt" => 1,
            "ass" => 2,
            "ssa" => 3,
            _ => int.MaxValue
        };

    private static Language GetSubtitleLanguage(string code)
    {
        // Prefer the exact language tag (some extractors use regional tags).
        Language language = Language.Get(code);
        if (language != null && language != Language.Unknown)
            return language;

        // yt-dlp's "en-orig", "en-US" and "el-en" still describe the
        // output language of the subtitles, not the source language.
        int separator = code.IndexOfAny(new[] { '-', '_' });
        return separator > 0 ? Language.Get(code[..separator]) : language;
    }

    private static bool IsTranslatedSubtitle(string code, SubtitlesFormat subtitle)
    {
        // tlang= is the actual translation request, regardless of the tag.
        if (Uri.TryCreate(subtitle.url, UriKind.Absolute, out Uri uri))
        {
            foreach (string part in uri.Query.TrimStart('?').Split('&'))
            {
                int equals = part.IndexOf('=');
                if (equals > 0 && part[..equals].Equals("tlang", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Identify translations from metadata, not regional language tags.
        return !code.EndsWith("-orig", StringComparison.OrdinalIgnoreCase) &&
               subtitle.name?.Contains(" from ", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsM3u8Url(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri uri) &&
           uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

    // Infer missing widths only from consistent same-height renditions.
    private static void RecoverMissingWidths(List<Format> formats)
    {
        foreach (Format fmt in formats)
        {
            if (fmt == null || !HasVideo(fmt) || fmt.width.GetValueOrDefault() > 0 ||
                fmt.height.GetValueOrDefault() <= 0)
                continue;

            double height = fmt.height.Value;
            double[] widths = formats
                .Where(f => f != null && HasVideo(f) && f.height == height && f.width > 0)
                .Select(f => f.width.Value)
                .ToArray();

            if (widths.Length == 0)
                continue;

            double min = widths.Min();
            double max = widths.Max();
            if (max - min <= Math.Max(2.0, min * 0.01))
                fmt.width = widths[0];
        }
    }

    private static bool IsHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // Codec 'none' means absent; null means unknown.
    private static bool IsAbsent(string value)
        => string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnown(string value)
        => !string.IsNullOrWhiteSpace(value) && !IsAbsent(value) &&
           !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);

    private static bool IsAudioExtension(string ext)
        => ext?.ToLowerInvariant() is
            "m4a" or "m4b" or "m4p" or "m4r" or "isma" or "mp3" or
            "aac" or "flac" or "wav" or "wma" or "opus" or "ogg" or
            "oga" or "alac" or "aiff" or "aif" or "ape" or "ac3" or
            "eac3" or "dts" or "mka" or "amr" or "caf" or "dsf" or "dff";

    private static bool IsVideoExtension(string ext)
        => ext?.ToLowerInvariant() is
            "mp4" or "m4v" or "mov" or "mkv" or "webm" or "avi" or
            "wmv" or "flv" or "ts" or "m2ts" or "mts" or "mpg" or
            "mpeg" or "ogv" or "3gp" or "3g2" or "f4v" or "vob" or "ismv";

    private static bool HasVideo(Format fmt)
    {
        if (fmt == null || IsAbsent(fmt.vcodec))
            return false;

        if (IsKnown(fmt.vcodec) || fmt.width > 0 || fmt.height > 0 ||
            fmt.fps > 0 || fmt.vbr > 0)
            return true;

        // audio_ext='none' does not imply a muxed video lacks audio.
        if (IsAbsent(fmt.video_ext))
            return false;
        if (!string.IsNullOrWhiteSpace(fmt.video_ext))
            return true;

        // Fallback to the container extension when video metadata is absent.
        return !IsAudioExtension(fmt.ext) && IsVideoExtension(fmt.ext);
    }

    private static bool HasAudio(Format fmt)
    {
        if (fmt == null || IsAbsent(fmt.acodec))
            return false;

        if (IsKnown(fmt.acodec) || fmt.abr > 0 || fmt.asr > 0 ||
            fmt.audio_channels > 0)
            return true;

        // audio_ext identifies audio-only formats, not muxed audio.
        if (!string.IsNullOrWhiteSpace(fmt.audio_ext) && !IsAbsent(fmt.audio_ext))
            return true;

        if (IsAudioExtension(fmt.ext) && !HasVideo(fmt))
            return true;

        // A direct MP4/MOV may contain audio; FFmpeg must confirm.
        bool direct = IsDirectHttp(fmt.protocol) ||
            (string.IsNullOrWhiteSpace(fmt.protocol) && IsHttpUrl(fmt.url) &&
             !IsM3u8Url(fmt.url));
        return HasVideo(fmt) && direct && fmt.fragments?.Count is not > 0 &&
               (string.Equals(fmt.ext, "mp4", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fmt.ext, "m4v", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fmt.ext, "mov", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDirectHttp(string protocol)
        => string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase);

    private static bool IsHls(string protocol)
        => protocol != null && protocol.StartsWith("m3u8", StringComparison.OrdinalIgnoreCase);

    // Fragmented DASH formats are not directly playable URLs.
    private static bool IsPlayable(Format fmt)
    {
        if (!IsHttpUrl(fmt.url)) return false;

        string protocol = fmt.protocol ?? "";
        if (protocol.Contains("dash", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsHls(protocol)) return true;
        if (fmt.fragments?.Count > 0) return false;

        return protocol.Length == 0 || protocol.Equals("http", StringComparison.OrdinalIgnoreCase) ||
               protocol.Equals("https", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetManifestFallback(Format fmt, YoutubeDLJson info)
    {
        // Master-manifest fallback can select a different rendition.
        if (!IsHls(fmt.protocol)) return null;

        string manifest = IsHttpUrl(fmt.manifest_url) ? fmt.manifest_url :
            (IsHls(info.protocol) ? info.manifest_url : null);
        return IsHttpUrl(manifest) && !string.Equals(manifest, fmt.url, StringComparison.Ordinal)
            ? manifest : null;
    }

    private static long KbpsToBps(double? kbps)
    {
        double value = kbps.GetValueOrDefault();
        return value > 0 && double.IsFinite(value)
            ? (long)Math.Min(value * 1000, long.MaxValue) : 0;
    }

    // Preserve the existing public helper for callers that have no root info.
    public void AddHeaders(ExternalStream stream, Format fmt) => AddHeaders(stream, fmt, null);

    private static readonly char[] InvalidHeaderNameChars = ['\r', '\n', ':'];
    private static readonly char[] InvalidHeaderValueChars = ['\r', '\n'];
    private void AddHeaders(ExternalStream stream, Format fmt, YoutubeDLJson info)
    {
        // Prefer format headers, falling back to root metadata.
        var sourceHeaders = fmt.http_headers is { Count: > 0 }
            ? fmt.http_headers : info?.http_headers;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sourceHeaders != null)
            foreach (var header in sourceHeaders)
            {
                if (string.IsNullOrEmpty(header.Key) || string.IsNullOrEmpty(header.Value) ||
                    header.Key.IndexOfAny(InvalidHeaderNameChars) >= 0 ||
                    header.Value.IndexOfAny(InvalidHeaderValueChars) >= 0)
                    continue;

                headers[header.Key] = header.Value;
            }

        // Do not forward a root Cookie header across hosts.
        if (ReferenceEquals(sourceHeaders, info?.http_headers) &&
            Uri.TryCreate(info?.url, UriKind.Absolute, out Uri rootUri) &&
            Uri.TryCreate(fmt.url, UriKind.Absolute, out Uri targetUri) &&
            !rootUri.Host.Equals(targetUri.Host, StringComparison.OrdinalIgnoreCase))
            headers.Remove("Cookie");

        // Keep yt-dlp's User-Agent in custom headers for compatible ordering.
        if (headers.Remove("Referer", out string referrer))
            stream.Referrer = referrer;
        
        // Convert raw Cookie headers to domain-scoped FFmpeg cookies.
        headers.Remove("Cookie", out string cookieHeader);
        headers.Remove("Cookies"); // Legacy invalid header from old plugin.

        if (headers.Count > 0)
            stream.HTTPHeaders = headers;

        if (Options != null && Options.TryGetValue("ForwardCookies", out string setting) &&
            bool.TryParse(setting, out bool enabled) && !enabled)
            return;

        // yt-dlp sometimes exposes cookies only on the root result.
        string rawCookies = !string.IsNullOrWhiteSpace(fmt.cookies)
            ? fmt.cookies : info?.cookies;
        if (string.IsNullOrWhiteSpace(rawCookies) && string.IsNullOrWhiteSpace(cookieHeader))
            return;
        
        // Build primary/fallback cookies separately; empty disables reuse.
        stream.Cookies = BuildFFmpegCookies(rawCookies, cookieHeader, fmt.url, fmt.url);
        if (!string.IsNullOrWhiteSpace(stream.UrlFallback))
            stream.CookiesFallback = BuildFFmpegCookies(rawCookies, cookieHeader, stream.UrlFallback, fmt.url);
    }

    // Convert and validate yt-dlp Set-Cookie records for FFmpeg.
    private static string BuildFFmpegCookies(string raw, string cookieHeader, string url,
        string originUrl)
    {
        if (!IsHttpUrl(url))
            return string.Empty;

        if (!Uri.TryCreate(originUrl, UriKind.Absolute, out Uri origin))
            return string.Empty;
        Uri destination = new(url);
        var selected = new List<string>();
        var knownNames = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (string entry in SplitCookieEntries(raw))
            {
                if (TryParseCookie(entry, origin, destination, out string name, out string native))
                {
                    selected.Add(native);
                    knownNames.Add(name);
                }
            }
        }

        // Scope explicit Cookie headers to the original host.
        if (!string.IsNullOrWhiteSpace(cookieHeader) &&
            destination.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase) &&
            destination.Scheme.Equals(origin.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string pair in cookieHeader.Split(';'))
            {
                string item = pair.Trim();
                int equals = item.IndexOf('=');
                if (equals <= 0) continue;
                string name = item[..equals].Trim();
                string value = item[(equals + 1)..].Trim();
                if (!IsSafeCookie(name, value) || !knownNames.Add(name))
                    continue;

                selected.Add($"{name}={value}; path=/; domain={origin.DnsSafeHost};\r\n");
            }
        }

        return string.Concat(selected);
    }

    // Split combined Set-Cookie records while retaining their attributes.
    private static IEnumerable<string> SplitCookieEntries(string raw)
    {
        foreach (string line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            StringBuilder current = null;
            foreach (string part in line.Split(';'))
            {
                string token = part.Trim();
                if (token.Length == 0) continue;

                bool prefixed = token.StartsWith("Set-Cookie:", StringComparison.OrdinalIgnoreCase);
                if (prefixed)
                    token = token["Set-Cookie:".Length..].Trim();

                int equals = token.IndexOf('=');
                string key = (equals < 0 ? token : token[..equals]).Trim();
                bool newCookie = equals > 0 && (prefixed || !IsCookieAttribute(key));

                if (newCookie)
                {
                    if (current != null)
                        yield return current.ToString();
                    current = new StringBuilder(token);
                }
                else if (current != null)
                    current.Append("; ").Append(token);
            }

            if (current != null)
                yield return current.ToString();
        }
    }

    private static bool IsCookieAttribute(string key)
        => key.Equals("Domain", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Secure", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Expires", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Max-Age", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("SameSite", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Priority", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Partitioned", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("SameParty", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Version", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Comment", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("CommentURL", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Port", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("Discard", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeCookie(string name, string value)
    {
        // Reject header injection, malformed names, and line delimiters.
        if (string.IsNullOrEmpty(name) || value == null ||
            name.Any(c => c <= ' ' || c >= 127 || "()<>@,;:\\\"/[]?={}".Contains(c)))
            return false;
        return !value.Any(c => c == '\r' || c == '\n' || c == '\0' || c == ';');
    }

    private static bool TryParseCookie(string line, Uri origin, Uri destination,
        out string name, out string native)
    {
        name = native = null;
        string text = line.Trim();
        if (text.StartsWith("Set-Cookie:", StringComparison.OrdinalIgnoreCase))
            text = text["Set-Cookie:".Length..].Trim();

        string[] parts = text.Split(';');
        int equals = parts[0].IndexOf('=');
        if (equals <= 0) return false;
        string cookieName = parts[0][..equals].Trim();
        string cookieValue = parts[0][(equals + 1)..].Trim();
        if (!IsSafeCookie(cookieName, cookieValue)) return false;

        string domain = null;
        string path = null;
        bool secure = false;
        bool invalid = false;
        DateTimeOffset? expires = null;
        long? maxAge = null;

        for (int i = 1; i < parts.Length; i++)
        {
            string attr = parts[i].Trim();
            int pos = attr.IndexOf('=');
            string key = (pos < 0 ? attr : attr[..pos]).Trim();
            string value = pos < 0 ? null : attr[(pos + 1)..].Trim();

            if (key.Equals("Domain", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(value)) invalid = true;
                else domain = value.TrimStart('.');
            }
            else if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(value) || !value.StartsWith('/')) invalid = true;
                else path = value;
            }
            else if (key.Equals("Secure", StringComparison.OrdinalIgnoreCase))
                secure = true;
            else if (key.Equals("Expires", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out long timestamp) && timestamp >= 0 && timestamp <= 253402300799L)
                    expires = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                else if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                    expires = date;
                else
                    invalid = true;
            }
            else if (key.Equals("Max-Age", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out long seconds)) maxAge = seconds;
                else invalid = true;
            }
        }

        if (invalid || (secure && destination.Scheme != Uri.UriSchemeHttps) ||
            maxAge is <= 0 || expires is { } expiry && expiry <= DateTimeOffset.UtcNow)
            return false;

        string host = destination.DnsSafeHost;
        // Restrict host-only cookies to the original host.
        domain ??= origin.DnsSafeHost;
        if (!host.Equals(domain, StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            return false;
        if (domain.Length == 0 || domain.IndexOfAny(new[] { '\r', '\n', ';', ' ' }) >= 0)
            return false;

        // Use the original URL's RFC 6265 default cookie path.
        if (path == null)
        {
            string requestPath = origin.AbsolutePath;
            int lastSlash = requestPath.LastIndexOf('/');
            path = lastSlash <= 0 ? "/" : requestPath[..lastSlash];
        }
        string urlPath = destination.AbsolutePath;
        if (!urlPath.StartsWith(path, StringComparison.Ordinal) ||
            (urlPath.Length > path.Length && !path.EndsWith("/", StringComparison.Ordinal) &&
             urlPath[path.Length] != '/'))
            return false;
        if (path.IndexOfAny(new[] { '\r', '\n', ';' }) >= 0)
            return false;

        name = cookieName;
        native = $"{cookieName}={cookieValue}; path={path}; domain={domain};\r\n";
        return true;
    }

    // Score resolution by ratio; Flyleaf handles HDR tone mapping.
    private double VideoScore(Format fmt)
    {
        double target = Config.Video.MaxVerticalResolution;
        double height = fmt.height.GetValueOrDefault();
        double score;

        if (height <= 0)
            score = -6;
        else if (target > 0)
            score = -1.8 * Math.Abs(Math.Log2(height / target));
        else
            score = Math.Log2(height / 360.0);

        string range = fmt.dynamic_range ?? "";
        if (range.Length != 0 &&
            !range.Equals("SDR", StringComparison.OrdinalIgnoreCase) &&
            !range.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            score += 0.8;

        // Unknown FPS gets no penalty.
        double fps = fmt.fps.GetValueOrDefault();
        if (fps > 30)
            score += 0.35 * Math.Clamp(Math.Log2(fps / 30.0), 0, 1.5);

        // Prefer direct HTTP over HLS at comparable quality.
        if (IsDirectHttp(fmt.protocol))
            score += 0.40;

        if (HasAudio(fmt))
        {
            var language = Language.Get(fmt.language);
            if (Config.Audio.Languages.Count == 0 ||
                Config.Audio.Languages.Contains(language) || language == Language.Unknown)
                score += 0.16;
        }

        // Use bitrate only as a tie-breaker across codecs.
        double vbr = fmt.vbr.GetValueOrDefault();
        if (vbr > 0)
            score += 0.03 * Math.Clamp(Math.Log2(vbr / 1000.0), -1, 1);

        return score;
    }

    private static IEnumerable<Format> Formats(YoutubeDLJson info)
    {
        if (info == null) return Enumerable.Empty<Format>();
        return info.formats ?? new List<Format> { info };
    }

    private Format GetBestMatch(YoutubeDLJson info)
        => Formats(info)
            .Where(f => f != null && IsPlayable(f) && HasVideo(f))
            .OrderByDescending(VideoScore)
            .ThenByDescending(f => f.height)
            .ThenByDescending(f => f.fps)
            .FirstOrDefault();

    private Format GetAudioOnly(YoutubeDLJson info)
    {
        var candidates = Formats(info)
            .Where(f => f != null && IsPlayable(f) && HasAudio(f))
            .ToList();

        if (candidates.Count == 0) return null;

        // Prefer audio-only formats, then language, then bitrate.
        return candidates
            .OrderBy(f => HasVideo(f) ? 1 : 0)
            .ThenBy(f => AudioLanguagePriority(f.language))
            .ThenByDescending(f => f.abr.GetValueOrDefault() > 0 ? f.abr.GetValueOrDefault() : f.tbr.GetValueOrDefault())
            .ThenByDescending(f => f.audio_channels)
            .FirstOrDefault();
    }

    private int AudioLanguagePriority(string language)
    {
        var lang = Language.Get(language);
        int index = Config.Audio.Languages.IndexOf(lang);
        return index < 0 ? int.MaxValue : index;
    }

    // Wait for selected formats' available_at, with item cancellation.
    private string WaitForAvailability(ExtractionSession session, PlaylistItem item,
        long selection, YoutubeDLJson info)
    {
        Format video = GetBestMatch(info);
        Format audio = video != null && HasAudio(video) ? null : GetAudioOnly(info);
        long availableAt = Math.Max(video?.available_at ?? 0, audio?.available_at ?? 0);

        // Root available_at is meaningful only for single-format results.
        if (availableAt <= 0 && info.formats?.Count is not > 0)
            availableAt = info.available_at ?? 0;

        // Ignore invalid/unrepresentable timestamps rather than blocking.
        if (availableAt <= 0 || availableAt > 253402300799L)
            return null;

        long remainingMs = availableAt * 1000 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (remainingMs <= 0)
            return null;

        int maxWaitSeconds = GetIntOption("MaxAvailabilityWaitSeconds", 30, 1, 600);
        if (remainingMs > maxWaitSeconds * 1000L)
            return $"yt-dlp format is not available for another {Math.Ceiling(remainingMs / 1000.0):0} seconds";

        if (Logger.CanDebug)
            Log.Debug($"yt-dlp: waiting {remainingMs / 1000.0:F1}s for selected formats " +
                $"(available_at={availableAt}, video={video?.format_id ?? "none"}, " +
                $"audio={audio?.format_id ?? "none"})");

        while (true)
        {
            if (!IsCurrent(session) || Handler.OpenItemCounter != selection ||
                !ReferenceEquals(item, Selected))
                return null;

            remainingMs = availableAt * 1000 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (remainingMs <= 0)
                return null;

            // Wake on cancellation and check item switches.
            if (session.Cancellation.Token.WaitHandle.WaitOne((int)Math.Min(remainingMs, 200)))
                return null;
        }
    }

    public PlaylistItem SuggestItem()
    {
        ExtractionSession session = Volatile.Read(ref activeSession);
        if (session == null || !IsCurrent(session) ||
            !ReferenceEquals(Handler.OpenedPlugin, this))
            return null;

        // Select the preferred item without reordering the playlist.
        return Volatile.Read(ref session.PreferredItem) ??
               Volatile.Read(ref session.FirstItem);
    }

    public OpenResults OpenItem()
    {
        ExtractionSession session = Volatile.Read(ref activeSession);
        PlaylistItem item = Selected;
        long selection = Handler.OpenItemCounter;
        if (session == null || !IsCurrent(session) || item == null)
            return null;
        if (GetTag(item) is not PlaylistEntry entry)
            return new OpenResults("yt-dlp playlist entry is missing");

        int refreshMinutes = GetIntOption("StreamRefreshMinutes", 15, 1, 240);
        if (entry.Info != null &&
            DateTimeOffset.UtcNow - entry.ResolvedAt < TimeSpan.FromMinutes(refreshMinutes))
        {
            string waitError = WaitForAvailability(session, item, selection, entry.Info);
            if (!IsCurrent(session) || Handler.OpenItemCounter != selection ||
                !ReferenceEquals(item, Selected))
                return null;
            return waitError == null ? new OpenResults() : new OpenResults(waitError);
        }

        YoutubeDLJson resolved = null;
        ProcessResult result = null;
        try
        {
            result = RunWithRetry(session, entry.Url, flatPlaylist: false, isItem: true, info =>
            {
                if (resolved == null && Formats(info).Any(f => f != null && IsPlayable(f) &&
                                                            (HasVideo(f) || HasAudio(f))))
                    resolved = info;
            }, selection);
        }
        catch (Exception e)
        {
            if (IsCurrent(session) && Handler.OpenItemCounter == selection)
                return new OpenResults($"yt-dlp extraction: {e.Message}");
        }

        if (!IsCurrent(session) || Handler.OpenItemCounter != selection ||
            !ReferenceEquals(item, Selected))
            return null;
        if (resolved == null)
            return new OpenResults(result?.LastError ?? "yt-dlp could not resolve playable streams");

        string availabilityError = WaitForAvailability(session, item, selection, resolved);
        if (!IsCurrent(session) || Handler.OpenItemCounter != selection ||
            !ReferenceEquals(item, Selected))
            return null;
        if (availabilityError != null)
            return new OpenResults(availabilityError);

        // Replace cached signed URLs with newly extracted formats.
        if (entry.Info != null)
        {
            item.ExternalVideoStreams.Clear();
            item.ExternalAudioStreams.Clear();
            item.ExternalSubtitlesStreams.Clear();
        }
        if (!FillItemStreams(item, resolved))
            return new OpenResults("yt-dlp returned no compatible media formats");

        entry.Info = resolved;
        entry.ResolvedAt = DateTimeOffset.UtcNow;
        return new OpenResults();
    }

    public ExternalAudioStream SuggestExternalAudio()
    {
        if (Handler.OpenedPlugin?.Name != Name || GetTag(Selected) is not PlaylistEntry { Info: { } info })
            return null;

        Format fmt = GetAudioOnly(info);
        return fmt == null ? null : Selected.ExternalAudioStreams
            .FirstOrDefault(s => string.Equals(s.Url, fmt.url, StringComparison.Ordinal));
    }

    public ExternalVideoStream SuggestExternalVideo()
    {
        if (Handler.OpenedPlugin?.Name != Name || GetTag(Selected) is not PlaylistEntry { Info: { } info })
            return null;

        Format fmt = GetBestMatch(info);
        if (fmt == null) return null;

        var selected = Selected.ExternalVideoStreams
            .FirstOrDefault(s => string.Equals(s.Url, fmt.url, StringComparison.Ordinal));

        if (selected != null && Logger.CanDebug)
            Log.Debug($"Selected video: {fmt.format_id} {fmt.width}x{fmt.height} " +
                $"{fmt.fps}fps {fmt.vcodec} {fmt.dynamic_range} {fmt.protocol} " +
                $"audio={(HasAudio(fmt) ? "embedded" : "separate")} " +
                $"cookies={(string.IsNullOrEmpty(selected.Cookies) ? "none" : "set")} " +
                $"fallbackCookies={(string.IsNullOrEmpty(selected.UrlFallback) ? "n/a" :
                    string.IsNullOrEmpty(selected.CookiesFallback) ? "none" : "set")}");

        return selected;
    }
}
