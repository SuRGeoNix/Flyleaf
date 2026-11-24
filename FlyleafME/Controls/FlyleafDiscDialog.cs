using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using DiscType = Flyleaf.Controls.WPF.FlyleafDiscInput.DiscType;

namespace Flyleaf.Controls.WPF;

/// <summary>Opens a disc title through FFmpeg's existing protocol/demuxer, without disc navigation.</summary>
internal sealed class FlyleafDiscDialog : FlyleafModalDialog
{
    readonly bool blurayAvailable, dvdAvailable;
    readonly ComboBox type = new(), drives = new(), minimumDuration = new();
    readonly ListBox playlists = new() { SelectionMode = SelectionMode.Extended, MaxHeight = 180, DisplayMemberPath = nameof(FlyleafMplsReader.Playlist.Label) };
    readonly TextBox playlist = new();
    readonly TextBlock playlistStatus = FlyleafPanelUI.Text("", secondary: true);
    FlyleafMplsReader.Playlist[] availablePlaylists = [];
    CancellationTokenSource? playlistScan;
    readonly TextBox location = new(), title = new() { Text = "1" }, angle = new() { Text = "1" }, chapter = new() { Text = "1" };
    readonly TextBlock detection = FlyleafPanelUI.Text("", secondary: true), error = FlyleafPanelUI.ValidationMessage();
    readonly StackPanel playlistRow = new(), titleRow = new();
    string? inspectedLocation;
    DiscType detectedType;
    string? discRoot;
    internal string[] Inputs { get; private set; } = [];

    // The playlist scan is advisory; manual or automatic selection remains available.

    internal FlyleafDiscDialog(bool append, ResourceDictionary theme) : base(append ? "Add disc" : "Open disc")
    {
        Resources.MergedDictionaries.Add(theme);
        (blurayAvailable, dvdAvailable) = FlyleafDiscInput.Capabilities();
        var body = new StackPanel { Width = 480, Margin = new Thickness(18) };
        AddField(body, "Disc type", type);
        type.Items.Add(new ComboBoxItem { Content = "Automatic", IsEnabled = blurayAvailable || dvdAvailable });
        type.Items.Add(new ComboBoxItem { Content = blurayAvailable ? "Blu-ray" : "Blu-ray (not supported by this FFmpeg build)", IsEnabled = blurayAvailable });
        type.Items.Add(new ComboBoxItem { Content = dvdAvailable ? "DVD" : "DVD (not supported by this FFmpeg build)", IsEnabled = dvdAvailable });
        type.SelectedIndex = 0;

        AddField(body, "Optical drive", drives);
        drives.Items.Add("Choose a drive…");
        foreach (var drive in DriveInfo.GetDrives())
            if (drive.DriveType == DriveType.CDRom) drives.Items.Add(drive.Name);
        drives.SelectedIndex = 0;

        AddField(body, "Disc location", location);
        var browse = FlyleafPanelUI.Button("Browse folder…");
        var iso = FlyleafPanelUI.Button("Browse DVD ISO…");
        iso.IsEnabled = dvdAvailable; iso.Margin = new Thickness(8, 0, 0, 0);
        var locations = new StackPanel { Orientation = Orientation.Horizontal };
        locations.Children.Add(browse); locations.Children.Add(iso); body.Children.Add(locations);
        body.Children.Add(detection);
        body.Children.Add(FlyleafPanelUI.SectionHeading("Advanced options", topMargin: 16));
        playlistRow.Children.Add(FlyleafPanelUI.Text("Blu-ray playlists · select one or more (Ctrl/Shift)"));
        var playlistActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        minimumDuration.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
        foreach (var label in new[] { "All durations", "5+ minutes", "20+ minutes", "60+ minutes" }) minimumDuration.Items.Add(label);
        minimumDuration.SelectedIndex = 0;
        AutomationProperties.SetName(minimumDuration, "Minimum playlist duration");
        var selectAll = FlyleafPanelUI.Button("Select all shown"); selectAll.Margin = new Thickness(8, 0, 0, 0);
        var clear = FlyleafPanelUI.Button("Clear selection"); clear.Margin = new Thickness(8, 0, 0, 0);
        playlistActions.Children.Add(minimumDuration); playlistActions.Children.Add(selectAll); playlistActions.Children.Add(clear);
        playlistRow.Children.Add(playlistActions);
        playlists.SetResourceReference(Control.BackgroundProperty, "FlyleafME.RaisedSurfaceBrush");
        playlists.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush");
        playlists.SetResourceReference(Control.BorderBrushProperty, "FlyleafME.BorderBrush");
        playlists.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "FlyleafME.SuggestionItemStyle");
        AutomationProperties.SetName(playlists, "Blu-ray playlists");
        playlistRow.Children.Add(playlists); playlistRow.Children.Add(playlistStatus);
        AddField(playlistRow, "Manual playlist number (empty = automatic when none selected)", playlist);
        minimumDuration.SelectionChanged += (_, _) => { if (playlistScan == null) FilterPlaylists(); };
        selectAll.Click += (_, _) => playlists.SelectAll();
        clear.Click += (_, _) => { playlists.UnselectAll(); playlist.Clear(); };
        playlists.SelectionChanged += (_, _) => { if (playlists.SelectedItems.Count > 0) playlist.Clear(); };
        playlist.TextChanged += (_, _) => { if (playlist.Text.Length > 0) playlists.UnselectAll(); };
        AddField(titleRow, "DVD title", title);
        body.Children.Add(playlistRow); body.Children.Add(titleRow);
        AddField(body, "Angle (1 = first)", angle);
        AddField(body, "Start chapter", chapter);
        body.Children.Add(FlyleafPanelUI.Text("Direct title playback only. Mount Blu-ray ISO images in Windows first. Interactive disc menus are not supported.", secondary: true));
        body.Children.Add(error);

