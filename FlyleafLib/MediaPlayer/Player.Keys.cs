using System.Runtime.InteropServices;
using System.Windows.Input;

namespace FlyleafLib.MediaPlayer;

partial class Player
{
    /* Player Key Bindings
     *
     * Config.Player.KeyBindings.Keys
     *
     * KeyDown / KeyUp Events (Control / WinFormsHost / WindowFront (FlyleafWindow))
     * Exposes KeyDown/KeyUp if required to listen on additional Controls/Windows
     * Allows KeyBindingAction.Custom to set an external Action for Key Binding
     */

    Tuple<KeyBinding,long> onKeyUpBinding;

    /// <summary>
    /// Can be used to route KeyDown events (WPF)
    /// </summary>
    /// <param name="player"></param>
    /// <param name="e"></param>
    public static bool KeyDown(Player player, KeyEventArgs e)
    {
        e.Handled = KeyDown(player, e.Key == Key.System ? e.SystemKey : e.Key);

        return e.Handled;
    }

    /// <summary>
    /// Can be used to route KeyDown events (WinForms)
    /// </summary>
    /// <param name="player"></param>
    /// <param name="e"></param>
    public static void KeyDown(Player player, System.Windows.Forms.KeyEventArgs e)
        => e.Handled = KeyDown(player, KeyInterop.KeyFromVirtualKey((int)e.KeyCode));

    /// <summary>
    /// Can be used to route KeyUp events (WPF)
    /// </summary>
    /// <param name="player"></param>
    /// <param name="e"></param>
    public static bool KeyUp(Player player, KeyEventArgs e)
    {
        e.Handled = KeyUp(player, e.Key == Key.System ? e.SystemKey : e.Key);

        return e.Handled;
    }

    /// <summary>
    /// Can be used to route KeyUp events (WinForms)
    /// </summary>
    /// <param name="player"></param>
    /// <param name="e"></param>
    public static void KeyUp(Player player, System.Windows.Forms.KeyEventArgs e)
        => e.Handled = KeyUp(player, KeyInterop.KeyFromVirtualKey((int)e.KeyCode));

