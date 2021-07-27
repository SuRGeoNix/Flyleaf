using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;

using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaStream;

using static FlyleafLib.Plugins.YoutubeDLJson;

namespace FlyleafLib.Plugins
{
    public class YoutubeDL : PluginBase, IPluginVideo, IPluginAudio
    {
        public bool IsPlaylist => false;
        Session Session => Player.Session;

        YoutubeDLJson ytdl;

        public static string plugin_path = "Plugins\\YoutubeDL\\youtube-dl.exe";

        static JsonSerializerSettings settings = new JsonSerializerSettings();
        static YoutubeDL() { settings.NullValueHandling = NullValueHandling.Ignore; }

        public override void OnLoad() { }

        public override void OnInitialized()
        {
            base.OnInitialized();
            ytdl = null;
        }

        public OpenVideoResults OpenVideo()
        {
            Uri uri;
            try
            {
                uri = new Uri(Session.InitialUrl);
                if ((uri.Scheme.ToLower() != "http" && uri.Scheme.ToLower() != "https") || Utils.GetUrlExtention(uri.AbsolutePath).ToLower() == "m3u8") return null;
            } catch (Exception) { return null; }

            try
            {
                string url = Session.InitialUrl;
                Session.SingleMovie.UrlType = UrlType.Web;
                if (Regex.IsMatch(uri.DnsSafeHost, @"\.youtube\.", RegexOptions.IgnoreCase))
                {
                    var query = HttpUtility.ParseQueryString(uri.Query);
                    url = uri.Scheme + "://" + uri.Host + uri.AbsolutePath + "?v=" + query["v"];
                }

                string tmpFile = Path.GetTempPath() + Guid.NewGuid().ToString();

                Process proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = plugin_path,
                        Arguments = $"--no-check-certificate --skip-download --write-info-json -o \"{tmpFile}\" \"{url}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };
                proc.Start();
                while (!proc.HasExited && Player.Status == Status.Opening) { Thread.Sleep(35); }
                if (Player.Status != Status.Opening) { if (!proc.HasExited) proc.Kill(); return null; }
                if (!File.Exists($"{tmpFile}.info.json")) return null;

                // Parse Json Object
                string json = File.ReadAllText($"{tmpFile}.info.json");
                ytdl = JsonConvert.DeserializeObject<YoutubeDLJson>(json, settings);
                if (ytdl == null || ytdl.formats == null || ytdl.formats.Count == 0) return null;

                Format fmt;
                // Fix Nulls (we are not sure if they have audio/video)
                for (int i = 0; i < ytdl.formats.Count; i++)
                {
                    fmt = ytdl.formats[i];
                    if (ytdl.formats[i].vcodec == null) ytdl.formats[i].vcodec = "";
                    if (ytdl.formats[i].acodec == null) ytdl.formats[i].acodec = "";

                    if (HasVideo(fmt))
                    {
                        VideoStreams.Add(new VideoStream()
                        {
                            Url = fmt.url,
                            BitRate = (long) fmt.vbr,
                            CodecName = fmt.vcodec,
                            Language = Language.Get(fmt.language),
                            Width = (int) fmt.width,
                            Height = (int) fmt.height,
                            FPS = fmt.fps
                        });
                    }

                    if (HasAudio(fmt))
                    {
                        AudioStreams.Add(new AudioStream()
                        {
                            Url = fmt.url,
                            BitRate = (long) fmt.abr,
                            CodecName = fmt.acodec,
                            Language = Language.Get(fmt.language)
                        });
                    }
                    
                }

                fmt = GetBestMatch();

                Player.Session.SingleMovie.Title = ytdl.title;

                foreach(var t1 in VideoStreams)
                    if (fmt.url == t1.Url) return new OpenVideoResults(t1);

            }
            catch (Exception e) { Debug.WriteLine($"[Youtube-DL] Error ... {e.Message}"); }

            return new OpenVideoResults();
        }
        public AudioStream OpenAudio()
        {
            var fmt = GetAudioOnly();
            foreach(var t1 in AudioStreams)
                if (fmt.url == t1.Url) return t1;

            return null;
        }

        public VideoStream OpenVideo(VideoStream stream)
        {
            if (string.IsNullOrEmpty(stream.Url)) return null;

            foreach(var vstream in VideoStreams)
                if (vstream.Url == stream.Url) return vstream;

            return null;
        }
        public AudioStream OpenAudio(AudioStream stream)
        {
            if (string.IsNullOrEmpty(stream.Url)) return null;

            foreach(var astream in AudioStreams)
                if (astream.Url == stream.Url) return astream;

            return null;
        }

        private Format GetAudioOnly()
        {
            // Prefer best with no video (dont waste bandwidth)
            for (int i = ytdl.formats.Count - 1; i >= 0; i--)
                if (ytdl.formats[i].vcodec == "none" && ytdl.formats[i].acodec.Trim() != "" && ytdl.formats[i].acodec != "none")
                    return ytdl.formats[i];

            // Prefer audio from worst video
            for (int i = 0; i < ytdl.formats.Count; i++)
                if (ytdl.formats[i].acodec.Trim() != "" && ytdl.formats[i].acodec != "none")
                    return ytdl.formats[i];

            return null;
        }
        private Format GetBestMatch()
        {
            // TODO: Expose in settings (vCodecs Blacklist) || Create a HW decoding failed list dynamic (check also for whitelist)
            List<string> vCodecsBlacklist = new List<string>() { "vp9" };

            // Current Screen Resolution | TODO: Check when the control changes screens and get also refresh rate (back to renderer)
            var bounds = Player.renderer.Info.ScreenBounds;

            // Video Streams Order based on Screen Resolution
            var iresults =
                from    format in ytdl.formats
                where   format.height <= bounds.Height && format.vcodec != "none" && (format.protocol == null || !Regex.IsMatch(format.protocol, "dash", RegexOptions.IgnoreCase))
                orderby format.tbr      descending
                orderby format.fps      descending
                orderby format.height   descending
                orderby format.width    descending
                select  format;

            if (iresults == null) return ytdl.formats[ytdl.formats.Count - 1]; // Fallback: Youtube-DL best match
            List<Format> results = iresults.ToList();
            if (results.Count == 0) return null;

            // Best Resolution
            int bestWidth = (int)results[0].width;
            int bestHeight = (int)results[0].height;

            // Choose from the best resolution (0. with acodec and not blacklisted 1. not blacklisted 2. any)
            int priority = 0;
            while (priority < 3)
            {
                for (int i = 0; i < results.Count; i++)
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

            return ytdl.formats[ytdl.formats.Count - 1]; // Fallback: Youtube-DL best match
        }
        private static bool IsBlackListed(List<string> blacklist, string codec)
        {
            foreach (string codec2 in blacklist)
                if (Regex.IsMatch(codec, codec2, RegexOptions.IgnoreCase))
                    return true;

            return false;
        }
        private static bool HasVideo(Format fmt) { if ((fmt.height > 0 || fmt.vbr > 0 || (fmt.abr == 0 && (string.IsNullOrEmpty(fmt.acodec) || fmt.acodec != "none"))) || (!string.IsNullOrEmpty(fmt.vcodec) && fmt.vcodec != "none")) return true; return false; }
        private static bool HasAudio(Format fmt) { if (fmt.abr > 0 ||  (!string.IsNullOrEmpty(fmt.acodec) && fmt.acodec != "none")) return true; return false; }
    }
}