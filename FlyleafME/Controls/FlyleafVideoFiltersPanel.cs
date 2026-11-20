using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

/// <summary>Processor-aware filter panel. All filter values remain in Player.Config.</summary>
public sealed class FlyleafVideoFiltersPanel : StackPanel
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafVideoFiltersPanel),
        new PropertyMetadata(null, (d, _) => { var panel = (FlyleafVideoFiltersPanel)d; if (panel.IsLoaded) panel.Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    readonly ComboBox processor = new() { Margin = new Thickness(0, 6, 0, 10), DisplayMemberPath = "Name", SelectedValuePath = "Value" };
    readonly CheckBox synchronize = new() { Content = "Sync filters across processors", Margin = new Thickness(0, 6, 0, 10), ToolTip = "Map common saved filters when switching processors; picture output can differ." };
    readonly TextBlock active = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock processorHeading = Label("Processor preference");
    /// <summary>Hide the standalone selector when embedded in a larger Video panel.</summary>
    public bool ShowProcessorSelection
    {
        get => processor.Visibility == Visibility.Visible;
        set => processorHeading.Visibility = processor.Visibility = active.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    readonly StackPanel filters = new();
    readonly Button reset = FlyleafPanelUI.ResetButton("Reset picture filters to defaults");
    FlyleafPanelContext? observedContext;
    Player? player;
    Renderer? renderer;
    bool refreshQueued;

    record ProcessorChoice(string Name, VideoProcessors Value);

    public FlyleafVideoFiltersPanel()
    {
        processor.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
        synchronize.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        Children.Add(processorHeading);
        processor.ItemsSource = new[]
        {
            new ProcessorChoice("Auto", VideoProcessors.Auto), new ProcessorChoice("FlyleafVP", VideoProcessors.Flyleaf),
            new ProcessorChoice("D3D11VP", VideoProcessors.D3D11), new ProcessorChoice("Software fallback", VideoProcessors.SwsScale)
        };
        Children.Add(processor); Children.Add(active);
        Children.Add(FlyleafPanelUI.SectionHeading("Picture filters", reset, topMargin: 4));
        Children.Add(synchronize); Children.Add(status); Children.Add(filters);
        foreach (var text in new[] { active, status }) text.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        synchronize.SetResourceReference(CheckBox.ForegroundProperty, "FlyleafME.TextBrush");
        reset.IsEnabled = false;
        reset.Click += (_, _) =>
        {
            RefreshFilters();
            foreach (var editor in filters.Children.OfType<FlyleafVideoFilterEditor>()) editor.Reset();
        };
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text };
        label.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush");
        return label;
    }

    void Attach()
    {
        Detach();
        observedContext = Context;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        AttachPlayer();
    }

    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        observedContext = null;
        DetachPlayer();
    }

    void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(player, observedContext?.Player)) AttachPlayer();
    }

    void DetachPlayer()
    {
        if (player != null)
        {
            player.PropertyChanged -= PlayerChanged;
            player.Video.PropertyChanged -= VideoChanged;
        }
        if (renderer != null)
        {
            renderer.FiltersChanged -= FiltersChanged;
            renderer.PropertyChanged -= RendererChanged;
        }
        player = null; renderer = null;
        filters.Children.Clear();
        reset.IsEnabled = false;
        BindingOperations.ClearBinding(processor, Selector.SelectedValueProperty);
        BindingOperations.ClearBinding(synchronize, ToggleButton.IsCheckedProperty);
    }

    void AttachPlayer()
    {
        DetachPlayer();
        player = observedContext?.Player;
        if (player != null)
        {
            player.PropertyChanged += PlayerChanged;
            player.Video.PropertyChanged += VideoChanged;
            synchronize.SetBinding(ToggleButton.IsCheckedProperty, new Binding("SyncVPFilters")
            { Source = player.Config.Video, Mode = BindingMode.TwoWay });
        }
        ObserveRenderer();
        QueueRefresh();
    }

    void ObserveRenderer()
    {
        if (ReferenceEquals(renderer, player?.Renderer)) return;
        if (renderer != null)
        {
            renderer.FiltersChanged -= FiltersChanged;
            renderer.PropertyChanged -= RendererChanged;
        }
        renderer = player?.Renderer;
        BindingOperations.ClearBinding(processor, Selector.SelectedValueProperty);
        if (renderer != null)
        {
            renderer.FiltersChanged += FiltersChanged;
            renderer.PropertyChanged += RendererChanged;
            processor.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(Renderer.PreferredVideoProcessor))
            { Source = renderer, Mode = BindingMode.TwoWay });
        }
    }

    void PlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Player.Renderer) or nameof(Player.CanPlay) or nameof(Player.Status)) QueueRefresh();
    }
    void VideoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Video.IsOpened)) QueueRefresh();
    }
    void RendererChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Renderer.PreferredVideoProcessor) or nameof(Renderer.VideoProcessor)) QueueRefresh();
    }
    void FiltersChanged(object? sender, EventArgs e) => QueueRefresh();

    void QueueRefresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(QueueRefresh)); return; }
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            refreshQueued = false;
            if (IsLoaded) { ObserveRenderer(); RefreshFilters(); }
        }));
    }

    void RefreshFilters()
    {
        processor.IsEnabled = renderer != null;
        synchronize.IsEnabled = player != null;
        reset.IsEnabled = false;
        if (player?.Video.IsOpened != true || renderer == null)
        {
            filters.Children.Clear();
            active.Text = "Active processor: no video";
            status.Text = "Open a video to adjust its initialized filters.";
            return;
        }
        var snapshot = renderer.GetVideoFilters();
        active.Text = "Active processor: " + (snapshot.Processor switch
        {
            VideoProcessors.D3D11 => "D3D11VP", VideoProcessors.Flyleaf => "FlyleafVP",
            VideoProcessors.SwsScale => "Software fallback", _ => "initializing…"
        });
        if (snapshot.Processor == VideoProcessors.Auto)
        {
            filters.Children.Clear();
            status.Text = "Waiting for the video processor to initialize.";
            return;
        }
        if (snapshot.Processor == VideoProcessors.D3D11)
        {
            status.Text = "Device-supported native filter levels.";
            UpdateEditors(snapshot.D3D11Filters.OrderBy(filter => filter.Filter).Cast<object>().ToArray());
        }
        else
        {
            status.Text = "Native filter levels · ranges are in the tooltips.";
            UpdateEditors(snapshot.FlyleafFilters.OrderBy(filter => filter.Filter).Cast<object>().ToArray());
        }
        reset.IsEnabled = filters.Children.Count > 0;
        if (filters.Children.Count == 0) status.Text = "No supported filters are available for this video processor.";
    }

    void UpdateEditors(object[] currentFilters)
    {
        // Values already notify their existing bindings. Rebuild only when filter
        // identities/ranges change, not on repeated readiness or status updates.
        if (filters.Children.Count == currentFilters.Length && currentFilters.Select((filter, i) =>
            filters.Children[i] is FlyleafVideoFilterEditor editor && editor.Matches(filter)).All(matches => matches)) return;
        filters.Children.Clear();
        foreach (var filter in currentFilters) filters.Children.Add(new FlyleafVideoFilterEditor { Filter = filter });
    }

}
