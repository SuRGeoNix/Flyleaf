using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
//using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

using static FlyleafLib.Utils;

namespace FlyleafLib.Plugins
{
    public class YoutubeDL : PluginBase, IOpen, ISuggestExternalAudio, ISuggestExternalVideo
    {
        /* TODO
         * 1) Check Audio streams if we need to add also video streams with audio
         * 2) Check Best Audio bitrates/quality (mainly for audio only player)
         * 3) Dispose ytdl and not tag it to every item (use only format if required)
         */
        public new int              Priority        { get; set; } = 1999;

        static string               plugin_path     = "yt-dlp.exe";

        static JsonSerializerOptions
                                    jsonSettings    = new JsonSerializerOptions();
        static string               defaultBrowser;

        FileSystemWatcher watcher;
        string workingDir;
        List<Process> procToKill = new List<Process>();
        Process proc;
        bool addingItem;

        //int retries; TBR

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

            
            jsonSettings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        }

        public override SerializableDictionary<string, string> GetDefaultOptions()
        {
            SerializableDictionary<string, string> defaultOptions = new SerializableDictionary<string, string>();

            // 1.Default Browser/Profile
            // 2.Forces also ipv4 (ipv6 causes delays for some reason)
            defaultOptions.Add("ExtraArguments", defaultBrowser == null ? "" : $"--cookies-from-browser {defaultBrowser}");

            return defaultOptions;
        }

        public override void OnInitialized()
        {
            DisposeInternal(false);
        }        
        public override void Dispose()
        {
            DisposeInternal(true);
        }

        private Format GetAudioOnly(YoutubeDLJson ytdl)
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
        private Format GetBestMatch(YoutubeDLJson ytdl)
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
            double bestWidth = results[0].width;
            double bestHeight = results[0].height;