    public static bool KeyDown(Player player, Key key)
    {
        if (player == null)
            return false;

        player.Activity.RefreshActive();

        if (player.onKeyUpBinding != null)
        {
            if (player.onKeyUpBinding.Item1.Key == key)
                return true;

            if (DateTime.UtcNow.Ticks - player.onKeyUpBinding.Item2 < TimeSpan.FromSeconds(2).Ticks)
                return false;

            player.onKeyUpBinding = null; // In case of keyboard lost capture (should be handled from hosts)
        }

        List<KeyBinding> keysList = new();
        var spanList = CollectionsMarshal.AsSpan(player.Config.Player.KeyBindings.Keys); // should create dictionary here with key+alt+ctrl+shift hash
        foreach(var binding in spanList)
            if (binding.Key == key)
                keysList.Add(binding);

        if (keysList.Count == 0)
            return false;

        bool alt, ctrl, shift;
        alt     = Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt);
        ctrl    = Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl);
        shift   = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        var spanList2 = CollectionsMarshal.AsSpan(keysList);
        foreach(var binding in spanList2)
        {
            if (binding.Alt == alt && binding.Ctrl == ctrl && binding.Shift == shift)
            {
                if (binding.IsKeyUp)
                    player.onKeyUpBinding = new(binding, DateTime.UtcNow.Ticks);
                else
                    ExecuteBinding(player, binding, false);

                return true;
            }
        }

        return false;
    }
    public static bool KeyUp(Player player, Key key)
    {
        if (player == null || player.onKeyUpBinding == null || player.onKeyUpBinding.Item1.Key != key)
            return false;

        ExecuteBinding(player, player.onKeyUpBinding.Item1, true);
        player.onKeyUpBinding = null;
        return true;
    }

    static void ExecuteBinding(Player player, KeyBinding binding, bool isKeyUp)
    {
        if (CanDebug) player.Log.Debug($"[Keys|{(isKeyUp ? "Up" : "Down")}] {(binding.Action == KeyBindingAction.Custom && binding.ActionName != null ? binding.ActionName : binding.Action)}");
        binding.ActionInternal?.Invoke();
    }

    internal Action GetKeyBindingAction(KeyBindingAction action)
        => keyBindingActions.GetValueOrDefault(action);
    Dictionary<KeyBindingAction, Action> keyBindingActions;
    void InitializeKeyBindingActions() => keyBindingActions = new()
    {
        [KeyBindingAction.ForceIdle]                = Activity.ForceIdle,
        [KeyBindingAction.ForceActive]              = Activity.ForceActive,
        [KeyBindingAction.ForceFullActive]          = Activity.ForceFullActive,

        [KeyBindingAction.AudioDelayAdd]            = Config.Audio.DelayAdd,
        [KeyBindingAction.AudioDelayRemove]         = Config.Audio.DelayRemove,
        [KeyBindingAction.AudioDelayAdd2]           = Config.Audio.DelayAdd2,
        [KeyBindingAction.AudioDelayRemove2]        = Config.Audio.DelayRemove2,
        [KeyBindingAction.ToggleAudio]              = Config.Audio.Toggle,
        [KeyBindingAction.ToggleMute]               = Config.Audio.ToggleMute,
        [KeyBindingAction.VolumeUp]                 = Config.Audio.VolumeUp,
        [KeyBindingAction.VolumeDown]               = Config.Audio.VolumeDown,

        [KeyBindingAction.TakeSnapshot]             = Commands.TakeSnapshotAction,
        [KeyBindingAction.NormalScreen]             = NormalScreen,
        [KeyBindingAction.FullScreen]               = FullScreen,
        [KeyBindingAction.ToggleFullScreen]         = ToggleFullScreen,
        [KeyBindingAction.ToggleRecording]          = ToggleRecording,
        [KeyBindingAction.ToggleVideo]              = Config.Video.Toggle,
        [KeyBindingAction.ToggleKeepRatio]          = Config.Video.ToggleKeepRatio,
        [KeyBindingAction.ToggleVideoAcceleration]  = Config.Video.ToggleVideoAcceleration,
        [KeyBindingAction.ZoomIn]                   = Config.Video.ZoomIn,
        [KeyBindingAction.ZoomOut]                  = Config.Video.ZoomOut,
        [KeyBindingAction.ShowPrevFrame]            = ShowFramePrev,
        [KeyBindingAction.ShowNextFrame]            = ShowFrameNext,

        [KeyBindingAction.SubtitlesDelayAdd]        = Config.Subtitles.DelayAdd,
        [KeyBindingAction.SubtitlesDelayRemove]     = Config.Subtitles.DelayRemove,
        [KeyBindingAction.SubtitlesDelayAdd2]       = Config.Subtitles.DelayAdd2,
        [KeyBindingAction.SubtitlesDelayRemove2]    = Config.Subtitles.DelayRemove2,
        [KeyBindingAction.ToggleSubtitles]          = Config.Subtitles.Toggle,

        [KeyBindingAction.OpenFromClipboard]        = OpenFromClipboard,
        [KeyBindingAction.OpenFromFileDialog]       = OpenFromFileDialog,
        [KeyBindingAction.OpenNextItem]             = OpenNextItem,
        [KeyBindingAction.OpenPrevItem]             = OpenPrevItem,
        [KeyBindingAction.CopyToClipboard]          = CopyToClipboard,
        [KeyBindingAction.CopyItemToClipboard]      = CopyItemToClipboard,

        [KeyBindingAction.Flush]                    = Flush,
        [KeyBindingAction.Stop]                     = Stop,
        [KeyBindingAction.Pause]                    = Pause,
        [KeyBindingAction.Play]                     = Play,
        [KeyBindingAction.TogglePlayPause]          = TogglePlayPause,
        [KeyBindingAction.ToggleReversePlayback]    = ToggleReversePlayback,
        [KeyBindingAction.ToggleLoopPlayback]       = ToggleLoopPlayback,

        [KeyBindingAction.ToggleSeekAccurate]       = ToggleSeekAccurate,
        [KeyBindingAction.SeekBackward]             = SeekBackward,
        [KeyBindingAction.SeekForward]              = SeekForward,
        [KeyBindingAction.SeekBackward2]            = SeekBackward2,
        [KeyBindingAction.SeekForward2]             = SeekForward2,
        [KeyBindingAction.SeekBackward3]            = SeekBackward3,
        [KeyBindingAction.SeekForward3]             = SeekForward3,
        [KeyBindingAction.SeekToStart]              = SeekToStart,
        [KeyBindingAction.SeekToEnd]                = SeekToEnd,

        [KeyBindingAction.SpeedAdd]                 = SpeedUp,
        [KeyBindingAction.SpeedAdd2]                = SpeedUp2,
        [KeyBindingAction.SpeedRemove]              = SpeedDown,
        [KeyBindingAction.SpeedRemove2]             = SpeedDown2,
        
        [KeyBindingAction.ResetAll]                 = ResetAll,
    };
}

