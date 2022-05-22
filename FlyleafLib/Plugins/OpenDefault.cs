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
        public new int  Priority    { get; set; } = 3000;

        public bool CanOpen()
        {
            return true;
        }

        public OpenResults Open()
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

            // TODO playlist files
            try
            {
                Uri uri = new Uri(Playlist.Url);
                if (uri.IsFile)
                {
                    Playlist.InputType = InputType.File;
                    if (File.Exists(Playlist.Url))
                    {
                        var fi = new FileInfo(Playlist.Url);
                        Playlist.FolderBase = fi.DirectoryName;
                    }
                }
                else if (uri.Scheme.ToLower().StartsWith("http"))
                {
                    Playlist.InputType = InputType.Web;
                    Playlist.FolderBase = Path.GetTempPath();
                }
                else if (uri.IsUnc)
                { 
                    Playlist.InputType = InputType.UNC;
                    Playlist.FolderBase = Path.GetTempPath();
                }
                else
                {
                    //Playlist.InputType = InputType.Unknown;
                    Playlist.FolderBase = Path.GetTempPath();
                }

            } catch { }

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
            {
                try { Uri uri = new Uri(Playlist.Url); item.Title = Path.GetFileName(uri.LocalPath); } catch { }
            }

            AddPlaylistItem(item);
            Handler.OnPlaylistCompleted();

            return new OpenResults();
        }

        public OpenResults OpenItem()
        {
            return new OpenResults();
        }

        public void ScrapeItem(PlaylistItem item)
        {
            if (Utils.ExtractSeasonEpisode(item.OriginalTitle, out int season, out int episode))
            {
                item.Season = season;
                item.Episode = episode;
            }

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