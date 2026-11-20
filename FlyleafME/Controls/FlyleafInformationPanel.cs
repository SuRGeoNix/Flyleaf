using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>Media details and automatically collected playback statistics.</summary>
public sealed class FlyleafInformationPanel : StackPanel
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafInformationPanel),
        new PropertyMetadata(null, (d, _) => { if (((FlyleafInformationPanel)d).IsLoaded) ((FlyleafInformationPanel)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    readonly StackPanel content = new();
    readonly TextBlock feedback = Text("", true);
    FlyleafPanelContext? observedContext;
    FlyleafMediaInformation? source;
    IDisposable? lease;

    public FlyleafInformationPanel()
    {
        Children.Add(content);
        feedback.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => UpdateLease();
    }
    void Attach()
    {
        Detach(); observedContext = Context;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        BindSource();
    }
    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        observedContext = null; lease?.Dispose(); lease = null; source = null; content.Children.Clear();
    }
    void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(source, observedContext?.MediaElement?.Information)) BindSource();
    }
    void BindSource()
    {
        lease?.Dispose(); lease = null; content.Children.Clear();
        source = observedContext?.MediaElement?.Information;
        if (source == null) return;
        UpdateLease();
        feedback.Visibility = Visibility.Collapsed;
        var overlay = new CheckBox { Content = "Show statistics overlay" };
        overlay.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        overlay.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(FlyleafMediaInformation.ShowStatisticsOverlay))
        {
            Source = source, Mode = BindingMode.TwoWay
        });
        content.Children.Add(overlay);
        var copy = Button("Copy information");
        copy.Click += (_, _) =>
        {
            if (source == null) return;
            source.Refresh();
            try { Clipboard.SetText(source.Summary); feedback.Text = "Information copied."; }
            catch (Exception ex) { feedback.Text = ex.Message; }
            feedback.Visibility = Visibility.Visible;
        };
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.Margin = new Thickness(0, 12, 0, 0);
        content.Children.Add(CollapsibleSection("Source", Details(source.SourceSection), expanded: true));
        content.Children.Add(CollapsibleSection("Playback", Details(source.PlaybackSection), expanded: true));
        content.Children.Add(CollapsibleSection("Processing", Details(source.ProcessingSection), expanded: true));
        content.Children.Add(CollapsibleSection("Video", Details(source.VideoSection), expanded: true));
        content.Children.Add(CollapsibleSection("Audio", Details(source.AudioSection), expanded: true));
        content.Children.Add(CollapsibleSection("Subtitles", Details(source.SubtitleSection), expanded: true));
        content.Children.Add(CollapsibleSection("Data", Details(source.DataSection), expanded: true));
        content.Children.Add(CollapsibleSection("Statistics", Details(source.StatisticsSection), expanded: true));
        var diagnostics = new StackPanel();
        diagnostics.Children.Add(CollapsibleSection("Latest error", Message(nameof(FlyleafMediaInformation.ErrorDetails)), expanded: true));
        diagnostics.Children.Add(CollapsibleSection("Latest snapshot", Message(nameof(FlyleafMediaInformation.CaptureDetails)), expanded: true));
        diagnostics.Children.Add(CollapsibleSection("Latest recording", Message(nameof(FlyleafMediaInformation.RecordingDetails)), expanded: true));
        content.Children.Add(CollapsibleSection("Diagnostics", diagnostics, expanded: true));
        content.Children.Add(copy);
        content.Children.Add(feedback);
    }
    void UpdateLease()
    {
        if (IsLoaded && IsVisible && source != null)
            lease ??= source.Acquire();
        else
        {
            lease?.Dispose();
            lease = null;
        }
    }

    static ItemsControl Details(FlyleafMediaInformation.DetailSection section)
    {
        var rows = new ItemsControl { Margin = new Thickness(0, 4, 0, 0) };
        rows.SetResourceReference(ItemsControl.ItemTemplateProperty, "FlyleafME.InformationRowTemplate");
        rows.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(section.Rows)) { Source = section });
        return rows;
    }

    TextBox Message(string property)
    {
        var text = new TextBox();
        text.SetResourceReference(StyleProperty, "FlyleafME.InformationTextStyle");
        text.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.OneWay });
        return text;
    }
}
