using System.IO;
using FlyleafLib.MediaFramework.MediaContext;

namespace Flyleaf.Controls.WPF;

// The final path is chosen only after the input has been resolved and streams selected.
internal sealed record FlyleafDownloadRequest(string Input, string Folder, string? Name = null, string? Extension = null,
    FlyleafDownloadSelection? Selection = null, bool AudioOnly = false, string? Title = null, bool VideoOnly = false,
    DateTimeOffset? StartAt = null, TimeSpan? RecordingDuration = null, FlyleafDownloadTarget? Target = null)
{
    internal FlyleafDownloadRequest Validate()
    {
        if (RecordingDuration is { } duration && (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(365)))
            throw new ArgumentException("Recording duration must be greater than zero and at most 365 days.");
        if (StartAt is { } start && start <= DateTimeOffset.UtcNow)
            throw new ArgumentException("The scheduled start time must be in the future.");
        if (StartAt.HasValue && RecordingDuration.HasValue && StartAt.Value.UtcDateTime > DateTime.MaxValue - RecordingDuration.Value)
            throw new ArgumentException("The recording end time is out of range.");
        if (AudioOnly && VideoOnly) throw new ArgumentException("Choose audio-only or video-only, not both.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Input);
        ArgumentException.ThrowIfNullOrWhiteSpace(Folder);
        if (!Path.IsPathFullyQualified(Folder)) throw new ArgumentException("Enter an absolute download folder path.");
        string folder = Path.GetFullPath(Folder);
        if (File.Exists(folder)) throw new IOException("The download folder is a file.");
        string? name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();
        string? extension = string.IsNullOrWhiteSpace(Extension) ? null : Extension.Trim().TrimStart('.');
        if (name != null && (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || IsReserved(name) || name.Length > 200))
            throw new ArgumentException("Enter a valid filename without its extension (up to 200 characters).");
        if (extension != null && (extension.Length == 0 || extension.Length > 16 || !extension.All(char.IsAsciiLetterOrDigit)))
            throw new ArgumentException("Enter a valid file extension, or leave it blank for Automatic.");
        return this with { Input = Input.Trim(), Folder = folder, Name = name, Extension = extension };
    }

    internal string ReserveOutput(Downloader downloader, string title, HashSet<string> reservedPaths)
    {
        string extension = Extension ?? downloader.DecCtx.Extension;
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 16 || !extension.All(char.IsAsciiLetterOrDigit))
            throw new IOException("The input has no recommended output extension. Specify one manually.");
        string name = Name ?? AutomaticName(title, Input, extension);
        Directory.CreateDirectory(Folder);
        for (int i = 0; ; i++)
        {
            string path = Path.Combine(Folder, (i == 0 ? name : $"{name} ({i})") + "." + extension);
            if (reservedPaths.Contains(path))
            {
                if (Name == null) continue;
                throw new IOException("The output file is reserved by another download.");
            }
            try { using (File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { } return path; }
            catch (IOException) when (Name == null && (File.Exists(path) || Directory.Exists(path))) { }
            catch (UnauthorizedAccessException) when (Name == null && Directory.Exists(path)) { }
        }
    }

    internal static string AutomaticName(string title, string input, string extension)
    {
        if (string.IsNullOrWhiteSpace(title)) title = "Download";
        // Local inputs often report their full filename as the title.
        if (title.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)) title = title[..^(extension.Length + 1)];
        else if (File.Exists(input) && StringComparer.OrdinalIgnoreCase.Equals(title, Path.GetFileName(input))) title = Path.GetFileNameWithoutExtension(input);
        string name = Utils.GetValidFileName(title).Trim().TrimEnd('.', ' ');
        if (name.Length > 120) name = name[..(char.IsHighSurrogate(name[119]) ? 119 : 120)].TrimEnd('.', ' ');
        if (name.Length == 0) name = "Download";
        if (IsReserved(name)) name = "_" + name;
        return name;
    }

    static bool IsReserved(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]);
    }
}