public class KeysConfig
{
    /// <summary>
    /// Currently configured key bindings
    /// (Normally you should not access this directly)
    /// </summary>
    public List<KeyBinding> Keys            { get ; set; }

    Player player;

    public KeysConfig() { }

    public KeysConfig Clone()
    {
        KeysConfig keys = (KeysConfig) MemberwiseClone();
        keys.player     = null;
        keys.Keys       = null;

        return keys;
    }

    internal void SetPlayer(Player player)
    {
        Keys ??= [];

        if (!player.Config.Loaded && Keys.Count == 0)
            LoadDefault();

        foreach(var binding in Keys)
            if (binding.Action != KeyBindingAction.Custom)
                binding.ActionInternal = player.GetKeyBindingAction(binding.Action);

        this.player = player;
    }

    /// <summary>
    /// Adds a custom keybinding
    /// </summary>
    /// <param name="key">The key to bind</param>
    /// <param name="isKeyUp">If should fire on each keydown or just on keyup</param>
    /// <param name="action">The action to execute</param>
    /// <param name="actionName">A unique name to be able to identify it</param>
    /// <param name="alt">If Alt should be pressed</param>
    /// <param name="ctrl">If Ctrl should be pressed</param>
    /// <param name="shift">If Shift should be pressed</param>
    /// <exception cref="Exception">Keybinding already exists</exception>
    public void AddCustom(Key key, bool isKeyUp, Action action, string actionName, bool alt = false, bool ctrl = false, bool shift = false)
    {
        for (int i=0; i<Keys.Count; i++)
            if (Keys[i].Key == key && Keys[i].Alt == alt && Keys[i].Ctrl == ctrl && Keys[i].Shift == shift)
            {
                Keys[i].IsKeyUp         = isKeyUp;
                Keys[i].Action          = KeyBindingAction.Custom;
                Keys[i].ActionName      = actionName;
                Keys[i].ActionInternal  = action;

                return;
            }

        Keys.Add(new KeyBinding() { Alt = alt, Ctrl = ctrl, Shift = shift, Key = key, IsKeyUp = isKeyUp, Action = KeyBindingAction.Custom, ActionName = actionName, ActionInternal = action });
    }

