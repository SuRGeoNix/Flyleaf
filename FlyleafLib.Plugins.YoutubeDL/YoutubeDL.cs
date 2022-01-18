using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;

using Newtonsoft.Json;

using FlyleafLib.MediaFramework.MediaInput;

namespace FlyleafLib.Plugins
{
    public class YoutubeDL : PluginBase, IOpen, IProvideAudio, IProvideVideo, IProvideSubtitles, ISuggestAudioInput, ISuggestVideoInput, ISuggestSubtitlesInput
    {
        public List<AudioInput>     AudioInputs     { get; set; } = new List<AudioInput>();
        public List<VideoInput>     VideoInputs     { get; set; } = new List<VideoInput>();
        public List<SubtitlesInput> SubtitlesInputs { get; set; } = new List<SubtitlesInput>();

        public bool                 IsPlaylist      => false;
        public new int              Priority        { get; set; } = 1999;

        static string               plugin_path     = "yt-dlp.exe";

        static JsonSerializerSettings
                                    jsonSettings    = new JsonSerializerSettings();
        static string               defaultBrowser;

        int retries;
        YoutubeDLJson               ytdl;

        static YoutubeDL()
        {
            // Default priority of which browser's cookies will be used (default profile)
            // https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/cookies.py

            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appdataroaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            if (Directory.Exists(Path.Combine(appdata, @"BraveSoftware\Brave-Browser\User Data")))
                defaultBrowser = "brave";
            else if (Directory.Exists(Path.Combine(appdata, @"Google\Chrome\User Data")))
                defaultBrowser = "chrome";
            else if (Directory.Exists(Path.Combine(appdata, @"Mozilla\Firefox\Profiles")))
                defaultBrowser = "firefox";
            else if (Directory.Exists(Path.Combine(appdataroaming, @"Opera Software\Opera Stable")))
                defaultBrowser = "opera";
            else if (Directory.Exists(Path.Combine(appdata, @"Vivaldi\User Data")))
                defaultBrowser = "vivaldi";
            else if (Directory.Exists(Path.Combine(appdata, @"Chromium\User Data")))
                defaultBrowser = "chromium";
            else if (Directory.Exists(Path.Combine(appdata, @"Microsoft\Edge\User Data")))
                defaultBrowser = "edge";

            jsonSettings.NullValueHandling = NullValueHandling.Ignore;
        }

        public override SerializableDictionary<string, string> GetDefaultOptions()
        {
            SerializableDictionary<string, string> defaultOptions = new SerializableDictionary<string, string>();

            // 1.Default Browser/Profile 2.Forces also ipv4 (ipv6 causes delays for some reason)
            defaultOptions.Add("ExtraArguments", defaultBrowser == null ? "" : $"-4 --cookies-from-browser {defaultBrowser}");

            return defaultOptions;
        }

        public override void OnInitialized()
        {
            AudioInputs.Clear();
            VideoInputs.Clear();
            SubtitlesInputs.Clear();
            ytdl = null;
            retries = 0;
        }

        public override void OnInitializingSwitch()
        {
            retries = 0;
        }

        public override OpenResults OnOpenVideo(VideoInput input)
        {
            if (input.Plugin == null || input.Plugin.Name != Name) return null;

            Format fmt = (Format) input.Tag;

            bool gotReferer = false;
            Config.Demuxer.FormatOpt["headers"] = "";
            if (fmt.http_headers != null)
                foreach (var hdr in fmt.http_headers)
                {
                    if (hdr.Key.ToLower() == "referer")
                    {
                        gotReferer = true;
                        Config.Demuxer.FormatOpt["referer"] = hdr.Value;
                    }
                    else if (hdr.Key.ToLower() != "user-agent")
                        Config.Demuxer.FormatOpt["headers"] += hdr.Key + ": " + hdr.Value + "\r\n";
                }

            if (!gotReferer)
                Config.Demuxer.FormatOpt["referer"] = Handler.UserInputUrl;

            return new OpenResults();
        }
        public override OpenResults OnOpenAudio(AudioInput input)
        {
            if (input.Plugin == null || input.Plugin.Name != Name) return null;

            Format fmt = (Format) input.Tag;

            var curFormatOpt = decoder.VideoStream == null ? Config.Demuxer.FormatOpt : Config.Demuxer.AudioFormatOpt;

            bool gotReferer = false;
            curFormatOpt["headers"] = "";
            if (fmt.http_headers != null)
                foreach (var hdr in fmt.http_headers)
                {
                    if (hdr.Key.ToLower() == "referer")
                    {
                        gotReferer = true;
                        curFormatOpt["referer"] = hdr.Value;
                    }
                    else if (hdr.Key.ToLower() != "user-agent")
                        curFormatOpt["headers"] += hdr.Key + ": " + hdr.Value + "\r\n";
                }

            if (!gotReferer)
                curFormatOpt["referer"] = Handler.UserInputUrl;

            return new OpenResults();
        }

