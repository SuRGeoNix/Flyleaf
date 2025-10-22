﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;

using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

using static FlyleafLib.Utils;

namespace FlyleafLib.Plugins;

public class YoutubeDL : PluginBase, IOpen, ISuggestExternalAudio, ISuggestExternalVideo
{
    /* TODO
     * 1) Check Audio streams if we need to add also video streams with audio
     * 2) Check Best Audio bitrates/quality (mainly for audio only player)
     * 3) Dispose ytdl and not tag it to every item (use only format if required)
     * 4) Use playlist_index to set the default playlist item
     * 5) Video with audio has the language of the audio (this makes the GetBestMatch video to take count also the audio but we don't know if it is enabled/required)
     * 6) Review supported subtitles formats (allow more/other than vtt?) - when they will work again
     * 
     * Lot of issues with yt-dlp currently:
     * - Cookies can't be exported anymore (?)
     * - Brings playlists when it shouldn't
     * - Double entries for formats (w/o "default" - youtube only?) (clean-up here based on format's main-unique fields / urls?)
     * - Subtitles 429 errors - https://github.com/yt-dlp/yt-dlp/issues/13831
     * - Audio languages are new thing? Creates different streams for those (instead of single with embedded streams?)
     */

    public new int      Priority        { get; set; } = 1999;
    static string       plugin_path     = "yt-dlp.exe";
    static JsonSerializerOptions
                        jsonSettings    = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    FileSystemWatcher   watcher;
    string              workingDir;

    Process             proc;
    int                 procId = -1;
    object              procLocker = new();

    bool                addingItem;
    string              dynamicOptions = "";
    bool                errGenericImpersonate;
    long                sessionId = -1; // same for playlists
    int                 retries;

    public override Dictionary<string, string> GetDefaultOptions()
        => new()
        {
            { "ExtraArguments", "" } // TBR: Restore default functionality with --cookies-from-browser {defaultBrowser} || https://github.com/yt-dlp/yt-dlp/issues/7271
        };

    public override void OnInitializing()
        => DisposeInternal();

    public override void Dispose()
        => DisposeInternal();

    private Format GetAudioOnly(YoutubeDLJson ytdl)
    {
        foreach (var lang in Config.Audio.Languages)
        {
            // Prefer best with no video (dont waste bandwidth)
            for (int i = ytdl.formats.Count - 1; i >= 0; i--)
                if (lang == Language.Get(ytdl.formats[i].language) && HasAudio(ytdl.formats[i]) && !HasVideo(ytdl.formats[i]))
                    return ytdl.formats[i];

            // Prefer audio from worst video?
            for (int i = 0; i < ytdl.formats.Count; i++)
                if (lang == Language.Get(ytdl.formats[i].language) && HasAudio(ytdl.formats[i]))
                    return ytdl.formats[i];
        }

        return null;
    }
    private Format GetBestMatch(YoutubeDLJson ytdl)
    {
        // Video Streams Order based on Screen Resolution
        var iresults =
            from    format in ytdl.formats

            where
                HasVideo(format) &&
                format.height <= Config.Video.MaxVerticalResolution &&
                (!HasAudio(format) || Config.Audio.Languages.Contains(Language.Get(format.language))) && // TBR: Does not respect audio language priority
                (!Regex.IsMatch(format.protocol, "dash", RegexOptions.IgnoreCase) || format.vcodec.Equals("vp9", StringComparison.OrdinalIgnoreCase))

            orderby format.width    descending,
                    format.height   descending,
                    format.protocol descending, // prefer m3u8 over https
                    format.vcodec,              // prefer avc over vp09 (because YT can't seek vp09 at all)
                    format.tbr      descending,
                    format.fps      descending

            select  format;

        if (iresults == null || !iresults.Any())
        {
            // Fall-back to any
            iresults =
                from    format in ytdl.formats
                where   HasVideo(format)
                orderby format.width    descending,
                        format.height   descending,
                        format.protocol descending,
                        format.vcodec,
                        format.tbr      descending,
                        format.fps      descending
                select  format;

            if (iresults == null || !iresults.Any())
                return null;
        }

        List<Format> results = [.. iresults];

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

                if (priority == 0 && results[i].acodec != "none")
                    return results[i];
                else if (priority == 1)
                    return results[i];
                else if (priority == 2)
                    return results[i];
            }

