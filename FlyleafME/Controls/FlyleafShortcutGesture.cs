using System.Windows.Input;
using FlyleafLib.MediaPlayer;
using PlayerKeyBinding = FlyleafLib.MediaPlayer.KeyBinding;

namespace Flyleaf.Controls.WPF;

public sealed record FlyleafShortcutGesture(Key Key, bool Alt = false, bool Ctrl = false, bool Shift = false, bool IsKeyUp = false)
{
    public static FlyleafShortcutGesture From(PlayerKeyBinding binding) => new(binding.Key, binding.Alt, binding.Ctrl, binding.Shift, binding.IsKeyUp);
    internal void Apply(PlayerKeyBinding binding)
    {
        binding.Key = Key; binding.Alt = Alt; binding.Ctrl = Ctrl; binding.Shift = Shift; binding.IsKeyUp = IsKeyUp;
    }
    public override string ToString()
    {
        if (Key == Key.None) return "Unassigned";
        // WPF may format these keys using their Prior and Next aliases.
        var name = Key switch
        {
            Key.PageUp => nameof(Key.PageUp),
            Key.PageDown => nameof(Key.PageDown),
            _ => new KeyConverter().ConvertToString(Key)
        };
        return (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + name;
    }

    internal PlayerKeyBinding? Conflict(IEnumerable<PlayerKeyBinding> bindings, PlayerKeyBinding? except = null)
        => Key == Key.None ? null : bindings.FirstOrDefault(b => b != except && b.Key == Key && b.Alt == Alt && b.Ctrl == Ctrl && b.Shift == Shift);
}