        public override OpenResults OnOpenSubtitles(SubtitlesInput input)
        {
            if (input.Plugin == null || input.Plugin.Name != Name) return null;

            var curFormatOpt = Config.Demuxer.SubtitlesFormatOpt;
            curFormatOpt["referer"] = Handler.UserInputUrl;

            return new OpenResults();
        }

        public bool IsValidInput(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string scheme = uri.Scheme.ToLower();
                
                if (scheme != "http" && scheme != "https")
                    return false;

                string ext = Utils.GetUrlExtention(uri.AbsolutePath).ToLower();

                if (ext == "m3u8" || ext == "mp3" || ext == "m3u" || ext == "pls")
                    return false;

                // TBR: try to avoid processing radio stations
                if (string.IsNullOrEmpty(uri.PathAndQuery) || uri.PathAndQuery.Length < 5)
                    return false;

            } catch (Exception) { return false; }

            return true;
        }

        public OpenResults Open(Stream iostream)
        {
            return null;
        }

        public OpenResults Open(string url)
        {
            try
            {
                /* TODO playlists
                 * use -P path instead of -o file to extract all info.json for each media in playlist
                 * use global proc and wait until you have only the first one to start playing and let it continue for the rest (abort on dispose/initialize)
                 * review how to expose both playlist and no-playlist wiht multiple inputs (resolution/codecs)
                 */

                Uri uri = new Uri(url);

                string tmpFile = Path.GetTempPath() + Guid.NewGuid().ToString() + ".tmp"; // extension required on some cases (fall back to generic extractor t1.tmp will create 2 json's in case of playlist t1.info.json and t1.tmp.info.json)

                Process proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName        = Path.Combine(Engine.Plugins.Folder, Name, plugin_path),
                        Arguments       = $"{Options["ExtraArguments"]} --no-playlist --no-check-certificate --skip-download --youtube-skip-dash-manifest --write-info-json -o \"{tmpFile}\" \"{url}\"",
                        CreateNoWindow  = true,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WindowStyle     = ProcessWindowStyle.Hidden
                    }
                };

                proc.Start();

                while (!proc.HasExited && !Handler.Interrupt)
                    Thread.Sleep(35);

                if (Handler.Interrupt)
                {
                    Log.Info("Interrupted");
                    if (!proc.HasExited) proc.Kill();
                    return null;
                }

                if (!File.Exists($"{tmpFile}.info.json"))
                {
                    if (Logger.CanDebug)
                    {
                        try { Log.Debug($"[StandardOutput]\r\n{proc.StandardOutput.ReadToEnd()}"); } catch { }
                        try { Log.Debug($"[StandardError] \r\n{proc.StandardError. ReadToEnd()}"); } catch { }
                    }

                    Log.Warn("Couldn't find info json tmp file");

                    if (retries == 0 && !Handler.Interrupt)
                    {
                        retries++;
                        Log.Info("Retry");
                        return Open(url);
                    }

                    return null;
                }

                // Parse Json Object
                string json = File.ReadAllText($"{tmpFile}.info.json");
                ytdl = JsonConvert.DeserializeObject<YoutubeDLJson>(json, jsonSettings);
                if (ytdl == null) return null;

                Format fmt;
                InputData inputData = new InputData()
                {
                    Folder  = Path.GetTempPath(),
                    Title   = ytdl.title
                };

                // If no formats still could have a single format attched to the main root class
                if (ytdl.formats == null)
                {
                    ytdl.formats = new List<Format>();
                    ytdl.formats.Add(ytdl);
                }

                // Fix Nulls (we are not sure if they have audio/video)
                for (int i = 0; i < ytdl.formats.Count; i++)
                {
                    fmt = ytdl.formats[i];
                    if (ytdl.formats[i].vcodec == null) ytdl.formats[i].vcodec = "";
                    if (ytdl.formats[i].acodec == null) ytdl.formats[i].acodec = "";
                    if (ytdl.formats[i].protocol == null) ytdl.formats[i].protocol = "";

                    bool hasAudio = HasAudio(fmt);
                    bool hasVideo = HasVideo(fmt);

                    if (hasVideo)
                    {
                        VideoInputs.Add(new VideoInput()
                        {
                            InputData   = inputData,
                            Tag         = fmt,
                            Url         = fmt.url,
                            UrlFallback = string.IsNullOrEmpty(fmt.manifest_url) ? ytdl.manifest_url : fmt.manifest_url,
                            Protocol    = fmt.protocol,
                            HasAudio    = hasAudio,
                            BitRate     = (long) fmt.vbr,
                            Codec       = fmt.vcodec,
                            Language    = Language.Get(fmt.language),
                            Width       = (int) fmt.width,
                            Height      = (int) fmt.height,
                            FPS         = fmt.fps
                        });
                    }

                    if (hasAudio)
                    {
                        AudioInputs.Add(new AudioInput()
                        {
                            InputData   = inputData,
                            Tag         = fmt,
                            Url         = fmt.url,
                            UrlFallback = string.IsNullOrEmpty(fmt.manifest_url) ? ytdl.manifest_url : fmt.manifest_url,
                            Protocol    = fmt.protocol,
                            HasVideo    = hasVideo,
                            BitRate     = (long) fmt.abr,
                            Codec       = fmt.acodec,
                            Language    = Language.Get(fmt.language)
                        });
                    }
                }