    /// <summary>
    /// Adds a new key binding
    /// </summary>
    /// <param name="key">The key to bind</param>
    /// <param name="action">Which action from the available to assign</param>
    /// <param name="alt">If Alt should be pressed</param>
    /// <param name="ctrl">If Ctrl should be pressed</param>
    /// <param name="shift">If Shift should be pressed</param>
    /// <exception cref="Exception">Keybinding already exists</exception>
    public void Add(Key key, KeyBindingAction action, bool alt = false, bool ctrl = false, bool shift = false)
    {
        for (int i=0; i<Keys.Count; i++)
            if (Keys[i].Key == key && Keys[i].Alt == alt && Keys[i].Ctrl == ctrl && Keys[i].Shift == shift)
            {
                Keys[i].IsKeyUp         = isKeyUpBinding.Contains(action);
                Keys[i].Action          = action;
                Keys[i].ActionInternal  = player?.GetKeyBindingAction(action);

                return;
            }

        Keys.Add(new()
        {
            Alt     = alt,
            Ctrl    = ctrl,
            Shift   = shift,
            Key     = key,
            IsKeyUp = isKeyUpBinding.Contains(action),
            Action  = action,
            ActionInternal = player?.GetKeyBindingAction(action)
        });
    }

    public bool Exists(string actionName)
    {
        foreach (var keybinding in Keys)
            if (keybinding.ActionName == actionName)
                return true;

        return false;
    }

    public KeyBinding Get(string actionName)
    {
        foreach (var keybinding in Keys)
            if (keybinding.ActionName == actionName)
                return keybinding;

        return null;
    }

    /// <summary>
    /// Removes a binding based on Key/Ctrl combination
    /// </summary>
    /// <param name="key">The assigned key</param>
    /// <param name="alt">If Alt is assigned</param>
    /// <param name="ctrl">If Ctrl is assigned</param>
    /// <param name="shift">If Shift is assigned</param>
    public void Remove(Key key, bool alt = false, bool ctrl = false, bool shift = false)
    {
        for (int i=Keys.Count-1; i >=0; i--)
            if (Keys[i].Key == key && Keys[i].Alt == alt && Keys[i].Ctrl == ctrl && Keys[i].Shift == shift)
                Keys.RemoveAt(i);
    }

    /// <summary>
    /// Removes a binding based on assigned action
    /// </summary>
    /// <param name="action">The assigned action</param>
    public void Remove(KeyBindingAction action)
    {
        for (int i=Keys.Count-1; i >=0; i--)
            if (Keys[i].Action == action)
                Keys.RemoveAt(i);
    }

    /// <summary>
    /// Removes a binding based on assigned action's name
    /// </summary>
    /// <param name="actionName">The assigned action's name</param>
    public void Remove(string actionName)
    {
        for (int i=Keys.Count-1; i >=0; i--)
            if (Keys[i].ActionName == actionName)
                Keys.RemoveAt(i);
    }

    /// <summary>
    /// Removes all the bindings
    /// </summary>
    public void RemoveAll() => Keys.Clear();

