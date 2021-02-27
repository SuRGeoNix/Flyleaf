using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Web;

using Newtonsoft.Json;

namespace SuRGeoNix.Flyleaf
{
    public class OpenSubtitles
    {
        static void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("H.mm.ss.fff")}] [OPENSUBS] {msg}"); }

        static readonly string  restUrl     = "https://rest.opensubtitles.org/search";
        static readonly string  userAgent   = "Flyleaf v2.0";

        static Dictionary<string, List<OpenSubtitles>> cache = new Dictionary<string, List<OpenSubtitles>>();

        // https://trac.opensubtitles.org/projects/opensubtitles/wiki/HashSourceCodes
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

        public static List<OpenSubtitles> SearchByHash2(string hash, long length)
        {
            List<OpenSubtitles> subsCopy = new List<OpenSubtitles>();

            if (cache.ContainsKey(hash + "|" + length))
            {
                foreach (OpenSubtitles sub in cache[hash + "|" + length]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = new TimeSpan(0, 0, 0, 0, -1) })
            {
                string resp = "";
                List<OpenSubtitles> subs = new List<OpenSubtitles>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    Log($"Searching for /moviebytesize-{length}/moviehash-{hash}");
                    resp = client.PostAsync($"{restUrl}/moviebytesize-{length}/moviehash-{hash}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitles>>(resp);
                    Log($"Search Results {subs.Count}");
                    cache.Add(hash + "|" + length, subs);
                    foreach (OpenSubtitles sub in subs) subsCopy.Add(sub);
                } catch (Exception e) { Log($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public static List<OpenSubtitles> SearchByIMDB(string imdbid, Language lang = null, string season = null, string episode = null)
        {
            if (lang == null) lang = Language.Get("eng");
            List<OpenSubtitles> subsCopy = new List<OpenSubtitles>();

            if (cache.ContainsKey(imdbid + "|" + season + "|" + episode + lang.IdSubLanguage))
            {
                foreach (OpenSubtitles sub in cache[imdbid + "|" + season + "|" + episode + lang.IdSubLanguage]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = new TimeSpan(0, 0, 0, 0, -1) })
            {
                string resp = "";
                List<OpenSubtitles> subs = new List<OpenSubtitles>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    string qSeason  = season  != null ? $"/season-{season}"   : "";
                    string qEpisode = episode != null ? $"/episode-{episode}" : "";
                    string query = $"{qEpisode}/imdbid-{imdbid}{qSeason}/sublanguageid-{lang.IdSubLanguage}";

                    Log($"Searching for {query}");
                    resp = client.PostAsync($"{restUrl}{query}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitles>>(resp);
                    Log($"Search Results {subs.Count}");

                    cache.Add(imdbid + "|" + season + "|" + episode + lang.IdSubLanguage, subs);
                    foreach (OpenSubtitles sub in subs) subsCopy.Add(sub);
                } catch (Exception e) { Log($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public static List<OpenSubtitles> SearchByHash(string hash, long length, Language lang = null)
        {
            if (lang == null) lang = Language.Get("eng");
            List<OpenSubtitles> subsCopy = new List<OpenSubtitles>();

            if (cache.ContainsKey(hash + "|" + length + "|" + lang.IdSubLanguage))
            {
                foreach (OpenSubtitles sub in cache[hash + "|" + length + "|" + lang.IdSubLanguage]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = new TimeSpan(0, 0, 0, 0, -1) })
            {
                string resp = "";
                List<OpenSubtitles> subs = new List<OpenSubtitles>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                {
                    Log($"Searching for /moviebytesize-{length}/moviehash-{hash}/sublanguageid-{lang.IdSubLanguage}");
                    resp = client.PostAsync($"{restUrl}/moviebytesize-{length}/moviehash-{hash}/sublanguageid-{lang.IdSubLanguage}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitles>>(resp);
                    Log($"Search Results {subs.Count}");
                    cache.Add(hash + "|" + length + "|" + lang.IdSubLanguage, subs);
                    foreach (OpenSubtitles sub in subs) subsCopy.Add(sub);
                } catch (Exception e) { Log($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }
        public static List<OpenSubtitles> SearchByName(string name, Language lang = null)
        {
            if (lang == null) lang = Language.Get("eng");
            List<OpenSubtitles> subsCopy = new List<OpenSubtitles>();

            if (cache.ContainsKey(name + "|" + lang.IdSubLanguage))
            {
                foreach (OpenSubtitles sub in cache[name + "|" + lang.IdSubLanguage]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = new TimeSpan(0, 0, 0, 0, -1) })
            {
                string resp = "";
                FileInfo file = new FileInfo(name);
                List<OpenSubtitles> subs = new List<OpenSubtitles>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                try
                { 
                    Log($"Searching for /query-{HttpUtility.UrlEncode(file.Name.Replace('.', ' '))}/sublanguageid-{lang.IdSubLanguage}");
                    resp = client.PostAsync($"{restUrl}/query-{HttpUtility.UrlEncode(file.Name.Replace('.', ' '))}/sublanguageid-{lang.IdSubLanguage}", null).Result.Content.ReadAsStringAsync().Result;
                    subs = JsonConvert.DeserializeObject<List<OpenSubtitles>>(resp);
                    Log($"Search Results {subs.Count}");

                    cache.Add(name + "|" + lang.IdSubLanguage, subs);
                    foreach (OpenSubtitles sub in subs) subsCopy.Add(sub);
                } catch (Exception e) { Log($"Error fetching subtitles {e.Message} - {e.StackTrace}"); }

                return subsCopy;
            }
        }

        public void Download(string encoding = "utf8") // Download -> Unzip -> ConvertToUtf8
        {
            try
            {
                using (var client = new WebClient())
                {
                    // Too many invalid converts to utf8 with opensubtitles (SubEncoding is not trustworthy)
                    //string subDownloadLinkEnc = SubDownloadLink.Replace("/download/", $"/download/subencoding-{encoding}/");
                    string subDownloadLinkEnc = SubDownloadLink;

                    string zipPath = Path.GetTempPath() + SubFileName + "." + SubLanguageID + ".srt.gz";

                    File.Delete(zipPath);
                    File.Delete(zipPath.Substring(0,zipPath.Length - 3));

                    Log($"Downloading {zipPath}");
                    client.DownloadFile(new Uri(subDownloadLinkEnc), zipPath);

                    Log($"Unzipping {zipPath}");
                    string unzipPath = Utils.GZipDecompress(zipPath);
                    Log($"Unzipped at {unzipPath}");

                    //Encoding subsEnc = Subtitles.Detect(unzipPath);
                    //Log($"Converting {subsEnc.ToString()} to UTF8");
                    //if (subsEnc != Encoding.UTF8) unzipPath = Subtitles.Convert(unzipPath, subsEnc, Encoding.UTF8);

                    AvailableAt = unzipPath;

                    //return unzipPath;
                }
            } catch (Exception e) { AvailableAt = null; Log($"Failed to download subtitle {SubFileName} - {e.Message}"); }

            
            //return null;
        }
        public bool Equals(OpenSubtitles other)
        {
            if (SubHash == other.SubHash)
                return true;

            return false;
        }


        public string AvailableAt       { get; set; }


        public string MatchedBy { get; set; } 
        public string IDSubMovieFile { get; set; } 
        [System.Xml.Serialization.XmlIgnore]
        public string MovieHash { get; set; } 
        public string MovieByteSize { get; set; } 
        public string MovieTimeMS { get; set; } 
        public string IDSubtitleFile { get; set; } 
        public string SubFileName { get; set; } 
        public string SubActualCD { get; set; } 
        public string SubSize { get; set; } 
        public string SubHash { get; set; } 
        public string SubLastTS { get; set; } 
        public string SubTSGroup { get; set; } 
        public string InfoReleaseGroup { get; set; } 
        public string InfoFormat { get; set; } 
        public string InfoOther { get; set; } 
        public string IDSubtitle { get; set; } 
        public string UserID { get; set; } 
        public string SubLanguageID { get; set; } 
        public string SubFormat { get; set; } 
        public string SubSumCD { get; set; } 
        public string SubAuthorComment { get; set; } 
        public string SubAddDate { get; set; } 
        public string SubBad { get; set; } 
        public string SubRating { get; set; } 
        public string SubSumVotes { get; set; } 
        public string SubDownloadsCnt { get; set; } 
        public string MovieReleaseName { get; set; } 
        public string MovieFPS { get; set; } 
        public string IDMovie { get; set; } 
        public string IDMovieImdb { get; set; } 
        public string MovieName { get; set; } 
        public string MovieNameEng { get; set; } 
        public string MovieYear { get; set; } 
        public string MovieImdbRating { get; set; } 
        public string SubFeatured { get; set; } 
        public string UserNickName { get; set; } 
        public string SubTranslator { get; set; } 
        public string ISO639 { get; set; } 
        public string LanguageName { get; set; } 
        public string SubComments { get; set; } 
        public string SubHearingImpaired { get; set; } 
        public string UserRank { get; set; } 
        public string SeriesSeason { get; set; } 
        public string SeriesEpisode { get; set; } 
        public string MovieKind { get; set; } 
        public string SubHD { get; set; } 
        public string SeriesIMDBParent { get; set; } 
        public string SubEncoding { get; set; } 
        public string SubAutoTranslation { get; set; } 
        public string SubForeignPartsOnly { get; set; } 
        public string SubFromTrusted { get; set; } 
        public string SubTSGroupHash { get; set; } 
        public string SubDownloadLink { get; set; } 
        public string ZipDownloadLink { get; set; } 
        public string SubtitlesLink { get; set; } 
        public double Score { get; set; }
    }
}
