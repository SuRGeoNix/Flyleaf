using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    const string QueueSearchAction = "control.queue.search";
    Popup? queueSearchPopup;
    FlyleafQuickQueueSearch? quickQueueSearch;
    // Popup.IsOpen can be coerced to false until the popup has loaded for the first time.
    bool queueSearchActive;
    bool IsQueueSearchOpen => queueSearchActive;
    bool CanSearchQueue => !disposed && IsLoaded && Queue.HasPlayer && Queue.TotalItems > 1;

    /// <summary>The current configurable gesture, or null when unassigned.</summary>
    public string? QueueSearchShortcut => Player?.Config.Playback.KeyBindings.Keys
        .Where(binding => binding.Action == KeyBindingAction.Custom && binding.ActionName == QueueSearchAction && binding.Key != Key.None)
        .Select(binding => FlyleafShortcutGesture.From(binding).ToString()).FirstOrDefault();

    /// <summary>Open quick queue search without changing the queue panel's filter.</summary>
    public void ShowQueueSearch()
    {
        Dispatcher.VerifyAccess();
        if (!CanSearchQueue || !overlayRoot.IsLoaded) return;
        if (IsQueueSearchOpen) { if (queueSearchPopup?.IsOpen == true) quickQueueSearch!.FocusSearch(); return; }
        CloseGeneralMenu();
        if (queueSearchPopup == null)
        {
            quickQueueSearch = new FlyleafQuickQueueSearch { IsSearchShortcut = MatchesQueueSearchShortcut };
            quickQueueSearch.CloseRequested += () => CloseQueueSearch();
            quickQueueSearch.PlayRequested += PlayQueueSearchResult;
            queueSearchPopup = new Popup { Child = quickQueueSearch, PlacementTarget = overlayRoot,
                Placement = PlacementMode.Relative, StaysOpen = false, AllowsTransparency = true };
            queueSearchPopup.Opened += (_, _) =>
            {
                if (!IsQueueSearchOpen) { queueSearchPopup.IsOpen = false; return; }
                UpdateHostKeyBindings();
                quickQueueSearch.FocusSearch();
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (IsQueueSearchOpen && queueSearchPopup.IsOpen && !quickQueueSearch.IsKeyboardFocusWithin)
                        quickQueueSearch.FocusSearch();
                }));
            };
            queueSearchPopup.Closed += (_, _) => FinishQueueSearch();
            overlayRoot.Children.Add(queueSearchPopup);
            overlayRoot.SizeChanged += (_, _) => { if (IsQueueSearchOpen) PositionQueueSearch(); };
        }
        quickQueueSearch!.Attach(Queue);
        PositionQueueSearch();
        // Suppress shortcuts and pending playback focus requests before transferring keyboard focus.
        queueSearchActive = true;
        UpdateHostKeyBindings();
        // The native video surface can own the keyboard when the shortcut is invoked.
        if (Host.Overlay is { IsVisible: true } overlay && !overlay.IsActive) overlay.Focus();
        queueSearchPopup.IsOpen = true;
    }

    void PositionQueueSearch()
    {
        if (queueSearchPopup == null || quickQueueSearch == null) return;
        double width = Math.Min(520, Math.Max(1, overlayRoot.ActualWidth - 24));
        double top = (osdHeaderVisible ? overlayHeader.ActualHeight : 0) + 12;
        quickQueueSearch.Width = width;
        quickQueueSearch.Height = Math.Min(440, Math.Max(1, overlayRoot.ActualHeight - top - 12));
        queueSearchPopup.HorizontalOffset = Math.Max(0, (overlayRoot.ActualWidth - width) / 2);
        queueSearchPopup.VerticalOffset = top;
    }

    bool MatchesQueueSearchShortcut(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        return Player?.Config.Playback.KeyBindings.Keys.Any(binding => binding.Action == KeyBindingAction.Custom &&
            binding.ActionName == QueueSearchAction && binding.Key != Key.None && binding.Key == key &&
            binding.Alt == modifiers.HasFlag(ModifierKeys.Alt) && binding.Ctrl == modifiers.HasFlag(ModifierKeys.Control) &&
            binding.Shift == modifiers.HasFlag(ModifierKeys.Shift) && !modifiers.HasFlag(ModifierKeys.Windows)) == true;
    }

    void CloseQueueSearch()
    {
        if (queueSearchPopup == null) return;
        bool wasOpen = queueSearchPopup.IsOpen;
        queueSearchPopup.IsOpen = false;
        // Cancelling a deferred first open may not raise Closed.
        if (!wasOpen) FinishQueueSearch();
    }

    void FinishQueueSearch()
    {
        if (!queueSearchActive) return;
        queueSearchActive = false;
        quickQueueSearch!.Detach();
        if (!disposed)
        {
            UpdateHostKeyBindings();
            if (IsLoaded) FocusPlayback();
        }
    }

    void PlayQueueSearchResult(object item)
    {
        if (!CanSearchQueue) { CloseQueueSearch(); return; }
        // Queue descriptors may have changed while the debounced refresh was pending.
        if (item is FlyleafQueueSource source && Queue.Sources.Contains(source))
        {
            CloseQueueSearch(); Queue.Open(source);
        }
        else if (item is FlyleafQueueChild child && Queue.Sources.Contains(child.Source) && child.Source.Children.Contains(child))
        {
            CloseQueueSearch(); Queue.Open(child.Source, child);
        }
    }
}
