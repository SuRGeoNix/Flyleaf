using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using Microsoft.Win32;
using static Flyleaf.Controls.WPF.FlyleafMenuItems;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    public event EventHandler<FlyleafMenuBuildingEventArgs>? MenuBuilding;
    public event EventHandler<FlyleafOpenInputEventArgs>? OpenInputRequested;
    /// <summary>Handles a batch selected in the Open dialog. Subscribing enables multi-select.</summary>
    public event EventHandler<IReadOnlyList<string>>? OpenFilesRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler<FlyleafPlaylistActionEventArgs>? PlaylistActionRequested;
    public static readonly DependencyProperty PlaylistStatusProperty = DependencyProperty.Register(
        nameof(PlaylistStatus), typeof(string), typeof(FlyleafME), new PropertyMetadata("Queue"));
    public string PlaylistStatus { get => (string)GetValue(PlaylistStatusProperty); set => SetValue(PlaylistStatusProperty, value); }
    public static readonly DependencyProperty PlaylistActionsAvailableProperty = DependencyProperty.Register(
        nameof(PlaylistActionsAvailable), typeof(bool), typeof(FlyleafME), new PropertyMetadata(false));
    public bool PlaylistActionsAvailable { get => (bool)GetValue(PlaylistActionsAvailableProperty); set => SetValue(PlaylistActionsAvailableProperty, value); }
    public static readonly DependencyProperty SupportsFolderImportProperty = DependencyProperty.Register(
        nameof(SupportsFolderImport), typeof(bool), typeof(FlyleafME),
        new PropertyMetadata(false, (d, _) => ((FlyleafME)d).RefreshPanelContexts()));
    /// <summary>Shows folder import actions when the application handles AddFolder requests.</summary>
    public bool SupportsFolderImport { get => (bool)GetValue(SupportsFolderImportProperty); set => SetValue(SupportsFolderImportProperty, value); }
    internal bool HasPlaylistActionHandler => PlaylistActionRequested != null;
    internal bool CanOfferFolderImport => SupportsFolderImport && HasPlaylistActionHandler;

    internal void AddFolderMenuItem(ItemsControl menu)
    {
        if (CanOfferFolderImport)
            Add(menu, "Add folder…", PlaylistActionsAvailable ? () => RequestPlaylistAction(FlyleafPlaylistAction.AddFolder) : null);
    }
    bool CanChangeQueue => !disposed && (!HasPlaylistActionHandler || PlaylistActionsAvailable);
    internal void AddOpenQueueMenuItems(ItemsControl menu, bool directInput = false)
    {
        Add(menu, "Open files…", CanChangeQueue ? () =>
        {
            if (directInput) OpenFile();
            else if (HasPlaylistActionHandler) RequestPlaylistAction(FlyleafPlaylistAction.Open);
            else PickQueueFiles(false);
        } : null);
        if (CanOfferFolderImport)
            Add(menu, "Open folder…", CanChangeQueue ? () => RequestPlaylistAction(FlyleafPlaylistAction.OpenFolder) : null);
        Add(menu, "Open URL…", CanChangeQueue ? () =>
        {
            if (directInput) OpenUrl();
            else if (HasPlaylistActionHandler) RequestPlaylistAction(FlyleafPlaylistAction.OpenUrl);
            else PromptQueueUrl(false);
        } : null);
        Add(menu, "Open disc…", CanOpenDisc ? () => OpenDisc(false, directInput) : null);
        Add(menu, "Paste", CanChangeQueue ? () => PasteMenuInput(false, directInput) : null);
    }
    internal void AddAppendQueueMenuItems(ItemsControl menu)
    {
        Add(menu, "Add files…", CanChangeQueue ? () =>
        {
            if (HasPlaylistActionHandler) RequestPlaylistAction(FlyleafPlaylistAction.Add);
            else PickQueueFiles(true);
        } : null);
        AddFolderMenuItem(menu);
        Add(menu, "Add URL…", CanChangeQueue ? () =>
        {
            if (HasPlaylistActionHandler) RequestPlaylistAction(FlyleafPlaylistAction.AddUrl);
            else PromptQueueUrl(true);
        } : null);
        Add(menu, "Add disc…", CanOpenDisc ? () => OpenDisc(true) : null);
        Add(menu, "Paste", CanChangeQueue ? () => PasteMenuInput(true) : null);
    }
    void AddNewQueueMenuItem(ItemsControl menu)
    {
        Add(menu, "New queue", CanChangeQueue ? () =>
        {
            if (HasPlaylistActionHandler) RequestPlaylistAction(FlyleafPlaylistAction.New);
            else Queue.Clear();
        } : null);
    }
    internal void AddSaveQueueMenuItems(ItemsControl menu)
    {
        if (!HasPlaylistActionHandler) return;
        Separator(menu);
        bool canSave = CanChangeQueue;
        Add(menu, "Save", canSave ? () => RequestPlaylistAction(FlyleafPlaylistAction.Save) : null);
        Add(menu, "Save as…", canSave ? () => RequestPlaylistAction(FlyleafPlaylistAction.SaveAs) : null);
        Add(menu, "Export…", canSave ? () => RequestPlaylistAction(FlyleafPlaylistAction.Export) : null);
    }
    internal void AddQueueMenuItems(ItemsControl menu)
    {
        AddNewQueueMenuItem(menu);
        Separator(menu);
        AddOpenQueueMenuItems(menu);
        AddSaveQueueMenuItems(menu);
    }
    public void RequestPlaylistAction(FlyleafPlaylistAction action, IReadOnlyList<string>? inputs = null) => PlaylistActionRequested?.Invoke(this, new(action, inputs));
    ContextMenu? generalMenu;
    int menuActionGeneration;

    /// <summary>Routes a user-supplied input through application policy, then defaults to the current player.</summary>
    public void OpenInput(string input)
    {
        Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var request = new FlyleafOpenInputEventArgs(input);
        OpenInputRequested?.Invoke(this, request);
        if (!request.Handled) Player?.OpenAsync(input);
    }

    public void ShowContextMenu()
    {
        Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        ShowContextMenu(null, PlacementMode.MousePoint);
    }

    internal void ShowContextMenu(FrameworkElement? anchor, PlacementMode placement, FlyleafPlaybackBar? playbackBar = null)
    {
        if (!IsLoaded || disposed) return;
        CloseGeneralMenu();
        var menu = BuildContextMenu(); generalMenu = menu;
        menu.PlacementTarget = anchor ?? (Host.Overlay is { IsVisible: true } overlay ? overlay : Host.Surface is { IsVisible: true } surface ? surface : this);
        menu.Placement = placement;
        if (playbackBar != null) menu.Opened += (_, _) => playbackBar.TrackOpenPlaybackMenu(menu);
        menu.IsOpen = true;
    }
    void CloseGeneralMenu() { if (generalMenu != null) generalMenu.IsOpen = false; generalMenu = null; }

    ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu { Resources = Resources, MaxHeight = SystemParameters.WorkArea.Height };
        menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
        menu.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "FlyleafME.ThemeMenuItemStyle");
        var target = Player;
        bool Current() => !disposed && target is { IsDisposed: false } && ReferenceEquals(Player, target);
        var source = target?.Playlist.Selected;
        bool CurrentInput() => Current() && ReferenceEquals(source, target!.Playlist.Selected);
        IDisposable? activity = null;
        menu.Opened += (_, _) => activity = FlyleafPopupActivity.Hold(target);
        menu.Closed += (_, _) => { activity?.Dispose(); activity = null; if (ReferenceEquals(generalMenu, menu)) generalMenu = null; };

        var input = Group(menu, "Input");
        AddOpenQueueMenuItems(input, directInput: true);
        Separator(input);
        Add(input, "Copy original input", !string.IsNullOrEmpty(target?.Playlist.Url) ? () =>
        {
            if (CurrentInput()) { try { target!.CopyToClipboard(); } catch (Exception ex) { MenuError(ex); } }
        } : null);
        Add(input, "Copy current item", !string.IsNullOrEmpty(source?.DirectUrl) ? () =>
        {
            if (CurrentInput()) { try { target!.CopyItemToClipboard(); } catch (Exception ex) { MenuError(ex); } }
        } : null);
        Separator(input);
        Add(input, "Close input", Current() && (target!.CanPlay || target.Status == Status.Opening || target.Playlist.Url != null || target.Playlist.IOStream != null)
            ? () => RunMenuAction(target!, p => p.Stop()) : null);

        var queue = Group(menu, "Queue");
        AddNewQueueMenuItem(queue);
        AddAppendQueueMenuItems(Group(queue, "Add"));
        Separator(queue);
        Add(queue, "Previous item", Queue.CanPrevious ? () => Queue.Previous() : null);
        Add(queue, "Next item", Queue.CanNext ? () => Queue.Next() : null);
        Add(queue, "Search queue…", CanSearchQueue ? () => { Dispatcher.BeginInvoke(new Action(ShowQueueSearch)); } : null).InputGestureText = QueueSearchShortcut ?? "";
        Separator(queue);
        if (Queue.Sources.Count >= 2)
        {
            FlyleafMenuItems.QueueSort(Group(queue, "Sort"), Queue, () => !disposed);
            Add(queue, "Shuffle", () => { Queue.ShuffleItems(); ShowOSD("Queue shuffled"); });
            Separator(queue);
        }
        Add(queue, "Auto-next", () => Queue.AutoAdvance = !Queue.AutoAdvance, Queue.AutoAdvance).IsCheckable = true;
        Add(queue, "Repeat queue", () => Queue.Repeat = !Queue.Repeat, Queue.Repeat).IsCheckable = true;
        PanelLink(queue, "queue");

        var playback = Group(menu, "Playback");
        var speeds = Group(playback, "Speed");
        speeds.IsEnabled = FlyleafPlaybackActions.CanPlay(target);
        if (target != null)
        {
            FlyleafMenuItems.Speed(speeds, target, Current);
            FlyleafMenuItems.Modes(playback, target, Current);
        }
        Separator(playback);
        var chapters = Group(playback, "Chapters");
        if (FlyleafPlaybackActions.CanPlay(target) && target?.Chapters is { Count: > 0 } choices)
            foreach (var chapter in choices)
                Add(chapters, $"{FlyleafPlaybackBar.FormatTime(Math.Max(0, chapter.StartTime))} · {chapter.Title}", () =>
                {
                    if (CurrentInput()) RunMenuAction(target, p => { if (p.Chapters.Contains(chapter)) p.SeekToChapter(chapter); });
                });
        else chapters.IsEnabled = false;
        var frames = Group(playback, "Frame stepping");
        frames.IsEnabled = FlyleafPlaybackActions.CanStep(target);
        Add(frames, "Previous frame", () => RunMenuAction(target!, p => { if (FlyleafPlaybackActions.CanStep(p)) p.ShowFramePrev(); }));
        Add(frames, "Next frame", () => RunMenuAction(target!, p => { if (FlyleafPlaybackActions.CanStep(p)) p.ShowFrameNext(); }));
        PanelLink(playback, "playback");

        var video = Group(menu, "Video");
        Add(video, "Enabled", Current() ? () => RunMenuAction(target!, p => p.Video.Toggle()) : null, target?.Video.Enabled == true);
        AddCaptureDeviceMenu(video, target, video: true);
        var renderer = target?.Renderer;
        bool canFrame = Current() && target!.Video.IsOpened && renderer is { Disposed: false };
        void ChangeFraming(Action<Renderer> change)
        {
            if (!CurrentInput() || !target!.Video.IsOpened || renderer is not { Disposed: false } || !ReferenceEquals(renderer, target.Renderer)) return;
            try { change(renderer); }
            catch (Exception ex) { MenuError(ex); }
        }

        if (CurrentInput() && target is { Video.IsOpened: true })
        {
            var config = target.Config.Video;
            Add(video, "Super Resolution", () =>
            {
                if (!CurrentInput() || !target.Video.IsOpened) return;
                try
                {
                    config.SuperResolution = !config.SuperResolution;
                    NotifySettingsEdited(target);
                }
                catch (Exception ex) { MenuError(ex); }
            }, config.SuperResolution);
        }
        Separator(video);
        var size = Group(video, "Size");
        size.IsEnabled = canFrame;
        Add(size, "Fit to window", canFrame ? () => ChangeFraming(r => r.SetOriginalSize(false)) : null, renderer?.OriginalSize == false);
        Add(size, "Original size", canFrame ? () => ChangeFraming(r => r.SetOriginalSize(true)) : null, renderer?.OriginalSize == true);
        var aspect = Group(video, "Aspect ratio");
        aspect.IsEnabled = canFrame && renderer?.OriginalSize == false;
        foreach (var ratio in AspectRatio.AspectRatios.Where(ratio => ratio != AspectRatio.Custom))
            Add(aspect, ratio.ToString(), canFrame ? () => ChangeFraming(r => r.AspectRatio = ratio) : null, renderer?.AspectRatio == ratio);
        Add(video, "Rotate left 90°", canFrame ? () => ChangeFraming(r => r.RotateLeft()) : null);
        Add(video, "Rotate right 90°", canFrame ? () => ChangeFraming(r => r.RotateRight()) : null);
        Add(video, "Reset framing", canFrame ? () => ChangeFraming(r => r.ResetViewport()) : null);
        Separator(video);
        Add(video, "Take snapshot", canFrame && target!.CanPlay ? () =>
        {
            if (CurrentInput()) RunMenuAction(target, p => { if (p.Video.IsOpened) p.TakeSnapshotToFile(); });
        } : null);
        if (Current()) FlyleafTrackMenus.AddVideoTracks(video, target!, Current);
        PanelLink(video, "video");

        var audio = Group(menu, "Audio");
        Add(audio, "Enabled", Current() ? () => { if (Current()) RunMenuAction(target!, p => p.Audio.Toggle()); } : null, target?.Audio.Enabled == true);
        Add(audio, "Mute", Current() ? () => { if (Current()) target!.Audio.Mute = !target.Audio.Mute; } : null, target?.Audio.Mute == true);
        Separator(audio);
        AddCaptureDeviceMenu(audio, target, video: false);
        var devices = Group(audio, "Output device");
        if (Current())
            foreach (var endpoint in Engine.Audio.Devices)
                Add(devices, endpoint.Name, () => { if (Current()) target!.Audio.Device = endpoint; }, endpoint.Id == target!.Audio.Device?.Id);
        else devices.IsEnabled = false;
        Add(audio, "Reset delay", target?.Audio.IsOpened == true ? () => RunMenuAction(target, p => p.Audio.Delay = 0) : null);
        if (Current()) FlyleafTrackMenus.AddAudioTracks(audio, target!, Current);
        PanelLink(audio, "audio");

        var subtitles = Group(menu, "Subtitles");
        Add(subtitles, "Enabled", Current() ? () => RunMenuAction(target!, p => p.Subtitles.Toggle()) : null, target?.Subtitles.Enabled == true);
        Add(subtitles, "Reset delay", target?.Subtitles.IsOpened == true ? () => RunMenuAction(target, p => p.Subtitles.Delay = 0) : null);
        if (Current()) FlyleafTrackMenus.AddSubtitleTracks(subtitles, target!, Current);
        PanelLink(subtitles, "subtitles");

        var information = Group(menu, "Media information");
        Add(information, "Show debug overlay", () => Information.ShowDebugOverlay = !Information.ShowDebugOverlay, Information.ShowDebugOverlay);
        PanelLink(information, "information");
        MenuBuilding?.Invoke(this, new FlyleafMenuBuildingEventArgs(menu));
        Separator(menu);
        Add(menu, IsFullScreen ? "Exit fullscreen" : "Fullscreen", () => SetCurrentValue(IsFullScreenProperty, !IsFullScreen));
        if (panelDefinitions.ContainsKey("settings")) Add(menu, "Settings…", () => ShowPanel("settings"));
        else if (SettingsRequested != null) Add(menu, "Settings…", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }
    void PanelLink(ItemsControl menu, string id)
    {
        if (!panelDefinitions.ContainsKey(id)) return;
        if (menu.Items.Count > 0) Separator(menu);
        Add(menu, "Open panel…", () => Dispatcher.BeginInvoke(new Action(() => { if (!disposed && IsLoaded && panelDefinitions.ContainsKey(id)) ShowPanel(id); })));
    }
    async void RunMenuAction(Player target, Action<Player> action)
    {
        if (disposed || target.IsDisposed || !ReferenceEquals(Player, target)) return;
        var source = target.Playlist.Selected;
        var generation = Volatile.Read(ref menuActionGeneration);
        using var activity = FlyleafPopupActivity.Hold(target);
        // Player is a WPF dependency property: validate ownership on the UI thread,
        // and use a plain generation value to cancel stale work on the worker.
        try { await Task.Run(() => { if (!target.IsDisposed && Volatile.Read(ref menuActionGeneration) == generation && ReferenceEquals(source, target.Playlist.Selected)) action(target); }); }
        catch (Exception ex) { if (!disposed && ReferenceEquals(Player, target)) MenuError(ex); }
    }
    internal bool CanOpenCaptureDevice(Player? target) => CanChangeQueue && Engine.IsLoaded && target is { IsDisposed: false } && ReferenceEquals(Player, target);
    internal void PopulateCaptureDeviceMenu(ItemsControl menu, Player? target, bool video)
    {
        FlyleafCaptureDeviceMenus.Populate(menu, video, () => CanOpenCaptureDevice(target), input =>
        {
            if (!CanOpenCaptureDevice(target)) return;
            try { OpenInput(input); }
            catch (Exception ex) { MenuError(ex); }
        });
    }
    void AddCaptureDeviceMenu(ItemsControl menu, Player? target, bool video)
    {
        var group = Group(menu, "Capture devices");
        group.IsEnabled = CanOpenCaptureDevice(target);
        Add(group, "Loading devices…", null);
        group.SubmenuOpened += (_, e) =>
        {
            if (ReferenceEquals(e.Source, group)) PopulateCaptureDeviceMenu(group, target, video);
        };
    }
    void MenuError(Exception error) => MessageBox.Show(Window.GetWindow(this), error.Message, "Media action", MessageBoxButton.OK, MessageBoxImage.Error);
    /// <summary>Shows a file picker and routes single inputs or batches through the corresponding open event.</summary>
    public void OpenFile()
    {
        Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        var openFiles = OpenFilesRequested;
        var dialog = new OpenFileDialog { Title = "Open files", Filter = "All files|*.*", Multiselect = openFiles != null };
        using var activity = FlyleafPopupActivity.Hold(Player);
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || disposed) return;
        try
        {
            var files = dialog.FileNames;
            if (files.Length > 1) openFiles?.Invoke(this, files);
            else OpenInput(dialog.FileName);
        }
        catch (Exception ex) { MenuError(ex); }
    }
    void OpenUrl()
    {
        var input = PromptForInput("Open URL", "Media URL");
        if (input != null) try { OpenInput(input); } catch (Exception ex) { MenuError(ex); }
    }
    /// <summary>Opens a media input or files copied to the clipboard through the normal input policy.</summary>
    public void PasteInput() => PasteMenuInput(false, directInput: true);

    void PasteMenuInput(bool append, bool directInput = false)
    {
        if (!CanChangeQueue) return;
        try
        {
            string[] inputs = Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>().ToArray() : [Clipboard.GetText().Trim()];
            inputs = inputs.Where(input => !string.IsNullOrWhiteSpace(input)).ToArray();
            if (inputs.Length == 0) return;
            if (directInput && inputs.Length == 1) OpenInput(inputs[0]);
            else if (directInput && OpenFilesRequested is { } openFiles) openFiles(this, inputs);
            else if (HasPlaylistActionHandler) RequestPlaylistAction(append ? FlyleafPlaylistAction.AddInputs : FlyleafPlaylistAction.OpenInputs, inputs);
            else ApplyQueueInputs(inputs, append);
        }
        catch (Exception ex) { MenuError(ex); }
    }
    void PickQueueFiles(bool append)
    {
        if (!CanChangeQueue) return;
        var dialog = new OpenFileDialog { Title = append ? "Add files" : "Open files", Filter = "All files|*.*", Multiselect = true };
        using var activity = FlyleafPopupActivity.Hold(Player);
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || disposed) return;
        try { ApplyQueueInputs(dialog.FileNames, append); }
        catch (Exception ex) { MenuError(ex); }
    }
    void PromptQueueUrl(bool append)
    {
        if (!CanChangeQueue) return;
        var input = PromptForInput(append ? "Add URL" : "Open URL", "Media URL", append ? "Add" : "Open");
        if (!string.IsNullOrWhiteSpace(input))
            try { ApplyQueueInputs([input], append); } catch (Exception ex) { MenuError(ex); }
    }
    void ApplyQueueInputs(IReadOnlyList<string> inputs, bool append)
    {
        if (!CanChangeQueue || inputs.Count == 0) return;
        Queue.ApplyEntries(inputs.Select(input => new FlyleafQueueEntry { Input = input }), append);
        if (!append && Queue.Sources.FirstOrDefault() is { } first)
            Queue.Open(first, play: Player?.Config.Playback.AutoPlay == true);
    }
    public string? PromptForInput(string title, string label, string acceptLabel = "Open")
    {
        Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        using var activity = FlyleafPopupActivity.Hold(Player);
        var dialog = new FlyleafInputDialog(title, label, acceptLabel) { Owner = Window.GetWindow(this) };
        dialog.Resources.MergedDictionaries.Add(Resources);
        return dialog.ShowDialog() == true && !disposed ? dialog.Input.Text.Trim() : null;
    }
    bool IsGeneralMenuTarget(object original, out FlyleafPlaybackBar? playbackBar)
    {
        playbackBar = null;
        if (original is not DependencyObject source) return true;
        for (DependencyObject? item = source; item != null; item = item is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
        {
            if (item is ButtonBase or TextBoxBase or Slider or ComboBox or FlyleafPanel ||
                ReferenceEquals(item, overlayHeader) || ReferenceEquals(item, debugOverlay) ||
                item is FrameworkElement { ContextMenu: not null }) return false;
            if (item is FlyleafPlaybackBar bar) playbackBar = bar;
        }
        return true;
    }
    void HostRightClick(object sender, MouseButtonEventArgs e)
    {
        if (IsQueueSearchOpen || e.Handled || !IsGeneralMenuTarget(e.OriginalSource, out var playbackBar)) return;
        ShowContextMenu(null, PlacementMode.MousePoint, playbackBar); e.Handled = true;
    }
    void HostMenuKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleDebugOverlayKey(sender, e, Keyboard.Modifiers)) return;
        if (IsQueueSearchOpen || e.Handled || !(e.Key == Key.Apps || e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift) ||
            !IsGeneralMenuTarget(e.OriginalSource, out var playbackBar)) return;
        ShowContextMenu(e.OriginalSource as FrameworkElement, PlacementMode.Bottom, playbackBar); e.Handled = true;
    }
}
