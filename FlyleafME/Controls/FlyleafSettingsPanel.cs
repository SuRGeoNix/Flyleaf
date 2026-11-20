using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FlyleafLib.MediaPlayer;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>A settings category contribution. Each factory must return a new visual.</summary>
public sealed class FlyleafSettingsPage
{
    public string Id { get; }
    public string Title { get; }
    public Func<FlyleafPanelContext, FrameworkElement> CreateEditor { get; }
    public Func<FlyleafPanelContext, bool>? IsAvailable { get; init; }

    public FlyleafSettingsPage(string id, string title, Func<FlyleafPanelContext, FrameworkElement> createEditor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(createEditor);
        Id = id; Title = title; CreateEditor = createEditor;
    }
}

/// <summary>Reusable settings navigation; applications may add categories through Pages.</summary>
public sealed class FlyleafSettingsPanel : Grid
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafSettingsPanel),
        new PropertyMetadata(null, (d, _) => { if (((FlyleafSettingsPanel)d).IsLoaded) ((FlyleafSettingsPanel)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    public ObservableCollection<FlyleafSettingsPage> Pages { get; } = [];
    public IReadOnlyDictionary<string, FlyleafShortcutGesture> CustomKeybindingDefaults { get; set; } = new Dictionary<string, FlyleafShortcutGesture>();

    readonly StackPanel rail = new();
    readonly Grid sidebar = new();
    readonly FlyleafSettingsBranding sidebarBranding = new(false), compactBranding = new(true);
    readonly Button compact = Button("Settings ▾");
    readonly ContentControl content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ScrollViewer pageScroll;
    FlyleafPanelContext? observedContext;
    FlyleafSettingsPage? selected;
    FlyleafSettingsPage? shownPage;
    FlyleafPanelContext? shownContext;
    Player? observedPlayer;
    ContextMenu? categoryMenu;

    public FlyleafSettingsPanel()
    {
        ColumnDefinitions.Add(new() { Width = new GridLength(0) });
        ColumnDefinitions.Add(new());
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new());
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        compact.Margin = new Thickness(0, 0, 0, 12);
        SetColumnSpan(compact, 2);
        Children.Add(compact);
        var navigationScroll = new ScrollViewer
        {
            Content = rail,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 12, 0)
        };
        sidebar.RowDefinitions.Add(new()); sidebar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        sidebar.Children.Add(navigationScroll); SetRow(sidebarBranding, 1); sidebar.Children.Add(sidebarBranding);
        SetRow(sidebar, 1); Children.Add(sidebar);
        pageScroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 12) };
        SetRow(pageScroll, 1); SetColumn(pageScroll, 1); Children.Add(pageScroll);
        SetRow(compactBranding, 2); SetColumnSpan(compactBranding, 2); Children.Add(compactBranding);
        Pages.Add(new("playback", "Playback", context => new FlyleafPlaybackSettings { Context = context }));
        Pages.Add(new("audio", "Audio", context => new FlyleafAudioSettings { Context = context }));
        Pages.Add(new("video", "Video", context => new FlyleafVideoSettings { Context = context }));
        Pages.Add(new("subtitles", "Subtitles", context => new FlyleafSubtitlesSettings { Context = context }));
        Pages.Add(new("demuxer", "Demuxer", context => new FlyleafDemuxerSettings { Context = context }));
        Pages.Add(new("plugins", "Plugins", context => new FlyleafPluginSettings { Context = context })
        { IsAvailable = context => context.Player != null && Engine.Plugins.Types.Values.Any(t => context.Player.Config.Plugins.TryGetValue(t.Name, out var options) && options.Count > 0) });
        Pages.Add(new("keybindings", "Bindings", context => new FlyleafKeybindingsSettings { Context = context, CustomDefaults = CustomKeybindingDefaults }));
        Pages.CollectionChanged += PagesChanged;
        compact.Click += (_, _) => ShowCategories();
        SizeChanged += (_, _) => UpdateLayoutMode();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    internal void AlignPageScrollbar(double panelRightPadding)
    {
        // Extend the scrollbar into the panel padding and retain the page's text gutter.
        double extension = Math.Max(0, panelRightPadding - 4);
        pageScroll.Margin = new Thickness(0, 0, -extension, 0);
        pageScroll.Padding = new Thickness(0, 0, 8 + extension, 12);
    }

    void Attach()
    {
        Detach();
        observedContext = Context;
        observedPlayer = Context?.Player;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        Rebuild();
        UpdateLayoutMode();
    }

    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        observedContext = null;
        if (categoryMenu != null) categoryMenu.IsOpen = false;
        categoryMenu = null;
    }

    void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlyleafPanelContext.Player) or nameof(FlyleafPanelContext.MediaElement))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded || Context?.Player == observedPlayer && Context?.MediaElement != null) return;
                observedPlayer = Context?.Player; shownPage = null; Rebuild();
            }));
    }

    void PagesChanged(object? sender, NotifyCollectionChangedEventArgs e) { if (IsLoaded) Rebuild(); }

    void Rebuild()
    {
        var available = AvailablePages().ToArray();
        selected = available.FirstOrDefault(p => p.Id == selected?.Id) ?? available.FirstOrDefault();
        rail.Children.Clear();
        foreach (var page in available)
        {
            var button = Button(page.Title);
            button.Margin = new Thickness(0, 0, 0, 5);
            button.FontWeight = page == selected ? FontWeights.SemiBold : FontWeights.Normal;
            if (page == selected)
            {
                button.SetResourceReference(Control.BackgroundProperty, "FlyleafME.SelectionBrush");
                button.SetResourceReference(Control.BorderBrushProperty, "FlyleafME.AccentBrush");
            }
            button.Click += (_, _) => { selected = page; Rebuild(); };
            rail.Children.Add(button);
        }
        ShowSelected();
    }

    void ShowSelected()
    {
        if (categoryMenu != null) categoryMenu.IsOpen = false;
        compact.Content = (selected?.Title ?? "Settings") + " ▾";
        if (selected == shownPage && Context == shownContext) return;
        shownPage = selected; shownContext = Context;
        content.Content = selected != null && Context != null ? selected.CreateEditor(Context) : null;
        pageScroll.ScrollToTop();
    }

    void UpdateLayoutMode()
    {
        bool wide = ActualWidth >= 580;
        ColumnDefinitions[0].Width = new GridLength(wide ? 145 : 0);
        sidebar.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        // Branding uses spare navigation space; short panels prioritize navigation and editors.
        sidebarBranding.Visibility = wide && ActualHeight >= 500 ? Visibility.Visible : Visibility.Collapsed;
        compactBranding.Visibility = !wide && ActualHeight >= 300 ? Visibility.Visible : Visibility.Collapsed;
        compact.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
    }

    void ShowCategories()
    {
        var menu = new ContextMenu { PlacementTarget = compact, Placement = PlacementMode.Bottom };
        menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
        foreach (var page in AvailablePages())
        {
            var item = new MenuItem { Header = page.Title, IsCheckable = true, IsChecked = page == selected };
            item.Click += (_, _) => { selected = page; Rebuild(); };
            menu.Items.Add(item);
        }
        categoryMenu = menu;
        menu.IsOpen = true;
    }

    IEnumerable<FlyleafSettingsPage> AvailablePages() => Pages.Where(p => Context != null && (p.IsAvailable?.Invoke(Context) ?? true));
}
