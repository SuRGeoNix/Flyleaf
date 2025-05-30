using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins;

// TODO: Config + Interrupt for WebClient / HttpClient
public class OpenSubtitlesOrg : PluginBase, ISearchOnlineSubtitles, IDownloadSubtitles
{
    public new int  Priority            { get; set; } = 2000;
    public int      SearchTimeoutMs     { get; set; } = 15000;
    public int      DownloadTimeoutMs   { get; set; } = 30000;

    static Dictionary<int, List<OpenSubtitlesOrgJson>> cache = [];
    static readonly string restUrl = "https://rest.opensubtitles.org/search";
    static readonly string userAgent = "Flyleaf v2.0";

    public override void OnInitializingSwitch()
        => OnInitialized();

    public bool DownloadSubtitles(ExternalSubtitlesStream extStream)
    {
        if (GetTag(extStream) == null || GetTag(extStream) is not OpenSubtitlesOrgJson sub)
            return false;

        try
        {
            string subDownloadLinkEnc = sub.SubDownloadLink;

            string filename = sub.SubFileName;
            if (filename.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                filename = filename.Substring(0, filename.Length - 4);

            string zipPath = Path.GetTempPath() + filename + ".srt.gz";

            File.Delete(zipPath);
            File.Delete(zipPath[..^3]);

            Log.Debug($"Downloading {zipPath}");
            Utils.DownloadFile(subDownloadLinkEnc, zipPath, DownloadTimeoutMs);

            Log.Debug($"Unzipping {zipPath}");
            string unzipPath = Utils.GZipDecompress(zipPath);
            File.Delete(zipPath);
            Log.Debug($"Unzipped at {unzipPath}");

            sub.AvailableAt = unzipPath;
        }
        catch (Exception e)
        {
            sub.AvailableAt = null;
            Log.Debug($"Failed to download subtitle {sub.SubFileName} - {e.Message}");
            return false;
        }

        extStream.Url = sub.AvailableAt;
        return true;
    }

    public void SearchOnlineSubtitles()
    {
        List<string> langs = [];

        foreach(var lang in Config.Subtitles.Languages)
        {
            var oLang = CultureToOnline(lang.Culture);
            if (oLang != null)
                langs.Add(oLang);
        }

        if (langs.Count == 0)
            return;

        string hash = null;
        if (Selected.FileSize != 0)
            if (Selected.IOStream != null)
            {
                lock (decoder.VideoDemuxer.lockFmtCtx) // fmtCtx reads the same IOStream / need to ensure that we don't change position
                {
                    var savePos = Selected.IOStream.Position;
                    hash = Utils.ToHexadecimal(ComputeMovieHash(Selected.IOStream, Selected.FileSize));
                    Selected.IOStream.Position = savePos;
                }
            }
            else
                hash = Utils.ToHexadecimal(ComputeMovieHash(Selected.Url));

        Search(hash, Selected.FileSize, langs);
    }

    // https://trac.OpenSubtitlesJson.org/projects/OpenSubtitlesJson/wiki/HashSourceCodes
    public byte[] ComputeMovieHash(string filename)
    {
        byte[] result;
        using (Stream input = File.OpenRead(filename))
        {
            result = ComputeMovieHash(input);
        }
        return result;
    }
    public byte[] ComputeMovieHash(Stream input, long length = -1)
    {
        long lhash, streamsize;
        streamsize = input.Length;
        lhash = length == -1 ? streamsize : length;

        long i = 0;
        byte[] buffer = new byte[sizeof(long)];
        while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
        {
            i++;
            lhash += BitConverter.ToInt64(buffer, 0);
        }

        input.Position = Math.Max(0, streamsize - 65536);
        i = 0;
        while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
        {
            i++;
            lhash += BitConverter.ToInt64(buffer, 0);
        }

        byte[] result = BitConverter.GetBytes(lhash);
        Array.Reverse(result);
        return result;
    }

    public List<OpenSubtitlesOrgJson> SearchByHash(string hash, long length, int season = 0, int episode = 0)
    {
        int hashCode = HashCode.Combine(hash, length, season, episode);
        if (cache.TryGetValue(hashCode, out var subsCache))
            return [.. subsCache];

        string qSeason  = season != 0 ? $"/season-{season}" : "";
        string qEpisode = episode != 0 ? $"/episode-{episode}" : "";
        string query    = $"{qEpisode}/moviebytesize-{length}/moviehash-{hash}{qSeason}";
        return SearchQuery(query, hashCode);
    }
    public List<OpenSubtitlesOrgJson> SearchByIMDB(string imdbid, string lang, int season = 0, int episode = 0)
    {
        int hashCode = HashCode.Combine(imdbid, lang, season, episode);
        if (cache.TryGetValue(hashCode, out var subsCache))
            return [.. subsCache];

        string qSeason  = season != 0 ? $"/season-{season}" : "";
        string qEpisode = episode != 0 ? $"/episode-{episode}" : "";
        string query    = $"{qEpisode}/imdbid-{imdbid}{qSeason}/sublanguageid-{lang}";
        return SearchQuery(query, hashCode);
    }
    public List<OpenSubtitlesOrgJson> SearchByName(string name, string lang, int season = 0, int episode = 0)
    {
        int hashCode = HashCode.Combine(name, lang, season, episode);
        if (cache.TryGetValue(hashCode, out var subsCache))
            return [.. subsCache];

        string qSeason  = season != 0 ? $"/season-{season}" : "";
        string qEpisode = episode != 0 ? $"/episode-{episode}" : "";
        string query    = $"{qEpisode}/query-{Uri.EscapeDataString(name.Replace('.', ' '))}{qSeason}/sublanguageid-{lang}";
        return SearchQuery(query, hashCode);
    }

    List<OpenSubtitlesOrgJson> SearchQuery(string query, int hashCode)
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromMilliseconds(SearchTimeoutMs) };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

