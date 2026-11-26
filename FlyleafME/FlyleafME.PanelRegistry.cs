using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    /// <summary>Reports an edit committed by a settings editor. The host owns persistence.</summary>
    public event EventHandler<FlyleafLib.MediaPlayer.Player>? SettingsEdited;

    internal void NotifySettingsEdited(FlyleafLib.MediaPlayer.Player target)
    {
        if (!disposed && ReferenceEquals(Player, target) && !target.IsDisposed) SettingsEdited?.Invoke(this, target);
    }

    internal event EventHandler? PanelRegistryChanged;

    readonly Dictionary<string, FlyleafPanelDefinition> panelDefinitions = new(StringComparer.Ordinal);
    public IReadOnlyCollection<FlyleafPanelDefinition> RegisteredPanels => panelDefinitions.Values;

    public bool IsPanelRegistered(string id) => panelDefinitions.ContainsKey(id);

    /// <summary>Register the built-in panels. Register custom definitions first to override a built-in ID.</summary>
    public void RegisterDefaultPanels(Action<FlyleafSettingsPanel>? configureSettings = null)
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        Add(new("audio", "Audio", c => new FlyleafAudioPanel { Context = c }) { PreferredWidth = 340 });
        Add(new("playback", "Playback", c => new FlyleafPlaybackPanel { Context = c })
        { MinimumWidth = 300, PreferredWidth = 360, PreferredFloatingHeight = 680 });
        Add(new("subtitles", "Subtitles", c => new FlyleafSubtitlesPanel { Context = c }) { PreferredWidth = 340 });
        Add(new("information", "Media information", c => new FlyleafInformationPanel { Context = c })
        { MinimumWidth = 300, PreferredWidth = 360, PreferredFloatingHeight = 680, DefaultMode = FlyleafPanelMode.Docked });
        Add(new("video", "Video adjustments", c => new FlyleafVideoPanel { Context = c })
        { MinimumWidth = 300, PreferredWidth = 360, PreferredFloatingHeight = 680 });
        Add(new("queue", "Queue", c => new FlyleafQueuePanel { Context = c })
        { HandlesScrolling = true, MinimumWidth = 300, PreferredWidth = 360, PreferredFloatingHeight = 680, DefaultMode = FlyleafPanelMode.Docked });
        Add(new("settings", "Settings", c =>
        {
            var settings = new FlyleafSettingsPanel { Context = c };
            configureSettings?.Invoke(settings);
            return settings;
        })
        {
            MinimumWidth = 300, MaximumWidth = 1000, PreferredWidth = 760, PreferredFloatingHeight = 760,
            DefaultMode = FlyleafPanelMode.Overlay, FillOverlayArea = true, HandlesScrolling = true,
            AllowedModes = FlyleafPanelModes.Overlay | FlyleafPanelModes.Floating
        });

        void Add(FlyleafPanelDefinition definition)
        {
            if (!IsPanelRegistered(definition.Id)) RegisterPanel(definition);
        }
    }

    public void RegisterPanel(FlyleafPanelDefinition definition)
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        if (panelDefinitions.ContainsKey(definition.Id) || panels.ContainsKey(definition.Id))
            throw new InvalidOperationException($"Panel '{definition.Id}' is already registered.");
        panelDefinitions.Add(definition.Id, definition);
        PanelRegistryChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool UnregisterPanel(string id)
    {
        Dispatcher.VerifyAccess();
        if (!panelDefinitions.Remove(id)) return false;
        restoredPanelPlacements.Remove(id);
        if (panels.TryGetValue(id, out var panel))
        {
            ClosePanelCore(panel, false);
            panel.Context?.Detach();
            panel.OwnerElement = null;
            panels.Remove(id);
        }
        PanelRegistryChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public FlyleafPanel ShowPanel(string id, FlyleafPanelMode? mode = null, FlyleafPanelSide? side = null)
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsLoaded) throw new InvalidOperationException("Show panels after the media element is loaded.");
        if (!panelDefinitions.TryGetValue(id, out var definition)) throw new KeyNotFoundException($"Panel '{id}' is not registered.");
        restoredPanelPlacements.TryGetValue(id, out var placement);
        panels.TryGetValue(id, out var cached);
        var requestedMode = mode ?? cached?.Mode ?? placement?.Mode ?? definition.DefaultMode;
        var requestedSide = side ?? cached?.Side ?? placement?.Side ?? definition.DefaultSide;
        if (!definition.Supports(requestedMode, requestedSide)) throw new InvalidOperationException("This panel does not support that presentation or side.");
        if (!panels.TryGetValue(id, out var panel))
        {
            var context = new FlyleafPanelContext(this, id);
            try
            {
                var editor = definition.CreateContent(context) ?? throw new InvalidOperationException("Panel content factory returned null.");
                panel = new FlyleafPanel(definition, editor);
                panel.AttachContext(this, context);
                if (placement != null)
                {
                    panel.Mode = placement.Mode;
                    panel.Side = placement.Side;
                    panel.PreferredWidth = placement.Width;
                    panel.PinnedInFullscreen = placement.PinnedInFullscreen;
                    var bounds = placement.FloatingBounds;
                    if (bounds != null && FloatingPanelBounds.IsValid(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                        definition.MinimumWidth, definition.MinimumFloatingHeight))
                        panel.FloatingBounds = new Rect(bounds.Left, bounds.Top, Math.Min(bounds.Width, definition.MaximumWidth), bounds.Height);
                    restoredPanelPlacements.Remove(id);
                }
                panels.Add(id, panel);
            }
            catch { context.Detach(); throw; }
        }
        ShowPanelCore(panel, requestedMode, requestedSide, restoreDockWidth: placement != null);
        return panel;
    }

    public void ClosePanel(string id)
    {
        Dispatcher.VerifyAccess();
        if (panels.TryGetValue(id, out var panel)) ClosePanel(panel);
    }

    /// <summary>Close an open panel, or open it using its last presentation.</summary>
    public void TogglePanel(string id)
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (panels.TryGetValue(id, out var panel) && panel.IsOpen) ClosePanel(panel);
        else ShowPanel(id);
    }

    void RefreshPanelContexts()
    {
        foreach (var panel in panels.Values.ToArray()) panel.Context?.Refresh();
    }

    void RestorePanelFocus(FlyleafPanel panel)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (disposed || !IsLoaded || !panel.IsOpen || panel.OwnerElement != this) return;
            if (panel.LastEditorFocus?.TryGetTarget(out var target) == true &&
                target is UIElement { IsVisible: true, IsEnabled: true } element && panel.IsAncestorOf(element))
                Keyboard.Focus(target);
            else if (!panel.Editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))) panel.Focus();
            UpdateHostKeyBindings();
        }));
    }

    /// <summary>Focus the visible playback window after pending UI work, while this player is active.</summary>
    public void FocusPlayback()
    {
        Dispatcher.VerifyAccess();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (disposed || !IsLoaded || IsQueueSearchOpen || overlayPanel?.IsOpen == true || panels.Values.Any(panel => panel.IsOpen && panel.IsKeyboardFocusWithin)) return;
            var owner = Window.GetWindow(this);
            if (owner?.IsActive != true && Host.Overlay?.IsActive != true && Host.Surface?.IsActive != true) return;
            // Keyboard input is routed by the child windows, not the main owner.
            // Respect Surface-only bindings rather than always choosing Overlay.
            if ((KeyBindings is AvailableWindows.Overlay or AvailableWindows.Both) && Host.Overlay?.IsVisible == true) Host.Overlay.Focus();
            else if ((KeyBindings is AvailableWindows.Surface or AvailableWindows.Both) && Host.Surface?.IsVisible == true) Host.Surface.Focus();
            UpdateHostKeyBindings();
        }));
    }
}
