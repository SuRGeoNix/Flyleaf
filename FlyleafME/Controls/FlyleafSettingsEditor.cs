using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using InputType = FlyleafLib.InputType;
using FlyleafLib.MediaPlayer;
using Microsoft.Win32;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>Shared lifecycle and compact editors for current-player settings.</summary>
public abstract class FlyleafSettingsEditor : StackPanel
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafSettingsEditor),
        new PropertyMetadata(null, (d, _) => { if (((FlyleafSettingsEditor)d).IsLoaded) ((FlyleafSettingsEditor)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }

    readonly List<Action> refreshers = [];
    readonly Dictionary<StackPanel, List<Action>> discardDrafts = [];
    readonly List<INotifyPropertyChanged> additionalSources = [];
    FlyleafPanelContext? observedContext;
    FlyleafUIConfig? observedUI;
    FlyleafAppearanceOptions? observedAppearance;
    FlyleafPlaybackBarOptions? observedPlaybackBar;
    FlyleafMouseOptions? observedMouse;
    FlyleafQueueOptions? observedQueue;
    protected Player? player;
    protected NotifyPropertyChanged? config;
    TextBlock? feedback;
    ContextMenu? openMenu;
    bool refreshing, busy, refreshQueued;

    protected FlyleafSettingsEditor()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    void Attach()
    {
        Detach();
        observedContext = Context;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        player = Context?.Player;
        observedUI = Context?.UIConfig;
        observedAppearance = observedUI?.Appearance; observedPlaybackBar = observedUI?.PlaybackBar; observedMouse = observedUI?.Mouse; observedQueue = observedUI?.Queue;
        if (player != null)
        {
            player.Audio.PropertyChanged += StreamChanged;
            player.Subtitles.PropertyChanged += StreamChanged;
        }
        config = player == null ? null : SelectConfig(player);
        if (config != null) config.PropertyChanged += ConfigChanged;
        Build();
    }

    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        if (config != null) config.PropertyChanged -= ConfigChanged;
        foreach (var source in additionalSources) source.PropertyChanged -= ConfigChanged;
        additionalSources.Clear();
        if (player != null)
        {
            player.Audio.PropertyChanged -= StreamChanged;
            player.Subtitles.PropertyChanged -= StreamChanged;
        }
        if (openMenu != null) openMenu.IsOpen = false;
        openMenu = null;
        observedContext = null;
        observedUI = null;
        observedAppearance = null; observedPlaybackBar = null; observedMouse = null; observedQueue = null;
        player = null; config = null;
    }

    void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlyleafPanelContext.Player) or nameof(FlyleafPanelContext.MediaElement))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded) return;
                var target = Context?.Player;
                var ui = Context?.UIConfig;
                if (target != player || ui != observedUI || ui?.Appearance != observedAppearance || ui?.PlaybackBar != observedPlaybackBar || ui?.Mouse != observedMouse || ui?.Queue != observedQueue ||
                    target != null && !ReferenceEquals(config, SelectConfig(target))) Attach();
            }));
    }

    void ConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Notifications can originate on a playback/settings worker. Touch visuals only here.
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded || refreshQueued) return;
            refreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { refreshQueued = false; if (IsLoaded && !busy) Refresh(); }));
        }));
    }

    void StreamChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsOpened") ConfigChanged(sender, e);
    }

    protected abstract NotifyPropertyChanged SelectConfig(Player target);
    protected abstract void Build();

    protected void Observe(INotifyPropertyChanged source)
    {
        source.PropertyChanged += ConfigChanged;
        additionalSources.Add(source);
    }

    protected void RefreshWith(Action refresh) => refreshers.Add(refresh);

    protected void BeginPage(string title, string? scope = null)
    {
        Children.Clear(); refreshers.Clear(); discardDrafts.Clear();
        var heading = Text(title); heading.FontSize = 20; heading.FontWeight = FontWeights.SemiBold;
        Children.Add(heading);
        if (!string.IsNullOrWhiteSpace(scope)) Children.Add(Text(scope, true));
        feedback = ValidationMessage(); Children.Add(feedback);
        IsEnabled = player is { IsDisposed: false } && !busy;
        if (config == null) feedback.Text = "No player is attached.";
    }

    protected StackPanel Group(string title, Action? reset, StackPanel? parent = null, Func<bool>? canReset = null, bool background = false)
    {
        var body = new StackPanel();
        Grid.SetIsSharedSizeScope(body, true);
        discardDrafts[body] = [];
        Button? button = null;
        if (reset != null)
        {
            button = ResetButton($"Reset {title.ToLowerInvariant()} to defaults");
            button.Click += (_, _) =>
            {
                if (canReset?.Invoke() == false) return;
                foreach (var discard in discardDrafts[body]) discard();
                Run(reset, background);
            };
            if (canReset != null) refreshers.Add(() => button.IsEnabled = canReset());
        }
        body.Children.Add(SectionHeading(title, button));
        (parent ?? this).Children.Add(body);
        return body;
    }

    /// <summary>Inline label/editor at normal widths; stacked at narrow widths with the same row spacing.</summary>
    internal static Grid SettingRow(string title, FrameworkElement editor)
    {
        var row = new Grid { Margin = new Thickness(0, 7, 0, 7) };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto, SharedSizeGroup = "SettingLabels" });
        row.ColumnDefinitions.Add(new());
        row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = Text(title, true); label.Margin = new Thickness(0, 0, 16, 0); label.MaxWidth = 210; label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label); Grid.SetColumn(editor, 1); row.Children.Add(editor);
        void ArrangeRow()
        {
            bool inline = row.ActualWidth >= 480;
            row.ColumnDefinitions[0].SharedSizeGroup = inline ? "SettingLabels" : null;
            row.ColumnDefinitions[0].Width = inline ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            Grid.SetColumnSpan(label, inline ? 1 : 2);
            label.MaxWidth = inline ? 210 : double.PositiveInfinity;
            label.Margin = inline ? new Thickness(0, 0, 16, 0) : new Thickness(0, 0, 0, 6);
            Grid.SetRow(editor, inline ? 0 : 1); Grid.SetColumn(editor, inline ? 1 : 0); Grid.SetColumnSpan(editor, inline ? 1 : 2);
        }
        row.SizeChanged += (_, _) => ArrangeRow(); row.Loaded += (_, _) => ArrangeRow();
        return row;
    }

    protected void Toggle(StackPanel parent, string title, string help, Func<bool> get, Action<bool> set, bool background = false)
    {
        var check = new CheckBox { Content = title, Margin = new Thickness(0, 10, 0, 0), ToolTip = help };
        check.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        parent.Children.Add(check);
        if (help.Length > 0) { var description = Text(help, true); description.Margin = new Thickness(25, 3, 0, 6); parent.Children.Add(description); }
        check.Click += (_, _) => { if (!refreshing) { bool value = check.IsChecked == true; Run(() => set(value), background); } };
        refreshers.Add(() => check.IsChecked = get());
    }

    protected void Duration(StackPanel parent, string title, string units, Func<long> get, Action<long> set,
        bool positive = false, Func<long, string?>? validate = null)
        => Number(parent, title, units, () => get() / (double)TimeSpan.TicksPerSecond,
            value => set((long)Math.Round(value * TimeSpan.TicksPerSecond)), positive ? 0.001 : 0, 86400,
            value => validate?.Invoke((long)Math.Round(value * TimeSpan.TicksPerSecond)));

    protected void Number(StackPanel parent, string title, string units, Func<double> get, Action<double> set,
        double min, double max, Func<double, string?>? validate = null, Func<bool>? canEdit = null, bool background = false)
    {
        var input = new TextBox { Width = 76, HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
        input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var suffix = Text(units, true); suffix.Margin = new Thickness(10, 0, 0, 0); suffix.MaxWidth = 150; suffix.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(input); row.Children.Add(suffix);
        var error = ValidationMessage(); error.Visibility = Visibility.Collapsed;
        parent.Children.Add(SettingRow(title, row)); parent.Children.Add(error);
        bool dirty = false;
        discardDrafts[parent].Add(() => { dirty = false; error.Visibility = Visibility.Collapsed; });
        input.TextChanged += (_, _) => { if (!refreshing) dirty = true; };
        void Commit()
        {
            if (!dirty || busy || canEdit?.Invoke() == false) return;
            string? message = null;
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
                !double.IsFinite(value) || value < min || value > max)
                message = $"Enter a number from {min.ToString(CultureInfo.CurrentCulture)} to {max.ToString(CultureInfo.CurrentCulture)}.";
            else message = validate?.Invoke(value);
            error.Text = message ?? ""; error.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            if (message != null) return;
            dirty = false; Run(() => set(value), background);
        }
        input.LostKeyboardFocus += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        refreshers.Add(() =>
        {
            input.IsEnabled = canEdit?.Invoke() != false;
            if (!dirty && !input.IsKeyboardFocusWithin) input.Text = get().ToString("0.###", CultureInfo.CurrentCulture);
        });
    }

    protected void Integer(StackPanel parent, string title, string units, Func<int> get, Action<int> set,
        int min, int max, Func<bool>? canEdit = null, bool background = false)
        => Number(parent, title, units, () => get(), v => set((int)v), min, max,
            v => v != Math.Truncate(v) ? "Enter a whole number." : null, canEdit, background);

    protected void DelayStep(StackPanel parent, string title, Func<long> get, Action<long> set)
        => Number(parent, title, "ms", () => get() / (double)TimeSpan.TicksPerMillisecond,
            v => set((long)Math.Round(v * TimeSpan.TicksPerMillisecond)), 1, 60000);

    protected StackPanel Advanced()
    {
        var body = new StackPanel();
        var expander = new Expander { Header = Text("Advanced"), Content = body, Margin = new Thickness(0, 22, 0, 0) };
        expander.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush");
        Children.Add(expander);
        return body;
    }

    protected TextBox TextEntry(StackPanel parent, string title, Func<string> get, Action<string> set,
        Func<string, string?>? validate = null, bool trim = true, Button? browseButton = null)
    {
        var input = new TextBox { MinWidth = 160, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left };
        input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        var error = ValidationMessage(); error.Visibility = Visibility.Collapsed;
        input.VerticalContentAlignment = VerticalAlignment.Center;
        FrameworkElement editor = input;
        if (browseButton != null)
        {
            var path = new DockPanel(); DockPanel.SetDock(browseButton, Dock.Right);
            input.HorizontalAlignment = HorizontalAlignment.Stretch; input.MinWidth = 0; input.MaxWidth = double.PositiveInfinity;
            path.Children.Add(browseButton); path.Children.Add(input); editor = path;
        }
        parent.Children.Add(SettingRow(title, editor)); parent.Children.Add(error);
        bool dirty = false;
        discardDrafts[parent].Add(() => { dirty = false; error.Visibility = Visibility.Collapsed; });
        input.TextChanged += (_, _) => { if (!refreshing) dirty = true; };
        void Commit()
        {
            if (!dirty || busy) return;
            var value = trim ? input.Text.Trim() : input.Text;
            var message = validate?.Invoke(value);
            error.Text = message ?? ""; error.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            if (message != null) return;
            dirty = false; Run(() => set(value));
        }
        input.LostKeyboardFocus += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        refreshers.Add(() => { if (!dirty && !input.IsKeyboardFocusWithin) input.Text = get(); });
        return input;
    }

    protected void FilePath(StackPanel parent, string title, Func<string> get, Action<string> set)
    {
        var browse = Button("Browse…"); browse.HorizontalAlignment = HorizontalAlignment.Left;
        browse.Margin = new Thickness(8, 0, 0, 0);
        var input = TextEntry(parent, title, get, set, browseButton: browse);
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = title, CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            input.Text = dialog.FileName; Run(() => set(dialog.FileName));
        };
    }

    protected void Languages(StackPanel parent, Func<List<Language>> get, Action<List<Language>> set)
    {
        var rows = new StackPanel();
        var languageEntry = new FlyleafLanguageEntry(() => get());
        var input = languageEntry.Input;
        var add = Button("Add language"); add.Margin = new Thickness(8, 0, 0, 0);
        var entry = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; entry.Children.Add(languageEntry); entry.Children.Add(add);
        var error = ValidationMessage();
        parent.Children.Add(rows); parent.Children.Add(entry); parent.Children.Add(error);
        string? signature = null;
        refreshers.Add(() =>
        {
            var languages = get();
            string current = string.Join("|", languages.Select(l => l.CultureName ?? l.OriginalInput ?? "und"));
            if (signature == current) return;
            signature = current; rows.Children.Clear();
            for (int index = 0; index < languages.Count; index++)
            {
                int position = index;
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var up = Button("↑"); up.ToolTip = "Higher priority"; up.IsEnabled = index > 0;
                var down = Button("↓"); down.ToolTip = "Lower priority"; down.IsEnabled = index < languages.Count - 1;
                var remove = Button("×"); remove.ToolTip = "Remove language"; remove.IsEnabled = languages.Count > 1;
                foreach (var b in new[] { up, down, remove }) { b.Margin = new Thickness(4, 0, 0, 0); actions.Children.Add(b); }
                void Change(int move, bool delete = false)
                {
                    var copy = get().ToList();
                    if (position >= copy.Count || copy[position] != languages[position]) return;
                    if (delete) { if (copy.Count <= 1) return; copy.RemoveAt(position); }
                    else { int to = position + move; if (to < 0 || to >= copy.Count) return; (copy[position], copy[to]) = (copy[to], copy[position]); }
                    Run(() => set(copy));
                }
                up.Click += (_, _) => Change(-1); down.Click += (_, _) => Change(1); remove.Click += (_, _) => Change(0, true);
                DockPanel.SetDock(actions, Dock.Right); row.Children.Add(actions);
                var caption = Text($"{index + 1}. {languages[index]} ({languages[index].CultureName ?? languages[index].OriginalInput ?? "und"})");
                caption.Margin = new Thickness(0); caption.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(caption);
                rows.Children.Add(row);
            }
        });
        void Add()
        {
            string value = input.Text.Trim();
            if (value.Length == 0) { error.Text = "Enter a language code or name."; return; }
            var language = FlyleafLib.Language.Get(value.Equals("und", StringComparison.OrdinalIgnoreCase) ? "und" : value);
            if (language.Culture == null && !value.Equals("und", StringComparison.OrdinalIgnoreCase))
            { error.Text = "Unknown language. Use a language code such as en or ell, a name, or und."; return; }
            var copy = get().ToList();
            if (copy.Contains(language)) { error.Text = "That language is already in the list."; return; }
            copy.Add(language); Run(() => set(copy)); input.Clear(); error.Text = "";
        }
        add.Click += (_, _) => Add();
        languageEntry.Confirmed += (_, _) => Add();
        discardDrafts[parent].Add(() => { input.Clear(); error.Text = ""; signature = null; });
    }

    protected void InputTypes(StackPanel parent, string title, Func<List<InputType>> get, Action<List<InputType>> set)
    {
        parent.Children.Add(Text(title));
        var row = new WrapPanel(); parent.Children.Add(row);
        var all = new CheckBox { Content = "All types", Margin = new Thickness(0, 6, 12, 0) };
        all.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle"); row.Children.Add(all);
        all.Click += (_, _) => Run(() => set(all.IsChecked == true ? [] : Enum.GetValues<InputType>().ToList()));
        refreshers.Add(() => all.IsChecked = get() == null || get().Count == 0);
        foreach (var type in Enum.GetValues<InputType>())
        {
            var check = new CheckBox { Content = type.ToString(), Margin = new Thickness(0, 6, 12, 0) };
            check.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle"); row.Children.Add(check);
            check.Click += (_, _) =>
            {
                var values = get()?.ToList() ?? [];
                if (values.Count == 0) values = Enum.GetValues<InputType>().ToList();
                if (check.IsChecked == true) { if (!values.Contains(type)) values.Add(type); }
                else values.Remove(type);
                // Empty has the core meaning All; never silently turn off the last restriction.
                if (values.Count == 0) { Refresh(); return; }
                Run(() => set(values));
            };
            refreshers.Add(() => check.IsChecked = get() == null || get().Count == 0 || get().Contains(type));
        }
    }

    protected void Options(StackPanel parent, Func<Dictionary<string, string>> get, Action<Dictionary<string, string>> set,
        string title = "Decoder options · name / value")
    {
        var rows = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        rows.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
        rows.ColumnDefinitions.Add(new() { Width = new GridLength(3, GridUnitType.Star) });
        rows.ColumnDefinitions.Add(new() { Width = new GridLength(32) });
        var add = Button("Add option"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 8, 0, 0);
        var error = ValidationMessage();
        parent.Children.Add(Text(title));
        parent.Children.Add(rows); parent.Children.Add(add); parent.Children.Add(error);
        var entries = new List<(TextBox Key, TextBox Value, Button Remove)>();
        bool dirty = false, building = false;
        Dictionary<string, string>? shown = null;
        void Header()
        {
            rows.Children.Clear(); rows.RowDefinitions.Clear(); rows.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var key = Text("Name", true); var value = Text("Value", true);
            key.Margin = value.Margin = new Thickness(0, 0, 8, 4); Grid.SetColumn(value, 1);
            rows.Children.Add(key); rows.Children.Add(value);
        }
        Header();
        void Commit()
        {
            if (!dirty || building || busy) return;
            if (!FlyleafSettingsValues.TryOptions(entries.Select(e => new KeyValuePair<string, string>(e.Key.Text, e.Value.Text)), out var result, out var message))
            { error.Text = message; return; }
            error.Text = ""; dirty = false; shown = new(result); Run(() => set(result));
        }
        void AddRow(string key, string value)
        {
            var name = new TextBox { Text = key, ToolTip = "Option name", MinWidth = 40, Margin = new Thickness(0, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
            var content = new TextBox { Text = value, ToolTip = "Option value", MinWidth = 40, Margin = new Thickness(0, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
            name.SetResourceReference(StyleProperty, "FlyleafME.InputStyle"); content.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
            var remove = Button("×"); remove.ToolTip = "Remove option"; remove.Margin = new Thickness(0, 4, 0, 4);
            remove.MinHeight = 30; remove.Padding = new Thickness(4); remove.VerticalAlignment = VerticalAlignment.Stretch;
            rows.RowDefinitions.Add(new() { Height = GridLength.Auto });
            int position = entries.Count + 1;
            Grid.SetRow(name, position); Grid.SetRow(content, position); Grid.SetRow(remove, position);
            Grid.SetColumn(content, 1); Grid.SetColumn(remove, 2);
            rows.Children.Add(name); rows.Children.Add(content); rows.Children.Add(remove);
            entries.Add((name, content, remove));
            foreach (var box in new[] { name, content })
            {
                box.TextChanged += (_, _) => { if (!building) dirty = true; };
                box.LostKeyboardFocus += (_, _) => Commit();
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            }
            remove.Click += (_, _) =>
            {
                int index = entries.FindIndex(e => e.Key == name);
                entries.RemoveAt(index); rows.Children.Remove(name); rows.Children.Remove(content); rows.Children.Remove(remove);
                rows.RowDefinitions.RemoveAt(index + 1);
                for (int i = index; i < entries.Count; i++)
                { Grid.SetRow(entries[i].Key, i + 1); Grid.SetRow(entries[i].Value, i + 1); Grid.SetRow(entries[i].Remove, i + 1); }
                dirty = true; Commit();
            };
        }
        add.Click += (_, _) => { AddRow("", ""); dirty = true; entries[^1].Key.Focus(); };
        refreshers.Add(() =>
        {
            var current = get() ?? [];
            if (dirty || rows.IsKeyboardFocusWithin || shown != null && shown.Count == current.Count && shown.All(p => current.TryGetValue(p.Key, out var value) && value == p.Value)) return;
            building = true;
            try { Header(); entries.Clear(); foreach (var pair in current) AddRow(pair.Key, pair.Value); shown = new(current); }
            finally { building = false; }
        });
        discardDrafts[parent].Add(() => { dirty = false; shown = null; error.Text = ""; });
    }

    protected void Choice<T>(StackPanel parent, string title, Func<T> get, Action<T> set, (T Value, string Label)[] choices)
        => Choice(parent, title, get, set, choices, null);

    protected void Choice<T>(StackPanel parent, string title, Func<T> get, Action<T> set, (T Value, string Label)[] choices, Button? action)
    {
        var button = Button(""); button.HorizontalAlignment = HorizontalAlignment.Left;
        button.MinWidth = 150;
        FrameworkElement editor = button;
        if (action != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(button);
            row.Children.Add(action);
            editor = row;
        }
        parent.Children.Add(SettingRow(title, editor));
        refreshers.Add(() => button.Content = (choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, get())).Label ?? get()?.ToString() ?? "") + " ▾");
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
            foreach (var choice in choices)
            {
                var item = new MenuItem { Header = choice.Label, IsCheckable = true, IsChecked = EqualityComparer<T>.Default.Equals(choice.Value, get()) };
                item.Click += (_, _) => Run(() => set(choice.Value)); menu.Items.Add(item);
            }
            openMenu = menu; menu.IsOpen = true;
        };
    }

    protected void Flags<T>(StackPanel parent, string title, Func<T> get, Action<T> set) where T : struct, Enum
    {
        var body = new WrapPanel(); parent.Children.Add(Text(title)); parent.Children.Add(body);
        foreach (var value in Enum.GetValues<T>())
        {
            ulong mask = Convert.ToUInt64(value);
            if (mask == 0 || (mask & (mask - 1)) != 0) continue;
            var check = new CheckBox { Content = value.ToString(), Margin = new Thickness(0, 6, 14, 0) };
            check.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle"); body.Children.Add(check);
            check.Click += (_, _) =>
            {
                ulong flags = Convert.ToUInt64(get());
                flags = check.IsChecked == true ? flags | mask : flags & ~mask;
                Run(() => set((T)Enum.ToObject(typeof(T), flags)));
            };
            refreshers.Add(() => check.IsChecked = (Convert.ToUInt64(get()) & mask) != 0);
        }
    }

    protected void Folder(StackPanel parent, string title, Func<string> get, Action<string> set, bool allowRelative = false)
    {
        var input = new TextBox(); input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        var browse = Button("Browse…"); browse.Margin = new Thickness(8, 0, 0, 0);
        var row = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(input);
        var error = ValidationMessage(); error.Visibility = Visibility.Collapsed;
        input.VerticalContentAlignment = VerticalAlignment.Center;
        parent.Children.Add(SettingRow(title, row)); parent.Children.Add(error);
        bool dirty = false;
        discardDrafts[parent].Add(() => { dirty = false; error.Visibility = Visibility.Collapsed; });
        input.TextChanged += (_, _) => { if (!refreshing) dirty = true; };
        void Commit()
        {
            if (!dirty || busy) return;
            string value = input.Text.Trim();
            try
            {
                if (!allowRelative && !Path.IsPathFullyQualified(value)) throw new ArgumentException("Enter an absolute folder path.");
                if (value.Length > 0) _ = Path.GetFullPath(value);
                if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || File.Exists(value)) throw new ArgumentException("Enter a valid folder path.");
                error.Visibility = Visibility.Collapsed; dirty = false; Run(() => set(value));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
        }
        input.LostKeyboardFocus += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = title };
            if (Directory.Exists(get())) dialog.InitialDirectory = get();
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            input.Text = dialog.FolderName; Commit();
        };
        refreshers.Add(() => { if (!dirty && !input.IsKeyboardFocusWithin) input.Text = get(); });
    }

    protected async void Run(Action action, bool background = false)
    {
        // Capture UI-owned context on the dispatcher, never read its dependency properties on the worker.
        var target = player;
        if (busy || target == null || target.IsDisposed || !IsLoaded || Context?.Player != target) return;
        var owner = Context?.MediaElement;
        busy = true;
        if (background) IsEnabled = false;
        try
        {
            if (background) await Task.Run(() => { if (!target.IsDisposed) action(); });
            else action();
            if (feedback != null) feedback.Text = "";
            owner?.NotifySettingsEdited(target);
        }
        catch (Exception ex) { if (feedback != null) feedback.Text = ex.Message; }
        finally
        {
            busy = false;
            if (IsLoaded) { IsEnabled = player is { IsDisposed: false }; Refresh(); }
        }
    }

    protected void Refresh()
    {
        if (config == null) return;
        refreshing = true;
        try { foreach (var refresh in refreshers) refresh(); }
        finally { refreshing = false; }
    }
}