    /// <summary>
    /// Resets to default bindings
    /// </summary>
    public void LoadDefault()
    {
        if (Keys == null)
            Keys = new List<KeyBinding>();
        else
            Keys.Clear();

        Add(Key.OemOpenBrackets,    KeyBindingAction.AudioDelayRemove);
        Add(Key.OemOpenBrackets,    KeyBindingAction.AudioDelayRemove2, false, true);
        Add(Key.OemCloseBrackets,   KeyBindingAction.AudioDelayAdd);
        Add(Key.OemCloseBrackets,   KeyBindingAction.AudioDelayAdd2, false, true);

        Add(Key.OemSemicolon,       KeyBindingAction.SubtitlesDelayRemove);
        Add(Key.OemSemicolon,       KeyBindingAction.SubtitlesDelayRemove2, false, true);
        Add(Key.OemQuotes,          KeyBindingAction.SubtitlesDelayAdd);
        Add(Key.OemQuotes,          KeyBindingAction.SubtitlesDelayAdd2, false, true);

        Add(Key.V,                  KeyBindingAction.OpenFromClipboard, false, true);
        Add(Key.O,                  KeyBindingAction.OpenFromFileDialog);
        Add(Key.Up,                 KeyBindingAction.OpenPrevItem, false, true);
        Add(Key.Down,               KeyBindingAction.OpenNextItem, false, true);
        Add(Key.C,                  KeyBindingAction.CopyToClipboard, false, true);
        Add(Key.C,                  KeyBindingAction.CopyItemToClipboard, false, false, true);

        Add(Key.Left,               KeyBindingAction.SeekBackward);
        Add(Key.Left,               KeyBindingAction.SeekBackward2, false, true);
        Add(Key.Right,              KeyBindingAction.SeekForward);
        Add(Key.Right,              KeyBindingAction.SeekForward2, false, true);
        Add(Key.PageUp,             KeyBindingAction.SeekBackward3);
        Add(Key.PageDown,           KeyBindingAction.SeekForward3);
        Add(Key.Home,               KeyBindingAction.SeekToStart);
        Add(Key.End,                KeyBindingAction.SeekToEnd);
        Add(Key.Left,               KeyBindingAction.ShowPrevFrame, false, false, true);
        Add(Key.Right,              KeyBindingAction.ShowNextFrame, false, false, true);

        Add(Key.Back,               KeyBindingAction.ToggleReversePlayback);
        Add(Key.S,                  KeyBindingAction.ToggleSeekAccurate, false, true);

        Add(Key.OemPlus,            KeyBindingAction.SpeedAdd);
        Add(Key.OemPlus,            KeyBindingAction.SpeedAdd2, false, false, true);
        Add(Key.OemMinus,           KeyBindingAction.SpeedRemove);
        Add(Key.OemMinus,           KeyBindingAction.SpeedRemove2, false, false, true);

        Add(Key.OemPlus,            KeyBindingAction.ZoomIn, false, true, false);
        Add(Key.OemMinus,           KeyBindingAction.ZoomOut, false, true, false);

        Add(Key.F,                  KeyBindingAction.ToggleFullScreen);

        Add(Key.P,                  KeyBindingAction.TogglePlayPause);
        Add(Key.Space,              KeyBindingAction.TogglePlayPause);
        Add(Key.MediaPlayPause,     KeyBindingAction.TogglePlayPause);
        Add(Key.Play,               KeyBindingAction.TogglePlayPause);

        Add(Key.A,                  KeyBindingAction.ToggleAudio, false, false, true);
        Add(Key.S,                  KeyBindingAction.ToggleSubtitles, false, false, true);
        Add(Key.V,                  KeyBindingAction.ToggleVideo, false, false, true);
        Add(Key.H,                  KeyBindingAction.ToggleVideoAcceleration, false, true);

        Add(Key.T,                  KeyBindingAction.TakeSnapshot, false, true);
        Add(Key.R,                  KeyBindingAction.ToggleRecording, false, true);
        Add(Key.R,                  KeyBindingAction.ToggleKeepRatio);

        Add(Key.M,                  KeyBindingAction.ToggleMute);
        Add(Key.Up,                 KeyBindingAction.VolumeUp);
        Add(Key.Down,               KeyBindingAction.VolumeDown);

        Add(Key.D0,                 KeyBindingAction.ResetAll);
        Add(Key.X,                  KeyBindingAction.Flush, false, true);

        Add(Key.I,                  KeyBindingAction.ForceIdle);
        Add(Key.Escape,             KeyBindingAction.NormalScreen);
        Add(Key.Q,                  KeyBindingAction.Stop, false, true, false);
    }

