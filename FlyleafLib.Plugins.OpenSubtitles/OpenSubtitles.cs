using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Web;

using Newtonsoft.Json;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins
{
    public class OpenSubtitles : PluginBase, IPluginSubtitles
    {
        public static int TimeoutSeconds { get; set; } = 15; // TODO config

        Session Session => Player.Session;

        static Dictionary<string, List<OpenSubtitlesJson>> cache = new Dictionary<string, List<OpenSubtitlesJson>>();
        static readonly string  restUrl     = "https://rest.opensubtitles.org/search";
        static readonly string  userAgent   = "Flyleaf v2.0";

        // https://trac.OpenSubtitlesJson.org/projects/OpenSubtitlesJson/wiki/HashSourceCodes
        public static byte[] ComputeMovieHash(string filename)
        {
            byte[] result;
            using (Stream input = File.OpenRead(filename))
            {
                result = ComputeMovieHash(input);
            }
            return result;
        }
        public static byte[] ComputeMovieHash(Stream input, long length = -1)
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

        public static List<OpenSubtitlesJson> SearchByHash(string hash, long length)
        {
            List<OpenSubtitlesJson> subsCopy = new List<OpenSubtitlesJson>();

            if (string.IsNullOrEmpty(hash)) return subsCopy;

            if (cache.ContainsKey(hash + "|" + length))
            {
                foreach (OpenSubtitlesJson sub in cache[hash + "|" + length]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = new TimeSpan(0, 0, TimeoutSeconds) })
            {
                string resp = "";
                List<OpenSubtitlesJson> subs = new List<OpenSubtitlesJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    Log($"Searching for /moviebytesize-{length}/moviehash-{hash}");
                    resp = client.PostAsync($"{restUrl}/moviebytesize-{length}/moviehash-{hash}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesJson>>(resp);
                    Log($"Search Results {subs.Count}");
                    cache.Add(hash + "|" + length, subs);
                    foreach (OpenSubtitlesJson sub in subs) subsCopy.Add(sub);
                } catch (Exception e) { Log($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public static List<OpenSubtitlesJson> SearchByIMDB(string imdbid, Language lang = null, string season = null, string episode = null)
        {
            if (lang == null) lang = Language.Get("eng");
            List<OpenSubtitlesJson> subsCopy = new List<OpenSubtitlesJson>();

            if (cache.ContainsKey(imdbid + "|" + season + "|" + episode + lang.IdSubLanguage))
            {
                foreach (OpenSubtitlesJson sub in cache[imdbid + "|" + season + "|" + episode + lang.IdSubLanguage]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = new TimeSpan(0, 0, TimeoutSeconds) })
            {
                string resp = "";
                List<OpenSubtitlesJson> subs = new List<OpenSubtitlesJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    string qSeason  = season  != null ? $"/season-{season}"   : "";
                    string qEpisode = episode != null ? $"/episode-{episode}" : "";
                    string query = $"{qEpisode}/imdbid-{imdbid}{qSeason}/sublanguageid-{lang.IdSubLanguage}";

                    Log($"Searching for {query}");
                    resp = client.PostAsync($"{restUrl}{query}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesJson>>(resp);
                    Log($"Search Results {subs.Count}");

                    cache.Add(imdbid + "|" + season + "|" + episode + lang.IdSubLanguage, subs);
                    foreach (OpenSubtitlesJson sub in subs) subsCopy.Add(sub);
                } catch (Exception e) { Log($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public static List<OpenSubtitlesJson> SearchByName(string name, Language lang = null)
        {
            if (lang == null) lang = Language.Get("eng");
            List<OpenSubtitlesJson> subsCopy = new List<OpenSubtitlesJson>();

            if (cache.ContainsKey(name + "|" + lang.IdSubLanguage))
            {
                foreach (OpenSubtitlesJson sub in cache[name + "|" + lang.IdSubLanguage]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = new TimeSpan(0, 0, TimeoutSeconds) })
            {
                string resp = "";
                List<OpenSubtitlesJson> subs = new List<OpenSubtitlesJson>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                { 
                    Log($"Searching for /query-{HttpUtility.UrlEncode(name.Replace('.', ' '))}/sublanguageid-{lang.IdSubLanguage}");
                    resp = client.PostAsync($"{restUrl}/query-{HttpUtility.UrlEncode(name.Replace('.', ' '))}/sublanguageid-{lang.IdSubLanguage}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitlesJson>>(resp);
                    Log($"Search Results {subs.Count}");

                    cache.Add(name + "|" + lang.IdSubLanguage, subs);
                    foreach (OpenSubtitlesJson sub in subs) subsCopy.Add(sub);
                } catch (Exception e) { Log($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public void Search(string filename, string hash, long length, List<Language> Languages)
        {
            List<OpenSubtitlesJson> subs = SearchByHash(hash, length);

            bool imdbExists = subs != null && subs.Count > 0 && subs[0].IDMovieImdb != null && subs[0].IDMovieImdb.Trim() != "";
            bool isEpisode  = imdbExists && subs[0].SeriesSeason != null && subs[0].SeriesSeason.Trim() != "" && subs[0].SeriesSeason.Trim() != "0" && subs[0].SeriesEpisode != null && subs[0].SeriesEpisode.Trim() != "" && subs[0].SeriesEpisode.Trim() != "0";

            foreach (Language lang in Languages)
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
            List<OpenSubtitlesJson> uniqueList = new List<OpenSubtitlesJson>();
            List<int> removeIds = new List<int>();
            for (int i=0; i<subs.Count-1; i++)
            {
                if (removeIds.Contains(i)) continue;

                for (int l=i+1; l<subs.Count; l++)
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
            for (int i=0; i<subs.Count; i++)
                if (!removeIds.Contains(i)) uniqueList.Add(subs[i]);

            subs.Clear();
            foreach (Language lang in Languages)
            {
                IEnumerable<OpenSubtitlesJson> movieHashRating = 
                    from sub in uniqueList
                    where sub.ISO639 != null && sub.ISO639 == lang.ISO639 && sub.MatchedBy.ToLower() == "moviehash"
                    orderby float.Parse(sub.SubRating) descending
                    select sub;

                IEnumerable<OpenSubtitlesJson> rating = 
                    from sub in uniqueList
                    where sub.ISO639 != null && sub.ISO639 == lang.ISO639 && sub.MatchedBy.ToLower() != "moviehash"
                    orderby float.Parse(sub.SubRating) descending
                    select sub;

                foreach (var t1 in movieHashRating) subs.Add(t1);
                foreach (var t1 in rating) subs.Add(t1);
            }

            foreach(var sub in subs)
                SubtitlesStreams.Add(new SubtitlesStream()
                {
                    UrlName     = sub.SubFileName,
                    Rating      = sub.SubRating,
                    Language    = Language.Get(sub.ISO639),
                    Tag         = sub
                });
        }

        public override void OnInitialized()
        {
            base.OnInitialized();
            searchOnceTmp = false;
        }

        public override void OnInitializingSwitch()
        {
            base.OnInitializingSwitch();
            OnInitialized();
        }

        bool searchOnceTmp;
        public void Search(Language lang)
        {
            if (searchOnceTmp || Player.Config.subs.UseOnlineDatabases == false || (Session.Movie.UrlType != UrlType.File && Session.Movie.UrlType != UrlType.Torrent)) return;
            searchOnceTmp = true; // Should be recoded to search by lang (so if a priority already found will skip the rest)

            string hash = null;
            if (Player.Session.Movie.FileSize != 0)
                if (Session.CurVideoStream != null && Session.CurVideoStream.Stream != null)
                {
                    var savePos = Session.CurVideoStream.Stream.Position;
                    hash  = Utils.ToHexadecimal(ComputeMovieHash(Session.CurVideoStream.Stream, Session.Movie.FileSize));
                    Session.CurVideoStream.Stream.Position = savePos;
                }
                else
                    hash = Utils.ToHexadecimal(ComputeMovieHash(Session.Movie.Url));

            Search(Session.Movie.Title, hash, Session.Movie.FileSize, Player.Config.subs.Languages);
        }

        public bool Download(SubtitlesStream stream)
        {
            if (stream.Tag == null || !(stream.Tag is OpenSubtitlesJson)) return false;

            var sub = (OpenSubtitlesJson) stream.Tag;
            if (sub.Download() != 0) return false;

            stream.Url = sub.AvailableAt;

            return true;
        }

        SubtitlesStream IPluginSubtitles.OpenSubtitles(Language lang)
        {
            foreach(var stream in SubtitlesStreams)
                if (stream.Language == lang) return stream;

            return null;
        }

        SubtitlesStream IPluginSubtitles.OpenSubtitles(SubtitlesStream stream)
        {
            if (stream.Tag == null || !(stream.Tag is OpenSubtitlesJson)) return null;

            return stream;
        }

        internal static void Log(string msg) { System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [OpenSubtitles] {msg}"); }
    }
}