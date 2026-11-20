using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Flyleaf.Controls.WPF;
using Microsoft.Win32;
using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace Flyleaf;

public partial class MainWindow
{
    static readonly HttpClient playlistHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly CancellationTokenSource playlistLifetime = new();
    string? playlistPath;
    string playlistName = "New queue";
    long savedQueueRevision;
    bool playlistOperation, playlistDisposed, allowPlaylistClose, closeAfterPlaylistOperation, playlistDocumentActive;
    bool playlistClosePromptQueued;
    string playlistStage = "Starting playlist operation";
    bool PlaylistDirty => savedQueueRevision != MediaElement.Queue.Revision;
    TaskCompletionSource? playlistIdle;
    static readonly string PlaylistFilter =
        $"All supported playlists|{string.Join(";", FlyleafPlaylistSerializer.SupportedExtensions.Select(extension => "*" + extension))}" +
        $"|Flyleaf playlists|*{FlyleafPlaylistSerializer.NativeExtension}" +
        $"|M3U playlists|*{FlyleafPlaylistSerializer.M3UExtension};*{FlyleafPlaylistSerializer.M3U8Extension}" +
        $"|PLS playlists|*{FlyleafPlaylistSerializer.PlsExtension}";

    static readonly FlyleafShortcutGesture NewWindowShortcut = new(Key.N, Ctrl: true, IsKeyUp: true);
    static readonly FlyleafShortcutGesture CloseWindowShortcut = new(Key.W, Ctrl: true, IsKeyUp: true);