                if (ytdl.automatic_captions != null)
                foreach (var subtitle1 in ytdl.automatic_captions)
                {
                    if (!Config.Subtitles.Languages.Contains(Language.Get(subtitle1.Key))) continue;

                    foreach (var subtitle in subtitle1.Value)
                    {
                        if (subtitle.ext.ToLower() != "vtt") continue;

                        SubtitlesInputs.Add(new SubtitlesInput()
                        { 
                            Downloaded  = true,
                            Converted   = true,
                            Protocol    = subtitle.ext,
                            Language    = Language.Get(subtitle.name),
                            Url         = subtitle.url
                        });
                    }
                    
                }

                if (GetBestMatch() == null && GetAudioOnly() == null)
                {
                    Log.Warn("No streams found");
                    return null;
                }
            }
            catch (Exception e) { Log.Error($"Open ({e.Message})"); return new OpenResults(e.Message); }

            return new OpenResults();
        }
        public VideoInput SuggestVideo()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return null;

            Format fmt = GetBestMatch();
            if (fmt == null) return null;

            foreach(var input in VideoInputs)
                if (fmt.url == input.Url) return input;

            return null;
        }
        public AudioInput SuggestAudio()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return null;

            var fmt = GetAudioOnly();
            if (fmt == null) return null;

            foreach(var input in AudioInputs)
                if (fmt.url == input.Url) return input;

            return null;
        }

        public SubtitlesInput SuggestSubtitles(Language lang)
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return null;

            foreach (var subtitle in SubtitlesInputs)
                if (subtitle.Language == lang) return subtitle;

            return null;
        }

        private Format GetAudioOnly()
        {
            // Prefer best with no video (dont waste bandwidth)
            for (int i = ytdl.formats.Count - 1; i >= 0; i--)
                if (HasAudio(ytdl.formats[i]) && !HasVideo(ytdl.formats[i]))
                    return ytdl.formats[i];

            // Prefer audio from worst video?
            for (int i = 0; i < ytdl.formats.Count; i++)
                if (HasAudio(ytdl.formats[i]))
                    return ytdl.formats[i];

            return null;
        }
        private Format GetBestMatch()
        {
            // TODO: Expose in settings (vCodecs Blacklist) || Create a HW decoding failed list dynamic (check also for whitelist)
            List<string> vCodecsBlacklist = new List<string>() { "vp9" };

            // Video Streams Order based on Screen Resolution
            var iresults =
                from    format in ytdl.formats
                where   HasVideo(format) && format.height <= Config.Video.MaxVerticalResolution && !Regex.IsMatch(format.protocol, "dash", RegexOptions.IgnoreCase)
                orderby format.tbr      descending
                orderby format.fps      descending
                orderby format.height   descending
                orderby format.width    descending
                select  format;
            
            if (iresults == null || iresults.Count() == 0)
            {
                // Fall-back to any
                iresults =
                    from    format in ytdl.formats
                    where   HasVideo(format)
                    orderby format.tbr      descending
                    orderby format.fps      descending
                    orderby format.height   descending
                    orderby format.width    descending
                    select  format;

                if (iresults == null || iresults.Count() == 0) return null;
            }

            List<Format> results = iresults.ToList();

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

            return results[results.Count - 1]; // Fall-back to any
        }
        private static bool IsBlackListed(List<string> blacklist, string codec)
        {
            foreach (string codec2 in blacklist)
                if (Regex.IsMatch(codec, codec2, RegexOptions.IgnoreCase))
                    return true;

            return false;
        }
        private static bool HasVideo(Format fmt)
        {
            if (fmt.height > 0 || fmt.vbr > 0 || fmt.vcodec != "none")
                return true;

            return false; 
        }
        private static bool HasAudio(Format fmt)
        {
            if (fmt.abr > 0 || fmt.acodec != "none")
                return true;

            return false;
        }
    }
}