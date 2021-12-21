using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Forms.Integration;
using System.Windows.Input;

using FlyleafLib.Controls;
using FlyleafLib.Controls.WPF;

namespace FlyleafLib.MediaPlayer
{
    partial class Player
    {
        /* Player Key Bindings
         * 
         * Config.Player.KeyBindings.Enabled
         * Config.Player.KeyBindings.FlyleafWindow
         * Config.Player.KeyBindings.Keys
         * 
         * KeyDown / KeyUp Events (Control / WinFormsHost / WindowFront (FlyleafWindow))
         * Exposes KeyDown/KeyUp if required to listen on additional Controls/Windows
         * Allows KeyBindingAction.Custom to set an external Action for Key Binding
         */

        /// <summary>
        /// Can be used to route KeyUp events (WPF)
        /// </summary>
        /// <param name="player"></param>
        /// <param name="e"></param>
        public void KeyUp(Player player, KeyEventArgs e)
        {
            e.Handled = KeyUp(player, e.Key);
        }

        /// <summary>
        /// Can be used to route KeyDown events (WPF)
        /// </summary>
        /// <param name="player"></param>
        /// <param name="e"></param>
        public void KeyDown(Player player, KeyEventArgs e)
        {
            e.Handled = KeyDown(player, e.Key);
        }

        /// <summary>
        /// Can be used to route KeyUp events (WinForms)
        /// </summary>
        /// <param name="player"></param>
        /// <param name="e"></param>
        public void KeyUp(Player player, System.Windows.Forms.KeyEventArgs e)
        {
            e.Handled = KeyUp(player, KeyInterop.KeyFromVirtualKey((int)e.KeyCode));
        }

        /// <summary>
        /// Can be used to route KeyDown events (WinForms)
        /// </summary>
        /// <param name="player"></param>
        /// <param name="e"></param>
        public void KeyDown(Player player, System.Windows.Forms.KeyEventArgs e)
        {
            e.Handled = KeyDown(player, KeyInterop.KeyFromVirtualKey((int)e.KeyCode));
        }

