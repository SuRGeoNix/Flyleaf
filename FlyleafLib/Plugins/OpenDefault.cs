using FlyleafLib.MediaFramework.MediaPlaylist;

namespace FlyleafLib.Plugins;

public class OpenDefault : PluginBase, IOpen, IScrapeItem
{
    /* TODO
     *
     * 1) Current Url Syntax issues
     *  ..\..\..\..\folder\file.mp3 | Cannot handle this
     *  file:///C:/folder/fi%20le.mp3 | FFmpeg & File.Exists cannot handle this
     *
     */

    public new int  Priority    { get; set; } = 3000;

    public bool CanOpen() => true;

    public OpenResults Open()
    {
        try
        {
            if (Playlist.IOStream != null)
            {
                AddPlaylistItem(new()
                {
                    IOStream= Playlist.IOStream,
                    Title   = "Custom IO Stream",
                    FileSize= Playlist.IOStream.Length
                });

                Handler.OnPlaylistCompleted();

                return new();
            }

            // Proper Url Format
            bool   isWeb    = false;
            string ext      = GetUrlExtention(Playlist.Url);
            Uri uri         = null;
            string localPath= null;

            try
            {
                uri         = new(Playlist.Url);
                isWeb       = uri.Scheme.StartsWith("http");
                localPath   = uri.LocalPath;
            } catch { }


            // Playlists (M3U, M3U8, PLS | TODO: WPL, XSPF)
            if (ext == "m3u")
            {
                var text = isWeb ? DownloadToString(Playlist.Url, 30000) : File.ReadAllText(Playlist.Url);
                if (text == null) return new("Could not read the playlist.");
                // HLS belongs to the media demuxer, even when named .m3u.
                // Ordinary lists retain the existing library-owned playlist path.
                if (!PlaylistFormats.IsHlsManifest(text))
                {
                    Playlist.InputType = InputType.Web; // TBR: Can be mixed
                    Playlist.FolderBase = Path.GetTempPath();
                    foreach (var mitem in M3UPlaylist.ParseFromString(text))
                    {
                        AddPlaylistItem(new()
                        {
                            Title = mitem.Title,
                            Url = mitem.Url,
                            DirectUrl = mitem.Url,
                            Referrer = mitem.Referrer,
                            HTTPHeaders = mitem.UserAgent == null ? null : new() { ["User-Agent"] = mitem.UserAgent }
                        });
                    }
                    Handler.OnPlaylistCompleted();
                    return new();
                }
            }
            else if (ext == "pls")
            {
                Playlist.InputType = InputType.Web; // TBR: Can be mixed
                Playlist.FolderBase = Path.GetTempPath();

                var items = PLSPlaylist.Parse(Playlist.Url);

                foreach(var mitem in items)
                {
                    AddPlaylistItem(new()
                    {
                        Title       = mitem.Title,
                        Url         = mitem.Url,
                        DirectUrl   = mitem.Url,
                        // Duration
                    });
                }

                Handler.OnPlaylistCompleted();

                return new();
            }

            // Single Playlist Item
            FileInfo fi         = null;
            PlaylistItem item   = new()
            {
                Url         = Playlist.Url,
                DirectUrl   = Playlist.Url
            };

            if (isWeb)
            {
                Playlist.InputType = InputType.Web;
                Playlist.FolderBase = Path.GetTempPath();
            }
            else if (uri != null && uri.IsFile)
            {
                try
                {
                    fi = new(Playlist.Url);
                    Playlist.FolderBase = fi.DirectoryName;
                }
                catch
                {
                    Playlist.FolderBase = Path.GetTempPath();
                }

                // TBR: UNC && !uri.IsLoopback (not clear yet why we separate file/unc*) | Rename UNC to RemoteUNC?
                Playlist.InputType = uri.IsUnc ? InputType.UNC : InputType.File; 
            }
            else // InputType.Unknown
                Playlist.FolderBase = Path.GetTempPath();

            if (fi != null)
            {
                item.Title      = fi.Name;
                item.FileSize   = fi.Length; // TBR: we could still get file size from AVIO (mainly for search online subs/hash)
            }
            else
            {
                item.Title = Path.GetFileName(localPath);

                if (string.IsNullOrEmpty(item.Title))
                    item.Title = Playlist.Url;
            }

            AddPlaylistItem(item);
            Handler.OnPlaylistCompleted();

            return new();

        }
        catch (Exception e)
        {
            return new(e.Message);
        }
    }

    public OpenResults OpenItem() => new();

    public void ScrapeItem(PlaylistItem item)
    {
        // Update Title (TBR: don't mess with other media types - only movies/tv shows)
        if (Playlist.InputType != InputType.File && Playlist.InputType != InputType.UNC && Playlist.InputType != InputType.Torrent)
            return;

        item.FillMediaParts();
    }
}
