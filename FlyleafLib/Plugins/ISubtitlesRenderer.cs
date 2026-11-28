using System.Drawing;
using FlyleafLib.MediaFramework.MediaFrame;

namespace FlyleafLib.Plugins;

/// <summary>Creates a renderer for the selected subtitle stream.</summary>
public interface ISubtitlesRenderer : IPlugin
{
    /// <summary>Returns null when the format or native runtime is unavailable.</summary>
    ISubtitlesRenderSession Open(SubtitlesRenderTrack track);
}

public interface ISubtitlesRenderSession : IDisposable
{
    /// <summary>Accepts an ASS event with timestamps on the media timeline, in milliseconds.</summary>
    void AddCue(string ass, long start, long duration);

    /// <summary>Renders positioned, straight-alpha BGRA rectangles. Return the same list until the pixels or positions change.</summary>
    IReadOnlyList<SubtitlesOverlayRect> Render(in SubtitlesRenderRequest request);

    /// <summary>Resets playback state after a discontinuity. Decoded events may be retained for seeks.</summary>
    void Seek();
}

/// <summary>A renderer session that sends parsed cues to Flyleaf's text display.</summary>
public interface ISubtitlesTextRenderSession : ISubtitlesRenderSession
{
    /// <summary>Parses a decoded cue, whose input can be an ASS event or plain text.</summary>
    string ParseCue(string text, bool isAss, out List<SubStyle> styles);

    void ISubtitlesRenderSession.AddCue(string ass, long start, long duration) => throw new NotSupportedException();
    IReadOnlyList<SubtitlesOverlayRect> ISubtitlesRenderSession.Render(in SubtitlesRenderRequest request) => throw new NotSupportedException();
    void ISubtitlesRenderSession.Seek() { }
}

public enum SubtitlesRenderMode { Text, Overlay, Bitmap }

/// <summary>The rendering path selected for the open subtitle track.</summary>
public sealed record SubtitlesRendererInfo(string Name, SubtitlesRenderMode Mode, Version PluginVersion = null, string FallbackReason = null);

public sealed record SubtitlesRenderTrack(string Codec, ReadOnlyMemory<byte> Header, IReadOnlyList<SubtitlesFont> Fonts);
public sealed record SubtitlesFont(string Name, ReadOnlyMemory<byte> Data);
public sealed record SubtitlesOverlayRect(int X, int Y, int Width, int Height, ReadOnlyMemory<byte> Pixels);

public readonly record struct SubtitlesRenderRequest(long Timestamp, int Width, int Height,
    int VideoWidth, int VideoHeight, SubtitlesRenderStyle Style)
{
    /// <summary>Full playback surface width in pixels, before clipping to the visible video.</summary>
    public int SurfaceWidth { get; init; }
}

/// <summary>Default appearance for subtitles converted from SubRip, with sizes relative to a 1920-DIP-wide view.</summary>
public sealed record SubtitlesRenderStyle(string FontFamily, double FontSize, bool Bold, bool Italic,
    Color Fill, Color Outline, double OutlineThickness, double BottomOffset, bool WithinVideo)
{
    public static SubtitlesRenderStyle Default { get; } = new("Segoe UI", 48, true, false, Color.White, Color.Black, 1, 48, true);

    /// <summary>Physical pixels per DIP on the playback surface.</summary>
    public double PixelsPerDip { get; init; } = 1;

    /// <summary>Size multiplier for the full playback surface width in DIPs.</summary>
    public static double GetViewScale(double width) => Math.Clamp(0.2 + 0.8 * width / 1920, 0.25, 4);
}
