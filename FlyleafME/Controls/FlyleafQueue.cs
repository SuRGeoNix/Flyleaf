using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaPlayer;
using DecoderContext = FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace Flyleaf.Controls.WPF;

public sealed class FlyleafQueueSource : NotifyPropertyChanged
{
    readonly ObservableCollection<FlyleafQueueChild> children = [];
    string title;
    bool current;
    public Guid Id { get; } = Guid.NewGuid();
    public string Input { get; }
    public string? UserAgent { get; }
    public string? Referrer { get; }
    internal bool HasHttpOptions => UserAgent != null || Referrer != null;
    internal void ApplyHttpOptions(PlaylistItem item)
    {
        if (Referrer != null) item.Referrer = Referrer;
        if (UserAgent != null)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.HTTPHeaders != null) foreach (var pair in item.HTTPHeaders) headers[pair.Key] = pair.Value;
            headers["User-Agent"] = UserAgent; item.HTTPHeaders = headers;
        }
    }
    internal bool HasCustomTitle { get; private set; }
    public string Title { get => title; set { HasCustomTitle = true; Set(ref title, value); } }
    public bool IsCurrent => current && children.Count == 0;
    int itemNumber;
    public int ItemNumber => itemNumber;
    internal void Number(int value) { if (Set(ref itemNumber, value, propertyName: nameof(ItemNumber))) Raise(nameof(IndexText)); }
    public string IndexText => itemNumber <= 0 ? "" : children.Count > 0 ? $"{itemNumber}–{itemNumber + children.Count - 1}" : itemNumber.ToString();
    public ReadOnlyObservableCollection<FlyleafQueueChild> Children { get; }
    internal FlyleafQueueSource(string input, string? userAgent = null, string? referrer = null)
    {
        Input = input;
        UserAgent = userAgent; Referrer = referrer;
        title = Uri.TryCreate(input, UriKind.Absolute, out var uri) && !uri.IsFile ? uri.Host + uri.AbsolutePath : Path.GetFileName(input);
        if (FlyleafDiscInput.DisplayTitle(input) is { } discTitle) { title = discTitle; HasCustomTitle = true; }
        if (string.IsNullOrWhiteSpace(title)) title = "Media source";
        Children = new(children);
    }
    internal void Mark(bool value) => Set(ref current, value, propertyName: nameof(IsCurrent));
    internal void Update(PlaylistItem[] items, PlaylistItem? selected)
    {
        var descriptors = items.Select((item, position) => (item.Title, item.DirectUrl, position)).ToArray();
        var expected = items.Length > 1 ? items.Length : 0;
        if (children.Count != expected || children.Where((child, i) => child.Title != descriptors[i].Title || child.DirectUrl != descriptors[i].DirectUrl).Any())
        {
            children.Clear();
            // Single-item sources need no redundant child row.
            if (items.Length > 1)
                foreach (var (name, url, index) in descriptors) children.Add(new(this, index, name, url));
        }
        Raise(nameof(IsCurrent)); Raise(nameof(IndexText));
        foreach (var child in children) child.Mark(child.Position < items.Length && ReferenceEquals(items[child.Position], selected));
    }
    internal void UnmarkChildren() { foreach (var child in children) child.Mark(false); }
}

/// <summary>A cached, read-only descriptor. It never retains a live PlaylistItem.</summary>
public sealed class FlyleafQueueChild : NotifyPropertyChanged
{
    public FlyleafQueueSource Source { get; }
    public string Title { get; }
    public int Position { get; }
    internal string? DirectUrl { get; }
    bool current;
    public bool IsCurrent => current;
    int itemNumber;
    public int ItemNumber => itemNumber;
    internal void Number(int value) { if (Set(ref itemNumber, value, propertyName: nameof(ItemNumber))) Raise(nameof(IndexText)); }
    public string IndexText => itemNumber > 0 ? itemNumber.ToString() : "";
    internal FlyleafQueueChild(FlyleafQueueSource source, int position, string title, string? directUrl)
    { Source = source; Position = position; Title = title; DirectUrl = directUrl; }
    internal void Mark(bool value) => Set(ref current, value, propertyName: nameof(IsCurrent));
}

