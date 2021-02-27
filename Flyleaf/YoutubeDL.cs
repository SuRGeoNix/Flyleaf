using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Newtonsoft.Json;

namespace SuRGeoNix.Flyleaf
{
    public class YoutubeDL
    {
        public static string plugin_path = "Plugins\\Youtube-dl\\youtube-dl.exe";
        public static YoutubeDL Get(string url, out string aUrlBest, out string vUrlBest, string referer) // will add .info.json
        {
            aUrlBest = null;
            vUrlBest = null;

            string tmpFile = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());

            Process proc = new Process 
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName                = plugin_path,
                    Arguments               = $"--referer \"{referer}\" --no-check-certificate --skip-download --write-info-json -o \"{tmpFile}\" \"{url}\"",
                    CreateNoWindow          = true,
                    UseShellExecute         = false,
                    RedirectStandardOutput  = true,
                    //RedirectStandardError = true,
                    WindowStyle             = ProcessWindowStyle.Hidden
                }
            };
            proc.Start();
            proc.WaitForExit();

            //string lines = "";
            //while (!proc.StandardOutput.EndOfStream)
            //    lines += proc.StandardOutput.ReadLine() + "\r\n";
            //Console.WriteLine(lines);

            if (!File.Exists($"{tmpFile}.info.json")) return null;

            string json = File.ReadAllText($"{tmpFile}.info.json");
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.NullValueHandling = NullValueHandling.Ignore;

            YoutubeDL ytdl = JsonConvert.DeserializeObject<YoutubeDL>(json, settings);

            if (ytdl == null || ytdl.formats == null || ytdl.formats.Count == 0) return null;

            for (int i=0; i<ytdl.formats.Count; i++) Dump(ytdl.formats[i]);

            // TODO: To let user choose formats

            //var aUrlsI =
            //    from format in ytdl.formats
            //    where format.abr == 192
            //    orderby format.abr descending
            //    select format;

            //var vUrlsI =
            //    from format in ytdl.formats
            //    where format.height == 1080
            //    orderby format.height descending
            //    select format;

            //aUrlBest = aUrlsI.ToList()[0].url;
            //vUrlBest = vUrlsI.ToList()[0].url;
            //Console.WriteLine("Will use ...");
            //Dump(aUrlsI.ToList()[0]);
            //Dump(vUrlsI.ToList()[0]);

            //var aUrlsI = 
            //    from format in ytdl.formats
            //    where format.abr > 0
            //    orderby format.abr descending
            //    select format;

            //var vUrlsI = 
            //    from format in ytdl.formats
            //    where format.height > 0
            //    orderby format.height descending
            //    select format;

            //Console.WriteLine("Will use ...");
            //List<Format> aUrls = aUrlsI.ToList();
            //List<Format> vUrls = vUrlsI.ToList();
            //if (aUrls.Count != 0)
            //{
            //    aUrlBest = aUrls[0].url;
            //    Dump(aUrls[0]);
            //}

            //if (vUrls.Count != 0)
            //{
            //    vUrlBest = vUrls.ToList()[0].url;
            //    Dump(vUrls[0]);
            //}

            aUrlBest = null;
            vUrlBest = ytdl.formats[ytdl.formats.Count - 1].url;
            Console.WriteLine("Will use ...");
            Dump(ytdl.formats[ytdl.formats.Count - 1]);

            return ytdl;
        }

        public static void Dump(Format fmt)
        {
            Console.WriteLine($"ABR:{fmt.abr} VBR:{fmt.vbr} TBR:{fmt.tbr} ACodec: {fmt.acodec} VCodec: {fmt.vcodec} [{fmt.width}x{fmt.height}@{fmt.fps}]");
        }

        public object series { get; set; } 
        public string format { get; set; } 
        public List<string> tags { get; set; } 
        public string channel_id { get; set; } 
        public string webpage_url_basename { get; set; } 
        public object artist { get; set; } 
        public List<Format> formats { get; set; } 
        public object chapters { get; set; } 
        public object release_year { get; set; } 
        public object album { get; set; } 
        public object annotations { get; set; } 
        public string upload_date { get; set; } 
        public string extractor_key { get; set; } 
        public object is_live { get; set; } 
        public double height { get; set; } 
        public string uploader { get; set; } 
        public List<Thumbnail> thumbnails { get; set; } 
        public object resolution { get; set; } 
        public string extractor { get; set; } 
        public object vbr { get; set; } 
        public double view_count { get; set; } 
        public double duration { get; set; } 
        public string description { get; set; } 
        public object requested_subtitles { get; set; } 
        public object episode_number { get; set; } 
        public double abr { get; set; } 
        public object release_date { get; set; } 
        public object start_time { get; set; } 
        public object alt_title { get; set; } 
        public object creator { get; set; } 
        public string acodec { get; set; } 
        public float fps { get; set; } 
        public string thumbnail { get; set; } 
        public double average_rating { get; set; } 
        public string ext { get; set; } 
        public string _filename { get; set; } 
        public Subtitles subtitles { get; set; } 
        public object dislike_count { get; set; } 
        public string uploader_id { get; set; } 
        public object end_time { get; set; } 
        public object playlist { get; set; } 
        public object license { get; set; } 
        public string format_id { get; set; } 
        public string channel_url { get; set; } 
        public double width { get; set; } 
        public string id { get; set; } 
        public string display_id { get; set; } 
        public object track { get; set; } 
        public string uploader_url { get; set; } 
        public string title { get; set; } 
        public AutomaticCaptions automatic_captions { get; set; } 
        public object season_number { get; set; } 
        public string webpage_url { get; set; } 
        public object like_count { get; set; } 
        public List<string> categories { get; set; } 
        public string fulltitle { get; set; } 
        public double age_limit { get; set; } 
        public string vcodec { get; set; } 
        public object playlist_index { get; set; } 
        public object stretched_ratio { get; set; } 

        public class Format
        {
            public string url { get; set; } 
            public string manifest_url { get; set; } 
            public object player_url { get; set; } 


            public double   asr { get; set; } 
            public double   abr { get; set; } 
            public double   vbr { get; set; } 
            public double   tbr { get; set; } 

            public string acodec { get; set; } 
            public string vcodec { get; set; } 

            public double width     { get; set; } 
            public double height    { get; set; }
            public double fps       { get; set; } 


            public string format { get; set; } 
            public string format_id { get; set; } 
            public string format_note { get; set; } 
            public string ext { get; set; } 
            public long filesize { get; set; } 
            public string protocol { get; set; } 
            public string container { get; set; } 
            public string language { get; set; } 
            public Dictionary<string, string> downloader_options { get; set; } 
            public Dictionary<string, string> http_headers { get; set; }

            public string fragment_base_url { get; set; } 
            public List<Fragment> fragments { get; set; }
        }

            public class Fragment    {
        public string path { get; set; } 
        public double duration { get; set; } 
    }
        
        public class Thumbnail    {
            public string url { get; set; } 
            public string resolution { get; set; } 
            public double height { get; set; } 
            public string id { get; set; } 
            public double width { get; set; } 
        }

        public class Subtitles    {
        }

        public class AutomaticCaptions    {
        }
    }
}
