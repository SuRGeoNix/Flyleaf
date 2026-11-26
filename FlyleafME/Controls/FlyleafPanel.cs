using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Flyleaf.Controls.WPF;

public enum FlyleafPanelMode { Overlay, Docked, Floating }
public enum FlyleafPanelSide { Left, Right }

/// <summary>A single editor instance that can move between presentation hosts.</summary>
public sealed class FlyleafPanel : Border
{
    internal FlyleafME? OwnerElement;
    internal Window? FloatingWindow;
    internal IDisposable? ActivityHold;
    internal Rect? FloatingBounds;
    public FlyleafPanelDefinition Definition { get; }
    public FlyleafPanelContext? Context { get; private set; }
    internal WeakReference<IInputElement>? LastEditorFocus;
    readonly Button placementButton = FlyleafPanelUI.Button("");
    readonly ToggleButton fullscreenPin = new() { Width = 28, Height = 28, Margin = new Thickness(0, 0, 4, 0) };
    ContextMenu? placementMenu;
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public FlyleafPanelMode Mode { get; internal set; }
    public FlyleafPanelSide Side { get; internal set; } = FlyleafPanelSide.Right;
    public bool IsOpen { get; internal set; }
    public bool PinnedInFullscreen { get; internal set; } = true;
    public FrameworkElement Editor { get; }
    double preferredWidth;
    public double PreferredWidth
    {
        get => preferredWidth;
        set
        {
            if (!double.IsFinite(value) || value < Definition.MinimumWidth || value > Definition.MaximumWidth)
                throw new ArgumentOutOfRangeException(nameof(value));
            preferredWidth = value;
        }
    }

    internal void AttachContext(FlyleafME owner, FlyleafPanelContext context)
    {
        var previousContext = Context;
        OwnerElement = owner;
        Context = context;
        // Preserve an app-supplied view model or DataContext binding.
        if (ReadLocalValue(DataContextProperty) == DependencyProperty.UnsetValue ||
            ReferenceEquals(ReadLocalValue(DataContextProperty), previousContext)) DataContext = context;
    }

    public FlyleafPanel(string id, string title, FrameworkElement editor)
        : this(new FlyleafPanelDefinition(id, title, _ => editor), editor) { }

