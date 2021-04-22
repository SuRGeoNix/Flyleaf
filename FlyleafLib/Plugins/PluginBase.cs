﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins.MediaStream;

namespace FlyleafLib.Plugins
{
    public abstract class PluginBase : IDisposable
    {
        public string   PluginName   => GetType().Name;
        public Version  PluginVersion => Assembly.GetExecutingAssembly().GetName().Version;

        public Player   Player;

        public List<AudioStream>    AudioStreams    {get; set; }    = new List<AudioStream>();
        public List<VideoStream>    VideoStreams    {get; set; }    = new List<VideoStream>();
        public List<SubtitleStream> SubtitleStreams {get; set; }    = new List<SubtitleStream>();

        public virtual void OnLoad() { }

        bool disposed = false;
        public virtual void Dispose()
        {
            if (disposed) return;

            Player = null;
            AudioStreams = null;
            VideoStreams = null;
            SubtitleStreams = null;

            disposed = true;
        }

        public virtual void OnInitializing() { }
        public virtual void OnInitialized()
        {
            AudioStreams.Clear();
            VideoStreams.Clear();
            SubtitleStreams.Clear();
        }

        public virtual void OnInitializingSwitch() { }
        public virtual void OnInitializedSwitch() { }

        //public virtual void OnSwitch() { }

        public virtual void OnVideoOpened() { }
        
    }

    public class OpenVideoResults
    {
        public VideoStream stream;
        public bool forceFailure;
        public bool runAsync;

        public OpenVideoResults() { }
        public OpenVideoResults(VideoStream stream) { this.stream = stream; }
    }

    public interface IPluginVideo
    {
        bool IsPlaylist { get; }
        OpenVideoResults OpenVideo();
        VideoStream OpenVideo(VideoStream stream);
    }

    public interface IPluginAudio
    {
        AudioStream OpenAudio();
        AudioStream OpenAudio(AudioStream stream);
    }

    public interface IPluginSubtitles
    {
        void Search(Language lang);
        bool Download(SubtitleStream stream);
        SubtitleStream OpenSubtitles(Language lang);
        SubtitleStream OpenSubtitles(SubtitleStream stream);
    }

    public interface IPluginExternal : IPluginSubtitles, IPluginVideo
    {
        VideoStream OpenVideo(Stream stream);
        SubtitleStream OpenSubtitles(string url);
    }
}