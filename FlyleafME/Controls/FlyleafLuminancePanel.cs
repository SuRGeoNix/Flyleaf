using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>Automatic, resolved and actually applied display luminance with optional overrides.</summary>
internal sealed class FlyleafLuminancePanel : StackPanel
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafLuminancePanel),
        new PropertyMetadata(null, (d, _) => { if (((FlyleafLuminancePanel)d).IsLoaded) ((FlyleafLuminancePanel)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    readonly Grid fields = new();
    readonly TextBlock automatic = Text("", true), resolved = Text("", true), applied = Text("", true), error = ValidationMessage();
    readonly List<Field> inputs = [];
    internal Button ResetAction { get; } = ResetButton("Reset luminance to automatic");
    FlyleafPanelContext? observed;
    Player? player;
    Renderer? renderer;
    bool refreshing, queued;

    public FlyleafLuminancePanel()
    {
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(36) }); fields.ColumnDefinitions.Add(new()); fields.ColumnDefinitions.Add(new());
        fields.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var black = Text("Black · nits", true); var peak = Text("Peak · nits", true);
        Grid.SetColumn(black, 1); Grid.SetColumn(peak, 2); fields.Children.Add(black); fields.Children.Add(peak);
        for (int row = 1; row <= 2; row++)
        {
            bool hdr = row == 2;
            fields.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var label = Text(hdr ? "HDR" : "SDR"); label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(label, row); fields.Children.Add(label);
            for (int column = 1; column <= 2; column++)
            {
                bool minimum = column == 1;
                var field = new Field(hdr, minimum); inputs.Add(field);
                Grid.SetRow(field, row); Grid.SetColumn(field, column); fields.Children.Add(field);
                field.Auto.Click += (_, _) =>
                {
                    if (refreshing || renderer?.GetDisplayLuminance() is not { } snapshot) return;
                    var target = hdr ? snapshot.HDR : snapshot.SDR;
                    field.Dirty = false;
                    Set(field, field.Auto.IsChecked == true ? -1 : minimum ? target.Minimum : target.Peak);
                };
                field.Input.LostKeyboardFocus += (_, _) => Commit(field);
                field.Input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(field); e.Handled = true; } };
            }
        }
        Children.Add(fields); Children.Add(automatic); Children.Add(resolved); Children.Add(applied); Children.Add(error);
        ResetAction.IsEnabled = false;
        ResetAction.Click += (_, _) =>
        {
            if (player is not { IsDisposed: false } target) return;
            foreach (var field in inputs) field.Dirty = false;
            var c = target.Config.Video;
            c.TargetSDRMinNits = c.TargetSDRPeakNits = c.TargetHDRMinNits = c.TargetHDRPeakNits = -1;
            error.Text = ""; Refresh();
        };
        ToolTip = "Auto fields show the resolved targets. Automatic black level scales with peak luminance; Applied shows the range currently used for display mapping.";
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach();
    }

    void Attach()
    {
        Detach(); observed = Context;
        if (observed != null) observed.PropertyChanged += Changed;
        player = Context?.Player;
        if (player != null) { player.PropertyChanged += Changed; player.Video.PropertyChanged += Changed; player.Config.Video.PropertyChanged += Changed; }
        Refresh();
    }
    void Detach()
    {
        if (observed != null) observed.PropertyChanged -= Changed;
        if (player != null) { player.PropertyChanged -= Changed; player.Video.PropertyChanged -= Changed; player.Config.Video.PropertyChanged -= Changed; }
        if (renderer != null) { renderer.PropertyChanged -= Changed; renderer.FiltersChanged -= FiltersChanged; }
        observed = null; player = null; renderer = null;
    }
    void FiltersChanged(object? sender, EventArgs e) => Queue();
    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is Video && e.PropertyName != nameof(Video.IsOpened)) return;
        if (sender is Player && e.PropertyName is not (nameof(Player.Renderer) or nameof(Player.IsDisposed))) return;
        Queue();
    }
    void Queue()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded || queued) return; queued = true;
            Dispatcher.BeginInvoke(new Action(() => { queued = false; if (IsLoaded) { if (Context?.Player != player) Attach(); else Refresh(); } }));
        }));
    }
    void Commit(Field field)
    {
        if (refreshing || !field.Dirty || field.Auto.IsChecked == true) return;
        if (!double.TryParse(field.Input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value) || value < (field.Minimum ? 0 : 0.1) || value > 100000)
        { error.Text = field.Minimum ? "Enter a black level from 0 to 100,000 nits." : "Enter a peak from 0.1 to 100,000 nits."; return; }
        Set(field, (float)value);
    }
    void Set(Field field, float value)
    {
        if (player is not { IsDisposed: false } target || renderer?.GetDisplayLuminance() is not { } snapshot) return;
        var c = target.Config.Video;
        var auto = field.HDR ? snapshot.AutomaticHDR : snapshot.AutomaticSDR;
        float min = field.Minimum ? value : field.HDR ? c.TargetHDRMinNits : c.TargetSDRMinNits;
        float peak = !field.Minimum ? value : field.HDR ? c.TargetHDRPeakNits : c.TargetSDRPeakNits;
        if (!FlyleafSettingsValues.ValidLuminanceRange(min, peak, auto.Peak))
        { error.Text = "Black must be below the effective peak, including when peak is automatic."; Refresh(); return; }
        try
        {
            if (field.HDR) { if (field.Minimum) c.TargetHDRMinNits = value; else c.TargetHDRPeakNits = value; }
            else { if (field.Minimum) c.TargetSDRMinNits = value; else c.TargetSDRPeakNits = value; }
            field.Dirty = false; error.Text = ""; Refresh();
        }
        catch (Exception ex) { error.Text = ex.Message; }
    }
    void Refresh()
    {
        var current = player is { IsDisposed: false } ? player.Renderer : null;
        if (current != renderer)
        {
            if (renderer != null) { renderer.PropertyChanged -= Changed; renderer.FiltersChanged -= FiltersChanged; }
            renderer = current;
            if (renderer != null) { renderer.PropertyChanged += Changed; renderer.FiltersChanged += FiltersChanged; }
        }
        var snapshot = player?.Video.IsOpened == true ? renderer?.GetDisplayLuminance() : null;
        fields.IsEnabled = ResetAction.IsEnabled = snapshot != null;
        if (snapshot is not { } s) { automatic.Text = "Open video to see automatic and effective display targets."; resolved.Text = applied.Text = ""; return; }
        refreshing = true;
        try
        {
            var c = player!.Config.Video;
            foreach (var field in inputs)
            {
                float configured = field.HDR ? field.Minimum ? c.TargetHDRMinNits : c.TargetHDRPeakNits : field.Minimum ? c.TargetSDRMinNits : c.TargetSDRPeakNits;
                var effective = field.HDR ? s.HDR : s.SDR;
                bool useAuto = !float.IsFinite(configured) || (field.Minimum ? configured < 0 : configured <= 0);
                field.Auto.IsChecked = useAuto;
                field.Input.IsReadOnly = useAuto;
                if (!field.Dirty && !field.Input.IsKeyboardFocusWithin)
                { field.Updating = true; field.Input.Text = (useAuto ? field.Minimum ? effective.Minimum : effective.Peak : configured).ToString("G7", CultureInfo.CurrentCulture); field.Updating = false; }
            }
            static string Range(DisplayLuminanceRange r) => $"{r.Minimum:G5}–{r.Peak:G6}";
            automatic.Text = $"Auto: SDR {Range(s.AutomaticSDR)} · HDR {Range(s.AutomaticHDR)} nits";
            resolved.Text = $"Targets: SDR {Range(s.SDR)} · HDR {Range(s.HDR)} nits";
            applied.Text = s.MappingActive ? $"Applied: {(s.HDROutput ? "HDR" : "SDR")} {Range(s.Applied)} nits" : "Flyleaf display mapping is not active for this processor/content.";
        }
        finally { refreshing = false; }
    }
    sealed class Field : StackPanel
    {
        internal readonly bool HDR, Minimum;
        internal readonly TextBox Input = new() { MinWidth = 55, MaxWidth = 84, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        internal readonly CheckBox Auto = new() { Content = "Auto", Margin = new Thickness(0, 4, 0, 0) };
        internal bool Dirty, Updating;
        internal Field(bool hdr, bool minimum)
        {
            HDR = hdr; Minimum = minimum; Margin = new Thickness(0, 6, 8, 6);
            Input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle"); Auto.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
            Input.TextChanged += (_, _) => { if (!Updating) Dirty = true; };
            Children.Add(Input); Children.Add(Auto);
        }
    }
}
