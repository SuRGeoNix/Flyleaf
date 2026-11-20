using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib.MediaFramework.MediaPlaylist;

namespace Flyleaf;

/// <summary>Portable playlist content. Playback/session state and discovered children are excluded.</summary>
public sealed class FlyleafPlaylistDocument
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "Playlist";
    public List<FlyleafPlaylistEntry> Entries { get; set; } = [];
}

public sealed class FlyleafPlaylistEntry
{
    public string Input { get; set; } = "";
    public string? Title { get; set; }
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }
}

/// <summary>Text codecs only; the application supplies file/network IO and the document's base URI.</summary>
public static class FlyleafPlaylistSerializer
{
    public const string NativeExtension = ".fpls";
    internal const string M3UExtension = ".m3u";
    internal const string M3U8Extension = ".m3u8";
    internal const string PlsExtension = ".pls";
    internal static IReadOnlyList<string> SupportedExtensions { get; } =
        Array.AsReadOnly<string>([NativeExtension, M3UExtension, M3U8Extension, PlsExtension]);

    public static bool IsSupportedExtension(string extension) =>
        SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static bool IsHls(string text) => PlaylistFormats.IsHlsManifest(text);
    public static FlyleafPlaylistDocument Read(string text, string extension, Uri baseUri)
    {
        extension = extension.ToLowerInvariant();
        FlyleafPlaylistDocument document;
        if (extension == NativeExtension)
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("version", out _) || !json.RootElement.TryGetProperty("entries", out _))
                throw new FormatException("A Flyleaf playlist requires version and entries fields.");
            document = JsonSerializer.Deserialize<FlyleafPlaylistDocument>(text, Options) ?? throw new FormatException("The playlist document is empty.");
            if (document.Version != 1) throw new FormatException($"Unsupported playlist version {document.Version}.");
        }
        else if (extension is M3UExtension or M3U8Extension) document = ReadM3U(text);
        else if (extension == PlsExtension) document = ReadPLS(text);
        else throw new FormatException("Choose a Flyleaf playlist, M3U/M3U8 or PLS file.");
        if (document.Entries == null || document.Entries.Any(entry => entry == null || string.IsNullOrWhiteSpace(entry.Input)))
            throw new FormatException("Every playlist entry must have an input.");
        foreach (var entry in document.Entries) entry.Input = Resolve(entry.Input, baseUri);
        Validate(document);
        return document;
    }
    public static string Write(FlyleafPlaylistDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(document, Options) + Environment.NewLine;
    }
    public static string WriteM3U(FlyleafPlaylistDocument document)
    {
        Validate(document);
        var text = new StringBuilder("#EXTM3U\n");
        foreach (var entry in document.Entries)
        {
            if (entry.Input.IndexOfAny(['\r', '\n']) >= 0) throw new FormatException("M3U inputs cannot contain line breaks.");
            var title = (entry.Title ?? "").Replace('\r', ' ').Replace('\n', ' ');
            text.Append("#EXTINF:-1,").Append(title).Append('\n');
            if (entry.Referrer != null) text.Append("#EXTVLCOPT:http-referrer=").Append(entry.Referrer).Append('\n');
            if (entry.UserAgent != null) text.Append("#EXTVLCOPT:http-user-agent=").Append(entry.UserAgent).Append('\n');
            text.Append(entry.Input).Append('\n');
        }
        return text.ToString();
    }
    public static void Validate(FlyleafPlaylistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != 1 || document.Entries == null || document.Entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Input)))
            throw new FormatException("Invalid playlist document.");
        foreach (var entry in document.Entries)
            foreach (var value in new[] { entry.UserAgent, entry.Referrer })
                if (value?.Any(c => c is '\r' or '\n' or '\0') == true) throw new FormatException("Playlist HTTP options cannot contain line breaks or null characters.");
    }
    static FlyleafPlaylistDocument ReadM3U(string text)
    {
        if (IsHls(text)) throw new FormatException("This is an HLS media manifest, not an editable playlist.");
        var result = new FlyleafPlaylistDocument();
        string? title = null;
        string? userAgent = null, referrer = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                title = null;
                bool quoted = false;
                for (var i = 0; i < line.Length; i++)
                {
                    if (line[i] == '"') quoted = !quoted;
                    if (line[i] == ',' && !quoted) { title = line[(i + 1)..].Trim(); break; }
                }
            }
            else if (line.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase))
            {
                var option = line[11..]; int equals = option.IndexOf('=');
                var key = (equals < 0 ? option : option[..equals]).Trim();
                var value = equals < 0 ? "" : option[(equals + 1)..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
                if (key.Equals("http-user-agent", StringComparison.OrdinalIgnoreCase)) userAgent = value;
                else if (key.Equals("http-referrer", StringComparison.OrdinalIgnoreCase)) referrer = value;
                else throw new FormatException($"Unsupported playlist option '{key}'. HTTP User-Agent and Referer options are supported.");
            }
            else if (line.StartsWith("#KODIPROP:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#EXTHTTP:", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("This playlist uses Kodi or EXTHTTP playback options that are not supported by the editable importer.");
            else if (!line.StartsWith('#'))
            {
                result.Entries.Add(new() { Input = line, Title = string.IsNullOrWhiteSpace(title) ? null : title, UserAgent = userAgent, Referrer = referrer });
                title = userAgent = referrer = null;
            }
        }
        return result;
    }
    static FlyleafPlaylistDocument ReadPLS(string text)
    {
        var files = new SortedDictionary<int, string>(); var titles = new Dictionary<int, string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF'); var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line[..equals].Trim(); var value = line[(equals + 1)..].Trim();
            if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var fileIndex) && fileIndex > 0)
                files[fileIndex] = value;
            else if (key.StartsWith("Title", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var titleIndex)) titles[titleIndex] = value;
        }
        if (files.Count == 0) throw new FormatException("The PLS playlist contains no File entries.");
        return new() { Entries = files.Select(pair => new FlyleafPlaylistEntry { Input = pair.Value, Title = titles.GetValueOrDefault(pair.Key) }).ToList() };
    }
    static string Resolve(string input, Uri baseUri)
    {
        input = input.Trim();
        if (Path.IsPathFullyQualified(input)) return Path.GetFullPath(input);
        var resolved = new Uri(baseUri, input);
        return resolved.IsFile ? resolved.LocalPath : resolved.AbsoluteUri;
    }
}