        private static bool ctrlWasDown;
        private static bool KeyUp(Player player, Key key)
        {
            if (key == Key.LeftCtrl || key == Key.RightCtrl) return false;

            if (player.Config.Player.ActivityMode)
                player.Activity.KeyboardTimestmap = DateTime.UtcNow.Ticks;

            foreach (var binding in player.Config.Player.KeyBindings.Keys)
                if (binding.Key == key && binding.Ctrl == ctrlWasDown)
                {
                    if (!isKeyUpBinding.Contains(binding.Action))
                        return false;

                    ctrlWasDown = false;

                    //player.Log(binding.Action.ToString());
                    binding.ActionInternal?.Invoke();

                    return true;
                }

            return false;
        }
        private static bool KeyDown(Player player, Key key)
        {
            if (key == Key.LeftCtrl || key == Key.RightCtrl) return false;

            foreach(var binding in player.Config.Player.KeyBindings.Keys)
                if (binding.Key == key && binding.Ctrl == (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
                {
                    if (isKeyUpBinding.Contains(binding.Action))
                    {
                        ctrlWasDown = binding.Ctrl;
                        return false;
                    }

                    //player.Log(binding.Action.ToString());
                    binding.ActionInternal?.Invoke();
                    
                    return true;
                }

            return false;
        }

        private void WindowFront_KeyUp(object sender, KeyEventArgs e)
        {
            if (!Config.Player.KeyBindings.Enabled || !Config.Player.KeyBindings.FlyleafWindow) return;

            //Log("WindowFront_KeyUp");
            KeyUp(((FlyleafWindow)sender).VideoView.Player, e);
        }
        private void WindowFront_KeyDown(object sender, KeyEventArgs e)
        {
            if (!Config.Player.KeyBindings.Enabled || !Config.Player.KeyBindings.FlyleafWindow) return;

            //Log("WindowFront_KeyDown");
            KeyDown(((FlyleafWindow)sender).VideoView.Player, e);
        }

        private void WinFormsHost_KeyUp(object sender, KeyEventArgs e)
        {
            if (!Config.Player.KeyBindings.Enabled) return;

            //Log("WinFormsHost_KeyUp");
            KeyUp(((Flyleaf)((WindowsFormsHost)sender).Child).Player, e);
        }
        private void WinFormsHost_KeyDown(object sender, KeyEventArgs e)
        {
            if (!Config.Player.KeyBindings.Enabled) return;

            //Log("WinFormsHost_KeyDown");
            KeyDown(((Flyleaf)((WindowsFormsHost)sender).Child).Player, e);
        }

        private void Control_KeyUp(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (!Config.Player.KeyBindings.Enabled) return;

            //Log("Control_KeyUp");
            KeyUp(((Flyleaf)sender).Player, e);
        }
        private void Control_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (!Config.Player.KeyBindings.Enabled) return;

            //Log("Control_KeyDown");
            KeyDown(((Flyleaf)sender).Player, e);
        }

        internal Action GetKeyBindingAction(KeyBindingAction action)
        {
            switch (action)
            {
                case KeyBindingAction.ActivityForceIdle:
                    return Activity.ForceIdle;

                case KeyBindingAction.AudioDelayAdd:
                    return Audio.DelayAdd;
                case KeyBindingAction.AudioDelayRemove:
                    return Audio.DelayRemove;
                case KeyBindingAction.AudioDelayAdd2:
                    return Audio.DelayAdd2;
                case KeyBindingAction.AudioDelayRemove2:
                    return Audio.DelayRemove2;
                case KeyBindingAction.ToggleAudio:
                    return Audio.Toggle;
                case KeyBindingAction.ToggleMute:
                    return Audio.ToggleMute;
                case KeyBindingAction.VolumeUp:
                    return Audio.VolumeUp;
                case KeyBindingAction.VolumeDown:
                    return Audio.VolumeDown;

                case KeyBindingAction.ToggleVideo:
                    return Video.Toggle;
                case KeyBindingAction.ToggleKeepRatio:
                    return Video.ToggleKeepRatio;
                case KeyBindingAction.ToggleVideoAcceleration:
                    return Video.ToggleVideoAcceleration;

                case KeyBindingAction.SubtitlesDelayAdd:
                    return Subtitles.DelayAdd;
                case KeyBindingAction.SubtitlesDelayRemove:
                    return Subtitles.DelayRemove;
                case KeyBindingAction.SubtitlesDelayAdd2:
                    return Subtitles.DelayAdd2;
                case KeyBindingAction.SubtitlesDelayRemove2:
                    return Subtitles.DelayRemove2;
                case KeyBindingAction.ToggleSubtitles:
                    return Subtitles.Toggle;

                case KeyBindingAction.OpenFromClipboard:
                    return OpenFromClipboard;

                case KeyBindingAction.OpenFromFileDialog:
                    return OpenFromFileDialog;

                case KeyBindingAction.CopyToClipboard:
                    return CopyToClipboard;

                case KeyBindingAction.Stop:
                    return Stop;

                case KeyBindingAction.Pause:
                    return Pause;

                case KeyBindingAction.Play:
                    return Play;

                case KeyBindingAction.TogglePlayPause:
                    return TogglePlayPause;

                case KeyBindingAction.TakeSnapshot:
                    return TakeSnapshot;

                case KeyBindingAction.NormalScreen:
                    return NormalScreen;

                case KeyBindingAction.FullScreen:
                    return FullScreen;

                case KeyBindingAction.ToggleFullScreen:
                    return ToggleFullScreen;

                case KeyBindingAction.ToggleRecording:
                    return ToggleRecording;

                case KeyBindingAction.ToggleReversePlayback:
                    return ToggleReversePlayback;

                case KeyBindingAction.SeekBackward:
                    return SeekBackward;

                case KeyBindingAction.SeekForward:
                    return SeekForward;

                case KeyBindingAction.SeekBackward2:
                    return SeekBackward2;

                case KeyBindingAction.SeekForward2:
                    return SeekForward2;

                case KeyBindingAction.SpeedAdd:
                    return SpeedUp;

                case KeyBindingAction.SpeedRemove:
                    return SpeedDown;

                case KeyBindingAction.ShowPrevFrame:
                    return ShowFramePrev;

                case KeyBindingAction.ShowNextFrame:
                    return ShowFrameNext;

                case KeyBindingAction.ZoomIn:
                    return ZoomIn;

                case KeyBindingAction.ZoomOut:
                    return ZoomOut;

                case KeyBindingAction.ResetAll:
                    return ResetAll;
            }

            return null;
        }
        private static HashSet<KeyBindingAction> isKeyUpBinding = new HashSet<KeyBindingAction>
        {
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
            { KeyBindingAction.Play },
            { KeyBindingAction.Pause },
            { KeyBindingAction.Stop },
            { KeyBindingAction.SpeedAdd },
            { KeyBindingAction.SpeedRemove },
            { KeyBindingAction.ActivityForceIdle }
        };
    }

