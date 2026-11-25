using System.Windows;
using System.Windows.Media;

using Microsoft.Win32;

namespace Flyleaf.Controls.WPF;

public enum FlyleafTheme
{
    Dark,
    Light,
    System
}

public enum FlyleafAccent
{
    Blue,
    Violet,
    Teal,
    Amber,
    Firebrick,
    Rose,
    Orange,
    Emerald,
    Slate,
    Custom
}

public static class FlyleafThemeManager
{
    const string ThemePath = "pack://application:,,,/FlyleafME;component/Themes/";

    public static Color GetAccentColor(FlyleafTheme theme, FlyleafAccent accent)
        => GetAccentColor(theme, accent, FlyleafAppearanceOptions.DefaultCustomAccentColor);

    public static Color GetAccentColor(FlyleafTheme theme, FlyleafAccent accent, string customAccentColor)
        => GetAccentColors(Resolve(theme), accent, customAccentColor).normal;

    public static FlyleafTheme Resolve(FlyleafTheme theme)
    {
        if (theme != FlyleafTheme.System)
            return theme;

        try
        {
            const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            return Registry.GetValue(key, "AppsUseLightTheme", 0) is int value && value != 0
                ? FlyleafTheme.Light : FlyleafTheme.Dark;
        }
        catch
        {
            return FlyleafTheme.Dark;
        }
    }

    public static void Apply(ResourceDictionary resources, FlyleafTheme theme, FlyleafAccent accent = FlyleafAppearanceOptions.DefaultAccent)
        => Apply(resources, theme, accent, FlyleafAppearanceOptions.DefaultCustomAccentColor);

    public static void Apply(ResourceDictionary resources, FlyleafTheme theme, FlyleafAccent accent, string customAccentColor)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var actualTheme = Resolve(theme);
        var palette = new ResourceDictionary
        {
            Source = new Uri($"{ThemePath}{actualTheme}.xaml", UriKind.Absolute)
        };

        var (normal, hover, onAccent) = GetAccentColors(actualTheme, accent, customAccentColor);
        palette["FlyleafME.AccentBrush"] = new SolidColorBrush(normal);
        palette["FlyleafME.AccentHoverBrush"] = new SolidColorBrush(hover);
        palette["FlyleafME.OnAccentBrush"] = new SolidColorBrush(onAccent);
        var volumeGradient = new LinearGradientBrush(normal, hover, new Point(0, 0.5), new Point(1, 0.5));
        volumeGradient.Freeze();
        palette["FlyleafME.PlaybackVolumeBrush"] = volumeGradient;
        palette["FlyleafME.LogoMarkBrush"] = new SolidColorBrush(normal);
        palette["FlyleafME.LogoFoldBrush"] = new SolidColorBrush(Color.FromRgb((byte)(normal.R * 0.68), (byte)(normal.G * 0.68), (byte)(normal.B * 0.68)));
        var surface = ((SolidColorBrush)palette["FlyleafME.SurfaceBrush"]).Color;
        const double selectionTint = 0.20;
        palette["FlyleafME.SelectionBrush"] = new SolidColorBrush(Color.FromRgb(
            (byte)(surface.R + (normal.R - surface.R) * selectionTint),
            (byte)(surface.G + (normal.G - surface.G) * selectionTint),
            (byte)(surface.B + (normal.B - surface.B) * selectionTint)));

        var dictionaries = resources.MergedDictionaries;
        int index = -1;

        for (int i = 0; i < dictionaries.Count; i++)
        {
            var path = dictionaries[i].Source?.OriginalString;
            if (path != null && path.Contains("FlyleafME;component/Themes/", StringComparison.OrdinalIgnoreCase) &&
                (path.EndsWith("/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith("/Light.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
            dictionaries[index] = palette;
        else
            dictionaries.Insert(0, palette);
    }

    static (Color normal, Color hover, Color onAccent) GetAccentColors(FlyleafTheme theme, FlyleafAccent accent, string customAccentColor)
    {
        bool light = theme == FlyleafTheme.Light;

        return accent switch
        {
            FlyleafAccent.Custom => GetCustomAccentColors(customAccentColor),
            // Keep the user's original Firebrick color exact in both themes.
            FlyleafAccent.Firebrick => light
                ? (Color.FromRgb(178, 34, 34), Color.FromRgb(142, 27, 27), Colors.White)
                : (Color.FromRgb(178, 34, 34), Color.FromRgb(202, 57, 57), Colors.White),
            FlyleafAccent.Rose => light
                ? (Color.FromRgb(190, 24, 93), Color.FromRgb(157, 23, 77), Colors.White)
                : (Color.FromRgb(244, 114, 182), Color.FromRgb(249, 168, 212), Color.FromRgb(55, 18, 37)),
            FlyleafAccent.Orange => light
                ? (Color.FromRgb(194, 65, 12), Color.FromRgb(154, 52, 18), Colors.White)
                : (Color.FromRgb(251, 146, 60), Color.FromRgb(253, 186, 116), Color.FromRgb(53, 26, 8)),
            FlyleafAccent.Emerald => light
                ? (Color.FromRgb(4, 120, 87), Color.FromRgb(6, 95, 70), Colors.White)
                : (Color.FromRgb(52, 211, 153), Color.FromRgb(110, 231, 183), Color.FromRgb(8, 47, 36)),
            FlyleafAccent.Slate => light
                ? (Color.FromRgb(71, 85, 105), Color.FromRgb(51, 65, 85), Colors.White)
                : (Color.FromRgb(203, 213, 225), Color.FromRgb(226, 232, 240), Color.FromRgb(24, 32, 44)),
            FlyleafAccent.Violet => light
                ? (Color.FromRgb(110, 64, 183), Color.FromRgb(90, 48, 159), Colors.White)
                : (Color.FromRgb(181, 142, 250), Color.FromRgb(203, 172, 255), Color.FromRgb(27, 19, 43)),
            FlyleafAccent.Teal => light
                ? (Color.FromRgb(0, 116, 117), Color.FromRgb(0, 94, 95), Colors.White)
                : (Color.FromRgb(82, 213, 199), Color.FromRgb(116, 230, 216), Color.FromRgb(13, 39, 39)),
            FlyleafAccent.Amber => light
                ? (Color.FromRgb(153, 91, 0), Color.FromRgb(125, 75, 0), Colors.White)
                : (Color.FromRgb(250, 183, 83), Color.FromRgb(255, 202, 121), Color.FromRgb(43, 29, 9)),
            _ => light
                ? (Color.FromRgb(36, 104, 197), Color.FromRgb(27, 87, 171), Colors.White)
                : (Color.FromRgb(112, 165, 245), Color.FromRgb(140, 181, 247), Color.FromRgb(16, 23, 37))
        };
    }

    static (Color normal, Color hover, Color onAccent) GetCustomAccentColors(string customAccentColor)
    {
        var rgb = (Color)ColorConverter.ConvertFromString(customAccentColor);
        var normal = Color.FromRgb(rgb.R, rgb.G, rgb.B);
        double luminance = 0.2126 * normal.ScR + 0.7152 * normal.ScG + 0.0722 * normal.ScB;
        var onAccent = luminance >= 0.179 ? Colors.Black : Colors.White;
        // Prefer the direction that increases text contrast, with room to show a hover on near-black and near-white accents.
        int target = luminance is < 0.06 or (>= 0.179 and < 0.78) ? 255 : 0;
        byte Tint(byte component) => (byte)Math.Round(component + (target - component) * 0.18);
        return (normal, Color.FromRgb(Tint(normal.R), Tint(normal.G), Tint(normal.B)), onAccent);
    }
}
