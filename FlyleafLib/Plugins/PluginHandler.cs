using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using FlyleafLib.MediaFramework.MediaInput;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.Plugins
{
    public class PluginHandler
    {
        public static Dictionary<string, PluginType> PluginTypes        { get; private set; }

        public Config                   Config                          { get ; private set; }
        public int                      UniqueId                        { get; set; }

        public Dictionary<string, PluginBase>   
                                        Plugins                         { get; private set; }
        public Dictionary<string, IOpen> 
                                        PluginsOpen                     { get; private set; }
        public Dictionary<string, IOpenSubtitles>
                                        PluginsOpenSubtitles            { get; private set; }

        public Dictionary<string, IProvideAudio>
                                        PluginsProvideAudio             { get; private set; }
        public Dictionary<string, IProvideVideo>
                                        PluginsProvideVideo             { get; private set; }
        public Dictionary<string, IProvideSubtitles>
                                        PluginsProvideSubtitles         { get; private set; }

        public Dictionary<string, ISuggestAudioInput>         
                                        PluginsSuggestAudioInput        { get; private set; }
        public Dictionary<string, ISuggestVideoInput>         
                                        PluginsSuggestVideoInput        { get; private set; }
        public Dictionary<string, ISuggestSubtitlesInput>     
                                        PluginsSuggestSubtitlesInput    { get; private set; }

        public Dictionary<string, ISuggestAudioStream>        
                                        PluginsSuggestAudioStream       { get; private set; }
        public Dictionary<string, ISuggestVideoStream>        
                                        PluginsSuggestVideoStream       { get; private set; }
        public Dictionary<string, ISuggestSubtitlesStream>    
                                        PluginsSuggestSubtitlesStream   { get; private set; }

        public Dictionary<string, ISearchSubtitles>           
                                        PluginsSearchSubtitles          { get; private set; }
        public Dictionary<string, IDownloadSubtitles>         
                                        PluginsDownloadSubtitles        { get; private set; }

        public string                   UserInputUrl                    { get; set; }
        public IOpen                    OpenedPlugin                    { get; private set; }
        public IOpenSubtitles           OpenedSubtitlesPlugin           { get; private set; }

        public AudioInput               AudioInput                      { get; private set; }
        public VideoInput               VideoInput                      { get; private set; }
        public SubtitlesInput           SubtitlesInput                  { get; private set; }

        public bool                     Interrupt                       { get; set; }

        bool searchedForSubtitles;

        public PluginHandler(Config config, int uniqueId = -1)
        {
            Config = config;
            UniqueId= uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
            LoadPlugins();
        }
        private void LoadPlugins()
        {
            Plugins = new Dictionary<string, PluginBase>();

            foreach (var type in PluginTypes.Values)
            {
                try
                {
                    PluginBase plugin = (PluginBase) Activator.CreateInstance(type.Type);
                    plugin.Handler  = this;
                    plugin.Name     = type.Name;
                    plugin.Type     = type.Type;
                    plugin.Version  = type.Version;

                    Plugins.Add(plugin.Name, plugin);

                    // Add Plugins with Options to Config Plugins
                    if (plugin.Options.Count > 0)
                        Config.Plugins.Add(plugin.Name, plugin.Options);

                } catch (Exception e) { Log($"[Plugins] [Error] Failed to load plugin ... ({e.Message} {Utils.GetRecInnerException(e)}"); }
            }

            PluginsOpen                     = new Dictionary<string, IOpen>();
            PluginsOpenSubtitles            = new Dictionary<string, IOpenSubtitles>();

            PluginsProvideAudio             = new Dictionary<string, IProvideAudio>();
            PluginsProvideVideo             = new Dictionary<string, IProvideVideo>();
            PluginsProvideSubtitles         = new Dictionary<string, IProvideSubtitles>();

            PluginsSuggestAudioInput        = new Dictionary<string, ISuggestAudioInput>();
            PluginsSuggestVideoInput        = new Dictionary<string, ISuggestVideoInput>();
            PluginsSuggestSubtitlesInput    = new Dictionary<string, ISuggestSubtitlesInput>();

            PluginsSuggestAudioStream       = new Dictionary<string, ISuggestAudioStream>();
            PluginsSuggestVideoStream       = new Dictionary<string, ISuggestVideoStream>();
            PluginsSuggestSubtitlesStream   = new Dictionary<string, ISuggestSubtitlesStream>();

            PluginsSearchSubtitles          = new Dictionary<string, ISearchSubtitles>();
            PluginsDownloadSubtitles        = new Dictionary<string, IDownloadSubtitles>();

            foreach (var plugin in Plugins.Values)
                LoadPluginInterfaces(plugin);
        }
        internal static void LoadAssemblies()
        {
            // Load .dll Assemblies
            if (Directory.Exists("Plugins"))
            {
                string[] dirs = Directory.GetDirectories("Plugins");
                foreach(string dir in dirs)
                    foreach(string file in Directory.GetFiles(dir, "*.dll"))
                        try { Assembly.LoadFrom(Path.GetFullPath(file));}
                        catch (Exception e) { Utils.Log($"[PluginHandler] [Error] Failed to load assembly ({e.Message} {Utils.GetRecInnerException(e)})"); }
            }

            // Load PluginBase Types
            PluginTypes = new Dictionary<string, PluginType>();
            Type pluginBaseType = typeof(PluginBase);
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
                try
                {
                    Type[] types = assembly.GetTypes();
                    foreach (var type in types)
                        if (pluginBaseType.IsAssignableFrom(type) && type.IsClass && !type.IsAbstract)
                        {
                            PluginTypes.Add(type.Name, new PluginType() { Name = type.Name, Type = type, Version = assembly.GetName().Version});
                            Utils.Log($"[PluginHandler] Loaded {type.Name} [{assembly.GetName().Version}]");
                        }
                } catch (Exception e) { Utils.Log($"[PluginHandler] Failed to load plugin type ({e.Message})"); }
        }
        private void LoadPluginInterfaces(PluginBase plugin)
        {
            if (plugin is IOpen) PluginsOpen.Add(plugin.Name, (IOpen)plugin);
            else if (plugin is IOpenSubtitles) PluginsOpenSubtitles.Add(plugin.Name, (IOpenSubtitles)plugin);

            if (plugin is IProvideAudio) PluginsProvideAudio.Add(plugin.Name, (IProvideAudio)plugin);
            if (plugin is IProvideVideo) PluginsProvideVideo.Add(plugin.Name, (IProvideVideo)plugin);
            if (plugin is IProvideSubtitles) PluginsProvideSubtitles.Add(plugin.Name, (IProvideSubtitles)plugin);

            if (plugin is ISuggestAudioInput) PluginsSuggestAudioInput.Add(plugin.Name, (ISuggestAudioInput)plugin);
            if (plugin is ISuggestVideoInput) PluginsSuggestVideoInput.Add(plugin.Name, (ISuggestVideoInput)plugin);
            if (plugin is ISuggestSubtitlesInput) PluginsSuggestSubtitlesInput.Add(plugin.Name, (ISuggestSubtitlesInput)plugin);

            if (plugin is ISuggestAudioStream) PluginsSuggestAudioStream.Add(plugin.Name, (ISuggestAudioStream)plugin);
            if (plugin is ISuggestVideoStream) PluginsSuggestVideoStream.Add(plugin.Name, (ISuggestVideoStream)plugin);
            if (plugin is ISuggestSubtitlesStream) PluginsSuggestSubtitlesStream.Add(plugin.Name, (ISuggestSubtitlesStream)plugin);

            if (plugin is ISearchSubtitles) PluginsSearchSubtitles.Add(plugin.Name, (ISearchSubtitles)plugin);
            if (plugin is IDownloadSubtitles) PluginsDownloadSubtitles.Add(plugin.Name, (IDownloadSubtitles)plugin);
        }

        #region Events
        public void OnInitializing()
        {
            foreach(var plugin in Plugins.Values)
                plugin.OnInitializing();
        }
        public void OnInitialized()
        {
            if (VideoInput != null) VideoInput.Enabled = false;
            if (AudioInput != null) AudioInput.Enabled = false;
            if (SubtitlesInput != null) SubtitlesInput.Enabled = false;

            foreach(var plugin in Plugins.Values)
                plugin.OnInitialized();

            UserInputUrl            = "";
            OpenedPlugin            = null;
            OpenedSubtitlesPlugin   = null;
            AudioInput              = null;
            VideoInput              = null;
            SubtitlesInput          = null;
            searchedForSubtitles    = false;
        }

        public void OnInitializingSwitch()
        {
            if (VideoInput != null) VideoInput.Enabled = false;
            foreach(var plugin in Plugins.Values)
                plugin.OnInitializingSwitch();
        }
        public void OnInitializedSwitch()
        {
            foreach(var plugin in Plugins.Values)
                plugin.OnInitializedSwitch();
        }

        public void OnDispose()
        {
            foreach(var plugin in Plugins.Values)
                plugin.Dispose();
        }

        public OpenResults OnOpen(InputBase input)
        {
            if (input == null) return new OpenResults("Invalid input");

            onClose(input is AudioInput ? (InputBase)AudioInput : (input is VideoInput ? (InputBase)VideoInput : (InputBase)SubtitlesInput));

            if (input is AudioInput)
                AudioInput = (AudioInput)input;
            else if (input is VideoInput)
                VideoInput = (VideoInput)input;
            else
            {
                SubtitlesInput = (SubtitlesInput)input;

                if (!SubtitlesInput.Downloaded && !DownloadSubtitles(SubtitlesInput))
                    return new OpenResults("Failed to download subtitles");

                SubtitlesInput.Downloaded = true;
            }    

            foreach(var plugin in Plugins.Values)
            {
                OpenResults res = input is AudioInput ? plugin.OnOpenAudio((AudioInput)input) : 
                                 (input is VideoInput ? plugin.OnOpenVideo((VideoInput)input) : 
                                                        plugin.OnOpenSubtitles((SubtitlesInput)input));

                if (res != null && res.Error != null)
                {
                    onClose(input);
                    return res;
                }
            }

            if (Config.Subtitles.Enabled && Config.Subtitles.UseOnlineDatabases && input is VideoInput 
                && (((IOpen)VideoInput.Plugin).IsPlaylist || !searchedForSubtitles) && !VideoInput.SearchedForSubtitles)
                SearchSubtitles();

            return null;
        }
        public void onClose(InputBase input)
        {
            if (input == null) return;
            input.Enabled = false;
        }

        public void Dispose()
        {
            foreach(var plugin in Plugins.Values)
                plugin.Dispose();
        }
        #endregion

        #region Audio / Video
        public OpenResults Open(Stream iostream)
        {
            var plugins = (from plugin in PluginsOpen.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
            {
                if (Interrupt) return new OpenResults("Cancelled");

                OpenResults res = plugin.Open(iostream);
                if (res == null) continue;
                if (res.Error != null) return res;

                if (plugin is IProvideAudio)
                    foreach(var input in ((IProvideAudio)plugin).AudioInputs)
                        input.Plugin = plugin;

                if (plugin is IProvideVideo)
                    foreach(var input in ((IProvideVideo)plugin).VideoInputs)
                        input.Plugin = plugin;

                if (plugin is IProvideSubtitles)
                    foreach(var input in ((IProvideSubtitles)plugin).SubtitlesInputs)
                        input.Plugin = plugin;

                OpenedPlugin = plugin;
                Log($"Current plugin {plugin.Name}");

                return res;
            }

            return null;
        }
        public OpenResults Open(string url)
        {
            UserInputUrl = url;

            var plugins = (from plugin in PluginsOpen.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
            {
                if (Interrupt) return new OpenResults("Cancelled");

                if (!plugin.IsValidInput(url)) continue;

                OpenResults res = plugin.Open(url);
                if (res == null) continue;
                if (res.Error != null) return res;

                if (plugin is IProvideAudio)
                    foreach(var input in ((IProvideAudio)plugin).AudioInputs)
                        input.Plugin = plugin;

                if (plugin is IProvideVideo)
                    foreach(var input in ((IProvideVideo)plugin).VideoInputs)
                        input.Plugin = plugin;

                if (plugin is IProvideSubtitles)
                    foreach(var input in ((IProvideSubtitles)plugin).SubtitlesInputs)
                        input.Plugin = plugin;

                OpenedPlugin = plugin;
                Log($"Current plugin {plugin.Name}");

                return res;
            }

            return null;
        }

        public VideoInput SuggestVideo()
        {
            var plugins = (from plugin in PluginsSuggestVideoInput.Values orderby plugin.Priority select plugin).ToList();
            foreach (var plugin in plugins)
            {
                if (Interrupt) return null;

                var input = plugin.SuggestVideo();
                if (input != null) { Log($"SuggestVideoInput {(input.InputData.Title != null ? input.InputData.Title : "Null Title")}"); return input; }
            }

            return null;
        }
        public AudioInput SuggestAudio()
        {
            var plugins = (from plugin in PluginsSuggestAudioInput.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
            {
                if (Interrupt) return null;

                var input = plugin.SuggestAudio();
                if (input != null) { Log($"SuggestAudioInput {(input.InputData.Title != null ? input.InputData.Title : "Null Title")}"); return input; }
            }

            return null;
        }

        public VideoStream SuggestVideo(List<VideoStream> streams)
        {
            if (streams == null || streams.Count == 0) return null;

            var plugins = (from plugin in PluginsSuggestVideoStream.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
            {
                if (Interrupt) return null;

                var stream = plugin.SuggestVideo(streams);
                if (stream != null) return stream;
            }

            return null;
        }
        public AudioStream SuggestAudio(List<AudioStream> streams)
        {
            if (streams == null || streams.Count == 0) return null;

            var plugins = (from plugin in PluginsSuggestAudioStream.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
            {
                if (Interrupt) return null;

                var stream = plugin.SuggestAudio(streams);
                if (stream != null) return stream;
            }

            return null;
        }

        public void SuggestAudio(out AudioStream stream, out AudioInput input, List<AudioStream> streams)
        {
            stream = null;
            input = null;

            if (Interrupt) return;

            stream = SuggestAudio(streams);
            if (stream != null) return;

            if (Interrupt) return;

            input = SuggestAudio();
            if (input != null) return;
        }
        #endregion

        #region Subtitles
        public OpenResults OpenSubtitles(string url)
        {
            var plugins = (from plugin in PluginsOpenSubtitles.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
            {
                OpenResults res = plugin.Open(url);
                if (res == null) continue;
                if (res.Error != null) return res;

                if (plugin is IProvideSubtitles)
                    foreach(var input in ((IProvideSubtitles)plugin).SubtitlesInputs)
                        input.Plugin = plugin;

                OpenedSubtitlesPlugin = plugin;

                return res;
            }

            return null;
        }
        public bool DownloadSubtitles(SubtitlesInput input)
        {
            bool res = false;

            var plugins = (from plugin in PluginsDownloadSubtitles.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
                if (res = plugin.Download(input)) break;

            return res;
        }

        public void SearchSubtitles()
        {
            // TODO: Search can be offline as well
            var plugins = (from plugin in PluginsSuggestSubtitlesInput.Values orderby plugin.Priority select plugin).ToList();
            foreach(var lang in Config.Subtitles.Languages)
                foreach(var plugin in plugins)
                    if (!Interrupt && plugin is ISearchSubtitles)
                    {
                        // Remember the inputs that have been already searched?
                        ((ISearchSubtitles)plugin).Search(lang);
                        if (plugin is IProvideSubtitles)
                            foreach(var input in ((IProvideSubtitles)plugin).SubtitlesInputs)input.Plugin = plugin;
                    }

            VideoInput.SearchedForSubtitles = true;
            searchedForSubtitles = true;
        }
        public void SuggestSubtitles(out SubtitlesStream stream, out SubtitlesInput input, List<SubtitlesStream> streams)
        {
            stream = null;
            input = null;

            foreach(var lang in Config.Subtitles.Languages)
            {
                if (Interrupt) return;

                stream = SuggestSubtitles(streams, lang);
                if (stream != null) return;

                if (Interrupt) return;

                input = SuggestSubtitles(lang);
                if (input != null) return;
            }
        }
        public SubtitlesStream SuggestSubtitles(List<SubtitlesStream> streams)
        {
            foreach(var lang in Config.Subtitles.Languages)
            {
                if (Interrupt) return null;

                SubtitlesStream subtitlesStream = SuggestSubtitles(streams, lang);
                if (subtitlesStream != null) return subtitlesStream;
            }

            return null;
        }
        public SubtitlesStream SuggestSubtitles(List<SubtitlesStream> streams, Language lang)
        {
            if (streams == null || streams.Count == 0) return null;

            var plugins = (from plugin in PluginsSuggestSubtitlesStream.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
            {
                if (Interrupt) return null;

                var stream = plugin.SuggestSubtitles(streams, lang);
                if (stream != null) return stream;
            }

            return null;
        }
        public SubtitlesInput SuggestSubtitles(Language lang)
        {
            var plugins = (from plugin in PluginsSuggestSubtitlesInput.Values orderby plugin.Priority select plugin).ToList();
            foreach(var plugin in plugins)
            {
                if (Interrupt) return null;

                var input = plugin.SuggestSubtitles(lang);
                if (input != null) return input;
            }

            return null;
        }
        #endregion

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [PluginHandler] {msg}"); }
    }
}