    public enum KeyBindingAction
    {
        Custom,
        ActivityForceIdle,

        AudioDelayAdd, AudioDelayAdd2, AudioDelayRemove, AudioDelayRemove2, ToggleMute, VolumeUp, VolumeDown,
        SubtitlesDelayAdd, SubtitlesDelayAdd2, SubtitlesDelayRemove, SubtitlesDelayRemove2,

        CopyToClipboard, OpenFromClipboard, OpenFromFileDialog,
        Stop, Pause, Play, TogglePlayPause,
        TakeSnapshot,
        NormalScreen, FullScreen, ToggleFullScreen,

        ToggleAudio, ToggleVideo, ToggleSubtitles,

        ToggleKeepRatio,
        ToggleVideoAcceleration,
        ToggleRecording,
        ToggleReversePlayback,
        SeekForward, SeekBackward, SeekForward2, SeekBackward2,
        SpeedAdd, SpeedRemove,
        ShowNextFrame, ShowPrevFrame,

        ResetAll,
        ZoomIn, ZoomOut,
    }

    public class KeysConfig
    {
        /// <summary>
        /// Whether key bindings should be enabled or not
        /// </summary>
        public bool             Enabled         { get ; set; } = true;

        /// <summary>
        /// Whether to subscribe key bindings also to the front windows (WPF)
        /// </summary>
        public bool             FlyleafWindow   { get ; set; } = true;

        /// <summary>
        /// Currently configured key bindings
        /// (Normally you should not access this directly)
        /// </summary>
        public List<KeyBinding> Keys            { get ; set; }

        Player player;

        public KeysConfig() { }

        internal void SetPlayer(Player player)
        {
            if (Keys == null)
                Keys = new List<KeyBinding>();

            if (Enabled && Keys.Count == 0)
                LoadDefault();

            this.player = player;

            foreach(var binding in Keys)
            {
                if (binding.Action != KeyBindingAction.Custom)
                    binding.ActionInternal = player.GetKeyBindingAction(binding.Action);
            }
        }

        /// <summary>
        /// Gets a read-only copy of the currently assigned key bindings
        /// </summary>
        /// <returns></returns>
        public ReadOnlyCollection<KeyBinding> Get()
        {
            return Keys.AsReadOnly();
        }

        public void AddCustom(Key key, Action action, string actionName, bool ctrl = false)
        {
            foreach(var keyBinding in Keys)
                if (keyBinding.Ctrl == ctrl && keyBinding.Key == key)
                    throw new Exception($"Keybinding {(ctrl ? "Ctrl + " : "")}{key} already assigned");

            Keys.Add(new KeyBinding() { Ctrl = ctrl, Key = key, Action = KeyBindingAction.Custom, Custom = actionName, ActionInternal = action });
        }

        /// <summary>
        /// Adds a new key binding 
        /// </summary>
        /// <param name="key">The key to bind</param>
        /// <param name="action">Which action from the available to assign</param>
        /// <param name="ctrl">If Ctrl should be pressed</param>
        /// <exception cref="Exception"></exception>
        public void Add(Key key, KeyBindingAction action, bool ctrl = false)
        {
            foreach(var keyBinding in Keys)
                if (keyBinding.Ctrl == ctrl && keyBinding.Key == key)
                    throw new Exception($"Keybinding {(ctrl ? "Ctrl + " : "")}{key} already assigned");

            if (player == null)
                Keys.Add(new KeyBinding() { Ctrl = ctrl, Key = key, Action = action });
            else
            {
                Keys.Add(new KeyBinding() { Ctrl = ctrl, Key = key, Action = action, ActionInternal = player.GetKeyBindingAction(action) });
            }
        }

