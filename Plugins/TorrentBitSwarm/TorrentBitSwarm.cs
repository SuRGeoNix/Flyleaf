using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using SuRGeoNix.BitSwarmLib;
using SuRGeoNix.BitSwarmLib.BEP;
using static SuRGeoNix.BitSwarmLib.BitSwarm;

using FlyleafLib.MediaFramework.MediaPlaylist;
using static FlyleafLib.Utils;

namespace FlyleafLib.Plugins;

public class TorrentBitSwarm : PluginBase, IOpen
{
    public new int          Priority            { get; set; } = 2000;

    public bool             Downloaded          => torrent != null && torrent.data.files != null && (torrent.data.files[fileIndex] == null || torrent.data.files[fileIndex].Created);
    public string           FolderComplete      => torrent.file.paths.Count == 1 ? cfg.FolderComplete : torrent.data.folder;
    public string           FileName            { get; private set; }
    public TorrentStream    TorrentStream       { get; private set; }

    BitSwarm        bitSwarm;
    TorrentOptions  cfg = new();
    Torrent         torrent;
    string          errorMsg;
    int             fileIndex;
    int             fileIndexNext;
    bool            downloadNextStarted;
    bool            torrentReceived;
    List<string>    sortedPaths;
    long            openId;
    long            openItemId;
    static Dictionary<string, string>
                    defaultOptions;

    public override void OnLoaded()
    {
        Options.PropertyChanged += Options_PropertyChanged;

        // Force initial update on torrent options from config options
        foreach (var prop in cfg.GetType().GetProperties())
            Options_PropertyChanged(this, new PropertyChangedEventArgs(prop.Name));
    }

    public override Dictionary<string, string> GetDefaultOptions()
    {
        if (defaultOptions != null)
            return defaultOptions;

        defaultOptions = [];

        TorrentOptions cfg = new();
        foreach (var prop in cfg.GetType().GetProperties())
            defaultOptions.Add(prop.Name, prop.GetValue(cfg).ToString());

        return defaultOptions;
    }