            priority++;
        }

        return results[^1]; // Fall-back to any
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

    private void DisposeInternal()
    {
        lock (procLocker)
        {
            if (Disposed)
                return;

            Log.Debug($"Disposing ({procId})");

            if (procId != -1)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "taskkill",
                    Arguments       = $"/pid {procId} /f /t",
                    CreateNoWindow  = true,
                    UseShellExecute = false,
                    WindowStyle     = ProcessWindowStyle.Hidden,
                }).WaitForExit();
            }

            retries         =  0;
            sessionId       = -1;
            dynamicOptions  = "";
            errGenericImpersonate = false;

            if (watcher != null)
            {
                watcher.Dispose();
                watcher = null;
            }

            if (workingDir != null)
            {
                Log.Debug($"Folder deleted ({workingDir})");
                Directory.Delete(workingDir, true);
                workingDir = null;
            }

            Disposed = true;
            Log.Debug($"Disposed ({procId})");
        }
    }

    private void NewPlaylistItem(string path)
    {
        string json = null;

        // File Watcher informs us on rename but the process still accessing the file
        for (int i=0; i<3; i++)
        {
            Thread.Sleep(20);
            try { json = File.ReadAllText(path); } catch { if (sessionId != Handler.OpenCounter) return; continue; }
            break;
        }

        YoutubeDLJson ytdl = null;

        try
        {
            ytdl = JsonSerializer.Deserialize<YoutubeDLJson>(json, jsonSettings);
        } catch (Exception e)
        {
            Log.Error($"[JsonSerializer] {e.Message}");
        }

        if (sessionId != Handler.OpenCounter) return;

        if (ytdl == null)
            return;

        if (ytdl._type == "playlist")
            return;

        PlaylistItem item = new();

        if (Playlist.ExpectingItems == 0)
            Playlist.ExpectingItems = (int)ytdl.playlist_count;

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
            ytdl.formats = [ytdl];

        // Audio / Video Streams
        for (int i = 0; i < ytdl.formats.Count; i++)
        {
            if (sessionId != Handler.OpenCounter)
                return;

            Format fmt = ytdl.formats[i];

            if (fmt.vcodec == null)
                fmt.vcodec = "";

            if (fmt.acodec == null)
                fmt.acodec = "";

            if (fmt.protocol == null)
                fmt.protocol = "";

            bool hasAudio = HasAudio(fmt);
            bool hasVideo = HasVideo(fmt);

            if (!hasVideo && !hasAudio)
                continue;

            if (Language.Get(fmt.language) == Language.Unknown)
                fmt.language = "en"; // Default to english ?

            if (hasAudio && !Config.Audio.Languages.Contains(Language.Get(fmt.language)))
                continue; // Just trying to reduce the number of streams?

            ExternalStream extStream;

            if (hasVideo)
            {
                extStream = new ExternalVideoStream()
                {
                    Url         = fmt.url,
                    UrlFallback = string.IsNullOrEmpty(fmt.manifest_url) ? ytdl.manifest_url : fmt.manifest_url,
                    Protocol    = fmt.protocol,
                    HasAudio    = hasAudio,
                    BitRate     = (long)fmt.vbr,
                    Codec       = fmt.vcodec,
                    //Language = Language.Get(fmt.language), // TBR: this is audio language
                    Width       = (int)fmt.width,
                    Height      = (int)fmt.height,
                    FPS         = fmt.fps
                };
            }
            else
            {
                extStream = new ExternalAudioStream()
                {
                    Url         = fmt.url,
                    UrlFallback = string.IsNullOrEmpty(fmt.manifest_url) ? ytdl.manifest_url : fmt.manifest_url,
                    Protocol    = fmt.protocol,
                    BitRate     = (long)fmt.abr,
                    Codec       = fmt.acodec,
                    Language    = Language.Get(fmt.language)
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
                    if (sessionId != Handler.OpenCounter)
                        return;

                    lang = Language.Get(subtitle1.Key);
                    if (!Config.Subtitles.Languages.Contains(lang))
                        continue;

                    foreach (var subtitle in subtitle1.Value)
                    {
                        if (!subtitle.ext.Equals("vtt", StringComparison.OrdinalIgnoreCase))
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
                        var minusPos = subtitle1.Key.IndexOf('-');
                        lang = minusPos > 1 ? Language.Get(subtitle1.Key[..minusPos]) : null;
                        if (lang != Config.Subtitles.Languages[0])
                            continue;

                        foreach (var subtitle in subtitle1.Value)
                        {
                            if (sessionId != Handler.OpenCounter)
                                return;

                            if (!subtitle.ext.Equals("vtt", StringComparison.OrdinalIgnoreCase))
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
            if (fmt.http_headers.TryGetValue("User-Agent", out string value))
            {
                extStream.UserAgent = value;
                fmt.http_headers.Remove("User-Agent");
            }

            if (fmt.http_headers.TryGetValue("Referer", out value))
            {
                extStream.Referrer = value;
                fmt.http_headers.Remove("Referer");
            }

            extStream.HTTPHeaders = fmt.http_headers;

            if (!string.IsNullOrEmpty(fmt.cookies))
                extStream.HTTPHeaders.Add("Cookies", fmt.cookies);

        }
    }

    public bool CanOpen()
    {
        try
        {
            if (Playlist.IOStream != null)
                return false;

            Uri uri = new(Playlist.Url);
            string scheme = uri.Scheme.ToLower();

            if (scheme != "http" && scheme != "https")
                return false;

            string ext = GetUrlExtention(uri.AbsolutePath);

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
            lock (procLocker)
            {
                Disposed = false;
                sessionId = Handler.OpenCounter;
                Playlist.InputType = InputType.Web;

                workingDir = Path.GetTempPath() + Guid.NewGuid().ToString();

                Log.Debug($"Folder created ({workingDir})");
                Directory.CreateDirectory(workingDir);
                proc = new Process
                {
                    EnableRaisingEvents = true,

                    StartInfo = new ProcessStartInfo
                    {
                        FileName        = Path.Combine(Engine.Plugins.Folder, Name, plugin_path),
                        // TBR: --extractor-args "youtube:skip=dash" | --youtube-skip-dash-manifest deprecated
                        Arguments       = $"{dynamicOptions}{Options["ExtraArguments"]} --no-check-certificate --skip-download --youtube-skip-dash-manifest --write-info-json -P \"{workingDir}\" \"{Playlist.Url}\" -o \"%(title).220B\"", // 418 max filename length
                        CreateNoWindow  = true,
                        UseShellExecute = false,
                        WindowStyle     = ProcessWindowStyle.Hidden,
                        RedirectStandardError   = true,
                        RedirectStandardOutput  = Logger.CanDebug,
                    }
                };

                proc.Exited += (o, e) =>
                {
                    lock (procLocker)
                    {
                        if (Logger.CanDebug)
                            Log.Debug($"Process completed ({(procId == -1 ? "Killed" : $"{procId}")})");

                        proc.Close();
                        proc    = null;
                        procId  = -1;
                    }
                };

                proc.ErrorDataReceived += (o, e) =>
                {
                    if (sessionId != Handler.OpenCounter || e.Data == null)
                        return;

                    Log.Debug($"[stderr] {e.Data}");

                    if (!errGenericImpersonate && e.Data.Contains("generic:impersonate"))
                        errGenericImpersonate = true;
                };

                if (Logger.CanDebug)
                    proc.OutputDataReceived += (o, e) =>
                    {
                        if (sessionId == Handler.OpenCounter)
                            Log.Debug($"[stdout] {e.Data}");
                    };

                watcher = new()
                {
                    Path = workingDir,
                    EnableRaisingEvents = true,
                };
                watcher.Renamed += (o, e) =>
                {
                    try
                    {
                        if (sessionId != Handler.OpenCounter)
                            return;

                        addingItem = true;

                        NewPlaylistItem(e.FullPath);

                        if (Playlist.Items.Count == 1)
                            Handler.OnPlaylistCompleted();

                    } catch (Exception e2) { Log.Warn($"Renamed Event Error {e2.Message} | {sessionId != Handler.OpenCounter}");
                    } finally { addingItem = false; }
                };

                proc.Start();
                procId = proc.Id;
                Log.Debug($"Process started ({procId})");

                // Don't try to read them at once at the end as the buffers (hardcoded to 4096) can be full and proc will freeze
                proc.BeginErrorReadLine();
                if (Logger.CanDebug)
                    proc.BeginOutputReadLine();
            }

            while (Playlist.Items.Count < 1 && (proc != null || addingItem) && sessionId == Handler.OpenCounter)
                Thread.Sleep(35);

            if (sessionId != Handler.OpenCounter)
            {
                Log.Info("Session cancelled");
                DisposeInternal();
                return null;
            }

            if (Playlist.Items.Count == 0) // Allow fallback to default plugin in case of YT-DLP bug with windows filename (this affects proper direct URLs as well)
            {
                if (!errGenericImpersonate || retries > 0)
                    return null;

                Log.Warn("Re-trying with --extractor-args \"generic:impersonate\"");
                DisposeInternal();
                retries = 1;
                dynamicOptions = "--extractor-args \"generic:impersonate\" ";
                return Open();
            }
        }
        catch (Exception e) { Log.Error($"Open ({e.Message})"); return new(e.Message); }

        return new();
    }

    public OpenResults OpenItem()
        => new();

    public ExternalAudioStream SuggestExternalAudio()
    {
        if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name)
            return null;

        var fmt = GetAudioOnly((YoutubeDLJson)GetTag(Selected));
        if (fmt == null)
            return null;

        foreach (var extStream in Selected.ExternalAudioStreams)
            if (fmt.url == extStream.Url)
                return extStream;

        return null;
    }
    public ExternalVideoStream SuggestExternalVideo()
    {
        if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name)
            return null;

        Format fmt = GetBestMatch((YoutubeDLJson)GetTag(Selected));
        if (fmt == null)
            return null;

        foreach (var extStream in Selected.ExternalVideoStreams)
            if (fmt.url == extStream.Url)
                return extStream;

        return null;
    }
}
