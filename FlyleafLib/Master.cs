using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;

namespace FlyleafLib
{
    /// <summary>
    /// Manages library's static configuration
    /// </summary>
    public static class Master
    {
        static Master()
        {
            Plugins     = new List<Type>();
            Players     = new List<Player>();
            AudioMaster = new AudioMaster();
            LoadPlugins();
        }

        /// <summary>
        /// Manages audio devices, volume & mute
        /// </summary>
        public static AudioMaster   AudioMaster     { get; }

        /// <summary>
        /// Holds player instances
        /// </summary>
        public static List<Player>  Players         { get; }

        /// <summary>
        /// Disables aborts (mainly required during seek) (Testing support for .NET 5)
        /// </summary>
        public static bool          PreventAborts   { get;  set; }

        /// <summary>
        /// Holds loaded plugin types
        /// </summary>
        public static List<Type>    Plugins         { get; }
        
        private static void LoadPlugins()
        {
            // Load .dll Assemblies
            if (Directory.Exists("Plugins"))
            {
                string[] dirs = Directory.GetDirectories("Plugins");
                foreach(string dir in dirs)
                    foreach(string file in Directory.GetFiles(dir, "*.dll"))
                        try { Assembly.LoadFrom(Path.GetFullPath(file));}
                        catch (Exception e) { Log($"[Plugins] [Error] Failed to load assembly ({e.Message} {Utils.GetRecInnerException(e)})"); }
            }

            // Find PluginBase Types | Try Catch in for can crash if older version exists
            var interfaceType = typeof(PluginBase);
            Type[] types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(p => interfaceType.IsAssignableFrom(p) && p.IsClass && p.Name != nameof(PluginBase))
            .ToArray();

            // Load Plugins
            foreach (var type in types)
                { Log($"[PluginLoader] {type.FullName}"); Plugins.Add(type); }

            // Fix Assemblies redirect bindings and binaryFormater
            AppDomain.CurrentDomain.AssemblyResolve += (o, a) =>
            {
                foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if (assembly.GetName().Name == (new AssemblyName(a.Name)).Name && (assembly.GetName().Name == "BitSwarmLib" || assembly.GetName().Name == "System.Buffers"))
                    {
                        Log($"[AssemblyResolver] Found {assembly.FullName}");
                        return assembly;
                    }

                Log($"[AssemblyResolver] for {a.Name} not found");

                return null;
            };
        }
        static JsonSerializerSettings loadjson = new JsonSerializerSettings(); // Pre-load common assemblies

        /// <summary>
        /// Registers FFmpeg libraries (ensure you provide x86 or x64 based on your project)
        /// </summary>
        /// <param name="absolutePath">Provide your custom absolute path or :1 for current or :2 for Libs\(x86 or x64 dynamic)\FFmpeg from current to base</param>
        /// <param name="verbosity">FFmpeg's verbosity (24: Warning, 64: Max offset ...)</param>
        public static void RegisterFFmpeg(string absolutePath = ":1", int verbosity = AV_LOG_WARNING) //AV_LOG_MAX_OFFSET
        {
            if (Utils.IsDesignMode || alreadyRegister) return;
            alreadyRegister = true;
            RootPath        = null;

            if (absolutePath == ":1") 
                RootPath = Environment.CurrentDirectory;
            else if (absolutePath != ":2")
                RootPath = absolutePath;
            else
            {
                var current = Environment.CurrentDirectory;
                var probe   = Path.Combine("Libs", Environment.Is64BitProcess ? "x64" : "x86", "FFmpeg");

                while (current != null)
                {
                    var ffmpegBinaryPath = Path.Combine(current, probe);
                    if (Directory.Exists(ffmpegBinaryPath)) { RootPath = ffmpegBinaryPath; break; }
                    current = Directory.GetParent(current)?.FullName;
                }
            }

            if (RootPath == null) throw new Exception("Failed to register FFmpeg libraries");

            try
            {
                uint ver = avformat_version();
                Log($"[FFmepgLoader] [Version: {ver >> 16}.{ver >> 8 & 255}.{ver & 255}] [Location: {RootPath}]");
                av_log_set_level(verbosity);
                av_log_set_callback(Utils.FFmpeg.ffmpegLogCallback);

            } catch (Exception e) { throw new Exception("Failed to register FFmpeg libraries", e); }
        }
        static bool alreadyRegister = false;

        private static void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [Master] {msg}"); }
    }
}
