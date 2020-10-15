using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
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
            input.Close();
            byte[] result = BitConverter.GetBytes(lhash);
            Array.Reverse(result);
            return result;
        }

        // https://github.com/Valyreon/Subloader
        public static List<OpenSubtitles> SearchByHash(string hash, long length, Language lang = null)
        {
            if (lang == null) lang = Language.Get("eng");

            if (cache.ContainsKey(hash + "|" + length + "|" + lang.IdSubLanguage))
            {
                List<OpenSubtitles> subsCopy = new List<OpenSubtitles>();
                foreach (OpenSubtitles sub in cache[hash + "|" + length + "|" + lang.IdSubLanguage]) subsCopy.Add(sub);

                return subsCopy;
            }

            using (HttpClient client = new HttpClient { Timeout = new TimeSpan(0, 0, 0, 0, -1) })
            {
                string resp = "";
                List<OpenSubtitles> subs = new List<OpenSubtitles>();

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Add("X-User-Agent", userAgent);

                Log($"Searching for /moviebytesize-{length}/moviehash-{hash}/sublanguageid-{lang.IdSubLanguage}");
                resp = client.PostAsync($"{restUrl}/moviebytesize-{length}/moviehash-{hash}/sublanguageid-{lang.IdSubLanguage}", null).Result.Content.ReadAsStringAsync().Result;
                subs = JsonConvert.DeserializeObject<List<OpenSubtitles>>(resp);
                Log($"Search Results {subs.Count}");

                cache.Add(hash + "|" + length + "|" + lang.IdSubLanguage, subs);

                List<OpenSubtitles> subsCopy = new List<OpenSubtitles>();
                foreach (OpenSubtitles sub in subs) subsCopy.Add(sub);

                return subsCopy;
            }
        }
        public static List<OpenSubtitles> SearchByName(string name, Language lang = null)
        {
            if (lang == null) lang = Language.Get("eng");

            if (cache.ContainsKey(name + "|" + lang.IdSubLanguage))
            {
                List<OpenSubtitles> subsCopy = new List<OpenSubtitles>();
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

                Log($"Searching for /query-{HttpUtility.UrlEncode(file.Name.Replace('.', ' '))}/sublanguageid-{lang.IdSubLanguage}");
                resp = client.PostAsync($"{restUrl}/query-{HttpUtility.UrlEncode(file.Name.Replace('.', ' '))}/sublanguageid-{lang.IdSubLanguage}", null).Result.Content.ReadAsStringAsync().Result;
                subs = JsonConvert.DeserializeObject<List<OpenSubtitles>>(resp);
                Log($"Search Results {subs.Count}");

                cache.Add(name + "|" + lang.IdSubLanguage, subs);

                List<OpenSubtitles> subsCopy = new List<OpenSubtitles>();
                foreach (OpenSubtitles sub in subs) subsCopy.Add(sub);

                return subsCopy;
            }
        }

        public string Download() // Download -> Unzip -> ConvertToUtf8
        {
            using (var client = new WebClient())
            {
                string zipPath = Path.GetTempPath() + SubFileName + "_" + SubLanguageID + ".srt.gz";

                File.Delete(zipPath);
                File.Delete(zipPath.Substring(0,zipPath.Length - 3));

                Log($"Downloading {zipPath}");
                client.DownloadFile(new Uri(SubDownloadLink), zipPath);

                Log($"Unzipping {zipPath}");
                string unzipPath = Utils.GZipDecompress(zipPath);
                Log($"Unzipped at {unzipPath}");

                Encoding subsEnc = Subtitles.Detect(unzipPath);
                Log($"Converting {subsEnc.ToString()} to UTF8");
                if (subsEnc != Encoding.UTF8) unzipPath = Subtitles.Convert(unzipPath, subsEnc, Encoding.UTF8);

                AvailableAt = unzipPath;

                return unzipPath;
            }
        }
        public bool Equals(OpenSubtitles other)
        {
            if (SubHash == other.SubHash)
                return true;

            return false;
        }


        public string AvailableAt       { get; set; }


        [JsonProperty("IDSubMovieFile")]
        public string IDSubMovieFile    { get; set; }

        [JsonProperty("MovieHash")]
        public string MovieHash         { get; set; }

        [JsonProperty("MovieByteSize")]
        public string MovieByteSize     { get; set; }

        [JsonProperty("IDSubtitleFile")]
        public string IDSubtitleFile    { get; set; }

        [JsonProperty("SubFileName")]
        public string SubFileName       { get; set; }

        [JsonProperty("SubHash")]
        public string SubHash           { get; set; }

        [JsonProperty("IDSubtitle")]
        public string IDSubtitle        { get; set; }

        [JsonProperty("SubLanguageID")]
        public string SubLanguageID     { get; set; }

        [JsonProperty("SubFormat")]
        public string SubFormat         { get; set; }

        [JsonProperty("SubRating")]
        public string SubRating         { get; set; }

        [JsonProperty("SubDownloadsCnt")]
        public string SubDownloadsCnt   { get; set; }

        [JsonProperty("IDMovie")]
        public string IDMovie           { get; set; }

        [JsonProperty("IDMovieImdb")]
        public string IDMovieImdb       { get; set; }

        [JsonProperty("LanguageName")]
        public string LanguageName      { get; set; }

        [JsonProperty("SubDownloadLink")]
        public string SubDownloadLink   { get; set; }

        [JsonProperty("ZipDownloadLink")]
        public string ZipDownloadLink   { get; set; }

        [JsonProperty("SubtitlesLink")]
        public string SubtitlesLink     { get; set; }
    }
}
