using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaPlaylist;

namespace FlyleafLib.Plugins;

public class OpenSubtitles : PluginBase, IOpenSubtitles, ISearchLocalSubtitles
{
    public new int Priority { get; set; } = 3000;

    public OpenSubtitlesResults Open(string url)
    {
        foreach(var extStream in Selected.ExternalSubtitlesStreams)
            if (extStream.Url == url)
                return new(extStream);

        string      title;
        bool        converted   = false;
        Language    lang        = Language.Unknown;

        if (File.Exists(url))
        {
            Selected.FillMediaParts();

            FileInfo fi = new(url);
            title       = fi.Name;
            
            if (title.Contains(".utf8"))
            {
                converted = true;

                if (!title.Contains($".{Language.Unknown.IdSubLanguage}.utf8"))
                    foreach (var lang2 in Config.Subtitles.Languages)
                        if (title.Contains($".{lang2.IdSubLanguage}.utf8", StringComparison.OrdinalIgnoreCase))
                            { lang = lang2; break; }
            }
            else if (ExtensionsSubtitlesBitmap.Contains(GetUrlExtention(title)))
                converted = true;
        }
        else
        {
            try
            {
                Uri uri = new(url);
                title = Path.GetFileName(uri.LocalPath);
                if (ExtensionsSubtitlesBitmap.Contains(GetUrlExtention(title)))
                    converted = true;

                if (title == null || title.Trim().Length == 0)
                    title = url;

            } catch
            {
                if (ExtensionsSubtitlesBitmap.Contains(GetUrlExtention(url)))
                    converted = true;

                title = url;
            }
        }
        
        ExternalSubtitlesStream newExtStream = new()
        {
            Url         = url,
            Title       = title,
            Downloaded  = true,
            Converted   = converted,
            Language    = lang
        };

        AddExternalStream(newExtStream);

        return new(newExtStream);
    }

    public OpenSubtitlesResults Open(Stream iostream) => null;

    public void SearchLocalSubtitles()
    {
        try
        {
            string folder = Path.Combine(Playlist.FolderBase, Selected.Folder, "Subs");
            if (!Directory.Exists(folder))
                return;

            string[] filesCur1 = Directory.GetFiles(folder, $"*.srt"); // We consider Subs/ folder has only subs for this movie/series
            string[] filesCur2 = Directory.GetFiles(folder, $"*.sub");

            string[] filesCur = new string[filesCur1.Length + filesCur2.Length];

            for (int i = 0; i < filesCur1.Length; i++)
                filesCur[i] = filesCur1[i];

            for (int i = 0; i < filesCur2.Length; i++)
                filesCur[i + filesCur1.Length] = filesCur2[i];

            if (filesCur.Length > 0)
                Selected.FillMediaParts();

            foreach(string file in filesCur)
            {
                bool exists = false;
                foreach(var extStream in Selected.ExternalSubtitlesStreams)
                    if (extStream.Url == file)
                        { exists = true; break; }

                if (exists)
                    continue;

                FileInfo fi = new(file);
                string title = fi.Name;

                // We might have same Subs/ folder for more than one episode/season then filename requires to have season/episode
                var mp = GetMediaParts(title, true);
                if (mp.Episode != Selected.Episode || (mp.Season != Selected.Season && Selected.Season > 0 && mp.Season > 0))
                    continue;

                bool converted = false;
                Language lang = Language.Unknown;

                if (ExtensionsSubtitlesBitmap.Contains(mp.Extension))
                    converted = true;

                else if (title.Contains(".utf8"))
                {
                    converted = true;

                    if (!title.Contains($".{Language.Unknown.IdSubLanguage}.utf8"))
                        foreach (var lang2 in Config.Subtitles.Languages)
                            if (title.Contains($".{lang2.IdSubLanguage}.utf8", StringComparison.OrdinalIgnoreCase))
                                { lang = lang2; break; }
                }

                Log.Debug($"Adding [{lang}] {file}");

                AddExternalStream(new ExternalSubtitlesStream()
                {
                    Url         = file,
                    Title       = title,
                    Converted   = converted,
                    Downloaded  = true,
                    Language    = lang
                });
            }
        } catch (Exception e) { Log.Error($"SearchLocalSubtitles failed ({e.Message})"); }
    }
}
