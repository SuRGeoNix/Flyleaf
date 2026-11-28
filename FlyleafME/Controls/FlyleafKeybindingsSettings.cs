using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FlyleafLib.MediaPlayer;
using PlayerKeyBinding = FlyleafLib.MediaPlayer.KeyBinding;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;
public sealed class FlyleafKeybindingsSettings : FlyleafSettingsEditor
{
    public IReadOnlyDictionary<string, FlyleafShortcutGesture> CustomDefaults { get; init; } = new Dictionary<string, FlyleafShortcutGesture>();
    sealed record ActionChoice(KeyBindingAction Action, string Label)
    {
        public override string ToString() => Label;
    }
    protected override NotifyPropertyChanged SelectConfig(Player target) => target.Config.Playback;
    protected override void Build()
    {
        BeginPage("Bindings");
        if (player is not { } target) return;
        BuildBindings(target.Config.Playback.KeyBindings, Context?.UIConfig?.Mouse);
    }
    internal void BuildBindings(KeysConfig keys, FlyleafMouseOptions? mouse)
    {
        var defaults = new KeysConfig(); defaults.LoadDefault();
        Children.Add(Text("Library, control and registered application actions. Focus a shortcut field and press the new combination. Tab moves focus; Escape cancels capture. Windows-key combinations are not supported by the player router.", true));
        var error = ValidationMessage(); Children.Add(error);
        var rows = new StackPanel(); Children.Add(rows);
        void Display()
        {
            var configuredDefaults = FlyleafSettingsReset.GetDefaults(this).Playback.KeyBindings;
            rows.Children.Clear();
            foreach (var section in keys.Keys.GroupBy(b => b.Action != KeyBindingAction.Custom ? "Library actions" : FlyleafControlKeybindings.Defaults.ContainsKey(b.ActionName ?? "") ? "Control actions · FlyleafME" : "Application actions").OrderBy(g => g.Key))
            {
                var heading = Text(section.Key); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 18, 0, 6); rows.Children.Add(heading);
                foreach (var binding in section.OrderBy(ActionLabel, StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
                    var label = Text(ActionLabel(binding)); label.Width = 210; label.ToolTip = binding.ActionName;
                    var field = CaptureField(gesture =>
                    {
                        if (gesture.Conflict(keys.Keys, binding) is { } other) return $"Already assigned to {ActionLabel(other)}.";
                        Run(() => gesture.Apply(binding)); return null;
                    }, () => FlyleafShortcutGesture.From(binding), error);
                    var release = new CheckBox { Content = "Release", ToolTip = "Run on key release instead of key press", IsChecked = binding.IsKeyUp, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
                    release.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
                    release.Click += (_, _) => Run(() => binding.IsKeyUp = release.IsChecked == true);
                    var clear = IconButton("\uE74D", "Unassign " + ActionLabel(binding)); clear.Margin = new Thickness(4, 0, 0, 0);
                    clear.Click += (_, _) => { Run(() => binding.Key = Key.None); Display(); };
                    var controls = new StackPanel { Orientation = Orientation.Horizontal };
                    controls.Children.Add(field); controls.Children.Add(release); controls.Children.Add(clear);
                    row.Children.Add(label); row.Children.Add(controls);
                    FlyleafShortcutGesture? Original(KeysConfig resetKeys)
                    {
                        var resetBindings = resetKeys.Keys ?? defaults.Keys;
                        if (binding.Action == KeyBindingAction.Custom)
                        {
                            var match = resetBindings.FirstOrDefault(b => b.Action == binding.Action && b.ActionName == binding.ActionName);
                            return match != null ? FlyleafShortcutGesture.From(match) : FlyleafControlKeybindings.Defaults.GetValueOrDefault(binding.ActionName ?? "") ?? CustomDefaults.GetValueOrDefault(binding.ActionName ?? "");
                        }
                        int index = keys.Keys.Where(b => b.Action == binding.Action).ToList().IndexOf(binding);
                        var baseline = resetBindings.Where(b => b.Action == binding.Action).ElementAtOrDefault(index)
                            ?? defaults.Keys.Where(b => b.Action == binding.Action).ElementAtOrDefault(index);
                        return baseline == null ? null : FlyleafShortcutGesture.From(baseline);
                    }
                    if (Original(configuredDefaults) != null)
                    {
                        var reset = ResetButton("Reset " + ActionLabel(binding)); reset.Margin = new Thickness(4, 0, 0, 0);
                        reset.Click += (_, _) =>
                        {
                            var original = Original(FlyleafSettingsReset.GetDefaults(this).Playback.KeyBindings);
                            if (original == null) return;
                            if (original.Conflict(keys.Keys, binding) is { } other) { error.Text = $"Default shortcut is assigned to {ActionLabel(other)}. Unassign it first."; return; }
                            Run(() => original.Apply(binding)); error.Text = ""; Display();
                        };
                        controls.Children.Add(reset);
                    }
                    rows.Children.Add(FlyleafSettingsSearch.Label(row, ActionLabel(binding)));
                }
            }
        }
        Display();
        var add = Group("Additional shortcut", null);
        add.Children.Add(Text("Add a missing library action or give an existing action another shortcut. Capture first, then press Add.", true));
        var available = Enum.GetValues<KeyBindingAction>().Where(a => a != KeyBindingAction.Custom &&
            !(a == KeyBindingAction.OpenPrevItem && keys.Exists("control.navigation.previous")) &&
            !(a == KeyBindingAction.OpenNextItem && keys.Exists("control.navigation.next")) &&
            !(a == KeyBindingAction.OpenFromFileDialog && keys.Exists("app.open")) &&
            !(a == KeyBindingAction.OpenFromClipboard && keys.Exists("app.paste"))).ToArray();
        var actionChoices = available.Select(action => new ActionChoice(action, FlyleafPluginOptionDescriptions.Label(action.ToString()))).ToArray();
        var selection = new ComboBox
        {
            ItemsSource = actionChoices, DisplayMemberPath = "Label", SelectedValuePath = "Action",
            SelectedValue = KeyBindingAction.TogglePlayPause, Width = 210,
            Margin = new Thickness(0, 0, 6, 4), ToolTip = "Library action"
        };
        selection.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
        FlyleafShortcutGesture? draft = null;
        var commit = IconButton("", "Add shortcut"); commit.IsEnabled = false; commit.Margin = new Thickness(4, 0, 0, 0);
        var shortcut = CaptureField(gesture =>
        {
            if (gesture.Conflict(keys.Keys) is { } other) return $"Already assigned to {ActionLabel(other)}.";
            draft = gesture; commit.IsEnabled = true; return null;
        }, () => draft, error);
        var capture = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        capture.Children.Add(shortcut); capture.Children.Add(commit);
        var addRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        addRow.Children.Add(selection); addRow.Children.Add(capture); add.Children.Add(addRow);
        commit.Click += (_, _) =>
        {
            if (draft is not { } gesture || selection.SelectedValue is not KeyBindingAction action) return;
            if (gesture.Conflict(keys.Keys) is { } other) { error.Text = $"Already assigned to {ActionLabel(other)}."; return; }
            Run(() =>
            {
                keys.Add(gesture.Key, action, gesture.Alt, gesture.Ctrl, gesture.Shift);
                draft = null; commit.IsEnabled = false; shortcut.Text = "Press shortcut…"; error.Text = ""; Display();
            });
        };
        if (mouse != null)
        {
            Observe(mouse);
            var section = Group("Mouse bindings", () =>
            {
                var baseline = new FlyleafMouseOptions();
                mouse.Bindings = baseline.Bindings; mouse.PanOnCtrlDrag = baseline.PanOnCtrlDrag;
                mouse.ZoomOnCtrlWheel = baseline.ZoomOnCtrlWheel; mouse.RotateOnShiftWheel = baseline.RotateOnShiftWheel;
                mouse.FullscreenOnDoubleClick = baseline.FullscreenOnDoubleClick;
            });
            var choices = new[] { (AvailableWindows.None, "Off"), (AvailableWindows.Surface, "Video surface"), (AvailableWindows.Overlay, "Overlay"), (AvailableWindows.Both, "Video + overlay") };
            Choice(section, "Core mouse gestures", () => mouse.Bindings, v => mouse.Bindings = v, choices);
            Choice(section, "Pan · Ctrl + left drag", () => mouse.PanOnCtrlDrag, v => mouse.PanOnCtrlDrag = v, choices);
            Choice(section, "Zoom · Ctrl + wheel", () => mouse.ZoomOnCtrlWheel, v => mouse.ZoomOnCtrlWheel = v, choices);
            Choice(section, "Rotate · Shift + wheel", () => mouse.RotateOnShiftWheel, v => mouse.RotateOnShiftWheel = v, choices);
            Choice(section, "Fullscreen · double-click", () => mouse.FullscreenOnDoubleClick, v => mouse.FullscreenOnDoubleClick = v, choices);
            section.Children.Add(Text("The core gesture switch gates pan, zoom, rotate and 360° mouse navigation. Fullscreen uses the composed control's double-click handling. Save UI/window defaults in Application to remember these choices.", true));
        }
        Refresh();
    }
    static string ActionLabel(PlayerKeyBinding binding) => binding.ActionName switch
    {
        "app.open" => "Open media",
        "app.paste" => "Paste media input",
        "app.newWindow" => "New window",
        "app.closeWindow" => "Close window",
        "control.subtitles.up" => "Subtitles · move up",
        "control.subtitles.down" => "Subtitles · move down",
        "control.subtitles.larger" => "Subtitles · increase text size",
        "control.subtitles.smaller" => "Subtitles · decrease text size",
        "control.navigation.previous" => "Previous item",
        "control.navigation.next" => "Next item",
        "control.navigation.previousPage" => "Previous items · large step",
        "control.navigation.nextPage" => "Next items · large step",
        "control.queue.autoAdvance" => "Auto-next",
        "control.playbackBar.pin" => "Pin / unpin playback bar",
        "control.queue.search" => "Search queue",
        null or "" => "Custom action",
        var name => FlyleafPluginOptionDescriptions.Label(name.StartsWith("app.", StringComparison.Ordinal) ? name[4..] : name).Replace('_', ' ').Replace('.', ' ')
    };
    static TextBox CaptureField(Func<FlyleafShortcutGesture, string?> assign, Func<FlyleafShortcutGesture?> get, TextBlock error)
    {
        var field = new TextBox { Width = 128, IsReadOnly = true, Tag = "FlyleafKeyCapture", Text = get()?.ToString() ?? "Press shortcut…", ToolTip = "Focus and press a shortcut; Tab moves focus, Escape cancels" };
        field.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        field.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) return;
            e.Handled = true;
            if (e.Key == Key.Escape) { field.Text = get()?.ToString() ?? "Press shortcut…"; field.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
            if (e.IsRepeat) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return;
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Windows) != 0) { error.Text = "Windows-key combinations are not supported."; return; }
            var gesture = new FlyleafShortcutGesture(key, modifiers.HasFlag(ModifierKeys.Alt), modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift), get()?.IsKeyUp ?? false);
            string? message = assign(gesture); error.Text = message ?? "";
            field.Text = get()?.ToString() ?? "Press shortcut…";
        };
        return field;
    }
}
