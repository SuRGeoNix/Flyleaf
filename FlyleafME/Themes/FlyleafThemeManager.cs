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
        => GetBaseAccentColor(accent, customAccentColor);

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

        Color normal = GetBaseAccentColor(accent, customAccentColor);
        double luminance = RelativeLuminance(normal);
        Color hover = GetAccentHover(actualTheme, accent, normal, luminance);
        Color onAccent = luminance >= 0.179 ? Colors.Black : Colors.White;
        var accentBrush = new SolidColorBrush(normal);
        palette["FlyleafME.AccentBrush"] = accentBrush;
        palette["FlyleafME.AccentHoverBrush"] = new SolidColorBrush(hover);
        palette["FlyleafME.OnAccentBrush"] = new SolidColorBrush(onAccent);
        Color played = DarkenPlaybackFill(normal);
        palette["FlyleafME.PlaybackPlayedBrush"] = new SolidColorBrush(played);
        // Slate's darker fill is close to the default Dark buffered gray.
        if (actualTheme == FlyleafTheme.Dark && accent == FlyleafAccent.Slate)
            palette["FlyleafME.PlaybackBufferedBrush"] = new SolidColorBrush(Color.FromRgb(149, 162, 177));
        var volumeGradient = new LinearGradientBrush(played, DarkenPlaybackFill(hover), new Point(0, 0.5), new Point(1, 0.5));
        volumeGradient.Freeze();
        palette["FlyleafME.PlaybackVolumeBrush"] = volumeGradient;
        palette["FlyleafME.LogoMarkBrush"] = new SolidColorBrush(normal);
        palette["FlyleafME.LogoFoldBrush"] = new SolidColorBrush(Color.FromRgb((byte)(normal.R * 0.68), (byte)(normal.G * 0.68), (byte)(normal.B * 0.68)));
        var surface = ((SolidColorBrush)palette["FlyleafME.SurfaceBrush"]).Color;
        const double selectionTint = 0.20;
        var selection = Color.FromRgb(
            (byte)(surface.R + (normal.R - surface.R) * selectionTint),
            (byte)(surface.G + (normal.G - surface.G) * selectionTint),
            (byte)(surface.B + (normal.B - surface.B) * selectionTint));
        palette["FlyleafME.SelectionBrush"] = new SolidColorBrush(selection);
        var overlay = ((SolidColorBrush)palette["FlyleafME.OverlayBrush"]).Color;
        var accentForeground = new SolidColorBrush(GetAccentForeground(actualTheme, normal, surface, selection, overlay));
        palette["FlyleafME.AccentForegroundBrush"] = accentForeground;
        palette["FlyleafME.AccentIconBrush"] = actualTheme == FlyleafTheme.Dark && accent == FlyleafAccent.Firebrick
            ? accentBrush
            : accentForeground;

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

    static Color DarkenPlaybackFill(Color color)
    {
        const double brightness = 0.64;
        return Color.FromRgb((byte)(color.R * brightness), (byte)(color.G * brightness), (byte)(color.B * brightness));
    }

    static Color GetBaseAccentColor(FlyleafAccent accent, string customAccentColor)
    {
        if (accent == FlyleafAccent.Custom)
        {
            var rgb = (Color)ColorConverter.ConvertFromString(customAccentColor);
            return Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }
        return accent switch
        {
            FlyleafAccent.Firebrick => Color.FromRgb(0xB2, 0x22, 0x22),
            FlyleafAccent.Orange => Color.FromRgb(0xDA, 0x6A, 0x1C),
            FlyleafAccent.Amber => Color.FromRgb(0xB9, 0x82, 0x13),
            FlyleafAccent.Emerald => Color.FromRgb(0x15, 0x86, 0x5C),
            FlyleafAccent.Teal => Color.FromRgb(0x00, 0x86, 0x89),
            FlyleafAccent.Violet => Color.FromRgb(0x8F, 0x62, 0xCD),
            FlyleafAccent.Rose => Color.FromRgb(0xC7, 0x4F, 0x86),
            FlyleafAccent.Slate => Color.FromRgb(0x6D, 0x80, 0x99),
            _ => Color.FromRgb(0x35, 0x7A, 0xD5)
        };
    }

    static Color GetAccentHover(FlyleafTheme theme, FlyleafAccent accent, Color normal, double luminance)
    {
        if (accent == FlyleafAccent.Firebrick)
            return theme == FlyleafTheme.Light ? Color.FromRgb(142, 27, 27) : Color.FromRgb(202, 57, 57);
        // Prefer the direction that increases text contrast, with room to show a hover on near-black and near-white accents.
        var target = luminance is < 0.06 or (>= 0.179 and < 0.78) ? Colors.White : Colors.Black;
        return Blend(normal, target, 0.18);
    }

    static Color GetAccentForeground(FlyleafTheme theme, Color accent, Color surface, Color selection, Color overlay)
    {
        bool light = theme == FlyleafTheme.Light;
        Color target = light ? Colors.Black : Colors.White;
        double surfaceLuminance = RelativeLuminance(surface);
        double selectionLuminance = RelativeLuminance(selection);
        // Check text on solid surfaces and icons on the overlay over the least favorable video backdrop.
        double overlayLuminance = RelativeLuminance(Blend(target, overlay, overlay.A / 255.0));
        double limit = light
            ? Math.Min((Math.Min(surfaceLuminance, selectionLuminance) + 0.05) / 4.5, (overlayLuminance + 0.05) / 3) - 0.05
            : Math.Max((Math.Max(surfaceLuminance, selectionLuminance) + 0.05) * 4.5, (overlayLuminance + 0.05) * 3) - 0.05;
        for (int step = 0; step < 20; step++)
        {
            Color candidate = Blend(accent, target, step / 20.0);
            double luminance = RelativeLuminance(candidate);
            if (light ? luminance <= limit : luminance >= limit) return candidate;
        }
        return target;
    }

    static double RelativeLuminance(Color color) => 0.2126 * color.ScR + 0.7152 * color.ScG + 0.0722 * color.ScB;

    static Color Blend(Color from, Color to, double amount)
    {
        byte Mix(byte start, byte end) => (byte)Math.Round(start + (end - start) * amount);
        return Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }
}