/// <summary>Reusable mixed-source queue. The application owns persistence and external opening policy.</summary>
public sealed class FlyleafQueue : NotifyPropertyChanged, IDisposable
{
    static readonly StringComparer names = StringComparer.OrdinalIgnoreCase;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly ObservableCollection<FlyleafQueueSource> sources = [];
    Player? player;
    FlyleafQueueSource? currentSource, navigationSource, highlightedSource;
    FlyleafQueueChild? navigationChild;
    Request? pending, nextRequest;
    bool issuingOpen, disposed, repeat, navigationFailed;
    bool autoAdvance;
    string message = "";
    long generation;
    long revision;
    readonly FlyleafQueueProgression progression = new();
    readonly DispatcherTimer slideTimer;
    PlaylistItem? progressionItem;
    long progressionVersion, progressionTimestamp;
    bool progressionError, progressionRunning, progressionBuffering;
    internal Func<int>? SlideShowIntervalProvider;
    public long Revision => revision;
    sealed record Request(FlyleafQueueSource Source, FlyleafQueueChild? Child, bool Play, PlaylistItem? Item = null, bool Automatic = false);
    public ReadOnlyObservableCollection<FlyleafQueueSource> Sources { get; }
    public FlyleafQueueSource? CurrentSource => currentSource;
    public bool IsOpening => pending != null;
    /// <summary>An automatic transition is opening its next input; temporary CanPlay=false is not user activity.</summary>
    public bool IsAutoAdvancing => pending?.Automatic == true;
    public bool HasPlayer => !disposed && player is { IsDisposed: false };
    public bool Repeat { get => repeat; set { dispatcher.VerifyAccess(); Set(ref repeat, value); NotifyNavigation(); } }
    public bool AutoAdvance { get => autoAdvance; set { dispatcher.VerifyAccess(); if (Set(ref autoAdvance, value)) UpdateProgression(); } }
    public string Message => message;
    public int TotalItems => sources.Sum(source => Math.Max(1, source.Children.Count));
    /// <summary>One-based position in the playback sequence; discovered children replace their parent.</summary>
    public int CurrentItemNumber
    {
        get
        {
            var origin = NavigationSource;
            int preceding = 0;
            foreach (var source in sources)
            {
                if (ReferenceEquals(source, origin))
                {
                    var child = nextRequest?.Source == origin ? nextRequest.Child :
                        pending?.Source == origin ? pending.Child : navigationChild;
                    int position = child?.Source == origin ? child.Position : 0;
                    return preceding + 1 + (source.Children.Count > 0 ? Math.Clamp(position, 0, source.Children.Count - 1) : 0);
                }
                preceding += Math.Max(1, source.Children.Count);
            }
            return 0;
        }
    }
    public string ItemCountText => CurrentItemNumber > 0 ? $"{CurrentItemNumber} / {TotalItems} items" : $"{TotalItems} items";
    public string? CurrentTitle => (nextRequest?.Source ?? pending?.Source ?? (navigationFailed ? navigationSource : currentSource)) is { HasCustomTitle: true, Children.Count: 0 } source ? source.Title : null;
    FlyleafQueueSource? NavigationSource => nextRequest?.Source ?? pending?.Source ?? navigationSource ?? currentSource;
    public bool CanNext => HasPlayer && NavigationTarget(1) != null;
    public bool CanPrevious => HasPlayer && NavigationTarget(-1) != null;
    internal FlyleafQueue()
    {
        Sources = new(sources);
        slideTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        slideTimer.Tick += (_, _) => UpdateProgression();
    }

