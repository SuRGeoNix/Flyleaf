using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

/// <summary>Settings branding uses core-library metadata, rather than sample-app or hardcoded version text.</summary>
internal sealed class FlyleafSettingsBranding : Border
{
    static readonly Assembly library = typeof(Player).Assembly;
    internal static string LibraryVersion { get; } = library.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? library.GetName().Version?.ToString(3) ?? "";
    internal static string Copyright { get; } = library.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    internal FlyleafSettingsBranding(bool compact)
    {
        var body = new StackPanel
        {
            Orientation = compact ? Orientation.Horizontal : Orientation.Vertical,
            HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Center
        };
        body.Children.Add(new FlyleafLogo
        {
            Width = compact ? 28 : 64, Height = compact ? 28 : 64,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = compact ? new Thickness(0, 0, 10, 0) : new Thickness(0, 0, 0, 6)
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        void Line(string value, double size, bool secondary)
        {
            if (value.Length == 0) return;
            var label = new TextBlock
            {
                Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
                TextAlignment = compact ? TextAlignment.Left : TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "FlyleafME.SecondaryTextBrush" : "FlyleafME.TextBrush");
            text.Children.Add(label);
        }
        Line(compact ? "FlyleafLib · v" + LibraryVersion : "FlyleafLib", 12, false);
        if (!compact) Line("v" + LibraryVersion, 11, true);
        Line(Copyright, 10, true);
        body.Children.Add(text); Child = body;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(0, 10, 0, 0);
        Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(0, 16, 12, 4);
        SetResourceReference(BorderBrushProperty, "FlyleafME.BorderBrush");
        ToolTip = $"FlyleafLib {LibraryVersion}\n{Copyright}";
    }
}
