using System.Windows.Media;

namespace Flyleaf.Controls.WPF;

public enum FlyleafOSDPosition { TopLeft, TopRight, BottomLeft, BottomRight }
/// <summary>WPF text-subtitle appearance; does not restyle bitmap subtitles.</summary>
public sealed class FlyleafSubtitleOptions : NotifyPropertyChanged
{
    string family = "Segoe UI", color = "#FFFFFFFF", outlineColor = "#FF000000";
    double size = 48, outline = 1, bottom = 48;
    bool bold = true, italic, withinVideo = true;
    public string FontFamily { get => family; set { if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("Choose a font family."); Set(ref family, value.Trim()); } }
    /// <summary>Font size at a 1920-DIP-wide playback area; scales with the view.</summary>
    public double FontSize { get => size; set => Set(ref size, FlyleafTextValues.Number(value, 8, 160)); }
    public bool Bold { get => bold; set => Set(ref bold, value); }
    public bool Italic { get => italic; set => Set(ref italic, value); }
    public string Color { get => color; set => Set(ref color, FlyleafTextValues.Color(value)); }
    public string OutlineColor { get => outlineColor; set => Set(ref outlineColor, FlyleafTextValues.Color(value)); }
    public double OutlineThickness { get => outline; set => Set(ref outline, FlyleafTextValues.Number(value, 0, 12)); }
    public double BottomOffset { get => bottom; set => Set(ref bottom, FlyleafTextValues.Number(value, 0, 500)); }
    public bool WithinVideo { get => withinVideo; set => Set(ref withinVideo, value); }
    public FlyleafSubtitleOptions Clone() => new() { FontFamily = FontFamily, FontSize = FontSize, Bold = Bold, Italic = Italic, Color = Color, OutlineColor = OutlineColor, OutlineThickness = OutlineThickness, BottomOffset = BottomOffset, WithinVideo = WithinVideo };
}
public sealed class FlyleafOSDOptions : NotifyPropertyChanged
{
    bool enabled = true;
    bool status = true, errors = true, showMilliseconds = true;
    FlyleafVisibilityPolicy timeDisplay = FlyleafVisibilityPolicy.Auto, queueDisplay = FlyleafVisibilityPolicy.Auto;
    double duration = 3.5, size = 16;
    FlyleafOSDPosition position = FlyleafOSDPosition.TopRight, clockPosition = FlyleafOSDPosition.TopLeft;
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public bool ShowStatus { get => status; set => Set(ref status, value); }
    public bool ShowErrors { get => errors; set => Set(ref errors, value); }
    public FlyleafVisibilityPolicy TimeDisplay { get => timeDisplay; set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref timeDisplay, value); } }
    public bool ShowMilliseconds { get => showMilliseconds; set => Set(ref showMilliseconds, value); }
    public FlyleafVisibilityPolicy QueueDisplay { get => queueDisplay; set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref queueDisplay, value); } }
    public double DurationSeconds { get => duration; set => Set(ref duration, FlyleafTextValues.Number(value, 0.5, 15)); }
    public double FontSize { get => size; set => Set(ref size, FlyleafTextValues.Number(value, 10, 48)); }
    public FlyleafOSDPosition Position { get => position; set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref position, value); } }
    public FlyleafOSDPosition ClockPosition { get => clockPosition; set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref clockPosition, value); } }
    public FlyleafOSDOptions Clone() => new() { Enabled = Enabled, ShowStatus = ShowStatus, ShowErrors = ShowErrors, TimeDisplay = TimeDisplay, ShowMilliseconds = ShowMilliseconds, QueueDisplay = QueueDisplay, DurationSeconds = DurationSeconds, FontSize = FontSize, Position = Position, ClockPosition = ClockPosition };
}
internal static class FlyleafTextValues
{
    internal static double Number(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max ? value : throw new ArgumentOutOfRangeException(nameof(value), $"Enter a value from {min} to {max}.");
    internal static string Color(string value)
    {
        if (value == null || value.Length is not (7 or 9) || value[0] != '#' || !value.AsSpan(1).ToString().All(Uri.IsHexDigit)) throw new ArgumentException("Use #RRGGBB or #AARRGGBB.");
        return value.ToUpperInvariant();
    }
    internal static Brush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush;
    }
}
