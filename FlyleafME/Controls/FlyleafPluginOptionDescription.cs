using System.Text.RegularExpressions;

namespace Flyleaf.Controls.WPF;

public enum FlyleafPluginOptionKind { Text, Boolean, Integer, Choice, Folder, File }

/// <summary>Optional presentation metadata. Stored plugin values remain strings.</summary>
public sealed record FlyleafPluginOptionDescription(string Label, string Group = "General",
    FlyleafPluginOptionKind Kind = FlyleafPluginOptionKind.Text, string Help = "", string Unit = "",
    long Minimum = int.MinValue, long Maximum = int.MaxValue, string[]? Choices = null, int MultipleOf = 1);

internal static class FlyleafPluginOptionDescriptions
{
    internal static string Label(string key) => Regex.Replace(key, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

    internal static FlyleafPluginOptionDescription Describe(string plugin, string key, string defaultValue, Type? declaredType)
    {
        if (plugin.Equals("YTDLP", StringComparison.OrdinalIgnoreCase) && Ytdlp.TryGetValue(key, out var known)) return known;
        if ((plugin.Equals("BitSwarm", StringComparison.OrdinalIgnoreCase) || plugin.Equals("BitSwarmPlugin", StringComparison.OrdinalIgnoreCase)) && BitSwarm.TryGetValue(key, out var torrent)) return torrent;
        var kind = declaredType == typeof(bool) || declaredType == null && bool.TryParse(defaultValue, out _) ? FlyleafPluginOptionKind.Boolean :
            declaredType == typeof(int) || declaredType == typeof(long) ? FlyleafPluginOptionKind.Integer : FlyleafPluginOptionKind.Text;
        if (declaredType?.IsEnum == true) return new(Label(key), Kind: FlyleafPluginOptionKind.Choice, Choices: Enum.GetNames(declaredType));
        return new(Label(key), Kind: kind);
    }

    static readonly Dictionary<string, FlyleafPluginOptionDescription> BitSwarm = new(StringComparer.Ordinal)
    {
        ["FolderComplete"] = new("Completed files", "Storage", FlyleafPluginOptionKind.Folder, "Used for the next torrent session."),
        ["FolderIncomplete"] = new("Incomplete files", "Storage", FlyleafPluginOptionKind.Folder, "Used for the next torrent session."),
        ["FolderTorrents"] = new("Torrent files", "Storage", FlyleafPluginOptionKind.Folder, "Used for the next torrent session."),
        ["TrackersPath"] = new("Additional trackers file", "Peer discovery", FlyleafPluginOptionKind.File, "Optional file containing tracker URLs, one per line. Used for the next torrent session."),
        ["EnablePEX"] = new("Peer exchange (PEX)", "Peer discovery", FlyleafPluginOptionKind.Boolean),
        ["EnableDHT"] = new("Distributed hash table (DHT)", "Peer discovery", FlyleafPluginOptionKind.Boolean),
        ["EnableTrackers"] = new("Use trackers", "Peer discovery", FlyleafPluginOptionKind.Boolean),
        ["PeersFromTracker"] = new("Peers per tracker", "Peer discovery", FlyleafPluginOptionKind.Integer, "−1 requests the maximum.", Minimum: -1),
        ["MaxTotalConnections"] = new("Maximum total connections", "Connections", FlyleafPluginOptionKind.Integer, Minimum: 1),
        ["MaxNewConnections"] = new("Maximum new connections", "Connections", FlyleafPluginOptionKind.Integer, Minimum: 1),
        ["ConnectionTimeout"] = new("Connection timeout", "Connections", FlyleafPluginOptionKind.Integer, Unit: "ms", Minimum: 1),
        ["HandshakeTimeout"] = new("Handshake timeout", "Connections", FlyleafPluginOptionKind.Integer, Unit: "ms", Minimum: 1),
        ["MetadataTimeout"] = new("Metadata timeout", "Transfer", FlyleafPluginOptionKind.Integer, Unit: "ms", Minimum: 1),
        ["MetadataParallelReq"] = new("Parallel metadata requests", "Transfer", FlyleafPluginOptionKind.Integer, "Must be a multiple of two. Used for the next torrent session.", Minimum: 2, MultipleOf: 2),
        ["PieceTimeout"] = new("Piece timeout", "Transfer", FlyleafPluginOptionKind.Integer, Unit: "ms", Minimum: 1),
        ["PieceRetries"] = new("Piece retries", "Transfer", FlyleafPluginOptionKind.Integer, Minimum: 0),
        ["BlockRequests"] = new("Block requests per peer", "Transfer", FlyleafPluginOptionKind.Integer, Minimum: 1),
        ["DownloadNext"] = new("Download the next item", "Playback", FlyleafPluginOptionKind.Boolean),
        ["EnableBuffering"] = new("Use buffering timeouts", "Playback", FlyleafPluginOptionKind.Boolean),
        ["PieceBufferTimeout"] = new("Buffering piece timeout", "Playback", FlyleafPluginOptionKind.Integer, Unit: "ms", Minimum: 1),
        ["PieceBufferRetries"] = new("Buffering piece retries", "Playback", FlyleafPluginOptionKind.Integer, Minimum: 0),
        ["Verbosity"] = new("Log verbosity", "Advanced", FlyleafPluginOptionKind.Integer, "0 is quiet; 4 is the most detailed.", Minimum: 0, Maximum: 4),
        ["LogTracker"] = new("Log trackers", "Advanced", FlyleafPluginOptionKind.Boolean),
        ["LogPeer"] = new("Log peers", "Advanced", FlyleafPluginOptionKind.Boolean),
        ["LogDHT"] = new("Log DHT", "Advanced", FlyleafPluginOptionKind.Boolean),
        ["LogStats"] = new("Log statistics", "Advanced", FlyleafPluginOptionKind.Boolean),
        ["PreventTimePeriods"] = new("Prevent timer-period changes", "Advanced", FlyleafPluginOptionKind.Boolean)
    };

    // Source: SuRGeoNix/Flyleaf.Plugins.YTDLP, Plugin/YTDLP.cs. Only keys supplied by
    // the installed version are presented; extra/new keys retain an editable fallback.
    static readonly Dictionary<string, FlyleafPluginOptionDescription> Ytdlp = new(StringComparer.Ordinal)
    {
        ["ForceExtract"] = new("Always use yt-dlp", "Extraction", FlyleafPluginOptionKind.Boolean, "Also extract URLs that look like direct media files."),
        ["ForwardCookies"] = new("Forward cookies to media requests", "Extraction", FlyleafPluginOptionKind.Boolean),
        ["UseHttpChunkSize"] = new("Use HTTP chunk sizing", "Extraction", FlyleafPluginOptionKind.Boolean),
        ["AllowTranslatedSubtitles"] = new("Include translated subtitles", "Extraction", FlyleafPluginOptionKind.Boolean),
        ["OpenTimeoutSeconds"] = new("Extraction timeout", "Extraction", FlyleafPluginOptionKind.Integer, Unit: "seconds", Minimum: 5, Maximum: 600),
        ["BrowserCookies"] = new("Use browser cookies", "Browser cookies", FlyleafPluginOptionKind.Boolean, "Requires the browser extension and application approval in its Options page. The plugin sets up its browser bridge when a URL needs it."),
        ["BrowserCookiesSource"] = new("Browser", "Browser cookies", FlyleafPluginOptionKind.Choice, Choices: ["Auto", "Chrome", "Brave"]),
        ["BrowserCookiesMode"] = new("Browser window", "Browser cookies", FlyleafPluginOptionKind.Choice, "Private/Auto also require the extension's private/incognito permission.", Choices: ["Normal", "Private", "Auto"]),
        ["BrowserCookiesTimeoutMs"] = new("Browser response timeout", "Browser cookies", FlyleafPluginOptionKind.Integer, Unit: "ms", Minimum: 200, Maximum: 10000),
        ["BrowserCookiesCacheSeconds"] = new("Normal-window cookie cache", "Browser cookies", FlyleafPluginOptionKind.Integer, Unit: "seconds", Minimum: 10, Maximum: 3600),
        ["PlaylistBatchSize"] = new("Discovery batch size", "Playlists", FlyleafPluginOptionKind.Integer, Unit: "items", Minimum: 1, Maximum: 500),
        ["PlaylistBatchDelayMs"] = new("Delay between batches", "Playlists", FlyleafPluginOptionKind.Integer, Unit: "ms", Minimum: 0, Maximum: 1000),
        ["PlaylistMaxItems"] = new("Maximum discovered items", "Playlists", FlyleafPluginOptionKind.Integer, "0 allows unlimited items.", "items", 0, 100000),
        ["PreferredItemWaitMs"] = new("Wait for requested playlist item", "Playlists", FlyleafPluginOptionKind.Integer, Unit: "ms", Minimum: 0, Maximum: 30000),
        ["StreamRefreshMinutes"] = new("Refresh resolved stream URLs", "Advanced", FlyleafPluginOptionKind.Integer, Unit: "minutes", Minimum: 1, Maximum: 240),
        ["MaxAvailabilityWaitSeconds"] = new("Wait for scheduled availability", "Advanced", FlyleafPluginOptionKind.Integer, Unit: "seconds", Minimum: 1, Maximum: 600),
        ["ExtraArguments"] = new("Additional yt-dlp arguments", "Advanced", Help: "Passed to subsequent extraction commands. Existing extra arguments are preserved verbatim.")
    };
}
