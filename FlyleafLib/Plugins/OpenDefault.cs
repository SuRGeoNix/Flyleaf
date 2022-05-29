using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using FlyleafLib.MediaFramework.MediaPlaylist;

namespace FlyleafLib.Plugins
{
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

        public bool CanOpen()
        {
            return true;
        }

        public OpenResults Open()
        {
            try
            {
                if (Playlist.IOStream != null)
                {
                    AddPlaylistItem(new PlaylistItem()
                    {
                        IOStream= Playlist.IOStream,
                        Title   = "Custom IO Stream",
                        FileSize= Playlist.IOStream.Length
                    });

                    Handler.OnPlaylistCompleted();

                    return new OpenResults();
                }

                // Proper Url Format
                string scheme;
                bool isWeb = false;
                string uriType = "";
                string ext = Utils.GetUrlExtention(Playlist.Url);
                string localPath = null;

                try
                {
                    Uri uri = new Uri(Playlist.Url);
                    scheme = uri.Scheme.ToLower();
                    isWeb = scheme.StartsWith("http");
                    uriType = uri.IsFile ? "file" : ((uri.IsUnc) ? "unc" : "");
                    localPath = uri.LocalPath;
                } catch { }


                // Playlists (M3U, PLS | TODO: WPL, XSPF)
                if (ext == "m3u")
                {
                    Playlist.InputType = InputType.Web; // TBR: Can be mixed
                    Playlist.FolderBase = Path.GetTempPath();

                    var items = isWeb ? M3UPlaylist.ParseFromHttp(Playlist.Url) : M3UPlaylist.Parse(Playlist.Url);

                    foreach(var mitem in items)
                    {
                        AddPlaylistItem(new PlaylistItem()
                        {
                            Title       = mitem.Title,
                            Url         = mitem.Url,
                            DirectUrl   = mitem.Url,
                            UserAgent   = mitem.UserAgent,
                            Referrer    = mitem.Referrer
                        });
                    }

                    Handler.OnPlaylistCompleted();

                    return new OpenResults();
                }
                else if (ext == "pls")
                {
                    Playlist.InputType = InputType.Web; // TBR: Can be mixed
                    Playlist.FolderBase = Path.GetTempPath();

                    var items = PLSPlaylist.Parse(Playlist.Url);

                    foreach(var mitem in items)
                    {
                        AddPlaylistItem(new PlaylistItem()
                        {
                            Title       = mitem.Title,
                            Url         = mitem.Url,
                            DirectUrl   = mitem.Url,
                            // Duration
                        });
                    }

                    Handler.OnPlaylistCompleted();

                    return new OpenResults();
                }


                // Single Playlist Item

                if (uriType == "file")
                {
                    Playlist.InputType = InputType.File;
                    if (File.Exists(Playlist.Url))
                    {
                        var fi = new FileInfo(Playlist.Url);
                        Playlist.FolderBase = fi.DirectoryName;
                    }
                }
                else if (isWeb)
                {
                    Playlist.InputType = InputType.Web;
                    Playlist.FolderBase = Path.GetTempPath();
                }
                else if (uriType == "unc")
                { 
                    Playlist.InputType = InputType.UNC;
                    Playlist.FolderBase = Path.GetTempPath();
                }
                else
                {
                    //Playlist.InputType = InputType.Unknown;
                    Playlist.FolderBase = Path.GetTempPath();
                }

                PlaylistItem item = new PlaylistItem();

                item.Url = Playlist.Url;
                item.DirectUrl = Playlist.Url;

                if (File.Exists(Playlist.Url))
                {
                    var fi = new FileInfo(Playlist.Url);
                    item.Title = fi.Extension == null ? fi.Name : fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
                    item.FileSize = fi.Length;
                }
                else
                    item.Title = localPath != null ? Path.GetFileName(localPath) : Playlist.Url;

                AddPlaylistItem(item);
                Handler.OnPlaylistCompleted();

                return new OpenResults();
            } catch (Exception e)
            {
                return new OpenResults(e.Message);
            }
        }

        public OpenResults OpenItem()
        {
            return new OpenResults();
        }

        public void ScrapeItem(PlaylistItem item)
        {
            // Update Season/Episode
            if (Utils.ExtractSeasonEpisode(item.OriginalTitle, out int season, out int episode))
            {
                item.Season = season;
                item.Episode = episode;
            }

            // Update Title (TBR: don't mess with other media types - only movies/tv shows)
            if (Playlist.InputType != InputType.File && Playlist.InputType != InputType.UNC && Playlist.InputType != InputType.Torrent)
                return;

            string title = item.OriginalTitle;//.Replace(".", " ").Replace("_", " ").Replace("-", " ").Trim();

            List<int> indices = new List<int>();
            indices.Add(Regex.Match(title, "[^a-z0-9][0-9]{4,}p").Index);
            indices.Add(Regex.Match(title, "[^a-z0-9][sS][0-9]{1,2}[eE][0-9]{1,2}").Index);
            //indices.Add(Regex.Match(title, "[^a-z0-9]19[0-9][0-9][^a-z0-9]").Index);
            //indices.Add(Regex.Match(title, "[^a-z0-9]20[0-2][0-9][^a-z0-9]").Index);

            var sorted = indices.OrderBy(x => x);

            int selectedIndex = -1;
            foreach (var index in sorted)
                if (index > 4) { selectedIndex = index; break; }

            if (selectedIndex != -1)
                title = title.Substring(0, selectedIndex).Trim();

            item.Title = title;

            if (item.Season != -1)
                item.Title += $" s{item.Season.ToString("00")}e{item.Episode.ToString("00")}";
        }
    }
}