using System.Windows.Data;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaPlaylist;

namespace Flyleaf.Controls.WPF;

/// <summary>Reopen the original source, not an expiring resolved media URL.</summary>
public sealed record FlyleafDownloadTarget(string Input, string? Title = null, int? Position = null,
    string? DirectUrl = null, string? UserAgent = null, string? Referrer = null)
{
    internal static FlyleafDownloadTarget From(FlyleafQueueSource source, FlyleafQueueChild? child = null) =>
        new(source.Input, child?.Title ?? source.Title, child?.Position, child?.DirectUrl, source.UserAgent, source.Referrer);

    internal string? Open(Downloader downloader)
    {
        if (!Position.HasValue && UserAgent == null && Referrer == null) return downloader.Open(Input);
        var error = downloader.Open(Input, defaultPlaylistItem: false);
        if (error != null) return error;
        var ctx = downloader.DecCtx;
        PlaylistItem[] items = [];
        BindingOperations.AccessCollection(ctx.Playlist.Items, () => items = ctx.Playlist.Items.ToArray(), false);
        PlaylistItem? item;
        if (Position is int position)
        {
            PlaylistItem[] matches = DirectUrl == null ? [] : items.Where(i => i.DirectUrl == DirectUrl).ToArray();
            item = matches.Length == 1 && matches[0].Title == Title ? matches[0] :
                position >= 0 && position < items.Length && items[position].Title == Title ? items[position] : null;
            if (item == null) return "The selected playlist item is no longer available or has changed.";
        }
        else item = ctx.SuggestItem();
        if (item == null) return "No media item was resolved.";
        if (Referrer != null) item.Referrer = Referrer;
        if (UserAgent != null)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.HTTPHeaders != null) foreach (var pair in item.HTTPHeaders) headers[pair.Key] = pair.Value;
            headers["User-Agent"] = UserAgent; item.HTTPHeaders = headers;
        }
        return ctx.Open(item, defaultVideo: true, defaultAudio: true, defaultSubtitles: false).Error;
    }
}
