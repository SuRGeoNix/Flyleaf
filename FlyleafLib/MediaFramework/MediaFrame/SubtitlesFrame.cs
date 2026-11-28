using System.Drawing;
using System.Globalization;
using System.Text;

namespace FlyleafLib.MediaFramework.MediaFrame;

public unsafe class SubtitlesFrame : FrameBase
{
    public uint duration;
    public string text;
    public List<SubStyle> subStyles;
    public AVSubtitle sub;
    internal bool SubmittedToOverlay;
    internal int BitmapWidth, BitmapHeight;
}

public struct SubStyle
{
    public SubStyles style;
    public Color value;

    public int from;
    public int len;

    public SubStyle(int from, int len, Color value) : this(SubStyles.COLOR, from, len, value) { }
    public SubStyle(SubStyles style, int from = -1, int len = -1, Color? value = null)
    {
        this.style  = style;
        this.value  = value == null ? Color.White : (Color)value;
        this.from   = from;
        this.len    = len;
    }
}

public enum SubStyles
{
    BOLD,
    ITALIC,
    UNDERLINE,
    STRIKEOUT,
    FONTSIZE,
    FONTNAME,
    COLOR
}

/// <summary>
/// Default Subtitles Parser
/// </summary>
public static class ParseSubtitles
{
    public static void Parse(SubtitlesFrame sFrame)
    {
        sFrame.text = SSAtoSubStyles(sFrame.text, out var subStyles);
        sFrame.subStyles = subStyles;
    }
    public static string SSAtoSubStyles(string s, out List<SubStyle> styles)
    {
        styles = [];
        if (string.IsNullOrEmpty(s))
            return "";

        var dialogue = GetDialogue(s.AsSpan());
        if (dialogue.IndexOfAny('{', '\\') < 0)
            return dialogue.Length == s.Length ? s : dialogue.ToString();

        var text = new StringBuilder(dialogue.Length);
        Dictionary<SubStyles, SubStyle> active = [];
        bool drawing = false;

        for (int i = 0; i < dialogue.Length; i++)
        {
            if (dialogue[i] == '{')
            {
                int closing = dialogue[i..].IndexOf('}');
                if (closing >= 0)
                {
                    var block = dialogue.Slice(i + 1, closing - 1).Trim();
                    if (block.IsEmpty || block[0] == '\\')
                    {
                        ApplyOverrides(block, text.Length, active, styles, ref drawing);
                        i += closing;
                        continue;
                    }
                }
            }

            if (dialogue[i] == '\\' && i + 1 < dialogue.Length)
            {
                char next = dialogue[i + 1];
                if (next is 'N' or 'n' or 'h' or '{' or '}')
                {
                    if (!drawing)
                        text.Append(next switch { 'N' => '\n', 'n' => ' ', 'h' => '\u00a0', _ => next });
                    i++;
                    continue;
                }
            }

            if (!drawing)
                text.Append(dialogue[i]);
        }

        foreach (var style in active.Values)
            AddSpan(style, text.Length, styles);

        return text.ToString();
    }

    static ReadOnlySpan<char> GetDialogue(ReadOnlySpan<char> cue)
    {
        var field = cue;
        bool fullLine = field.TrimStart().StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase);
        if (fullLine) field = field.TrimStart()[9..];

        int fieldCount = fullLine ? 9 : 8;
        for (int i = 0; i < fieldCount; i++)
        {
            int comma = field.IndexOf(',');
            if (comma < 0 || (!fullLine && (i is 0 or 1 or 4 or 5 or 6) && !int.TryParse(field[..comma], out _)))
                return cue;

            field = field[(comma + 1)..];
        }
        return field;
    }

    static void ApplyOverrides(ReadOnlySpan<char> block, int offset, Dictionary<SubStyles, SubStyle> active,
        List<SubStyle> styles, ref bool drawing)
    {
        int depth = 0;
        for (int i = 0; i < block.Length; i++)
        {
            if (block[i] == '(') depth++;
            else if (block[i] == ')' && depth > 0) depth--;
            else if (block[i] == '\\' && depth == 0)
            {
                int end = i + 1;
                while (end < block.Length && (char.IsAsciiLetter(block[end]) || (end == i + 1 && block[end] == '1')))
                    end++;

                var tag = block[(i + 1)..end];
                int next = block[end..].IndexOf('\\');
                var arg = (next < 0 ? block[end..] : block.Slice(end, next)).Trim();
                if (tag.SequenceEqual("p") && int.TryParse(arg, out int mode))
                    drawing = mode > 0;
                else if (tag.Length > 0 && tag[0] == 'r')
                {
                    foreach (var style in active.Values) AddSpan(style, offset, styles);
                    active.Clear();
                }
                else if (tag.SequenceEqual("c") || tag.SequenceEqual("1c"))
                {
                    if (arg.IsEmpty)
                        UpdateStyle(SubStyles.COLOR, null, false, offset, active, styles);
                    else if (arg.StartsWith("&H", StringComparison.OrdinalIgnoreCase) &&
                        uint.TryParse(arg[2..].TrimEnd('&'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint bgr))
                        UpdateStyle(SubStyles.COLOR, Color.FromArgb((int)(bgr & 255), (int)((bgr >> 8) & 255),
                            (int)((bgr >> 16) & 255)), true, offset, active, styles);
                }
                else if (tag.Length == 1 && int.TryParse(arg, out int enabled))
                {
                    SubStyles? style = tag[0] switch
                    {
                        'b' => SubStyles.BOLD, 'i' => SubStyles.ITALIC,
                        'u' => SubStyles.UNDERLINE, 's' => SubStyles.STRIKEOUT, _ => null
                    };
                    if (style.HasValue)
                        UpdateStyle(style.Value, null, enabled != 0, offset, active, styles);
                }
            }
        }
    }

    static void UpdateStyle(SubStyles kind, Color? color, bool enabled, int offset,
        Dictionary<SubStyles, SubStyle> active, List<SubStyle> styles)
    {
        if (active.Remove(kind, out var previous))
            AddSpan(previous, offset, styles);
        if (enabled)
            active.Add(kind, new(kind, offset, value: color));
    }

    static void AddSpan(SubStyle style, int end, List<SubStyle> styles)
    {
        if (end <= style.from)
            return;
        style.len = end - style.from;
        styles.Add(style);
    }
}
