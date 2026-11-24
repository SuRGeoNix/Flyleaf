using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;
using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace Flyleaf.Controls.WPF;

/// <summary>Tracks, framing, processing, picture filters and panoramic video controls.</summary>
public sealed class FlyleafVideoPanel : StackPanel
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafVideoPanel),
        new PropertyMetadata(null, (d, _) => { if (((FlyleafVideoPanel)d).IsLoaded) ((FlyleafVideoPanel)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }

    readonly CheckBox enabled = Check("Enable video");
    readonly CheckBox hflip = Check("Flip horizontally");
    readonly CheckBox vflip = Check("Flip vertically");
    readonly CheckBox panorama = Check("Enable 360° projection");
    readonly Button capture = Button("Choose capture device  ▾");
    readonly Button track = Button("Choose video track  ▾");
    readonly TextBlock trackCaption = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Button sizeMode = Button("Fit to window  ▾");
    readonly Button aspect = Button("Keep  ▾");
    readonly Button rotation = Button("0°  ▾");
    readonly Button processor = Button("Auto  ▾");
    readonly Button deinterlace = Button("Auto  ▾");
    readonly Button split = Button("None  ▾");
    readonly TextBlock details = Text("Open a video to choose its track.", true);
    readonly TextBlock feedback = Text("", true);
    readonly TextBlock effective = Text("No active processor", true);
    readonly TextBox customRatio = Input("16:9");
    readonly StackPanel framing = new();
    readonly StackPanel processing = new();
    readonly StackPanel panoFields = new();
    readonly StackPanel cropFields = new();
    readonly List<NumberField> numbers = [];
    readonly TextBox cropLeft = Input("0"), cropTop = Input("0"), cropRight = Input("0"), cropBottom = Input("0");
    readonly FlyleafVideoFiltersPanel filters = new() { ShowProcessorSelection = false };
    FlyleafPanelContext? observedContext;
    Player? player;
    Renderer? renderer;
    ContextMenu? openMenu;
    CropRect? displayedCrop;
    bool refreshing, refreshQueued;
    const double EditorHeight = 32;
    const double CompactFieldWidth = 76;
    const double StepWidth = 42;

    public FlyleafVideoPanel()
    {
        enabled.Content = "Video";
        track.Content = trackCaption;
        var header = new DockPanel(); enabled.Margin = new Thickness(0, 0, 12, 0); enabled.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(enabled, Dock.Left); header.Children.Add(enabled); header.Children.Add(track);
        Children.Add(header); Children.Add(details); Children.Add(feedback);
        Children.Add(Section("Capture input", capture));
        var picture = new StackPanel();
        var panoBody = new StackPanel();
        var views = new[] { (Title: "Picture", Body: picture), (Title: "Framing", Body: framing), (Title: "360°", Body: panoBody) };
        var navigation = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        var buttons = new List<Button>();
        void Select(int index)
        {
            for (int i = 0; i < views.Length; i++)
            {
                views[i].Body.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
                buttons[i].SetResourceReference(Control.BackgroundProperty, i == index ? "FlyleafME.SelectionBrush" : "FlyleafME.RaisedSurfaceBrush");
                buttons[i].SetResourceReference(Control.BorderBrushProperty, i == index ? "FlyleafME.AccentBrush" : "FlyleafME.BorderBrush");
            }
        }
        for (int i = 0; i < views.Length; i++)
        {
            int index = i; var button = Button(views[i].Title); button.Margin = new Thickness(0, 0, 5, 4); button.Padding = new Thickness(10, 6, 10, 6);
            button.Click += (_, _) => Select(index); buttons.Add(button); navigation.Children.Add(button);
        }
        Children.Add(navigation); foreach (var view in views) Children.Add(view.Body); Select(0);

        picture.Children.Add(EditorRow("Processor", processor)); picture.Children.Add(effective);
        filters.SetBinding(FlyleafVideoFiltersPanel.ContextProperty, new Binding(nameof(Context)) { Source = this });
        filters.Margin = new Thickness(0, 10, 0, 0); picture.Children.Add(filters);
        var luminance = new FlyleafLuminancePanel();
        luminance.SetBinding(FlyleafLuminancePanel.ContextProperty, new Binding(nameof(Context)) { Source = this });
        picture.Children.Add(Disclosure("Display luminance", luminance, luminance.ResetAction));
        processing.Children.Add(EditorRow("Deinterlace", deinterlace));
        deinterlace.ToolTip = "D3D11 processing; automatic processor selection may switch to satisfy this mode.";
        processing.Children.Add(EditorRow("Split alpha", split));
        picture.Children.Add(Disclosure("Deinterlacing & alpha", processing));

        var resetFraming = RendererAction(ResetButton("Reset framing to defaults"), r =>
        {
            DiscardNumberDrafts(framing);
            displayedCrop = null;
            r.ResetViewport();
        });
        resetFraming.ToolTip = "Reset size, aspect, crop, pan, rotation, mirrors and zoom.";
        framing.Children.Add(SectionHeading("Framing", resetFraming, topMargin: 4));
        framing.Children.Add(EditorRow("Size", sizeMode));
        framing.Children.Add(EditorRow("Aspect", aspect));
        customRatio.Width = 100;
        customRatio.HorizontalAlignment = HorizontalAlignment.Left;
        var customRow = EditorRow("Custom ratio", customRatio);
        framing.Children.Add(customRow);
        customRatio.Tag = customRow;
        var zoom = AddNumber(framing, "Zoom (%)", r => r.Zoom, (r, v) => r.Zoom = v, v => v >= 1 && v <= 10000, "Enter a zoom percentage between 1 and 10,000.");
        zoom.Tools(Step("−", "Zoom out", r => r.Zoom = Math.Max(1, r.Zoom - (player?.Config.Video.ZoomOffset ?? 10))),
            Step("+", "Zoom in", r => r.ZoomIn()));
        var panX = AddNumber(framing, "Pan X", r => r.PanXOffset, (r, v) => r.PanXOffset = v, v => v >= -10 && v <= 10, "Pan must be between −10 and 10.");
        var panY = AddNumber(framing, "Pan Y", r => r.PanYOffset, (r, v) => r.PanYOffset = v, v => v >= -10 && v <= 10, "Pan must be between −10 and 10.");
        var recenter = RendererAction(Button("Recenter"), r =>
        {
            panX.Dirty = panY.Dirty = false;
            r.PanXOffset = r.PanYOffset = 0;
            r.ZoomCenter = new Point(0.5, 0.5);
        });
        recenter.ToolTip = "Center the picture at the current zoom.";
        framing.Children.Add(EditorRow("", Inline(recenter)));

        rotation.Width = CompactFieldWidth;
        rotation.Height = rotation.MinHeight = EditorHeight;
        rotation.Padding = new Thickness(7, 4, 7, 4);
        framing.Children.Add(EditorRow("Rotation", Inline(rotation,
            Step("−90°", "Rotate left 90°", r => r.RotateLeft()), Step("+90°", "Rotate right 90°", r => r.RotateRight()))));

        hflip.Content = "Horizontal";
        hflip.ToolTip = "Mirror horizontally";
        vflip.Content = "Vertical";
        vflip.ToolTip = "Mirror vertically";
        framing.Children.Add(EditorRow("Mirror", Inline(hflip, vflip)));

        cropFields.Children.Add(Row(Labeled("Left", cropLeft), Labeled("Top", cropTop)));
        cropFields.Children.Add(Row(Labeled("Right", cropRight), Labeled("Bottom", cropBottom)));
        var cropApply = Button("Apply crop"); cropApply.Click += (_, _) => ApplyCrop();
        cropFields.Children.Add(Inline(cropApply));
        var resetCrop = RendererAction(ResetButton("Reset crop"), r => { displayedCrop = null; r.Crop = CropRect.Empty; });
        resetCrop.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = cropFields });
        framing.Children.Add(Disclosure("Crop · source pixels", cropFields, resetCrop));

        var resetPano = RendererAction(ResetButton("Reset 360° view"), r =>
        {
            DiscardNumberDrafts(panoFields);
            r.Pano360.Reset();
        });
        resetPano.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = panoFields });
        panoBody.Children.Add(SectionHeading("360° view", resetPano, topMargin: 4));
        panoBody.Children.Add(panorama);
        AddNumber(panoFields, "Yaw (°)", r => r.Pano360.Yaw, (r, v) => r.Pano360.Yaw = v, _ => true, "Enter a finite angle.");
        AddNumber(panoFields, "Pitch (°)", r => r.Pano360.Pitch, (r, v) => r.Pano360.Pitch = v, v => v >= -88.2 && v <= 88.2, "Pitch must be between −88.2° and 88.2°.");
        AddNumber(panoFields, "Zoom", r => r.Pano360.Zoom, (r, v) => r.Pano360.Zoom = v, v => v >= 0.1 && v <= 2, "Projection zoom must be between 0.1 and 2.");
        AddNumber(panoFields, "FOV (°)", r => r.Pano360.Fov, (r, v) => r.Pano360.Fov = v, v => v >= 30 && v <= 150, "Field of view must be between 30° and 150°.");
        panoBody.Children.Add(panoFields);
        panoBody.Children.Add(Text("Projection zoom is independent of framing. Reset keeps projection enabled.", true));

        track.Click += (_, _) => ShowTracks();
        capture.Click += (_, _) => ShowCaptureDevices();
        sizeMode.Click += (_, _) => Choose(sizeMode, new[] { ("Fit to window", false), ("Original size", true) },
            r => r.OriginalSize, (r, original) => { DiscardNumberDrafts(framing); r.SetOriginalSize(original); });
        aspect.Click += (_, _) => ChooseAspect();
        rotation.Click += (_, _) => Choose(rotation, new[] { ("0°", 0u), ("90°", 90u), ("180°", 180u), ("270°", 270u) }, r => r.Rotation, (r, v) => r.Rotation = v);
        processor.Click += (_, _) => Choose(processor, new[] { ("Auto", VideoProcessors.Auto), ("FlyleafVP", VideoProcessors.Flyleaf), ("D3D11VP", VideoProcessors.D3D11), ("Software fallback", VideoProcessors.SwsScale) }, r => r.PreferredVideoProcessor, (r, v) => r.PreferredVideoProcessor = v);
        deinterlace.Click += (_, _) => Choose(deinterlace, new[] { ("Auto", DeInterlace.Auto), ("Off / Progressive", DeInterlace.Progressive), ("Top field first", DeInterlace.TopField), ("Bottom field first", DeInterlace.BottomField) }, r => r.DeInterlace, (r, v) => r.DeInterlace = v);
        split.Click += (_, _) => Choose(split, Enum.GetValues<SplitFrameAlphaPosition>().Select(v => (v.ToString(), v)), r => r.SplitFrameAlphaPosition, (r, v) => r.SplitFrameAlphaPosition = v);
        customRatio.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyRatio(); e.Handled = true; } };
        hflip.Click += (_, _) => { if (!refreshing) Change(r => r.HFlip = hflip.IsChecked == true); };
        vflip.Click += (_, _) => { if (!refreshing) Change(r => r.VFlip = vflip.IsChecked == true); };
        panorama.Click += (_, _) => { if (!refreshing) Change(r => r.Pano360.Enabled = panorama.IsChecked == true); };
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach();
    }

    static FrameworkElement Disclosure(string title, UIElement body, Button? reset = null)
    {
        var label = Text(title);
        var expander = new Expander { Header = label, Content = body };
        expander.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush");
        var section = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        section.Children.Add(expander);
        if (reset != null)
        {
            label.Margin = new Thickness(0, 6, 36, 0);
            reset.HorizontalAlignment = HorizontalAlignment.Right;
            reset.VerticalAlignment = VerticalAlignment.Top;
            reset.Margin = new Thickness(0, 3, 0, 0);
            section.Children.Add(reset);
        }
        return section;
    }
    static Grid EditorRow(string title, FrameworkElement editor)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(86) }); row.ColumnDefinitions.Add(new());
        var label = Text(title, true); label.Margin = new Thickness(0, 0, 6, 0); label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label); Grid.SetColumn(editor, 1); row.Children.Add(editor); return row;
    }

    static CheckBox Check(string caption)
    {
        var check = new CheckBox { Content = caption, Margin = new Thickness(0, 10, 0, 4) };
        check.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle"); return check;
    }
    static TextBox Input(string value)
    {
        var input = new TextBox { Text = value, MinWidth = 55, VerticalContentAlignment = VerticalAlignment.Center };
        input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle"); return input;
    }
    static StackPanel Labeled(string label, UIElement editor)
    {
        var body = new StackPanel(); body.Children.Add(Text(label, true)); body.Children.Add(editor); return body;
    }
    static Grid Row(FrameworkElement left, FrameworkElement right)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new());
        right.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }
    static StackPanel Inline(params FrameworkElement[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var control in controls)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(row.Children.Count == 0 ? 0 : 6, 0, 0, 0);
            row.Children.Add(control);
        }
        return row;
    }

    Button Step(string title, string tooltip, Action<Renderer> action)
    {
        var button = Button(title);
        button.Height = button.MinHeight = EditorHeight;
        button.Width = StepWidth;
        button.Padding = new Thickness(4, 0, 4, 0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.ToolTip = tooltip;
        return RendererAction(button, action);
    }

    Button RendererAction(Button button, Action<Renderer> action)
    {
        button.Click += (_, _) => Change(action);
        return button;
    }

    static void DiscardNumberDrafts(StackPanel fields)
    {
        foreach (var field in fields.Children.OfType<NumberField>()) field.Dirty = false;
    }
    void Change(Action<Renderer> action)
    {
        if (renderer == null || player is not { IsDisposed: false }) return;
        try { action(renderer); feedback.Text = ""; Refresh(); }
        catch (Exception ex) { feedback.Text = ex.Message; }
    }
    NumberField AddNumber(StackPanel body, string label, Func<Renderer, double> read, Action<Renderer, double> write, Func<double, bool> valid, string error)
    {
        var field = new NumberField(label, read); numbers.Add(field);
        void Apply()
        {
            if (!double.TryParse(field.Input.Text, out var value) || !double.IsFinite(value) || !valid(value)) { feedback.Text = error; return; }
            field.Dirty = false; Change(r => write(r, value)); field.Refresh(renderer, true);
        }
        field.Input.LostKeyboardFocus += (_, _) => { if (field.Dirty) Apply(); };
        field.Input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Apply(); e.Handled = true; } };
        body.Children.Add(field); return field;
    }
    sealed class NumberField : Grid
    {
        internal readonly TextBox Input = FlyleafVideoPanel.Input("0");
        readonly Func<Renderer, double> read;
        internal bool Dirty;
        bool updating;
        internal NumberField(string label, Func<Renderer, double> read)
        {
            this.read = read; Margin = new Thickness(0, 5, 0, 5);
            ColumnDefinitions.Add(new() { Width = new GridLength(86) });
            ColumnDefinitions.Add(new() { Width = new GridLength(CompactFieldWidth) }); ColumnDefinitions.Add(new());
            var caption = Text(label, true); caption.VerticalAlignment = VerticalAlignment.Center; caption.Margin = new Thickness(0, 0, 6, 0);
            Input.Width = CompactFieldWidth; Input.Height = EditorHeight;
            Grid.SetColumn(Input, 1); Children.Add(caption); Children.Add(Input);
            Input.TextChanged += (_, _) => { if (!updating) Dirty = true; };
        }
        internal void Tools(params Button[] buttons)
        {
            var body = Inline(buttons);
            body.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(body, 2); Children.Add(body);
        }
        internal void Refresh(Renderer? current, bool force = false)
        {
            IsEnabled = current != null;
            if (force || !Dirty && !Input.IsKeyboardFocusWithin)
            { updating = true; Input.Text = (current == null ? 0 : read(current)).ToString("0.###", CultureInfo.CurrentCulture); updating = false; }
        }
    }

    void Attach()
    {
        Detach(); observedContext = Context;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        AttachPlayer();
    }
    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        observedContext = null; DetachPlayer();
    }
    void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(player, observedContext?.Player)) AttachPlayer();
    }
    void DetachRenderer()
    {
        if (renderer != null)
        {
            renderer.PropertyChanged -= RendererChanged; renderer.ViewportChanged -= ViewportChanged;
            renderer.Pano360.PropertyChanged -= RendererChanged;
        }
        renderer = null; displayedCrop = null;
        foreach (var number in numbers) number.Dirty = false;
    }
    void ObserveRenderer()
    {
        var current = player is { IsDisposed: false } ? player.Renderer : null;
        if (ReferenceEquals(current, renderer)) return;
        DetachRenderer(); renderer = current;
        if (renderer != null)
        {
            renderer.PropertyChanged += RendererChanged; renderer.ViewportChanged += ViewportChanged;
            renderer.Pano360.PropertyChanged += RendererChanged;
        }
    }
    void DetachPlayer()
    {
        if (openMenu != null) openMenu.IsOpen = false; openMenu = null;
        DetachRenderer();
        if (player != null)
        {
            player.PropertyChanged -= PlayerChanged; player.Video.PropertyChanged -= VideoChanged;
            player.OpenCompleted -= OpenChanged; player.OpenPlaylistItemCompleted -= OpenChanged;
            player.OpenVideoStreamCompleted -= OpenChanged; player.OpenExternalVideoStreamCompleted -= OpenChanged;
        }
        BindingOperations.ClearBinding(enabled, ToggleButton.IsCheckedProperty); player = null;
    }
    void AttachPlayer()
    {
        DetachPlayer(); player = observedContext?.Player;
        if (player != null)
        {
            player.PropertyChanged += PlayerChanged; player.Video.PropertyChanged += VideoChanged;
            player.OpenCompleted += OpenChanged; player.OpenPlaylistItemCompleted += OpenChanged;
            player.OpenVideoStreamCompleted += OpenChanged; player.OpenExternalVideoStreamCompleted += OpenChanged;
            enabled.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Enabled") { Source = player.Video, Mode = BindingMode.TwoWay });
        }
        displayedCrop = null; feedback.Text = ""; Refresh();
    }
    void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Player.Renderer) or nameof(Player.Status) or nameof(Player.CanPlay) or nameof(Player.IsDisposed)) QueueRefresh();
    }
    void VideoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Video.FPSCurrent) or nameof(Video.FramesDisplayed) or nameof(Video.FramesDropped) or nameof(Video.BitRate))) QueueRefresh();
    }
    void RendererChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
    void ViewportChanged(object? sender, EventArgs e) => QueueRefresh();
    void OpenChanged(object? sender, object e)
    {
        var error = e switch { OpenVideoStreamCompletedArgs result => result.Error, OpenExternalVideoStreamCompletedArgs result => result.Error, _ => null };
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded || !ReferenceEquals(sender, player)) return;
            if (e is OpenVideoStreamCompletedArgs or OpenExternalVideoStreamCompletedArgs) feedback.Text = error ?? "";
            Refresh();
        }));
    }
    void QueueRefresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(QueueRefresh)); return; }
        if (refreshQueued) return; refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { refreshQueued = false; if (IsLoaded) Refresh(); }));
    }
    void Refresh()
    {
        ObserveRenderer(); refreshing = true;
        var available = player is { IsDisposed: false };
        enabled.IsEnabled = available;
        capture.IsEnabled = available && Context?.MediaElement != null;
        track.IsEnabled = available && (player!.Video.Streams.Count > 0 || player.Playlist.Selected?.ExternalVideoStreams.Count > 0);
        trackCaption.Text = available && player!.Video.IsOpened ? $"{player.Video.Height}p · {player.Video.Codec}  ▾" : "Choose video track  ▾";
        processor.IsEnabled = renderer != null;
        details.Text = available && player!.Video.IsOpened ? $"{player.Video.Width} × {player.Video.Height} · {player.Video.FPS:0.###} fps" : "Open a video to choose its track.";
        framing.IsEnabled = processing.IsEnabled = panorama.IsEnabled = renderer != null;
        cropFields.IsEnabled = available && player!.Video.IsOpened && renderer != null;
        panoFields.IsEnabled = renderer?.Pano360.Enabled == true;
        foreach (var number in numbers) number.Refresh(renderer);
        bool originalSize = renderer?.OriginalSize == true;
        sizeMode.IsEnabled = available && player!.Video.IsOpened && renderer != null;
        sizeMode.Content = originalSize ? "Original size  ▾" : "Fit to window  ▾";
        aspect.IsEnabled = !originalSize;
        if (customRatio.Tag is FrameworkElement customEditor)
            customEditor.Visibility = !originalSize && renderer?.AspectRatio == AspectRatio.Custom ? Visibility.Visible : Visibility.Collapsed;
        aspect.Content = $"{renderer?.AspectRatio.ToString() ?? "Keep"}  ▾";
        rotation.Content = $"{renderer?.Rotation ?? 0}°  ▾";
        processor.Content = $"{ProcessorName(renderer?.PreferredVideoProcessor ?? VideoProcessors.Auto)}  ▾";
        deinterlace.Content = (renderer?.DeInterlace switch { DeInterlace.Progressive => "Off / Progressive", DeInterlace.TopField => "Top field first", DeInterlace.BottomField => "Bottom field first", _ => "Auto" }) + "  ▾";
        split.Content = $"{renderer?.SplitFrameAlphaPosition.ToString() ?? "None"}  ▾";
        effective.Text = available && player!.Video.IsOpened && renderer != null ? $"Active processor: {ProcessorName(renderer.VideoProcessor)}" : "No active processor";
        if (!customRatio.IsKeyboardFocusWithin) customRatio.Text = renderer?.AspectRatioCustom.ToString() ?? "16:9";
        var crop = renderer?.Crop ?? CropRect.Empty;
        if (displayedCrop != crop)
        {
            foreach (var entry in new[] { (cropLeft, crop.Left), (cropTop, crop.Top), (cropRight, crop.Right), (cropBottom, crop.Bottom) })
                entry.Item1.Text = entry.Item2.ToString(CultureInfo.CurrentCulture);
            displayedCrop = crop;
        }
        hflip.IsChecked = renderer?.HFlip == true; vflip.IsChecked = renderer?.VFlip == true; panorama.IsChecked = renderer?.Pano360.Enabled == true;
        refreshing = false;
    }
    void ApplyRatio()
    {
        var text = customRatio.Text.Replace('/', ':'); var parts = text.Split(':');
        if (parts.Length is < 1 or > 2 || !double.TryParse(parts[0], out var numerator) || !double.IsFinite(numerator) || numerator <= 0)
        { feedback.Text = "Enter a positive ratio, such as 16:9 or 1.85."; return; }
        double denominator = 1;
        if (parts.Length == 2 && (!double.TryParse(parts[1], out denominator) || !double.IsFinite(denominator) || denominator <= 0))
        { feedback.Text = "Enter a positive ratio, such as 16:9 or 1.85."; return; }
        if (!double.IsFinite(numerator / denominator) || numerator / denominator <= 0) { feedback.Text = "The ratio is outside the supported numeric range."; return; }
        Change(r => { r.AspectRatioCustom = new AspectRatio(numerator, denominator); r.AspectRatio = AspectRatio.Custom; });
    }
    static string ProcessorName(VideoProcessors value) => value switch
    {
        VideoProcessors.Flyleaf => "FlyleafVP", VideoProcessors.D3D11 => "D3D11VP",
        VideoProcessors.SwsScale => "Software fallback", _ => "Auto"
    };
    void ApplyCrop()
    {
        if (player?.Video.IsOpened != true || renderer == null) return;
        var bounds = renderer.GetCropBounds();
        var minWidth = renderer.SplitFrameAlphaPosition is SplitFrameAlphaPosition.Left or SplitFrameAlphaPosition.Right ? 2L : 1L;
        var minHeight = renderer.SplitFrameAlphaPosition is SplitFrameAlphaPosition.Top or SplitFrameAlphaPosition.Bottom ? 2L : 1L;
        if (!uint.TryParse(cropLeft.Text, out var left) || !uint.TryParse(cropTop.Text, out var top) || !uint.TryParse(cropRight.Text, out var right) || !uint.TryParse(cropBottom.Text, out var bottom)
            || (long)left + right + minWidth > bounds.Width || (long)top + bottom + minHeight > bounds.Height)
        { feedback.Text = "Enter non-negative whole pixels and leave at least one pixel of width and height."; return; }
        displayedCrop = null;
        Change(r => r.Crop = new CropRect(top, left, bottom, right));
    }
    ContextMenu Menu(Button anchor)
    {
        if (openMenu != null) openMenu.IsOpen = false;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MaxHeight = 360 }; openMenu = menu;
        menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
        IDisposable? hold = null;
        menu.Opened += (_, _) => hold = FlyleafPopupActivity.Hold(player);
        menu.Closed += (_, _) => { hold?.Dispose(); hold = null; };
        return menu;
    }
    void ShowCaptureDevices()
    {
        if (player is not { IsDisposed: false } target || Context?.MediaElement is not { } owner) return;
        var menu = Menu(capture);
        owner.PopulateCaptureDeviceMenu(menu, target, video: true);
        menu.IsOpen = true;
    }
    void ShowTracks()
    {
        if (player is not { IsDisposed: false } target) return;
        var menu = Menu(track);
        FlyleafTrackMenus.AddVideoTracks(menu, target, () => ReferenceEquals(player, target), () => feedback.Text = "Opening video…");
        if (menu.Items.Count > 0) menu.IsOpen = true;
    }
    void ChooseAspect() => Choose(aspect, AspectRatio.AspectRatios.Select(v => (v.ToString(), v)), r => r.AspectRatio, (r, v) => r.AspectRatio = v);
    void Choose<T>(Button anchor, IEnumerable<(string Label, T Value)> choices, Func<Renderer, T> read, Action<Renderer, T> write)
    {
        if (renderer is not { } current) return;
        var menu = Menu(anchor);
        foreach (var (label, value) in choices)
        {
            var item = new MenuItem { Header = label, IsChecked = EqualityComparer<T>.Default.Equals(read(current), value) };
            item.SetResourceReference(StyleProperty, "FlyleafME.ThemeMenuItemStyle");
            item.Click += (_, _) => { if (ReferenceEquals(current, renderer)) Change(r => write(r, value)); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
