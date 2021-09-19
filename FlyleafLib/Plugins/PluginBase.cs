using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaContext;

namespace FlyleafLib.Plugins
{
    public abstract class PluginBase : PluginType, IPlugin
    {
        public DecoderContext           decoder         => (DecoderContext) Handler;

        public Config                   Config          => Handler.Config;
        public PluginHandler            Handler         { get; internal set; }
        public bool                     Disposed        { get; protected set; }
        public SerializableDictionary<string, string>
                                        Options         { get; set; } = new SerializableDictionary<string, string>();
        public int                      Priority        { get; set; } = 1000;

        public virtual void OnInitializing() { }
        public virtual void OnInitialized() { }

        public virtual void OnInitializingSwitch() { }
        public virtual void OnInitializedSwitch() { }

        public virtual void OnBuffering() { }
        public virtual void OnBufferingCompleted() { }

        public virtual OpenResults OnOpenAudio(AudioInput input) { return null; }
        public virtual OpenResults OnOpenVideo(VideoInput input) { return null; }
        public virtual OpenResults OnOpenSubtitles(SubtitlesInput input) { return null; }

        public virtual void Dispose() { }

        public void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{Handler.UniqueId}] [Plugin: {Name}] {msg}"); }
    }
    public class PluginType
    {
        public Type                     Type            { get; internal set; }
        public string                   Name            { get; internal set; }
        public Version                  Version         { get; internal set; }
    }
    public class OpenResults
    {
        public string Error { get; private set; }

        public OpenResults() { }
        public OpenResults(string error) { Error = error; }
    }

    public interface IPlugin : IDisposable
    {
        string          Name        { get; }
        Version         Version     { get; }
        PluginHandler   Handler     { get; }
        int             Priority    { get; }

        void OnInitializing();
        void OnInitialized();
        void OnInitializingSwitch();
        void OnInitializedSwitch();

        void OnBuffering();
        void OnBufferingCompleted();

        OpenResults OnOpenAudio(AudioInput input);
        OpenResults OnOpenVideo(VideoInput input);
        OpenResults OnOpenSubtitles(SubtitlesInput input);
    }

    public interface IOpen : IPlugin
    {
        bool IsPlaylist { get; }

        bool IsValidInput(string url);
        OpenResults     Open(string url);
        OpenResults     Open(Stream iostream);
    }
    public interface IOpenSubtitles : IPlugin
    {
        OpenResults     Open(string url);
        OpenResults     Open(Stream iostream);
    }

    public interface IProvideAudio : IPlugin
    {
        List<AudioInput>    AudioInputs     { get; set; }
    }
    public interface IProvideVideo : IPlugin
    {
        List<VideoInput>    VideoInputs     { get; set; }
    }
    public interface IProvideSubtitles : IPlugin
    {
        List<SubtitlesInput>SubtitlesInputs { get; set; }
    }

    public interface ISuggestAudioInput : IPlugin
    {
        AudioInput SuggestAudio();
    }
    public interface ISuggestVideoInput : IPlugin
    {
        VideoInput SuggestVideo();
    }
    public interface ISuggestSubtitlesInput : IPlugin
    {
        SubtitlesInput SuggestSubtitles(Language lang);
    }

    public interface ISuggestAudioStream : IPlugin
    {
        AudioStream SuggestAudio(List<AudioStream> streams);
    }
    public interface ISuggestVideoStream : IPlugin
    {
        VideoStream SuggestVideo(List<VideoStream> streams);
    }
    public interface ISuggestSubtitlesStream : IPlugin
    {
        SubtitlesStream SuggestSubtitles(List<SubtitlesStream> streams, Language lang);
    }

    public interface ISearchSubtitles : IPlugin
    {
        void Search(Language lang);
    }
    public interface IDownloadSubtitles : IPlugin
    {
        bool Download(SubtitlesInput input);
    }
}