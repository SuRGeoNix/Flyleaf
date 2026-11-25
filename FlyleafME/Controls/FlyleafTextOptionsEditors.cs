using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;
/// <summary>Compact editors shared by subtitle appearance and OSD preferences.</summary>
public abstract class FlyleafTextOptionsEditor : StackPanel
{
    readonly List<Action> refreshers = [];
    readonly List<Action> discardDrafts = [];
    readonly TextBlock error = ValidationMessage();
    protected bool Updating;
    protected void Begin() { Children.Clear(); refreshers.Clear(); discardDrafts.Clear(); error.Text = ""; }
    protected void DiscardDrafts() { foreach (var discard in discardDrafts) discard(); error.Text = ""; }
    protected void Finish() { Children.Add(error); Refresh(); }
    protected void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Refresh)); return; }
        Updating = true; try { foreach (var refresh in refreshers) refresh(); } finally { Updating = false; }
    }
    protected void Changed(object? sender, PropertyChangedEventArgs e) => Refresh();
    protected void Apply(Action action)
    {
        try { action(); error.Text = ""; Refresh(); } catch (Exception ex) { error.Text = ex.Message; }
    }
    protected void Row(string title, FrameworkElement editor)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(92) });
        row.ColumnDefinitions.Add(new() { MaxWidth = editor.MaxWidth });
        var label = Text(title, true); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(label); Grid.SetColumn(editor, 1); row.Children.Add(editor); Children.Add(row);
    }
    protected TextBox Entry(string title, Func<string> read, Action<string> write, double width = 90)
        => Entry(title, read, write, out _, width);

    TextBox Entry(string title, Func<string> read, Action<string> write, out Action<string> choose, double width)
    {
        var input = new TextBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        bool dirty = false;
        discardDrafts.Add(() => dirty = false);
        input.TextChanged += (_, _) => { if (!Updating) dirty = true; };
        void Commit() { if (!dirty || Updating) return; Apply(() => { write(input.Text); dirty = false; }); }
        choose = value => { input.Text = value; Commit(); };
        input.LostKeyboardFocus += (_, _) => Commit(); input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        refreshers.Add(() => { if (!dirty && !input.IsKeyboardFocusWithin) input.Text = read(); });
        Row(title, input); return input;
    }
    protected void Number(string title, Func<double> read, Action<double> write)
        => Entry(title, () => read().ToString("0.###", CultureInfo.CurrentCulture), text =>
        { if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) throw new ArgumentException("Enter a number."); write(value); }, 76);
    protected void Toggle(string title, Func<bool> read, Action<bool> write)
    {
        var check = new CheckBox { Content = title, Margin = new Thickness(0, 6, 0, 6) }; check.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        check.Click += (_, _) => { if (!Updating) Apply(() => write(check.IsChecked == true)); };
        refreshers.Add(() => check.IsChecked = read()); Children.Add(check);
    }
    protected void Choice<T>(string title, IEnumerable<T> choices, Func<T> read, Action<T> write)
    {
        var select = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 190 };
        select.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
        select.SelectionChanged += (_, _) => { if (!Updating && select.SelectedItem is T item) Apply(() => write(item)); };
        refreshers.Add(() => select.SelectedItem = read()); Row(title, select);
    }
    protected void Color(string title, Func<string> read, Action<string> write)
    {
        var input = Entry(title, read, write, out var choose, 108);
        input.ToolTip = "#RRGGBB or #AARRGGBB · alpha controls transparency";
        var row = (Grid)Children[^1]; row.Children.Remove(input);
        var body = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = FlyleafColorPicker.Swatch(title, out var chip);
        body.Children.Add(input); body.Children.Add(swatch); Grid.SetColumn(body, 1); row.Children.Add(body);
        refreshers.Add(() => chip.Background = FlyleafTextValues.Brush(read()));
        swatch.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = swatch, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
            var picker = new MenuItem { Header = "Pick a color…" };
            picker.Click += (_, _) =>
            {
                menu.IsOpen = false;
                var current = (System.Windows.Media.Color)ColorConverter.ConvertFromString(read());
                if (FlyleafColorPicker.Pick(Window.GetWindow(swatch), current) is { } color && input.IsLoaded)
                    choose(color.ToString());
            };
            menu.Items.Add(picker);
            menu.Items.Add(new Separator());
            foreach (var (name, value) in new[] { ("White", "#FFFFFFFF"), ("Black", "#FF000000"), ("Yellow", "#FFFFFF00"), ("Warm white", "#FFFFE8C0"), ("Cyan", "#FF80E8FF"), ("Transparent", "#00000000") })
            {
                var item = new MenuItem { Header = name, Icon = new Border { Width = 14, Height = 14, Background = FlyleafTextValues.Brush(value), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) } };
                item.Click += (_, _) => choose(value); menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
    }
    protected void RefreshWith(Action action) => refreshers.Add(action);
}
public sealed class FlyleafSubtitleAppearanceEditor : FlyleafTextOptionsEditor
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(nameof(Options), typeof(FlyleafSubtitleOptions), typeof(FlyleafSubtitleAppearanceEditor), new PropertyMetadata(null, OptionsChanged));
    public FlyleafSubtitleOptions? Options { get => (FlyleafSubtitleOptions?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    internal bool ShowResetButton { get; init; } = true;
    static readonly Lazy<string[]> fonts = new(() => Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(f => f).ToArray());
    static void OptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (FlyleafSubtitleAppearanceEditor)d;
        if (e.OldValue is FlyleafSubtitleOptions old) PropertyChangedEventManager.RemoveHandler(old, editor.Changed, "");
        if (e.NewValue is FlyleafSubtitleOptions current) PropertyChangedEventManager.AddHandler(current, editor.Changed, "");
        editor.Build();
    }
    void Build()
    {
        Begin(); if (Options is not { } o) return;
        var font = new ComboBox { ItemsSource = fonts.Value, IsEditable = true, IsTextSearchEnabled = true, MaxWidth = 260 };
        font.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
        void CommitFont() { if (!Updating && font.Text != o.FontFamily) Apply(() => o.FontFamily = font.Text); }
        font.LostKeyboardFocus += (_, _) => CommitFont();
        font.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitFont(); e.Handled = true; } };
        font.SelectionChanged += (_, _) => { if (!Updating && font.SelectedItem is string family) Apply(() => o.FontFamily = family); };
        RefreshWith(() => { if (!font.IsKeyboardFocusWithin) font.Text = o.FontFamily; }); Row("Font", font);
        Number("Size", () => o.FontSize, v => o.FontSize = v);
        Toggle("Bold", () => o.Bold, v => o.Bold = v); Toggle("Italic", () => o.Italic, v => o.Italic = v);
        var bold = (CheckBox)Children[^2]; var italic = (CheckBox)Children[^1];
        Children.Remove(bold); Children.Remove(italic); bold.Margin = new Thickness(0, 6, 12, 6);
        var styles = new StackPanel { Orientation = Orientation.Horizontal }; styles.Children.Add(bold); styles.Children.Add(italic); Row("Style", styles);
        Color("Text color", () => o.Color, v => o.Color = v);
        Color("Outline color", () => o.OutlineColor, v => o.OutlineColor = v);
        Number("Outline", () => o.OutlineThickness, v => o.OutlineThickness = v);
        Number("Bottom offset", () => o.BottomOffset, v => o.BottomOffset = v);
        Toggle("Keep inside video", () => o.WithinVideo, v => o.WithinVideo = v);
        Children.Add(Text("Size, outline and offset scale with the playback area. Text subtitles only; bitmap subtitles keep their own appearance.", true));
        var preview = new FlyleafSubtitleText { Text = "The quick brown fox\nSubtitle appearance preview", Options = o, Height = 125 };
        Children.Add(new Border { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 53)), Child = preview, Margin = new Thickness(0, 10, 0, 8), ClipToBounds = true });
        if (ShowResetButton)
        {
            var reset = ResetButton("Reset appearance");
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Click += (_, _) => Reset();
            Children.Add(reset);
        }
        Finish();
    }
    internal void Reset()
    {
        if (Options is not { } o) return;
        DiscardDrafts();
        Apply(() =>
        {
            var defaults = new FlyleafSubtitleOptions();
            o.FontFamily = defaults.FontFamily; o.FontSize = defaults.FontSize; o.Bold = defaults.Bold; o.Italic = defaults.Italic;
            o.Color = defaults.Color; o.OutlineColor = defaults.OutlineColor; o.OutlineThickness = defaults.OutlineThickness; o.BottomOffset = defaults.BottomOffset; o.WithinVideo = defaults.WithinVideo;
        });
    }
}
public sealed class FlyleafOSDOptionsEditor : FlyleafTextOptionsEditor
{
    sealed record PositionChoice(FlyleafOSDPosition Value, string Name) { public override string ToString() => Name; }
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(nameof(Options), typeof(FlyleafOSDOptions), typeof(FlyleafOSDOptionsEditor), new PropertyMetadata(null, OptionsChanged));
    public FlyleafOSDOptions? Options { get => (FlyleafOSDOptions?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public bool ShowResetButton { get; init; } = true;
    public event EventHandler? PreviewRequested;
    static void OptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (FlyleafOSDOptionsEditor)d;
        if (e.OldValue is FlyleafOSDOptions old) PropertyChangedEventManager.RemoveHandler(old, editor.Changed, "");
        if (e.NewValue is FlyleafOSDOptions current) PropertyChangedEventManager.AddHandler(current, editor.Changed, "");
        editor.Build();
    }
    void Build()
    {
        Begin(); if (Options is not { } o) return;
        Toggle("Show action messages", () => o.Enabled, v => o.Enabled = v);
        Toggle("Show playback status", () => o.ShowStatus, v => o.ShowStatus = v);
        Toggle("Show error messages", () => o.ShowErrors, v => o.ShowErrors = v);
        Choice("Time display", Enum.GetValues<FlyleafVisibilityPolicy>(), () => o.TimeDisplay, v => o.TimeDisplay = v);
        Children.Add(Text("Auto shows playback time during mouse or keyboard activity and seeking while the playback bar is hidden. Static images have no playback clock. Recording keeps a separate REC indicator.", true));
        Toggle("Show milliseconds", () => o.ShowMilliseconds, v => o.ShowMilliseconds = v);
        Choice("Queue display", Enum.GetValues<FlyleafVisibilityPolicy>(), () => o.QueueDisplay, v => o.QueueDisplay = v);
        Children.Add(Text("Auto shows the queue position and title during activity and temporarily when changing items. Always keeps them visible; Never hides them. Only queues with more than one item are shown.", true));
        Number("Duration · s", () => o.DurationSeconds, v => o.DurationSeconds = v);
        Children.Add(Text("Duration applies to timed action and status messages and automatic queue feedback, not ongoing playback operations or errors.", true));
        Number("Text size", () => o.FontSize, v => o.FontSize = v);
        var positions = Enum.GetValues<FlyleafOSDPosition>().Select(p => new PositionChoice(p, FlyleafPluginOptionDescriptions.Label(p.ToString()))).ToArray();
        Choice("Messages", positions, () => positions.Single(p => p.Value == o.Position), v => o.Position = v.Value);
        Choice("Clock / queue position", positions, () => positions.Single(p => p.Value == o.ClockPosition), v => o.ClockPosition = v.Value);
        Children.Add(Text("The Messages position applies to status and action messages, which are displayed independently. Queue information, clock and recording share a corner. Errors always appear in the center; transient errors time out after 7 seconds or clear on recovery. Blocking errors remain until recovery or dismissal.", true));
        var preview = Button("Preview message"); preview.HorizontalAlignment = HorizontalAlignment.Left; preview.Margin = new Thickness(0, 8, 0, 0);
        RefreshWith(() => preview.IsEnabled = o.Enabled);
        preview.Click += (_, _) => PreviewRequested?.Invoke(this, EventArgs.Empty); Children.Add(preview);
        if (ShowResetButton)
        {
            var reset = ResetButton("Reset messages");
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Margin = new Thickness(0, 8, 0, 0);
            reset.Click += (_, _) => Reset();
            Children.Add(reset);
        }
        Finish();
    }
    public void Reset()
    {
        if (Options is not { } o) return;
        DiscardDrafts();
        Apply(() =>
        {
            var defaults = new FlyleafOSDOptions();
            o.Enabled = defaults.Enabled;
            o.DurationSeconds = defaults.DurationSeconds;
            o.FontSize = defaults.FontSize;
            o.Position = defaults.Position;
            o.ClockPosition = defaults.ClockPosition;
            o.ShowStatus = defaults.ShowStatus;
            o.ShowErrors = defaults.ShowErrors;
            o.TimeDisplay = defaults.TimeDisplay;
            o.ShowMilliseconds = defaults.ShowMilliseconds;
            o.QueueDisplay = defaults.QueueDisplay;
        });
    }
}
