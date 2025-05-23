﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;

using static FlyleafLib.Logger;

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
        keys.player = null;
        keys.Keys = null;
        return keys;
    }

    internal void SetPlayer(Player player)
    {
        Keys ??= new List<KeyBinding>();

        if (!player.Config.Loaded && Keys.Count == 0)
            LoadDefault();

        this.player = player;

        foreach(var binding in Keys)
        {
            if (binding.Action != KeyBindingAction.Custom)
                binding.ActionInternal = GetKeyBindingAction(binding.Action);
        }
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
                Keys[i].IsKeyUp = isKeyUp;
                Keys[i].Action = KeyBindingAction.Custom;
                Keys[i].ActionName = actionName;
                Keys[i].ActionInternal = action;

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
                Keys[i].IsKeyUp = isKeyUpBinding.Contains(action);
                Keys[i].Action = action;
                Keys[i].ActionInternal = player != null ? GetKeyBindingAction(action) : null;

                return;
            }

        if (player == null)
            Keys.Add(new KeyBinding() { Alt = alt, Ctrl = ctrl, Shift = shift, Key = key, IsKeyUp = isKeyUpBinding.Contains(action), Action = action });
        else
            Keys.Add(new KeyBinding() { Alt = alt, Ctrl = ctrl, Shift = shift, Key = key, IsKeyUp = isKeyUpBinding.Contains(action), Action = action, ActionInternal = GetKeyBindingAction(action) });
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
        Add(Key.C,                  KeyBindingAction.CopyToClipboard, false, true);
        Add(Key.C,                  KeyBindingAction.CopyItemToClipboard, false, false, true);

        Add(Key.Left,               KeyBindingAction.SeekBackward);
        Add(Key.Left,               KeyBindingAction.SeekBackward2, false, true);
        Add(Key.Right,              KeyBindingAction.SeekForward);
        Add(Key.Right,              KeyBindingAction.SeekForward2, false, true);
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

    private Action GetKeyBindingAction(KeyBindingAction action)
    {
        switch (action)
        {
            case KeyBindingAction.ForceIdle:
                return player.Activity.ForceIdle;
            case KeyBindingAction.ForceActive:
                return player.Activity.ForceActive;
            case KeyBindingAction.ForceFullActive:
                return player.Activity.ForceFullActive;

            case KeyBindingAction.AudioDelayAdd:
                return player.Audio.DelayAdd;
            case KeyBindingAction.AudioDelayRemove:
                return player.Audio.DelayRemove;
            case KeyBindingAction.AudioDelayAdd2:
                return player.Audio.DelayAdd2;
            case KeyBindingAction.AudioDelayRemove2:
                return player.Audio.DelayRemove2;
            case KeyBindingAction.ToggleAudio:
                return player.Audio.Toggle;
            case KeyBindingAction.ToggleMute:
                return player.Audio.ToggleMute;
            case KeyBindingAction.VolumeUp:
                return player.Audio.VolumeUp;
            case KeyBindingAction.VolumeDown:
                return player.Audio.VolumeDown;

            case KeyBindingAction.ToggleVideo:
                return player.Video.Toggle;
            case KeyBindingAction.ToggleKeepRatio:
                return player.Video.ToggleKeepRatio;
            case KeyBindingAction.ToggleVideoAcceleration:
                return player.Video.ToggleVideoAcceleration;

            case KeyBindingAction.SubtitlesDelayAdd:
                return player.Subtitles.DelayAdd;
            case KeyBindingAction.SubtitlesDelayRemove:
                return player.Subtitles.DelayRemove;
            case KeyBindingAction.SubtitlesDelayAdd2:
                return player.Subtitles.DelayAdd2;
            case KeyBindingAction.SubtitlesDelayRemove2:
                return player.Subtitles.DelayRemove2;
            case KeyBindingAction.ToggleSubtitles:
                return player.Subtitles.Toggle;

            case KeyBindingAction.OpenFromClipboard:
                return player.OpenFromClipboard;

            case KeyBindingAction.OpenFromFileDialog:
                return player.OpenFromFileDialog;

            case KeyBindingAction.CopyToClipboard:
                return player.CopyToClipboard;

            case KeyBindingAction.CopyItemToClipboard:
                return player.CopyItemToClipboard;

            case KeyBindingAction.Flush:
                return player.Flush;

            case KeyBindingAction.Stop:
                return player.Stop;

            case KeyBindingAction.Pause:
                return player.Pause;

            case KeyBindingAction.Play:
                return player.Play;

            case KeyBindingAction.TogglePlayPause:
                return player.TogglePlayPause;

            case KeyBindingAction.TakeSnapshot:
                return player.Commands.TakeSnapshotAction;

            case KeyBindingAction.NormalScreen:
                return player.NormalScreen;

            case KeyBindingAction.FullScreen:
                return player.FullScreen;

            case KeyBindingAction.ToggleFullScreen:
                return player.ToggleFullScreen;

            case KeyBindingAction.ToggleRecording:
                return player.ToggleRecording;

            case KeyBindingAction.ToggleReversePlayback:
                return player.ToggleReversePlayback;

            case KeyBindingAction.ToggleLoopPlayback:
                return player.ToggleLoopPlayback;

            case KeyBindingAction.ToggleSeekAccurate:
                return player.ToggleSeekAccurate;

            case KeyBindingAction.SeekBackward:
                return player.SeekBackward;

            case KeyBindingAction.SeekForward:
                return player.SeekForward;

            case KeyBindingAction.SeekBackward2:
                return player.SeekBackward2;

            case KeyBindingAction.SeekForward2:
                return player.SeekForward2;

            case KeyBindingAction.SeekBackward3:
                return player.SeekBackward3;

            case KeyBindingAction.SeekForward3:
                return player.SeekForward3;

            case KeyBindingAction.SpeedAdd:
                return player.SpeedUp;

                case KeyBindingAction.SpeedAdd2:
                return player.SpeedUp2;

            case KeyBindingAction.SpeedRemove:
                return player.SpeedDown;

                case KeyBindingAction.SpeedRemove2:
                return player.SpeedDown2;

            case KeyBindingAction.ShowPrevFrame:
                return player.ShowFramePrev;

            case KeyBindingAction.ShowNextFrame:
                return player.ShowFrameNext;

            case KeyBindingAction.ZoomIn:
                return player.ZoomIn;

            case KeyBindingAction.ZoomOut:
                return player.ZoomOut;

            case KeyBindingAction.ResetAll:
                return player.ResetAll;
        }

        return null;
    }
    private static HashSet<KeyBindingAction> isKeyUpBinding = new()
    {
        // TODO: Should Fire once one KeyDown and not again until KeyUp is fired (in case of Tasks keep track of already running actions?)

        // Having issues with alt/ctrl/shift (should save state of alt/ctrl/shift on keydown and not checked on keyup)

        { KeyBindingAction.OpenFromClipboard },
        { KeyBindingAction.OpenFromFileDialog },
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
{
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
}
