using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Flyleaf.Controls.WPF;

sealed class FlyleafDebugOverlay : Border
{
    readonly FlyleafMediaInformation source;
    readonly TabControl tabs = new();
    IDisposable? lease;

    internal FlyleafDebugOverlay(FlyleafMediaInformation source)
    {
        this.source = source;
        ClipToBounds = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        KeyboardNavigation.SetControlTabNavigation(this, KeyboardNavigationMode.None);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "FlyleafME.OverlayBrush");
        SetResourceReference(BorderBrushProperty, "FlyleafME.OverlayBorderBrush");
        AutomationProperties.SetName(this, "Playback debug overlay");

        tabs.SetResourceReference(StyleProperty, "FlyleafME.DebugTabsStyle");
        tabs.Items.Add(CreateTab("Playback",
            [("Source", source.SourceSection)],
            [("Playback", source.PlaybackSection), ("Statistics", source.PlaybackStatisticsSection)]));
        tabs.Items.Add(CreateTab("Video",
            [("Input", source.VideoSection)],
            [("Processing and output", source.ProcessingSection), ("Statistics", source.VideoStatisticsSection)]));
        tabs.Items.Add(CreateTab("Audio",
            [("Input", source.AudioSection)],
            [("Output", source.AudioOutputSection), ("Statistics", source.AudioStatisticsSection)]));
        tabs.Items.Add(CreateTab("Subtitles",
            [("Input", source.SubtitleSection)],
            [("Rendering", source.SubtitleRenderingSection)]));
        tabs.SelectedIndex = 0;
        Child = tabs;

        SetBinding(VisibilityProperty, new Binding(nameof(FlyleafMediaInformation.ShowDebugOverlay))
        {
            Source = source,
            Converter = new System.Windows.Controls.BooleanToVisibilityConverter(),
            FallbackValue = Visibility.Collapsed
        });
        Loaded += (_, _) => UpdateLease();
        Unloaded += (_, _) => Release();
        IsVisibleChanged += (_, _) => UpdateLease();
    }

    internal void CycleTab(bool backwards)
    {
        int count = tabs.Items.Count;
        tabs.SelectedIndex = (tabs.SelectedIndex + (backwards ? count - 1 : 1)) % count;
    }

    void UpdateLease()
    {
        if (!IsLoaded || !IsVisible) { Release(); return; }
        lease ??= source.Acquire();
    }

    void Release() { lease?.Dispose(); lease = null; }

    static TabItem CreateTab(string label,
        (string Title, FlyleafMediaInformation.DetailSection Details)[] first,
        (string Title, FlyleafMediaInformation.DetailSection Details)[] second)
    {
        var columns = new Grid { Margin = new Thickness(16, 14, 16, 4) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var left = CreateColumn(first);
        var right = CreateColumn(second);
        columns.Children.Add(left);
        columns.Children.Add(right);
        void ArrangeColumns(double width)
        {
            bool compact = width < 560;
            Grid.SetColumnSpan(left, compact ? 2 : 1);
            Grid.SetColumn(right, compact ? 0 : 1);
            Grid.SetColumnSpan(right, compact ? 2 : 1);
            Grid.SetRow(right, compact ? 1 : 0);
            left.Margin = new Thickness(0, 0, compact ? 0 : 12, 0);
            right.Margin = new Thickness(compact ? 0 : 12, 0, 0, 0);
        }
        ArrangeColumns(0);
        columns.SizeChanged += (_, e) => { if (e.WidthChanged) ArrangeColumns(e.NewSize.Width); };
        var tab = new TabItem { Header = label, Content = columns, IsTabStop = false };
        tab.SetResourceReference(StyleProperty, "FlyleafME.DebugTabStyle");
        return tab;
    }

    static StackPanel CreateColumn((string Title, FlyleafMediaInformation.DetailSection Details)[] sections)
    {
        var column = new StackPanel();
        foreach (var (title, details) in sections)
        {
            var heading = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.OverlayTextBrush");
            column.Children.Add(heading);

            var rows = new ItemsControl { Margin = new Thickness(0, 0, 0, 15) };
            rows.SetResourceReference(ItemsControl.ItemTemplateProperty, "FlyleafME.DebugRowTemplate");
            rows.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(details.Rows)) { Source = details });
            column.Children.Add(rows);
        }
        return column;
    }
}