        /// <summary>
        /// Removes a binding based on Key/Ctrl combination
        /// </summary>
        /// <param name="key">The assigned key</param>
        /// <param name="ctrl">The assigned state of Ctrl</param>
        public void Remove(Key key, bool ctrl = false)
        {
            for (int i=Keys.Count-1; i >=0; i--)
                if (Keys[i].Ctrl == ctrl && Keys[i].Key == key)
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
        /// Removes all the bindings
        /// </summary>
        public void RemoveAll()
        {
            Keys.Clear();
        }

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
            Add(Key.OemOpenBrackets,    KeyBindingAction.AudioDelayRemove2, true);
            Add(Key.OemCloseBrackets,   KeyBindingAction.AudioDelayAdd);
            Add(Key.OemCloseBrackets,   KeyBindingAction.AudioDelayAdd2, true);

            Add(Key.OemSemicolon,       KeyBindingAction.SubtitlesDelayRemove);
            Add(Key.OemSemicolon,       KeyBindingAction.SubtitlesDelayRemove2, true);
            Add(Key.OemQuotes,          KeyBindingAction.SubtitlesDelayAdd);
            Add(Key.OemQuotes,          KeyBindingAction.SubtitlesDelayAdd2, true);

            Add(Key.V,                  KeyBindingAction.OpenFromClipboard, true);
            Add(Key.O,                  KeyBindingAction.OpenFromFileDialog);
            Add(Key.C,                  KeyBindingAction.CopyToClipboard, true);

            Add(Key.Left,               KeyBindingAction.SeekBackward);
            Add(Key.Right,              KeyBindingAction.SeekForward);

            Add(Key.OemPlus,            KeyBindingAction.SpeedAdd);
            Add(Key.OemMinus,           KeyBindingAction.SpeedRemove);

            Add(Key.F,                  KeyBindingAction.ToggleFullScreen);
            Add(Key.M,                  KeyBindingAction.ToggleMute);
            Add(Key.P,                  KeyBindingAction.TogglePlayPause);
            Add(Key.Space,              KeyBindingAction.TogglePlayPause);

            Add(Key.A,                  KeyBindingAction.ToggleAudio, true);
            Add(Key.S,                  KeyBindingAction.ToggleSubtitles, true);
            Add(Key.D,                  KeyBindingAction.ToggleVideo, true);
            Add(Key.H,                  KeyBindingAction.ToggleVideoAcceleration, true);

            Add(Key.T,                  KeyBindingAction.TakeSnapshot, true);
            Add(Key.R,                  KeyBindingAction.ToggleRecording, true);
            Add(Key.R,                  KeyBindingAction.ToggleKeepRatio);

            Add(Key.OemComma,           KeyBindingAction.ShowPrevFrame);
            Add(Key.OemPeriod,          KeyBindingAction.ShowNextFrame);
            Add(Key.Back,               KeyBindingAction.ToggleReversePlayback, true);

            Add(Key.Up,                 KeyBindingAction.VolumeUp);
            Add(Key.Down,               KeyBindingAction.VolumeDown);

            Add(Key.OemPlus,            KeyBindingAction.ZoomIn, true);
            Add(Key.OemMinus,           KeyBindingAction.ZoomOut, true);

            Add(Key.D0,                 KeyBindingAction.ResetAll, true);

            Add(Key.I,                  KeyBindingAction.ActivityForceIdle);
            Add(Key.Escape,             KeyBindingAction.NormalScreen);
        }
    }
    public class KeyBinding
    {
        public bool             Ctrl    { get; set; }
        public Key              Key     { get; set; }
        public KeyBindingAction Action  { get; set; }
        public string           Custom  { get; set; }

        /// <summary>
        /// Sets action for custom key binding
        /// </summary>
        /// <param name="action"></param>
        public void SetAction(Action action)
        {
            ActionInternal = action;
        }

        internal Action ActionInternal;
    }
}