    void OpenNewPlayerWindow()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!playlistDisposed) App.Instance.OpenWindow(this);
        }));
    }

    void ClosePlayerWindow()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!playlistDisposed) Close();
        }));
    }

    void InitializePlaylistCommands()
    {
        MediaElement.PlaylistActionRequested += PlaylistActionRequested;
        MediaElement.SupportsFolderImport = true;
        MediaElement.Queue.PropertyChanged += QueueDocumentChanged;
        Closing += PlaylistClosing;
        var bindings = Player.Config.Playback.KeyBindings.Keys;
        EnsureWindowShortcut("app.newWindow", NewWindowShortcut);
        EnsureWindowShortcut("app.closeWindow", CloseWindowShortcut);
        foreach (var binding in bindings)
        {
            var actionId = GetApplicationActionId(binding);
            if (actionId == null) continue;
            binding.Action = KeyBindingAction.Custom;
            binding.ActionName = actionId;
            Action command = actionId switch
            {
                "app.open" => MediaElement.OpenFile,
                "app.paste" => PasteMediaInput,
                "app.newWindow" => OpenNewPlayerWindow,
                "app.closeWindow" => ClosePlayerWindow,
                _ => throw new InvalidOperationException($"Unknown application action '{actionId}'.")
            };
            binding.SetAction(command, binding.IsKeyUp);
        }
        savedQueueRevision = MediaElement.Queue.Revision; MediaElement.PlaylistActionsAvailable = true; UpdatePlaylistStatus();

        void EnsureWindowShortcut(string action, FlyleafShortcutGesture gesture)
        {
            if (bindings.Any(binding => GetApplicationActionId(binding) == action)) return;
            bool occupied = bindings.Any(binding => binding.Key == gesture.Key && binding.Ctrl == gesture.Ctrl &&
                binding.Shift == gesture.Shift && binding.Alt == gesture.Alt);
            bindings.Add(new()
            {
                Action = KeyBindingAction.Custom, ActionName = action,
                Key = occupied ? Key.None : gesture.Key,
                Ctrl = !occupied && gesture.Ctrl, Shift = !occupied && gesture.Shift, Alt = !occupied && gesture.Alt,
                IsKeyUp = gesture.IsKeyUp
            });
        }
    }
    static string? GetApplicationActionId(FlyleafLib.MediaPlayer.KeyBinding binding) => binding.Action switch
    {
        KeyBindingAction.OpenFromFileDialog => "app.open",
        KeyBindingAction.OpenFromClipboard => "app.paste",
        KeyBindingAction.Custom when binding.ActionName is "app.open" or "app.paste" or "app.newWindow" or "app.closeWindow" => binding.ActionName,
        _ => null
    };

    void PasteMediaInput()
    {
        try { var text = Clipboard.GetText().Trim(); if (text.Length > 0) MediaElement.OpenInput(text); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open input", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void DisposePlaylistCommands()
    {
        playlistDisposed = true; playlistLifetime.Cancel();
        MediaElement.PlaylistActionRequested -= PlaylistActionRequested;
        MediaElement.Queue.PropertyChanged -= QueueDocumentChanged;
        Closing -= PlaylistClosing;
    }
    void QueueDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FlyleafQueue.Revision)) UpdatePlaylistStatus();
        if (e.PropertyName == nameof(FlyleafQueue.IsAutoAdvancing)) UpdateHeaderVisibility();
    }
    void UpdatePlaylistStatus()
    {
        if (!playlistDisposed) MediaElement.PlaylistStatus = playlistName + (PlaylistDirty ? " *" : "") + (playlistOperation ? " — working…" : "");
    }
    static Uri PlaylistUri(string input) => Uri.TryCreate(input, UriKind.Absolute, out var uri) ? uri : new Uri(Path.GetFullPath(input));
    static string PlaylistExtension(string input)
    {
        try { return Path.GetExtension(PlaylistUri(input).AbsolutePath).ToLowerInvariant(); }
        catch (UriFormatException) { return ""; }
    }
    static bool IsPlaylistInput(string input) =>
        FlyleafPlaylistSerializer.IsSupportedExtension(PlaylistExtension(input));
    async Task RunPlaylistOperation(Func<Task> operation)
    {
        if (playlistOperation || playlistDisposed) return;
        playlistOperation = true; MediaElement.PlaylistActionsAvailable = false;
        playlistStage = "Starting playlist operation";
        UpdatePlaylistStatus();
        using var activity = FlyleafPopupActivity.Hold(Player);
        try { await operation(); }
        catch (OperationCanceledException) when (playlistLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var detailsPath = RecordPlaylistError(ex);
            if (!playlistDisposed) MessageBox.Show(this, ex.Message + (detailsPath == null ? "" : "\n\nDetails saved to:\n" + detailsPath), "Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            playlistOperation = false;
            playlistIdle?.TrySetResult();
            playlistIdle = null;
            if (!playlistDisposed) MediaElement.PlaylistActionsAvailable = true;
            UpdatePlaylistStatus();
            if (closeAfterPlaylistOperation) { closeAfterPlaylistOperation = false; _ = Dispatcher.BeginInvoke(new Action(() => { if (!playlistDisposed) Close(); })); }
        }
    }
    async Task WaitForPlaylistIdleAsync()
    {
        while (playlistOperation && !playlistDisposed)
            await (playlistIdle ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    internal async Task OpenLaunchInputsAsync(string[] inputs)
    {
        await WaitForPlaylistIdleAsync();
        if (playlistDisposed) return;
        if (inputs.Length == 1 && Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(inputs[0])))
        {
            if (Player.Video.IsOpened) MediaElement.OpenInput(inputs[0]);
            else MessageBox.Show(this, "Open a video before adding subtitles.", "Subtitles", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var media = GetQueueMediaFiles(inputs);
        if (media.Length > 0) await RunPlaylistOperation(() => OpenQueueInputs(media));
    }

    internal async Task<bool> CloseFromTrayAsync()
    {
        await WaitForPlaylistIdleAsync();
        if (playlistDisposed) return true;
        if (playlistClosePromptQueued) return false;

        await RunPlaylistOperation(async () => { allowPlaylistClose = await ConfirmPlaylistReplacement(); });
        if (allowPlaylistClose && !playlistDisposed) Close();
        if (!playlistDisposed) allowPlaylistClose = false;
        return playlistDisposed;
    }

    async void PlaylistActionRequested(object? sender, FlyleafPlaylistActionEventArgs e)
    {
        await RunPlaylistOperation(async () =>
        {
            switch (e.Action)
            {
                case FlyleafPlaylistAction.New:
                    if (!await ConfirmPlaylistReplacement()) return;
                    MediaElement.Queue.Clear(); playlistPath = null; playlistName = "New queue";
                    savedQueueRevision = MediaElement.Queue.Revision; playlistDocumentActive = false;
                    break;
                case FlyleafPlaylistAction.Add:
                    var dialog = new OpenFileDialog { Title = "Add files or playlists", Filter = "All supported inputs|*.*", Multiselect = true };
                    if (dialog.ShowDialog(this) == true) await AddQueueInputs(dialog.FileNames);
                    break;
                case FlyleafPlaylistAction.AddFolder:
                    await AddQueueFolder();
                    break;
                case FlyleafPlaylistAction.AddUrl:
                    var input = MediaElement.PromptForInput("Add URL", "Media or playlist URL", "Add");
                    if (input != null) await AddQueueInputs([input]);
                    break;
                case FlyleafPlaylistAction.AddInputs:
                    await AddQueueInputs(e.Inputs);
                    break;
                case FlyleafPlaylistAction.Save: await SavePlaylist(false); break;
                case FlyleafPlaylistAction.SaveAs: await SavePlaylist(true); break;
                case FlyleafPlaylistAction.Export: await ExportPlaylist(); break;
            }
        });
    }
    async Task LoadPlaylist(string input, bool append)
    {
        playlistStage = "Reading playlist document";
        var location = PlaylistUri(input); var baseUri = location;
        string text;
        if (location.IsFile) text = await PlaylistDocumentFiles.ReadTextAsync(location.LocalPath, playlistLifetime.Token);
        else
        {
            if (location.Scheme is not ("http" or "https")) throw new FormatException("Playlist documents can be loaded from a local file or HTTP/HTTPS URL.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(playlistLifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                using var response = await playlistHttp.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType == "text/html") throw new FormatException("The URL returned a web page instead of a playlist.");
                if (response.Content.Headers.ContentLength > PlaylistDocumentFiles.MaximumDocumentBytes) throw PlaylistDocumentFiles.TooLarge();
                Encoding? declaredEncoding = null;
                var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
                if (!string.IsNullOrWhiteSpace(charset))
                {
                    try { declaredEncoding = CodePagesEncodingProvider.Instance.GetEncoding(charset) ?? Encoding.GetEncoding(charset); }
                    catch (ArgumentException ex) { throw new FormatException("Unsupported playlist text encoding: " + charset, ex); }
                }
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                text = await PlaylistDocumentFiles.ReadTextAsync(stream, deadline.Token, declaredEncoding);
                baseUri = response.RequestMessage?.RequestUri ?? location;
            }
            catch (OperationCanceledException) when (!playlistLifetime.IsCancellationRequested)
            { throw new TimeoutException("Playlist download exceeded 30 seconds."); }
        }
        if (playlistDisposed) return;
        playlistStage = "Parsing playlist document";
        var extension = Path.GetExtension(baseUri.AbsolutePath).ToLowerInvariant();
        if (!FlyleafPlaylistSerializer.IsSupportedExtension(extension)) extension = PlaylistExtension(input);
        if (!FlyleafPlaylistSerializer.IsSupportedExtension(extension))
        {
            var start = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (start.StartsWith('<')) throw new FormatException("This document looks like a web page instead of a playlist.");
            extension = start.StartsWith('{') ? FlyleafPlaylistSerializer.NativeExtension : start.StartsWith("[playlist]", StringComparison.OrdinalIgnoreCase) ? ".pls" : ".m3u";
        }
        var hls = extension is ".m3u" or ".m3u8" && FlyleafPlaylistSerializer.IsHls(text);
        var document = hls ? new FlyleafPlaylistDocument { Entries = [new() { Input = input }] }
            : FlyleafPlaylistSerializer.Read(text, extension, baseUri);
        if (!append && !await ConfirmPlaylistReplacement()) return;
        if (playlistDisposed) return;
        playlistStage = append ? "Appending playlist entries" : "Replacing queue entries";
        MediaElement.Queue.ApplyEntries(document.Entries.Select(entry => new FlyleafQueueEntry { Input = entry.Input, Title = entry.Title, UserAgent = entry.UserAgent, Referrer = entry.Referrer }), append);
        if (!append)
        {
            playlistName = extension == FlyleafPlaylistSerializer.NativeExtension && !string.IsNullOrWhiteSpace(document.Title)
                ? document.Title : Path.GetFileNameWithoutExtension(location.AbsolutePath);
            if (string.IsNullOrWhiteSpace(playlistName)) playlistName = "Imported playlist";
            playlistPath = extension == FlyleafPlaylistSerializer.NativeExtension && location.IsFile ? location.LocalPath : null;
            savedQueueRevision = playlistPath != null ? MediaElement.Queue.Revision : -1;
            playlistDocumentActive = !hls;
        }
        playlistStage = "Showing queue panel";
        MediaElement.ShowPanel("queue"); UpdatePlaylistStatus();
        if (!append && MediaElement.Queue.Sources.FirstOrDefault() is { } first)
            MediaElement.Queue.Open(first, play: Player.Config.Playback.AutoPlay);
    }
    FlyleafPlaylistDocument CapturePlaylist(string title) => new()
    {
        Title = title,
        Entries = MediaElement.Queue.CaptureEntries().Select(entry => new FlyleafPlaylistEntry
        { Input = entry.Input, Title = entry.Title, UserAgent = entry.UserAgent, Referrer = entry.Referrer }).ToList()
    };
    async Task AddQueueFolder()
    {
        playlistStage = "Choosing a folder";
        var picker = new OpenFolderDialog { Title = "Add folder to queue" };
        if (picker.ShowDialog(this) != true) return;
        var folder = picker.FolderName;
        var options = new FolderImportDialog(MediaElement.Resources, folder) { Owner = this };
        if (options.ShowDialog() != true) return;

        playlistStage = "Scanning folder for media";
        var result = await FolderQueueImport.ScanAsync(folder, options.Options, playlistLifetime.Token);
        if (playlistDisposed) return;
        if (result.Paths.Length > 0)
        {
            bool wasEmpty = MediaElement.Queue.Sources.Count == 0;
            MediaElement.Queue.ApplyEntries(result.Paths.Select(path => new FlyleafQueueEntry { Input = path }), append: true);
            PlayFirstAddedQueueItem(wasEmpty);
        }

        string message = result.Paths.Length == 0 ? "No matching media found" : $"Added {result.Paths.Length:N0} to the queue";
        if (result.Paths.Length > 0 && result.UsedNameFallback)
            message += " · Windows name sorting used where Explorer view order was unavailable";
        if (result.SkippedFolders > 0) message += $" · Skipped folders: {result.SkippedFolders:N0}";
        MediaElement.ShowOSD(message);
    }

    async Task AddQueueInputs(IEnumerable<string> inputs)
    {
        bool wasEmpty = MediaElement.Queue.Sources.Count == 0;
        await AppendQueueInputs(inputs);
        PlayFirstAddedQueueItem(wasEmpty);
    }

    void PlayFirstAddedQueueItem(bool wasEmpty)
    {
        if (!wasEmpty || playlistDisposed || !Player.Config.Playback.AutoPlay) return;

        var queue = MediaElement.Queue;
        if (queue.Sources.FirstOrDefault() is { } first && queue.CurrentItemNumber == 0)
            queue.Open(first);
    }

    async Task AppendQueueInputs(IEnumerable<string> inputs)
    {
        foreach (var input in inputs)
        {
            if (playlistDisposed) return;
            if (IsPlaylistInput(input)) await LoadPlaylist(input, true);
            else if (!Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(input))) MediaElement.Queue.Add(input);
        }
    }
    async Task OpenQueueInputs(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 1 && IsPlaylistInput(inputs[0])) { await LoadPlaylist(inputs[0], false); return; }
        if (!await ConfirmPlaylistReplacement() || playlistDisposed) return;
        MediaElement.Queue.Clear(); playlistPath = null; playlistName = "Queue"; playlistDocumentActive = false;
        await AppendQueueInputs(inputs);
        if (playlistDisposed) return;
        UpdatePlaylistStatus();
        if (MediaElement.Queue.Sources.FirstOrDefault() is { } first)
            MediaElement.Queue.Open(first, play: Player.Config.Playback.AutoPlay);
    }
    async Task<bool> SavePlaylist(bool saveAs)
    {
        var path = saveAs ? null : playlistPath;
        if (path == null)
        {
            playlistStage = "Choosing playlist save path";
            var dialog = new SaveFileDialog { Title = "Save Flyleaf playlist", Filter = "Flyleaf playlist|*.fpls", DefaultExt = FlyleafPlaylistSerializer.NativeExtension, FileName = Utils.GetValidFileName(playlistName) };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        var name = Path.GetFileNameWithoutExtension(path);
        playlistStage = "Serializing native playlist";
        if (!string.Equals(Path.GetExtension(path), FlyleafPlaylistSerializer.NativeExtension, StringComparison.OrdinalIgnoreCase)) throw new FormatException("Save native playlists with the .fpls extension.");
        var document = CapturePlaylist(name); var revision = MediaElement.Queue.Revision;
        await WritePlaylistFile(path, FlyleafPlaylistSerializer.Write(document));
        if (playlistDisposed) return false;
        playlistPath = path; playlistName = name; savedQueueRevision = revision; playlistDocumentActive = true; UpdatePlaylistStatus(); return true;
    }
    async Task ExportPlaylist()
    {
        playlistStage = "Choosing playlist export path";
        var dialog = new SaveFileDialog { Title = "Export M3U playlist", Filter = "UTF-8 M3U8 playlist|*.m3u8|M3U playlist|*.m3u", DefaultExt = ".m3u8", FileName = Utils.GetValidFileName(playlistName) };
        if (dialog.ShowDialog(this) != true) return;
        playlistStage = "Serializing M3U playlist";
        var text = FlyleafPlaylistSerializer.WriteM3U(CapturePlaylist(playlistName));
        if (Path.GetExtension(dialog.FileName).ToLowerInvariant() is not (".m3u" or ".m3u8")) throw new FormatException("Export to an .m3u or .m3u8 file.");
        await WritePlaylistFile(dialog.FileName, text);
    }
    async Task WritePlaylistFile(string path, string text)
    {
        playlistStage = "Writing playlist file";
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), playlistLifetime.Token);
            playlistLifetime.Token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    async Task<bool> ConfirmPlaylistReplacement()
    {
        if (!PlaylistDirty || !playlistDocumentActive) return true;
        playlistStage = "Confirming unsaved playlist changes";
        var decision = MessageBox.Show(this, playlistDocumentActive ? "Save changes to the current playlist?" : "Save the current queue as a playlist before replacing it?", "Playlist changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (decision == MessageBoxResult.Cancel) return false;
        if (decision == MessageBoxResult.No) return true;
        return await SavePlaylist(false) && !PlaylistDirty;
    }
    string? RecordPlaylistError(Exception error)
    {
        try
        {
            Directory.CreateDirectory(App.Instance.SettingsDirectory);
            var path = Path.Combine(App.Instance.SettingsDirectory, "Playlist.Errors.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} | {playlistStage}\n{error}\n\n");
            return path;
        }
        catch { return null; } // A diagnostics failure must not hide the original error.
    }
    void PlaylistClosing(object? sender, CancelEventArgs e)
    {
        if (allowPlaylistClose) return;
        if (playlistOperation) { e.Cancel = true; closeAfterPlaylistOperation = true; return; }
        if (!PlaylistDirty || !playlistDocumentActive) return;
        e.Cancel = true;
        if (playlistClosePromptQueued) return;
        playlistClosePromptQueued = true;
        // Even a completed Task can continue inline. Return from Closing before
        // opening dialogs or requesting Close again (including the Discard path).
        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                if (playlistDisposed) return;
                await RunPlaylistOperation(async () => { if (await ConfirmPlaylistReplacement()) allowPlaylistClose = true; });
                if (allowPlaylistClose && !playlistDisposed) Close();
            }
            finally { playlistClosePromptQueued = false; }
        }));
    }
}
