using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
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
    /// <summary>Allows Settings search to build a detached editor from this factory and read its option labels.</summary>
    public bool IndexOptions { get; init; }

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
    readonly TextBox search = new() { Padding = new Thickness(9, 5, 9, 5) };
    readonly StackPanel searchItems = new() { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ScrollViewer searchScroll;
    SearchResult[]? searchIndex;
    Button? firstResult;
    FlyleafSettingsSearch.Highlight? searchHighlight;
    AdornerLayer? highlightLayer;
    FlyleafPanelContext? observedContext;
    FlyleafSettingsPage? selected;
    FlyleafSettingsPage? shownPage;
    FlyleafPanelContext? shownContext;
    Player? observedPlayer;
    ContextMenu? categoryMenu;

    sealed record SearchResult(FlyleafSettingsPage Page, FlyleafSettingsSearch.Location? Location = null)
    {
        internal string Title => Location?.Title ?? Page.Title;
        internal string Breadcrumb => Location is { Section.Length: > 0 } option ? $"{Page.Title} › {option.Section}" : Page.Title;
    }

    public FlyleafSettingsPanel()
    {
        ColumnDefinitions.Add(new() { Width = new GridLength(0) });
        ColumnDefinitions.Add(new());
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new());
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var top = new StackPanel();
        SetColumnSpan(top, 2); Children.Add(top);
        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        searchRow.ColumnDefinitions.Add(new()); searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        search.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        AutomationProperties.SetName(search, "Search settings");
        searchRow.Children.Add(search);
        var placeholder = Text("Search settings…", true);
        placeholder.Margin = new Thickness(10, 0, 0, 0); placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false; searchRow.Children.Add(placeholder);
        var clear = Button("×"); clear.Margin = new Thickness(5, 0, 0, 0); clear.MinWidth = 32;
        clear.ToolTip = "Clear settings search"; AutomationProperties.SetName(clear, "Clear settings search");
        clear.Visibility = Visibility.Collapsed; SetColumn(clear, 1); searchRow.Children.Add(clear);
        clear.Click += (_, _) => { search.Clear(); search.Focus(); };
        search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateSearch();
        };
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && firstResult is { } result)
            {
                e.Handled = true;
                result.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (e.Key == Key.Down && firstResult?.Focus() == true) e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || string.IsNullOrWhiteSpace(search.Text)) return;
            e.Handled = true; search.Clear(); search.Focus();
        };
        top.Children.Add(searchRow);
        compact.Margin = new Thickness(0, 0, 0, 12); top.Children.Add(compact);
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
        searchScroll = new ScrollViewer { Content = searchItems, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed, Padding = pageScroll.Padding };
        SetRow(searchScroll, 1); SetColumn(searchScroll, 1); Children.Add(searchScroll);
        SetRow(compactBranding, 2); SetColumnSpan(compactBranding, 2); Children.Add(compactBranding);
        Pages.Add(new("playback", "Playback", context => new FlyleafPlaybackSettings { Context = context }) { IndexOptions = true });
        Pages.Add(new("audio", "Audio", context => new FlyleafAudioSettings { Context = context }) { IndexOptions = true });
        Pages.Add(new("video", "Video", context => new FlyleafVideoSettings { Context = context }) { IndexOptions = true });
        Pages.Add(new("subtitles", "Subtitles", context => new FlyleafSubtitlesSettings { Context = context }) { IndexOptions = true });
        Pages.Add(new("demuxer", "Demuxer", context => new FlyleafDemuxerSettings { Context = context }) { IndexOptions = true });
        Pages.Add(new("plugins", "Plugins", context => new FlyleafPluginSettings { Context = context })
        { IndexOptions = true, IsAvailable = context => context.Player != null && Engine.Plugins.Types.Values.Any(t => context.Player.Config.Plugins.TryGetValue(t.Name, out var options) && options.Count > 0) });
        Pages.Add(new("keybindings", "Bindings", context => new FlyleafKeybindingsSettings { Context = context, CustomDefaults = CustomKeybindingDefaults }) { IndexOptions = true });
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
        searchScroll.Margin = pageScroll.Margin;
        searchScroll.Padding = pageScroll.Padding;
    }

    void Attach()
    {
        Detach();
        observedContext = Context;
        observedPlayer = Context?.Player;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        Rebuild();
        UpdateSearch();
        UpdateLayoutMode();
    }

    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        observedContext = null;
        if (categoryMenu != null) categoryMenu.IsOpen = false;
        categoryMenu = null;
        searchIndex = null;
        ClearSearchHighlight();
    }

    void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlyleafPanelContext.Player) or nameof(FlyleafPanelContext.MediaElement))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded || Context?.Player == observedPlayer && Context?.MediaElement != null) return;
                observedPlayer = Context?.Player; shownPage = null; RefreshPages();
            }));
    }

    void PagesChanged(object? sender, NotifyCollectionChangedEventArgs e) { if (IsLoaded) RefreshPages(); }

    void RefreshPages()
    {
        searchIndex = null;
        Rebuild(); UpdateSearch();
    }

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
            button.Click += (_, _) => SelectPage(page);
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

    void SelectPage(FlyleafSettingsPage page)
    {
        search.Clear();
        ClearSearchHighlight();
        selected = page;
        Rebuild();
    }

    IEnumerable<SearchResult> BuildSearchIndex()
    {
        if (Context is not { } context) yield break;
        foreach (var page in AvailablePages())
        {
            yield return new(page);
            if (page.IndexOptions)
                foreach (var location in IndexPageOptions(page, context)) yield return new(page, location);
        }
    }

    FlyleafSettingsSearch.Location[] IndexPageOptions(FlyleafSettingsPage page, FlyleafPanelContext context)
    {
        try
        {
            var editor = page == shownPage && content.Content is FrameworkElement { IsLoaded: true } visible
                ? visible : page.CreateEditor(context);
            if (editor is FlyleafSettingsEditor { IsLoaded: false } settings)
            {
                if (GetValue(FlyleafSettingsReset.DefaultsProperty) is Config defaults)
                    FlyleafSettingsReset.SetDefaults(settings, defaults);
                settings.BuildForSearch(context);
            }
            return FlyleafSettingsSearch.Find(editor).Select(option => option.Location).ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Could not read option labels on Settings page '{0}': {1}", page.Id, ex.Message);
            return [];
        }
    }

    void UpdateSearch()
    {
        ClearSearchHighlight();
        firstResult = null;
        searchItems.Children.Clear();
        var terms = search.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var query = search.Text.Trim();
        pageScroll.Visibility = terms.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchScroll.Visibility = terms.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (terms.Length == 0) { searchIndex = null; return; }

        var results = (searchIndex ??= BuildSearchIndex().Distinct().ToArray())
            .Where(result => terms.Any(term => Matches(result.Title, term)) &&
                terms.All(term => Matches(result.Title, term) || Matches(result.Breadcrumb, term)))
            .OrderBy(Rank);
        foreach (var result in results)
        {
            var name = Text(result.Title); name.Margin = new Thickness(0); name.FontSize = 13; name.FontWeight = FontWeights.SemiBold;
            var caption = new StackPanel(); caption.Children.Add(name);
            if (result.Location != null)
            {
                var breadcrumb = Text(result.Breadcrumb, true); breadcrumb.Margin = new Thickness(0, 3, 0, 0);
                caption.Children.Add(breadcrumb);
            }
            var button = Button(""); button.Content = caption; button.Margin = new Thickness(0, 0, 0, 5);
            AutomationProperties.SetName(button, result.Location == null ? result.Title : $"{result.Title}, {result.Breadcrumb}");
            button.Click += (_, _) => OpenResult(result);
            firstResult ??= button;
            searchItems.Children.Add(button);
        }
        if (firstResult == null) searchItems.Children.Add(Text("No settings found.", true));
        searchScroll.ScrollToTop();

        int Rank(SearchResult result)
        {
            if (result.Title.Equals(query, StringComparison.CurrentCultureIgnoreCase)) return result.Location == null ? 0 : 1;
            if (Matches(result.Title, query)) return 2;
            return terms.All(term => Matches(result.Title, term)) ? 3 : 4;
        }
        static bool Matches(string value, string term) => value.Contains(term, StringComparison.CurrentCultureIgnoreCase);
    }

    void OpenResult(SearchResult result)
    {
        SelectPage(result.Page);
        if (selected != result.Page) return;
        if (result.Location == null) { pageScroll.ScrollToTop(); return; }
        if (content.Content is not FrameworkElement editor) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsLoaded || !ReferenceEquals(content.Content, editor) || searchScroll.Visibility == Visibility.Visible) return;
            var target = FlyleafSettingsSearch.Find(editor).FirstOrDefault(option => option.Location == result.Location).Target;
            if (target == null) return;
            FlyleafSettingsSearch.Reveal(target);
            pageScroll.UpdateLayout();
            target.BringIntoView(new Rect(0, 0, target.ActualWidth, Math.Min(target.ActualHeight, 38)));
            if (AdornerLayer.GetAdornerLayer(target) is not { } layer) return;
            ClearSearchHighlight();
            searchHighlight = new(target) { IsHitTestVisible = false };
            highlightLayer = layer; layer.Add(searchHighlight);
        }));
    }

    void ClearSearchHighlight()
    {
        if (searchHighlight != null) highlightLayer?.Remove(searchHighlight);
        searchHighlight = null; highlightLayer = null;
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
            item.Click += (_, _) => SelectPage(page);
            menu.Items.Add(item);
        }
        categoryMenu = menu;
        menu.IsOpen = true;
    }

    IEnumerable<FlyleafSettingsPage> AvailablePages() => Pages.Where(p => Context != null && (p.IsAvailable?.Invoke(Context) ?? true));
}
