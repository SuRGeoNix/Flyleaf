using System.Globalization;

namespace Flyleaf.Controls.WPF;

/// <summary>Validation shared by settings editors and focused checks; no native/player dependencies.</summary>
internal static class FlyleafSettingsValues
{
    internal static bool TryOptions(IEnumerable<KeyValuePair<string, string>> entries,
        out Dictionary<string, string> result, out string? error)
    {
        result = new(StringComparer.Ordinal); error = null;
        foreach (var entry in entries)
        {
            string key = entry.Key.Trim(), value = entry.Value;
            if (key.Length == 0 && value.Length == 0) continue;
            if (key.Length == 0) { error = "Each option needs a name."; return false; }
            if (key.Contains('\0') || value.Contains('\0')) { error = "Option names and values cannot contain a null character."; return false; }
            if (!result.TryAdd(key, value)) { error = $"Duplicate option: {key}"; return false; }
        }
        return true;
    }

    internal static bool TryInteger(string text, long min, long max, out long value)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    internal static bool ValidLuminanceRange(float minimumOverride, float peakOverride, float automaticPeak)
    {
        float peak = float.IsFinite(peakOverride) && peakOverride > 0 ? Math.Max(0.1f, peakOverride) : Math.Max(0.1f, automaticPeak);
        return !float.IsFinite(minimumOverride) || minimumOverride < 0 || minimumOverride < peak && minimumOverride <= peak - 0.001f;
    }

    internal static bool TryAudioFilters(IEnumerable<(string Name, string Id, string Args)> entries,
        out List<(string Name, string Id, string Args)> result, out string? error)
    {
        result = []; error = null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            string name = entry.Name.Trim(), id = entry.Id.Trim();
            if (name.Length == 0 && id.Length == 0 && entry.Args.Length == 0) continue;
            if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            { error = "Use a filter name containing letters, digits or underscores."; return false; }
            if (id.Contains('\0') || entry.Args.Contains('\0')) { error = "Filter values cannot contain a null character."; return false; }
            if (id.Length > 0 && !ids.Add(id)) { error = $"Duplicate filter identifier: {id}"; return false; }
            result.Add((name, id, entry.Args));
        }
        return true;
    }
}