    private void Options_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        try
        {
            var prop = cfg.GetType().GetProperty(e.PropertyName);

            if (prop.PropertyType == typeof(bool))
                prop.SetValue(cfg, bool.Parse(Options[e.PropertyName]));
            else if (prop.PropertyType == typeof(int))
                prop.SetValue(cfg, int.Parse(Options[e.PropertyName]));
            else
                prop.SetValue(cfg, Options[e.PropertyName]);
        }
        catch (Exception) { }
    }

    public override void OnInitializing()
        => TorrentStream?.Cancel();
    public override void OnInitialized()
        => Dispose();
    public override void OnInitializingSwitch()
    {
        if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return;

        if (cfg != null) cfg.EnableBuffering = false;
        TorrentStream?.Cancel();
    }

    public override void OnBuffering()
    {
        if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name || TorrentStream == null)
            return;

        TorrentStream.Cancel();
        if (cfg != null)
            cfg.EnableBuffering = true;
    }
    public override void OnBufferingCompleted()
    {
        if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name)
            return;

        if (cfg != null)
            cfg.EnableBuffering = false;
    }

    public override void Dispose()
    {
        if (Disposed)
            return;

        Disposed = true;

        TorrentStream?.Cancel();

        try
        {
            bitSwarm?.Dispose();
            torrent?. Dispose();
            bitSwarm            = null;
            torrent             = null;
            sortedPaths         = null;
            TorrentStream       = null;
            downloadNextStarted = false;
            cfg.EnableBuffering = false;

        } catch(Exception e)
        {
            Log.Error("Dispose - " + e.Message);
        }
    }

    private void OnFinishing(object source, FinishingArgs e)
    {
        Log.Info("Download of " + torrent.file.paths[fileIndexNext == -1 ? fileIndex : fileIndexNext] + " finished");
        Selected.DirectUrl = Path.Combine(FolderComplete, FileName);

        e.Cancel = DownloadNext();
        if (!e.Cancel)
            Log.Info("Stopped");
    }
    private void MetadataReceived(object source, MetadataReceivedArgs e)
    {
        try
        {
            if (openId != Handler.OpenCounter)
                return;

            torrent = e.Torrent;
            Playlist.Title = torrent.file.name;
            Playlist.FolderBase = FolderComplete;

            sortedPaths = GetMoviesSorted(torrent.file.paths);

            foreach (var path in sortedPaths)
            {
                PlaylistItem item = new PlaylistItem();

                var fileIndex = torrent.file.paths.IndexOf(path);

                int index = path.LastIndexOf('\\');
                if (index != -1)
                {
                    item.Title = path.Substring(path.LastIndexOf('\\') + 1);
                    item.Folder = path.Substring(0, path.LastIndexOf('\\'));
                }
                else
                    item.Title = path;

                item.FileSize = torrent.file.lengths[fileIndex];

                if (torrent.data.files[fileIndex] != null && torrent.data.files[fileIndex].Created)
                    item.DirectUrl = Path.Combine(FolderComplete, path);

                AddPlaylistItem(item, path);
            }

            Handler.OnPlaylistCompleted();

        }
        catch (Exception e2)
        {
            Log.Error("MetadataReceived - " + e2.Message);
        }

        torrentReceived = true;
    }
    private bool DownloadNext()
    {
        if (cfg.DownloadNext && !downloadNextStarted && torrent != null && fileIndex > -1 && (torrent.data.files[fileIndex] == null || torrent.data.files[fileIndex].Created))
        {
            downloadNextStarted = true;

            var fileIndex = sortedPaths.IndexOf(torrent.file.paths[this.fileIndex]) + 1;
            if (fileIndex > sortedPaths.Count - 1) return false;

            var fileIndex2 = torrent.file.paths.IndexOf(sortedPaths[fileIndex]);
            if (fileIndex2 == -1 || torrent.data.files[fileIndex2] == null || torrent.data.files[fileIndex2].Created) return false;

            Log.Info("Downloading next " + torrent.file.paths[fileIndex2]);

            bitSwarm.IncludeFiles(new List<string>() { torrent.file.paths[fileIndex2] });

            if (!bitSwarm.isRunning) { Log.Info("Starting"); bitSwarm.Start(); }

            fileIndexNext = fileIndex2;

            return true;
        }

        return false;
    }

    public bool CanOpen()
        => ValidateInput(Playlist.Url) != BitSwarm.InputType.Unkown;

    public OpenResults Open()
    {
        try
        {
            openId = Handler.OpenCounter;
            Disposed                    = false;
            torrentReceived             = false;
            bitSwarm                    = new BitSwarm(cfg);
            bitSwarm.MetadataReceived   += MetadataReceived;
            bitSwarm.OnFinishing        += OnFinishing;
            bitSwarm.StatusChanged      += BitSwarm_StatusChanged;

            Playlist.InputType = InputType.Torrent;

            errorMsg = null;
            bitSwarm.Open(Playlist.Url);
            Log.Info("Starting");
            bitSwarm.Start();

            while (!torrentReceived && errorMsg == null && !Handler.Interrupt && openId == Handler.OpenCounter) { Thread.Sleep(35); }

            if (!string.IsNullOrEmpty(errorMsg)) { Dispose(); return new(errorMsg); }

            if (Handler.Interrupt || openId != Handler.OpenCounter) { Dispose(); return null; }

            if (sortedPaths == null || sortedPaths.Count == 0) { Dispose(); return new("No video files found in torrent"); }

            return new();
        }
        catch(Exception e)
        {
            if (Regex.IsMatch(e.Message, "completed or is invalid"))
            {
                MetadataReceived(this, new MetadataReceivedArgs(bitSwarm.torrent));
                return new();
            }

            Log.Error("Error ... " + e.Message);
            return new(e.Message);
        }
    }

    private void BitSwarm_StatusChanged(object source, StatusChangedArgs e)
    {
        if (e.Status == 2)
            errorMsg = e.ErrorMsg;
    }

    public OpenResults OpenItem()
    {
        FileName    = GetTag(Selected).ToString();
        fileIndex   = torrent.file.paths.IndexOf(FileName);

        if (Downloaded && !File.Exists(Path.Combine(FolderComplete, FileName)))
        {
            var session = bitSwarm.LoadedSessionFile;
            bitSwarm.Pause();
            File.Delete(session);
            bitSwarm.Open(Playlist.Url);
        }

        openItemId = Handler.OpenItemCounter;
        downloadNextStarted     = false;
        bitSwarm.FocusAreInUse  = false;
        fileIndexNext           = -1;

        if (!Downloaded)
        {
            TorrentStream = torrent.GetTorrentStream(FileName);
            Selected.IOStream  = TorrentStream;
            bitSwarm.IncludeFiles([FileName]);
            if (!bitSwarm.isRunning) { Log.Info("Starting"); bitSwarm.Start(); }

            // Prepare for subs
            bool threadDone = false;
            Task.Run(() =>
            {
                if (Handler.Interrupt || openItemId != Handler.OpenItemCounter) { threadDone = true; return; }
                byte[] tmp = new byte[65536];
                TorrentStream.Position = 0;
                TorrentStream.Read(tmp, 0, 65536);
                if (Handler.Interrupt || openItemId != Handler.OpenItemCounter) { threadDone = true; return; }
                TorrentStream.Position = TorrentStream.Length - 65536;
                TorrentStream.Read(tmp, 0, 65536);
                threadDone = true;
            });

            while (!threadDone)
            {
                if (Handler.Interrupt || openItemId != Handler.OpenItemCounter) { TorrentStream?.Cancel(); return null; }
                Thread.Sleep(30);
            }

            TorrentStream.Position = 0;
        }
        else if (File.Exists(Path.Combine(FolderComplete, FileName)))
        {
            Selected.IOStream = null;
            Selected.Url = Path.Combine(FolderComplete, FileName);
            Selected.DirectUrl = Selected.Url;

            if (!DownloadNext()) { Log.Info("Pausing"); bitSwarm.Pause(); }
        }
        else
            return new("File not found");

        return new();
    }

    public class TorrentOptions : Options
    {
        public new TorrentOptions Clone() { return (TorrentOptions) MemberwiseClone(); }

        public bool     DownloadNext    { get; set; } = true;

        public TorrentOptions()
        {
            FolderComplete      = GetUserDownloadPath() != null ? Path.Combine(GetUserDownloadPath(), "Torrents") : Path.Combine(Path.GetTempPath(), "Torrents");
            FolderIncomplete    = GetUserDownloadPath() != null ? Path.Combine(GetUserDownloadPath(), "Torrents", "_incomplete") : Path.Combine(Path.GetTempPath(), "Torrents", "_incomplete");
            FolderTorrents      = FolderIncomplete;

            MaxTotalConnections = 80;
            MaxNewConnections   = 15;
            BlockRequests       = 4;

            PreventTimePeriods  = true;
        }
    }
}
