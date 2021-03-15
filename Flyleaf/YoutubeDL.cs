using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

using Newtonsoft.Json;

using SuRGeoNix.Flyleaf.MediaFramework;

namespace SuRGeoNix.Flyleaf
{
    public class YoutubeDL
    {
        public static string plugin_path = "Plugins\\Youtube-dl\\youtube-dl.exe";

        public string json_path { get; private set; }
        public static string GetJsonPath(string url)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "History", "Youtube-DL", BitConverter.ToString(MD5CryptoServiceProvider.Create().ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "").ToLower());
        }
        public static void ParseHeaders(Dictionary<string, string> headers, DecoderContext decoder)
        {
            decoder.Headers     = "";
            decoder.Referer     = "";
            decoder.UserAgent   = "";

            foreach (var hdr in headers)
            {
                if (hdr.Key.ToLower() == "user-agent")
                    decoder.UserAgent = hdr.Value;
                else if (hdr.Key.ToLower() == "referer")
                    decoder.Referer = hdr.Value;
                else
                    decoder.Headers += hdr.Key + ": " + hdr.Value + "\r\n";
            }
        }
        public static bool IsBlackListed(List<string> blacklist, string codec)
        {
            foreach (string codec2 in blacklist)
                if (System.Text.RegularExpressions.Regex.IsMatch(codec, codec2, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;

            return false;
        }
        public static YoutubeDL New(string url, ref int interrupt)
        {
            try
            {
                // Download .Json if not exists already || Disabled should check also expiration timestamps
                string tmpFile = GetJsonPath(url);

                //if (!File.Exists($"{tmpFile}.info.json"))
                //{
                Process proc = new Process 
                {
                    /* --flat-playlist                      Do not extract the videos of a playlist, only list them.
                        * --no-playlist                        Download only the video, if the URL refers to a video and a playlist.
                        * --youtube-skip-dash-manifest         Do not download the DASH manifests and related data on YouTube videos
                        * --merge-output-format FORMAT         If a merge is required (e.g. bestvideo+bestaudio), output to given container format. One of mkv, mp4, ogg, webm, flv. Ignored if no merge is required
                        */
                    StartInfo = new ProcessStartInfo
                    {
                        FileName                = plugin_path,
                        Arguments               = $"--no-check-certificate --skip-download --write-info-json -o \"{tmpFile}\" \"{url}\"",
                        CreateNoWindow          = true,
                        UseShellExecute         = false,
                        //RedirectStandardOutput  = true,
                        //RedirectStandardError = true,
                        WindowStyle             = ProcessWindowStyle.Hidden
                    }
                };
                proc.Start();
                while (!proc.HasExited && interrupt == 0) { Thread.Sleep(35); }
                if (interrupt == 1) { if (!proc.HasExited) proc.Kill(); return null; }

                if (!File.Exists($"{tmpFile}.info.json")) return null;
                //}

                // Parse Json Object
                string json = File.ReadAllText($"{tmpFile}.info.json");
                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.NullValueHandling = NullValueHandling.Ignore;
                YoutubeDL ytdl = JsonConvert.DeserializeObject<YoutubeDL>(json, settings);
                ytdl.json_path = $"{tmpFile}.info.json";
                if (ytdl == null || ytdl.formats == null || ytdl.formats.Count == 0) return null;

                // Fix Nulls (we are not sure if they have audio/video)
                for (int i=0; i<ytdl.formats.Count; i++)
                {
                    if (ytdl.formats[i].vcodec == null) ytdl.formats[i].vcodec = "";
                    if (ytdl.formats[i].acodec == null) ytdl.formats[i].acodec = "";

                    Dump(ytdl.formats[i]);
                }

                return ytdl;
                
            } catch (Exception e) { Console.WriteLine($"[Youtube-DL] Error ... {e.Message}"); }

            return null;
        }
        public static void Dump(Format fmt)
        {
            Console.WriteLine($"ABR:{fmt.abr} VBR:{fmt.vbr} TBR:{fmt.tbr} ACodec: {fmt.acodec} VCodec: {fmt.vcodec} [{fmt.width}x{fmt.height}@{fmt.fps}]");
        }

        public Format GetAudioOnly()
        {
            // Prefer best with no video (dont waste bandwidth)
            for (int i=formats.Count-1; i>= 0; i--)
                if (formats[i].vcodec == "none" && formats[i].acodec.Trim() != "" && formats[i].acodec != "none")
                    return formats[i];

            // Prefer audio from worst video
            for (int i=0; i<formats.Count; i++)
                if (formats[i].acodec.Trim() != "" && formats[i].acodec != "none")
                    return formats[i];

            return null;
        }

        //public int lastScreenWidth, lastScreenHeight;
        public Format GetBestMatch(Control control)
        {
            // TODO: Expose in settings (vCodecs Blacklist) || Create a HW decoding failed list dynamic (check also for whitelist)
            List<string> vCodecsBlacklist = new List<string>() { "vp9" };

            if (control.InvokeRequired)
                return (Format) control.Invoke(new Func<Format>(() => GetBestMatch(control)));
            else
            {
                // Current Screen Resolution | TODO: Check when the control changes screens and get also refresh rate (back to renderer)
                var bounds = Screen.FromControl(control).Bounds;
                //lastScreenWidth = bounds.Width;
                //lastScreenHeight = bounds.Height;

                // Video Streams Order based on Screen Resolution
                var iresults =
                    from    format in formats
                    where   format.height <= bounds.Height && format.vcodec != "none" && (format.protocol == null || !System.Text.RegularExpressions.Regex.IsMatch(format.protocol, "dash", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    orderby format.tbr      descending
                    orderby format.fps      descending
                    orderby format.height   descending
                    orderby format.width    descending
                    select  format;

                if (iresults == null) return formats[formats.Count-1]; // Fallback: Youtube-DL best match
                List<Format> results = iresults.ToList();
                if (results.Count == 0) return null;

                // Best Resolution
                int bestWidth  = (int) results[0].width;
                int bestHeight = (int) results[0].height;

                // Choose from the best resolution (0. with acodec and not blacklisted 1. not blacklisted 2. any)
                int priority = 0;
                while (priority < 3)
                {
                    for (int i=0; i<results.Count; i++)
                    {
                        if (results[i].width != bestWidth || results[i].height != bestHeight) break;

                        if (priority == 0 && !IsBlackListed(vCodecsBlacklist, results[i].vcodec) && results[i].acodec != "none")
                            return results[i];
                        else if (priority == 1 && !IsBlackListed(vCodecsBlacklist, results[i].vcodec))
                            return results[i];
                        else if (priority == 2)
                            return results[i];
                    }

                    priority++;
                }

                return formats[formats.Count-1]; // Fallback: Youtube-DL best match
            }
        }

        public string url { get; set; }
        //public string manifest_url { get; set; }
        //public string fragment_base_url { get; set; }

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
            //public string manifest_url { get; set; } 
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