    internal FlyleafPanel(FlyleafPanelDefinition definition, FrameworkElement editor)
    {
        definition.Validate();
        ArgumentNullException.ThrowIfNull(editor);
        Definition = definition;
        Editor = editor;
        Mode = definition.DefaultMode; Side = definition.DefaultSide;
        PreferredWidth = definition.PreferredWidth;
        Focusable = true;
        Editor.GotKeyboardFocus += (_, e) => LastEditorFocus = new WeakReference<IInputElement>(e.NewFocus);
        SetResourceReference(BackgroundProperty, "FlyleafME.SurfaceBrush");
        SetResourceReference(BorderBrushProperty, "FlyleafME.BorderBrush");
        BorderThickness = new Thickness(1);
        var layout = new DockPanel();
        var titleRow = new DockPanel();
        var close = FlyleafPanelUI.Button("×"); close.Padding = new Thickness(8, 1, 8, 1); close.MinHeight = 28; close.ToolTip = "Close panel";
        close.Click += (_, _) => OwnerElement?.ClosePanel(this);
        DockPanel.SetDock(close, Dock.Right);
        titleRow.Children.Add(close);
        placementButton.Margin = new Thickness(0, 0, 8, 0); placementButton.MinHeight = 28; placementButton.Padding = new Thickness(8, 4, 8, 4);
        placementButton.ToolTip = "Panel placement";
        DockPanel.SetDock(placementButton, Dock.Right); titleRow.Children.Add(placementButton);
        placementButton.Click += (_, _) => ShowPlacementMenu();
        fullscreenPin.SetResourceReference(StyleProperty, "FlyleafME.OverlayIconToggleStyle");
        fullscreenPin.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "FlyleafME.TextBrush");
        AutomationProperties.SetName(fullscreenPin, "Keep panel docked in fullscreen");
        fullscreenPin.Click += (_, _) => ToggleFullscreenPin();
        DockPanel.SetDock(fullscreenPin, Dock.Right); titleRow.Children.Add(fullscreenPin);
        var caption = new TextBlock { Text = Title, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush");
        titleRow.Children.Add(caption);
        titleRow.Background = System.Windows.Media.Brushes.Transparent;
        titleRow.MouseLeftButtonDown += (_, e) =>
        {
            if (Mode == FlyleafPanelMode.Floating && FloatingWindow is { } window &&
                e.LeftButton == MouseButtonState.Pressed)
            {
                window.DragMove();
                e.Handled = true;
            }
        };
        var header = new Border { Child = titleRow, Padding = new Thickness(12, 9, 10, 9), BorderThickness = new Thickness(0, 0, 0, 1) };
        header.SetResourceReference(BorderBrushProperty, "FlyleafME.BorderBrush");
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var editorPadding = new Thickness(12);
        if (Definition.HandlesScrolling)
        {
            if (editor is FlyleafSettingsPanel settings) settings.AlignPageScrollbar(editorPadding.Right);
            layout.Children.Add(new Border { Child = editor, Padding = editorPadding });
        }
        else layout.Children.Add(new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = editorPadding });
        Child = layout;
        RefreshPresentation();
        Unloaded += (_, _) => { if (placementMenu != null) placementMenu.IsOpen = false; placementMenu = null; };
        IsKeyboardFocusWithinChanged += (_, _) => OwnerElement?.UpdateHostKeyBindings();
        // Do not mark character KeyDown events handled: WPF needs them unhandled
        // to generate TextInput. Host shortcuts are disabled by the focus scope above.
        MouseMove += (_, _) => OwnerElement?.Player?.Activity.RefreshFullActive();
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        MouseEnter += (_, _) =>
        {
            if (IsOpen && Mode == FlyleafPanelMode.Docked)
                ActivityHold ??= FlyleafPopupActivity.Hold(OwnerElement?.Player);
        };
        MouseLeave += (_, _) =>
        {
            if (Mode == FlyleafPanelMode.Docked) { ActivityHold?.Dispose(); ActivityHold = null; }
        };
    }

    internal void RefreshPresentation()
    {
        placementButton.Content = (Mode == FlyleafPanelMode.Overlay && Definition.FillOverlayArea ? "Player area" : Mode.ToString()) + " ▾";
        fullscreenPin.Visibility = Mode == FlyleafPanelMode.Docked ? Visibility.Visible : Visibility.Collapsed;
        fullscreenPin.IsChecked = PinnedInFullscreen;
        fullscreenPin.Content = PinnedInFullscreen ? "\uE840" : "\uE718";
        fullscreenPin.ToolTip = PinnedInFullscreen ? "Auto-hide in fullscreen" : "Keep docked in fullscreen";
    }

    void ToggleFullscreenPin()
    {
        if (OwnerElement is not { } owner) return;
        bool pinned = fullscreenPin.IsChecked == true;
        // A mouse click on the pin shouldn't keep an unpinned panel open through keyboard focus.
        bool returnFocus = owner.IsFullScreen && !pinned && fullscreenPin.IsKeyboardFocused &&
            InputManager.Current.MostRecentInputDevice is MouseDevice;
        if (returnFocus) Keyboard.ClearFocus();
        owner.SetPanelFullscreenPin(this, pinned);
        if (returnFocus) owner.FocusPlayback();
    }

    void ShowPlacementMenu()
    {
        var menu = new ContextMenu { PlacementTarget = placementButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.ThemeContextMenuStyle");
        void Add(string label, FlyleafPanelMode mode, FlyleafPanelSide side)
        {
            if (!Definition.Supports(mode, side)) return;
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = Mode == mode && (mode == FlyleafPanelMode.Floating || Definition.FillOverlayArea && mode == FlyleafPanelMode.Overlay || Side == side) };
            item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (OwnerElement is not { IsLoaded: true } owner) return;
                try { owner.ShowPanel(this, mode, side); }
                catch (InvalidOperationException ex) { MessageBox.Show(ex.Message, "Panel placement", MessageBoxButton.OK, MessageBoxImage.Information); }
            }));
            menu.Items.Add(item);
        }
        if (Definition.FillOverlayArea) Add("Player area", FlyleafPanelMode.Overlay, Definition.DefaultSide);
        else { Add("Overlay left", FlyleafPanelMode.Overlay, FlyleafPanelSide.Left); Add("Overlay right", FlyleafPanelMode.Overlay, FlyleafPanelSide.Right); }
        Add("Dock left", FlyleafPanelMode.Docked, FlyleafPanelSide.Left); Add("Dock right", FlyleafPanelMode.Docked, FlyleafPanelSide.Right);
        Add("Floating window", FlyleafPanelMode.Floating, FlyleafPanelSide.Right);
        placementMenu = menu; menu.IsOpen = true;
    }
}
