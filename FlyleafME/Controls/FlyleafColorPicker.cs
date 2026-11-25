using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Flyleaf.Controls.WPF;

public static class FlyleafColorPicker
{
    sealed record DialogOwner(IntPtr Handle) : Forms.IWin32Window;

    /// <summary>Shows the Windows color picker and returns the chosen RGB with the original opacity, or null if canceled.</summary>
    public static Color? Pick(Window? owner, Color current)
    {
        using var picker = new Forms.ColorDialog
        {
            Color = Drawing.Color.FromArgb(current.R, current.G, current.B),
            FullOpen = true
        };
        var handle = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
        var result = handle == IntPtr.Zero ? picker.ShowDialog() : picker.ShowDialog(new DialogOwner(handle));
        if (result != Forms.DialogResult.OK) return null;

        var chosen = picker.Color;
        return Color.FromArgb(current.A, chosen.R, chosen.G, chosen.B);
    }

    internal static Button Swatch(string title, out Border chip)
    {
        chip = new Border { Width = 14, Height = 14, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
        var button = FlyleafPanelUI.Button("");
        button.Content = chip;
        button.Width = 30;
        button.Padding = new Thickness(4);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.Margin = new Thickness(6, 0, 0, 0);
        var label = $"Choose {title.ToLowerInvariant()}";
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
        return button;
    }
}
