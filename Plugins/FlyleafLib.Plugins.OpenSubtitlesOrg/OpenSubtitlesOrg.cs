using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;

using Newtonsoft.Json;

using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins
{
    // TODO: Config + Interrupt for WebClient / HttpClient
    public class OpenSubtitlesOrg : PluginBase, ISearchOnlineSubtitles, IDownloadSubtitles
    {
        public new int  Priority            { get; set; } = 2000;
        public int      SearchTimeoutMs     { get; set; } = 15000;
        public int      DownloadTimeoutMs   { get; set; } = 30000;

        static Dictionary<string, List<OpenSubtitlesOrgJson>> cache = new Dictionary<string, List<OpenSubtitlesOrgJson>>();
        static readonly string restUrl = "https://rest.opensubtitles.org/search";
        static readonly string userAgent = "Flyleaf v2.0";

        public override void OnInitializingSwitch()
        {
            OnInitialized();
        }

        public bool DownloadSubtitles(ExternalSubtitlesStream extStream)
        {
            if (GetTag(extStream) == null || !(GetTag(extStream) is OpenSubtitlesOrgJson))
                return false;

            var sub = (OpenSubtitlesOrgJson)GetTag(extStream);

            try
            {
                string subDownloadLinkEnc = sub.SubDownloadLink;

                string filename = sub.SubFileName;
                if (filename.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                    filename = filename.Substring(0, filename.Length - 4);

                string zipPath = Path.GetTempPath() + filename + "." + sub.SubLanguageID + ".srt.gz";

                File.Delete(zipPath);
                File.Delete(zipPath.Substring(0, zipPath.Length - 3));

                Log.Debug($"Downloading {zipPath}");
                Utils.DownloadFile(subDownloadLinkEnc, zipPath, DownloadTimeoutMs);

                Log.Debug($"Unzipping {zipPath}");
                string unzipPath = Utils.GZipDecompress(zipPath);
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
            List<string> langs = new List<string>();

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

            Search(Selected.Title, hash, Selected.FileSize, langs);
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

        public List<OpenSubtitlesOrgJson> SearchByHash(string hash, long length)
        {
            List<OpenSubtitlesOrgJson> subsCopy = new List<OpenSubtitlesOrgJson>();

            if (string.IsNullOrEmpty(hash)) return subsCopy;

            if (cache.ContainsKey(hash + "|" + length))
            {
                foreach (OpenSubtitlesOrgJson sub in cache[hash + "|" + length]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(SearchTimeoutMs) })
            {
                string resp = "";
                List<OpenSubtitlesOrgJson> subs = new List<OpenSubtitlesOrgJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    Log.Debug($"Searching for /moviebytesize-{length}/moviehash-{hash}");
                    resp = client.PostAsync($"{restUrl}/moviebytesize-{length}/moviehash-{hash}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesOrgJson>>(resp);
                    Log.Debug($"Search Results {subs.Count}");
                    cache.Add(hash + "|" + length, subs);
                    foreach (OpenSubtitlesOrgJson sub in subs) subsCopy.Add(sub);
                }
                catch (Exception e) { Log.Debug($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public List<OpenSubtitlesOrgJson> SearchByIMDB(string imdbid, string lang, string season = null, string episode = null)
        {
            List<OpenSubtitlesOrgJson> subsCopy = new List<OpenSubtitlesOrgJson>();

            if (cache.ContainsKey(imdbid + "|" + season + "|" + episode + lang))
            {
                foreach (OpenSubtitlesOrgJson sub in cache[imdbid + "|" + season + "|" + episode + lang]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(SearchTimeoutMs) })
            {
                string resp = "";
                List<OpenSubtitlesOrgJson> subs = new List<OpenSubtitlesOrgJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    string qSeason = season != null ? $"/season-{season}" : "";
                    string qEpisode = episode != null ? $"/episode-{episode}" : "";
                    string query = $"{qEpisode}/imdbid-{imdbid}{qSeason}/sublanguageid-{lang}";

                    Log.Debug($"Searching for {query}");
                    resp = client.PostAsync($"{restUrl}{query}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesOrgJson>>(resp);
                    Log.Debug($"Search Results {subs.Count}");

                    cache.Add(imdbid + "|" + season + "|" + episode + lang, subs);
                    foreach (OpenSubtitlesOrgJson sub in subs) subsCopy.Add(sub);
                }
                catch (Exception e) { Log.Debug($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public List<OpenSubtitlesOrgJson> SearchByName(string name, string lang)
        {
            List<OpenSubtitlesOrgJson> subsCopy = new List<OpenSubtitlesOrgJson>();

            if (cache.ContainsKey(name + "|" + lang))
            {
                foreach (OpenSubtitlesOrgJson sub in cache[name + "|" + lang]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(SearchTimeoutMs) })
            {
                string resp = "";
                List<OpenSubtitlesOrgJson> subs = new List<OpenSubtitlesOrgJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    Log.Debug($"Searching for /query-{Uri.EscapeDataString(name.Replace('.', ' '))}/sublanguageid-{lang}");
                    resp = client.PostAsync($"{restUrl}/query-{Uri.EscapeDataString(name.Replace('.', ' '))}/sublanguageid-{lang}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesOrgJson>>(resp);
                    Log.Debug($"Search Results {subs.Count}");

                    cache.Add(name + "|" + lang, subs);
                    foreach (OpenSubtitlesOrgJson sub in subs) subsCopy.Add(sub);
                }
                catch (Exception e) { Log.Debug($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public void Search(string filename, string hash, long length, List<string> Languages)
        {
            List<OpenSubtitlesOrgJson> subs = SearchByHash(hash, length);

            bool imdbExists = subs != null && subs.Count > 0 && subs[0].IDMovieImdb != null && subs[0].IDMovieImdb.Trim() != "";
            bool isEpisode = imdbExists && subs[0].SeriesSeason != null && subs[0].SeriesSeason.Trim() != "" && subs[0].SeriesSeason.Trim() != "0" && subs[0].SeriesEpisode != null && subs[0].SeriesEpisode.Trim() != "" && subs[0].SeriesEpisode.Trim() != "0";

            foreach (string lang in Languages)
            {
                if (imdbExists)
                {
                    if (isEpisode)
                        subs.AddRange(SearchByIMDB(subs[0].IDMovieImdb, lang, subs[0].SeriesSeason, subs[0].SeriesEpisode));
                    else
                        subs.AddRange(SearchByIMDB(subs[0].IDMovieImdb, lang));
                }

                subs.AddRange(SearchByName(filename, lang));
            }

            // Unique by SubHashes (if any)
            List<OpenSubtitlesOrgJson> uniqueList = new List<OpenSubtitlesOrgJson>();
            List<int> removeIds = new List<int>();
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
            for (int i = 0; i < subs.Count; i++)
                if (!removeIds.Contains(i)) uniqueList.Add(subs[i]);

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
                float rating = float.Parse(sub.SubRating);
                if (rating > 10) rating = 10;
                else if (rating < 0) rating = 0;

                AddExternalStream(new ExternalSubtitlesStream()
                {
                    Title   = sub.SubFileName,
                    Rating  = rating,
                    Language= Language.Get(sub.SubLanguageID)
                }, sub);
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
        public static readonly HashSet<string> ISOX_Online = new HashSet<string>
        {
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
        };
    }
}