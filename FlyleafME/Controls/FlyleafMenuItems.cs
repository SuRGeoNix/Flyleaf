using System.Windows;
using System.Windows.Controls;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

internal static class FlyleafMenuItems
{
    internal static MenuItem Add(ItemsControl menu, string label, Action? action, bool selected = false)
    {
        var item = new MenuItem { Header = label, IsChecked = selected, IsEnabled = action != null };
        item.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.ThemeMenuItemStyle");
        if (action != null) item.Click += (_, _) => action();
        menu.Items.Add(item); return item;
    }
    internal static MenuItem Group(ItemsControl menu, string label)
    {
        var group = Add(menu, label, null); group.IsEnabled = true; return group;
    }
    internal static void Separator(ItemsControl menu)
    {
        var separator = new Separator();
        separator.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.ThemeMenuSeparatorStyle");
        menu.Items.Add(separator);
    }
    internal static void Speed(ItemsControl menu, Player target, Func<bool> isCurrent)
    {
        foreach (var value in FlyleafPlaybackActions.SpeedPresets)
            Add(menu, $"{value:0.##}×", FlyleafPlaybackActions.CanPlay(target) ? () =>
            {
                if (isCurrent() && FlyleafPlaybackActions.CanPlay(target)) target.Speed = value;
            } : null, Math.Abs(target.Speed - value) < 0.001);
    }
    internal static void Modes(ItemsControl menu, Player target, Func<bool> isCurrent)
    {
        Add(menu, "Repeat current item", !target.IsDisposed ? () =>
        {
            if (isCurrent() && !target.IsDisposed) target.LoopPlayback = !target.LoopPlayback;
        } : null, target.LoopPlayback);
        Add(menu, "Reverse video", (target.ReversePlayback || FlyleafPlaybackActions.CanReverse(target)) && !target.IsDisposed ? () =>
        {
            if (isCurrent() && !target.IsDisposed && (target.ReversePlayback || FlyleafPlaybackActions.CanReverse(target))) target.ReversePlayback = !target.ReversePlayback;
        } : null, target.ReversePlayback);
    }
}
