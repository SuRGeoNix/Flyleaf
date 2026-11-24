using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaStream;
using Microsoft.Win32;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

internal sealed class FlyleafNewDownloadDialog : FlyleafModalDialog
{
    readonly TextBox input = new(), folder = new(), name = new(), extension = new();
    readonly CheckBox scheduledStart = new() { Content = "Start at a specific date and time" }, limitedDuration = new() { Content = "Limit recording duration" };
    readonly DatePicker recordingDate = new() { SelectedDateFormat = DatePickerFormat.Short };
    readonly ComboBox startHour = new(), startMinute = new(), durationDays = new(), durationHours = new(), durationMinutes = new(), durationSeconds = new();
    readonly IReadOnlyList<FlyleafDownloadTarget>? targets;
    bool Batch => targets is { Count: > 1 };
    readonly TextBlock error = ValidationMessage(), resolved = Text("", true);
    readonly CheckBox defaultAudio = new() { Content = "Follow video (suggest audio)", IsChecked = true };
    readonly StackPanel advanced = new() { Visibility = Visibility.Collapsed };
    readonly ComboBox mode = new(), media = new(), videoInputs = new(), audioInputs = new();
    readonly ComboBox videoStreams = new(), audioStreams = new();
    readonly Button inspect, browseInput, add, start, cancel;
    readonly Config config;
    Downloader? probe;
    bool busy, updating;
    internal FlyleafDownloadRequest Request { get; private set; } = null!;
    internal bool StartNow { get; private set; }
    internal Config SourceConfig => config;
    string Input => targets?[0].Input ?? input.Text.Trim();
    bool Custom => mode.SelectedIndex == 1;
    bool FollowAudio => defaultAudio.IsChecked == true;

