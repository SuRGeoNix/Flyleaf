﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

using static FlyleafLib.Utils;

namespace FlyleafLib.Plugins;

public abstract class PluginBase : PluginType, IPlugin
{
    public ObservableDictionary<string, string>
                                    Options         => Config?.Plugins[Name];
    public Config                   Config          => Handler.Config;

    public Playlist                 Playlist        => Handler.Playlist;
    public PlaylistItem             Selected        => Handler.Playlist.Selected;

    public DecoderContext           decoder         => (DecoderContext) Handler;

    public PluginHandler            Handler         { get; internal set; }
    public LogHandler               Log             { get; internal set; }
    public bool                     Disposed        { get; protected set;} = true;
    public int                      Priority        { get; set; } = 1000;

    public virtual void OnLoaded() { }
    public virtual void OnInitializing() { }
    public virtual void OnInitialized() { }

    public virtual void OnInitializingSwitch() { }
    public virtual void OnInitializedSwitch() { }

    public virtual void OnBuffering() { }
    public virtual void OnBufferingCompleted() { }

    public virtual void OnOpen() { }
    public virtual void OnOpenExternalAudio() { }
    public virtual void OnOpenExternalVideo() { }
    public virtual void OnOpenExternalSubtitles() { }

    public virtual void Dispose() { }

    public void AddExternalStream(ExternalStream extStream, object tag = null, PlaylistItem item = null)
    {
        item ??= Playlist.Selected;

        if (item != null)
            PlaylistItem.AddExternalStream(extStream, item, Name, tag);
    }

    public void AddPlaylistItem(PlaylistItem item, object tag = null)
        => Playlist.AddItem(item, Name, tag);

    public void AddTag(object tag, PlaylistItem item = null)
    {
        item ??= Playlist.Selected;

        item?.AddTag(tag, Name);
    }

    public object GetTag(ExternalStream extStream)
        => extStream?.GetTag(Name);

    public object GetTag(PlaylistItem item)
        => item?.GetTag(Name);

    public virtual Dictionary<string, string> GetDefaultOptions() => new();
}
public class PluginType
{
    public Type                     Type            { get; internal set; }
    public string                   Name            { get; internal set; }
    public Version                  Version         { get; internal set; }
}
public class OpenResults
{
    public string   Error;
    public bool     Success => Error == null;

    public OpenResults() { }
    public OpenResults(string error) => Error = error;
}

public class OpenSubtitlesResults : OpenResults
{
    public ExternalSubtitlesStream ExternalSubtitlesStream;
    public OpenSubtitlesResults(ExternalSubtitlesStream extStream, string error = null) : base(error) => ExternalSubtitlesStream = extStream;
}

public interface IPlugin : IDisposable
{
    string          Name        { get; }
    Version         Version     { get; }
    PluginHandler   Handler     { get; }
    int             Priority    { get; }

    void OnLoaded();
    void OnInitializing();
    void OnInitialized();
    void OnInitializingSwitch();
    void OnInitializedSwitch();

    void OnBuffering();
    void OnBufferingCompleted();

    void OnOpenExternalAudio();
    void OnOpenExternalVideo();
    void OnOpenExternalSubtitles();
}

public interface IOpen : IPlugin
{
    bool CanOpen();
    OpenResults     Open();
    OpenResults     OpenItem();
}
public interface IOpenSubtitles : IPlugin
{
    OpenSubtitlesResults Open(string url);
    OpenSubtitlesResults Open(Stream iostream);
}

public interface IScrapeItem : IPlugin
{
    void ScrapeItem(PlaylistItem item);
}

public interface ISuggestPlaylistItem : IPlugin
{
    PlaylistItem SuggestItem();
}

public interface ISuggestExternalAudio : IPlugin
{
    ExternalAudioStream SuggestExternalAudio();
}
public interface ISuggestExternalVideo : IPlugin
{
    ExternalVideoStream SuggestExternalVideo();
}

public interface ISuggestAudioStream : IPlugin
{
    AudioStream SuggestAudio(ObservableCollection<AudioStream> streams);
}
public interface ISuggestVideoStream : IPlugin
{
    VideoStream SuggestVideo(ObservableCollection<VideoStream> streams);
}

public interface ISuggestSubtitlesStream : IPlugin
{
    SubtitlesStream SuggestSubtitles(ObservableCollection<SubtitlesStream> streams, List<Language> langs);
}

public interface ISuggestSubtitles : IPlugin
{
    /// <summary>
    /// Suggests from all the available subtitles
    /// </summary>
    /// <param name="stream">Embedded stream</param>
    /// <param name="extStream">External stream</param>
    void SuggestSubtitles(out SubtitlesStream stream, out ExternalSubtitlesStream extStream);
}

public interface ISuggestBestExternalSubtitles : IPlugin
{
    /// <summary>
    /// Suggests only if best match exists (to avoid search local/online)
    /// </summary>
    /// <returns></returns>
    ExternalSubtitlesStream SuggestBestExternalSubtitles();
}

public interface ISearchLocalSubtitles : IPlugin
{
    void SearchLocalSubtitles();
}

public interface ISearchOnlineSubtitles : IPlugin
{
    void SearchOnlineSubtitles();
}

public interface IDownloadSubtitles : IPlugin
{
    bool DownloadSubtitles(ExternalSubtitlesStream extStream);
}