    public FlyleafQueueSource Add(string input) => Add(new FlyleafQueueEntry { Input = input });
    public FlyleafQueueSource Add(FlyleafQueueEntry entry)
    {
        dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        var source = CreateSource(entry.Input, entry.UserAgent, entry.Referrer);
        if (!string.IsNullOrWhiteSpace(entry.Title)) source.Title = entry.Title;
        AddSource(source); Edited(); NotifyNavigation(); return source;
    }
    static FlyleafQueueSource CreateSource(string input, string? userAgent = null, string? referrer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        input = input.Trim();
        if (Utils.GetUrlExtention(input) == "lnk") input = Utils.GetLnkTargetPath(input) ?? input;
        if (Utils.ExtensionsSubtitles.Contains(Utils.GetUrlExtention(input))) throw new ArgumentException("Load subtitle files from the Subtitles panel.", nameof(input));
        return new FlyleafQueueSource(input.Trim(), userAgent, referrer);
    }
    void AddSource(FlyleafQueueSource source)
    {
        source.PropertyChanged += SourceChanged; sources.Add(source);
    }
    void SourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FlyleafQueueSource.Title)) { Edited(); NotifyNavigation(); }
    }
    void Edited() { revision++; Raise(nameof(Revision)); }
    public FlyleafQueueEntry[] CaptureEntries()
    {
        dispatcher.VerifyAccess();
        return sources.Select(source => new FlyleafQueueEntry { Input = source.Input, Title = source.Title, UserAgent = source.UserAgent, Referrer = source.Referrer }).ToArray();
    }
    public void ApplyEntries(IEnumerable<FlyleafQueueEntry> entries, bool append = false)
    {
        dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        var prepared = entries.Select(entry =>
        {
            var source = CreateSource(entry.Input, entry.UserAgent, entry.Referrer);
            if (!string.IsNullOrWhiteSpace(entry.Title)) source.Title = entry.Title;
            return source;
        }).ToArray();
        if (!append) Clear();
        foreach (var source in prepared) AddSource(source);
        if (prepared.Length > 0) Edited();
        NotifyNavigation();
    }
    public void Remove(FlyleafQueueSource source) => Remove([source]);
    public void Remove(IEnumerable<FlyleafQueueSource> selection)
    {
        dispatcher.VerifyAccess();
        var removed = selection.Where(sources.Contains).ToHashSet();
        if (removed.Count == 0) return;
        var active = NavigationSource;
        var replace = active != null && removed.Contains(active);
        var index = active == null ? -1 : sources.IndexOf(active);
        var following = replace ? sources.Skip(index + 1).FirstOrDefault(s => !removed.Contains(s)) : null;
        var replacement = following ?? (replace ? sources.Take(index).LastOrDefault(s => !removed.Contains(s)) : null);
        var child = following == null ? replacement?.Children.LastOrDefault() : null;
        if (replace) StopQueuePlayback();
        foreach (var source in removed) { source.PropertyChanged -= SourceChanged; sources.Remove(source); }
        Edited(); NotifyNavigation();
        if (replacement != null) Open(replacement, child);
    }
    void StopQueuePlayback()
    {
        generation++; pending = nextRequest = null; navigationSource = null; navigationChild = null;
        SetCurrent(null);
        if (HasPlayer) player!.Stop();
        Raise(nameof(IsOpening)); Raise(nameof(IsAutoAdvancing));
    }
    public void MoveBefore(IEnumerable<FlyleafQueueSource> selection, FlyleafQueueSource? before)
    {
        dispatcher.VerifyAccess();
        var selected = selection.ToHashSet();
        var moving = sources.Where(selected.Contains).ToArray();
        if (moving.Length == 0 || before != null && moving.Contains(before)) return;
        var arranged = sources.Except(moving).ToList();
        var index = before == null ? arranged.Count : arranged.IndexOf(before);
        if (index < 0) return;
        arranged.InsertRange(index, moving);
        for (int i = 0; i < arranged.Count; i++) sources.Move(sources.IndexOf(arranged[i]), i);
        Edited(); NotifyNavigation();
    }
    public void Move(FlyleafQueueSource source, int offset)
    {
        dispatcher.VerifyAccess(); var index = sources.IndexOf(source);
        if (index < 0 || index + offset < 0 || index + offset >= sources.Count) return;
        sources.Move(index, index + offset); Edited(); NotifyNavigation();
    }
    public void Clear()
    {
        dispatcher.VerifyAccess(); var changed = sources.Count > 0;
        if (changed || pending != null) StopQueuePlayback();
        foreach (var source in sources) source.PropertyChanged -= SourceChanged;
        sources.Clear(); nextRequest = null; generation++;
        SetCurrent(null); SetMessage(""); Raise(nameof(IsOpening)); Raise(nameof(IsAutoAdvancing)); NotifyNavigation();
        if (changed) Edited();
    }
    public void Open(FlyleafQueueSource source, FlyleafQueueChild? child = null, bool play = true)
    {
        dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (!sources.Contains(source) || child != null && !ReferenceEquals(child.Source, source) || player is not { IsDisposed: false }) return;
        var request = new Request(source, child, play);
        if (pending != null)
        {
            nextRequest = request;
            NotifyNavigation();
            SetMessage("Waiting to open " + source.Title);
            // The queue still opens one item at a time; interrupt only the superseded open.
            player.CancelCurrentOperation();
            return;
        }
        Start(request);
    }
    void Start(Request request)
    {
        if (player is not { IsDisposed: false } target || !sources.Contains(request.Source)) return;
        CancelProgression(); progressionError = progressionBuffering = false;
        generation++; navigationSource = request.Source; navigationChild = request.Child; navigationFailed = false;
        if (ReferenceEquals(currentSource, request.Source) && request.Child != null)
        {
            var item = Resolve(request.Child);
            if (item == null) { SetMessage("This source changed. Reopen it and select an available child."); return; }
            request = request with { Item = item };
        }
        else SetCurrent(null);
        pending = request; Raise(nameof(IsOpening)); Raise(nameof(IsAutoAdvancing)); NotifyNavigation(); SetMessage("Opening " + request.Source.Title + "…");
        try
        {
            if (request.Item != null) target.OpenAsync(request.Item);
            else
            {
                issuingOpen = true;
                try { target.OpenAsync(request.Source.Input, defaultPlaylistItem: request.Child == null && !request.Source.HasHttpOptions); }
                finally { issuingOpen = false; }
            }
        }
        catch (Exception ex) { Complete(request, false, ex.Message); }
    }
    public void Next() { dispatcher.VerifyAccess(); Navigate(1); }
    public void Previous() { dispatcher.VerifyAccess(); Navigate(-1); }
    public void NavigateItems(int offset)
    {
        dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (!HasPlayer || offset == 0) return;
        if (offset is 1 or -1) { Navigate(offset); return; }
        if (NavigationTarget(offset) is { } target) Open(target.Source, target.Child);
    }
    internal (FlyleafQueueSource Source, FlyleafQueueChild? Child)? NavigationTarget(int offset)
    {
        // Expand only already-discovered child sequences. Select once; never
        // open every intermediate item in order to perform a large jump.
        var entries = new List<(FlyleafQueueSource Source, FlyleafQueueChild? Child)>();
        foreach (var source in sources)
            if (source.Children.Count > 0)
                entries.AddRange(source.Children.Select(child => (source, (FlyleafQueueChild?)child)));
            else entries.Add((source, null));
        var origin = NavigationSource;
        var selectedChild = nextRequest?.Child ?? pending?.Child ?? navigationChild;
        if (selectedChild == null && origin != null && origin.Children.Count > 0)
        {
            int index = ReferenceEquals(origin, currentSource) && player != null ? Array.IndexOf(LiveItems(), player.Playlist.Selected) : 0;
            selectedChild = origin.Children.FirstOrDefault(c => c.Position == index);
        }
        if (origin?.Children.Count == 0) selectedChild = null;
        int current = entries.FindIndex(e => ReferenceEquals(e.Source, origin) &&
            (selectedChild == null ? e.Child == null : e.Child?.Position == selectedChild.Position));
        int destination = NavigationIndex(current, entries.Count, offset, repeat);
        return destination < 0 || destination == current && !repeat ? null : entries[destination];
    }
    internal static int NavigationIndex(int current, int count, int offset, bool repeat)
    {
        if (count == 0 || offset == 0 || current < 0 && offset < 0) return -1;
        long index = (long)current + offset;
        return repeat ? (int)((index % count + count) % count) : (int)Math.Clamp(index, 0, count - 1);
    }
    void Navigate(int delta)
    {
        if (NavigationTarget(delta) is { } target) Open(target.Source, target.Child);
    }
    /// <summary>Sorts top-level queue entries by their display names, preserving the current source and embedded item order.</summary>
    public void SortByName(bool descending = false, bool numeric = false)
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sources.Count < 2) return;

        var sorted = numeric
            ? FlyleafNameSorting.OrderByNumber(sources, source => source.Title, descending)
            : descending ? sources.OrderByDescending(source => source.Title, names) : sources.OrderBy(source => source.Title, names);
        ReorderSources(sorted.ToArray());
    }

    /// <summary>Reorders the visible queue once. Playback follows this same order.</summary>
    public void ShuffleItems()
    {
        dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (sources.Count < 2) return;
        var shuffled = sources.ToArray();
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        // Even a random identity permutation should visibly change the queue.
        if (shuffled.SequenceEqual(sources)) (shuffled[0], shuffled[1]) = (shuffled[1], shuffled[0]);
        ReorderSources(shuffled);
    }
    void ReorderSources(FlyleafQueueSource[] order)
    {
        bool changed = false;
        for (int i = 0; i < order.Length; i++)
        {
            if (ReferenceEquals(sources[i], order[i])) continue;
            sources.Move(sources.IndexOf(order[i]), i);
            changed = true;
        }
        if (changed) { Edited(); NotifyNavigation(); }
    }
    void SetCurrent(FlyleafQueueSource? value)
    {
        if (ReferenceEquals(currentSource, value)) return;
        if (value == null) CancelProgression();
        currentSource = value;
        Raise(nameof(CurrentSource)); Raise(nameof(CurrentTitle)); NotifyNavigation();
    }
    void SetMessage(string value) => Set(ref message, value, propertyName: nameof(Message));
    // The highlighted row follows navigation intent, while CurrentSource remains the confirmed playback source.
    void UpdateHighlight()
    {
        var request = nextRequest ?? pending;
        var source = request?.Source ?? (navigationFailed ? navigationSource : currentSource);
        if (source != null && !sources.Contains(source)) source = null;
        if (!ReferenceEquals(highlightedSource, source))
        {
            highlightedSource?.Mark(false);
            highlightedSource?.UnmarkChildren();
            highlightedSource = source;
        }
        if (source == null) return;
        source.Mark(true);
        if (source.Children.Count == 0) return;
        var child = request == null ? navigationChild : request.Child;
        int position = child != null && ReferenceEquals(child.Source, source) ? child.Position : 0;
        foreach (var item in source.Children) item.Mark(item.Position == position);
    }
    void NotifyNavigation() { UpdateHighlight(); UpdateItemNumbers(); Raise(nameof(CanNext)); Raise(nameof(CanPrevious)); Raise(nameof(TotalItems)); Raise(nameof(CurrentItemNumber)); Raise(nameof(ItemCountText)); Raise(nameof(CurrentTitle)); }
    void UpdateItemNumbers()
    {
        int number = 1;
        foreach (var source in sources)
        {
            source.Number(number);
            foreach (var child in source.Children) child.Number(number + child.Position);
            number += Math.Max(1, source.Children.Count);
        }
    }
    PlaylistItem[] LiveItems()
    {
        if (player == null) return [];
        PlaylistItem[] items = [];
        BindingOperations.AccessCollection(player.Playlist.Items, () => items = player.Playlist.Items.ToArray(), false);
        return items;
    }
    PlaylistItem? Resolve(FlyleafQueueChild child)
    {
        var items = LiveItems();
        if (child.DirectUrl != null)
        {
            var matches = items.Where(item => item.DirectUrl == child.DirectUrl).ToArray();
            if (matches.Length == 1) return matches[0];
            return null;
        }
        return !string.IsNullOrWhiteSpace(child.Title) && child.Position < items.Length && items[child.Position].Title == child.Title ? items[child.Position] : null;
    }
    void UpdateChildren()
    {
        if (currentSource != null && player is { IsDisposed: false })
        {
            var items = LiveItems();
            currentSource.Update(items, player.Playlist.Selected);
            if (!navigationFailed && pending == null)
                navigationChild = currentSource.Children.FirstOrDefault(child => child.Position == Array.IndexOf(items, player.Playlist.Selected));
            NotifyNavigation();
        }
    }
    void Complete(Request request, bool success, string? error)
    {
        if (!ReferenceEquals(pending, request)) return;
        pending = null; Raise(nameof(IsOpening));
        if (nextRequest is { } next) { nextRequest = null; Start(next); return; }
        var retained = sources.Contains(request.Source);
        success = success && retained; navigationFailed = !success;
        if (success) { SetCurrent(request.Source); UpdateChildren(); }
        SetMessage(success || !retained ? "" : error ?? "Could not open this source.");
        if (success && request.Item == null && request.Child != null)
        {
            // Resolve against freshly expanded library items; never reuse a cached live item.
            var item = Resolve(request.Child);
            if (item == null) { SetMessage("The requested child is not available. Select a child from the refreshed source."); return; }
            Start(request); return;
        }
        if (success && request.Play && player is { IsDisposed: false, CanPlay: true } target && !target.IsPlaying)
        {
            var version = generation; var selected = target.Playlist.Selected;
            _ = Task.Run(() =>
            {
                try { if (!target.IsDisposed && version == generation && ReferenceEquals(selected, target.Playlist.Selected)) target.Play(); }
                catch (Exception ex) { Post(target, () => { if (version == generation) SetMessage(ex.Message); }); }
            });
        }
        Raise(nameof(IsAutoAdvancing));
        if (success && player is { IsDisposed: false, CanPlay: true } ready) BeginProgression(ready);
        NotifyNavigation();
    }
    internal void SetPlayer(Player? value)
    {
        dispatcher.VerifyAccess(); if (disposed || ReferenceEquals(player, value)) return;
        CancelProgression();
        Observe(false); player = value; generation++; pending = nextRequest = null; navigationSource = null; navigationChild = null; SetCurrent(null); Observe(true);
        Raise(nameof(IsOpening)); Raise(nameof(IsAutoAdvancing)); Raise(nameof(HasPlayer)); NotifyNavigation();
    }
    void Observe(bool attach)
    {
        if (player == null) return;
        if (attach)
        {
            player.Opening += Opening; player.OpenCompleted += OpenCompleted;
            player.OpenPlaylistItemCompleted += ItemCompleted; player.PlaybackStopped += PlaybackStopped;
            player.BufferingStarted += BufferingStarted; player.BufferingCompleted += BufferingCompleted;
            player.PropertyChanged += PlayerChanged; player.Playlist.PropertyChanged += PlaylistChanged;
            player.Playlist.Items.CollectionChanged += ItemsChanged;
        }
        else
        {
            player.Opening -= Opening; player.OpenCompleted -= OpenCompleted;
            player.OpenPlaylistItemCompleted -= ItemCompleted; player.PlaybackStopped -= PlaybackStopped;
            player.BufferingStarted -= BufferingStarted; player.BufferingCompleted -= BufferingCompleted;
            player.PropertyChanged -= PlayerChanged; player.Playlist.PropertyChanged -= PlaylistChanged;
            player.Playlist.Items.CollectionChanged -= ItemsChanged;
        }
    }
    void Post(object? sender, Action action)
    {
        if (dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(() => { if (!disposed && ReferenceEquals(sender, player)) action(); }));
    }
    void Opening(object? sender, OpeningArgs e)
    {
        if (e.IsSubtitles || issuingOpen) return;
        var version = generation;
        Post(sender, () => { if (version != generation) return; generation++; pending = nextRequest = null; navigationSource = null; navigationChild = null; SetCurrent(null); Raise(nameof(IsOpening)); Raise(nameof(IsAutoAdvancing)); NotifyNavigation(); });
    }
    void OpenCompleted(object? sender, OpenCompletedArgs e)
    {
        if (e.IsSubtitles) return;
        var request = pending;
        if (request == null || request.Item != null) return;
        if (!string.Equals(Utils.FixFileUrl(request.Source.Input), e.Url, StringComparison.Ordinal)) return;
        Post(sender, () =>
        {
            if (!ReferenceEquals(request, pending)) return;
            // A superseded discovery must not start a second-stage item open.
            if (nextRequest != null) { Complete(request, false, null); return; }
            if (e.Success && request.Source.HasHttpOptions)
            {
                // Discover first, then attach options before opening the item.
                // Never change shared Demuxer.FormatOpt for one queue entry.
                var items = LiveItems();
                foreach (var item in items) request.Source.ApplyHttpOptions(item);
                var selected = request.Child == null ? items.FirstOrDefault() : Resolve(request.Child);
                if (selected == null) { Complete(request, false, "The source did not produce a playable item."); return; }
                var configured = request with { Item = selected }; pending = configured;
                try { player!.OpenAsync(selected); } catch (Exception ex) { Complete(configured, false, ex.Message); }
                return;
            }
            Complete(request, e.Success, e.Error);
        });
    }
    void ItemCompleted(object? sender, DecoderContext.OpenPlaylistItemCompletedArgs e)
    {
        var request = pending;
        Post(sender, () =>
        {
            if (request?.Item != null && ReferenceEquals(request, pending)) Complete(request, e.Success && ReferenceEquals(e.Item, request.Item), e.Error ?? "Selection changed while opening.");
            else UpdateChildren();
            NotifyNavigation();
        });
    }
    void PlaylistChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Playlist.Selected)) { var target = player; Post(target, UpdateChildren); }
    }
    void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) { var target = player; Post(target, UpdateChildren); }
    void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlyleafLib.MediaPlayer.Player.Status) or nameof(FlyleafLib.MediaPlayer.Player.IsDisposed) or nameof(FlyleafLib.MediaPlayer.Player.LoopPlayback) or nameof(FlyleafLib.MediaPlayer.Player.ReversePlayback))
        {
            var version = generation;
            Post(sender, () =>
            {
                if (version != generation) return;
                if (player?.Status == Status.Stopped && pending == null) { CancelProgression(); SetCurrent(null); }
                else UpdateProgression();
                NotifyNavigation();
            });
        }
    }
    void PlaybackStopped(object? sender, PlaybackStoppedArgs e)
    {
        var version = generation;
        Post(sender, () =>
        {
            if (version != generation) return;
            if (e.Error != null) { progressionError = true; slideTimer.Stop(); return; }
            UpdateProgression();
        });
    }
    void BufferingStarted(object? sender, EventArgs e)
    {
        var version = generation;
        Post(sender, () =>
        {
            if (version != generation) return;
            progressionBuffering = true; UpdateProgression();
        });
    }
    void BufferingCompleted(object? sender, BufferingCompletedArgs e)
    {
        var version = generation;
        Post(sender, () =>
        {
            if (version != generation) return;
            progressionBuffering = false;
            if (e.Error != null) progressionError = true;
            UpdateProgression();
        });
    }
    void BeginProgression(Player target)
    {
        CancelProgression();
        if (progressionError) return;
        progressionItem = target.Playlist.Selected;
        progressionVersion = generation;
        progressionTimestamp = Stopwatch.GetTimestamp();
        UpdateProgression();
    }
    void CancelProgression()
    {
        slideTimer.Stop(); progression.Reset(); progressionItem = null; progressionRunning = false;
    }
    void UpdateProgression()
    {
        if (disposed || player is not { IsDisposed: false } target || progressionItem == null ||
            progressionVersion != generation || pending != null || currentSource == null ||
            !ReferenceEquals(progressionItem, target.Playlist.Selected))
        { slideTimer.Stop(); return; }

        long now = Stopwatch.GetTimestamp();
        double elapsed = progressionRunning ? Stopwatch.GetElapsedTime(progressionTimestamp, now).TotalMilliseconds : 0;
        progressionTimestamp = now;
        bool image = FlyleafQueueProgression.IsImageFormat(target.MainDemuxer?.Name);
        bool singleFrameImage = image && target.Video.FramesTotal == 1 && !target.Audio.IsOpened;
        bool blocked = progressionError || progressionBuffering || target.LoopPlayback || target.ReversePlayback;
        progressionRunning = autoAdvance && !blocked && target.Status is Status.Playing or Status.Ended;
        if (progression.Update(elapsed, SlideShowIntervalProvider?.Invoke() ?? 3000,
            target.Status, target.Duration, target.CurTime, image, target.IsLive, autoAdvance, blocked, singleFrameImage))
        {
            slideTimer.Stop();
            if (NavigationTarget(1) is { } next)
                Start(new Request(next.Source, next.Child, true, Automatic: true));
            return;
        }
        // Timed/animated playback uses its completion event. Poll only image
        // candidates that have not demonstrated a duration or a playback clock.
        if (autoAdvance && !blocked && (image || !target.IsLive && target.Status == Status.Ended) && !progression.TimedPlayback && CanNext &&
            target.Status is Status.Playing or Status.Ended)
            slideTimer.Start();
        else slideTimer.Stop();
    }
    public void Dispose()
    {
        dispatcher.VerifyAccess(); if (disposed) return;
        CancelProgression(); SlideShowIntervalProvider = null;
        Observe(false); foreach (var source in sources) source.PropertyChanged -= SourceChanged;
        player = null; pending = nextRequest = null; disposed = true;
        highlightedSource?.Mark(false); highlightedSource?.UnmarkChildren(); highlightedSource = null;
    }
}