    internal FlyleafNewDownloadDialog(ResourceDictionary theme, string? initialInput, string downloadFolder, Config? source, IReadOnlyList<FlyleafDownloadTarget>? targets = null) : base(targets is { Count: > 1 } ? "New downloads" : "New download")
    {
        this.targets = targets;
        config = FlyleafDownloadManager.CreateConfig(source);
        Resources.MergedDictionaries.Add(theme);
        // Keep the themed modal chrome, but give the scrollable body a finite viewport.
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Manual;
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(1, workArea.Width - 32);
        MaxHeight = Math.Max(1, workArea.Height - 40);
        MinWidth = Math.Min(440, MaxWidth);
        MinHeight = Math.Min(300, MaxHeight);
        Width = Math.Min(720, MaxWidth);
        Height = Math.Min(560, MaxHeight);
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0, GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            ResizeBorderThickness = new Thickness(6), UseAeroCaptionButtons = false
        });
        var body = new StackPanel { Margin = new Thickness(14, 4, 14, 12) };
        input.Text = Batch ? $"{targets!.Count} selected queue items" : targets?[0].Input ?? initialInput ?? ""; folder.Text = downloadFolder;
        if (Batch) { input.ToolTip = string.Join(Environment.NewLine, targets!.Select(t => t.Title ?? t.Input)); name.IsEnabled = false; }
        foreach (var box in new[] { input, folder, name, extension }) box.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        foreach (var combo in new[] { mode, media, videoInputs, audioInputs, videoStreams, audioStreams, startHour, startMinute, durationDays, durationHours, durationMinutes, durationSeconds })
        {
            combo.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            combo.MaxDropDownHeight = 280;
        }
        defaultAudio.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        body.Children.Add(FieldLabel("Input URL or file"));
        browseInput = IconButton("\uE838", "Browse input file");
        browseInput.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "All files|*.*" };
            if (dialog.ShowDialog(this) == true) input.Text = dialog.FileName;
        };
        body.Children.Add(WithButton(input, browseInput));
        if (targets != null) { input.IsReadOnly = true; browseInput.IsEnabled = false; }
        mode.ItemsSource = Batch ? new[] { "Automatic" } : new[] { "Automatic", "Custom" }; mode.SelectedIndex = 0;
        media.ItemsSource = new[] { "Video + audio", "Video only", "Audio only" }; media.SelectedIndex = 0;
        var automatic = Section("Media selection", media, Text("Uses the library's recommended streams. No input inspection is needed.", true));
        body.Children.Add(Section("Download mode", mode)); body.Children.Add(automatic);
        inspect = Button("Resolve streams…"); inspect.Margin = new Thickness(0, 6, 0, 4); inspect.Visibility = Visibility.Collapsed;
        inspect.Click += async (_, _) => await InspectAsync();
        body.Children.Add(inspect); body.Children.Add(resolved);
        defaultAudio.Margin = new Thickness(0, 8, 0, 4);
        advanced.Children.Add(Section("Video", Text("External input (if available)", true), videoInputs, Text("Output stream", true), videoStreams));
        advanced.Children.Add(Section("Audio", defaultAudio, Text("External input (if available)", true), audioInputs, Text("Output stream", true), audioStreams));
        advanced.Children.Add(Text("Choose one video and one audio track, or None for either. Changing an external input inspects only that input. Choose Automatic for the recommended streams.", true));
        body.Children.Add(advanced);
        mode.SelectionChanged += async (_, _) =>
        {
            if (busy) return;
            automatic.Visibility = Custom ? Visibility.Collapsed : Visibility.Visible;
            inspect.Visibility = Custom ? Visibility.Visible : Visibility.Collapsed;
            if (!Custom && probe != null) await InspectAsync();
            else if (Custom && probe == null && Input.Length != 0) await InspectAsync();
            else SetBusy(false);
        };
        videoInputs.SelectionChanged += async (_, _) => await SwitchAsync(video: true);
        audioInputs.SelectionChanged += async (_, _) => await SwitchAsync(video: false);
        videoStreams.SelectionChanged += async (_, _) => await FollowAudioAsync();
        audioStreams.SelectionChanged += (_, _) => { if (!updating) UpdateResolved(); };
        defaultAudio.Checked += async (_, _) => await FollowAudioAsync();
        defaultAudio.Unchecked += (_, _) => { if (!updating) { UpdateSelectors(); UpdateResolved(); } };
        body.Children.Add(FieldLabel("Download folder"));
        var browseFolder = IconButton("\uE838", "Browse download folder");
        browseFolder.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Download folder" };
            if (Directory.Exists(folder.Text)) dialog.InitialDirectory = folder.Text;
            if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName;
        };
        body.Children.Add(WithButton(folder, browseFolder));
        var output = new Grid(); output.ColumnDefinitions.Add(new()); output.ColumnDefinitions.Add(new() { Width = new GridLength(130) });
        var filename = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        filename.Children.Add(FieldLabel("Filename (optional)")); filename.Children.Add(name);
        var suffix = new StackPanel(); suffix.Children.Add(FieldLabel("Extension (optional)")); suffix.Children.Add(extension);
        Grid.SetColumn(suffix, 1); output.Children.Add(filename); output.Children.Add(suffix); body.Children.Add(output);
        body.Children.Add(Text(Batch ? "Each item uses its resolved title. Leave the extension blank for the recommended container." : "Leave both blank to use the resolved media title and the library's recommended extension. Existing files are never overwritten.", true));
        var schedule = new StackPanel();
        foreach (var check in new[] { scheduledStart, limitedDuration })
        {
            check.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
            check.Margin = new Thickness(0, 8, 0, 6);
        }
        var next = DateTime.Now.AddMinutes(5);
        recordingDate.SelectedDate = next.Date;
        recordingDate.DisplayDateStart = DateTime.Today;
        recordingDate.MinWidth = 130;
        recordingDate.SetResourceReference(Control.BackgroundProperty, "FlyleafME.RaisedSurfaceBrush");
        recordingDate.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush");
        recordingDate.SetResourceReference(Control.BorderBrushProperty, "FlyleafME.BorderBrush");
        // Theme the date field; keep the native calendar's own system palette.
        var dateTextStyle = new Style(typeof(System.Windows.Controls.Primitives.DatePickerTextBox));
        dateTextStyle.Setters.Add(new Setter(Control.ForegroundProperty, new System.Windows.DynamicResourceExtension("FlyleafME.TextBrush")));
        dateTextStyle.Setters.Add(new Setter(Control.BackgroundProperty, new System.Windows.DynamicResourceExtension("FlyleafME.RaisedSurfaceBrush")));
        dateTextStyle.Setters.Add(new Setter(TextBox.IsReadOnlyProperty, true));
        recordingDate.Resources[typeof(System.Windows.Controls.Primitives.DatePickerTextBox)] = dateTextStyle;
        System.Windows.Automation.AutomationProperties.SetName(recordingDate, "Recording start date");
        ConfigureNumber(startHour, 23, next.Hour, "Start hour");
        ConfigureNumber(startMinute, 59, next.Minute, "Start minute");
        ConfigureNumber(durationDays, 365, 0, "Recording days");
        ConfigureNumber(durationHours, 23, 1, "Recording hours");
        ConfigureNumber(durationMinutes, 59, 0, "Recording minutes");
        ConfigureNumber(durationSeconds, 59, 0, "Recording seconds");
        var startFields = new WrapPanel { IsEnabled = false };
        startFields.Children.Add(NumberField("Date (local)", recordingDate));
        startFields.Children.Add(NumberField("Hour", startHour));
        startFields.Children.Add(NumberField("Minute", startMinute));
        var durationFields = new WrapPanel { IsEnabled = false };
        durationFields.Children.Add(NumberField("Days", durationDays));
        durationFields.Children.Add(NumberField("Hours", durationHours));
        durationFields.Children.Add(NumberField("Minutes", durationMinutes));
        durationFields.Children.Add(NumberField("Seconds", durationSeconds));
        schedule.Children.Add(scheduledStart); schedule.Children.Add(startFields);
        schedule.Children.Add(limitedDuration); schedule.Children.Add(durationFields);
        limitedDuration.Checked += (_, _) => durationFields.IsEnabled = true;
        limitedDuration.Unchecked += (_, _) => durationFields.IsEnabled = false;
        scheduledStart.Checked += (_, _) =>
        {
            startFields.IsEnabled = true;
            if (recordingDate.SelectedDate is { } date && date.AddHours(startHour.SelectedIndex).AddMinutes(startMinute.SelectedIndex) <= DateTime.Now)
            {
                var future = DateTime.Now.AddMinutes(5);
                recordingDate.DisplayDateStart = DateTime.Today;
                recordingDate.SelectedDate = future.Date;
                startHour.SelectedIndex = future.Hour; startMinute.SelectedIndex = future.Minute;
            }
        };
        scheduledStart.Unchecked += (_, _) => startFields.IsEnabled = false;
        schedule.Children.Add(Text("Scheduled recordings use a fixed time window. If a slot is busy, recording may start late. Pausing does not extend the window. Flyleaf must remain running.", true));
        body.Children.Add(CollapsibleSection("Recording schedule (optional)", schedule));
        var footer = new StackPanel { Margin = new Thickness(14, 6, 14, 12) };
        footer.Children.Add(error);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        cancel = Button("Cancel"); cancel.IsCancel = true;
        add = Button("Add to downloads"); add.Margin = new Thickness(6, 0, 0, 0);
        start = Button("Download now"); start.IsDefault = true; start.Margin = new Thickness(6, 0, 0, 0);
        start.SetResourceReference(StyleProperty, "FlyleafME.PrimaryButtonStyle");
        actions.Children.Add(cancel); actions.Children.Add(add); actions.Children.Add(start); footer.Children.Add(actions);
        add.Click += (_, _) => Accept(false); start.Click += (_, _) => Accept(true);
        void UpdateScheduleActions()
        {
            bool scheduled = scheduledStart.IsChecked == true;
            add.Visibility = scheduled ? Visibility.Collapsed : Visibility.Visible;
            start.Content = scheduled ? "Schedule recording" : Batch ? "Download all now" : "Download now";
        }
        scheduledStart.Checked += (_, _) => UpdateScheduleActions();
        scheduledStart.Unchecked += (_, _) => UpdateScheduleActions();
        UpdateScheduleActions();
        var footerBorder = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "FlyleafME.BorderBrush");
        var layout = new DockPanel(); DockPanel.SetDock(footerBorder, Dock.Bottom); layout.Children.Add(footerBorder);
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Body.Content = layout;
        Loaded += (_, _) => input.Focus();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Closed += (_, _) =>
        {
            try { probe?.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            finally { probe = null; }
        };
    }

    static TextBlock FieldLabel(string label)
    {
        var text = Text(label); text.Margin = new Thickness(0, 12, 0, 5); return text;
    }
    static void ConfigureNumber(ComboBox box, int maximum, int initial, string label)
    {
        box.ItemsSource = Enumerable.Range(0, maximum + 1).Select(n => n.ToString("00", CultureInfo.InvariantCulture)).ToArray();
        box.SelectedIndex = initial; box.MinWidth = maximum > 99 ? 82 : 68;
        System.Windows.Automation.AutomationProperties.SetName(box, label);
    }
    static StackPanel NumberField(string label, FrameworkElement control)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 8, 4) };
        var heading = Text(label, true); heading.Margin = new Thickness(0, 0, 0, 4);
        field.Children.Add(heading); field.Children.Add(control); return field;
    }
    static DockPanel WithButton(TextBox box, Button button)
    {
        var row = new DockPanel(); button.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button); row.Children.Add(box); return row;
    }
    void UpdateSelectors()
    {
        videoInputs.IsEnabled = !busy && videoInputs.Items.Count != 0 && (videoInputs.Items.Count > 1 || videoInputs.SelectedItem == null);
        videoStreams.IsEnabled = !busy && videoStreams.Items.Count > 1;
        audioInputs.IsEnabled = !busy && !FollowAudio && audioInputs.Items.Count != 0 &&
            (audioInputs.Items.Count > 1 || audioInputs.SelectedItem == null);
        audioStreams.IsEnabled = !busy && !FollowAudio && audioStreams.Items.Count > 1;
    }
    void SetBusy(bool value)
    {
        busy = value; mode.IsEnabled = inspect.IsEnabled = cancel.IsEnabled = !value;
        add.IsEnabled = start.IsEnabled = !value && (!Custom || probe != null);
        advanced.IsEnabled = !value;
        input.IsReadOnly = targets != null || value || probe != null; browseInput.IsEnabled = !input.IsReadOnly;
        media.IsEnabled = !value; UpdateSelectors();
    }
    async Task InspectAsync()
    {
        if (busy) return;
        error.Text = "";
        if (probe != null)
        {
            SetBusy(true);
            try { await Task.Run(probe.Dispose); }
            catch (Exception ex) { error.Text = ex.Message; }
            finally
            {
                probe = null; advanced.Visibility = Visibility.Collapsed; resolved.Text = "";
                inspect.Content = "Resolve streams…"; SetBusy(false);
            }
            return;
        }
        if (Input.Length == 0) { error.Text = "Enter a media URL or file."; SetBusy(false); return; }
        SetBusy(true); string url = Input; resolved.Text = "Resolving media…";
        try
        {
            probe = new Downloader(FlyleafDownloadManager.CreateConfig(config));
            FlyleafDownloadSelection.Check(await Task.Run(() => targets != null ? targets[0].Open(probe) : probe.Open(url)));
            RefreshStreams();
            advanced.Visibility = Visibility.Visible; inspect.Content = "Change input / reset stream selection";
            UpdateResolved();
        }
        catch (Exception ex)
        {
            error.Text = ex.Message; resolved.Text = "";
            if (probe != null)
                try { await Task.Run(probe.Dispose); }
                catch (Exception closeError) { error.Text += " · " + closeError.Message; }
            probe = null; advanced.Visibility = Visibility.Collapsed; inspect.Content = "Resolve streams…";
        }
        finally { SetBusy(false); }
    }
    sealed record InputChoice(ExternalStream? Stream, string Label)
    { public override string ToString() => Label; }
    static void FillInputs(ComboBox box, IEnumerable<ExternalStream> streams, ExternalStream? current)
    {
        var choices = streams.Select(s => new InputChoice(s, FlyleafExternalChoice.Describe(s))).ToList();
        if (current == null) choices.Insert(0, new InputChoice(null, choices.Count == 0 ? "No separate input" : "No external input selected"));
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => ReferenceEquals(c.Stream, current)) ?? choices.FirstOrDefault();
    }
    sealed record TrackChoice(StreamBase? Stream, string Label)
    { public override string ToString() => Label; }
    static StreamBase? Selected(ComboBox box) => (box.SelectedItem as TrackChoice)?.Stream;
    static void FillTracks(ComboBox box, IEnumerable<StreamBase> streams, StreamBase? current)
    {
        var choices = streams.Select(s => new TrackChoice(s, FlyleafStreamChoice.Describe(s) +
            (s is AudioStream ? (s.Demuxer.Type == MediaType.Audio ? " · External" : " · Embedded") : ""))).Prepend(new TrackChoice(null, "None")).ToArray();
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => ReferenceEquals(c.Stream, current)) ??
            choices.FirstOrDefault(c => c.Stream?.Enabled == true) ?? choices[0];
    }
    void RefreshStreams(StreamBase? previousVideo = null, StreamBase? previousAudio = null, bool keepVideo = false, bool keepAudio = false)
    {
        var ctx = probe!.DecCtx; var item = ctx.Playlist.Selected ?? throw new InvalidOperationException("No media item was resolved.");
        updating = true;
        try
        {
            FillInputs(videoInputs, item.ExternalVideoStreams, item.ExternalVideoStream);
            FillInputs(audioInputs, item.ExternalAudioStreams, item.ExternalAudioStream);
            FillTracks(videoStreams, ctx.VideoDemuxer.VideoStreams, keepVideo ? previousVideo : ctx.VideoStream);
            FillTracks(audioStreams, ctx.VideoDemuxer.AudioStreams.Cast<StreamBase>().Concat(ctx.AudioDemuxer.AudioStreams),
                keepAudio ? previousAudio : ctx.AudioStream);
        }
        finally { updating = false; }
        UpdateSelectors(); UpdateResolved();
    }
    // Select the suggested audio against the exact selected embedded video (including MPEG-TS programs).
    static void SuggestAudio(DecoderContext ctx, VideoStream? video)
    {
        foreach (var stream in ctx.VideoDemuxer.VideoStreams.Where(s => s.Enabled && !ReferenceEquals(s, video)).ToArray())
            ctx.VideoDemuxer.DisableStream(stream);
        if (video != null && !video.Enabled) FlyleafDownloadSelection.Check(ctx.OpenVideoStream(video, false).Error);
        // Remove previous audio selections before asking the existing plugin pipeline to suggest one.
        foreach (var stream in ctx.VideoDemuxer.AudioStreams.Where(s => s.Enabled).ToArray()) ctx.VideoDemuxer.DisableStream(stream);
        foreach (var stream in ctx.AudioDemuxer.AudioStreams.Where(s => s.Enabled).ToArray()) ctx.AudioDemuxer.DisableStream(stream);
        ctx.SuggestAudio(out var embedded, out var external, ctx.VideoDemuxer.AudioStreams);
        if (embedded != null) FlyleafDownloadSelection.Check(ctx.OpenAudioStream(embedded).Error);
        else if (external != null)
        {
            if (ReferenceEquals(external, ctx.Playlist.Selected.ExternalAudioStream) && !ctx.AudioDemuxer.Disposed &&
                ctx.SuggestAudio(ctx.AudioDemuxer.AudioStreams) is { } track)
                FlyleafDownloadSelection.Check(ctx.OpenAudioStream(track).Error);
            else FlyleafDownloadSelection.Check(ctx.Open(external).Error);
        }
    }
    async Task FollowAudioAsync()
    {
        if (updating || busy || probe == null) return;
        if (!FollowAudio) { UpdateResolved(); return; }
        var video = Selected(videoStreams) as VideoStream;
        SetBusy(true); error.Text = "";
        try
        {
            await Task.Run(() => SuggestAudio(probe.DecCtx, video));
            RefreshStreams(video, keepVideo: true);
        }
        catch (Exception ex) { await ResetAfterFailureAsync(ex); }
        finally { SetBusy(false); }
    }
    async Task SwitchAsync(bool video)
    {
        if (updating || busy || probe == null || (!video && FollowAudio)) return;
        if ((video ? videoInputs : audioInputs).SelectedItem is not InputChoice { Stream: { } stream }) return;
        var previousVideo = Selected(videoStreams); var previousAudio = Selected(audioStreams);
        bool follow = FollowAudio;
        SetBusy(true); error.Text = "";
        try
        {
            await Task.Run(() =>
            {
                FlyleafDownloadSelection.Check(probe.DecCtx.Open(stream, false).Error);
                if (video && follow) SuggestAudio(probe.DecCtx, probe.DecCtx.VideoStream);
            });
            var ctx = probe.DecCtx;
            // A new video input invalidates its embedded tracks, but not an independently selected external audio track.
            bool preserveAudio = video && !follow && (previousAudio == null || ctx.AudioDemuxer.AudioStreams.Any(s => ReferenceEquals(s, previousAudio)));
            RefreshStreams(previousVideo, previousAudio, keepVideo: !video, keepAudio: preserveAudio);
            if (!video && ctx.AudioDemuxer.AudioStream is { } selected)
            {
                updating = true;
                try { audioStreams.SelectedItem = audioStreams.Items.OfType<TrackChoice>().FirstOrDefault(c => ReferenceEquals(c.Stream, selected)); }
                finally { updating = false; }
            }
            UpdateResolved();
        }
        catch (Exception ex) { await ResetAfterFailureAsync(ex); }
        finally { SetBusy(false); }
    }
    async Task ResetAfterFailureAsync(Exception ex)
    {
        error.Text = ex.Message;
        // A failed external open may invalidate its native streams; do not accept stale selections.
        try { await Task.Run(probe!.Dispose); } catch { }
        probe = null; advanced.Visibility = Visibility.Collapsed; resolved.Text = ""; inspect.Content = "Resolve streams…";
    }
    void UpdateResolved()
    {
        if (probe == null) return;
        var ctx = probe.DecCtx;
        var video = Selected(videoStreams); var audio = Selected(audioStreams);
        string ext = video != null || audio?.Demuxer.Type == MediaType.Video ? ctx.VideoDemuxer.Extension : audio != null ? ctx.AudioDemuxer.Extension : "";
        resolved.Text = ctx.Playlist.Selected.Title + $" · {(video == null ? 0 : 1)} video / {(audio == null ? 0 : 1)} audio selected" + (string.IsNullOrEmpty(ext) ? "" : " · Recommended extension: ." + ext);
    }
    DateTimeOffset? ReadRecordingStart()
    {
        if (scheduledStart.IsChecked != true) return null;
        if (recordingDate.SelectedDate is not { } date || startHour.SelectedIndex < 0 || startMinute.SelectedIndex < 0)
            throw new ArgumentException("Choose a recording start date and time.");
        var local = date.Date.AddHours(startHour.SelectedIndex).AddMinutes(startMinute.SelectedIndex);
        if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
            throw new ArgumentException("Choose an unambiguous local start time outside the daylight-saving transition.");
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }
    TimeSpan? ReadRecordingDuration()
    {
        if (limitedDuration.IsChecked != true) return null;
        if (new[] { durationDays, durationHours, durationMinutes, durationSeconds }.Any(box => box.SelectedIndex < 0))
            throw new ArgumentException("Choose a recording duration.");
        var duration = new TimeSpan(durationDays.SelectedIndex, durationHours.SelectedIndex, durationMinutes.SelectedIndex, durationSeconds.SelectedIndex);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(365))
            throw new ArgumentException("Choose a recording duration greater than zero and at most 365 days.");
        return duration;
    }
    void Accept(bool begin)
    {
        if (busy) return;
        try
        {
            FlyleafDownloadSelection? selection = null;
            string? title = targets?[0].Title;
            if (Custom)
            {
                if (probe == null) throw new ArgumentException("Resolve the input before selecting streams.");
                var item = probe.DecCtx.Playlist.Selected;
                var video = Selected(videoStreams); var selectedAudio = Selected(audioStreams);
                var main = new[] { video, selectedAudio }.OfType<StreamBase>().Where(s => s.Demuxer.Type == MediaType.Video).Select(FlyleafStreamChoice.From).ToArray();
                var audio = new[] { selectedAudio }.OfType<StreamBase>().Where(s => s.Demuxer.Type == MediaType.Audio).Select(FlyleafStreamChoice.From).ToArray();
                if (main.Length + audio.Length == 0) throw new ArgumentException("Select at least one audio or video stream.");
                title = item.Title;
                selection = new(title, item.Index, item.ExternalVideoStream != null ? FlyleafExternalChoice.From(item.ExternalVideoStream) : null,
                    item.ExternalAudioStream != null ? FlyleafExternalChoice.From(item.ExternalAudioStream) : null, main, audio);
            }
            Request = new FlyleafDownloadRequest(Input, folder.Text.Trim(), name.Text, extension.Text, selection, !Custom && media.SelectedIndex == 2, title, VideoOnly: !Custom && media.SelectedIndex == 1,
                StartAt: ReadRecordingStart(), RecordingDuration: ReadRecordingDuration(), Target: targets?[0]).Validate();
            StartNow = begin || Request.StartAt.HasValue; DialogResult = true;
        }
        catch (Exception ex) { error.Text = ex.Message; }
    }
}