            // Choose from the best resolution (0. with acodec and not blacklisted 1. not blacklisted 2. any)
            int priority = 0;
            while (priority < 3)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i].width != bestWidth || results[i].height != bestHeight)
                        break;

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

        private void DisposeInternal(bool fullDispose)
        {
            lock (procToKill)
            {
                if (proc != null)
                {
                    if (!proc.HasExited)
                        procToKill.Add(proc);
                    proc = null;
                }

                if (procToKill.Count > 0)
                {
                    if (fullDispose)
                        KillProcesses();
                    else
                        KillProcessesAsync();
                }
            }

            if (Disposed)
                return;

            Disposed = true;

            if (watcher != null)
            {
                Log.Debug($"Disposing watcher");
                watcher.Dispose();
            }

            if (workingDir != null)
            {
                Log.Debug($"Removing folder {workingDir}");
                Directory.Delete(workingDir, true);
            }

            watcher = null;
            workingDir = null;
        }
        private void KillProcessesAsync()
        {
            Task.Run(() => KillProcesses());
        }
        private void KillProcesses()
        {
            // yt-dlp will create two processes so we need to kill also child processes
            // 1. NOT WORKING for .Net 4
            // 2. Causes hang issues on debug mode (VS messes with process tree?)
            while (procToKill.Count > 0)
            {
                try
                {
                    Process proc;
                    lock(procToKill)
                    {
                        proc = procToKill[0];
                        procToKill.RemoveAt(0);
                    }
                    if (proc.HasExited)
                        continue;

                    Log.Debug($"Killing process {proc.Id}");
                    var procId = proc.Id;
                    #if NET5_0_OR_GREATER
                    proc.Kill(true);
                    #else
                    proc.Kill();
                    #endif
                    Log.Debug($"Killed process {procId}");
                } catch (Exception e) { Log.Debug($"Killing process task failed with {e.Message}"); }            
            }
        }
        private void NewPlaylistItem(string path)
        {
            string json = null;

            // File Watcher informs us on rename but the process still accessing the file
            for (int i=0; i<3; i++)
            {
                Thread.Sleep(20);
                try { json = File.ReadAllText(path); } catch { continue; }
                break;
            }
            
            var ytdl = JsonSerializer.Deserialize<YoutubeDLJson>(json, jsonSettings);
            if (ytdl == null)
                return;

            if (ytdl._type == "playlist")
                return;

            PlaylistItem item = new PlaylistItem();
            
            if (Playlist.ExpectingItems == 0)
            {
                if (int.TryParse(ytdl.playlist_count, out int pcount))
                    Playlist.ExpectingItems = pcount;
            }

            if (Playlist.Title == null)
            {
                if (!string.IsNullOrEmpty(ytdl.playlist_title))
                {
                    Playlist.Title = ytdl.playlist_title;
                    Log.Debug($"Playlist Title -> {Playlist.Title}");
                }
                else if (!string.IsNullOrEmpty(ytdl.playlist))
                {
                    Playlist.Title = ytdl.playlist;
                    Log.Debug($"Playlist Title -> {Playlist.Title}");
                }
            }

            item.Title = ytdl.title;
            Log.Debug($"Adding {item.Title}");

            item.DirectUrl = ytdl.webpage_url;

            // If no formats still could have a single format attched to the main root class
            if (ytdl.formats == null)
            {
                ytdl.formats = new List<Format>();
                ytdl.formats.Add(ytdl);
            }

            // Audio / Video Streams
            for (int i=0; i<ytdl.formats.Count; i++)
            {
                Format fmt = ytdl.formats[i];

                if (ytdl.formats[i].vcodec == null)
                    ytdl.formats[i].vcodec = "";

                if (ytdl.formats[i].acodec == null)
                    ytdl.formats[i].acodec = "";

                if (ytdl.formats[i].protocol == null)
                    ytdl.formats[i].protocol = "";

                bool hasAudio = HasAudio(fmt);
                bool hasVideo = HasVideo(fmt);

                if (!hasVideo && !hasAudio)
                    continue;

                ExternalStream extStream;

                if (hasVideo)
                {
                    extStream = new ExternalVideoStream()
                    {
                        Url = fmt.url,
                        UrlFallback = string.IsNullOrEmpty(fmt.manifest_url) ? ytdl.manifest_url : fmt.manifest_url,
                        Protocol = fmt.protocol,
                        HasAudio = hasAudio,
                        BitRate = (long)fmt.vbr,
                        Codec = fmt.vcodec,
                        //Language = Language.Get(fmt.language),
                        Width = (int)fmt.width,
                        Height = (int)fmt.height,
                        FPS = fmt.fps
                    };
                }
                else
                {
                    extStream = new ExternalAudioStream()
                    {
                        Url = fmt.url,
                        UrlFallback = string.IsNullOrEmpty(fmt.manifest_url) ? ytdl.manifest_url : fmt.manifest_url,
                        Protocol = fmt.protocol,
                        HasVideo = hasVideo,
                        BitRate = (long)fmt.abr,
                        Codec = fmt.acodec,
                        Language = Language.Get(fmt.language)
                    };
                }

                AddHeaders(extStream, fmt);
                AddExternalStream(extStream, fmt, item);
            }

            if (GetBestMatch(ytdl) == null && GetAudioOnly(ytdl) == null)
            {
                Log.Warn("No streams found");
                return;
            }

            // Subtitles Streams
            try
            {
                if (ytdl.automatic_captions != null)
                {
                    bool found = false;
                    Language lang;

                    foreach (var subtitle1 in ytdl.automatic_captions)
                    {
                        lang = Language.Get(subtitle1.Key);
                        if (!Config.Subtitles.Languages.Contains(lang))
                            continue;

                        foreach (var subtitle in subtitle1.Value)
                        {
                            if (subtitle.ext.ToLower() != "vtt")
                                continue;

                            if (Language.Get(subtitle1.Key) == Config.Subtitles.Languages[0])
                                found = true;

                            AddExternalStream(new ExternalSubtitlesStream()
                            {
                                Downloaded  = true,
                                Converted   = true,
                                Protocol    = subtitle.ext,
                                Language    = lang,
                                Url         = subtitle.url
                            }, null, item);
                        }
                    }

                    if (!found) // TBR: There are still subtitles converted from one language to another (eg. Spanish from English, es-en)
                    {
                        foreach (var subtitle1 in ytdl.automatic_captions)
                        {
                            lang = subtitle1.Key.IndexOf('-') > 1 ? Language.Get(subtitle1.Key.Substring(0, subtitle1.Key.IndexOf('-'))) : null;
                            if (lang != Config.Subtitles.Languages[0])
                                continue;

                            foreach (var subtitle in subtitle1.Value)
                            {
                                if (subtitle.ext.ToLower() != "vtt")
                                    continue;

                                AddExternalStream(new ExternalSubtitlesStream()
                                {
                                    Downloaded  = true,
                                    Converted   = true,
                                    Protocol    = subtitle.ext,
                                    Language    = lang,
                                    Url         = subtitle.url
                                }, null, item);
                            }
                        }
                    }
                }
            } catch (Exception e) { Log.Warn($"Failed to add subtitles ({e.Message})"); }

            AddPlaylistItem(item, ytdl);
        }
        public void AddHeaders(ExternalStream extStream, Format fmt)
        {
            if (fmt.http_headers != null)
            {
                if (fmt.http_headers.ContainsKey("User-Agent"))
                {
                    extStream.UserAgent = fmt.http_headers["User-Agent"];
                    fmt.http_headers.Remove("User-Agent");
                }

                if (fmt.http_headers.ContainsKey("Referer"))
                {
                    extStream.Referrer = fmt.http_headers["Referer"];
                    fmt.http_headers.Remove("Referer");
                }
                            
                extStream.HTTPHeaders = fmt.http_headers;
            }
        }

        public bool CanOpen()
        {
            try
            {
                if (Playlist.IOStream != null)
                    return false;

                Uri uri = new Uri(Playlist.Url);
                string scheme = uri.Scheme.ToLower();
                
                if (scheme != "http" && scheme != "https")
                    return false;

                string ext = Utils.GetUrlExtention(uri.AbsolutePath);

                if (ext == "m3u8" || ext == "mp3" || ext == "m3u" || ext == "pls")
                    return false;

                // TBR: try to avoid processing radio stations
                if (string.IsNullOrEmpty(uri.PathAndQuery) || uri.PathAndQuery.Length < 5)
                    return false;

            } catch (Exception) { return false; }

            return true;
        }
        public OpenResults Open()
        {
            try
            {
                Disposed = false;
                long sessionId = Handler.OpenCounter;
                Playlist.InputType = InputType.Web;

                workingDir = Path.GetTempPath() + Guid.NewGuid().ToString();

                Log.Debug($"Creating folder {workingDir}");
                Directory.CreateDirectory(workingDir);

                proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName        = Path.Combine(Engine.Plugins.Folder, Name, plugin_path),
                        Arguments       = $"{Options["ExtraArguments"]} --no-check-certificate --skip-download --youtube-skip-dash-manifest --write-info-json -P \"{workingDir}\" \"{Playlist.Url}\" -o \"%(title).220B\"", // 418 max filename length
                        CreateNoWindow  = true,
                        UseShellExecute = false,
                        WindowStyle     = ProcessWindowStyle.Hidden,
                        //RedirectStandardError = true,
                        //RedirectStandardOutput = true
                    }
                };

                watcher = new FileSystemWatcher(workingDir);
                watcher.EnableRaisingEvents = true;
                watcher.Renamed += (o, e) =>
                {
                    try
                    {
                        if (Handler.Interrupt || sessionId != Handler.OpenCounter)
                        {
                            Log.Debug($"[Cancelled] Adding {e.FullPath}");
                            return;
                        }

                        addingItem = true;
                        NewPlaylistItem(e.FullPath);
                    } catch (Exception e2) { Log.Warn($"Renamed Event Error {e2.Message} | {sessionId != Handler.OpenCounter}"); }

                    addingItem = false;
                };
                proc.EnableRaisingEvents = true;
                proc.Start();
                Log.Debug($"Process {proc.Id} started");
                var procId = proc.Id;

                proc.Exited += (o, e) => {
                    Log.Debug($"Process {procId} stopped");

                    while (Playlist.Items.Count < 1 && addingItem && !Handler.Interrupt && sessionId == Handler.OpenCounter)
                        Thread.Sleep(35);

                    if (!Handler.Interrupt && sessionId == Handler.OpenCounter && Playlist.Items.Count > 0)
                        Handler.OnPlaylistCompleted();
                };

                while (Playlist.Items.Count < 1 && (!proc.HasExited || addingItem) && !Handler.Interrupt && sessionId == Handler.OpenCounter)
                    Thread.Sleep(35);

                if (Handler.Interrupt || sessionId != Handler.OpenCounter)
                {
                    Log.Info("Cancelled");
                    //DisposeInternal(false);
                    //KillProcessesAsync(); // Normally after interrupt OnInitialized will be called

                    return null;
                }

                if (Playlist.Items.Count == 0) // Allow fallback to default plugin in case of YT-DLP bug with windows filename (this affects proper direct URLs as well)
                    return null;

                //    if (Logger.CanDebug)
                //    {
                //        try { Log.Debug($"[StandardOutput]\r\n{proc.StandardOutput.ReadToEnd()}"); } catch { }
                //        try { Log.Debug($"[StandardError] \r\n{proc.StandardError. ReadToEnd()}"); } catch { }
                //    }

                //    if (retries == 0 && !Handler.Interrupt)
                //    {
                //        retries++;
                //        Log.Info("Retry");
                //        return Open(url);
                //    }
            }
            catch (Exception e) { Log.Error($"Open ({e.Message})"); return new OpenResults(e.Message); }

            return new OpenResults();
        }
        public OpenResults OpenItem()
        {
            //TBR: should check expiration for Urls and re-download json if required for the specific playlist item
            return new OpenResults();
        }

        public ExternalAudioStream SuggestExternalAudio()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return null;

            var fmt = GetAudioOnly((YoutubeDLJson)GetTag(Selected));
            if (fmt == null) return null;

            foreach (var extStream in Selected.ExternalAudioStreams)
                if (fmt.url == extStream.Url) return extStream;

            return null;
        }
        public ExternalVideoStream SuggestExternalVideo()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return null;

            Format fmt = GetBestMatch((YoutubeDLJson)GetTag(Selected));
            if (fmt == null) return null;

            foreach (var extStream in Selected.ExternalVideoStreams)
                if (fmt.url == extStream.Url) return extStream;

            return null;
        }
    }
}