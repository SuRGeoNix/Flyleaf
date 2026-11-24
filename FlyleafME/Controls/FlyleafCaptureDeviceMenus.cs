using System.Windows.Controls;
using static Flyleaf.Controls.WPF.FlyleafMenuItems;

namespace Flyleaf.Controls.WPF;

/// <summary>Shared capture input choices using FlyleafLib's device enumeration and format URLs.</summary>
internal static class FlyleafCaptureDeviceMenus
{
    internal static void Populate(ItemsControl menu, bool video, Func<bool> canOpen, Action<string> open)
    {
        menu.Items.Clear();
        if (!canOpen()) { Add(menu, "Capture input unavailable", null); return; }
        if (!video && Engine.Config.DisableAudio) { Add(menu, "Audio engine is disabled", null); return; }

        void AddInput(ItemsControl parent, string label, string? url) =>
            Add(parent, label, string.IsNullOrWhiteSpace(url) ? null : () => { if (canOpen()) open(url); });

        try
        {
            if (video)
            {
                Engine.Video.RefreshCapDevices();
                foreach (var device in Engine.Video.CapDevices)
                {
                    if (device.Streams is not { Count: > 0 } formats)
                    {
                        AddInput(menu, device.FriendlyName, device.Url);
                        continue;
                    }

                    var group = Group(menu, device.FriendlyName);
                    if (!string.IsNullOrWhiteSpace(device.Url))
                    {
                        AddInput(group, "Default format", device.Url);
                        Separator(group);
                    }
                    foreach (var format in formats) AddInput(group, format.ToString(), format.Url);
                }
            }
            else
            {
                Engine.Audio.RefreshCapDevices();
                foreach (var device in Engine.Audio.CapDevices) AddInput(menu, device.FriendlyName, device.Url);
            }

            if (!menu.HasItems) Add(menu, "No capture devices found", null);
        }
        catch (Exception ex)
        {
            menu.Items.Clear();
            Add(menu, "Unable to enumerate capture devices", null).ToolTip = ex.Message;
        }
    }
}