            Log.Debug($"Searching for {query}");
            string resp = client.PostAsync($"{restUrl}{query}", null).Result.Content.ReadAsStringAsync().Result;
            List<OpenSubtitlesOrgJson> subs = JsonSerializer.Deserialize<List<OpenSubtitlesOrgJson>>(resp);
            subs ??= [];
            Log.Debug($"Search Results {subs.Count}");
            cache.Add(hashCode, subs);
            return [.. subs];
        }
        catch (Exception e)
        {
            Log.Debug($"Error fetching subtitles {e.Message} - {e.StackTrace}");
            cache.Add(hashCode, []);
            return [];
        }
    }

    public void Search(string hash, long length, List<string> Languages)
    {
        Selected.FillMediaParts();

        // Search by Hash (Any Lang)
        List<OpenSubtitlesOrgJson> subs = SearchByHash(hash, length, Selected.Season, Selected.Episode);

        // Search by Name / Lang
        var title = Selected.MediaTitle.ToLower();
        if (title != "")
            foreach (string lang in Languages)
                subs.AddRange(SearchByName(title, lang, Selected.Season, Selected.Episode));
            
        CleanSubs(subs);

        // Search by IMDB Id / Lang
        if (title != "")
            for (int i = 0; i < subs.Count; i++)
                if (subs[i].IDMovieImdb != null && subs[i].IDMovieImdb.Trim() != "" &&
                    (subs[i].MovieName          != null && title == subs[i].MovieName       .ToLower()) ||
                    (subs[i].MovieNameEng       != null && title == subs[i].MovieNameEng    .ToLower()) ||
                    (subs[i].MovieReleaseName   != null && title == subs[i].MovieReleaseName.ToLower()) ||
                    (subs[i].SubFileName        != null && title == Utils.GetMediaParts(subs[i].SubFileName).Title.ToLower())
                    )
                {
                    List<OpenSubtitlesOrgJson> subs2 = [];
                    foreach (string lang in Languages)
                        subs2.AddRange(SearchByIMDB(subs[i].IDMovieImdb.PadLeft(7, '0'), lang, Selected.Season, Selected.Episode)); // imdbid requireds 7 digits

                    CleanSubs(subs2);
                    subs.AddRange(subs2);
                    break;
                }

        // Unique by SubHashes (if any)
        List<int> removeIds = [];
        for (int i = 0; i < subs.Count - 1; i++)
        {
            if (removeIds.Contains(i)) continue;

            for (int l = i + 1; l < subs.Count; l++)
            {
                if (removeIds.Contains(l)) continue;

                if (subs[l].SubHash == subs[i].SubHash)
                {
                    if (subs[l].AvailableAt == null)
                        removeIds.Add(l);
                    else
                        { removeIds.Add(i); break; }
                }
            }
        }

        List<OpenSubtitlesOrgJson> uniqueList = [];
        for (int i = 0; i < subs.Count; i++)
            if (!removeIds.Contains(i))
                uniqueList.Add(subs[i]);

        subs.Clear();
        foreach (string lang in Languages)
        {
            IEnumerable<OpenSubtitlesOrgJson> movieHashRating =
                from sub in uniqueList
                where sub.SubLanguageID == lang && sub.MatchedBy.ToLower() == "moviehash"
                orderby float.Parse(sub.SubRating) descending
                select sub;

            IEnumerable<OpenSubtitlesOrgJson> rating =
                from sub in uniqueList
                where sub.SubLanguageID == lang && sub.MatchedBy.ToLower() != "moviehash"
                orderby float.Parse(sub.SubRating) descending
                select sub;

            foreach (var t1 in movieHashRating) subs.Add(t1);
            foreach (var t1 in rating) subs.Add(t1);
        }

        foreach (var sub in subs)
        {
            float rating = Math.Min(Math.Max(0, float.Parse(sub.SubRating)), 10);

            AddExternalStream(new ExternalSubtitlesStream()
            {
                Title   = sub.SubFileName,
                Rating  = rating,
                Language= Language.Get(sub.SubLanguageID)
            }, sub);
        }
    }

    void CleanSubs(List<OpenSubtitlesOrgJson> subs)
    {
        // Ensure same season/episode/year if any (TBR: might filename has different from sub?)

        for (int i = subs.Count - 1; i >= 0; i--)
        {
            var sub = subs[i];

            if (!int.TryParse(sub.SeriesEpisode, out int episode))
                episode = 0;

            if (Selected.Episode != episode)
                { subs.RemoveAt(i); continue; }

            if (!int.TryParse(sub.SeriesSeason, out int season))
                season = 0;

            if (Selected.Season > 0 && Selected.Season != season)
                { subs.RemoveAt(i); continue; }

            if (!int.TryParse(sub.MovieYear, out int year))
                year = 0;

            if (Selected.Episode == 0 && Selected.Year > 0 && Selected.Year != year)
                { subs.RemoveAt(i); continue; }
        }
    }

    public static string CultureToOnline(CultureInfo cult)
    {
        if (cult.IetfLanguageTag.StartsWith("zh-Hant"))
            return "zht";
        else if (cult.IetfLanguageTag == "pt-BR")
            return "pob";
        else if (cult.ThreeLetterISOLanguageName == "nob")
            return "nor";
        else if (cult.ThreeLetterISOLanguageName == "srp")
            return "scc";
        else if (cult.ThreeLetterISOLanguageName == "fil")
            return "tgl";

        if (ISOX_Online.TryGetValue(cult.ThreeLetterISOLanguageName, out string retValue))
            return retValue;

        Language.ISO639_2T_TO_2B.TryGetValue(cult.ThreeLetterISOLanguageName, out string iso639_2b);

        if (iso639_2b != null && ISOX_Online.TryGetValue(iso639_2b, out retValue))
            return retValue;

        return null;
    }

    // https://www.opensubtitles.org/addons/export_languages.php (UploadEnabled && WebEnabled)
    public static readonly HashSet<string> ISOX_Online =
    [
        "alb",
        "ara",
        //"arg", no culture?
        "arm",
        "ast",
        "baq",
        "bre",
        "bul",
        "cat",
        "chi",
        "cze",
        "dan",
        "dut",
        "ell",
        "eng",
        "epo",
        "est",
        "fin",
        "fre",
        "geo",
        "ger",
        "glg",
        "heb",
        "hin",
        "hrv",
        "hun",
        "ice",
        "ind",
        "ita",
        "jpn",
        "khm",
        "kor",
        "mac",
        "may",
        "nor",
        "oci",
        "per",
        "pob",
        "pol",
        "por",
        "rum",
        "rus",
        "scc",
        "sin",
        "slo",
        "slv",
        "spa",
        "swe",
        "tat",
        "tgl",
        "tha",
        "tur",
        "ukr",
        "vie",
        "zht",
    ];
}
