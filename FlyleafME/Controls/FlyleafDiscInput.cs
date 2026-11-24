using System.Globalization;
using System.IO;
using static Flyleaf.FFmpeg.Raw;

namespace Flyleaf.Controls.WPF;

/// <summary>Disc input identification and display names, independent of the queue's persistence policy.</summary>
public static class FlyleafDiscInput
{
    internal enum DiscType { Auto, Bluray, Dvd }
    const string BlurayPrefix = "bluray:", BlurayFormatPrefix = "fmt://mpegts?bluray:", DvdPrefix = "fmt://dvdvideo?";

    // Query the loaded FFmpeg libraries, not their build names or configuration strings.
    internal static unsafe (bool Bluray, bool Dvd) Capabilities() =>
        (avio_find_protocol_name("bluray:") != null && av_find_input_format("mpegts") != null, av_find_input_format("dvdvideo") != null);

    internal static string NormalizeRoot(string path)
    {
        if (path.Length == 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') path += Path.DirectorySeparatorChar;
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string leaf = Path.GetFileName(path);
        if (leaf.Equals("BDMV", StringComparison.OrdinalIgnoreCase) || leaf.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path) ?? path;
        return Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
    }

    internal static (bool Bluray, bool Dvd) Structures(string root) =>
        (Directory.Exists(Path.Combine(root, "BDMV", "PLAYLIST")) && Directory.Exists(Path.Combine(root, "BDMV", "STREAM")),
         File.Exists(Path.Combine(root, "VIDEO_TS", "VIDEO_TS.IFO")));

    internal static string BuildBluray(string root, int playlist = -1, int angle = 1, int chapter = 1) =>
        playlist == -1 && angle == 1 && chapter == 1 ? BlurayPrefix + root
            : FormattableString.Invariant($"{BlurayFormatPrefix}{root}&playlist={playlist}&angle={angle - 1}&chapter={chapter}");

    internal static string BuildDvd(string root, int title = 1, int angle = 1, int chapter = 1) =>
        FormattableString.Invariant($"{DvdPrefix}{root}&title={title}&angle={angle}&chapter_start={chapter}");

    /// <summary>Returns a disc input for a local disc folder, or null for an ordinary media input.</summary>
    public static string? FromFolder(string input)
    {
        if (!Path.IsPathFullyQualified(input) || !Directory.Exists(input)) return null;
        string root = NormalizeRoot(input);
        var (bd, dvd) = Structures(root);
        if (!bd && !dvd) return null;
        if (bd && dvd) throw new InvalidOperationException("Both disc structures are present. Use Open disc to choose the disc type.");
        if (!Engine.IsLoaded) throw new InvalidOperationException("Initialize the Flyleaf engine before opening a disc.");
        var capabilities = Capabilities();
        if (bd && !capabilities.Bluray || dvd && !capabilities.Dvd)
            throw new NotSupportedException($"{(bd ? "Blu-ray" : "DVD")} playback is not available in the loaded FFmpeg build.");
        if (dvd && root.Contains('&')) throw new ArgumentException("DVD paths containing & are not supported with format options.");
        return bd ? BuildBluray(root) : BuildDvd(root);
    }

    internal static string? DisplayTitle(string input)
    {
        bool bd = input.StartsWith(BlurayPrefix, StringComparison.Ordinal) || input.StartsWith(BlurayFormatPrefix, StringComparison.Ordinal);
        string prefix = input.StartsWith(BlurayFormatPrefix, StringComparison.Ordinal) ? BlurayFormatPrefix : bd ? BlurayPrefix : DvdPrefix;
        if (!input.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string path = input[prefix.Length..], options = "";
        int separator = path.IndexOf('&');
        if (prefix != BlurayPrefix && separator >= 0)
        { options = path[(separator + 1)..]; path = path[..separator]; }
        if (path.Length == 0) return null;
        string name = path;
        try
        {
            string root = Path.TrimEndingDirectorySeparator(path);
            name = Path.GetFileName(root);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = root;
                if (root.Length == 3 && char.IsAsciiLetter(root[0]) && root[1] == ':')
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)) name = drive.VolumeLabel;
                }
            }
            else if (!bd && Path.GetExtension(root).Equals(".iso", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        if (string.IsNullOrWhiteSpace(name)) name = path;
        string result = name + (bd ? " · Blu-ray" : " · DVD");
        string key = bd ? "playlist=" : "title=";
        foreach (var option in options.Split('&'))
            if (option.StartsWith(key, StringComparison.Ordinal) && int.TryParse(option.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= (bd ? 0 : 1))
                return result + (bd ? " · Playlist " + number.ToString("D5", CultureInfo.InvariantCulture) : " · Title " + number.ToString(CultureInfo.InvariantCulture));
        return result;
    }
}
