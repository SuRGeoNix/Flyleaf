using System.Collections.ObjectModel;
using System.Windows.Input;
using FlyleafLib.MediaPlayer;
using PlayerKeyBinding = FlyleafLib.MediaPlayer.KeyBinding;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    /// <summary>Default gestures for the control's presentation actions, independent of application actions.</summary>
    public static IReadOnlyDictionary<string, FlyleafShortcutGesture> ControlKeybindingDefaults => FlyleafControlKeybindings.Defaults;

    /// <summary>Creates detached default gestures for both core and composed-control actions.</summary>
    public static KeysConfig CreateDefaultKeybindings()
    {
        var keys = new KeysConfig();
        keys.LoadDefault();
        FlyleafControlKeybindings.Attach(keys, _ => { }, addDefaultGestures: true);
        return keys;
    }

    void RegisterControlKeybindings(Player target)
    {
        // The saved bindings may outlive this control. Do not retain the control
        // through the player's custom-action delegates.
        var owner = new WeakReference<FlyleafME>(this);
        var player = new WeakReference<Player>(target);
        FlyleafControlKeybindings.Attach(target.Config.Playback.KeyBindings, id =>
        {
            if (!owner.TryGetTarget(out var control) || !player.TryGetTarget(out var expected) || control.Dispatcher.HasShutdownStarted) return;
            void Execute()
            {
                if (control.disposed || !control.IsLoaded || control.Player != expected || expected.IsDisposed) return;
                switch (id)
                {
                    case "control.subtitles.up": control.MoveSubtitlesUp(); break;
                    case "control.subtitles.down": control.MoveSubtitlesDown(); break;
                    case "control.subtitles.larger": control.IncreaseSubtitleTextSize(); break;
                    case "control.subtitles.smaller": control.DecreaseSubtitleTextSize(); break;
                    case "control.navigation.previous": control.PreviousItem(); break;
                    case "control.navigation.next": control.NextItem(); break;
                    case "control.navigation.previousPage": control.PreviousItems(); break;
                    case "control.navigation.nextPage": control.NextItems(); break;
                    case "control.queue.autoAdvance": control.ToggleQueueAutoAdvance(); break;
                    case "control.queue.search": control.ShowQueueSearch(); break;
                    case "control.playbackBar.pin" when control.UIConfig.PlaybackBar is
                        { Design: FlyleafPlaybackBarDesign.Modern, Mode: FlyleafPlaybackBarMode.Dynamic } options:
                        options.DynamicPinned = !options.DynamicPinned;
                        break;
                }
            }
            if (control.Dispatcher.CheckAccess()) Execute(); else control.Dispatcher.BeginInvoke(new Action(Execute));
        }, addDefaultGestures: !target.Config.Loaded);
    }

    public void ToggleQueueAutoAdvance()
    {
        Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        Queue.AutoAdvance = !Queue.AutoAdvance;
    }

    public void MoveSubtitlesUp() => AdjustSubtitleText(false, 2);
    public void MoveSubtitlesDown() => AdjustSubtitleText(false, -2);
    public void IncreaseSubtitleTextSize() => AdjustSubtitleText(true, 2);
    public void DecreaseSubtitleTextSize() => AdjustSubtitleText(true, -2);

    void AdjustSubtitleText(bool size, double delta)
    {
        Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        var options = UIConfig.Subtitles;
        if (size)
        {
            options.FontSize = Math.Clamp(options.FontSize + delta, 8, 160);
            ShowOSD($"Subtitle size {options.FontSize:0.###}");
        }
        else
        {
            options.BottomOffset = Math.Clamp(options.BottomOffset + delta, 0, 500);
            ShowOSD($"Subtitle bottom offset {options.BottomOffset:0.###}");
        }
    }
}

internal static class FlyleafControlKeybindings
{
    internal static readonly IReadOnlyDictionary<string, FlyleafShortcutGesture> Defaults = new ReadOnlyDictionary<string, FlyleafShortcutGesture>(new Dictionary<string, FlyleafShortcutGesture>
    {
        ["control.subtitles.up"] = new(Key.Up, Alt: true),
        ["control.subtitles.down"] = new(Key.Down, Alt: true),
        ["control.subtitles.larger"] = new(Key.Right, Alt: true),
        ["control.subtitles.smaller"] = new(Key.Left, Alt: true),
        ["control.navigation.previous"] = new(Key.Up, Ctrl: true, IsKeyUp: true),
        ["control.navigation.next"] = new(Key.Down, Ctrl: true, IsKeyUp: true),
        ["control.navigation.previousPage"] = new(Key.PageUp, Ctrl: true, IsKeyUp: true),
        ["control.navigation.nextPage"] = new(Key.PageDown, Ctrl: true, IsKeyUp: true),
        ["control.queue.autoAdvance"] = new(Key.F5, IsKeyUp: true),
        ["control.queue.search"] = new(Key.F, Ctrl: true, IsKeyUp: true),
        ["control.playbackBar.pin"] = new(Key.B, Ctrl: true, IsKeyUp: true)
    });

    internal static void Attach(KeysConfig keys, Action<string> execute, bool addDefaultGestures)
    {
        foreach (var (id, gesture) in Defaults)
        {
            // Preserve the existing core navigation gestures while moving their
            // implementation to the control's queue/collection navigation.
            KeyBindingAction? legacy = id switch { "control.navigation.previous" => KeyBindingAction.OpenPrevItem, "control.navigation.next" => KeyBindingAction.OpenNextItem, _ => null };
            if (legacy != null)
                foreach (var binding in keys.Keys.Where(b => b.Action == legacy))
                { binding.Action = KeyBindingAction.Custom; binding.ActionName = id; }
            var existing = keys.Keys.Where(b => b.Action == KeyBindingAction.Custom && b.ActionName == id).ToArray();
            if (existing.Length == 0)
            {
                var binding = new PlayerKeyBinding { Action = KeyBindingAction.Custom, ActionName = id };
                // AddCustom overwrites a matching gesture (including None). Append
                // distinct unassigned rows if defaults are disabled or occupied.
                // Seed newer shortcuts in older configs only when their row is absent.
                bool seedDefault = addDefaultGestures || id is "control.queue.autoAdvance" or "control.queue.search" or "control.playbackBar.pin";
                (seedDefault && gesture.Conflict(keys.Keys) == null ? gesture : new FlyleafShortcutGesture(Key.None)).Apply(binding);
                keys.Keys.Add(binding); existing = [binding];
            }
            // Migrate only the previous default, without replacing custom gestures or an occupied Ctrl+F.
            if (id == "control.queue.search")
                foreach (var binding in existing)
                    if (FlyleafShortcutGesture.From(binding) == new FlyleafShortcutGesture(Key.F3, IsKeyUp: true) && gesture.Conflict(keys.Keys, binding) == null)
                        gesture.Apply(binding);
            foreach (var binding in existing) binding.SetAction(() => execute(id), binding.IsKeyUp);
        }
    }
}
