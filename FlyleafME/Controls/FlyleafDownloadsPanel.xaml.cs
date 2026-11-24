using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Flyleaf.Controls.WPF;

/// <summary>Reusable view of an application-owned manager, independent of playback lifetime.</summary>
public partial class FlyleafDownloadsPanel : UserControl
{
    readonly FlyleafDownloadManager manager;
    public FlyleafPanelContext Context { get; }
    FlyleafDownloadJob? Selected => DownloadList.SelectedItem as FlyleafDownloadJob;
    bool updating;

    public FlyleafDownloadsPanel(FlyleafDownloadManager manager, FlyleafPanelContext context)
    {
        this.manager = manager; Context = context;
        InitializeComponent();
        DataContext = manager;
        ConcurrencyBox.ItemsSource = Enumerable.Range(1, 8).ToArray();
        Loaded += (_, _) => { manager.Changed -= ManagerChanged; manager.Changed += ManagerChanged; Refresh(); };
        Unloaded += (_, _) => manager.Changed -= ManagerChanged;
        Refresh();
    }

    public void AddDownload(string? input = null) => AddDownloadsCore(input, null);

    public void AddCurrentDownload()
    {
        var view = Context.MediaElement;
        if (view?.Player?.Playlist.Selected is not { } item || view.Queue.IsOpening) return;
        var playlist = view.Player.Playlist;
        var source = view.Queue.CurrentSource;
        string? input = source?.Input ?? playlist.Url;
        if (!string.IsNullOrWhiteSpace(input))
            AddDownloads([new(input, item.Title, item.Index >= 0 ? item.Index : null, item.DirectUrl, source?.UserAgent, source?.Referrer)]);
    }

    public void AddDownloads(IReadOnlyList<FlyleafDownloadTarget> targets)
    {
        if (targets.Count != 0) AddDownloadsCore(null, targets);
    }

    void AddDownloadsCore(string? input, IReadOnlyList<FlyleafDownloadTarget>? targets)
    {
        if (!manager.CanAdd || Context.MediaElement == null) return;
        var dialog = new FlyleafNewDownloadDialog(Context.MediaElement.Resources, input, manager.DownloadFolder, Context.Player?.Config, targets) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var requests = targets is { Count: > 1 }
                ? targets.Select(t => dialog.Request with { Input = t.Input, Target = t, Title = t.Title }).ToArray()
                : new[] { dialog.Request };
            foreach (var request in requests) DownloadList.SelectedItem = manager.Add(request, dialog.SourceConfig, dialog.StartNow);
            DownloadList.ScrollIntoView(DownloadList.SelectedItem);
        }
        catch (Exception ex) { Context.MediaElement?.ShowError(ex.Message); }
    }
    void ManagerChanged(object? sender, EventArgs e) => Refresh();
    void Refresh()
    {
        updating = true;
        ConcurrencyBox.SelectedItem = manager.Concurrency;
        updating = false;
        var state = Selected?.State;
        AddButton.IsEnabled = manager.CanAdd;
        StartAllButton.IsEnabled = manager.CanAdd && manager.Jobs.Any(j => j.State is FlyleafDownloadState.Queued or FlyleafDownloadState.Paused);
        PauseAllButton.IsEnabled = manager.Jobs.Any(j => j.State == FlyleafDownloadState.Downloading);
        StopAllButton.IsEnabled = manager.CanAdd && manager.HasPending;
        ClearButton.IsEnabled = manager.Jobs.Any(j => j.Terminal);
        StartButton.IsEnabled = manager.CanAdd && state is (FlyleafDownloadState.Queued or FlyleafDownloadState.Paused);
        PauseButton.IsEnabled = state == FlyleafDownloadState.Downloading;
        StopButton.IsEnabled = Selected is { Terminal: false } && state != FlyleafDownloadState.Stopping;
        RemoveButton.IsEnabled = Selected?.Terminal == true;
        OpenButton.IsEnabled = Selected is { Terminal: true, HasOutput: true } j && File.Exists(j.OutputPath) && state != FlyleafDownloadState.Failed;
        FolderButton.IsEnabled = Selected != null && Directory.Exists(Selected.Folder);
    }
    void Selection_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Refresh(); }
    void Concurrency_Changed(object sender, SelectionChangedEventArgs e)
    { if (!updating && ConcurrencyBox.SelectedItem is int n) manager.Concurrency = n; }
    void Add_Click(object sender, RoutedEventArgs e) => AddDownload();
    void StartAll_Click(object sender, RoutedEventArgs e) => manager.StartAll();
    void PauseAll_Click(object sender, RoutedEventArgs e)
    { foreach (var j in manager.Jobs.ToArray()) manager.Pause(j); }
    async void StopAll_Click(object sender, RoutedEventArgs e) => await manager.StopAllAsync();
    void Clear_Click(object sender, RoutedEventArgs e) => manager.ClearFinished();
    void Start_Click(object sender, RoutedEventArgs e) { if (Selected is { } j) manager.Start(j); }
    void Pause_Click(object sender, RoutedEventArgs e) { if (Selected is { } j) manager.Pause(j); }
    async void Stop_Click(object sender, RoutedEventArgs e) { if (Selected is { } j) await manager.StopAsync(j); }
    void Remove_Click(object sender, RoutedEventArgs e) { if (Selected is { } j) manager.Remove(j); }
    void Open_Click(object sender, RoutedEventArgs e)
    { if (Selected is { Terminal: true, HasOutput: true } j && File.Exists(j.OutputPath)) Context.MediaElement?.OpenInput(j.OutputPath); }
    void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } j) return;
        try { Process.Start(new ProcessStartInfo(j.Folder) { UseShellExecute = true }); }
        catch (Exception ex) { Context.MediaElement?.ShowError(ex.Message); }
    }
}