        var accept = FlyleafPanelUI.Button(append ? "Add" : "Open"); accept.IsDefault = true;
        accept.IsEnabled = blurayAvailable || dvdAvailable;
        var cancel = FlyleafPanelUI.Button("Cancel"); cancel.IsCancel = true; cancel.Margin = new Thickness(8, 0, 0, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(accept); actions.Children.Add(cancel); body.Children.Add(actions);
        Body.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(280, SystemParameters.WorkArea.Height - 100) };

        drives.SelectionChanged += (_, _) =>
        {
            if (drives.SelectedIndex > 0 && drives.SelectedItem is string path) SetLocation(path);
        };
        location.LostKeyboardFocus += (_, _) => InspectLocation();
        type.SelectionChanged += (_, _) => UpdateType();
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Select disc root, BDMV or VIDEO_TS folder" };
            if (dialog.ShowDialog(this) == true) SetLocation(dialog.FolderName);
        };
        iso.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Select DVD image", Filter = "Disc images (*.iso)|*.iso", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) { type.SelectedIndex = (int)DiscType.Dvd; SetLocation(dialog.FileName); }
        };
        accept.Click += (_, _) => Accept();
        if (drives.Items.Count == 2) drives.SelectedIndex = 1;
        else UpdateType();
        Loaded += (_, _) => location.Focus();
        Closed += (_, _) => playlistScan?.Cancel();
    }

    static void AddField(Panel panel, string label, Control control)
    {
        panel.Children.Add(FlyleafPanelUI.Text(label));
        control.SetResourceReference(StyleProperty, control is ComboBox ? "FlyleafME.ComboBoxStyle" : "FlyleafME.InputStyle");
        control.Margin = new Thickness(0, 4, 0, 8);
        AutomationProperties.SetName(control, label);
        panel.Children.Add(control);
    }

    DiscType SelectedType => type.SelectedIndex < 0 ? DiscType.Auto : (DiscType)type.SelectedIndex;
    DiscType EffectiveType => SelectedType == DiscType.Auto ? detectedType : SelectedType;

    void SetLocation(string path)
    {
        location.Text = path;
        InspectLocation();
    }

    void InspectLocation()
    {
        string path = location.Text.Trim();
        if (path == inspectedLocation) return;
        inspectedLocation = path; discRoot = null; detectedType = DiscType.Auto;
        playlistScan?.Cancel(); playlistScan = null;
        availablePlaylists = []; playlists.ItemsSource = null; playlist.Clear(); playlistStatus.Text = "";
        minimumDuration.SelectedIndex = 0;
        error.Text = "";
        try
        {
            if (path.Length > 0)
            {
                // A drive letter alone refers to its root, not the drive's current working directory.
                if (path.Length == 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') path += Path.DirectorySeparatorChar;
                path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                if (Directory.Exists(path))
                {
                    discRoot = FlyleafDiscInput.NormalizeRoot(path);
                    var (bd, dvd) = FlyleafDiscInput.Structures(discRoot);
                    detectedType = bd == dvd ? DiscType.Auto : bd ? DiscType.Bluray : DiscType.Dvd;
                    if (bd) LoadPlaylists(discRoot);
                }
                else if (File.Exists(path) && Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase)) discRoot = path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error.Text = ex.Message;
        }
        UpdateType();
    }

    async void LoadPlaylists(string root)
    {
        var scan = new CancellationTokenSource(); playlistScan = scan;
        playlistStatus.Text = "Reading playlist information…";
        try
        {
            var found = await Task.Run(() => FlyleafMplsReader.Scan(root, scan.Token), scan.Token);
            if (scan.IsCancellationRequested || !ReferenceEquals(playlistScan, scan)) return;
            availablePlaylists = found;
            FilterPlaylists();
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!scan.IsCancellationRequested && ReferenceEquals(playlistScan, scan)) playlistStatus.Text = "Playlist information unavailable: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(playlistScan, scan)) playlistScan = null;
            scan.Dispose();
        }
    }

    void FilterPlaylists()
    {
        int minutes = minimumDuration.SelectedIndex switch { 1 => 5, 2 => 20, 3 => 60, _ => 0 };
        var selected = playlists.SelectedItems.Cast<FlyleafMplsReader.Playlist>().Select(item => item.Number).ToHashSet();
        var visible = availablePlaylists.Where(item => minutes == 0 || item.Duration is { } duration && duration.TotalMinutes >= minutes).ToArray();
        playlists.ItemsSource = visible;
        foreach (var item in visible) if (selected.Contains(item.Number)) playlists.SelectedItems.Add(item);
        playlistStatus.Text = availablePlaylists.Length == 0 ? "No playlists found. Automatic and manual selection are available." : $"{visible.Length} of {availablePlaylists.Length} playlists · longest first";
    }

    void UpdateType()
    {
        var effective = EffectiveType;
        playlistRow.Visibility = effective != DiscType.Dvd ? Visibility.Visible : Visibility.Collapsed;
        titleRow.Visibility = effective == DiscType.Dvd ? Visibility.Visible : Visibility.Collapsed;
        detection.Text = detectedType switch
        {
            DiscType.Bluray => "Blu-ray structure detected",
            DiscType.Dvd => "DVD structure detected",
            _ => discRoot != null && File.Exists(discRoot) ? "DVD ISO: select DVD explicitly" : "Select a mounted disc or extracted disc root"
        };
        if (effective == DiscType.Bluray && !blurayAvailable) detection.Text += " · Blu-ray support unavailable";
        if (effective == DiscType.Dvd && !dvdAvailable) detection.Text += " · DVD support unavailable";
        if (!blurayAvailable && !dvdAvailable) detection.Text = "Neither Blu-ray nor DVD playback is available in the loaded FFmpeg build.";
    }

    static bool Number(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    void Accept()
    {
        InspectLocation();
        error.Text = "";
        if (discRoot == null) { error.Text = "Choose an existing disc location."; return; }
        var selected = EffectiveType;
        if (selected == DiscType.Auto) { error.Text = "Select the disc type explicitly."; return; }
        if (selected == DiscType.Bluray && !blurayAvailable || selected == DiscType.Dvd && !dvdAvailable)
        { error.Text = "The loaded FFmpeg build does not support this disc type."; return; }
        if (selected == DiscType.Bluray && !Directory.Exists(discRoot)) { error.Text = "Mount Blu-ray ISO images in Windows first."; return; }
        if (Directory.Exists(discRoot) && !Directory.Exists(Path.Combine(discRoot, selected == DiscType.Bluray ? "BDMV" : "VIDEO_TS")))
        { error.Text = "The selected folder does not contain the required disc structure."; return; }
        if (!Number(angle.Text, 1, selected == DiscType.Bluray ? 255 : 9, out int a)) { error.Text = "Enter a valid angle number (starting at 1)."; return; }
        if (!Number(chapter.Text, 1, 65534, out int c)) { error.Text = "Enter a valid chapter number (starting at 1)."; return; }

        if (selected == DiscType.Bluray)
        {
            string choice = playlist.Text.Trim();
            int p = -1;
            if (choice.Length != 0 && !Number(choice, 0, 99999, out p))
            { error.Text = "Enter an MPLS playlist number (0–99999), or leave empty for Automatic."; return; }
            var selectedPlaylists = playlists.SelectedItems.Cast<FlyleafMplsReader.Playlist>().OrderBy(item => item.Number).ToArray();
            Inputs = selectedPlaylists.Length == 0 ? [FlyleafDiscInput.BuildBluray(discRoot, p, a, c)]
                : selectedPlaylists.Select(item => FlyleafDiscInput.BuildBluray(discRoot, item.Number, a, c)).ToArray();
        }
        else
        {
            if (!Number(title.Text, 1, 99, out int t)) { error.Text = "Enter a DVD title number (1–99)."; return; }
            Inputs = [FlyleafDiscInput.BuildDvd(discRoot, t, a, c)];
        }
        // The current fmt:// parser uses the first ampersand as the options delimiter.
        if (Inputs.Any(input => input.StartsWith("fmt://", StringComparison.Ordinal)) && discRoot.Contains('&'))
        { error.Text = "Disc paths containing & are not supported with format options. Choose a different path."; Inputs = []; return; }
        DialogResult = true;
    }
}
