using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Color = System.Drawing.Color;

using static FlyleafLib.Plugins.LibAss.LibAssNative;

namespace FlyleafLib.Plugins.LibAss;

internal sealed unsafe class LibAssSession : ISubtitlesRenderSession
{
    const int DirectWrite = 4;
    const long HistoryMilliseconds = 120_000;

    readonly record struct Cue(string Text, long Start, long Duration);

    readonly bool subRip;
    readonly int playResY;
    readonly HashSet<Cue> seen = [];
    readonly PriorityQueue<Cue, long> cueEnds = new();
    nint library, renderer, track;
    SubtitlesRenderRequest settings;
    IReadOnlyList<SubtitlesOverlayRect> overlay;
    long? renderedTimestamp;
    (string Family, bool Bold, bool Italic) defaultFont;
    double fontCellHeight = 1;

    internal LibAssSession(SubtitlesRenderTrack source)
    {
        subRip = source.Codec == "subrip";
        playResY = subRip ? ReadPlayResY(source.Header.Span) : 0;
        try
        {
            if (ass_library_version() < 0x01705000)
                throw new NotSupportedException("This plugin requires libass 0.17.5 or later.");

            library = ass_library_init();
            if (library == 0) throw new InvalidOperationException("Could not initialize libass.");

            ass_set_extract_fonts(library, 1);
            foreach (var font in source.Fonts)
                fixed (byte* data = font.Data.Span)
                    ass_add_font(library, font.Name, data, font.Data.Length);

            renderer = ass_renderer_init(library);
            track = ass_new_track(library);
            if (renderer == 0 || track == 0) throw new InvalidOperationException("Could not create a libass renderer and track.");

            ass_set_fonts(renderer, 0, "Segoe UI", DirectWrite, 0, 1);
            fixed (byte* header = source.Header.Span)
                ass_process_codec_private(track, header, source.Header.Length);

            // FFmpeg can reuse ReadOrder values after a seek. Deduplicate by cue and time instead.
            ass_set_check_readorder(track, 0);
            ass_configure_prune(track, HistoryMilliseconds);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void AddCue(string ass, long start, long duration)
    {
        int readOrderEnd = subRip ? ass.IndexOf(',') : -1;
        Cue cue = new(readOrderEnd < 0 ? ass : ass[(readOrderEnd + 1)..], start, duration);
        if (!seen.Add(cue)) return;

        if (subRip)
        {
            // FFmpeg translates the HTML closing tags into explicit "off" commands.
            // Restore the user's default bold or italic after those spans.
            ass = ass.Replace(@"{\b0}", @"{\b}").Replace(@"{\i0}", @"{\i}");
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(ass);
        fixed (byte* data = utf8)
            ass_process_chunk(track, data, utf8.Length, start, duration);
        cueEnds.Enqueue(cue, start + duration);
        renderedTimestamp = null;
    }

    public IReadOnlyList<SubtitlesOverlayRect> Render(in SubtitlesRenderRequest request)
    {
        var next = request with { Timestamp = 0, Style = subRip ? request.Style : null, SurfaceWidth = subRip ? request.SurfaceWidth : 0 };
        if (next != settings)
        {
            ass_set_frame_size(renderer, request.Width, request.Height);
            ass_set_storage_size(renderer, request.VideoWidth, request.VideoHeight);
            ass_set_pixel_aspect(renderer, subRip ? 1 : 0);
            if (subRip) ApplySubRipStyle(request);
            settings = next;
            overlay = null;
        }

        if (overlay != null && request.Timestamp == renderedTimestamp)
            return overlay;

        Image* image = ass_render_frame(renderer, track, request.Timestamp, out int changed);
        renderedTimestamp = request.Timestamp;
        while (cueEnds.TryPeek(out var cue, out long end) && end < request.Timestamp - HistoryMilliseconds)
        {
            cueEnds.Dequeue();
            seen.Remove(cue);
        }

        if (changed == 0 && overlay != null)
            return overlay;

        List<SubtitlesOverlayRect> rectangles = null;
        for (; image != null; image = image->Next)
        {
            if (image->Width <= 0 || image->Height <= 0 || image->Bitmap == null || (byte)image->Color == 255) continue;
            byte red = (byte)(image->Color >> 24), green = (byte)(image->Color >> 16), blue = (byte)(image->Color >> 8);
            int opacity = 255 - (byte)image->Color;
            byte[] pixels = new byte[checked(image->Width * image->Height * 4)];
            int offset = 0;
            for (int y = 0; y < image->Height; y++)
            {
                byte* mask = image->Bitmap + y * image->Stride;
                for (int x = 0; x < image->Width; x++)
                {
                    pixels[offset++] = blue;
                    pixels[offset++] = green;
                    pixels[offset++] = red;
                    pixels[offset++] = (byte)((mask[x] * opacity + 127) / 255);
                }
            }
            (rectangles ??= []).Add(new(image->X, image->Y, image->Width, image->Height, pixels));
        }
        return overlay = rectangles == null ? Array.Empty<SubtitlesOverlayRect>() : rectangles;
    }

    void ApplySubRipStyle(in SubtitlesRenderRequest request)
    {
        var style = request.Style ?? SubtitlesRenderStyle.Default;
        double dpi = double.IsFinite(style.PixelsPerDip) && style.PixelsPerDip > 0 ? style.PixelsPerDip : 1;
        int width = request.SurfaceWidth > 0 ? request.SurfaceWidth : request.Width;
        double viewScale = SubtitlesRenderStyle.GetViewScale(width / dpi) * dpi;
        double scale = viewScale * playResY / request.Height;
        // Updating line position invalidates libass's cached positions for active cues.
        // Explicitly positioned cues keep their authored coordinates.
        ass_set_line_position(renderer, Math.Clamp(100 * style.BottomOffset * viewScale / request.Height, 0, 100));
        string family = string.IsNullOrWhiteSpace(style.FontFamily) ? "Segoe UI" : style.FontFamily;
        var font = (family, style.Bold, style.Italic);
        if (font != defaultFont)
        {
            var typeface = new Typeface(new FontFamily(family),
                style.Italic ? FontStyles.Italic : FontStyles.Normal,
                style.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            // An ASS font size requests the cell height. Flyleaf's text size is an em size.
            fontCellHeight = typeface.TryGetGlyphTypeface(out var glyph) && glyph.Height > 0 ? glyph.Height : 1;
            defaultFont = font;
        }
        string[] fields =
        [
            $"Default.FontName={family}",
            FormattableString.Invariant($"Default.FontSize={style.FontSize * scale * fontCellHeight:0.###}"),
            $"Default.Bold={(style.Bold ? 1 : 0)}",
            $"Default.Italic={(style.Italic ? 1 : 0)}",
            $"Default.PrimaryColour={FormatColor(style.Fill)}",
            $"Default.OutlineColour={FormatColor(style.Outline)}",
            FormattableString.Invariant($"Default.Outline={style.OutlineThickness * scale:0.###}"),
            "Default.MarginV=0",
            "Default.Shadow=0"
        ];
        Span<nint> pointers = stackalloc nint[fields.Length + 1];
        pointers.Clear();
        try
        {
            for (int i = 0; i < fields.Length; i++)
                pointers[i] = Marshal.StringToCoTaskMemUTF8(fields[i]);
            fixed (nint* values = pointers)
                ass_set_style_overrides(library, values);
            ass_process_force_style(track);
        }
        finally
        {
            foreach (nint pointer in pointers)
                Marshal.FreeCoTaskMem(pointer);
        }
    }

    static string FormatColor(Color color) => $"&H{255 - color.A:X2}{color.B:X2}{color.G:X2}{color.R:X2}";

    static int ReadPlayResY(ReadOnlySpan<byte> header)
    {
        foreach (string line in Encoding.UTF8.GetString(header).Split('\n'))
            if (line.StartsWith("PlayResY:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line.AsSpan(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) && height > 0)
                return height;
        return 288;
    }

    public void Seek() => overlay = null;

    public void Dispose()
    {
        if (track != 0) ass_free_track(track);
        if (renderer != 0) ass_renderer_done(renderer);
        if (library != 0) ass_library_done(library);
        track = renderer = library = 0;
        seen.Clear();
        cueEnds.Clear();
        overlay = null;
    }
}