    static HashSet<KeyBindingAction> isKeyUpBinding = new()
    {   // TODO: Should Fire once one KeyDown and not again until KeyUp is fired (in case of Tasks keep track of already running actions?)
        // Having issues with alt/ctrl/shift (should save state of alt/ctrl/shift on keydown and not checked on keyup)

        { KeyBindingAction.OpenFromClipboard },
        { KeyBindingAction.OpenFromFileDialog },
        { KeyBindingAction.OpenNextItem },
        { KeyBindingAction.OpenPrevItem },
        { KeyBindingAction.CopyToClipboard },
        { KeyBindingAction.TakeSnapshot },
        { KeyBindingAction.NormalScreen },
        { KeyBindingAction.FullScreen },
        { KeyBindingAction.ToggleFullScreen },
        { KeyBindingAction.ToggleAudio },
        { KeyBindingAction.ToggleVideo },
        { KeyBindingAction.ToggleKeepRatio },
        { KeyBindingAction.ToggleVideoAcceleration },
        { KeyBindingAction.ToggleSubtitles },
        { KeyBindingAction.ToggleMute },
        { KeyBindingAction.TogglePlayPause },
        { KeyBindingAction.ToggleRecording },
        { KeyBindingAction.ToggleReversePlayback },
        { KeyBindingAction.ToggleLoopPlayback },
        { KeyBindingAction.Play },
        { KeyBindingAction.Pause },
        { KeyBindingAction.Stop },
        { KeyBindingAction.Flush },
        { KeyBindingAction.ToggleSeekAccurate },
        { KeyBindingAction.SeekToStart },
        { KeyBindingAction.SeekToEnd },
        { KeyBindingAction.SpeedAdd },
        { KeyBindingAction.SpeedAdd2 },
        { KeyBindingAction.SpeedRemove },
        { KeyBindingAction.SpeedRemove2 },
        { KeyBindingAction.ForceIdle },
        { KeyBindingAction.ForceActive },
        { KeyBindingAction.ForceFullActive },
        { KeyBindingAction.ResetAll }
    };
}
public class KeyBinding
{
    public bool             Alt             { get; set; }
    public bool             Ctrl            { get; set; }
    public bool             Shift           { get; set; }
    public Key              Key             { get; set; }
    public KeyBindingAction Action          { get; set; }
    public string           ActionName      { get => Action == KeyBindingAction.Custom ? actionName : Action.ToString(); set => actionName = value; }
    string actionName;
    public bool             IsKeyUp         { get; set; }

    /// <summary>
    /// Sets action for custom key binding
    /// </summary>
    /// <param name="action"></param>
    /// <param name="isKeyUp"></param>
    public void SetAction(Action action, bool isKeyUp)
    {
        ActionInternal  = action;
        IsKeyUp = isKeyUp;
    }

    internal Action ActionInternal;
}

public enum KeyBindingAction
{   // NOTE: To be able to support compatibility with previous config versions add new to end
    Custom,
    ForceIdle, ForceActive, ForceFullActive,

    AudioDelayAdd, AudioDelayAdd2, AudioDelayRemove, AudioDelayRemove2, ToggleMute, VolumeUp, VolumeDown,
    SubtitlesDelayAdd, SubtitlesDelayAdd2, SubtitlesDelayRemove, SubtitlesDelayRemove2,

    CopyToClipboard, CopyItemToClipboard, OpenFromClipboard, OpenFromFileDialog,
    Stop, Pause, Play, TogglePlayPause, ToggleReversePlayback, ToggleLoopPlayback, Flush,
    TakeSnapshot,
    NormalScreen, FullScreen, ToggleFullScreen,

    ToggleAudio, ToggleVideo, ToggleSubtitles,

    ToggleKeepRatio,
    ToggleVideoAcceleration,
    ToggleRecording,
    ToggleSeekAccurate, SeekForward, SeekBackward, SeekForward2, SeekBackward2, SeekForward3, SeekBackward3,
    SpeedAdd, SpeedAdd2, SpeedRemove, SpeedRemove2,
    ShowNextFrame, ShowPrevFrame,

    ResetAll,
    ZoomIn, ZoomOut,

    SeekToStart, SeekToEnd,
    OpenNextItem, OpenPrevItem,
}
