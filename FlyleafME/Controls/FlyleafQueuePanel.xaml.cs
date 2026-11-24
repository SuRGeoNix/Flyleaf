using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafQueuePanel : UserControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(FlyleafPanelContext), typeof(FlyleafQueuePanel), new PropertyMetadata(null, (d, _) => { if (((FlyleafQueuePanel)d).IsLoaded) ((FlyleafQueuePanel)d).Attach(); }));
    public FlyleafPanelContext? Context { get => (FlyleafPanelContext?)GetValue(ContextProperty); set => SetValue(ContextProperty, value); }
    readonly ObservableCollection<object> rows = [];
    readonly HashSet<FlyleafQueueSource> collapsed = [];
    FlyleafPanelContext? observedContext;
    FlyleafQueue? queue;
    Point dragStart;
    FlyleafQueueSource? dragSource;
    bool deferredSelection;
    string searchText = "";
    bool IsSearching => searchText.Length != 0;
    IDisposable? queueMenuActivity;
    public FlyleafQueuePanel()
    {
        InitializeComponent(); Items.ItemsSource = rows;
        SearchBox.TextChanged += Search_TextChanged;
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach();
    }
    void Attach()
    {
        Detach(); observedContext = Context;
        if (observedContext != null) observedContext.PropertyChanged += ContextChanged;
        SetQueue();
    }
    void Detach()
    {
        if (observedContext != null) observedContext.PropertyChanged -= ContextChanged;
        observedContext = null;
        if (Items.ContextMenu.IsOpen) Items.ContextMenu.IsOpen = false;
        if (queue != null) queue.PropertyChanged -= QueueChanged;
        queue = null; DataContext = null;
    }
    void ContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(queue, observedContext?.MediaElement?.Queue)) SetQueue();
    }
    void SetQueue()
    {
        if (queue != null) queue.PropertyChanged -= QueueChanged;
        queue = observedContext?.MediaElement?.Queue;
        if (queue != null) queue.PropertyChanged += QueueChanged;
        DataContext = queue; RefreshRows();
    }
    void QueueChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FlyleafQueue.Revision) or nameof(FlyleafQueue.TotalItems)) RefreshRows();
    }
    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text.Trim();
        if (searchText == text) return;
        searchText = text;
        dragSource = null; deferredSelection = false;
        Items.Tag = IsSearching;
        RefreshRows();
        if (!IsSearching) ScheduleCurrentScroll(true);
    }

    void ClearSearch_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }


    void RefreshRows()
    {
        SortQueueButton.IsEnabled = queue?.Sources.Count >= 2;
        var desired = new List<object>();
        int matches = 0;
        if (queue != null)
            foreach (var source in queue.Sources)
            {
                if (!IsSearching)
                {
                    desired.Add(source);
                    if (!collapsed.Contains(source)) desired.AddRange(source.Children);
                }
                else if (FlyleafQueueSearch.Matches(source, searchText))
                {
                    desired.Add(source); desired.AddRange(source.Children);
                    matches += Math.Max(1, source.Children.Count);
                }
                else
                {
                    // Keep the parent as context, even when only its children match.
                    var children = source.Children.Where(child => FlyleafQueueSearch.Matches(child, searchText)).ToArray();
                    if (children.Length == 0) continue;
                    desired.Add(source); desired.AddRange(children); matches += children.Length;
                }
            }
        SearchCount.Text = IsSearching ? $"{matches} matching" : "";
        NoSearchResults.Visibility = IsSearching && matches == 0 ? Visibility.Visible : Visibility.Collapsed;
        var selection = Items.SelectedItems.Cast<object>().ToArray();
        var desiredSet = desired.ToHashSet();
        for (int i = rows.Count - 1; i >= 0; i--) if (!desiredSet.Contains(rows[i])) rows.RemoveAt(i);
        var retained = rows.ToHashSet();
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < rows.Count && ReferenceEquals(rows[i], desired[i])) continue;
            var existing = retained.Contains(desired[i]) ? rows.IndexOf(desired[i]) : -1;
            if (existing >= 0) rows.Move(existing, i); else rows.Insert(i, desired[i]);
        }
        foreach (var item in selection) if (rows.Contains(item) && !Items.SelectedItems.Contains(item)) Items.SelectedItems.Add(item);
        collapsed.RemoveWhere(source => queue?.Sources.Contains(source) != true);
        UpdateSelection();
    }
    void UpdateSelection() => RemoveSelection.IsEnabled = Items.SelectedItems.OfType<FlyleafQueueSource>().Any();
    void Items_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    void Expand_Click(object sender, RoutedEventArgs e)
    {
        if (!IsSearching && sender is Button { Tag: FlyleafQueueSource source })
        {
            if (!collapsed.Add(source)) collapsed.Remove(source);
            ((Button)sender).Content = collapsed.Contains(source) ? "›" : "⌄";
            RefreshRows(); e.Handled = true;
        }
    }
    void PlayItem(object? item)
    {
        if (item is FlyleafQueueSource source) queue?.Open(source);
        else if (item is FlyleafQueueChild child) queue?.Open(child.Source, child);
    }
    static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null) { if (element is T match) return match; element = VisualTreeHelper.GetParent(element); }
        return null;
    }
    void Remove_Click(object sender, RoutedEventArgs e) => queue?.Remove(Items.SelectedItems.OfType<FlyleafQueueSource>().ToArray());
    void Items_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { Remove_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Enter) { PlayItem(Items.SelectedItem); e.Handled = true; }
    }
    void Items_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragSource = null; deferredSelection = false;
        if (e.OriginalSource is not DependencyObject original || Ancestor<ButtonBase>(original) != null) return;
        var row = Ancestor<ListBoxItem>(original);
        if (row != null && e.ClickCount == 2)
        {
            Items.SelectedItem = row.DataContext;
            PlayItem(row.DataContext); e.Handled = true; return;
        }
        dragSource = IsSearching ? null : row?.DataContext as FlyleafQueueSource;
        dragStart = e.GetPosition(Items);
        // Keep an existing multiple selection intact until this is a click rather than a drag.
        if (dragSource != null && row!.IsSelected && Keyboard.Modifiers == ModifierKeys.None)
        {
            deferredSelection = true; e.Handled = true;
            row.Focus();
        }
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (deferredSelection && dragSource != null) { Items.SelectedItems.Clear(); Items.SelectedItem = dragSource; }
        deferredSelection = false; dragSource = null;
        base.OnPreviewMouseLeftButtonUp(e);
    }
    void Items_MouseMove(object sender, MouseEventArgs e)
    {
        if (IsSearching || dragSource == null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(Items);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var moving = Items.SelectedItems.OfType<FlyleafQueueSource>().ToArray();
        if (!moving.Contains(dragSource)) moving = [dragSource];
        dragSource = null; deferredSelection = false;
        DragDrop.DoDragDrop(Items, new DataObject(typeof(FlyleafQueueSource[]), moving), DragDropEffects.Move);
    }
    void Items_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(FlyleafQueueSource[])))
            e.Effects = !IsSearching && e.Data.GetData(typeof(FlyleafQueueSource[])) is FlyleafQueueSource[] moving && moving.All(s => queue?.Sources.Contains(s) == true) ? DragDropEffects.Move : DragDropEffects.None;
        else e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.OriginalSource is DependencyObject original && Ancestor<ListBoxItem>(original) is { } row) row.BringIntoView();
        e.Handled = true;
    }
    void Items_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(FlyleafQueueSource[])) is FlyleafQueueSource[] moving)
        {
            if (IsSearching || queue == null || moving.Any(s => !queue.Sources.Contains(s))) return;
            var row = e.OriginalSource is DependencyObject original ? Ancestor<ListBoxItem>(original) : null;
            var before = row?.DataContext is FlyleafQueueChild child ? child.Source : row?.DataContext as FlyleafQueueSource;
            if (before != null && row != null && e.GetPosition(row).Y > row.ActualHeight / 2)
            {
                var index = queue.Sources.IndexOf(before);
                before = index + 1 < queue.Sources.Count ? queue.Sources[index + 1] : null;
            }
            queue.MoveBefore(moving, before);
        }
        else if (Context?.MediaElement is { } view)
        {
            var inputs = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (inputs == null && e.Data.GetData(DataFormats.Text) is string input) inputs = [input];
            if (inputs == null) return;
            if (view.HasPlaylistActionHandler) view.RequestPlaylistAction(FlyleafPlaylistAction.AddInputs, inputs);
            else foreach (var value in inputs) queue?.Add(value);
        }
    }
    void Sort_Click(object sender, RoutedEventArgs e) => OpenToolbarMenu(CreateSortMenu());

    internal ContextMenu CreateSortMenu()
    {
        var menu = CreateToolbarMenu(SortQueueButton);
        AddSortChoices(menu);
        return menu;
    }

    void AddSortChoices(ItemsControl menu)
    {
        if (queue is not { Sources.Count: >= 2 } target) return;

        FlyleafMenuItems.QueueSort(menu, target, () => ReferenceEquals(queue, target));
    }

    void Shuffle_Click(object sender, RoutedEventArgs e) => ShuffleQueue();

    void ShuffleQueue()
    {
        queue?.ShuffleItems(); Context?.MediaElement?.ShowOSD("Queue shuffled");
    }
    void Add_Click(object sender, RoutedEventArgs e) => OpenToolbarMenu(CreateAddMenu());
    void Queue_Click(object sender, RoutedEventArgs e) => OpenToolbarMenu(CreateQueueMenu());

    internal ContextMenu CreateAddMenu()
    {
        var menu = CreateToolbarMenu(AddButton);
        AddImportChoices(menu);
        return menu;
    }

    internal ContextMenu CreateQueueMenu()
    {
        var menu = CreateToolbarMenu(QueueButton);
        AddQueueChoices(menu);
        return menu;
    }

    void AddQueueChoices(ItemsControl menu)
    {
        if (Context?.MediaElement is not { } view) return;

        view.AddQueueMenuItems(menu);
    }

    void Items_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = e.OriginalSource is DependencyObject element
            ? ItemsControl.ContainerFromElement(Items, element) as ListBoxItem : null;
        PopulateQueueMenu(row?.DataContext ?? (e.CursorLeft < 0 && e.CursorTop < 0 ? Items.SelectedItem : null));
    }

    internal void PopulateQueueMenu(object? rowItem = null)
    {
        var menu = Items.ContextMenu;
        menu.Items.Clear();
        menu.Resources = Context?.MediaElement?.Resources ?? Resources;
        menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");

        var target = queue;
        var item = rowItem;
        if (target != null && item != null)
        {
            var selection = Items.SelectedItems.Contains(rowItem)
                ? Items.SelectedItems.Cast<object>() : new[] { item };
            var removable = selection.OfType<FlyleafQueueSource>().ToArray();

            FlyleafMenuItems.Add(menu, "Play", target.HasPlayer ? () => PlayItem(item) : null).InputGestureText = "Enter";
            FlyleafMenuItems.Add(menu, "Remove selected", removable.Length > 0 ? () => target.Remove(removable) : null)
                .InputGestureText = "Delete";
            FlyleafMenuItems.Separator(menu);
        }

        if (Context?.MediaElement != null)
        {
            AddImportChoices(FlyleafMenuItems.Group(menu, "Add"));
            AddQueueChoices(FlyleafMenuItems.Group(menu, "Queue"));
        }

        if (target != null)
        {
            FlyleafMenuItems.Separator(menu);
            if (target.Sources.Count >= 2)
            {
                AddSortChoices(FlyleafMenuItems.Group(menu, "Sort"));
                FlyleafMenuItems.Add(menu, "Shuffle", ShuffleQueue);
                FlyleafMenuItems.Separator(menu);
            }
            FlyleafMenuItems.Add(menu, "Auto-next", () => target.AutoAdvance = !target.AutoAdvance, target.AutoAdvance).IsCheckable = true;
            FlyleafMenuItems.Add(menu, "Repeat queue", () => target.Repeat = !target.Repeat, target.Repeat).IsCheckable = true;
        }
    }

    void AddImportChoices(ItemsControl menu)
    {
        Context?.MediaElement?.AddAppendQueueMenuItems(menu);
    }

    void QueueMenu_Opened(object sender, RoutedEventArgs e) => queueMenuActivity ??= FlyleafPopupActivity.Hold(Context?.Player);

    void QueueMenu_Closed(object sender, RoutedEventArgs e)
    {
        queueMenuActivity?.Dispose();
        queueMenuActivity = null;
    }

    void Toolbar_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ToolbarDivider.Visibility = QueueTools.DesiredSize.Width + PlaybackTools.DesiredSize.Width <= e.NewSize.Width
            ? Visibility.Visible : Visibility.Hidden;

    ContextMenu CreateToolbarMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, Resources = Context?.MediaElement?.Resources ?? Resources };
        menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
        menu.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "FlyleafME.ThemeMenuItemStyle");
        return menu;
    }

    void OpenToolbarMenu(ContextMenu menu)
    {
        IDisposable? activity = null;
        menu.Opened += (_, _) => activity = FlyleafPopupActivity.Hold(Context?.Player);
        menu.Closed += (_, _) => { activity?.Dispose(); activity = null; };
        menu.IsOpen = true;
    }
}